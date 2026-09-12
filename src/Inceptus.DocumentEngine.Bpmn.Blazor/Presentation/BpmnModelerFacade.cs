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
internal sealed class BpmnModelerFacade : IDisposable
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

    internal ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default) =>
        ExecuteDocumentOperationAsync(BpmnModelerOperation.Import, async (host, token) =>
        {
            var result = await host.ImportNativeDocumentAsync(utf8Json, token).ConfigureAwait(false);
            return new BpmnModelerDocumentResult(
                Map(result.Status), result.Snapshot, result.Diagnostics);
        }, cancellationToken);

    internal ValueTask<BpmnModelerFileResult> ExportNativeDocumentAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteFileOperationAsync(BpmnModelerOperation.Export, async (host, token) =>
        {
            var result = await host.ExportNativeDocumentAsync(token).ConfigureAwait(false);
            return new BpmnModelerFileResult(
                Map(result.Status),
                result.Succeeded
                    ? new BpmnModelerFileArtifact(
                        DocumentCanvas.NativeDocumentFileName,
                        DocumentCanvas.NativeDocumentContentType,
                        result.Payload)
                    : null,
                result.Diagnostics);
        }, cancellationToken);

    internal ValueTask<BpmnModelerFileResult> PublishAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteFileOperationAsync(BpmnModelerOperation.Publish, async (host, token) =>
        {
            var result = await host.PublishProcessAsync(token).ConfigureAwait(false);
            return new BpmnModelerFileResult(
                Map(result.Status),
                result.Succeeded
                    ? new BpmnModelerFileArtifact(
                        DocumentCanvas.PublishFileName,
                        DocumentCanvas.PublishContentType,
                        result.Payload)
                    : null,
                result.Diagnostics);
        }, cancellationToken);

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
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

    private async ValueTask<BpmnModelerFileResult> ExecuteFileOperationAsync(
        BpmnModelerOperation operation,
        Func<DocumentCanvasHost, CancellationToken, ValueTask<BpmnModelerFileResult>> execute,
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

    private static BpmnModelerOperationStatus Map(NativeDocumentHostOperationStatus status) =>
        status switch
        {
            NativeDocumentHostOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
            NativeDocumentHostOperationStatus.Rejected => BpmnModelerOperationStatus.Rejected,
            NativeDocumentHostOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
            NativeDocumentHostOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
            _ => BpmnModelerOperationStatus.Failed,
        };

    private static BpmnModelerOperationStatus Map(PublishedProcessHostOperationStatus status) =>
        status switch
        {
            PublishedProcessHostOperationStatus.Succeeded => BpmnModelerOperationStatus.Succeeded,
            PublishedProcessHostOperationStatus.Rejected => BpmnModelerOperationStatus.Rejected,
            PublishedProcessHostOperationStatus.Unavailable => BpmnModelerOperationStatus.Unavailable,
            PublishedProcessHostOperationStatus.Cancelled => BpmnModelerOperationStatus.Cancelled,
            _ => BpmnModelerOperationStatus.Failed,
        };

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and
        not AccessViolationException;
}
