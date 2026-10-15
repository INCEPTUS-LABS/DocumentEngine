using System.Reflection;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisplayModeRoundTripsPreserveToolboxAndLiveEditor(bool initiallyCompact)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var culture = new ModelerCultureScope("en");
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1));
        var selection = new ToolboxSelectionState();
        var grid = new TestGridSource(new() { ShowGrid = false, SnapToGrid = true, PointDistance = 25 });
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection, gridSettings: grid);
        await host.InitializeAsync("active", "standby", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(1.4, new VectorD(-100, -80)));
        var visualId = session.CaptureState().CurrentScene!.Items.First(item => item.Origin.VisualStateId is not null).Origin.VisualStateId!;
        var initial = AttachedDocument(session).CaptureSnapshot();
        Assert.True((await session.ExecuteAsync(new Inceptus.DocumentEngine.Contracts.Commands.MoveVisualStateCommand(
            initial.DocumentId, initial.Revision, visualId, new PointD(120, 160),
            Inceptus.DocumentEngine.Contracts.Visuals.VisualPlacementMode.Pinned))).IsCommitted);
        await session.WaitForIdleAsync(timeout.Token);
        await host.UndoAsync(timeout.Token);
        Assert.True(session.CaptureState().HistoryStatus.CanRedo);
        await session.UpdateEditorStateAsync(CopyEditorState(session.CaptureState().EditorState, [visualId]));
        var activator = new DisplayModeActivator(host, selection);
        await using var services = DisplayModeServices(activator);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>());
        var modeler = activator.Modeler;
        var canvas = activator.Canvas;
        var panel = activator.Toolbox;
        var inputs = activator.Inputs.ToArray();
        var facade = typeof(InceptusBpmnModeler).GetField("_facade", PropertiesUxFlags)!.GetValue(modeler);
        var presentation = (ToolboxPresentationState)typeof(ToolboxPanel).GetField("_presentation", PropertiesUxFlags)!.GetValue(panel)!;
        var catalog = panel.Catalog;
        var group = catalog.Groups.First(group => catalog.GetItems(group.GroupId).Length > 0);
        var tool = catalog.GetItems(group.GroupId)[0].ItemId;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Assert.True(panel.ToggleGroup(group.GroupId));
            if (initiallyCompact) panel.ToggleViewMode();
            Assert.True(await panel.SelectItemAsync(tool));
        });

        var document = AttachedDocument(session).CaptureSnapshot();
        var before = session.CaptureState();
        var saved = host.CaptureNativeSaveStatus();
        var hostBefore = host.CaptureState();
        var markup = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        AssertDisplayButton(markup, false);
        Assert.DoesNotContain("hidden", HostToolbarTag(markup));

        for (var cycle = 0; cycle < 4; cycle++)
        {
            await renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            Assert.Equal(ToolboxViewMode.Compact, presentation.ViewMode);
            markup = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            AssertDisplayButton(markup, true);
            Assert.Contains("hidden", HostToolbarTag(markup));
            Assert.Contains("class=\"demo-host\" data-display-mode=\"maximized\"", markup);
            Assert.Equal(before.Generation, session.CaptureState().Generation);
            Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
            Assert.Same(before.EditorState, session.CaptureState().EditorState);
            var maximized = new Canvas2DSurfaceSize(1200, 800, 2);
            await surface.RaiseAsync(maximized);
            Assert.Equal(maximized, host.CaptureState().SurfaceSize);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                Assert.True(await panel.SelectItemAsync(catalog.Items.First(item => item.ItemId != tool).ItemId));
                Assert.True(await panel.SelectItemAsync(tool));
            });
            await renderer.Dispatcher.InvokeAsync(() => panel.ToggleViewMode());
            Assert.Equal(ToolboxViewMode.Expanded, presentation.ViewMode);
            // Parent renders must allow a manual mode change while maximized.
            await renderer.Dispatcher.InvokeAsync(() => modeler.SetParametersAsync(ParameterView.Empty));
            Assert.Equal(ToolboxViewMode.Expanded, presentation.ViewMode);
            await renderer.Dispatcher.InvokeAsync(modeler.ToggleDisplayModeAsync);
            Assert.Equal(initiallyCompact ? ToolboxViewMode.Compact : ToolboxViewMode.Expanded, presentation.ViewMode);
            Assert.Same(catalog, panel.Catalog);
            Assert.Equal(group.GroupId, presentation.ExpandedToolboxGroupId);
            Assert.Equal(tool, selection.SelectedItemId);
            await surface.RaiseAsync(new Canvas2DSurfaceSize(900, 600, 1));
            var after = session.CaptureState();
            Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
            Assert.Equal(before.DocumentRevision, after.DocumentRevision);
            Assert.Equal(before.PresentationContext, after.PresentationContext);
            Assert.Equal(before.HistoryStatus, after.HistoryStatus);
            Assert.Equal(before.EditorState.Selection.ToArray(), after.EditorState.Selection.ToArray());
            Assert.Equal(before.EditorState.Viewport.Zoom, after.EditorState.Viewport.Zoom);
            Assert.Equal(before.EditorState.Viewport.Pan, after.EditorState.Viewport.Pan);
            Assert.Equal(saved!.CurrentCheckpoint, host.CaptureNativeSaveStatus()!.CurrentCheckpoint);
            Assert.Equal(saved.SavedCheckpoint, host.CaptureNativeSaveStatus()!.SavedCheckpoint);
            Assert.Equal(hostBefore.SmartEditEnabled, host.CaptureState().SmartEditEnabled);
            Assert.Same(grid.Grid, session.GridSettings);
            Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
            Assert.Same(before.LayoutResult, after.LayoutResult);
            Assert.Same(before.RoutingResult, after.RoutingResult);
            Assert.True(host.CaptureState().LatestPresentationSucceeded);
            before = after;
        }

        Assert.Same(modeler, activator.Modeler);
        Assert.Same(canvas, activator.Canvas);
        Assert.Same(panel, activator.Toolbox);
        Assert.Equal(inputs, activator.Inputs);
        Assert.Same(facade, typeof(InceptusBpmnModeler).GetField("_facade", PropertiesUxFlags)!.GetValue(modeler));
        markup = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        AssertDisplayButton(markup, false);
        Assert.DoesNotContain("hidden", HostToolbarTag(markup));
        Assert.Contains("class=\"demo-host\" data-display-mode=\"normal\"", markup);
        // Editing remains available through the existing command and history path after resizing.
        var result = await session.ExecuteAsync(new Inceptus.DocumentEngine.Contracts.Commands.MoveVisualStateCommand(
            document.DocumentId, document.Revision, visualId, new PointD(120, 160),
            Inceptus.DocumentEngine.Contracts.Visuals.VisualPlacementMode.Pinned));
        Assert.True(result.IsCommitted);
        Assert.True(session.CaptureState().HistoryStatus.CanUndo);
        await session.WaitForIdleAsync(timeout.Token);
        await host.UndoAsync(timeout.Token);
        Assert.True(session.CaptureState().HistoryStatus.CanRedo);
        await host.RedoAsync(timeout.Token);
        Assert.True(session.CaptureState().HistoryStatus.CanUndo);
    }

    [Fact]
    public async Task DisplayModeRapidClicksSerializeAsyncCallbacksAndStopOnDisposal()
    {
        using var culture = new ModelerCultureScope("en");
        var activator = new DisplayModeActivator();
        await using var services = DisplayModeServices(activator);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<BpmnModelerDisplayMode>();
        var callback = EventCallback.Factory.Create<BpmnModelerDisplayMode>(new object(), async mode =>
        {
            observed.Add(mode);
            await release.Task;
        });
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<InceptusBpmnModeler>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(InceptusBpmnModeler.DisplayModeChanged)] = callback })));
        var pending = new List<Task>();
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            for (var i = 0; i < 20; i++) pending.Add(activator.Modeler.ToggleDisplayModeAsync());
        });
        Assert.Equal([BpmnModelerDisplayMode.Maximized], observed);
        release.SetResult();
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(20, observed.Count);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? BpmnModelerDisplayMode.Maximized : BpmnModelerDisplayMode.Normal), observed);
        AssertDisplayButton(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString), false);

        release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Clear();
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            pending.Add(activator.Modeler.ToggleDisplayModeAsync());
            pending.Add(activator.Modeler.ToggleDisplayModeAsync());
            activator.Modeler.Dispose();
        });
        release.SetResult();
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(21, observed.Count);
        await renderer.Dispatcher.InvokeAsync(activator.Modeler.ToggleDisplayModeAsync);
        Assert.Equal(21, observed.Count);
    }

    [Theory]
    [InlineData("en", "Maximize drawing area", "Restore drawing area")]
    [InlineData("pl", "Maksymalizuj obszar rysowania", "Przywróć obszar rysowania")]
    [InlineData("de", "Zeichenfläche maximieren", "Zeichenfläche wiederherstellen")]
    [InlineData("fr", "Agrandir la zone de dessin", "Restaurer la zone de dessin")]
    [InlineData("es", "Maximizar el área de dibujo", "Restaurar el área de dibujo")]
    public async Task DisplayModeHostParametersSynchronizeWithoutEchoAndLabelsAreLocalized(string cultureName, string maximize, string restore)
    {
        using var culture = new ModelerCultureScope(cultureName);
        var activator = new DisplayModeActivator();
        await using var services = DisplayModeServices(activator);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var callbacks = 0;
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<InceptusBpmnModeler>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InceptusBpmnModeler.DisplayMode)] = BpmnModelerDisplayMode.Maximized,
                [nameof(InceptusBpmnModeler.DisplayModeChanged)] = EventCallback.Factory.Create<BpmnModelerDisplayMode>(new object(), _ => callbacks++),
            })));
        Assert.Contains($"aria-label=\"{restore}\"", System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        Assert.Equal(0, callbacks);
        await renderer.Dispatcher.InvokeAsync(() => activator.Modeler.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(InceptusBpmnModeler.DisplayMode)] = BpmnModelerDisplayMode.Normal })));
        Assert.Contains($"title=\"{maximize}\"", System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        Assert.Equal(0, callbacks);
        await renderer.Dispatcher.InvokeAsync(activator.Modeler.ToggleDisplayModeAsync);
        await renderer.Dispatcher.InvokeAsync(() => activator.Modeler.SetParametersAsync(ParameterView.Empty));
        Assert.Contains($"title=\"{restore}\"", System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        Assert.Equal(1, callbacks);
    }

    private static void AssertDisplayButton(string markup, bool maximized)
    {
        var button = Regex.Match(markup, "<button[^>]*class=\"display-mode-toggle\"[^>]*>.*?</button>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(button);
        Assert.Contains("type=\"button\"", button);
        Assert.Contains($"aria-pressed=\"{(maximized ? "true" : "false")}\"", button);
        Assert.Contains($"title=\"{(maximized ? "Restore" : "Maximize")} drawing area\"", button);
        Assert.Contains($"aria-label=\"{(maximized ? "Restore" : "Maximize")} drawing area\"", button);
        Assert.Contains("<svg", button);
        Assert.Contains("focusable=\"false\"", button);
        Assert.Contains(maximized ? "M3 3l6 6" : "M9 9L3 3", button);
        Assert.DoesNotContain("disabled", button);
    }

    private static string HostToolbarTag(string markup) => Regex.Match(markup, "<div class=\"editor-toolbar host-toolbar\"[^>]*>").Value;

    private static ServiceProvider DisplayModeServices(IComponentActivator activator) =>
        new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton(activator).BuildServiceProvider();

    private sealed class DisplayModeActivator(DocumentCanvasHost? host = null, ToolboxSelectionState? selection = null) : IComponentActivator
    {
        internal InceptusBpmnModeler Modeler { get; private set; } = null!;
        internal DocumentCanvas Canvas { get; private set; } = null!;
        internal ToolboxPanel Toolbox { get; private set; } = null!;
        internal List<InputFile> Inputs { get; } = [];

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InceptusBpmnModeler modeler) Modeler = modeler;
            if (component is ToolboxPanel toolbox) Toolbox = toolbox;
            if (component is InputFile input) Inputs.Add(input);
            if (component is BpmnDemoHost demo && host is not null) demo.AttachEditor(host, host.Operations());
            if (component is DocumentCanvas canvas)
            {
                Canvas = canvas;
                if (host is not null)
                {
                    typeof(DocumentCanvas).GetField("_host", PropertiesUxFlags)!.SetValue(canvas, host);
                    typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!.SetValue(canvas, host.CaptureState());
                    typeof(DocumentCanvas).GetField("_toolboxSelection", PropertiesUxFlags)!.SetValue(canvas, selection);
                }
            }
            return component;
        }
    }
}
