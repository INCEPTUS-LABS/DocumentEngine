using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private sealed record ConnectorToolboxRequest(long SessionVersion, DocumentId DocumentId,
        DocumentRevision Revision, PresentationViewContext Context, VisualStateId VisualId,
        Matrix2D Viewport, PointD Click);
    private ConnectorToolboxRequest? _connectorToolboxRequest;

    private void CompleteConnectorToolboxClick(CanvasPointerInput input, Canvas2DInteractionResult result)
    {
        if (input.Kind != CanvasPointerEventKind.Up || !result.IsCompletedSelectionClick ||
            !result.Succeeded || !_smartEditEnabled || input.ControlKey || input.MetaKey || input.ShiftKey ||
            result.SessionState.EditorState.Selection is not { Length: 1 } selection ||
            result.SessionState.CurrentScene is not { } scene) return;
        var state = result.SessionState;
        _connectorToolboxRequest = new(_documentSessionVersion, state.DocumentId, state.DocumentRevision,
            state.PresentationContext, selection[0], scene.ViewportTransform,
            new PointD(input.ClientX - input.CanvasLeft, input.ClientY - input.CanvasTop));
        if (!TryCaptureConnectorToolboxTarget(state, out _, out _, out _)) _connectorToolboxRequest = null;
    }

    private bool TryCaptureConnectorToolboxTarget(EditingSessionState? state, out DocumentSnapshot document,
        out VisualStateId id, out Canvas2DSceneItem? target)
    {
        document = null!;
        id = null!;
        target = null;
        if (_connectorToolboxRequest is not { } request) return false;
        if (!_smartEditEnabled || _disposed || !_initialized || _propertiesFormOpen || _modelViewPropertiesFormOpen ||
            _contextMenu is not null || state is null || state.IsClosed || state.EditorState.ActiveGesture is not null ||
            state.EditorState.SemanticSceneSelection is not null || _toolboxSelection.SelectedItemId is not null ||
            ConnectionToolState.GetSelection(state.EditorState) is not null ||
            _documentSessionVersion != request.SessionVersion || state.DocumentId != request.DocumentId ||
            state.DocumentRevision != request.Revision || state.PresentationContext != request.Context ||
            state.EditorState.Selection.Length != 1 || state.EditorState.Selection[0] != request.VisualId ||
            state.CurrentScene is { } currentScene && currentScene.ViewportTransform != request.Viewport)
        {
            _connectorToolboxRequest = null;
            return false;
        }
        if (state.Status != EditingSessionStatus.Ready || state.CurrentScene is null || _surfaceSize is null ||
            _session is null || !_session.TryCaptureDocumentSnapshot(out var snapshot) || snapshot.Revision != request.Revision ||
            !snapshot.VisualModel.TryGetVisualState(request.VisualId, out var visual) ||
            !ModelerPropertyEditing.SupportsConnectorActions(snapshot, visual!.SemanticElementId) ||
            !TryResolveVisualPropertiesTarget(state, snapshot, request.VisualId, null,
                GetSpatialRegionId(state, request.VisualId), out target, out _)) return false;
        id = request.VisualId;
        document = snapshot;
        return true;
    }

    private ElementToolboxState? CaptureConnectorToolbox(EditingSessionState? state)
    {
        if (!TryCaptureConnectorToolboxTarget(state, out var document, out var id, out _))
            return state?.Status == EditingSessionStatus.Rebuilding && _connectorToolboxRequest is { } pending &&
                _elementToolbox is { IsConnector: true } previous && previous.VisualId == pending.VisualId ? previous : null;
        var click = _connectorToolboxRequest!.Click;
        var surface = _surfaceSize!.Value;
        const double height = 58, gap = 8;
        var canProperties = CaptureToolboxPropertiesEligibility(document, id);
        var width = ElementToolboxState.MeasureActionsWidth(true, canProperties, false);
        var position = new PointD(Math.Clamp(click.X - width / 2, gap, Math.Max(gap, surface.CssWidth - width - gap)),
            Math.Clamp(click.Y - height - gap, gap, Math.Max(gap, surface.CssHeight - height - gap)));
        var canDelete = TryCreateDeletionRequest(document, id, out var request) && request is not null &&
            _deletionCatalog.GetMatchingRegistrations(request).Length == 1;
        return new(id, new RectD(click.X, click.Y, 0, 0), position, default, 0, false, canDelete, [])
        { IsConnector = true, CanProperties = canProperties };
    }
}
