using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class ToolboxPanelTests
{
    [Fact]
    public async Task A24StartupFirstRenderCollapsesEveryMainGroupBeforeAnyInteractiveOrAfterRenderCallback()
    {
        var catalog = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions);
        var markup = await RenderAsync(catalog);
        Assert.Equal(4, Count(markup, "aria-expanded=\"false\""));
        Assert.DoesNotContain("aria-expanded=\"true\"", markup);
        Assert.Equal(4, Count(markup, " hidden"));
        Assert.Contains("data-expanded-toolbox-group-id=\"\"", markup);
        Assert.Equal(["Events", "Activities", "Gateways", "Flow"], catalog.Groups.Select(group => group.DisplayName));
        // HtmlRenderer never runs OnAfterRender. This is the first authoritative markup.
    }

    [Fact]
    public async Task A24StartupAccordionStillExpandsOneGroupAndPreservesItAcrossParentRenders()
    {
        var activator = new ToolboxRenderActivator();
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ToolboxRenderHost>());
        var host = activator.Host;
        host.Catalog = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions);
        await renderer.Dispatcher.InvokeAsync(host.RefreshAsync);
        Assert.DoesNotContain("aria-expanded=\"true\"", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        foreach (var group in host.Catalog.Groups)
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                Assert.True(activator.Panel.ToggleGroup(group.GroupId));
                await host.RefreshAsync();
            });
            for (var update = 0; update < 3; update++)
            {
                host.Selection = update % 2 == 0 ? host.Catalog.Items[0].ItemId : null;
                await renderer.Dispatcher.InvokeAsync(host.RefreshAsync);
                var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
                Assert.Contains($"data-expanded-toolbox-group-id=\"{group.GroupId.Value}\"", markup);
                Assert.Equal(1, Count(markup, "aria-expanded=\"true\""));
                Assert.Equal(3, Count(markup, "aria-expanded=\"false\""));
            }
        }
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Assert.True(activator.Panel.ToggleGroup(host.Catalog.Groups[^1].GroupId));
            await host.RefreshAsync();
        });
        Assert.DoesNotContain("aria-expanded=\"true\"", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
    }
}
