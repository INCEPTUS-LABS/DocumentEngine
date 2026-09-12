namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Orders callback invocation, not host-owned asynchronous callback completion. A consumer
/// can await a subsequent modeler operation from a callback without blocking this stream.
/// </summary>
internal sealed class BpmnModelerNotifications : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<object, Task> _invoke;
    private Task _pending = Task.CompletedTask;
    private bool _disposed;

    internal BpmnModelerNotifications(Func<object, Task> invoke) =>
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));

    internal Task Pending
    {
        get
        {
            lock (_sync)
            {
                return _pending;
            }
        }
    }

    internal Task Enqueue(object notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            return _pending = InvokeInOrderAsync(_pending, notification);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }

    private async Task InvokeInOrderAsync(Task previous, object notification)
    {
        // Never invoke consumer code in the enqueueing operation or under this lock.
        await Task.Yield();
        await previous.ConfigureAwait(false);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        // The component dispatcher also checks disposal at the point of invocation.
        await _invoke(notification).ConfigureAwait(false);
    }
}
