using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

public sealed record ElementToolboxState(VisualStateId VisualId, RectD Anchor, PointD ActionsPosition,
    PointD CreationPosition, double CreationWidth, bool CanAutoPoints, bool CanDelete,
    ImmutableArray<ToolboxItemDefinition> Candidates);

internal sealed partial class DocumentCanvasHost
{
    private bool _smartEditEnabled = true;
    private (DocumentId Document, DocumentRevision Revision, VisualStateId Visual, DocumentScopeId Scope)? _elementCandidateKey;
    private ImmutableArray<ToolboxItemDefinition> _elementCandidates = [];
    private VisualStateId? _dismissedElementToolbox;
    internal int ElementCandidateEvaluationCount { get; private set; }

    internal void SetSmartEditEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed || _smartEditEnabled == enabled) return;
            _smartEditEnabled = enabled;
            _dismissedElementToolbox = null;
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    internal void DismissElementToolbox()
    {
        lock (_sync) _dismissedElementToolbox = _session?.CaptureState().EditorState.Selection.FirstOrDefault();
    }

    private ElementToolboxState? CaptureElementToolbox(EditingSessionState? state)
    {
        if (state?.EditorState.Selection.Length != 1 || state.EditorState.Selection[0] != _dismissedElementToolbox)
            _dismissedElementToolbox = null;
        if (!_smartEditEnabled || _disposed || !_initialized || _propertiesFormOpen || _modelViewPropertiesFormOpen || _contextMenu is not null ||
            state is not { Status: EditingSessionStatus.Ready, CurrentScene: { } scene } ||
            state.EditorState.ActiveGesture is not null || state.EditorState.Selection.Length != 1 ||
            state.EditorState.SemanticSceneSelection is not null || _toolboxSelection.SelectedItemId is not null ||
            ConnectionToolState.GetSelection(state.EditorState) is not null ||
            state.PresentationContext is not ProcessScopePresentationContext process ||
            _session is null || !_session.TryCaptureDocumentSnapshot(out var document) || document.Revision != state.DocumentRevision ||
            _surfaceSize is not { } surface) return null;
        var id = state.EditorState.Selection.Single();
        if (_dismissedElementToolbox == id) return null;
        _dismissedElementToolbox = null;
        if (!document.VisualModel.TryGetVisualState(id, out var visual) ||
            !BpmnSmartNextCatalog.SupportsElementActions(document, visual!.SemanticElementId) ||
            !TryResolveVisualPropertiesTarget(state, document, id, null, GetSpatialRegionId(state, id), out var body, out _)) return null;
        var key = (document.DocumentId, document.Revision, id, process.ScopeId);
        if (_elementCandidateKey != key)
        {
            _elementCandidates = BpmnSmartNextCatalog.GetCandidates(document, visual.SemanticElementId, process.ScopeId);
            _elementCandidateKey = key;
            ElementCandidateEvaluationCount++;
        }
        var top = scene.ViewportTransform.TransformPoint(body!.Bounds.TopLeft);
        var bottom = scene.ViewportTransform.TransformPoint(new(body.Bounds.Right, body.Bounds.Bottom));
        var anchor = new RectD(top.X, top.Y, bottom.X - top.X, bottom.Y - top.Y);
        if (!anchor.Intersects(new RectD(0, 0, surface.CssWidth, surface.CssHeight))) return null;
        var width = Math.Min(424, Math.Max(0, surface.CssWidth - 16));
        var center = (top.X + bottom.X) / 2;
        var actions = new PointD(Math.Clamp(center - 62, 8, Math.Max(8, surface.CssWidth - 132)),
            Math.Clamp(top.Y - 54, 8, Math.Max(8, surface.CssHeight - 54)));
        // At the top edge there may be no room above the node. Keep its body visible by moving the toolbar beside it.
        if (new RectD(actions.X, actions.Y, 124, 48).Intersects(anchor))
            actions = new PointD(Math.Clamp(bottom.X + 12, 8, Math.Max(8, surface.CssWidth - 132)), actions.Y);
        var panelHeight = 158d;
        var lowerY = bottom.Y + 12;
        if (lowerY + panelHeight > surface.CssHeight - 8) lowerY = top.Y - panelHeight - 68;
        var lower = new PointD(Math.Clamp(center - width / 2, 8, Math.Max(8, surface.CssWidth - width - 8)),
            Math.Clamp(lowerY, 8, Math.Max(8, surface.CssHeight - panelHeight - 8)));
        var canDelete = TryCreateDeletionRequest(document, id, out var request) && request is not null &&
            _deletionCatalog.GetMatchingRegistrations(request).Length == 1;
        return new(id, anchor, actions, lower, width, BpmnAutoPointsPlanner.CanOptimize(document, id), canDelete, _elementCandidates);
    }

    internal ValueTask<HistoryOperationResult?> SmartCreateAsync(ToolboxItemId itemId, CancellationToken cancellationToken = default) =>
        ExecuteElementToolboxAsync(itemId, false, cancellationToken);

    internal ValueTask<HistoryOperationResult?> AutoPointsAsync(CancellationToken cancellationToken = default) =>
        ExecuteElementToolboxAsync(null, false, cancellationToken);

    internal ValueTask<HistoryOperationResult?> DeleteToolboxElementAsync(CancellationToken cancellationToken = default) =>
        ExecuteElementToolboxAsync(null, true, cancellationToken);

    private async ValueTask<HistoryOperationResult?> ExecuteElementToolboxAsync(ToolboxItemId? itemId, bool delete, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EditingSession? session;
            ElementToolboxState? overlay;
            lock (_sync) { session = _session; overlay = CaptureElementToolbox(session?.CaptureState()); }
            if (session is null || overlay is null || !session.TryCaptureDocumentSnapshot(out var document)) return null;
            var state = session.CaptureState();
            if (!TryResolveVisualPropertiesTarget(state, document, overlay.VisualId, null, GetSpatialRegionId(state, overlay.VisualId),
                out var body, out _)) return null;
            ICommand command;
            if (itemId is not null)
            {
                var configuration = _nativeDocumentSessionConfiguration!;
                command = await new BpmnSmartCreatePlanner(configuration.CommandHandlers, configuration.CommandValidators)
                    .PlanAsync(document, overlay.VisualId, itemId, _documentCreationIdentityProvider, linked.Token).ConfigureAwait(false);
            }
            else if (delete)
            {
                if (!TryCreateDeletionRequest(document, overlay.VisualId, out var request) || request is null) return null;
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
                if (!overlay.CanAutoPoints) return null;
                command = new BpmnAutoPointsCommand(document, overlay.VisualId);
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
