using Inceptus.DocumentEngine.Contracts.Visuals;

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
        : this(creationId, commandFactory, targetEligibility, true)
    {
    }

    public AnchorConnectionCreationRegistration(
        AnchorConnectionCreationId creationId,
        IAnchorConnectionCreationCommandFactory commandFactory,
        IAnchorConnectionTargetEligibility? targetEligibility,
        bool isImplicitSourceGesture)
        : this(creationId, commandFactory, targetEligibility, isImplicitSourceGesture, ConnectorVisualDescriptor.Default)
    {
    }

    public AnchorConnectionCreationRegistration(
        AnchorConnectionCreationId creationId, IAnchorConnectionCreationCommandFactory commandFactory,
        IAnchorConnectionTargetEligibility? targetEligibility, bool isImplicitSourceGesture,
        ConnectorVisualDescriptor visualDescriptor)
    {
        ArgumentNullException.ThrowIfNull(creationId);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(visualDescriptor);

        CreationId = creationId;
        CommandFactory = commandFactory;
        TargetEligibility = targetEligibility;
        IsImplicitSourceGesture = isImplicitSourceGesture;
        VisualDescriptor = visualDescriptor;
    }

    public AnchorConnectionCreationId CreationId { get; }

    public IAnchorConnectionCreationCommandFactory CommandFactory { get; }

    public IAnchorConnectionTargetEligibility? TargetEligibility { get; }

    public bool IsImplicitSourceGesture { get; }
    public ConnectorVisualDescriptor VisualDescriptor { get; }
}
