using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Toolbox;

public sealed class ToolboxCatalogTests
{
    private static readonly SemanticTypeId SharedTypeId = new("test:toolbox:type:shared");

    [Fact]
    public void CatalogComposesIndependentContributionsInCanonicalOrder()
    {
        var groupA = Group("group:a", 1, "Group A");
        var groupB = Group("group:b", 0, "Group B");
        var groupC = Group("group:c", 1, "Group C");
        var itemA2 = Item("item:a:2", groupA.GroupId, 0, "A2");
        var itemA1 = Item("item:a:1", groupA.GroupId, 0, "A1");
        var itemB = Item("item:b", groupB.GroupId, 4, "B");
        var itemC = Item("item:c", groupC.GroupId, 0, "C");
        var contributionA = new ToolboxContribution(
            [groupC, groupA],
            [itemC, itemA2, itemA1]);
        var contributionB = new ToolboxContribution([groupB], [itemB]);

        var forward = new ToolboxCatalog([contributionA, contributionB]);
        var reverse = new ToolboxCatalog([contributionB, contributionA]);

        Assert.Equal(
            ["group:b", "group:a", "group:c"],
            forward.Groups.Select(static group => group.GroupId.Value));
        Assert.Equal(
            ["item:b", "item:a:1", "item:a:2", "item:c"],
            forward.Items.Select(static item => item.ItemId.Value));
        Assert.Equal(
            forward.Groups.Select(static group => group.GroupId),
            reverse.Groups.Select(static group => group.GroupId));
        Assert.Equal(
            forward.Items.Select(static item => item.ItemId),
            reverse.Items.Select(static item => item.ItemId));
        Assert.Equal(
            ["item:a:1", "item:a:2"],
            forward.GetItems(groupA.GroupId).Select(static item => item.ItemId.Value));
    }

    [Fact]
    public void CatalogPreservesMultipleItemsForTheSameSemanticType()
    {
        var group = Group("group:variants", 0, "Variants");
        var first = Item("item:first", group.GroupId, 0, "First", SharedTypeId);
        var second = Item("item:second", group.GroupId, 1, "Second", SharedTypeId);

        var catalog = new ToolboxCatalog(
        [
            new ToolboxContribution([group], [first, second]),
        ]);

        Assert.Equal(2, catalog.Items.Length);
        Assert.All(catalog.Items, item => Assert.Equal(SharedTypeId, item.ElementTypeId));
        Assert.True(catalog.TryGetItem(first.ItemId, out var resolved));
        Assert.Same(first, resolved);
        Assert.False(catalog.TryGetItem(new ToolboxItemId("item:missing"), out resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void CatalogOrdersSectionsAndTheirGroupsWithoutIntroducingRecursion()
    {
        var firstSection = new ToolboxSectionDefinition(
            new ToolboxSectionId("section:first"),
            "First",
            0);
        var secondSection = new ToolboxSectionDefinition(
            new ToolboxSectionId("section:second"),
            "Second",
            1);
        var secondGroup = new ToolboxGroupDefinition(
            new ToolboxGroupId("group:second"),
            "Second group",
            0,
            secondSection.SectionId,
            new ToolboxIconDescriptor("group:second", "◇"));
        var firstGroupB = new ToolboxGroupDefinition(
            new ToolboxGroupId("group:first:b"),
            "First B",
            1,
            firstSection.SectionId,
            new ToolboxIconDescriptor("group:first:b", "▭"));
        var firstGroupA = new ToolboxGroupDefinition(
            new ToolboxGroupId("group:first:a"),
            "First A",
            0,
            firstSection.SectionId,
            new ToolboxIconDescriptor("group:first:a", "◎"));

        var catalog = new ToolboxCatalog(
        [
            new ToolboxContribution(
                [secondGroup, firstGroupB, firstGroupA],
                sections: [secondSection, firstSection]),
        ]);

        Assert.Equal([firstSection, secondSection], catalog.Sections.ToArray());
        Assert.Equal(
            [firstGroupA, firstGroupB],
            catalog.GetGroups(firstSection.SectionId).ToArray());
        Assert.Equal([secondGroup], catalog.GetGroups(secondSection.SectionId).ToArray());
        Assert.Empty(catalog.GetGroups(new ToolboxSectionId("section:missing")));
        Assert.Equal("group:first:a", catalog.Groups[0].GroupId.Value);
        Assert.Equal("group:second", catalog.Groups[^1].GroupId.Value);
        Assert.Equal("group:first:a", firstGroupA.Icon?.IconKey);
    }

    [Fact]
    public void CatalogRejectsDuplicateGroupAndItemIdsAcrossContributions()
    {
        var section = new ToolboxSectionDefinition(
            new ToolboxSectionId("section:duplicate"),
            "Original",
            0);
        var duplicateSection = new ToolboxSectionDefinition(
            section.SectionId,
            "Duplicate",
            1);
        var group = Group("group:duplicate", 0, "Original");
        var duplicateGroup = Group("group:duplicate", 1, "Duplicate");
        var otherGroup = Group("group:other", 1, "Other");
        var item = Item("item:duplicate", group.GroupId, 0, "Original");
        var duplicateItem = Item("item:duplicate", otherGroup.GroupId, 1, "Duplicate");

        Assert.Throws<ArgumentException>(() => new ToolboxCatalog(
        [
            new ToolboxContribution(sections: [section]),
            new ToolboxContribution(sections: [duplicateSection]),
        ]));
        Assert.Throws<ArgumentException>(() => new ToolboxCatalog(
        [
            new ToolboxContribution([group]),
            new ToolboxContribution([duplicateGroup]),
        ]));
        Assert.Throws<ArgumentException>(() => new ToolboxCatalog(
        [
            new ToolboxContribution([group], [item]),
            new ToolboxContribution([otherGroup], [duplicateItem]),
        ]));
    }

    [Fact]
    public void CatalogRejectsUnknownGroupsAndNullContributionEntries()
    {
        var unknownSection = new ToolboxSectionId("section:unknown");
        var orphanGroup = new ToolboxGroupDefinition(
            new ToolboxGroupId("group:orphan"),
            "Orphan",
            0,
            unknownSection);
        var unknown = new ToolboxGroupId("group:unknown");
        var item = Item("item:orphan", unknown, 0, "Orphan");

        Assert.Throws<ArgumentException>(() => new ToolboxCatalog(
        [
            new ToolboxContribution([orphanGroup]),
        ]));
        Assert.Throws<ArgumentException>(() => new ToolboxCatalog(
        [
            new ToolboxContribution(items: [item]),
        ]));
        Assert.Throws<ArgumentException>(() => new ToolboxCatalog([null!]));
        Assert.Throws<ArgumentException>(() => new ToolboxContribution([null!]));
        Assert.Throws<ArgumentException>(() => new ToolboxContribution(items: [null!]));
        Assert.Throws<ArgumentException>(() => new ToolboxContribution(sections: [null!]));
    }

    [Fact]
    public void DefinitionsRejectInvalidDisplayIconAndOrderMetadata()
    {
        var groupId = new ToolboxGroupId("group:valid");
        var sectionId = new ToolboxSectionId("section:valid");
        var itemId = new ToolboxItemId("item:valid");
        var icon = new ToolboxIconDescriptor("test:icon", "□");

        Assert.Throws<ArgumentException>(() =>
            new ToolboxGroupDefinition(groupId, " ", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToolboxGroupDefinition(groupId, "Valid", -1));
        Assert.Throws<ArgumentException>(() =>
            new ToolboxIconDescriptor(" "));
        Assert.Throws<ArgumentException>(() =>
            new ToolboxIconDescriptor("test:icon", " "));
        Assert.Throws<ArgumentException>(() =>
            new ToolboxSectionDefinition(sectionId, " ", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToolboxSectionDefinition(sectionId, "Valid", -1));
        Assert.Throws<ArgumentException>(() => new ToolboxItemDefinition(
            itemId,
            SharedTypeId,
            groupId,
            string.Empty,
            0,
            icon));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolboxItemDefinition(
            itemId,
            SharedTypeId,
            groupId,
            "Valid",
            -1,
            icon));
    }

    [Fact]
    public void ContributionAndCatalogDefensivelyCopyAndExposeImmutableCollections()
    {
        var section = new ToolboxSectionDefinition(
            new ToolboxSectionId("section:stable"),
            "Stable",
            0);
        var group = Group("group:stable", 0, "Stable");
        var item = Item("item:stable", group.GroupId, 0, "Stable");
        var groups = new List<ToolboxGroupDefinition> { group };
        var items = new List<ToolboxItemDefinition> { item };
        var sections = new List<ToolboxSectionDefinition> { section };
        var contribution = new ToolboxContribution(groups, items, sections);
        var contributions = new List<ToolboxContribution> { contribution };
        var catalog = new ToolboxCatalog(contributions);

        groups.Clear();
        items.Clear();
        sections.Clear();
        contributions.Clear();

        Assert.Equal(section, Assert.Single(contribution.Sections));
        Assert.Equal(group, Assert.Single(contribution.Groups));
        Assert.Equal(item, Assert.Single(contribution.Items));
        Assert.Equal(section, Assert.Single(catalog.Sections));
        Assert.Equal(group, Assert.Single(catalog.Groups));
        Assert.Equal(item, Assert.Single(catalog.Items));
        AssertReadOnly(contribution.Sections);
        AssertReadOnly(contribution.Groups);
        AssertReadOnly(contribution.Items);
        AssertReadOnly(catalog.Sections);
        AssertReadOnly(catalog.Groups);
        AssertReadOnly(catalog.Items);
        AssertReadOnly(catalog.GetItems(group.GroupId));
    }

    [Fact]
    public void EmptyCatalogAndEmptyGroupsAreStable()
    {
        var emptyGroup = Group("group:empty", 0, "Empty");
        var catalog = new ToolboxCatalog(
        [
            ToolboxContribution.Empty,
            new ToolboxContribution([emptyGroup]),
        ]);

        Assert.Empty(ToolboxCatalog.Empty.Groups);
        Assert.Empty(ToolboxCatalog.Empty.Items);
        Assert.Empty(ToolboxCatalog.Empty.Sections);
        Assert.Equal(emptyGroup, Assert.Single(catalog.Groups));
        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.GetItems(emptyGroup.GroupId));
        Assert.Empty(catalog.GetItems(new ToolboxGroupId("group:missing")));
    }

    private static ToolboxGroupDefinition Group(
        string id,
        int order,
        string displayName) =>
        new(new ToolboxGroupId(id), displayName, order);

    private static ToolboxItemDefinition Item(
        string id,
        ToolboxGroupId groupId,
        int order,
        string displayName,
        SemanticTypeId? elementTypeId = null) =>
        new(
            new ToolboxItemId(id),
            elementTypeId ?? SharedTypeId,
            groupId,
            displayName,
            order,
            new ToolboxIconDescriptor($"icon:{id}", "□"));

    private static void AssertReadOnly<T>(ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }
}
