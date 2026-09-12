using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Profiles;

/// <summary>
/// One sparse, persistent profile assignment. Its container is a semantic identity,
/// not a second scope or visual identity; profile policies define eligible endpoints.
/// </summary>
public sealed record ModelProfileElementAssignmentSnapshot
{
    public ModelProfileElementAssignmentSnapshot(
        ModelProfileId profileId,
        SemanticElementId semanticElementId,
        SemanticElementId containerSemanticElementId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(containerSemanticElementId);
        ProfileId = profileId;
        SemanticElementId = semanticElementId;
        ContainerSemanticElementId = containerSemanticElementId;
    }

    public ModelProfileId ProfileId { get; }

    public SemanticElementId SemanticElementId { get; }

    public SemanticElementId ContainerSemanticElementId { get; }
}
