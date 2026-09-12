namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Matches source anchors and creates notation-owned immutable connection Commands.
/// </summary>
public interface IAnchorConnectionCreationCommandFactory
{
    bool CanStart(AnchorConnectionCreationSourceRequest request);

    AnchorConnectionCreationPlanResult CreatePlan(AnchorConnectionCreationRequest request);
}
