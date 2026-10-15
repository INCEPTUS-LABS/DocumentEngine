using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private TouchPlacementPress? _touchPlacementPress;

    private bool IsCurrentTouchPlacement(CanvasPointerInput input, EditingSession session,
        ToolboxPlacementController? placementController)
    {
        var current = session.CaptureState();
        return _touchPlacementPress is { } press && input.IsTouch &&
            input.Kind == CanvasPointerEventKind.Up && press.Input.PointerId == input.PointerId &&
            press.Input.CaptureGeneration == input.CaptureGeneration &&
            press.ItemId == placementController?.ActiveItemId &&
            press.State.DocumentId == current.DocumentId && press.State.DocumentRevision == current.DocumentRevision &&
            press.State.PresentationContext == current.PresentationContext;
    }

    private async ValueTask<bool> TryHandleTouchPanUnderGateAsync(
        CanvasPointerInput input,
        EditingSession session,
        Canvas2DInteractionController controller,
        ToolboxPlacementController? placementController)
    {
        ViewportPanGesture? gesture;
        lock (_sync) { gesture = _viewportPanGesture; }
        if (input.Kind < CanvasPointerEventKind.TouchPanStart)
        {
            if (input.Kind == CanvasPointerEventKind.Down) _touchPlacementPress = null;
            // Browser ownership normally filters these already. Keep direct managed input
            // from activating another editor operation while a touch pan owns the surface.
            return gesture?.IsTouch == true;
        }

        if (!input.IsTouch || input.CaptureGeneration <= 0) return true;
        if (input.Kind == CanvasPointerEventKind.TouchPanStart)
        {
            _touchPlacementPress = null;
            if (gesture is not null) return true;

            // Cancel only this pointer's preview, leaving the selected creation/connection
            // tool intact. The browser assigned a new capture generation at the transition,
            // so a delayed release of the old edit cannot release these two contacts.
            _ = await controller.PointerCancelledAsync(input.PointerId, _lifetimeToken).ConfigureAwait(false);
            await SuspendPlacementPreviewUnderGateAsync(session, forgetPointer: true, _lifetimeToken)
                .ConfigureAwait(false);
            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready || state.CurrentScene is null ||
                state.EditorState.ActiveGesture is not null)
            {
                if (_pointerObserver is { } observer)
                    await observer.ReleaseCaptureAsync(input.CaptureGeneration, _lifetimeToken).ConfigureAwait(false);
                return true;
            }

            var point = TouchPanPoint(input);
            if (point is null) return true;
            lock (_sync)
            {
                _viewportPanGesture = new(input.PointerId, input.CaptureGeneration, point.Value, IsTouch: true);
            }
            _ = await SetPointerCursorUnderGateAsync("grabbing", _lifetimeToken).ConfigureAwait(false);
            return true;
        }

        if (gesture is not { IsTouch: true } || gesture.PointerId != input.PointerId ||
            gesture.CaptureGeneration != input.CaptureGeneration) return true;

        var current = TouchPanPoint(input);
        if (input.Kind != CanvasPointerEventKind.TouchPanCancel && current is { } cssPoint)
        {
            var translation = cssPoint - gesture.LastCssPoint;
            if (translation != default)
            {
                var result = await session.PanViewportAsync(translation, _lifetimeToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    lock (_sync)
                    {
                        if (_viewportPanGesture == gesture)
                            _viewportPanGesture = gesture with { LastCssPoint = cssPoint };
                    }
                }
            }
        }

        if (current is null || input.Kind is CanvasPointerEventKind.TouchPanEnd or CanvasPointerEventKind.TouchPanCancel)
        {
            lock (_sync) { _viewportPanGesture = null; }
            _ = await SetPointerCursorUnderGateAsync(PlacementCursor(session, placementController), _lifetimeToken)
                .ConfigureAwait(false);
        }
        return true;
    }

    private static PointD? TouchPanPoint(CanvasPointerInput input)
    {
        var x = input.ClientX - input.CanvasLeft;
        var y = input.ClientY - input.CanvasTop;
        return double.IsFinite(x) && double.IsFinite(y) ? new PointD(x, y) : null;
    }

    private sealed record TouchPlacementPress(CanvasPointerInput Input, EditingSessionState State, ToolboxItemId ItemId);
}
