namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Associates one diagram deletion capability with its notation-owned Command factory.
/// </summary>
public sealed class DiagramDeletionRegistration
{
    public DiagramDeletionRegistration(
        DiagramDeletionId deletionId,
        IDiagramDeletionCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(deletionId);
        ArgumentNullException.ThrowIfNull(commandFactory);
        DeletionId = deletionId;
        CommandFactory = commandFactory;
    }

    public DiagramDeletionId DeletionId { get; }

    public IDiagramDeletionCommandFactory CommandFactory { get; }
}
