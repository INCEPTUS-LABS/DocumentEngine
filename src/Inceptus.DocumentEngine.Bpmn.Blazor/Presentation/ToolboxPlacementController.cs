using System.Collections.Immutable;
using System.Diagnostics;
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
    private const string PlacementBlocked = "TOOLBOX_PLACEMENT_BLOCKED";
    private const string PlacementOutsideRegion = "TOOLBOX_PLACEMENT_OUTSIDE_REGION";
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

    internal ToolboxPlacementEvaluationMetrics? LastEvaluationMetrics { get; private set; }

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
        if (!TryEvaluate(observed, session, itemId, registration, cssPoint,
                out var evaluation, out var failure))
        {
            await ClearPreviewAsync(session, cancellationToken).ConfigureAwait(false);
            return failure!;
        }

        return await PresentEvaluationAsync(session, observed, evaluation!, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<ToolboxPlacementControllerResult> PresentEvaluationAsync(
        EditingSession session,
        EditingSessionState observed,
        PlacementEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (!IsSameAuthoritativeState(observed, session.CaptureState()))
        {
            return Failure(PlacementStale,
                "The Toolbox preview became stale before it could be presented.",
                evaluation.Request.ToolboxItemId.Value);
        }

        var updated = CopyEditorState(observed.EditorState, evaluation.Feedback);
        if (observed.EditorState.Equals(updated))
        {
            return ToolboxPlacementControllerResult.HandledWithoutCommit(evaluation.Diagnostics);
        }

        var update = await session.UpdateEditorStateAsync(updated, cancellationToken)
            .ConfigureAwait(false);
        return update.Succeeded
            ? ToolboxPlacementControllerResult.HandledWithoutCommit(evaluation.Diagnostics)
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
            // The final click owns a fresh evaluation. A previously green feedback item is
            // presentation only and is never permission to execute a persistent command.
            if (!TryEvaluate(observed, session, itemId, registration, cssPoint,
                    out var evaluation, out var requestFailure))
            {
                await ClearPreviewAsync(session, cancellationToken).ConfigureAwait(false);
                return requestFailure!;
            }

            if (!evaluation!.IsAllowed)
            {
                return await PresentEvaluationAsync(session, observed, evaluation, cancellationToken)
                    .ConfigureAwait(false);
            }

            var request = evaluation.Request;
            var presentation = evaluation.Region;
            if (_selection.SelectedItemId != itemId ||
                !IsSameAuthoritativeState(observed, session.CaptureState()))
            {
                return Failure(PlacementStale,
                    "The Toolbox placement became stale before command planning.", itemId.Value);
            }

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

    private bool TryEvaluate(
        EditingSessionState observed,
        EditingSession session,
        ToolboxItemId itemId,
        ToolboxPlacementRegistration registration,
        PointD cssPoint,
        out PlacementEvaluation? evaluation,
        out ToolboxPlacementControllerResult? failure)
    {
        var evaluationStarted = Stopwatch.GetTimestamp();
        var allocationStarted = GC.GetAllocatedBytesForCurrentThread();
        LastEvaluationMetrics = null;
        evaluation = null;
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

        var destinationAllowed = TryResolvePlacementTarget(
                observed.CurrentScene!,
                observed.ActiveScopeId,
                scenePoint,
                out var documentPoint,
                out var targetScopeId,
                out var presentation);
        if (!observed.TryGetCurrentProcessInteraction(out var scopeScene) ||
            scopeScene is null || scopeScene.ActiveScopeId != targetScopeId)
        {
            failure = Failure(
                PlacementUnavailable,
                "Toolbox placement requires a visible editable Process presentation at this point.",
                observed.ActiveScopeId.Value);
            return false;
        }

        if (!destinationAllowed && registration.PreviewProvider is null)
        {
            failure = Failure(PlacementUnavailable,
                "Toolbox placement requires a visible editable Process presentation at this point.",
                observed.ActiveScopeId.Value);
            return false;
        }

        // Outside a destination, preview geometry remains in Scene coordinates and follows the
        // pointer. It cannot become an attachment target or an executable canonical proposal.
        if (!destinationAllowed)
        {
            documentPoint = scenePoint;
            presentation = null;
        }

        var nodeBodyIds = scopeScene.ProjectedGraph!.Nodes.Select(static node =>
            Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node")).ToHashSet();
        var bodies = observed.CurrentScene!.Items.Where(item =>
            item.IsVisible && !item.Bounds.IsEmpty &&
            item.Origin.ProjectedObjectId is not null && nodeBodyIds.Contains(item.Id) &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0).ToArray();
        var targets = new List<ToolboxPlacementTarget>();
        if (destinationAllowed)
        {
            foreach (var body in bodies)
            {
                if (body.SpatialRegion?.Id == presentation?.Id &&
                    body.Origin.SemanticElementId is { } semanticId &&
                    body.Origin.VisualStateId is { } visualId &&
                    document.SemanticModel.TryGetElement(semanticId, out var semantic) &&
                    semantic is not null)
                {
                    targets.Add(new ToolboxPlacementTarget(semanticId, semantic.TypeId,
                        visualId, body.Origin.ProjectedObjectId!,
                        presentation?.MapSceneToLocal(body.Bounds) ?? body.Bounds));
                }
            }
        }

        var request = new ToolboxPlacementRequest(
            itemId,
            document,
            observed.DocumentRevision,
            documentPoint,
            _identityProvider,
            targetScopeId,
            targets);
        try
        {
            ToolboxPlacementPreview? preview = null;
            ToolboxPlacementCandidate? candidate;
            EditorFeedbackSnapshot? feedback;
            if (registration.PreviewProvider is { } provider)
            {
                var providerStarted = Stopwatch.GetTimestamp();
                preview = provider.Evaluate(new ToolboxPlacementPreviewRequest(itemId, document,
                    documentPoint, targetScopeId, targets));
                var providerElapsed = Stopwatch.GetElapsedTime(providerStarted);
                if (preview is null || preview.ToolboxItemId != itemId)
                {
                    failure = Failure(PlacementCandidateFailed,
                        "The registered Toolbox preview provider returned an invalid proposal.", itemId.Value);
                    return false;
                }

                candidate = preview.AttachmentCandidate;
                var displayedBounds = presentation?.MapLocalToScene(preview.Bounds) ?? preview.Bounds;
                var diagnostics = preview.Diagnostics.ToBuilder();
                if (!destinationAllowed)
                {
                    diagnostics.Add(Rejection(PlacementOutsideRegion,
                        "Choose an editable destination region for the complete element.", itemId));
                }
                else if (!DocumentGeometryBoundary.Contains(preview.Bounds))
                {
                    if (!diagnostics.Any(static diagnostic => diagnostic.Code == "CMD_VISUAL_STATE_GEOMETRY_INVALID"))
                    {
                        diagnostics.Add(Rejection("CMD_VISUAL_STATE_GEOMETRY_INVALID",
                            "The complete element must remain inside the Document boundary.", itemId));
                    }
                }
                else if ((presentation is not null && !presentation.Bounds.Contains(displayedBounds)) ||
                    observed.CurrentScene.Items.Any(item => item.IsVisible &&
                        Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item) &&
                        item.Bounds.Intersects(displayedBounds)))
                {
                    diagnostics.Add(Rejection(PlacementOutsideRegion,
                        "The complete element must fit inside the editable destination region.", itemId));
                }

                // Only canonical body items obstruct placement. Labels, connectors, profile
                // decoration and transient feedback never enter this set. Bounds.Intersects
                // rejects positive-area overlap and permits exact boundary contact.
                var collisionStarted = Stopwatch.GetTimestamp();
                var bodiesConsidered = 0;
                var hasCollision = false;
                foreach (var body in bodies)
                {
                    bodiesConsidered++;
                    if ((candidate is null || body.Origin.VisualStateId != candidate.Target.VisualStateId) &&
                        body.Bounds.Intersects(displayedBounds))
                    {
                        hasCollision = true;
                        break;
                    }
                }
                var collisionElapsed = Stopwatch.GetElapsedTime(collisionStarted);
                if (hasCollision)
                {
                    diagnostics.Add(Rejection(PlacementBlocked,
                        "The element overlaps an existing node body.", itemId));
                }

                preview = new ToolboxPlacementPreview(preview.ToolboxItemId, preview.SemanticTypeId,
                    preview.Bounds, preview.Hotspot, preview.Label, preview.LabelPlacement,
                    candidate, preview.IsAllowed && diagnostics.Count == preview.Diagnostics.Length,
                    diagnostics);
                feedback = new EditorFeedbackSnapshot(PlacementPreviewId, displayedBounds, preview);
                LastEvaluationMetrics = new ToolboxPlacementEvaluationMetrics(
                    bodies.Length, bodiesConsidered, providerElapsed, collisionElapsed,
                    Stopwatch.GetElapsedTime(evaluationStarted),
                    GC.GetAllocatedBytesForCurrentThread() - allocationStarted);
            }
            else
            {
                // Registered tools without the optional preview capability retain their
                // original candidate/factory behavior and never show an invented green body.
                candidate = registration.CandidateProvider?.ResolveCandidate(request);
                feedback = candidate is null ? null : new EditorFeedbackSnapshot(
                    PlacementPreviewId, candidate.FeedbackKind,
                    presentation?.MapLocalToScene(candidate.PreviewBounds) ?? candidate.PreviewBounds,
                    properties: candidate.Properties,
                    presentationMode: candidate.FeedbackPresentationMode);
            }

            request = new ToolboxPlacementRequest(itemId, document, observed.DocumentRevision,
                documentPoint, _identityProvider, targetScopeId, targets, candidate);
            evaluation = new PlacementEvaluation(request, presentation, feedback,
                preview?.IsAllowed ?? true, preview?.Diagnostics ?? []);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            failure = Failure(PlacementCandidateFailed,
                "The registered Toolbox placement provider could not evaluate a proposal.",
                itemId.Value, exception);
            return false;
        }

        return true;
    }

    private static Diagnostic Rejection(string code, string message, ToolboxItemId itemId) =>
        new(code, DiagnosticSeverity.Error, message, itemId.Value);

    private sealed record PlacementEvaluation(
        ToolboxPlacementRequest Request,
        Canvas2DSpatialRegion? Region,
        EditorFeedbackSnapshot? Feedback,
        bool IsAllowed,
        ImmutableArray<Diagnostic> Diagnostics);

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

internal sealed record ToolboxPlacementEvaluationMetrics(
    int ApplicableBodyCount,
    int CollisionBodiesConsidered,
    TimeSpan ProviderElapsed,
    TimeSpan CollisionElapsed,
    TimeSpan EvaluationElapsed,
    long AllocatedBytes);

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
