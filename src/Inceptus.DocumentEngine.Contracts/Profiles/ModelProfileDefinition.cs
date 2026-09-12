using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Profiles;

/// <summary>
/// Describes one optional model/view profile contributed by a notation plugin.
/// </summary>
public sealed record ModelProfileDefinition
{
    public ModelProfileDefinition(ModelProfileId id, string displayName, int order = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        DisplayName = displayName;
        Order = order;
    }

    public ModelProfileId Id { get; }

    public string DisplayName { get; }

    public int Order { get; }
}
