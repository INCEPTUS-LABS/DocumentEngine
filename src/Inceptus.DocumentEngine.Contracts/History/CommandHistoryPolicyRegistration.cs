using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.History;

public sealed class CommandHistoryPolicyRegistration
{
    public CommandHistoryPolicyRegistration(
        CommandTypeId typeId,
        ICommandHistoryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(policy);
        TypeId = typeId;
        Policy = policy;
    }

    public CommandTypeId TypeId { get; }

    public ICommandHistoryPolicy Policy { get; }
}
