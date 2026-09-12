using Inceptus.DocumentEngine.Contracts.EditorState;

namespace Inceptus.DocumentEngine.Runtime.EditorState;

/// <summary>
/// Owns one transient Editor State snapshot and replaces it atomically.
/// </summary>
public sealed class EditorStateStore
{
    private EditorStateSnapshot _snapshot;

    public EditorStateStore(EditorStateSnapshot? initialSnapshot = null) =>
        _snapshot = initialSnapshot ?? EditorStateSnapshot.Empty;

    public EditorStateSnapshot CaptureSnapshot() => Volatile.Read(ref _snapshot);

    public bool TryUpdate(
        EditorStateSnapshot expectedSnapshot,
        EditorStateSnapshot updatedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        ArgumentNullException.ThrowIfNull(updatedSnapshot);

        return ReferenceEquals(
            Interlocked.CompareExchange(ref _snapshot, updatedSnapshot, expectedSnapshot),
            expectedSnapshot);
    }
}
