using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
#pragma warning disable BL0006 // Inspect the production Blazor diff and file-input component identities.
    [Fact]
    public async Task SiteToolbarRenderingKeepsButtonHandlersMenusAndFileInputsAcrossRebuilding()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("site-toolbar-rendering", "container");
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new SiteToolbarJsRuntime()).BuildServiceProvider();
        await using var renderer = new SiteToolbarRenderer(services, host);
        var ready = DemoState(host);
        await renderer.ShowAsync(ready);
        var handlers = renderer.ButtonHandlers();
        var inputs = renderer.FileInputIds();
        Assert.Equal(7, handlers.Length);
        Assert.Equal(2, inputs.Length);
        for (var i = 0; i < 5; i++)
        {
            await renderer.ShowAsync(ready with { Session = ActionBarSession(ready.Session!, EditingSessionStatus.Rebuilding) });
            Assert.Empty(renderer.AvailabilityEdits);
            Assert.DoesNotContain(renderer.LastEdits, kind => kind is RenderTreeEditType.PrependFrame or RenderTreeEditType.RemoveFrame);
            Assert.Equal(handlers, renderer.ButtonHandlers());
            Assert.Equal(inputs, renderer.FileInputIds());
            await renderer.ShowAsync(ready);
            Assert.Empty(renderer.AvailabilityEdits);
            Assert.Equal(handlers, renderer.ButtonHandlers());
            Assert.Equal(inputs, renderer.FileInputIds());
        }
        await renderer.ShowAsync(ready with { PropertiesFormOpen = true });
        Assert.NotEmpty(renderer.AvailabilityEdits);
        Assert.False(SiteToolbarEnabled(renderer.Component!, "CanTransfer"));
        Assert.False(SiteToolbarEnabled(renderer.Component!, "CanValidate"));
        Assert.Equal(handlers, renderer.ButtonHandlers());
        Assert.Equal(inputs, renderer.FileInputIds());
        Assert.Equal(0, renderer.DisposalCount);
        await renderer.ShowAsync(ready);
        Assert.True(SiteToolbarEnabled(renderer.Component!, "CanTransfer"));
        Assert.True(SiteToolbarEnabled(renderer.Component!, "CanValidate"));
        await renderer.ShowAsync(ready with { ValidationInFlight = true });
        Assert.True(SiteToolbarEnabled(renderer.Component!, "CanTransfer"));
        Assert.False(SiteToolbarEnabled(renderer.Component!, "CanValidate"));
    }

    private sealed class SiteToolbarRenderer(IServiceProvider services, DocumentCanvasHost host) : Renderer(services, NullLoggerFactory.Instance)
    {
        private int _id;
        internal BpmnDemoHost? Component { get; private set; }
        internal int DisposalCount { get; private set; }
        internal List<RenderTreeEditType> LastEdits { get; } = [];
        internal List<RenderTreeEditType> AvailabilityEdits { get; } = [];
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        internal Task ShowAsync(DemoHostEditorState state) => Dispatcher.InvokeAsync(async () =>
        {
            if (Component is null)
            {
                Component = (BpmnDemoHost)InstantiateComponent(typeof(BpmnDemoHost));
                Component.AttachEditor(host, host.Operations());
                _id = AssignRootComponentId(Component);
            }
            typeof(BpmnDemoHost).GetField("_state", PropertiesUxFlags)!.SetValue(Component, state);
            await RenderRootComponentAsync(_id, ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(BpmnDemoHost.Editor)] = host }));
        });

        internal ulong[] ButtonHandlers() => Frames().Where(frame => frame.FrameType == RenderTreeFrameType.Attribute &&
            frame.AttributeName == "onclick" && frame.AttributeEventHandlerId != 0)
            .Select(frame => frame.AttributeEventHandlerId).Skip(1).ToArray();
        internal int[] FileInputIds() => Frames().Where(frame => frame.FrameType == RenderTreeFrameType.Component &&
            frame.ComponentType == typeof(InputFile)).Select(frame => frame.ComponentId).ToArray();
        private IEnumerable<RenderTreeFrame> Frames() => GetCurrentRenderTreeFrames(_id).Array.Take(GetCurrentRenderTreeFrames(_id).Count);

        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            DisposalCount += batch.DisposedComponentIDs.Count;
            LastEdits.Clear();
            AvailabilityEdits.Clear();
            foreach (var diff in batch.UpdatedComponents.Array.Take(batch.UpdatedComponents.Count))
            {
                foreach (var edit in diff.Edits)
                {
                    LastEdits.Add(edit.Type);
                    if ((edit.Type == RenderTreeEditType.SetAttribute &&
                        batch.ReferenceFrames.Array[edit.ReferenceFrameIndex].AttributeName is "disabled" or "aria-disabled") ||
                        (edit.Type == RenderTreeEditType.RemoveAttribute && edit.RemovedAttributeName is "disabled" or "aria-disabled"))
                        AvailabilityEdits.Add(edit.Type);
                }
            }
            return Task.CompletedTask;
        }

        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Site toolbar rendering failed.", exception);
    }

    private sealed class SiteToolbarJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("Blazor._internal.InputFile.init", identifier);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
#pragma warning restore BL0006
}
