using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class HistoryState
{
    internal HistoryState(
        ImmutableArray<HistoryEntry> entries,
        int cursor,
        ulong version)
    {
        if (entries.IsDefault)
        {
            throw new ArgumentException("History entries must be initialized.", nameof(entries));
        }

        if (cursor < 0 || cursor > entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(cursor));
        }

        Entries = entries;
        Cursor = cursor;
        Version = version;
    }

    internal ImmutableArray<HistoryEntry> Entries { get; }

    internal int Cursor { get; }

    internal ulong Version { get; }
}
