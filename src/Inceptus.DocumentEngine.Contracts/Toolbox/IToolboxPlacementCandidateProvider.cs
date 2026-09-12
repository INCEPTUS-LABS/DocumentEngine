namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Resolves an optional visible target and transient preview for a Toolbox pointer location.
/// </summary>
public interface IToolboxPlacementCandidateProvider
{
    ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request);
}
