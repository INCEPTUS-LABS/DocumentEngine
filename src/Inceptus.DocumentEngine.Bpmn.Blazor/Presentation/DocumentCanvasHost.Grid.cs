namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    internal async Task RefreshGridAsync()
    {
        if (_disposed) return;
        await EnterModelerOperationAsync(_lifetimeToken).ConfigureAwait(false);
        try
        {
            var session = _session;
            if (_disposed || session is null) return;
            // The shared preference is read on every frame and at gesture acquisition.
            // An active drag keeps its acquired snapping rule until commit/cancel.
            await session.RenderCurrentAsync(_lifetimeToken).ConfigureAwait(false);
            if (session.CaptureState().EditorState.ActiveGesture is null)
                await RefreshPlacementAtPointerUnderGateAsync(session, _lifetimeToken).ConfigureAwait(false);
        }
        finally { await CompleteModelerOperationAsync().ConfigureAwait(false); }
    }
}
