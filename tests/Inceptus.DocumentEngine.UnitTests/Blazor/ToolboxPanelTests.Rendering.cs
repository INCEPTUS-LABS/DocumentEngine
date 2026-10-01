using System.Globalization;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class ToolboxPanelTests
{
    [Theory]
    [InlineData("selection")]
    [InlineData("clear-selection")]
    [InlineData("group")]
    [InlineData("mode")]
    [InlineData("culture")]
    [InlineData("catalog")]
    [InlineData("prefix")]
    [InlineData("callback")]
    public async Task RenderGuardSkipsUnchangedInputsButPreservesMeaningfulChanges(string change)
    {
        using var culture = new ModelerCultureScope("en");
        var activator = new ToolboxRenderActivator();
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ToolboxRenderHost>());
        var host = activator.Host;
        var panel = activator.Panel;
        var initial = panel.BuildCount;
        for (var i = 0; i < 3; i++)
        {
            await renderer.Dispatcher.InvokeAsync(host.RefreshAsync);
        }
        Assert.Equal(initial, panel.BuildCount);
        var callbackCalls = 0;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            switch (change)
            {
                case "selection": host.Selection = host.Catalog.Items[1].ItemId; break;
                case "clear-selection": host.Selection = null; break;
                case "group": Assert.True(panel.ToggleGroup(host.Catalog.Groups[1].GroupId)); break;
                case "mode": panel.ToggleViewMode(); break;
                case "culture":
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl");
                    host.Culture = CultureInfo.CurrentUICulture;
                    break;
                case "catalog": host.Catalog = ToolboxCatalog.Empty; break;
                case "prefix": host.Prefix = "replacement-editor"; break;
                case "callback":
                    host.Callback = EventCallback.Factory.Create<ToolboxItemId>(new object(), _ => callbackCalls++);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
            await host.RefreshAsync();
        });
        Assert.True(panel.BuildCount > initial);
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        switch (change)
        {
            case "selection":
                Assert.Contains("data-selected-toolbox-item-id=\"test:task\"", markup);
                Assert.Equal(1, Count(markup, "aria-pressed=\"true\""));
                break;
            case "clear-selection":
                Assert.Contains("data-selected-toolbox-item-id=\"\"", markup);
                Assert.DoesNotContain("aria-pressed=\"true\"", markup);
                break;
            case "group":
                Assert.Contains("data-expanded-toolbox-group-id=\"test:tasks\"", markup);
                Assert.Equal(1, Count(markup, "aria-expanded=\"true\""));
                break;
            case "mode": Assert.Contains("data-toolbox-view-mode=\"compact\"", markup); break;
            case "culture": Assert.Contains("Przybornik", markup); break;
            case "catalog":
                Assert.DoesNotContain("class=\"toolbox-item", markup);
                Assert.DoesNotContain("aria-expanded=\"true\"", markup);
                Assert.False(await renderer.Dispatcher.InvokeAsync(() => panel.SelectItemAsync(new ToolboxItemId("test:event"))));
                break;
            case "prefix": Assert.Contains("id=\"replacement-editor-toolbox\"", markup); break;
            case "callback":
                Assert.True(await renderer.Dispatcher.InvokeAsync(() => panel.SelectItemAsync(host.Catalog.Items[0].ItemId)));
                Assert.Equal(1, callbackCalls);
                break;
        }
        Assert.DoesNotContain(" disabled", markup); // This component has no readiness input or disabled-item policy.
    }

    private sealed class ToolboxRenderActivator : IComponentActivator
    {
        internal ToolboxRenderHost Host { get; private set; } = null!;
        internal CountingToolboxPanel Panel { get; private set; } = null!;

        public IComponent CreateInstance(Type componentType) => componentType == typeof(ToolboxRenderHost)
            ? Host = new ToolboxRenderHost()
            : componentType == typeof(ToolboxPanel)
                ? Panel = new CountingToolboxPanel()
                : (IComponent)Activator.CreateInstance(componentType)!;
    }

    private sealed class CountingToolboxPanel : ToolboxPanel
    {
        internal int BuildCount { get; private set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            BuildCount++;
            base.BuildRenderTree(builder);
        }
    }

    private sealed class ToolboxRenderHost : ComponentBase
    {
        internal ToolboxCatalog Catalog { get; set; } = SectionedCatalog();
        internal ToolboxItemId? Selection { get; set; } = new("test:event");
        internal string Prefix { get; set; } = "test-editor";
        internal CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("en");
        internal EventCallback<ToolboxItemId> Callback { get; set; }
        internal Task RefreshAsync() => InvokeAsync(StateHasChanged);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<CultureInfo>>(0);
            builder.AddAttribute(1, "Name", "ModelerUICulture");
            builder.AddAttribute(2, "Value", Culture);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(content =>
            {
                content.OpenComponent<ToolboxPanel>(0);
                content.AddAttribute(1, nameof(ToolboxPanel.Catalog), Catalog);
                content.AddAttribute(2, nameof(ToolboxPanel.SelectedItemId), Selection);
                content.AddAttribute(3, nameof(ToolboxPanel.DomIdPrefix), Prefix);
                content.AddAttribute(4, nameof(ToolboxPanel.SelectedItemIdChanged), Callback);
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
