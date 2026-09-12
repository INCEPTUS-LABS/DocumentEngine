using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Describes one transient collapsed-state update. It is not a persistent Document command.
/// </summary>
public sealed record SemanticSceneViewActionPlan
{
    public SemanticSceneViewActionPlan(
        ModelProfileId profileId,
        SemanticElementId semanticElementId,
        bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ProfileId = profileId;
        SemanticElementId = semanticElementId;
        IsCollapsed = isCollapsed;
    }

    public ModelProfileId ProfileId { get; }

    public SemanticElementId SemanticElementId { get; }

    public bool IsCollapsed { get; }
}
