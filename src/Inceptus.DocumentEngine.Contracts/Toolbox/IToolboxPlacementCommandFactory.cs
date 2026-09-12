namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Creates a notation-owned immutable Command plan without executing persistent work.
/// </summary>
public interface IToolboxPlacementCommandFactory
{
    ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request);
}
