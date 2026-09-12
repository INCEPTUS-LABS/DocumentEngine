using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Coordinates one transient Toolbox placement with the authoritative Editing Session. Notation
/// behavior remains downstream in the registered command factory.
/// </summary>
internal sealed class ToolboxPlacementController
{
    private const string PlacementUnavailable = "TOOLBOX_PLACEMENT_UNAVAILABLE";
    private const string PlacementStale = "TOOLBOX_PLACEMENT_STALE";
    private const string PlacementFactoryFailed = "TOOLBOX_PLACEMENT_FACTORY_FAILED";
    private const string PlacementCandidateFailed = "TOOLBOX_PLACEMENT_CANDIDATE_FAILED";
    private const string PlacementCommandRejected = "TOOLBOX_PLACEMENT_COMMAND_REJECTED";
    private const string PlacementSelectionFailed = "TOOLBOX_PLACEMENT_SELECTION_FAILED";
    private const string PlacementPreviewId = "inceptus:toolbox-placement-candidate";

    private readonly ToolboxPlacementCatalog _catalog;
    private readonly ToolboxSelectionState _selection;
    private readonly IDocumentCreationIdentityProvider _identityProvider;
    private readonly Canvas2DSpatialEditPlannerCatalog _spatialEditPlanners;
    private int _placementInFlight;

    internal ToolboxPlacementController(
        ToolboxPlacementCatalog catalog,
        ToolboxSelectionState selection,
        IDocumentCreationIdentityProvider identityProvider,
        Canvas2DSpatialEditPlannerCatalog? spatialEditPlanners = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(identityProvider);
        _catalog = catalog;
        _selection = selection;
        _identityProvider = identityProvider;
        _spatialEditPlanners = spatialEditPlanners ?? Canvas2DSpatialEditPlannerCatalog.Empty;
    }

    internal ToolboxItemId? ActiveItemId
    {
        get
        {
            var selected = _selection.SelectedItemId;
            return selected is not null && _catalog.TryGetRegistration(selected, out _)
                ? selected
                : null;
        }
    }

    internal bool IsPlacementActive => ActiveItemId is not null;

    internal bool IsPlacementInFlight => Volatile.Read(ref _placementInFlight) != 0;

    internal bool Cancel() =>
        ActiveItemId is { } activeItemId && _selection.Clear(activeItemId);

    internal async ValueTask<ToolboxPlacementControllerResult> UpdatePreviewAtCssPointAsync(
        EditingSession session,
        PointD cssPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var itemId = ActiveItemId;
        if (itemId is null || !_catalog.TryGetRegistration(itemId, out var registration) ||
            registration is null)
        {
            return ToolboxPlacementControllerResult.NotHandled();
        }

        var observed = session.CaptureState();
        if (!TryCreatePlacementRequest(
                observed,
                session,
                itemId,
                cssPoint,
                candidate: null,
                out var request,
                out var presentation,
                out var failure))
        {
            return failure!;
        }

        ToolboxPlacementCandidate? candidate = null;
        if (registration.CandidateProvider is not null)
        {
            try
            {
                candidate = registration.CandidateProvider.ResolveCandidate(request!);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return Failure(
                    PlacementCandidateFailed,
                    "The registered Toolbox placement candidate provider could not resolve a preview.",
                    itemId.Value,
                    exception);
            }
        }

        var feedback = candidate is null
            ? null
            : new EditorFeedbackSnapshot(
                PlacementPreviewId,
                candidate.FeedbackKind,
                presentation?.MapLocalToScene(candidate.PreviewBounds) ?? candidate.PreviewBounds,
                properties: candidate.Properties,
                presentationMode: candidate.FeedbackPresentationMode);
        var updated = CopyEditorState(observed.EditorState, feedback);
        if (observed.EditorState.Equals(updated))
        {
            return ToolboxPlacementControllerResult.HandledWithoutCommit();
        }

        var update = await session.UpdateEditorStateAsync(updated, cancellationToken)
            .ConfigureAwait(false);
        return update.Succeeded
            ? ToolboxPlacementControllerResult.HandledWithoutCommit()
            : ToolboxPlacementControllerResult.HandledWithoutCommit(update.Diagnostics);
    }

    internal static async ValueTask<bool> ClearPreviewAsync(
        EditingSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var observed = session.CaptureState();
        if (!observed.EditorState.TemporaryFeedback.Any(feedback =>
                StringComparer.Ordinal.Equals(feedback.Id, PlacementPreviewId)))
        {
            return false;
        }

        var update = await session.UpdateEditorStateAsync(
                CopyEditorState(observed.EditorState, temporaryFeedback: null),
                cancellationToken)
            .ConfigureAwait(false);
        return update.Succeeded;
    }

    internal async ValueTask<ToolboxPlacementControllerResult> TryPlaceAtCssPointAsync(
        EditingSession session,
        PointD cssPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var itemId = ActiveItemId;
        if (itemId is null || !_catalog.TryGetRegistration(itemId, out var registration) ||
            registration is null)
        {
            return ToolboxPlacementControllerResult.NotHandled();
        }

        if (Interlocked.CompareExchange(ref _placementInFlight, 1, 0) != 0)
        {
            return ToolboxPlacementControllerResult.HandledWithoutCommit();
        }

        try
        {
            var observed = session.CaptureState();
            if (!TryCreatePlacementRequest(
                    observed,
                    session,
                    itemId,
                    cssPoint,
                    candidate: null,
                    out var request,
                    out var presentation,
                    out var requestFailure))
            {
                return requestFailure!;
            }

            ToolboxPlacementCandidate? candidate = null;
            if (registration.CandidateProvider is not null)
            {
                try
                {
                    candidate = registration.CandidateProvider.ResolveCandidate(request!);
                }
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    return Failure(
                        PlacementCandidateFailed,
                        "The registered Toolbox placement candidate provider could not resolve a candidate.",
                        itemId.Value,
                        exception);
                }
            }

            request = new ToolboxPlacementRequest(
                request!.ToolboxItemId,
                request.Document,
                request.ExpectedRevision,
                request.DocumentPoint,
                request.IdentityProvider,
                request.TargetScopeId,
                request.VisibleTargets,
                candidate);

            ToolboxPlacementPlanResult planned;
            try
            {
                planned = registration.CommandFactory.CreatePlan(request);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return Failure(
                    PlacementFactoryFailed,
                    "The registered Toolbox placement factory could not create a Command plan.",
                    itemId.Value,
                    exception);
            }

            if (planned is null)
            {
                return Failure(
                    PlacementFactoryFailed,
                    "The registered Toolbox placement factory returned no Command plan result.",
                    itemId.Value);
            }

            if (!planned.Succeeded || planned.Plan is not { } plan)
            {
                return ToolboxPlacementControllerResult.HandledWithoutCommit(planned.Diagnostics);
            }

            if (presentation is not null)
            {
                Canvas2DSpatialEditPlanResult spatialPlan;
                try
                {
                    spatialPlan = _spatialEditPlanners.Plan(new Canvas2DSpatialEditRequest(
                        request.Document,
                        observed.ActiveScopeId,
                        Canvas2DSpatialEditKind.Creation,
                        plan.Command,
                        plan.CreatedSemanticElementId,
                        presentation));
                }
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    return Failure(PlacementFactoryFailed,
                        "The registered spatial placement policy could not create a Command plan.",
                        itemId.Value, exception);
                }

                if (!spatialPlan.Succeeded)
                {
                    return ToolboxPlacementControllerResult.HandledWithoutCommit(spatialPlan.Diagnostics);
                }

                plan = new ToolboxPlacementPlan(spatialPlan.Command!,
                    plan.CreatedSemanticElementId, plan.CreatedVisualStateId);
            }

            var current = session.CaptureState();
            if (_selection.SelectedItemId != itemId ||
                !IsSameAuthoritativeState(observed, current) ||
                plan.Command.TargetDocumentId != current.DocumentId ||
                plan.Command.ExpectedRevision != current.DocumentRevision ||
                !session.TryCaptureDocumentSnapshot(out var currentDocument) ||
                currentDocument.DocumentId != current.DocumentId ||
                currentDocument.Revision != current.DocumentRevision)
            {
                return Failure(
                    PlacementStale,
                    "The Toolbox placement became stale before its Command could execute.",
                    itemId.Value);
            }

            var commandResult = await session.ExecuteForSceneTargetAsync(
                    plan.Command, observed.CurrentScene!, observed.Generation,
                    targetSceneObjectId: null, presentation, cancellationToken)
                .ConfigureAwait(false);
            if (!commandResult.IsCommitted)
            {
                var diagnostics = commandResult.Diagnostics.IsEmpty
                    ? ImmutableArray.Create(new Diagnostic(
                        PlacementCommandRejected,
                        DiagnosticSeverity.Error,
                        "The Toolbox placement Command was not committed.",
                        itemId.Value))
                    : commandResult.Diagnostics;
                return ToolboxPlacementControllerResult.HandledWithoutCommit(diagnostics);
            }

            // Persistent success owns one-shot completion. Clear only the item that produced this
            // Command so a later user selection cannot be erased by an older callback.
            _selection.Clear(itemId);

            await session.WaitForIdleAsync(CancellationToken.None).ConfigureAwait(false);
            var rebuilt = session.CaptureState();
            if (!IsAuthoritativeReadyState(rebuilt) ||
                rebuilt.DocumentId != observed.DocumentId ||
                rebuilt.ActiveScopeId != observed.ActiveScopeId ||
                rebuilt.DocumentRevision < commandResult.CommittedRevision ||
                !ContainsVisualState(rebuilt, plan.CreatedVisualStateId, presentation?.Id))
            {
                return ToolboxPlacementControllerResult.CommittedWithoutSelection(
                    plan.CreatedVisualStateId,
                    planned.Diagnostics.Add(new Diagnostic(
                        PlacementSelectionFailed,
                        DiagnosticSeverity.Error,
                        "The placed node could not be selected because its authoritative Scene is unavailable.",
                        plan.CreatedVisualStateId.Value)));
            }

            var editorState = rebuilt.EditorState;
            var selected = new EditorStateSnapshot(
                [plan.CreatedVisualStateId],
                editorState.HoveredObjectId,
                editorState.ActiveToolId,
                editorState.FocusTargetId,
                editorState.Viewport,
                editorState.ActiveGesture,
                RemovePlacementFeedback(editorState),
                editorState.ToolState,
                semanticSceneSelection: null);
            var selectionResult = await session.UpdateEditorStateAsync(selected, cancellationToken)
                .ConfigureAwait(false);
            if (!selectionResult.Succeeded)
            {
                var diagnostics = planned.Diagnostics
                    .AddRange(selectionResult.Diagnostics)
                    .Add(new Diagnostic(
                        PlacementSelectionFailed,
                        DiagnosticSeverity.Error,
                        "The placed node was created, but its transient selection could not be installed.",
                        plan.CreatedVisualStateId.Value));
                return ToolboxPlacementControllerResult.CommittedWithoutSelection(
                    plan.CreatedVisualStateId,
                    diagnostics);
            }

            return ToolboxPlacementControllerResult.Committed(
                plan.CreatedVisualStateId,
                planned.Diagnostics.AddRange(commandResult.Diagnostics));
        }
        finally
        {
            Volatile.Write(ref _placementInFlight, 0);
        }
    }

    private static bool IsAuthoritativeReadyState(EditingSessionState state) =>
        !state.IsClosed &&
        state.Status == EditingSessionStatus.Ready &&
        state.CurrentScene is { } scene &&
        scene.DocumentId == state.DocumentId &&
        scene.SourceRevision == state.DocumentRevision &&
        state.ProjectedGraph is not null &&
        state.LayoutResult is not null &&
        state.EditorState.ActiveGesture is null;

    private bool TryCreatePlacementRequest(
        EditingSessionState observed,
        EditingSession session,
        ToolboxItemId itemId,
        PointD cssPoint,
        ToolboxPlacementCandidate? candidate,
        out ToolboxPlacementRequest? request,
        out Canvas2DSpatialRegion? presentation,
        out ToolboxPlacementControllerResult? failure)
    {
        request = null;
        presentation = null;
        failure = null;
        if (!IsAuthoritativeReadyState(observed) ||
            !session.TryCaptureDocumentSnapshot(out var document) ||
            document is null ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision)
        {
            failure = Failure(
                PlacementUnavailable,
                "Toolbox placement requires a Ready Editing Session with current authoritative artifacts.",
                observed.DocumentId.Value);
            return false;
        }

        PointD scenePoint;
        try
        {
            scenePoint = Canvas2DRenderer.ConvertCssToDocument(
                observed.CurrentScene!,
                cssPoint);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            failure = Failure(
                PlacementUnavailable,
                "The Canvas point could not be converted to an authoritative document position.",
                observed.DocumentId.Value,
                exception);
            return false;
        }

        if (!TryResolvePlacementTarget(
                observed.CurrentScene!,
                observed.ActiveScopeId,
                scenePoint,
                out var documentPoint,
                out var targetScopeId,
                out presentation) ||
            !observed.TryGetCurrentProcessInteraction(out var scopeScene) ||
            scopeScene is null || scopeScene.ActiveScopeId != targetScopeId)
        {
            failure = Failure(
                PlacementUnavailable,
                "Toolbox placement requires a visible editable Process presentation at this point.",
                observed.ActiveScopeId.Value);
            return false;
        }

        var geometryById = scopeScene.LayoutResult!.Nodes.ToDictionary(
            static geometry => geometry.ProjectedObjectId);
        var targetRegionId = presentation?.Id;
        var targets = scopeScene.ProjectedGraph!.Nodes
            .Where(node =>
                node.Source.SemanticElementId is not null &&
                node.Source.VisualStateId is not null &&
                observed.CurrentScene!.Items.Any(item => item.IsVisible &&
                    item.Origin.VisualStateId == node.Source.VisualStateId &&
                    item.SpatialRegion?.Id == targetRegionId &&
                    (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0) &&
                geometryById.TryGetValue(node.Id, out var geometry) &&
                !geometry.Bounds.IsEmpty &&
                document.SemanticModel.TryGetElement(
                    node.Source.SemanticElementId,
                    out _))
            .Select(node => new ToolboxPlacementTarget(
                node.Source.SemanticElementId!,
                document.SemanticModel.Elements.Single(
                    element => element.Id == node.Source.SemanticElementId).TypeId,
                node.Source.VisualStateId!,
                node.Id,
                geometryById[node.Id].Bounds))
            .OrderBy(static target => target.VisualStateId.Value, StringComparer.Ordinal)
            .ToArray();
        request = new ToolboxPlacementRequest(
            itemId,
            document,
            observed.DocumentRevision,
            documentPoint,
            _identityProvider,
            targetScopeId,
            targets,
            candidate);
        return true;
    }

    private static EditorStateSnapshot CopyEditorState(
        EditorStateSnapshot source,
        EditorFeedbackSnapshot? temporaryFeedback) =>
        new(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            source.ActiveGesture,
            temporaryFeedback is null
                ? RemovePlacementFeedback(source)
                : RemovePlacementFeedback(source).Append(temporaryFeedback),
            source.ToolState,
            source.SemanticSceneSelection);

    private static IEnumerable<EditorFeedbackSnapshot> RemovePlacementFeedback(
        EditorStateSnapshot source) =>
        source.TemporaryFeedback.Where(feedback =>
            !StringComparer.Ordinal.Equals(feedback.Id, PlacementPreviewId));

    private static bool IsSameAuthoritativeState(
        EditingSessionState expected,
        EditingSessionState current) =>
        IsAuthoritativeReadyState(current) &&
        current.DocumentId == expected.DocumentId &&
        current.DocumentRevision == expected.DocumentRevision &&
        current.ActiveScopeId == expected.ActiveScopeId &&
        current.Generation == expected.Generation &&
        ReferenceEquals(current.CurrentScene, expected.CurrentScene);

    private static bool ContainsVisualState(
        EditingSessionState state,
        VisualStateId visualStateId,
        Canvas2DSpatialRegionId? presentationId) =>
        state.CurrentScene!.Items.Any(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.IsVisible && item.SpatialRegion?.Id == presentationId);

    internal static bool TryResolvePlacementTarget(
        Canvas2DScene scene,
        DocumentScopeId activeScopeId,
        PointD scenePoint,
        out PointD processLocalPoint,
        out DocumentScopeId targetScopeId,
        out Canvas2DSpatialRegion? presentation)
    {
        processLocalPoint = default;
        targetScopeId = activeScopeId;
        presentation = null;
        if (scene.Items.Any(item => item.IsVisible &&
            Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item) &&
            item.Bounds.Contains(scenePoint)))
        {
            return false;
        }

        var containingInstances = (scene.SpatialPresentationPlan?.Regions ?? [])
            .Where(instance => instance.Bounds.Contains(scenePoint))
            .ToArray();
        if (containingInstances.Length == 1)
        {
            presentation = containingInstances[0];
            processLocalPoint = presentation.MapSceneToLocal(scenePoint);
            return true;
        }

        if (containingInstances.Length != 0 ||
            scene.SpatialPresentationPlan is not null)
        {
            return false;
        }

        processLocalPoint = scenePoint;
        return true;
    }

    private static ToolboxPlacementControllerResult Failure(
        string code,
        string message,
        string sourceIdentity,
        Exception? exception = null)
    {
        var metadata = exception is null
            ? null
            : new[]
            {
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name),
            };
        return ToolboxPlacementControllerResult.HandledWithoutCommit(
        [
            new Diagnostic(
                code,
                DiagnosticSeverity.Error,
                message,
                sourceIdentity,
                metadata),
        ]);
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}

internal sealed record ToolboxPlacementControllerResult(
    bool Handled,
    bool IsCommitted,
    VisualStateId? CreatedVisualStateId,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal static ToolboxPlacementControllerResult NotHandled() => new(false, false, null, []);

    internal static ToolboxPlacementControllerResult HandledWithoutCommit(
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, false, null, Copy(diagnostics));

    internal static ToolboxPlacementControllerResult Committed(
        VisualStateId visualStateId,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, true, visualStateId, Copy(diagnostics));

    internal static ToolboxPlacementControllerResult CommittedWithoutSelection(
        VisualStateId visualStateId,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, true, visualStateId, Copy(diagnostics));

    private static ImmutableArray<Diagnostic> Copy(IEnumerable<Diagnostic>? diagnostics) =>
        diagnostics is null ? [] : [.. diagnostics];
}
