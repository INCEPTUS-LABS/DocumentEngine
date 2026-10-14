using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    internal bool CanAlign(bool horizontal)
    {
        lock (_sync)
            return !_disposed && _initialized && !_propertiesFormOpen && !_modelViewPropertiesFormOpen &&
                _session is { } session && TryPrepareAlignment(session, session.CaptureState(), horizontal, out _, out _);
    }

    private static bool TryPrepareAlignment(EditingSession session, EditingSessionState state, bool horizontal,
        out MoveVisualStatesCommand? command, out Canvas2DSceneItem? reference)
    {
        command = null;
        reference = null;
        if (state.Status != EditingSessionStatus.Ready || state.EditorState.ActiveGesture is not null ||
            state.CurrentScene is null || state.SelectionOrder.Length < 2 ||
            state.PresentationContext is not ProcessScopePresentationContext ||
            !session.TryCaptureDocumentSnapshot(out var document) || document.Revision != state.DocumentRevision)
            return false;

        var moves = new List<VisualStateMove>();
        foreach (var id in state.SelectionOrder)
        {
            if (!document.VisualModel.TryGetVisualState(id, out var visual) || visual is null ||
                visual.BoundaryAttachment is not null ||
                !document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) || element is null ||
                !BpmnGridPolicy.Supports(element.TypeId) ||
                !TryResolveVisualPropertiesTarget(state, document, id, null, GetSpatialRegionId(state, id), out var body, out _) ||
                body is null || body.DerivedRegionMapping is not null)
                return false;

            if (reference is null)
            {
                reference = body;
                continue;
            }

            // Compare centers in the shared displayed diagram, then invert this node's
            // own region mapping. Different Pools/Lanes need not share a local origin.
            // Keep each node inside its existing region without resizing or reparenting.
            var bounds = body.Bounds;
            var target = horizontal
                ? new PointD(bounds.X, reference.Bounds.Y + (reference.Bounds.Height - bounds.Height) / 2)
                : new PointD(reference.Bounds.X + (reference.Bounds.Width - bounds.Width) / 2, bounds.Y);
            if (target == bounds.TopLeft) continue;
            var targetBounds = new RectD(target.X, target.Y, bounds.Width, bounds.Height);
            var local = body.MapPresentationToLocal(targetBounds);
            if (!DocumentGeometryBoundary.Contains(local) ||
                body.SpatialRegion is { } region && !region.Bounds.Contains(targetBounds)) return false;

            // Convert at the existing scene/local boundary once, retaining the untouched
            // authoritative axis exactly. Grid preferences never move the reference.
            var position = horizontal ? new PointD(visual.Position.X, local.Y) : new PointD(local.X, visual.Position.Y);
            if (position != visual.Position) moves.Add(new(id, position, VisualPlacementMode.Pinned));
        }

        if (moves.Count == 0) return false;
        command = new MoveVisualStatesCommand(document.DocumentId, document.Revision, moves);
        return true;
    }

    internal async ValueTask<HistoryOperationResult?> AlignAsync(bool horizontal, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EditingSession? session;
            lock (_sync) session = _disposed || !_initialized || _propertiesFormOpen || _modelViewPropertiesFormOpen ? null : _session;
            if (session is null) return null;
            var state = session.CaptureState();
            if (!TryPrepareAlignment(session, state, horizontal, out var command, out var reference)) return null;
            var result = await session.ExecuteForSceneTargetAsync(command!, state.CurrentScene!, state.Generation,
                reference!.Id, reference.SpatialRegion, linked.Token).ConfigureAwait(false);
            RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted) await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }
}
