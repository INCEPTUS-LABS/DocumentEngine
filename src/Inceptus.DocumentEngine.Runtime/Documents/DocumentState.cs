using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal sealed class DocumentState
{
    public DocumentState(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    public DocumentSnapshot Snapshot { get; }
}
