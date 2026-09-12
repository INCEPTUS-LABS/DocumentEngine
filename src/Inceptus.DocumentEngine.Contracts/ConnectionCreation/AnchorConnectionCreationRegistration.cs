namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Associates one creation capability with its notation-owned Command factory.
/// </summary>
public sealed class AnchorConnectionCreationRegistration
{
    public AnchorConnectionCreationRegistration(
        AnchorConnectionCreationId creationId,
        IAnchorConnectionCreationCommandFactory commandFactory,
        IAnchorConnectionTargetEligibility? targetEligibility = null)
    {
        ArgumentNullException.ThrowIfNull(creationId);
        ArgumentNullException.ThrowIfNull(commandFactory);

        CreationId = creationId;
        CommandFactory = commandFactory;
        TargetEligibility = targetEligibility;
    }

    public AnchorConnectionCreationId CreationId { get; }

    public IAnchorConnectionCreationCommandFactory CommandFactory { get; }

    public IAnchorConnectionTargetEligibility? TargetEligibility { get; }
}
