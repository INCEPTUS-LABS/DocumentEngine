using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class ToolboxPanelTests
{
    [Fact]
    public async Task A22FlowToolsRenderAccessibleConnectionButtonsWithoutElementPlacementMetadata()
    {
        using var culture = new ModelerCultureScope("en");
        var all = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions);
        Assert.Equal(["Events", "Activities", "Gateways", "Flow"], all.Groups.Select(group => group.DisplayName));
        Assert.Equal(8, all.Items.Length);
        var flow = Assert.Single(all.Groups, group => group.DisplayName == "Flow");
        var tools = all.GetConnectionItems(flow.GroupId);
        Assert.Equal(["Sequence Flow", "Message Flow"], tools.Select(item => item.DisplayName));
        Assert.Empty(all.GetItems(flow.GroupId));
        var catalog = new ToolboxCatalog([new ToolboxContribution([flow], [], all.Sections, tools)]);
        var markup = await RenderAsync(catalog, tools[1].ItemId);

        Assert.Contains("aria-label=\"Create Sequence Flow\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Create Message Flow\"", markup, StringComparison.Ordinal);
        Assert.Contains($"data-toolbox-connection-creation-id=\"{tools[1].CreationId.Value}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-toolbox-element-type-id", markup, StringComparison.Ordinal);
        Assert.Equal(2, Count(markup, "data-toolbox-connection-creation-id="));
        Assert.Equal(1, Count(markup, "aria-pressed=\"true\""));
        Assert.DoesNotContain("tabindex=\"-1\"", markup, StringComparison.Ordinal);

        var panel = new ToolboxPanel();
        typeof(ToolboxPanel).GetProperty(nameof(ToolboxPanel.Catalog))!.SetValue(panel, catalog);
        ToolboxItemId? published = null;
        typeof(ToolboxPanel).GetProperty(nameof(ToolboxPanel.SelectedItemIdChanged))!.SetValue(panel,
            EventCallback.Factory.Create<ToolboxItemId>(new object(), id => published = id));
        Assert.True(await panel.SelectItemAsync(tools[1].ItemId));
        Assert.Equal(tools[1].ItemId, published);
        Assert.False(await panel.SelectItemAsync(new("unknown:connection")));
    }

    [Fact]
    public void A22ConnectionSelectionUsesExistingTransientToolStateAndPreservesOtherEditorState()
    {
        var visual = new VisualStateId("a22:selected");
        var state = new EditorStateSnapshot(selection: [visual], toolState:
            [KeyValuePair.Create("other:tool", PropertyValue.FromText("retained"))]);
        var creationId = new AnchorConnectionCreationId("a22:message");
        var selected = ConnectionToolState.WithSelection(state, creationId);
        Assert.Equal(creationId, ConnectionToolState.GetSelection(selected));
        Assert.Equal(ConnectionToolState.ToolId, selected.ActiveToolId);
        Assert.Equal(visual, Assert.Single(selected.Selection));
        Assert.Equal(state.ToolState["other:tool"], selected.ToolState["other:tool"]);
        var cleared = ConnectionToolState.WithSelection(selected, null);
        Assert.Null(ConnectionToolState.GetSelection(cleared));
        Assert.Null(cleared.ActiveToolId);
        Assert.Equal(state.ToolState, cleared.ToolState);
        Assert.Equal(state.Selection.AsEnumerable(), cleared.Selection);
    }

    [Fact]
    public void A22ConnectionDefinitionsShareCatalogIdentityValidationWithoutBecomingPlacementItems()
    {
        var group = new ToolboxGroupDefinition(new("a22:flow"), "Flow", 0);
        var item = new ToolboxConnectionItemDefinition(new("a22:item"), new("a22:create"), group.GroupId,
            "Connection", 0, new("a22:line"));
        var element = new ToolboxItemDefinition(item.ItemId, new("a22:element"), group.GroupId,
            "Element", 0, new("a22:box"));
        Assert.Throws<ArgumentException>(() => new ToolboxCatalog([
            new ToolboxContribution([group], [element], [], [item])]));
        Assert.Throws<ArgumentException>(() => new ToolboxCatalog([
            new ToolboxContribution([], [], [], [item])]));
        var catalog = new ToolboxCatalog([new ToolboxContribution([group], [], [], [item])]);
        Assert.Empty(catalog.Items);
        Assert.False(catalog.TryGetItem(item.ItemId, out _));
        Assert.True(catalog.TryGetConnectionItem(item.ItemId, out var resolved));
        Assert.Same(item, resolved);
    }
}
