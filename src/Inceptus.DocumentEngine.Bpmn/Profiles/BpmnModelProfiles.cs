using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Bpmn.Profiles;

/// <summary>
/// Stable optional profile identities and definitions contributed by the BPMN plugin.
/// </summary>
public static class BpmnModelProfiles
{
    public static ModelProfileId OrganizationalId { get; } =
        new("bpmn:profile:organizational");

    public static ModelProfileId StageId { get; } =
        new("bpmn:profile:stage");

    public static ModelProfileDefinition Organizational { get; } =
        new(OrganizationalId, "Organizational profile", order: 100);

    public static ModelProfileDefinition Stage { get; } =
        new(StageId, "Stage profile", order: 200);

    public static ImmutableArray<ModelProfileDefinition> Definitions { get; } =
        [Organizational, Stage];
}
