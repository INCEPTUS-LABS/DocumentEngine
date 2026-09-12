using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private const string PresentationCaptureUnavailable =
        "EDITING_SESSION_PRESENTATION_CAPTURE_UNAVAILABLE";
    private const string PresentationCaptureFailed =
        "EDITING_SESSION_PRESENTATION_CAPTURE_FAILED";

    /// <summary>
    /// Derives a detached Scene from one current revision/scope/generation tuple using caller-
    /// supplied transient presentation preferences. It does not install state, execute a Command,
    /// or change History.
    /// </summary>
    public async ValueTask<EditingSessionPresentationCaptureResult> CapturePresentationAsync(
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);

        try
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CaptureFailure(
                EditingSessionPresentationCaptureStatus.Cancelled,
                "The presentation capture was cancelled.");
        }

        try
        {
            DocumentSnapshot document;
            EditingSessionPipelineArtifacts artifacts;
            EditingSessionGeneration generation;
            lock (_sync)
            {
                if (_closing || _closed || _document is null ||
                    _status != EditingSessionStatus.Ready ||
                    _currentScene is null ||
                    _artifacts is null)
                {
                    return CaptureFailure(
                        EditingSessionPresentationCaptureStatus.Unavailable,
                        "A current presentation is required before Publish can begin.");
                }

                document = _document.CaptureSnapshot();
                artifacts = _artifacts;
                generation = _generation;
                if (!artifacts.IsCompatibleWith(
                        document.DocumentId,
                        document.Revision,
                        _activeScopeId) ||
                    _currentScene.DocumentId != document.DocumentId ||
                    _currentScene.SourceRevision != document.Revision)
                {
                    return CaptureFailure(
                        EditingSessionPresentationCaptureStatus.Unavailable,
                        "The current presentation changed before Publish could capture it.");
                }
            }

            var rebuilt = await _pipeline.RebuildSceneAsync(
                document,
                artifacts,
                EditorStateSnapshot.Empty,
                modelProfileViewState,
                modelProfileElementViewState,
                cancellationToken).ConfigureAwait(false);
            if (rebuilt.Status != EditingSessionPipelineStatus.Succeeded ||
                rebuilt.Scene is null ||
                rebuilt.Artifacts is null ||
                !rebuilt.Artifacts.IsCompatibleWith(
                    document.DocumentId,
                    document.Revision,
                    artifacts.ScopeId))
            {
                return EditingSessionPresentationCaptureResult.Failure(
                    EditingSessionPresentationCaptureStatus.Failed,
                    rebuilt.Diagnostics.Any(static diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error)
                            ? rebuilt.Diagnostics
                            :
                            [
                                Error(
                                    PresentationCaptureFailed,
                                    "The current process presentation could not be prepared for Publish.",
                                    document.DocumentId.Value),
                            ]);
            }

            return EditingSessionPresentationCaptureResult.Success(
                new EditingSessionPresentationCapture(
                    document,
                    artifacts.ScopeId,
                    generation,
                    rebuilt.Artifacts.ProjectedGraph,
                    rebuilt.Artifacts.LayoutResult,
                    rebuilt.Artifacts.RoutingResult,
                    rebuilt.Scene));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CaptureFailure(
                EditingSessionPresentationCaptureStatus.Cancelled,
                "The presentation capture was cancelled.");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private EditingSessionPresentationCaptureResult CaptureFailure(
        EditingSessionPresentationCaptureStatus status,
        string message) =>
        EditingSessionPresentationCaptureResult.Failure(
            status,
            [
                Error(
                    status == EditingSessionPresentationCaptureStatus.Failed
                        ? PresentationCaptureFailed
                        : PresentationCaptureUnavailable,
                    message,
                    _documentId.Value),
            ]);
}
