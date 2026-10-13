using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Per-component bridge from the public handle to the existing canvas-host authorities.
/// The child component continues to own asynchronous host and browser disposal.
/// </summary>
internal sealed partial class BpmnModelerFacade : IDisposable, IBpmnDocumentEditor
{
    private readonly object _sync = new();
    private DocumentCanvasHost? _host;
    private bool _disposed;

    internal BpmnModelerFacade(
        BpmnModelerCompositionFactory factory,
        Func<object, Task> invokeNotification)
    {
        ArgumentNullException.ThrowIfNull(factory);
        CompositionFactory = factory;
        Notifications = new BpmnModelerNotifications(invokeNotification);
    }

    internal BpmnModelerCompositionFactory CompositionFactory { get; }

    internal BpmnModelerNotifications Notifications { get; }

    public BpmnModelerNativeSaveStatus? NativeSaveStatus => CaptureHost()?.CaptureNativeSaveStatus();

    public bool AcknowledgeNativeSave(BpmnModelerSaveCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return CaptureHost()?.AcknowledgeNativeSave(checkpoint) == true;
    }

    internal void Attach(DocumentCanvasHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_host is not null && !ReferenceEquals(_host, host))
            {
                throw new InvalidOperationException("The modeler already owns a canvas host.");
            }

            host.ModelerNotifications = Notifications;
            _host = host;
            host.ViewChanged += OnViewChanged;
        }
    }

    internal BpmnModelerDocumentResult CaptureDocumentSnapshot()
    {
        var result = CaptureHost()?.CaptureDocumentSnapshot() ??
            new BpmnModelerDocumentResult(BpmnModelerOperationStatus.Unavailable, null,
                UnavailableDiagnostics());
        if (!result.Succeeded)
        {
            _ = ReportFailureAsync(BpmnModelerOperation.Capture, result.Status, result.Diagnostics);
        }

        return result;
    }

    internal ValueTask<BpmnModelerDocumentResult> NewDocumentAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteDocumentOperationAsync(BpmnModelerOperation.New, async (host, token) =>
        {
            var result = await host.NewDiagramAsync(token).ConfigureAwait(false);
            var status = result.Status switch
            {
                NewDiagramHostOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
                NewDiagramHostOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
                NewDiagramHostOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
                _ => BpmnModelerOperationStatus.Failed,
            };
            return new BpmnModelerDocumentResult(status, result.Snapshot, result.Diagnostics);
        }, cancellationToken);

    internal ValueTask<BpmnModelerDocumentResult> LoadDocumentAsync(
        DocumentSnapshot? snapshot,
        CancellationToken cancellationToken = default) =>
        ExecuteDocumentOperationAsync(BpmnModelerOperation.Load, async (host, token) =>
        {
            var result = await host.LoadDocumentAsync(snapshot, token).ConfigureAwait(false);
            return new BpmnModelerDocumentResult(
                Map(result.Status), result.Snapshot, result.Diagnostics);
        }, cancellationToken);

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            if (_host is not null) _host.ViewChanged -= OnViewChanged;
            _host = null;
            Notifications.Dispose();
        }
    }

    private DocumentCanvasHost? CaptureHost()
    {
        lock (_sync)
        {
            return _disposed ? null : _host;
        }
    }

    private async ValueTask<BpmnModelerDocumentResult> ExecuteDocumentOperationAsync(
        BpmnModelerOperation operation,
        Func<DocumentCanvasHost, CancellationToken, ValueTask<BpmnModelerDocumentResult>> execute,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(BpmnModelerOperationStatus.Cancelled, null, []);
        }

        var host = CaptureHost();
        if (host is null)
        {
            var diagnostics = UnavailableDiagnostics();
            await ReportFailureAsync(operation, BpmnModelerOperationStatus.Unavailable, diagnostics)
                .ConfigureAwait(false);
            return new(BpmnModelerOperationStatus.Unavailable, null, diagnostics);
        }

        try
        {
            // The host reports its own returned failures under its canonical operation gate.
            return await execute(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(BpmnModelerOperationStatus.Cancelled, null, []);
        }
#pragma warning disable CA1031 // Unexpected bridge failures become bounded operation diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            var diagnostics = FailedDiagnostics();
            await ReportFailureAsync(operation, BpmnModelerOperationStatus.Failed, diagnostics)
                .ConfigureAwait(false);
            return new(BpmnModelerOperationStatus.Failed, null, diagnostics);
        }
#pragma warning restore CA1031
    }

    private Task ReportFailureAsync(
        BpmnModelerOperation operation,
        BpmnModelerOperationStatus status,
        ImmutableArray<Diagnostic> diagnostics) =>
        status is BpmnModelerOperationStatus.Succeeded or BpmnModelerOperationStatus.Cancelled
            ? Task.CompletedTask
            : Notifications.Enqueue(new BpmnModelerOperationFailedEventArgs(
                operation, status, diagnostics));

    private static ImmutableArray<Diagnostic> UnavailableDiagnostics() =>
        [new("BPMN_MODELER_UNAVAILABLE", DiagnosticSeverity.Error,
            "The modeler is not ready for this operation.")];

    private static ImmutableArray<Diagnostic> FailedDiagnostics() =>
        [new("BPMN_MODELER_OPERATION_FAILED", DiagnosticSeverity.Error,
            "The modeler operation could not be completed.")];

    private static BpmnModelerOperationStatus Map(BpmnModelerOperationStatus status) =>
        status switch
        {
            BpmnModelerOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
            BpmnModelerOperationStatus.Rejected => BpmnModelerOperationStatus.Rejected,
            BpmnModelerOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
            BpmnModelerOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
            _ => BpmnModelerOperationStatus.Failed,
        };

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and
        not AccessViolationException;
}
