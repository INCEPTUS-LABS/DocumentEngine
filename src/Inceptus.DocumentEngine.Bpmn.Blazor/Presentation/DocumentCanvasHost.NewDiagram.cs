using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private const string NewDiagramUnavailable = "CANVAS_NEW_DIAGRAM_UNAVAILABLE";
    private const string NewDiagramCreationFailed = "CANVAS_NEW_DIAGRAM_CREATION_FAILED";
    private const string NewDiagramReplacementFailed = "CANVAS_NEW_DIAGRAM_REPLACEMENT_FAILED";

    private static readonly DocumentSessionReplacementMessages NewDiagramReplacementMessages =
        new(
            NewDiagramUnavailable,
            "New diagram is unavailable while the editor is not ready.",
            NewDiagramReplacementFailed,
            "The new diagram editor could not be created.",
            "The new diagram canvas could not be initialized.",
            "The new diagram could not reach a ready editing session.",
            "The new diagram could not replace the current editor session.");

    internal async ValueTask<NewDiagramHostResult> NewDiagramAsync(
        CancellationToken cancellationToken = default)
    {
        NewDiagramHostResult? outcome = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return outcome = NewDiagramHostResult.Cancelled;
        }

        var notify = false;
        try
        {
            EditingSession? currentSession;
            EditingSessionConfiguration? sourceConfiguration;
            lock (_sync)
            {
                currentSession = _session;
                sourceConfiguration = _nativeDocumentSessionConfiguration;
            }

            if (currentSession is null || sourceConfiguration is null)
            {
                return outcome = NewDiagramHostResult.Failure(
                    NewDiagramHostOperationStatus.Unavailable,
                    Error(
                        NewDiagramUnavailable,
                        NewDiagramReplacementMessages.UnavailableMessage));
            }

            DocumentConstructionResult construction;
#pragma warning disable CA1031 // Construction failures become a bounded operation diagnostic.
            try
            {
                construction = DocumentFactory.CreateEmpty(
                    CreateFreshDocumentId(currentSession.CaptureState().DocumentId),
                    connectorAnchorPolicyProvider:
                        sourceConfiguration.ConnectorAnchorPolicyProvider);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                var diagnostic = Error(
                    NewDiagramCreationFailed,
                    "A new empty Document could not be created.",
                    exception.GetType().FullName ?? exception.GetType().Name);
                SetNewDiagramFailure(diagnostic);
                notify = true;
                return outcome = NewDiagramHostResult.Failure(
                    NewDiagramHostOperationStatus.Failed,
                    diagnostic);
            }
#pragma warning restore CA1031

            if (!construction.Succeeded || construction.Document is null)
            {
                var diagnostic = Error(
                    NewDiagramCreationFailed,
                    "A new empty Document could not be created.");
                SetNewDiagramFailure(diagnostic);
                notify = true;
                return outcome = NewDiagramHostResult.Failure(
                    NewDiagramHostOperationStatus.Failed,
                    diagnostic);
            }

            DocumentSessionReplacementResult replacement;
            try
            {
                replacement = await ReplaceDocumentUnderGateAsync(
                    construction.Document,
                    NewDiagramReplacementMessages,
                    linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return outcome = NewDiagramHostResult.Cancelled;
            }

            notify = replacement.ShouldNotify;
            return outcome = replacement.Status switch
            {
                DocumentSessionReplacementStatus.Succeeded => NewDiagramHostResult.Success with
                {
                    Snapshot = construction.Document.CaptureSnapshot(),
                },
                DocumentSessionReplacementStatus.Unavailable => NewDiagramHostResult.Failure(
                    NewDiagramHostOperationStatus.Unavailable,
                    replacement.Diagnostic!),
                _ => NewDiagramHostResult.Failure(
                    NewDiagramHostOperationStatus.Failed,
                    replacement.Diagnostic!),
            };
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
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    private static DocumentId CreateFreshDocumentId(DocumentId previousDocumentId)
    {
        ArgumentNullException.ThrowIfNull(previousDocumentId);
        DocumentId candidate;
        do
        {
            candidate = new DocumentId($"document:{Guid.NewGuid():N}");
        }
        while (candidate == previousDocumentId);

        return candidate;
    }

    private void SetNewDiagramFailure(Diagnostic diagnostic)
    {
        lock (_sync)
        {
            _hostDiagnostics = [diagnostic];
        }
    }
}

internal enum NewDiagramHostOperationStatus
{
    Succeeded = 0,
    Unavailable = 1,
    Failed = 2,
    Cancelled = 3,
}

internal sealed record NewDiagramHostResult(
    NewDiagramHostOperationStatus Status,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal DocumentSnapshot? Snapshot { get; init; }

    internal bool Succeeded => Status == NewDiagramHostOperationStatus.Succeeded;

    internal static NewDiagramHostResult Success { get; } =
        new(NewDiagramHostOperationStatus.Succeeded, []);

    internal static NewDiagramHostResult Failure(
        NewDiagramHostOperationStatus status,
        Diagnostic diagnostic) =>
        new(status, [diagnostic]);

    internal static NewDiagramHostResult Cancelled { get; } =
        new(NewDiagramHostOperationStatus.Cancelled, []);
}
