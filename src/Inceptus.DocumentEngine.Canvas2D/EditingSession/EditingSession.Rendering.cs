using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask RenderInstalledSceneAsync(
        EditingSessionGeneration generation,
        Canvas2DScene scene)
    {
        var stateChanged = false;
        await _rendererGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_closing || _closed || generation != _generation ||
                    !ReferenceEquals(scene, _currentScene))
                {
                    return;
                }
            }

            var result = await _renderer.RenderAsync(scene).ConfigureAwait(false);
            lock (_sync)
            {
                if (!_closing && !_closed && generation == _generation &&
                    ReferenceEquals(scene, _currentScene))
                {
                    // Graphics execution is post-install presentation work. A failure never
                    // invalidates the current scene or changes Ready to RuntimeFaulted.
                    _presentationDiagnostics = result.Diagnostics;
                    stateChanged = true;
                }
            }
        }
        finally
        {
            _rendererGate.Release();
        }

        if (stateChanged)
        {
            NotifyStateChanged();
        }
    }

    private async ValueTask RenderLastKnownGoodSceneAsync(
        EditingSessionGeneration generation,
        Canvas2DScene scene)
    {
        await _rendererGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_closing || _closed || generation != _generation ||
                    _status != EditingSessionStatus.RuntimeFaulted ||
                    !ReferenceEquals(scene, _lastKnownGoodScene))
                {
                    return;
                }
            }

            var result = await _renderer.RenderAsync(scene).ConfigureAwait(false);
            lock (_sync)
            {
                if (!_closing && !_closed && generation == _generation &&
                    _status == EditingSessionStatus.RuntimeFaulted &&
                    ReferenceEquals(scene, _lastKnownGoodScene))
                {
                    _presentationDiagnostics = result.Diagnostics;
                }
            }
        }
        finally
        {
            _rendererGate.Release();
        }
    }

    private async ValueTask<Canvas2DRendererResult> ResizeCoreAsync(
        Canvas2DSurfaceSize surfaceSize,
        CancellationToken cancellationToken)
    {
        if (IsClosedOrClosing())
        {
            return RendererFailure("The Editing Session is closed.");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            Canvas2DRendererResult result;
            var stateChanged = false;
            await _rendererGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (IsClosedOrClosing())
                {
                    return RendererFailure("The Editing Session is closed.");
                }

                result = await _renderer.ResizeAsync(surfaceSize, linked.Token)
                    .ConfigureAwait(false);
                lock (_sync)
                {
                    if (!_closing && !_closed)
                    {
                        _presentationDiagnostics = result.Diagnostics;
                        stateChanged = true;
                    }
                }

            }
            finally
            {
                _rendererGate.Release();
            }

            if (stateChanged)
            {
                NotifyStateChanged();
            }

            if (result.Succeeded)
            {
                var state = CaptureState();
                var visibleRegion = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                    state.EditorState.Viewport,
                    surfaceSize);
                if (state.EditorState.Viewport.VisibleDocumentRegion != visibleRegion)
                {
                    var viewport = new Inceptus.DocumentEngine.Contracts.EditorState.ViewportSnapshot(
                        state.EditorState.Viewport.Zoom,
                        state.EditorState.Viewport.Pan,
                        visibleRegion);
                    var updated = await UpdateEditorStateCoreAsync(
                        CopyEditorStateWithViewport(state.EditorState, viewport),
                        linked.Token,
                        state.ActiveScopeId,
                        state.Generation).ConfigureAwait(false);
                    if (!updated.Succeeded)
                    {
                        return RendererFailure(
                            "The visible Document region could not be synchronized after resizing.");
                    }
                }
                else
                {
                    var rendered = await RenderCurrentCoreAsync(linked.Token)
                        .ConfigureAwait(false);
                    if (!rendered.Succeeded)
                    {
                        return rendered;
                    }
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
            return Canvas2DRendererResult.Cancelled(Error(
                EditingSessionDiagnosticCodes.PipelineCancelled,
                "The renderer operation was cancelled.",
                _documentId.Value));
        }
    }

    private async ValueTask<Canvas2DRendererResult> RenderCurrentCoreAsync(
        CancellationToken cancellationToken)
    {
        Canvas2DScene? scene;
        EditingSessionGeneration generation;
        lock (_sync)
        {
            if (_closing || _closed || _status != EditingSessionStatus.Ready ||
                _currentScene is null)
            {
                return RendererFailure("No current Canvas2DScene is available for rendering.");
            }

            scene = _currentScene;
            generation = _generation;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        Canvas2DRendererResult renderResult;
        var stateChanged = false;
        try
        {
            await _rendererGate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Canvas2DRendererResult.Cancelled(Error(
                EditingSessionDiagnosticCodes.PipelineCancelled,
                "The renderer operation was cancelled.",
                _documentId.Value));
        }

        try
        {
            lock (_sync)
            {
                if (_closing || _closed || generation != _generation ||
                    !ReferenceEquals(scene, _currentScene))
                {
                    return RendererFailure("The requested Canvas2DScene is no longer current.");
                }
            }

            renderResult = await _renderer.RenderAsync(scene, linked.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (!_closing && !_closed && generation == _generation &&
                    ReferenceEquals(scene, _currentScene))
                {
                    _presentationDiagnostics = renderResult.Diagnostics;
                    stateChanged = true;
                }
            }
        }
        finally
        {
            _rendererGate.Release();
        }

        if (stateChanged)
        {
            NotifyStateChanged();
        }
        return renderResult;
    }

    private bool IsClosedOrClosing()
    {
        lock (_sync)
        {
            return _closing || _closed;
        }
    }

    private Canvas2DRendererResult RendererFailure(string message) =>
        Canvas2DRendererResult.Failure(
            [Error(
                EditingSessionDiagnosticCodes.InvalidOperation,
                message,
                _documentId.Value)]);
}
