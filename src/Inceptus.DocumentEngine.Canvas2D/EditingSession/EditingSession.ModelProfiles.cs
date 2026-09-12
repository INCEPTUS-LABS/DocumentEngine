using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask<EditingSessionOperationResult>
        UpdateModelProfileViewStateCoreAsync(
            ModelProfileViewStateSnapshot requestedState,
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
            DocumentSnapshot snapshot;
            EditingSessionPipelineArtifacts? artifacts;
            EditingSessionGeneration expectedGeneration;
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(EditingSessionOperationStatus.Closed);
                }

                if (_activeModelProfileViewState.Equals(requestedState))
                {
                    return OperationResult(EditingSessionOperationStatus.Succeeded);
                }

                cancellationToken.ThrowIfCancellationRequested();
                snapshot = AttachedDocument.CaptureSnapshot();
                artifacts = _compatibleArtifacts;
                expectedGeneration = _generation;
                if (artifacts is null ||
                    !artifacts.IsCompatibleWith(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        _activeScopeId))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "Profile visibility requires current retained Process artifacts.",
                            _documentId.Value)]);
                }
            }

            var started = BeginRun(
                snapshot,
                artifacts,
                cancellationToken,
                expectedGeneration,
                requestedModelProfileViewState: requestedState);
            runTask = started.Task;
            if (runTask is null)
            {
                return OperationResult(
                    EditingSessionOperationStatus.Superseded,
                    started.Diagnostics);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return await runTask.ConfigureAwait(false);
    }
}
