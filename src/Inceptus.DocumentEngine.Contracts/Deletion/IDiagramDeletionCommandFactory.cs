namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Matches current diagram targets and creates notation-owned immutable deletion Commands.
/// </summary>
public interface IDiagramDeletionCommandFactory
{
    bool CanDelete(DiagramDeletionRequest request);

    DiagramDeletionPlanResult CreatePlan(DiagramDeletionRequest request);
}
