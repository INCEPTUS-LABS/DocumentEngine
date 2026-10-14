using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
#pragma warning disable BL0006 // Inspect actual Blazor render diffs in the canonical unit suite.
    [Fact]
    public async Task A24PanelRenderingSkipsEquivalentParametersAndUpdatesControlsInPlace()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("panel-rendering", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var panel = host.CaptureState().ElementToolbox!;
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new A24PanelRenderer(services);
        using var culture = new ModelerCultureScope("en");
        await renderer.ShowAsync(panel);
        var component = renderer.Component;
        Assert.Equal(1, renderer.PanelRenderCount);
        for (var i = 0; i < 20; i++) await renderer.ShowAsync(panel with { });
        Assert.Equal(1, renderer.PanelRenderCount);
        Assert.Equal(0, renderer.DisposalCount);

        await renderer.ShowAsync(panel with { CanAutoPoints = true });
        Assert.Equal(2, renderer.PanelRenderCount);
        Assert.DoesNotContain(renderer.LastEdits, kind => kind is RenderTreeEditType.PrependFrame or RenderTreeEditType.RemoveFrame);
        Assert.Same(component, renderer.Component);
        Assert.Equal(0, renderer.DisposalCount);

        var moved = panel with
        {
            ActionsPosition = panel.ActionsPosition + new VectorD(30, 20),
            CreationPosition = panel.CreationPosition + new VectorD(30, 20)
        };
        await renderer.ShowAsync(moved);
        Assert.Equal(3, renderer.PanelRenderCount);
        Assert.DoesNotContain(renderer.LastEdits, kind => kind is RenderTreeEditType.PrependFrame or RenderTreeEditType.RemoveFrame);
        Assert.Equal(0, renderer.DisposalCount);

        using (new ModelerCultureScope("pl")) await renderer.ShowAsync(moved);
        Assert.Equal(4, renderer.PanelRenderCount);
        Assert.Same(component, renderer.Component);
        await renderer.ShowAsync(null);
        Assert.Equal(5, renderer.PanelRenderCount);
    }

    // Use the same Blazor Renderer boundary as the existing component tests; count
    // actual panel render batches, not calls to ShouldRender or simulated markup.
    private sealed class A24PanelRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        private int _id;
        internal ElementToolbox? Component { get; private set; }
        internal int PanelRenderCount { get; private set; }
        internal int DisposalCount { get; private set; }
        internal List<RenderTreeEditType> LastEdits { get; } = [];
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        internal Task ShowAsync(ElementToolboxState? state) => Dispatcher.InvokeAsync(async () =>
        {
            if (Component is null)
            {
                Component = (ElementToolbox)InstantiateComponent(typeof(ElementToolbox));
                _id = AssignRootComponentId(Component);
            }
            await RenderRootComponentAsync(_id, ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ElementToolbox.State)] = state }));
        });

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
        {
            DisposalCount += renderBatch.DisposedComponentIDs.Count;
            foreach (var diff in renderBatch.UpdatedComponents.Array.Take(renderBatch.UpdatedComponents.Count))
            {
                if (diff.ComponentId != _id) continue;
                PanelRenderCount++;
                LastEdits.Clear();
                foreach (var edit in diff.Edits) LastEdits.Add(edit.Type);
            }
            return Task.CompletedTask;
        }

        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Panel rendering failed.", exception);
    }
#pragma warning restore BL0006
}
