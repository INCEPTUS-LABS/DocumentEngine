using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private const string DocumentLoadUnavailable = "CANVAS_DOCUMENT_LOAD_UNAVAILABLE";
    private const string DocumentLoadFailed = "CANVAS_DOCUMENT_LOAD_FAILED";

    private static readonly DocumentSessionReplacementMessages DocumentLoadReplacementMessages =
        new(
            DocumentLoadUnavailable,
            "Document loading is unavailable while the editor is not ready.",
            "CANVAS_DOCUMENT_LOAD_REPLACEMENT_FAILED",
            "The loaded Document editor could not be created.",
            "The loaded Document canvas could not be initialized.",
            "The loaded Document could not reach a ready editing session.",
            "The loaded Document could not replace the current editor session.");

    internal async ValueTask<NativeDocumentHostImportResult> LoadDocumentAsync(
        DocumentSnapshot? document,
        CancellationToken cancellationToken = default)
    {
        NativeDocumentHostImportResult? outcome = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = NativeDocumentHostImportResult.Cancelled;
        }

        var notify = false;
        try
        {
            EditingSession? currentSession;
            lock (_sync)
            {
                currentSession = _session;
                if (_disposed || !_initialized || currentSession is null)
                {
                    return outcome = NativeDocumentHostImportResult.Failure(
                        NativeDocumentHostOperationStatus.Unavailable,
                        Error(
                            DocumentLoadUnavailable,
                            DocumentLoadReplacementMessages.UnavailableMessage));
                }
            }

            if (document is null)
            {
                return outcome = NativeDocumentHostImportResult.Rejected(
                    [Error("CANVAS_DOCUMENT_LOAD_REQUIRED", "A Document snapshot is required.")]);
            }

            var reconstruction = DocumentReconstructor.Reconstruct(
                document,
                currentSession.ConnectorAnchorPolicyProvider);
            if (!reconstruction.Succeeded)
            {
                return outcome = NativeDocumentHostImportResult.Rejected(reconstruction.Diagnostics);
            }

            var replacement = await ReplaceDocumentUnderGateAsync(
                reconstruction.Document!,
                DocumentLoadReplacementMessages,
                linked.Token).ConfigureAwait(false);
            notify = replacement.ShouldNotify;
            return outcome = replacement.Status switch
            {
                DocumentSessionReplacementStatus.Succeeded =>
                    NativeDocumentHostImportResult.Success with
                    {
                        Snapshot = reconstruction.Document!.CaptureSnapshot(),
                    },
                DocumentSessionReplacementStatus.Unavailable =>
                    NativeDocumentHostImportResult.Failure(
                        NativeDocumentHostOperationStatus.Unavailable,
                        replacement.Diagnostic!),
                _ => NativeDocumentHostImportResult.Failure(
                    NativeDocumentHostOperationStatus.Failed,
                    replacement.Diagnostic!),
            };
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = NativeDocumentHostImportResult.Cancelled;
        }
#pragma warning disable CA1031 // Load failures become bounded operation diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return outcome = NativeDocumentHostImportResult.Failure(
                NativeDocumentHostOperationStatus.Failed,
                Error(
                    DocumentLoadFailed,
                    "The Document snapshot could not be loaded.",
                    exception.GetType().FullName ?? exception.GetType().Name));
        }
#pragma warning restore CA1031
        finally
        {
            try
            {
                await ReportModelerOperationFailureUnderGateAsync(
                    BpmnModelerOperation.Load, outcome).ConfigureAwait(false);
            }
            finally
            {
                await CompleteModelerOperationAsync().ConfigureAwait(false);
            }
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }
}
