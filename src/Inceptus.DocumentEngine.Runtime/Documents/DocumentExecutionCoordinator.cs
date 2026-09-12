using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Owns the exclusive execution gate and FIFO committed-event delivery queue for
/// one runtime <see cref="Document"/> instance.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The coordinator and its gate share the lifetime of the runtime Document, which has no disposal lifecycle.")]
internal sealed class DocumentExecutionCoordinator
{
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly Action _beginDrain;
    private readonly IDocumentDispatchScheduler _dispatchScheduler;
    private readonly object _queueSync = new();
    private DocumentDispatchWorkItem? _queueHead;
    private DocumentDispatchWorkItem? _queueTail;
    private bool _dispatchScheduled;
    private TaskCompletionSource _dispatchIdle = CreateCompletedCompletionSource();

    internal DocumentExecutionCoordinator(IDocumentDispatchScheduler? dispatchScheduler = null)
    {
        _dispatchScheduler = dispatchScheduler ?? ThreadPoolDocumentDispatchScheduler.Instance;
        _beginDrain = BeginDrain;
    }

    internal Task WaitForExecutionAsync(CancellationToken cancellationToken) =>
        _executionGate.WaitAsync(cancellationToken);

    internal void ReleaseExecution() => _executionGate.Release();

    /// <summary>
    /// Appends prepared work in commit order. The caller must hold the Document
    /// execution gate. The returned value elects that caller to start the drainer
    /// only after releasing the execution gate.
    /// </summary>
    internal bool Enqueue(DocumentDispatchWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        lock (_queueSync)
        {
            if (workItem.Next is not null)
            {
                throw new ArgumentException("The work item is already queued.", nameof(workItem));
            }

            var existingTail = _queueTail;
            var queueWasEmpty = existingTail is null;
            if (existingTail is null)
            {
                _queueHead = workItem;
                _queueTail = workItem;
            }
            else
            {
                existingTail.Next = workItem;
                _queueTail = workItem;
            }

            if (_dispatchScheduled)
            {
                return false;
            }

            _dispatchScheduled = true;
            if (queueWasEmpty)
            {
                _dispatchIdle = workItem.DispatchIdleCompletion;
            }

            return true;
        }
    }

    /// <summary>
    /// Starts delivery outside the Document execution gate. Only the caller elected
    /// by <see cref="Enqueue"/> may call this method for a queue-drain cycle.
    /// </summary>
    internal DocumentDispatchScheduleResult TryStartDispatch()
    {
#pragma warning disable CA1031 // Rejection is post-commit diagnostic state, never a rollback.
        try
        {
            if (_dispatchScheduler.TrySchedule(_beginDrain))
            {
                return DocumentDispatchScheduleResult.Scheduled;
            }

            MarkDispatchSchedulingRejected();
            return DocumentDispatchScheduleResult.Rejected(null);
        }
        catch (Exception exception)
        {
            MarkDispatchSchedulingRejected();
            return DocumentDispatchScheduleResult.Rejected(exception);
        }
#pragma warning restore CA1031
    }

    // Retained for focused coordinator tests; production orchestration uses the
    // result-returning method so a rejection becomes an observable diagnostic.
    internal void StartDispatch() => _ = TryStartDispatch();

    internal Task WaitForDispatchIdleAsync()
    {
        lock (_queueSync)
        {
            return _dispatchIdle.Task;
        }
    }

    private void BeginDrain() => _ = DrainAsync();

    private void MarkDispatchSchedulingRejected()
    {
        lock (_queueSync)
        {
            // No drainer was started, so the complete FIFO remains queued. Clearing
            // the election allows a later enqueue to retry scheduling the same head.
            _dispatchScheduled = false;
        }
    }

    private async Task DrainAsync()
    {
        var completedNormally = false;

        try
        {
            while (true)
            {
                DocumentDispatchWorkItem? workItem;
                TaskCompletionSource? completedDrain = null;

                lock (_queueSync)
                {
                    workItem = _queueHead;
                    if (workItem is null)
                    {
                        _queueTail = null;
                        _dispatchScheduled = false;
                        completedDrain = _dispatchIdle;
                    }
                    else
                    {
                        _queueHead = workItem.Next;
                        workItem.Next = null;
                        if (_queueHead is null)
                        {
                            _queueTail = null;
                        }
                    }
                }

                if (workItem is null)
                {
                    completedNormally = true;
                    completedDrain?.TrySetResult();
                    return;
                }

                await workItem.DispatchAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (!completedNormally)
            {
                RecoverFaultedDrain();
            }
        }
    }

    private void RecoverFaultedDrain()
    {
        var restart = false;
        TaskCompletionSource? completedDrain = null;

        lock (_queueSync)
        {
            if (_queueHead is null)
            {
                _queueTail = null;
                _dispatchScheduled = false;
                completedDrain = _dispatchIdle;
            }
            else
            {
                restart = true;
            }
        }

        completedDrain?.TrySetResult();
        if (restart)
        {
            _ = TryStartDispatch();
        }
    }

    private static TaskCompletionSource CreateCompletedCompletionSource()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
