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
            // Deliver any committed mutation before reporting a subsequent operation failure.
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
        BpmnModelerOperationStatus status) => status switch
        {
            BpmnModelerOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
            BpmnModelerOperationStatus.Rejected => BpmnModelerOperationStatus.Rejected,
            BpmnModelerOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
            BpmnModelerOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
            _ => BpmnModelerOperationStatus.Failed,
        };


}
