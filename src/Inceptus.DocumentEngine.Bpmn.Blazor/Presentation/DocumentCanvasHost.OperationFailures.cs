using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private ValueTask ReportModelerOperationFailureUnderGateAsync(NewDiagramHostResult? result) =>
        result is null
            ? ValueTask.CompletedTask
            : ReportModelerOperationFailureUnderGateAsync(
                BpmnModelerOperation.New,
                result.Status switch
                {
                    NewDiagramHostOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
                    NewDiagramHostOperationStatus.Unavailable =>
                        BpmnModelerOperationStatus.Unavailable,
                    NewDiagramHostOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
                    _ => BpmnModelerOperationStatus.Failed,
                },
                result.Diagnostics);

    private ValueTask ReportModelerOperationFailureUnderGateAsync(
        BpmnModelerOperation operation,
        NativeDocumentHostImportResult? result) =>
        result is null
            ? ValueTask.CompletedTask
            : ReportModelerOperationFailureUnderGateAsync(
                operation, ModelerStatus(result.Status), result.Diagnostics);

    private ValueTask ReportModelerOperationFailureUnderGateAsync(
        NativeDocumentHostExportResult? result) =>
        result is null
            ? ValueTask.CompletedTask
            : ReportModelerOperationFailureUnderGateAsync(
                BpmnModelerOperation.Export, ModelerStatus(result.Status), result.Diagnostics);

    private ValueTask ReportModelerOperationFailureUnderGateAsync(
        PublishedProcessHostResult? result) =>
        result is null
            ? ValueTask.CompletedTask
            : ReportModelerOperationFailureUnderGateAsync(
                BpmnModelerOperation.Publish, ModelerStatus(result.Status), result.Diagnostics);

    private ValueTask ReportModelerOperationFailureUnderGateAsync(
        PublicationDialogHostResult? result) =>
        result is null
            ? ValueTask.CompletedTask
            : ReportModelerOperationFailureUnderGateAsync(
                BpmnModelerOperation.Publish, ModelerStatus(result.Status), result.Diagnostics);

    private async ValueTask ReportModelerOperationFailureUnderGateAsync(
        BpmnModelerOperation operation,
        BpmnModelerOperationStatus status,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (status is BpmnModelerOperationStatus.Succeeded or BpmnModelerOperationStatus.Cancelled ||
            ModelerNotifications is null)
        {
            return;
        }

        try
        {
            // A toolbar publication edit may have committed before package generation failed.
            // Deliver that mutation before reporting the subsequent operation failure.
            await CollectModelerNotificationsUnderGateAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        lock (_sync)
        {
            if (!_disposed)
            {
                _ = ModelerNotifications?.Enqueue(new BpmnModelerOperationFailedEventArgs(
                    operation, status, diagnostics));
            }
        }
    }

    private static BpmnModelerOperationStatus ModelerStatus(
        NativeDocumentHostOperationStatus status) => status switch
        {
            NativeDocumentHostOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
            NativeDocumentHostOperationStatus.Rejected => BpmnModelerOperationStatus.Rejected,
            NativeDocumentHostOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
            NativeDocumentHostOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
            _ => BpmnModelerOperationStatus.Failed,
        };

    private static BpmnModelerOperationStatus ModelerStatus(
        PublishedProcessHostOperationStatus status) => status switch
        {
            PublishedProcessHostOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
            PublishedProcessHostOperationStatus.Rejected => BpmnModelerOperationStatus.Rejected,
            PublishedProcessHostOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
            PublishedProcessHostOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
            _ => BpmnModelerOperationStatus.Failed,
        };
}
