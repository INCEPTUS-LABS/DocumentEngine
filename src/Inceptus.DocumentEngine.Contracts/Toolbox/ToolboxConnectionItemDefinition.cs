using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>Display metadata for selecting an existing connection-creation capability.</summary>
public sealed class ToolboxConnectionItemDefinition : IEquatable<ToolboxConnectionItemDefinition>
{
    public ToolboxConnectionItemDefinition(ToolboxItemId itemId, AnchorConnectionCreationId creationId,
        ToolboxGroupId groupId, string displayName, int order, ToolboxIconDescriptor icon)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(creationId);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentNullException.ThrowIfNull(icon);
        ItemId = itemId;
        CreationId = creationId;
        GroupId = groupId;
        DisplayName = displayName;
        Order = order;
        Icon = icon;
    }

    public ToolboxItemId ItemId { get; }
    public AnchorConnectionCreationId CreationId { get; }
    public ToolboxGroupId GroupId { get; }
    public string DisplayName { get; }
    public int Order { get; }
    public ToolboxIconDescriptor Icon { get; }

    public bool Equals(ToolboxConnectionItemDefinition? other) => ReferenceEquals(this, other) || other is not null &&
        ItemId == other.ItemId && CreationId == other.CreationId && GroupId == other.GroupId &&
        StringComparer.Ordinal.Equals(DisplayName, other.DisplayName) && Order == other.Order && Icon.Equals(other.Icon);
    public override bool Equals(object? obj) => Equals(obj as ToolboxConnectionItemDefinition);
    public override int GetHashCode() => HashCode.Combine(ItemId, CreationId, GroupId,
        StringComparer.Ordinal.GetHashCode(DisplayName), Order, Icon);
}
