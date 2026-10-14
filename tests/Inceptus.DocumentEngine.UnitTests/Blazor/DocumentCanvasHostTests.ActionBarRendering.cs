using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
#pragma warning disable BL0006 // Verify the real Blazor diff, including HTML element continuity.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionBarRenderingPreservesButtonsAndHandlersAcrossHoverRebuild(bool smartEdit)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("action-bar-rendering", "container");
        host.SetSmartEditEnabled(smartEdit);
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new PublishDownloadRuntime()).BuildServiceProvider();
        await using var renderer = new ActionBarRenderer(services);
        var ready = host.CaptureState();
        await renderer.ShowAsync(ready);
        var handlers = renderer.ButtonHandlers();
        Assert.Equal(7, handlers.Length);
        var editor = ready.Session!.EditorState;
        for (var i = 0; i < 5; i++)
        {
            await renderer.ShowAsync(ready with
            {
                Session = ActionBarSession(ready.Session!, EditingSessionStatus.Rebuilding,
                editor: new EditorStateSnapshot(hoveredObjectId: new("hover-" + i),
                    activeToolId: editor.ActiveToolId, viewport: editor.Viewport,
                    activeGesture: editor.ActiveGesture, temporaryFeedback: editor.TemporaryFeedback,
                    toolState: editor.ToolState))
            });
            Assert.Empty(renderer.DisabledEdits);
            Assert.DoesNotContain(renderer.LastEdits, kind => kind is RenderTreeEditType.PrependFrame or RenderTreeEditType.RemoveFrame);
            Assert.Equal(handlers, renderer.ButtonHandlers());
            await renderer.ShowAsync(ready);
            Assert.Empty(renderer.DisabledEdits);
            Assert.DoesNotContain(renderer.LastEdits, kind => kind is RenderTreeEditType.PrependFrame or RenderTreeEditType.RemoveFrame);
            Assert.Equal(handlers, renderer.ButtonHandlers());
        }
        Assert.Equal(0, renderer.DisposalCount);

        // A genuine capability change updates only an attribute, keeping button identity.
        await renderer.ShowAsync(ready with { CanAutoArrange = false });
        Assert.Single(renderer.DisabledEdits);
        Assert.Equal(handlers, renderer.ButtonHandlers());
        Assert.DoesNotContain(renderer.LastEdits, kind => kind is RenderTreeEditType.PrependFrame or RenderTreeEditType.RemoveFrame);
    }

    [Theory]
    [InlineData("form")]
    [InlineData("model-view-form")]
    [InlineData("gesture")]
    [InlineData("fault")]
    public async Task ActionBarRenderingReflectsFunctionalGuardsAndFaultHistoryRecovery(string change)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("action-bar-guards", "container");
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new PublishDownloadRuntime()).BuildServiceProvider();
        await using var renderer = new ActionBarRenderer(services);
        var ready = host.CaptureState();
        await renderer.ShowAsync(ready);
        var changed = change switch
        {
            "form" => ready with { PropertiesFormOpen = true },
            "model-view-form" => ready with { ModelViewPropertiesFormOpen = true },
            "gesture" => ready with
            {
                Session = ActionBarSession(ready.Session!, EditingSessionStatus.Ready,
                editor: new(activeGesture: new("drag", "move", new(0, 0), new(10, 10))))
            },
            _ => ready with
            {
                Session = ActionBarSession(ready.Session!, EditingSessionStatus.RuntimeFaulted,
                history: new HistoryStatus(1, true, false))
            }
        };
        await renderer.ShowAsync(changed);
        Assert.False(ActionBarEnabled(ActionBarSnapshot(renderer.Component!), "CanCreateNewDiagram"));
        Assert.False(ActionBarEnabled(ActionBarSnapshot(renderer.Component!), "CanZoomIn"));
        Assert.False(ActionBarEnabled(ActionBarSnapshot(renderer.Component!), "CanAutoArrange"));
        Assert.Equal(change == "fault", ActionBarEnabled(ActionBarSnapshot(renderer.Component!), "CanUndo"));
        Assert.NotEmpty(renderer.DisabledEdits);
        await renderer.ShowAsync(ready);
        Assert.True(ActionBarEnabled(ActionBarSnapshot(renderer.Component!), "CanCreateNewDiagram"));
    }

    // Same Renderer seam as the existing panel tests. The test subclass omits browser
    // initialization, as HtmlRenderer does; the production render tree is unchanged.
    private sealed class ActionBarRenderCanvas : DocumentCanvas
    {
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }

    private sealed class ActionBarRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        private int _id;
        internal DocumentCanvas? Component { get; private set; }
        internal int DisposalCount { get; private set; }
        internal List<RenderTreeEditType> LastEdits { get; } = [];
        internal List<RenderTreeEditType> DisabledEdits { get; } = [];
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        internal Task ShowAsync(DocumentCanvasHostState state) => Dispatcher.InvokeAsync(async () =>
        {
            if (Component is null)
            {
                Component = (DocumentCanvas)InstantiateComponent(typeof(ActionBarRenderCanvas));
                _id = AssignRootComponentId(Component);
            }
            typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!.SetValue(Component, state);
            await RenderRootComponentAsync(_id, ParameterView.Empty);
        });

        internal ulong[] ButtonHandlers() => GetCurrentRenderTreeFrames(_id).Array
            .Take(GetCurrentRenderTreeFrames(_id).Count)
            .Where(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onclick" &&
                frame.AttributeEventHandlerId != 0)
            .Select(frame => frame.AttributeEventHandlerId).Skip(1).Take(7).ToArray();

        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            DisposalCount += batch.DisposedComponentIDs.Count;
            foreach (var diff in batch.UpdatedComponents.Array.Take(batch.UpdatedComponents.Count))
            {
                if (diff.ComponentId != _id) continue;
                LastEdits.Clear();
                DisabledEdits.Clear();
                foreach (var edit in diff.Edits)
                {
                    LastEdits.Add(edit.Type);
                    if ((edit.Type == RenderTreeEditType.SetAttribute &&
                        batch.ReferenceFrames.Array[edit.ReferenceFrameIndex].AttributeName == "disabled") ||
                        (edit.Type == RenderTreeEditType.RemoveAttribute && edit.RemovedAttributeName == "disabled"))
                        DisabledEdits.Add(edit.Type);
                }
            }
            return Task.CompletedTask;
        }

        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Action bar rendering failed.", exception);
    }
#pragma warning restore BL0006
}
