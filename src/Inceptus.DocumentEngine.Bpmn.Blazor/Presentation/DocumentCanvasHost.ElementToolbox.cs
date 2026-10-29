using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

public sealed record ElementToolboxState(VisualStateId VisualId, RectD Anchor, PointD ActionsPosition,
    PointD CreationPosition, double CreationWidth, bool CanAutoPoints, bool CanDelete,
    ImmutableArray<ToolboxItemDefinition> Candidates)
{
    public DocumentScopeId? OpenSubProcessScopeId => NavigationRequest?.Action.TargetScopeId;
    public bool IsConnector { get; init; }
    public bool CanProperties { get; init; } = true;
    public double ActionsWidth => MeasureActionsWidth(IsConnector, CanProperties, OpenSubProcessScopeId is not null);
    internal static double MeasureActionsWidth(bool connector, bool properties, bool navigation) =>
        (connector ? 106d : navigation ? 162d : 124d) - (properties ? 0d : connector ? 48d : 38d);
    internal DocumentCanvasElementScopeNavigationRequest? NavigationRequest { get; init; }
}

internal sealed partial class DocumentCanvasHost
{
    private bool _smartEditEnabled = true;
    private (DocumentId Document, DocumentRevision Revision, VisualStateId Visual, DocumentScopeId Scope)? _elementCandidateKey;
    private ImmutableArray<ToolboxItemDefinition> _elementCandidates = [];
    private VisualStateId? _dismissedElementToolbox;
    private ElementToolboxState? _elementToolbox;
    private (long SessionVersion, DocumentId Document, DocumentScopeId Scope, VisualStateId Visual, SemanticElementId Element)? _elementToolboxOwner;
    private bool _elementCanAutoPoints;
    private bool _elementCanDelete;
    internal int ElementCandidateEvaluationCount { get; private set; }

    internal void SetSmartEditEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed || _smartEditEnabled == enabled) return;
            _smartEditEnabled = enabled;
            _connectorToolboxRequest = null;
            _dismissedElementToolbox = null;
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    internal void DismissElementToolbox()
    {
        lock (_sync)
        {
            _dismissedElementToolbox = _session?.CaptureState().EditorState.Selection.FirstOrDefault();
            _connectorToolboxRequest = null;
        }
    }

    private ElementToolboxState? CaptureElementToolbox(EditingSessionState? state)
    {
        if (CaptureConnectorToolbox(state) is { } connector) return _elementToolbox = connector;
        if (!TryCaptureElementToolboxTarget(state, out var document, out var id, out var scope))
            return _elementToolbox = null;
        document.VisualModel.TryGetVisualState(id, out var visual);
        var owner = (_documentSessionVersion, document.DocumentId, scope, id, visual!.SemanticElementId);
        if (_elementToolboxOwner != owner)
        {
            _elementToolboxOwner = owner;
            _elementToolbox = null;
            _elementCandidateKey = null;
        }

        // Rebuilding temporarily has no current Scene. Retain presentation only for
        // the same, still-valid selected element; it is never an action authority.
        if (state!.Status == EditingSessionStatus.Rebuilding) return _elementToolbox;
        if (state is not { Status: EditingSessionStatus.Ready, CurrentScene: { } scene } ||
            !TryResolveVisualPropertiesTarget(state, document, id, null, GetSpatialRegionId(state, id), out var body, out _))
            return _elementToolbox = null;
        var bounds = ElementToolboxDisplayedBounds(scene, state, body!);
        if (bounds is null) return _elementToolbox;
        var key = (document.DocumentId, document.Revision, id, scope);
        if (_elementCandidateKey != key)
        {
            var candidates = BpmnSmartNextCatalog.GetCandidates(document, visual.SemanticElementId, scope);
            if (!_elementCandidates.SequenceEqual(candidates)) _elementCandidates = candidates;
            _elementCandidateKey = key;
            _elementCanAutoPoints = BpmnAutoPointsPlanner.CanOptimize(document, id);
            _elementCanDelete = TryCreateDeletionRequest(document, id, out var request) && request is not null &&
                _deletionCatalog.GetMatchingRegistrations(request).Length == 1;
            ElementCandidateEvaluationCount++;
        }
        var anchor = ElementToolboxScreenBounds(scene, bounds.Value);
        var top = anchor.TopLeft;
        var bottom = new PointD(anchor.Right, anchor.Bottom);
        var surface = _surfaceSize!.Value;
        if (!anchor.Intersects(new RectD(0, 0, surface.CssWidth, surface.CssHeight))) return _elementToolbox = null;
        var width = Math.Min(424, Math.Max(0, surface.CssWidth - 16));
        var center = (top.X + bottom.X) / 2;
        var navigation = TryResolveScopeNavigationAction(_scopeNavigationCatalog, document, scope, id, out var action)
            ? new DocumentCanvasElementScopeNavigationRequest(_documentSessionVersion, document.DocumentId, document.Revision, scope, action!)
            : null;
        var canProperties = CaptureToolboxPropertiesEligibility(document, id);
        var actionsWidth = ElementToolboxState.MeasureActionsWidth(false, canProperties, navigation is not null);
        PointD ClampActions(double x, double y) => new(
            Math.Clamp(x, 8, Math.Max(8, surface.CssWidth - actionsWidth - 8)),
            Math.Clamp(y, 8, Math.Max(8, surface.CssHeight - 56)));
        var actions = ClampActions(center - actionsWidth / 2, top.Y - 54);
        // Prefer above, then beside, then below the selected body. Every candidate is clamped
        // using the actual button count, including at high zoom and narrow viewport edges.
        foreach (var candidate in new[] { actions, ClampActions(bottom.X + 12, top.Y),
                     ClampActions(top.X - actionsWidth - 12, top.Y), ClampActions(center - actionsWidth / 2, bottom.Y + 12) })
        {
            if (new RectD(candidate.X, candidate.Y, actionsWidth, 48).Intersects(anchor)) continue;
            actions = candidate;
            break;
        }
        var panelHeight = 158d;
        var lowerY = bottom.Y + 12;
        if (lowerY + panelHeight > surface.CssHeight - 8) lowerY = top.Y - panelHeight - 68;
        var lower = new PointD(Math.Clamp(center - width / 2, 8, Math.Max(8, surface.CssWidth - width - 8)),
            Math.Clamp(lowerY, 8, Math.Max(8, surface.CssHeight - panelHeight - 8)));
        if (new RectD(lower.X, lower.Y, width, panelHeight).Intersects(new RectD(actions.X, actions.Y, actionsWidth, 48)))
        {
            var belowActions = actions.Y + 60;
            var aboveActions = actions.Y - panelHeight - 12;
            if (belowActions + panelHeight <= surface.CssHeight - 8) lower = new(lower.X, belowActions);
            else if (aboveActions >= 8) lower = new(lower.X, aboveActions);
        }
        if (_elementToolbox is not { } previous || previous.VisualId != id || previous.Anchor != anchor ||
            previous.ActionsPosition != actions || previous.CreationPosition != lower || previous.CreationWidth != width ||
            previous.CanAutoPoints != _elementCanAutoPoints || previous.CanDelete != _elementCanDelete ||
            previous.Candidates != _elementCandidates || previous.NavigationRequest != navigation || previous.CanProperties != canProperties)
            _elementToolbox = new(id, anchor, actions, lower, width, _elementCanAutoPoints, _elementCanDelete, _elementCandidates)
            { NavigationRequest = navigation, CanProperties = canProperties };
        return _elementToolbox;
    }

    private bool CaptureToolboxPropertiesEligibility(DocumentSnapshot document, VisualStateId id) =>
        _propertiesSchemaCatalog is not null &&
        DocumentCanvasPropertySnapshot.TryCreate(document, id, _propertiesSchemaCatalog, out var properties) &&
        properties is { IsPropertiesAvailable: true };

    private bool TryCaptureElementToolboxTarget(EditingSessionState? state,
        out DocumentSnapshot document, out VisualStateId id, out DocumentScopeId scope)
    {
        document = null!;
        id = null!;
        scope = null!;
        if (state?.EditorState.Selection.Length != 1 || state.EditorState.Selection[0] != _dismissedElementToolbox)
            _dismissedElementToolbox = null;
        if (!_smartEditEnabled || _disposed || !_initialized || _propertiesFormOpen || _modelViewPropertiesFormOpen || _contextMenu is not null ||
            state is null || state.IsClosed || state.EditorState.Selection.Length != 1 ||
            state.EditorState.SemanticSceneSelection is not null || _toolboxSelection.SelectedItemId is not null ||
            ConnectionToolState.GetSelection(state.EditorState) is not null ||
            state.PresentationContext is not ProcessScopePresentationContext process ||
            _session is null || !_session.TryCaptureDocumentSnapshot(out var snapshot) || snapshot.Revision != state.DocumentRevision ||
            _surfaceSize is null) return false;
        id = state.EditorState.Selection.Single();
        if (_dismissedElementToolbox == id) return false;
        _dismissedElementToolbox = null;
        if (!snapshot.VisualModel.TryGetVisualState(id, out var visual) ||
            !BpmnSmartNextCatalog.SupportsElementActions(snapshot, visual!.SemanticElementId) ||
            !snapshot.SemanticModel.TryGetScope(visual.SemanticElementId, out var semanticScope) || semanticScope!.Id != process.ScopeId)
            return false;
        document = snapshot;
        scope = process.ScopeId;
        return true;
    }

    private static RectD? ElementToolboxDisplayedBounds(Canvas2DScene scene, EditingSessionState state, Canvas2DSceneItem body)
    {
        if (state.EditorState.ActiveGesture is not { } gesture) return body.Bounds;
        // Follow the Scene's existing preview of this body. Do not calculate a
        // second move/resize preview or interpret browser coordinates here.
        var previews = scene.Items.Where(item => item.IsVisible &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
            item.Origin.VisualStateId == body.Origin.VisualStateId &&
            item.Origin.RelatedSceneObjectIds.Contains(body.Id) &&
            item.Metadata.TryGetValue("inceptus.canvas2d:editor-kind", out var kind) &&
            kind.Kind == PropertyValueKind.Text && kind.TextValue == gesture.Kind).Take(2).ToArray();
        return previews.Length switch { 0 => body.Bounds, 1 => previews[0].Bounds, _ => null };
    }

    private static RectD ElementToolboxScreenBounds(Canvas2DScene scene, RectD bounds)
    {
        var top = scene.ViewportTransform.TransformPoint(bounds.TopLeft);
        var bottom = scene.ViewportTransform.TransformPoint(new(bounds.Right, bounds.Bottom));
        return new(top.X, top.Y, bottom.X - top.X, bottom.Y - top.Y);
    }

    internal ValueTask<HistoryOperationResult?> SmartCreateAsync(ToolboxItemId itemId, CancellationToken cancellationToken = default) =>
        ExecuteElementToolboxAsync(itemId, false, cancellationToken);

    internal ValueTask<HistoryOperationResult?> AutoPointsAsync(CancellationToken cancellationToken = default) =>
        ExecuteElementToolboxAsync(null, false, cancellationToken);

    internal ValueTask<HistoryOperationResult?> DeleteToolboxElementAsync(CancellationToken cancellationToken = default) =>
        ExecuteElementToolboxAsync(null, true, cancellationToken);

    internal async ValueTask<EditingSessionOperationResult?> OpenToolboxSubProcessAsync(
        ElementToolboxState request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { return null; }
        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasScopeNavigationContextAction? currentAction;
            lock (_sync)
            {
                session = _session;
                var state = session?.CaptureState();
                if (request.NavigationRequest is not { } navigation ||
                    navigation.DocumentSessionVersion != _documentSessionVersion ||
                    state is not { Status: EditingSessionStatus.Ready, CurrentScene: not null } ||
                    state.EditorState.ActiveGesture is not null ||
                    !TryCaptureElementToolboxTarget(state, out var document, out var id, out var scope) ||
                    navigation.DocumentId != document.DocumentId || navigation.Revision != document.Revision ||
                    navigation.SourceScopeId != scope || request.VisualId != id || navigation.Action.VisualStateId != id ||
                    !TryResolveVisualPropertiesTarget(state, document, id, null, GetSpatialRegionId(state, id), out var body, out _) ||
                    !ElementToolboxScreenBounds(state.CurrentScene, body!.Bounds)
                        .Intersects(new RectD(0, 0, _surfaceSize!.Value.CssWidth, _surfaceSize.Value.CssHeight)) ||
                    !TryResolveScopeNavigationAction(_scopeNavigationCatalog, document, scope, id, out currentAction) ||
                    currentAction != navigation.Action) return null;
            }
            var result = await NavigateToScopeUnderGateAsync(session!, currentAction!.TargetScopeId,
                changed => notify |= changed, linked.Token).ConfigureAwait(false);
            notify |= result?.Succeeded == true;
            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify) await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<HistoryOperationResult?> ExecuteElementToolboxAsync(ToolboxItemId? itemId, bool delete, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EditingSession? session;
            EditingSessionState? state;
            DocumentSnapshot document;
            VisualStateId id;
            var connector = false;
            lock (_sync)
            {
                session = _session;
                state = session?.CaptureState();
                if (state is not { Status: EditingSessionStatus.Ready, CurrentScene: not null } ||
                    state.EditorState.ActiveGesture is not null ||
                    !(connector = TryCaptureConnectorToolboxTarget(state, out document, out id, out _)) &&
                    !TryCaptureElementToolboxTarget(state, out document, out id, out _)) return null;
                if (connector && (!delete || itemId is not null)) return null;
            }
            if (session is null || !TryResolveVisualPropertiesTarget(state, document, id, null, GetSpatialRegionId(state, id),
                out var body, out _)) return null;
            if (_surfaceSize is not { } surface) return null;
            var bounds = ElementToolboxScreenBounds(state.CurrentScene!, body!.Bounds);
            // Connector paths can have zero width or height and still cross the viewport.
            var visible = connector
                ? bounds.Left < surface.CssWidth && bounds.Right > 0 && bounds.Top < surface.CssHeight && bounds.Bottom > 0
                : bounds.Intersects(new RectD(0, 0, surface.CssWidth, surface.CssHeight));
            if (!visible) return null;
            ICommand command;
            if (itemId is not null)
            {
                var configuration = _nativeDocumentSessionConfiguration!;
                var grid = session.GridSettings;
                command = await new BpmnSmartCreatePlanner(configuration.CommandHandlers, configuration.CommandValidators)
                    .PlanAsync(document, id, itemId, _documentCreationIdentityProvider, grid, linked.Token).ConfigureAwait(false);
            }
            else if (delete)
            {
                if (!TryCreateDeletionRequest(document, id, out var request) || request is null) return null;
                var registrations = _deletionCatalog.GetMatchingRegistrations(request);
                if (registrations.Length != 1) return null;
                var plan = registrations[0].CommandFactory.CreatePlan(request);
                if (!plan.Succeeded || plan.Plan is null)
                {
                    lock (_sync) ReplaceInteractionDiagnosticsUnderLock(plan.Diagnostics);
                    return null;
                }
                command = plan.Plan.Command;
            }
            else
            {
                if (!BpmnAutoPointsPlanner.CanOptimize(document, id)) return null;
                command = new BpmnAutoPointsCommand(document, id);
            }
            var result = await session.ExecuteForSceneTargetAsync(command, state.CurrentScene!, state.Generation,
                body!.Id, body.SpatialRegion, linked.Token).ConfigureAwait(false);
            RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
                if (command is BpmnSmartCreateCommand create)
                {
                    var editor = session.CaptureState().EditorState;
                    await session.UpdateEditorStateAsync(new EditorStateSnapshot([create.CreatedVisualStateId], null,
                        editor.ActiveToolId, editor.FocusTargetId, editor.Viewport, null, editor.TemporaryFeedback, editor.ToolState), linked.Token).ConfigureAwait(false);
                }
            }
            return result;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            lock (_sync) ReplaceInteractionDiagnosticsUnderLock([new Diagnostic("BPMN_ELEMENT_TOOLBOX_FAILED", DiagnosticSeverity.Error,
                exception.Message)]);
            return null;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }
}
