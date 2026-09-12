using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Canvas2D.Scene;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask<EditingSessionOperationResult> UpdateViewportCoreAsync(
        ViewportSnapshot requestedViewport,
        CancellationToken cancellationToken) =>
        await UpdateViewportCoreAsync(
            _ => requestedViewport,
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<EditingSessionOperationResult> PanViewportCoreAsync(
        VectorD canvasTranslation,
        CancellationToken cancellationToken) =>
        await UpdateViewportCoreAsync(
            current => canvasTranslation == default
                ? current
                : new ViewportSnapshot(
                    current.Zoom,
                    current.Pan + canvasTranslation,
                    current.VisibleDocumentRegion),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<EditingSessionOperationResult> UpdateViewportCoreAsync(
        Func<ViewportSnapshot, ViewportSnapshot> requestedViewportFactory,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        Task<EditingSessionOperationResult>? runTask = null;
        try
        {
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(EditingSessionOperationStatus.Closed);
                }

                var state = CaptureStateUnderLock();
                if (state.Status != EditingSessionStatus.Ready ||
                    state.CurrentScene is null)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "The viewport can be updated only while the Editing Session has a current scene.",
                            _documentId.Value)]);
                }

                var currentEditorState = state.EditorState;
                if (currentEditorState.ActiveGesture is not null)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "The viewport cannot be updated while a persistent gesture is active.",
                            _documentId.Value)]);
                }

                var requestedViewport = requestedViewportFactory(currentEditorState.Viewport);
                var viewport = PreserveVisibleDocumentRegion(
                    currentEditorState.Viewport,
                    requestedViewport);
                if (currentEditorState.Viewport.Equals(viewport))
                {
                    return OperationResult(EditingSessionOperationStatus.Succeeded);
                }

                if (_artifacts is not { } artifacts ||
                    !artifacts.IsCompatibleWith(
                        state.CurrentScene.DocumentId,
                        state.CurrentScene.SourceRevision,
                        state.ActiveScopeId))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.IncompatibleSceneArtifacts,
                            "The current Editing Session runtime results are not compatible with a viewport update.",
                            _documentId.Value)]);
                }

                var snapshot = AttachedDocument.CaptureSnapshot();
                if (snapshot.DocumentId != state.CurrentScene.DocumentId ||
                    snapshot.Revision != state.CurrentScene.SourceRevision)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            EditingSessionDiagnosticCodes.StalePipelineResult,
                            "The current scene no longer represents the Document revision for the viewport update.",
                            _documentId.Value)]);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Cancelled,
                        [CancelledDiagnostic(_documentId.Value)]);
                }

                var updatedEditorState = CopyEditorStateWithViewport(
                    currentEditorState,
                    viewport);
                if (!EditorState.TryUpdate(currentEditorState, updatedEditorState))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "Editor State changed before the viewport update could be installed.",
                            _documentId.Value)]);
                }

                var started = BeginRun(
                    snapshot,
                    artifacts,
                    cancellationToken,
                    state.Generation,
                    EditingSessionStatus.Ready);
                runTask = started.Task ?? throw new InvalidOperationException(
                    "A validated viewport scene rebuild could not be started atomically.");
            }
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return await runTask.ConfigureAwait(false);
    }

    private static ViewportSnapshot PreserveVisibleDocumentRegion(
        ViewportSnapshot current,
        ViewportSnapshot requested)
    {
        if (current.VisibleDocumentRegion is null)
        {
            return requested;
        }

        var cssSurface = Canvas2DSceneBuilder.CalculateCanvasCssSurface(current);
        var visibleRegion = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
            requested,
            cssSurface);
        return requested.VisibleDocumentRegion == visibleRegion
            ? requested
            : new ViewportSnapshot(requested.Zoom, requested.Pan, visibleRegion);
    }

    private static EditorStateSnapshot CopyEditorStateWithViewport(
        EditorStateSnapshot source,
        ViewportSnapshot viewport) =>
        new(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState,
            source.SemanticSceneSelection);
}
