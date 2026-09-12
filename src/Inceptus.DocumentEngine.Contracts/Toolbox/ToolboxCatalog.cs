using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Provides the immutable, validated, and deterministically ordered Toolbox catalog.
/// </summary>
public sealed class ToolboxCatalog
{
    private readonly ImmutableDictionary<ToolboxSectionId, ImmutableArray<ToolboxGroupDefinition>>
        _groupsBySection;
    private readonly ImmutableDictionary<ToolboxGroupId, ImmutableArray<ToolboxItemDefinition>>
        _itemsByGroup;
    private readonly ImmutableDictionary<ToolboxItemId, ToolboxItemDefinition> _itemsById;

    public ToolboxCatalog(IEnumerable<ToolboxContribution>? contributions = null)
    {
        var contributionCopy = contributions?.ToArray() ?? [];
        if (Array.Exists(contributionCopy, static contribution => contribution is null))
        {
            throw new ArgumentException(
                "Toolbox catalog contributions cannot contain null values.",
                nameof(contributions));
        }

        var sections = contributionCopy
            .SelectMany(static contribution => contribution.Sections)
            .ToArray();
        var groups = contributionCopy
            .SelectMany(static contribution => contribution.Groups)
            .ToArray();
        var items = contributionCopy
            .SelectMany(static contribution => contribution.Items)
            .ToArray();

        RejectDuplicateSectionIds(sections, nameof(contributions));
        RejectDuplicateGroupIds(groups, nameof(contributions));
        RejectDuplicateItemIds(items, nameof(contributions));

        Sections = sections
            .OrderBy(static section => section.Order)
            .ThenBy(static section => section.SectionId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var sectionRanks = Sections
            .Select(static (section, index) => new KeyValuePair<ToolboxSectionId, int>(
                section.SectionId,
                index))
            .ToImmutableDictionary();
        RejectUnknownSectionReferences(groups, sectionRanks, nameof(contributions));

        Groups = groups
            .OrderBy(group => group.SectionId is null ? -1 : sectionRanks[group.SectionId])
            .ThenBy(static group => group.Order)
            .ThenBy(static group => group.GroupId.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var groupRanks = Groups
            .Select(static (group, index) => new KeyValuePair<ToolboxGroupId, int>(
                group.GroupId,
                index))
            .ToImmutableDictionary();
        RejectUnknownGroupReferences(items, groupRanks, nameof(contributions));

        Items = items
            .OrderBy(item => groupRanks[item.GroupId])
            .ThenBy(static item => item.Order)
            .ThenBy(static item => item.ItemId.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        _groupsBySection = Sections.ToImmutableDictionary(
            static section => section.SectionId,
            section => Groups
                .Where(group => group.SectionId == section.SectionId)
                .ToImmutableArray());
        _itemsByGroup = Groups.ToImmutableDictionary(
            static group => group.GroupId,
            group => Items
                .Where(item => item.GroupId == group.GroupId)
                .ToImmutableArray());
        _itemsById = Items.ToImmutableDictionary(static item => item.ItemId);
    }

    public static ToolboxCatalog Empty { get; } = new();

    public ImmutableArray<ToolboxSectionDefinition> Sections { get; }

    public ImmutableArray<ToolboxGroupDefinition> Groups { get; }

    public ImmutableArray<ToolboxItemDefinition> Items { get; }

    public ImmutableArray<ToolboxGroupDefinition> GetGroups(ToolboxSectionId sectionId)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        return _groupsBySection.TryGetValue(sectionId, out var groups) ? groups : [];
    }

    public ImmutableArray<ToolboxItemDefinition> GetItems(ToolboxGroupId groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return _itemsByGroup.TryGetValue(groupId, out var items) ? items : [];
    }

    public bool TryGetItem(
        ToolboxItemId itemId,
        [NotNullWhen(true)] out ToolboxItemDefinition? item)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return _itemsById.TryGetValue(itemId, out item);
    }

    private static void RejectDuplicateSectionIds(
        ToolboxSectionDefinition[] sections,
        string parameterName)
    {
        var ordered = sections
            .OrderBy(static section => section.SectionId.Value, StringComparer.Ordinal)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].SectionId != ordered[index].SectionId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Toolbox section ID '{ordered[index].SectionId}' is registered more than once.",
                parameterName);
        }
    }

    private static void RejectDuplicateGroupIds(
        ToolboxGroupDefinition[] groups,
        string parameterName)
    {
        var ordered = groups
            .OrderBy(static group => group.GroupId.Value, StringComparer.Ordinal)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].GroupId != ordered[index].GroupId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Toolbox group ID '{ordered[index].GroupId}' is registered more than once.",
                parameterName);
        }
    }

    private static void RejectDuplicateItemIds(
        ToolboxItemDefinition[] items,
        string parameterName)
    {
        var ordered = items
            .OrderBy(static item => item.ItemId.Value, StringComparer.Ordinal)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].ItemId != ordered[index].ItemId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Toolbox item ID '{ordered[index].ItemId}' is registered more than once.",
                parameterName);
        }
    }

    private static void RejectUnknownSectionReferences(
        IEnumerable<ToolboxGroupDefinition> groups,
        ImmutableDictionary<ToolboxSectionId, int> sectionRanks,
        string parameterName)
    {
        foreach (var group in groups)
        {
            if (group.SectionId is null || sectionRanks.ContainsKey(group.SectionId))
            {
                continue;
            }

            throw new ArgumentException(
                $"Toolbox group '{group.GroupId}' references unknown section '{group.SectionId}'.",
                parameterName);
        }
    }

    private static void RejectUnknownGroupReferences(
        IEnumerable<ToolboxItemDefinition> items,
        ImmutableDictionary<ToolboxGroupId, int> groupRanks,
        string parameterName)
    {
        foreach (var item in items)
        {
            if (groupRanks.ContainsKey(item.GroupId))
            {
                continue;
            }

            throw new ArgumentException(
                $"Toolbox item '{item.ItemId}' references unknown group '{item.GroupId}'.",
                parameterName);
        }
    }
}
