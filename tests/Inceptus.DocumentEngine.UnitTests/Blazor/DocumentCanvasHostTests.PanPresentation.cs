using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Organizational.ContextMenus;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task PanPresentationPreservesSessionStatesAndRendersCompletion(bool succeeds, bool middlePointer)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        var pointer = PointerObserver(test.Host);
        if (middlePointer)
        {
            await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleDownCssPointAsync(new PointD(200, 200)));
        }

        var before = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var originalMarkup = await test.MarkupAsync();
        var states = new ConcurrentQueue<EditingSessionState>();
        var hints = new ConcurrentQueue<EditingSessionGeneration?>();
        session.StateChanged += (_, args) => states.Enqueue(args.State);
        test.Host.StateChanged += hint => { hints.Enqueue(hint); return Task.CompletedTask; };
        test.ResetCounts();
        test.Execution.BlockRender = true;
        test.Execution.RenderResult = new Canvas2DInteropOperationResult
        {
            Succeeded = succeeds,
            Code = succeeds ? null : "TEST_PAN_PRESENTATION_FAILURE",
        };
        var operation = test.Renderer.Dispatcher.InvokeAsync(() => middlePointer
            ? pointer.MiddleMoveCssPointAsync(new PointD(211, 207))
            : pointer.WheelAsync(-11, -7));
        try
        {
            await test.Execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await test.DrainAsync();
            Assert.Contains(states, state => state.Status == EditingSessionStatus.Rebuilding);
            Assert.Contains(states, state => state.Status == EditingSessionStatus.Ready && !state.IsCurrentScenePresented);
            Assert.True(session.CaptureState().IsPanPresentationPending);
            Assert.False(test.Host.CaptureState().LatestPresentationSucceeded);
            Assert.Equal(originalMarkup, await test.MarkupAsync());
            Assert.Equal(0, test.Activator.Canvas.BuildCount);
        }
        finally
        {
            test.Execution.ReleaseRender();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await test.DrainAsync();
        var final = session.CaptureState();
        Assert.Equal(3, states.Count);
        Assert.Equal(3, hints.Count);
        Assert.All(states, state => Assert.Equal(final.Generation, state.Generation));
        Assert.Equal(before.Generation.Value + 1, final.Generation.Value);
        Assert.Equal(new VectorD(11, 7) + before.EditorState.Viewport.Pan, final.EditorState.Viewport.Pan);
        Assert.All(states.Take(2), state => Assert.True(state.IsPanPresentationPending));
        Assert.False(final.IsPanPresentationPending);
        Assert.Equal(succeeds, final.IsCurrentScenePresented);
        Assert.Equal(succeeds, test.Host.CaptureState().LatestPresentationSucceeded);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, final.HistoryStatus);
        Assert.Equal(before.CurrentScene!.Items, final.CurrentScene!.Items);
        Assert.Equal(0, test.Execution.FullUploadCount - test.FullUploadsBefore);
        Assert.InRange(test.Activator.Canvas.BuildCount, 1, 2);
        Assert.Equal(0, test.Activator.Toolbox.BuildCount);
        Assert.InRange(test.Activator.Input.BuildCount, 1, 2);
        var markup = await test.MarkupAsync();
        Assert.Contains(succeeds ? "data-presentation-status=\"ready\"" : "data-presentation-status=\"renderfaulted\"", markup);
        Assert.Contains("data-scene-status=\"ready\"", markup);
        Assert.Contains("data-viewport-pan-x=\"-489\"", markup);
        Assert.Contains("data-viewport-pan-y=\"-493\"", markup);
        await test.Renderer.Dispatcher.InvokeAsync(test.Activator.Canvas.RefreshAsync);
        Assert.Equal(markup, await test.MarkupAsync()); // Same final output as an unconditional full refresh.
        if (!succeeds)
        {
            Assert.NotEmpty(final.PresentationDiagnostics);
            test.Execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = true };
            await test.Renderer.Dispatcher.InvokeAsync(() => session.RenderCurrentAsync().AsTask());
            await test.DrainAsync();
            Assert.True(session.CaptureState().IsCurrentScenePresented);
            Assert.Contains("data-presentation-status=\"ready\"", await test.MarkupAsync());
            Assert.Empty(session.CaptureState().PresentationDiagnostics);
        }
    }

    [Theory]
    [InlineData("toolbox")]
    [InlineData("properties")]
    [InlineData("import")]
    [InlineData("zoom")]
    [InlineData("validation")]
    [InlineData("pointer")]
    public async Task PanPresentationQueuesActionsUntilRendererCompletes(string action)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        var before = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var body = before.CurrentScene!.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
        var visualId = body.Origin.VisualStateId!;
        if (action == "properties")
        {
            await test.Renderer.Dispatcher.InvokeAsync(() => session.UpdateEditorStateAsync(
                new Inceptus.DocumentEngine.Contracts.EditorState.EditorStateSnapshot(
                    selection: [visualId], viewport: before.EditorState.Viewport)).AsTask());
            before = session.CaptureState();
        }
        var imported = NativeDocumentSerializer.Export(CreateImportedDocument("test:a125:queued-import", 31));
        test.Execution.BlockRender = true;
        test.ResetCounts();
        var pan = test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).WheelAsync(11, 7));
        Task? queued = null;
        try
        {
            await test.Execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await test.DrainAsync();
            Assert.Equal(0, test.Activator.Canvas.BuildCount);
            if (action == "toolbox")
            {
                Assert.True(test.Selection.Select(BpmnModelerCompositionFactory.ToolboxCatalog.Items[0].ItemId));
            }
            queued = action switch
            {
                "toolbox" => test.Host.RefreshToolboxPlacementAsync().AsTask(),
                "properties" => test.Host.OpenPropertiesAsync(visualId).AsTask(),
                "import" => test.Host.ImportNativeDocumentAsync(imported.AsMemory()).AsTask(),
                "zoom" => test.Host.ZoomInAsync().AsTask(),
                "validation" => test.Host.ValidateAsync().AsTask(),
                "pointer" => PointerObserver(test.Host).WheelAsync(3, 5),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
            Assert.False(queued.IsCompleted);
            Assert.False(session.CaptureState().IsCurrentScenePresented);
            Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
            Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        }
        finally
        {
            test.Execution.ReleaseRender();
            await pan.WaitAsync(TimeSpan.FromSeconds(10));
            if (queued is not null)
            {
                await queued.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        await test.DrainAsync();
        Assert.Equal(1, test.Execution.MaximumConcurrency);
        Assert.Equal(EditingSessionStatus.Ready, Session(test.Host).CaptureState().Status);
        Assert.True(Session(test.Host).CaptureState().IsCurrentScenePresented);
        Assert.True(test.Activator.Canvas.BuildCount > 0);
        if (action == "properties") { Assert.True(test.Host.CaptureState().PropertiesFormOpen); }
        if (action == "import") { Assert.Equal("test:a125:queued-import", Session(test.Host).CaptureState().DocumentId.Value); }
        if (action == "validation") { Assert.NotNull(test.Host.CaptureState().ValidationSnapshot); }
    }

    [Theory]
    [InlineData("zoom")]
    [InlineData("validation")]
    [InlineData("issues")]
    [InlineData("selection")]
    [InlineData("hover")]
    [InlineData("css")]
    [InlineData("dpr")]
    [InlineData("import")]
    [InlineData("new-diagram")]
    [InlineData("properties")]
    [InlineData("properties-apply")]
    [InlineData("scope")]
    [InlineData("profile")]
    [InlineData("pool-collapse")]
    public async Task PanPresentationFallsBackForOtherEditorChanges(string action)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        var hints = new ConcurrentQueue<EditingSessionGeneration?>();
        test.Host.StateChanged += hint => { hints.Enqueue(hint); return Task.CompletedTask; };
        test.ResetCounts();
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var scene = session.CaptureState().CurrentScene!;
            var body = scene.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
            if (action is "properties" or "properties-apply")
            {
                var editor = session.CaptureState().EditorState;
                Assert.True((await session.UpdateEditorStateAsync(new Inceptus.DocumentEngine.Contracts.EditorState.EditorStateSnapshot(
                    selection: [BpmnDemoPipeline.TaskVisualId], viewport: editor.Viewport))).Succeeded);
            }
            switch (action)
            {
                case "zoom": await test.Host.ZoomInAsync(); break;
                case "validation": await test.Host.ValidateAsync(); break;
                case "issues":
                    InvokePublishComponent(test.Activator.Canvas, "ToggleIssuesPanel");
                    await test.Activator.Canvas.RefreshAsync();
                    break;
                case "selection": await PointerObserver(test.Host).ClickDocumentPointAsync(scene, Center(body.Bounds)); break;
                case "hover": await PointerObserver(test.Host).MoveDocumentPointAsync(scene, Center(body.Bounds)); break;
                case "css": await test.Surface.RaiseAsync(new Canvas2DSurfaceSize(1000, 600, 1)); break;
                case "dpr": await test.Surface.RaiseAsync(new Canvas2DSurfaceSize(900, 600, 2)); break;
                case "import":
                    Assert.True((await test.Host.ImportNativeDocumentAsync(NativeDocumentSerializer.Export(
                        CreateImportedDocument("test:a125:fallback-import", 31)).AsMemory())).Succeeded);
                    break;
                case "new-diagram": Assert.True((await test.Host.NewDiagramAsync()).Succeeded); break;
                case "properties":
                    Assert.NotNull(await test.Host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));
                    break;
                case "properties-apply":
                    var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
                        await test.Host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));
                    var draft = new DocumentCanvasPropertiesDraft(properties)
                    {
                        X = DocumentCanvasPropertiesDraft.Format(properties.Bounds.X + 20),
                    };
                    test.Host.UpdatePropertiesFormState(true, BpmnDemoPipeline.TaskVisualId, true);
                    Assert.True((await test.Host.ApplyPropertiesAsync(draft)).Succeeded);
                    break;
                case "scope": Assert.True((await test.Host.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId))?.Succeeded); break;
                case "profile":
                    await EnableOrganizationalProfileAsync(session);
                    Assert.True((await session.UpdateModelProfileViewStateAsync(session.CaptureState()
                        .ModelProfileViewState.WithPreferredVisibility(BpmnModelProfiles.OrganizationalId, false))).Succeeded);
                    break;
                case "pool-collapse":
                    await EnableOrganizationalProfileAsync(session);
                    var pool = await AddOrganizationalPoolFromMenuAsync(test.Host);
                    await OpenOrganizationalPoolContextAsync(test.Host, pool);
                    Assert.True((await test.Host.ExecuteSemanticSceneViewContextActionAsync(OrganizationalPoolActions.CollapseId))?.Succeeded);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        });
        await test.DrainAsync();
        Assert.True(test.Activator.Canvas.BuildCount > 0);
        Assert.All(hints, Assert.Null);
        var markup = await test.MarkupAsync();
        await test.Renderer.Dispatcher.InvokeAsync(test.Activator.Canvas.RefreshAsync);
        Assert.Equal(markup, await test.MarkupAsync());
    }

    [Fact]
    public async Task PanPresentationRebuildingStillRejectsImportAndNewDiagramInManagedComponent()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var observed = false;
        Exception? observerFailure = null;
        Session(test.Host).StateChanged += (_, args) =>
        {
            if (args.State.Status != EditingSessionStatus.Rebuilding) { return; }
            observed = true;
            observerFailure = Record.Exception(() =>
            {
                var component = test.Activator.Canvas;
                Assert.False((bool)typeof(DocumentCanvas).GetProperty("CanUseNativeDocumentTransfer", PropertiesUxFlags)!.GetValue(component)!);
                var import = (Task)typeof(DocumentCanvas).GetMethod("ImportNativeDocumentAsync", PropertiesUxFlags)!
                    .Invoke(component, [new InputFileChangeEventArgs([])])!;
                Assert.True(import.IsCompletedSuccessfully);
                InvokePublishComponent(component, "OpenNewDiagramConfirmation");
                Assert.False((bool)typeof(DocumentCanvas).GetField("_newDiagramConfirmationOpen", PropertiesUxFlags)!.GetValue(component)!);
                Assert.False((bool)typeof(DocumentCanvas).GetField("_nativeDocumentImportInProgress", PropertiesUxFlags)!.GetValue(component)!);
            });
        };
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).WheelAsync(11, 7));
        Assert.True(observed);
        Assert.Null(observerFailure); // Session observers isolate exceptions; assert outside their delivery.
    }

    [Fact]
    public async Task PanPresentationDelayedPendingHintCannotSuppressCompletionOrNewGeneration()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var hints = new ConcurrentQueue<EditingSessionGeneration?>();
        test.Host.StateChanged += hint => { hints.Enqueue(hint); return Task.CompletedTask; };
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).WheelAsync(11, 7));
        var pending = hints.First(hint => hint is not null);
        var callback = typeof(DocumentCanvas).GetMethod("HandleHostStateChangedAsync", PropertiesUxFlags)!
            .CreateDelegate<Func<EditingSessionGeneration?, Task>>(test.Activator.Canvas);
        test.ResetCounts();
        await callback(pending);
        Assert.True(test.Activator.Canvas.BuildCount > 0);
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.ZoomInAsync().AsTask());
        test.ResetCounts();
        await callback(pending);
        Assert.True(test.Activator.Canvas.BuildCount > 0);
        Assert.Contains("data-zoom-percentage=\"110\"", await test.MarkupAsync());
    }

    [Theory]
    [InlineData("unknown-contributor")]
    [InlineData("changing-guides")]
    [InlineData("issues-open")]
    [InlineData("tool-selected")]
    public async Task PanPresentationUsesConservativeRenderingOutsideQuietEligiblePan(string reason)
    {
        await using var test = await PanComponentFixture.CreateAsync(reason != "unknown-contributor");
        if (reason == "issues-open")
        {
            await test.Renderer.Dispatcher.InvokeAsync(() =>
            {
                InvokePublishComponent(test.Activator.Canvas, "ToggleIssuesPanel");
                return test.Activator.Canvas.RefreshAsync();
            });
        }
        if (reason == "tool-selected")
        {
            test.Selection.Select(BpmnModelerCompositionFactory.ToolboxCatalog.Items[0].ItemId);
        }
        test.ResetCounts();
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).WheelAsync(
            reason == "changing-guides" ? -700 : 11, reason == "changing-guides" ? -700 : 7));
        await test.DrainAsync();
        if (reason == "changing-guides")
        {
            Assert.InRange(test.Activator.Canvas.BuildCount, 1, 2);
            Assert.Equal(0, test.Activator.Toolbox.BuildCount);
            Assert.Equal(test.FullUploadsBefore, test.Execution.FullUploadCount);
            Assert.Equal(2, Session(test.Host).CaptureState().CurrentScene!.BoundaryGuides.Items.Length);
        }
        else
        {
            Assert.True(test.Activator.Canvas.BuildCount >= 3);
        }
        var markup = await test.MarkupAsync();
        await test.Renderer.Dispatcher.InvokeAsync(test.Activator.Canvas.RefreshAsync);
        Assert.Equal(markup, await test.MarkupAsync());
    }

    private sealed class PanComponentFixture : IAsyncDisposable
    {
        internal RecordingRenderExecution Execution { get; } = new();
        internal RecordingSurfaceObserver Surface { get; } = new(new Canvas2DSurfaceSize(900, 600, 1));
        internal ToolboxSelectionState Selection { get; } = new();
        internal DocumentCanvasHost Host { get; private set; } = null!;
        internal PanComponentActivator Activator { get; private set; } = null!;
        internal HtmlRenderer Renderer { get; private set; } = null!;
        internal int FullUploadsBefore { get; private set; }
        private ServiceProvider _services = null!;
        private HtmlRootComponent _rendered;

        internal static async Task<PanComponentFixture> CreateAsync(bool invariantContributors = true)
        {
            var test = new PanComponentFixture();
            test.Host = CreateHost(test.Execution, test.Surface,
                compositionFactory: invariantContributors ? BpmnModelerTestComposition.DemoFactory : null,
                toolboxSelection: test.Selection,
                replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
            await test.Host.InitializeAsync("active", "standby", "container");
            await PointerObserver(test.Host).WheelAsync(500, 500);
            test.Activator = new PanComponentActivator(test.Host, test.Selection);
            test._services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
                .AddSingleton<IJSRuntime>(new PublishDownloadRuntime())
                .AddSingleton<IComponentActivator>(test.Activator).BuildServiceProvider();
            test.Renderer = new HtmlRenderer(test._services, NullLoggerFactory.Instance);
            test._rendered = await test.Renderer.Dispatcher.InvokeAsync(
                () => test.Renderer.RenderComponentAsync<DocumentCanvas>());
            // The existing HtmlRenderer seam omits OnAfterRender; attach the real callback.
            test.Host.StateChanged += typeof(DocumentCanvas).GetMethod("HandleHostStateChangedAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate<Func<EditingSessionGeneration?, Task>>(test.Activator.Canvas);
            return test;
        }

        internal Task DrainAsync() => Renderer.Dispatcher.InvokeAsync(() => { });
        internal Task<string> MarkupAsync() => Renderer.Dispatcher.InvokeAsync(_rendered.ToHtmlString);
        internal void ResetCounts()
        {
            Activator.Canvas.BuildCount = 0;
            Activator.Toolbox.BuildCount = 0;
            Activator.Input.BuildCount = 0;
            FullUploadsBefore = Execution.FullUploadCount;
        }

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Host.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class PanComponentActivator(DocumentCanvasHost host, ToolboxSelectionState selection) : IComponentActivator
    {
        internal CountingPanCanvas Canvas { get; private set; } = null!;
        internal CountingPanToolbox Toolbox { get; private set; } = null!;
        internal CountingPanInput Input { get; private set; } = null!;

        public IComponent CreateInstance(Type componentType)
        {
            if (componentType == typeof(DocumentCanvas))
            {
                Canvas = new CountingPanCanvas();
                typeof(DocumentCanvas).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Canvas, host);
                typeof(DocumentCanvas).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Canvas, host.CaptureState());
                typeof(DocumentCanvas).GetField("_toolboxSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Canvas, selection);
                return Canvas;
            }
            if (componentType == typeof(ToolboxPanel)) { return Toolbox = new CountingPanToolbox(); }
            if (componentType == typeof(InputFile)) { return Input = new CountingPanInput(); }
            return (IComponent)System.Activator.CreateInstance(componentType)!;
        }
    }

    private sealed class CountingPanCanvas : DocumentCanvas
    {
        internal int BuildCount { get; set; }
        internal Task RefreshAsync() => InvokeAsync(StateHasChanged);
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            BuildCount++;
            base.BuildRenderTree(builder);
        }
    }

    private sealed class CountingPanToolbox : ToolboxPanel
    {
        internal int BuildCount { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            BuildCount++;
            base.BuildRenderTree(builder);
        }
    }

    private sealed class CountingPanInput : InputFile
    {
        internal int BuildCount { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            BuildCount++;
            base.BuildRenderTree(builder);
        }
    }
}
