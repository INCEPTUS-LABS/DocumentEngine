using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

internal static class EditingSessionTestSynchronization
{
    internal static async Task WaitForCommittedEventAndSessionIdleAsync(
        Document document,
        EditingSession session)
    {
        // Commands enqueue events without awaiting subscribers. Drain delivery before
        // settling all session pipeline work triggered by those events.
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
