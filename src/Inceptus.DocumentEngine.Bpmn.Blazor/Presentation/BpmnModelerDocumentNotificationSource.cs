using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Retains the exact committed snapshots for one modeler session without running
/// presentation work or consumer callbacks in the Document dispatch queue.
/// </summary>
internal sealed class BpmnModelerDocumentNotificationSource : IDocumentChangedSubscriber, IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<DocumentSnapshot> _pending = new();
    private readonly Action? _notificationReceived;
    private TaskCompletionSource _receiptPulse = CreateReceiptPulse();
    private DocumentRevision _lastReceivedRevision;
    private bool _disposed;

    internal BpmnModelerDocumentNotificationSource(
        DocumentSnapshot initialSnapshot,
        Action? notificationReceived = null)
    {
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        InitialSnapshot = initialSnapshot;
        _lastReceivedRevision = initialSnapshot.Revision;
        _notificationReceived = notificationReceived;
    }

    internal DocumentSnapshot InitialSnapshot { get; }

    internal DocumentRevision LastReceivedRevision
    {
        get
        {
            lock (_sync)
            {
                return _lastReceivedRevision;
            }
        }
    }

    public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        TaskCompletionSource receiptPulse;
        lock (_sync)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (change.DocumentId != InitialSnapshot.DocumentId)
            {
                throw new ArgumentException(
                    "The committed notification must belong to this modeler session's Document.",
                    nameof(change));
            }

            if (change.CommittedRevision <= _lastReceivedRevision)
            {
                return ValueTask.CompletedTask;
            }

            _pending.Enqueue(change.CommittedSnapshot);
            _lastReceivedRevision = change.CommittedRevision;
            receiptPulse = _receiptPulse;
            _receiptPulse = CreateReceiptPulse();
        }

        receiptPulse.TrySetResult();
        _notificationReceived?.Invoke();
        return ValueTask.CompletedTask;
    }

    internal async ValueTask WaitForReceiptAsync(
        DocumentRevision revision,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task receipt;
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new OperationCanceledException(
                        "The modeler Document notification source has been disposed.");
                }

                if (_lastReceivedRevision >= revision)
                {
                    return;
                }

                receipt = _receiptPulse.Task;
            }

            await receipt.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal ImmutableArray<DocumentSnapshot> TakePendingThrough(DocumentRevision revision)
    {
        lock (_sync)
        {
            var snapshots = ImmutableArray.CreateBuilder<DocumentSnapshot>();
            while (_pending.TryPeek(out var snapshot) && snapshot.Revision <= revision)
            {
                snapshots.Add(_pending.Dequeue());
            }

            return snapshots.ToImmutable();
        }
    }

    public void Dispose()
    {
        TaskCompletionSource receiptPulse;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
            receiptPulse = _receiptPulse;
        }

        receiptPulse.TrySetCanceled();
    }

    private static TaskCompletionSource CreateReceiptPulse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
