using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private const string NativeDocumentExportUnavailable =
        "CANVAS_NATIVE_DOCUMENT_EXPORT_UNAVAILABLE";
    private const string NativeDocumentImportUnavailable =
        "CANVAS_NATIVE_DOCUMENT_IMPORT_UNAVAILABLE";
    private const string NativeDocumentExportFailed =
        "CANVAS_NATIVE_DOCUMENT_EXPORT_FAILED";
    private const string NativeDocumentReplacementFailed =
        "CANVAS_NATIVE_DOCUMENT_REPLACEMENT_FAILED";

    private static readonly DocumentSessionReplacementMessages NativeImportReplacementMessages =
        new(
            NativeDocumentImportUnavailable,
            "Native Document import is unavailable while the editor is not ready.",
            NativeDocumentReplacementFailed,
            "The imported Document editor could not be created.",
            "The imported Document canvas could not be initialized.",
            "The imported Document could not reach a ready editing session.",
            "The imported Document could not replace the current editor session.");

    private string? _nativeDocumentCanvasElementId;
    private string? _nativeDocumentStandbyCanvasElementId;
    private EditingSessionConfiguration? _nativeDocumentSessionConfiguration;

    internal async ValueTask<NativeDocumentHostExportResult> ExportNativeDocumentAsync(
        CancellationToken cancellationToken = default)
    {
        NativeDocumentHostExportResult? outcome = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = NativeDocumentHostExportResult.Cancelled;
        }

        try
        {
            EditingSession? session;
            lock (_sync)
            {
                session = _session;
                if (_disposed || !_initialized || session is null ||
                    _propertiesFormOpen || _modelViewPropertiesFormOpen)
                {
                    return outcome = NativeDocumentHostExportResult.Failure(
                        NativeDocumentHostOperationStatus.Unavailable,
                        Error(
                            NativeDocumentExportUnavailable,
                            "Native Document export is unavailable while the editor is not ready."));
                }
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                !session.TryCaptureDocumentSnapshot(out var snapshot))
            {
                return outcome = NativeDocumentHostExportResult.Failure(
                    NativeDocumentHostOperationStatus.Unavailable,
                    Error(
                        NativeDocumentExportUnavailable,
                        "Native Document export is unavailable while the editor is not ready."));
            }

#pragma warning disable CA1031 // Export failures become bounded operation diagnostics.
            try
            {
                return outcome = NativeDocumentHostExportResult.Success(
                    NativeDocumentSerializer.Export(snapshot));
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return outcome = NativeDocumentHostExportResult.Failure(
                    NativeDocumentHostOperationStatus.Failed,
                    Error(
                        NativeDocumentExportFailed,
                        "The current Document could not be prepared for download.",
                        exception.GetType().FullName ?? exception.GetType().Name));
            }
#pragma warning restore CA1031
        }
        finally
        {
            try
            {
                await ReportModelerOperationFailureUnderGateAsync(outcome).ConfigureAwait(false);
            }
            finally
            {
                await CompleteModelerOperationAsync().ConfigureAwait(false);
            }
        }
    }

    internal async ValueTask<NativeDocumentHostImportResult> ImportNativeDocumentAsync(
        ReadOnlyMemory<byte> utf8Json,
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
            }

            if (currentSession is null)
            {
                return outcome = NativeDocumentHostImportResult.Failure(
                    NativeDocumentHostOperationStatus.Unavailable,
                    Error(
                        NativeDocumentImportUnavailable,
                        NativeImportReplacementMessages.UnavailableMessage));
            }

            var import = NativeDocumentSerializer.Import(
                utf8Json,
                currentSession.ConnectorAnchorPolicyProvider);
            if (!import.Succeeded)
            {
                return outcome = NativeDocumentHostImportResult.Rejected(import.Diagnostics);
            }

            DocumentSessionReplacementResult replacement;
            try
            {
                replacement = await ReplaceDocumentUnderGateAsync(
                    import.Document!,
                    NativeImportReplacementMessages,
                    linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return outcome = NativeDocumentHostImportResult.Cancelled;
            }

            notify = replacement.ShouldNotify;
            return outcome = replacement.Status switch
            {
                DocumentSessionReplacementStatus.Succeeded =>
                    NativeDocumentHostImportResult.Success with
                    {
                        Snapshot = import.Document!.CaptureSnapshot(),
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
        finally
        {
            try
            {
                await ReportModelerOperationFailureUnderGateAsync(
                    BpmnModelerOperation.Import, outcome).ConfigureAwait(false);
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

    private static Diagnostic Error(string code, string message, string? sourceIdentity = null) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}

internal enum NativeDocumentHostOperationStatus
{
    Succeeded = 0,
    Rejected = 1,
    Unavailable = 2,
    Failed = 3,
    Cancelled = 4,
}

internal sealed record NativeDocumentHostExportResult(
    NativeDocumentHostOperationStatus Status,
    ImmutableArray<byte> Payload,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Succeeded => Status == NativeDocumentHostOperationStatus.Succeeded;

    internal static NativeDocumentHostExportResult Success(ImmutableArray<byte> payload) =>
        new(NativeDocumentHostOperationStatus.Succeeded, payload, []);

    internal static NativeDocumentHostExportResult Failure(
        NativeDocumentHostOperationStatus status,
        Diagnostic diagnostic) =>
        new(status, [], [diagnostic]);

    internal static NativeDocumentHostExportResult Cancelled { get; } =
        new(NativeDocumentHostOperationStatus.Cancelled, [], []);
}

internal sealed record NativeDocumentHostImportResult(
    NativeDocumentHostOperationStatus Status,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal DocumentSnapshot? Snapshot { get; init; }

    internal bool Succeeded => Status == NativeDocumentHostOperationStatus.Succeeded;

    internal static NativeDocumentHostImportResult Success { get; } =
        new(NativeDocumentHostOperationStatus.Succeeded, []);

    internal static NativeDocumentHostImportResult Rejected(
        ImmutableArray<Diagnostic> diagnostics) =>
        new(NativeDocumentHostOperationStatus.Rejected, diagnostics);

    internal static NativeDocumentHostImportResult Failure(
        NativeDocumentHostOperationStatus status,
        Diagnostic diagnostic) =>
        new(status, [diagnostic]);

    internal static NativeDocumentHostImportResult Cancelled { get; } =
        new(NativeDocumentHostOperationStatus.Cancelled, []);
}
