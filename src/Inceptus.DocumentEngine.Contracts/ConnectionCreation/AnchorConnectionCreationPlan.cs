using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Describes one immutable connection-creation Command and its created identities.
/// </summary>
public sealed class AnchorConnectionCreationPlan
{
    public AnchorConnectionCreationPlan(
        ICommand command,
        SemanticElementId createdSemanticRelationshipId,
        VisualStateId createdConnectorVisualStateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(createdSemanticRelationshipId);
        ArgumentNullException.ThrowIfNull(createdConnectorVisualStateId);

        Command = command;
        CreatedSemanticRelationshipId = createdSemanticRelationshipId;
        CreatedConnectorVisualStateId = createdConnectorVisualStateId;
    }

    public ICommand Command { get; }

    public SemanticElementId CreatedSemanticRelationshipId { get; }

    public VisualStateId CreatedConnectorVisualStateId { get; }
}
