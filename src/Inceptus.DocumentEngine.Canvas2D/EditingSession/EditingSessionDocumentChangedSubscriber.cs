using Inceptus.DocumentEngine.Contracts.Commands;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal sealed class EditingSessionDocumentChangedSubscriber : IDocumentChangedSubscriber
{
    private readonly object _sync;
    private EditingSession? _session;

    internal EditingSessionDocumentChangedSubscriber(EditingSession session, object notificationGate)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(notificationGate);
        // Observation and retirement share the session's notification gate. A separate
        // subscriber lock would invert CloseAsync's notification -> detach lock order.
        _sync = notificationGate;
        _session = session;
    }

    public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
    {
        lock (_sync)
        {
            _session?.ObserveDocumentChanged(change);
        }

        return ValueTask.CompletedTask;
    }

    internal void Detach()
    {
        lock (_sync)
        {
            _session = null;
        }
    }
}
