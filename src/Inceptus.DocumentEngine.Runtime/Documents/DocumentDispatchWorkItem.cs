namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Represents dispatch work fully prepared before a Document commit. Queueing the
/// item therefore requires only linked-list pointer updates under the queue lock.
/// </summary>
internal sealed class DocumentDispatchWorkItem
{
    private readonly Func<ValueTask> _dispatchAsync;
    private readonly Action<Exception>? _reportFailure;
    private readonly TaskCompletionSource _executionGateReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DocumentDispatchWorkItem(
        Func<ValueTask> dispatchAsync,
        Action<Exception>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(dispatchAsync);
        _dispatchAsync = dispatchAsync;
        _reportFailure = reportFailure;
    }

    internal TaskCompletionSource DispatchIdleCompletion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DocumentDispatchWorkItem? Next { get; set; }

    internal void ReleaseForDispatch() => _executionGateReleased.TrySetResult();

    internal async ValueTask DispatchAsync()
    {
        await _executionGateReleased.Task.ConfigureAwait(false);

        try
        {
            await _dispatchAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Every subscriber fault is isolated from later FIFO work.
        catch (Exception exception)
        {
            try
            {
                _reportFailure?.Invoke(exception);
            }
            catch (Exception)
            {
                // Reporting is diagnostic-only and must never stop the FIFO drain.
            }
        }
#pragma warning restore CA1031
    }
}
