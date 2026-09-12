using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Organizational.Profiles;

/// <summary>
/// Stable identity and definition of the optional Organizational profile.
/// </summary>
public static class OrganizationalModelProfile
{
    // This value is persisted by the committed N10.0 model-profile contract.
    public static ModelProfileId Id { get; } = new("bpmn:profile:organizational");

    public static ModelProfileDefinition Definition { get; } =
        new(Id, "Organizational profile", order: 100);
}
