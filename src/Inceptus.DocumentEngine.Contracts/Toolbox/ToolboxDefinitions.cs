using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Supplies plugin-neutral icon metadata for one Toolbox item.
/// </summary>
public sealed class ToolboxIconDescriptor : IEquatable<ToolboxIconDescriptor>
{
    public ToolboxIconDescriptor(string iconKey, string? fallbackGlyph = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);
        if (fallbackGlyph is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fallbackGlyph);
        }

        IconKey = iconKey;
        FallbackGlyph = fallbackGlyph;
    }

    public string IconKey { get; }

    public string? FallbackGlyph { get; }

    public bool Equals(ToolboxIconDescriptor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(IconKey, other.IconKey) &&
        StringComparer.Ordinal.Equals(FallbackGlyph, other.FallbackGlyph);

    public override bool Equals(object? obj) => Equals(obj as ToolboxIconDescriptor);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(IconKey),
        FallbackGlyph is null ? 0 : StringComparer.Ordinal.GetHashCode(FallbackGlyph));
}

/// <summary>
/// Immutable display metadata for one non-recursive Toolbox section.
/// </summary>
public sealed class ToolboxSectionDefinition : IEquatable<ToolboxSectionDefinition>
{
    public ToolboxSectionDefinition(
        ToolboxSectionId sectionId,
        string displayName,
        int order)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfNegative(order);

        SectionId = sectionId;
        DisplayName = displayName;
        Order = order;
    }

    public ToolboxSectionId SectionId { get; }

    public string DisplayName { get; }

    public int Order { get; }

    public bool Equals(ToolboxSectionDefinition? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        SectionId == other.SectionId &&
        StringComparer.Ordinal.Equals(DisplayName, other.DisplayName) &&
        Order == other.Order;

    public override bool Equals(object? obj) => Equals(obj as ToolboxSectionDefinition);

    public override int GetHashCode() => HashCode.Combine(
        SectionId,
        StringComparer.Ordinal.GetHashCode(DisplayName),
        Order);
}

/// <summary>
/// Immutable display metadata for one contributed Toolbox group.
/// </summary>
public sealed class ToolboxGroupDefinition : IEquatable<ToolboxGroupDefinition>
{
    public ToolboxGroupDefinition(
        ToolboxGroupId groupId,
        string displayName,
        int order,
        ToolboxSectionId? sectionId = null,
        ToolboxIconDescriptor? icon = null)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfNegative(order);

        GroupId = groupId;
        DisplayName = displayName;
        Order = order;
        SectionId = sectionId;
        Icon = icon;
    }

    public ToolboxGroupId GroupId { get; }

    public string DisplayName { get; }

    public int Order { get; }

    public ToolboxSectionId? SectionId { get; }

    public ToolboxIconDescriptor? Icon { get; }

    public bool Equals(ToolboxGroupDefinition? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        GroupId == other.GroupId &&
        StringComparer.Ordinal.Equals(DisplayName, other.DisplayName) &&
        Order == other.Order &&
        SectionId == other.SectionId &&
        Equals(Icon, other.Icon);

    public override bool Equals(object? obj) => Equals(obj as ToolboxGroupDefinition);

    public override int GetHashCode() => HashCode.Combine(
        GroupId,
        StringComparer.Ordinal.GetHashCode(DisplayName),
        Order,
        SectionId,
        Icon);
}

/// <summary>
/// Immutable, data-only metadata for one contributed Toolbox creation option.
/// </summary>
public sealed class ToolboxItemDefinition : IEquatable<ToolboxItemDefinition>
{
    public ToolboxItemDefinition(
        ToolboxItemId itemId,
        SemanticTypeId elementTypeId,
        ToolboxGroupId groupId,
        string displayName,
        int order,
        ToolboxIconDescriptor icon)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(elementTypeId);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentNullException.ThrowIfNull(icon);

        ItemId = itemId;
        ElementTypeId = elementTypeId;
        GroupId = groupId;
        DisplayName = displayName;
        Order = order;
        Icon = icon;
    }

    public ToolboxItemId ItemId { get; }

    public SemanticTypeId ElementTypeId { get; }

    public ToolboxGroupId GroupId { get; }

    public string DisplayName { get; }

    public int Order { get; }

    public ToolboxIconDescriptor Icon { get; }

    public bool Equals(ToolboxItemDefinition? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ItemId == other.ItemId &&
        ElementTypeId == other.ElementTypeId &&
        GroupId == other.GroupId &&
        StringComparer.Ordinal.Equals(DisplayName, other.DisplayName) &&
        Order == other.Order &&
        Icon.Equals(other.Icon);

    public override bool Equals(object? obj) => Equals(obj as ToolboxItemDefinition);

    public override int GetHashCode() => HashCode.Combine(
        ItemId,
        ElementTypeId,
        GroupId,
        StringComparer.Ordinal.GetHashCode(DisplayName),
        Order,
        Icon);
}
