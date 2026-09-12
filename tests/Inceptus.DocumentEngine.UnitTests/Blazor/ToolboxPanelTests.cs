using System.Net;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class ToolboxPanelTests
{
    [Fact]
    public async Task NeutralCatalogRendersOrderedAccessibleButtonsFromDefinitionData()
    {
        var markup = await RenderAsync(NeutralDemoToolbox.Catalog);

        var groupIndex = markup.IndexOf(">Generic<", StringComparison.Ordinal);
        var elementIndex = markup.IndexOf(">Element<", StringComparison.Ordinal);
        var variantIndex = markup.IndexOf(">Element variant<", StringComparison.Ordinal);
        var policyIndex = markup.IndexOf(">Policy element<", StringComparison.Ordinal);

        var prefix = ExtractDomPrefix(markup);
        Assert.Contains($"id=\"{prefix}-toolbox\"", markup, StringComparison.Ordinal);
        Assert.Contains($"aria-labelledby=\"{prefix}-toolbox-heading\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("data-toolbox-view-mode=\"expanded\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("data-expanded-toolbox-group-id=\"demo:toolbox:generic\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Collapse Toolbox\"", markup,
            StringComparison.Ordinal);
        Assert.True(groupIndex >= 0 && groupIndex < elementIndex &&
            elementIndex < variantIndex && variantIndex < policyIndex);
        Assert.Equal(3, Count(markup, "class=\"toolbox-item\""));
        Assert.Equal(5, Count(markup, "type=\"button\""));
        Assert.Equal(3, Count(markup, "aria-pressed=\"false\""));
        Assert.Equal(1, Count(markup, "aria-expanded=\"true\""));
        Assert.Contains("title=\"Element\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Element\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-toolbox-icon-key=\"generic:element\"", markup,
            StringComparison.Ordinal);
        Assert.Contains(">□</span>", markup, StringComparison.Ordinal);
        Assert.Contains(">○</span>", markup, StringComparison.Ordinal);
        Assert.Contains(">◇</span>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("draggable", markup, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            NeutralDemoToolbox.Catalog.Items[0].ItemId,
            NeutralDemoToolbox.Catalog.Items[1].ItemId);
        Assert.Equal(
            NeutralDemoToolbox.Catalog.Items[0].ElementTypeId,
            NeutralDemoToolbox.Catalog.Items[1].ElementTypeId);
    }

    [Fact]
    public async Task EmptyGroupsAreOmittedAndUnknownIconUsesGenericFallback()
    {
        var emptyGroupId = new ToolboxGroupId("test:empty");
        var visibleGroupId = new ToolboxGroupId("test:visible");
        var catalog = new ToolboxCatalog(
        [
            new ToolboxContribution(
                groups:
                [
                    new ToolboxGroupDefinition(emptyGroupId, "Empty group", 0),
                    new ToolboxGroupDefinition(visibleGroupId, "Visible group", 1),
                ],
                items:
                [
                    new ToolboxItemDefinition(
                        new ToolboxItemId("test:item"),
                        new SemanticTypeId("test:type"),
                        visibleGroupId,
                        "Unknown icon item",
                        0,
                        new ToolboxIconDescriptor("plugin:unknown")),
                ]),
        ]);

        var markup = await RenderAsync(catalog);

        Assert.DoesNotContain("Empty group", markup, StringComparison.Ordinal);
        Assert.Contains("Visible group", markup, StringComparison.Ordinal);
        Assert.Contains("Unknown icon item", markup, StringComparison.Ordinal);
        Assert.Contains("data-toolbox-icon-key=\"plugin:unknown\"", markup,
            StringComparison.Ordinal);
        Assert.Contains(">□</span>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyCatalogRendersStableEmptyState()
    {
        var markup = await RenderAsync(ToolboxCatalog.Empty);

        Assert.Contains($"id=\"{ExtractDomPrefix(markup)}-toolbox\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("No tools available.", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"toolbox-group\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"toolbox-item\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionedCatalogRendersNotationHeadingOrderedAccordionAndGroupIcons()
    {
        var catalog = SectionedCatalog();

        var markup = await RenderAsync(catalog);

        Assert.True(
            markup.IndexOf(">Notation<", StringComparison.Ordinal) <
            markup.IndexOf(">Events<", StringComparison.Ordinal));
        Assert.True(
            markup.IndexOf(">Events<", StringComparison.Ordinal) <
            markup.IndexOf(">Tasks<", StringComparison.Ordinal));
        Assert.Contains("data-toolbox-section-id=\"test:notation\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("data-toolbox-group-icon-key=\"test:events\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("data-toolbox-group-icon-key=\"test:tasks\"", markup,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(markup, "aria-expanded=\"true\""));
        Assert.Equal(1, Count(markup, "aria-expanded=\"false\""));
        Assert.Contains("title=\"Event\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Event\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AccordionAndViewModeUseOneLocalAuthorityAndRetainEachOther()
    {
        var events = new ToolboxGroupId("test:events");
        var tasks = new ToolboxGroupId("test:tasks");
        var gateways = new ToolboxGroupId("test:gateways");
        var state = new ToolboxPresentationState();
        state.Reconcile([events, tasks, gateways]);

        Assert.Equal(ToolboxViewMode.Expanded, state.ViewMode);
        Assert.Equal(events, state.ExpandedToolboxGroupId);
        Assert.True(state.ToggleGroup(tasks));
        Assert.Equal(tasks, state.ExpandedToolboxGroupId);
        state.ToggleViewMode();
        Assert.Equal(ToolboxViewMode.Compact, state.ViewMode);
        Assert.Equal(tasks, state.ExpandedToolboxGroupId);
        Assert.True(state.ToggleGroup(tasks));
        Assert.Null(state.ExpandedToolboxGroupId);
        state.ToggleViewMode();
        Assert.Equal(ToolboxViewMode.Expanded, state.ViewMode);
        Assert.Null(state.ExpandedToolboxGroupId);
        Assert.False(state.ToggleGroup(new ToolboxGroupId("test:unknown")));
        Assert.Null(state.ExpandedToolboxGroupId);
    }

    [Fact]
    public async Task GroupAndModeChangesDoNotPublishOrClearPlacementSelection()
    {
        var catalog = SectionedCatalog();
        var selected = catalog.Items[1].ItemId;
        var published = 0;
        var panel = new ToolboxPanel();
        typeof(ToolboxPanel).GetProperty(nameof(ToolboxPanel.Catalog))!.SetValue(panel, catalog);
        typeof(ToolboxPanel).GetProperty(nameof(ToolboxPanel.SelectedItemId))!
            .SetValue(panel, selected);
        typeof(ToolboxPanel).GetProperty(nameof(ToolboxPanel.SelectedItemIdChanged))!
            .SetValue(
                panel,
                EventCallback.Factory.Create<ToolboxItemId>(
                new object(),
                _ => published++));
        typeof(ToolboxPanel)
            .GetMethod("OnParametersSet", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!
            .Invoke(panel, null);

        Assert.True(panel.ToggleGroup(catalog.Groups[1].GroupId));
        panel.ToggleViewMode();

        Assert.Equal(selected, panel.SelectedItemId);
        Assert.Equal(0, published);
        Assert.True(await panel.SelectItemAsync(catalog.Items[0].ItemId));
        Assert.Equal(1, published);
    }

    [Fact]
    public void PresentationSelectionIsLocalSingleSelectClearableAndExpectedClearIsSafe()
    {
        var selection = new ToolboxSelectionState();
        var first = NeutralDemoToolbox.Catalog.Items[0].ItemId;
        var second = NeutralDemoToolbox.Catalog.Items[1].ItemId;

        Assert.Null(selection.SelectedItemId);
        Assert.True(selection.Select(first));
        Assert.Equal(first, selection.SelectedItemId);
        Assert.False(selection.Select(first));
        Assert.Equal(first, selection.SelectedItemId);
        Assert.True(selection.Select(second));
        Assert.Equal(second, selection.SelectedItemId);
        Assert.False(selection.Clear(first));
        Assert.Equal(second, selection.SelectedItemId);
        Assert.True(selection.Clear(second));
        Assert.Null(selection.SelectedItemId);
        Assert.False(selection.Clear());
    }

    [Fact]
    public async Task PanelSelectionHandlerPublishesCatalogItemsAndRejectsUnknownItems()
    {
        var panel = new ToolboxPanel();
        typeof(ToolboxPanel)
            .GetProperty(nameof(ToolboxPanel.Catalog))!
            .SetValue(panel, NeutralDemoToolbox.Catalog);
        var first = NeutralDemoToolbox.Catalog.Items[0].ItemId;
        var second = NeutralDemoToolbox.Catalog.Items[1].ItemId;
        ToolboxItemId? published = null;
        typeof(ToolboxPanel)
            .GetProperty(nameof(ToolboxPanel.SelectedItemIdChanged))!
            .SetValue(
                panel,
                EventCallback.Factory.Create<ToolboxItemId>(
                    new object(),
                    itemId => published = itemId));

        Assert.True(await panel.SelectItemAsync(first));
        Assert.Equal(first, published);
        Assert.True(await panel.SelectItemAsync(second));
        Assert.Equal(second, published);
        Assert.False(await panel.SelectItemAsync(new ToolboxItemId("test:unknown")));
        Assert.Equal(second, published);
    }

    [Fact]
    public async Task ControlledSelectionIsRenderedWithoutPanelOwnedState()
    {
        var selected = NeutralDemoToolbox.Catalog.Items[1].ItemId;
        var markup = await RenderAsync(NeutralDemoToolbox.Catalog, selected);

        Assert.Contains(
            $"data-selected-toolbox-item-id=\"{selected.Value}\"",
            markup,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(markup, "class=\"toolbox-item selected\""));
        Assert.Equal(1, Count(markup, "aria-pressed=\"true\""));
    }

    private static async Task<string> RenderAsync(
        ToolboxCatalog catalog,
        ToolboxItemId? selectedItemId = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var parameters = ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(ToolboxPanel.Catalog)] = catalog,
                [nameof(ToolboxPanel.SelectedItemId)] = selectedItemId,
            });
        var component = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<ToolboxPanel>(parameters));
        return WebUtility.HtmlDecode(
            await renderer.Dispatcher.InvokeAsync(component.ToHtmlString));
    }

    private static ToolboxCatalog SectionedCatalog()
    {
        var sectionId = new ToolboxSectionId("test:notation");
        var events = new ToolboxGroupDefinition(
            new ToolboxGroupId("test:events"),
            "Events",
            0,
            sectionId,
            new ToolboxIconDescriptor("test:events", "◎"));
        var tasks = new ToolboxGroupDefinition(
            new ToolboxGroupId("test:tasks"),
            "Tasks",
            1,
            sectionId,
            new ToolboxIconDescriptor("test:tasks", "▭"));
        return new ToolboxCatalog(
        [
            new ToolboxContribution(
                [events, tasks],
                [
                    new ToolboxItemDefinition(
                        new ToolboxItemId("test:event"),
                        new SemanticTypeId("test:event:type"),
                        events.GroupId,
                        "Event",
                        0,
                        new ToolboxIconDescriptor("test:event", "○")),
                    new ToolboxItemDefinition(
                        new ToolboxItemId("test:task"),
                        new SemanticTypeId("test:task:type"),
                        tasks.GroupId,
                        "Task",
                        0,
                        new ToolboxIconDescriptor("test:task", "▭")),
                ],
                [new ToolboxSectionDefinition(sectionId, "Notation", 0)]),
        ]);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ExtractDomPrefix(string markup)
    {
        var match = Regex.Match(
            markup,
            "id=\"(?<prefix>inceptus-[0-9a-f]{32})-toolbox\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The Toolbox root must use a component-scoped DOM prefix.");
        return match.Groups["prefix"].Value;
    }
}
