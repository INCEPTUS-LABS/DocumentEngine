using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.EditorState;

/// <summary>
/// Owns one transient Editor State snapshot and replaces it atomically.
/// </summary>
public sealed class EditorStateStore
{
    private Entry _entry;

    public EditorStateStore(EditorStateSnapshot? initialSnapshot = null)
    {
        var snapshot = initialSnapshot ?? EditorStateSnapshot.Empty;
        _entry = new(snapshot, snapshot.Selection);
    }

    public EditorStateSnapshot CaptureSnapshot() => Volatile.Read(ref _entry).Snapshot;

    internal (EditorStateSnapshot Snapshot, ImmutableArray<VisualStateId> SelectionOrder) CaptureOrderedSnapshot()
    {
        var entry = Volatile.Read(ref _entry);
        return (entry.Snapshot, entry.SelectionOrder);
    }

    public bool TryUpdate(
        EditorStateSnapshot expectedSnapshot,
        EditorStateSnapshot updatedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        ArgumentNullException.ThrowIfNull(updatedSnapshot);

        var current = Volatile.Read(ref _entry);
        if (!ReferenceEquals(current.Snapshot, expectedSnapshot)) return false;

        // Membership remains authoritative in the canonical snapshot. Retain chronology
        // for surviving members and append newly selected targets. A bulk addition has
        // no click order, so it uses the snapshot's deterministic canonical order.
        var order = expectedSnapshot.Selection.AsSpan().SequenceEqual(updatedSnapshot.Selection.AsSpan())
            ? current.SelectionOrder
            : current.SelectionOrder.Where(updatedSnapshot.Selection.Contains)
                .Concat(updatedSnapshot.Selection.Where(id => !expectedSnapshot.Selection.Contains(id)))
                .ToImmutableArray();
        var replacement = new Entry(updatedSnapshot, order);
        return ReferenceEquals(Interlocked.CompareExchange(ref _entry, replacement, current), current);
    }

    private sealed record Entry(EditorStateSnapshot Snapshot, ImmutableArray<VisualStateId> SelectionOrder);
}
