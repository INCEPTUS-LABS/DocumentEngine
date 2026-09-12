namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Internal scheduling seam used to verify post-commit scheduling rejection without
/// exposing dispatch infrastructure through the public Runtime API.
/// </summary>
internal interface IDocumentDispatchScheduler
{
    /// <summary>
    /// Attempts to queue <paramref name="callback"/> for later execution. An
    /// implementation must never invoke the callback inline.
    /// </summary>
    bool TrySchedule(Action callback);
}

internal sealed class ThreadPoolDocumentDispatchScheduler : IDocumentDispatchScheduler
{
    internal static ThreadPoolDocumentDispatchScheduler Instance { get; } = new();

    private ThreadPoolDocumentDispatchScheduler()
    {
    }

    public bool TrySchedule(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ThreadPool.UnsafeQueueUserWorkItem(
            static scheduledCallback => scheduledCallback(),
            callback,
            preferLocal: false);
    }
}

internal readonly record struct DocumentDispatchScheduleResult(
    bool IsScheduled,
    Exception? Failure)
{
    internal static DocumentDispatchScheduleResult Scheduled => new(true, null);

    internal static DocumentDispatchScheduleResult Rejected(Exception? failure) =>
        new(false, failure);
}
