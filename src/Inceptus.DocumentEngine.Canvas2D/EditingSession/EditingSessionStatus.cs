namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public enum EditingSessionStatus
{
    Ready,
    Rebuilding,
    RuntimeFaulted,
}

public enum EditingSessionAttachStatus
{
    Ready,
    RuntimeFaulted,
    Failed,
    Cancelled,
}

public enum EditingSessionOperationStatus
{
    Succeeded,
    Superseded,
    Failed,
    Rejected,
    Cancelled,
    Closed,
}

public readonly record struct EditingSessionGeneration
{
    internal EditingSessionGeneration(long value) => Value = value;

    public long Value { get; }
}
