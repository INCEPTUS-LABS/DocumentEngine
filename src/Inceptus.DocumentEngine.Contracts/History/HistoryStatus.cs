namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Immutable, entry-free status of one Editing Session's linear History.
/// </summary>
public sealed class HistoryStatus : IEquatable<HistoryStatus>
{
    public HistoryStatus(int entryCount, bool canUndo, bool canRedo)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        if ((entryCount == 0 && (canUndo || canRedo)) ||
            (entryCount > 0 && !canUndo && !canRedo) ||
            (entryCount == 1 && canUndo && canRedo))
        {
            throw new ArgumentException(
                "History availability must be coherent with the entry count.",
                nameof(entryCount));
        }

        EntryCount = entryCount;
        CanUndo = canUndo;
        CanRedo = canRedo;
    }

    public int EntryCount { get; }

    public bool CanUndo { get; }

    public bool CanRedo { get; }

    public bool Equals(HistoryStatus? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        EntryCount == other.EntryCount &&
        CanUndo == other.CanUndo &&
        CanRedo == other.CanRedo;

    public override bool Equals(object? obj) => Equals(obj as HistoryStatus);

    public override int GetHashCode() => HashCode.Combine(EntryCount, CanUndo, CanRedo);
}
