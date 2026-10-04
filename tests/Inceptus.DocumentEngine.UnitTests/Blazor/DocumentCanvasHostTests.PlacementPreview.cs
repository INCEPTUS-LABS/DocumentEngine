using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task PlacementPreviewHidesCursorOnlyWithPresentedBodyAndRestoresOnLeaveCancelAndToolSwitch()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 2d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        await ArmPlacementTask(host, selection);
        Assert.Equal("crosshair", host.CaptureState().CssCursor);
        var pointer = PointerObserver(host);
        await pointer.MoveDocumentPointAsync(before.CurrentScene!, new PointD(750d, 450d));
        AssertPresentedPlacement(host);
        await pointer.LeaveAsync();
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Empty(session.CaptureState().EditorState.TemporaryFeedback);
        Assert.NotNull(selection.SelectedItemId);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(760d, 450d));
        AssertPresentedPlacement(host);
        await pointer.CancelAsync(1);
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Empty(session.CaptureState().EditorState.TemporaryFeedback);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(770d, 450d));
        AssertPresentedPlacement(host);
        var gateway = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions)
            .Items.First(item => item.ElementTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.True(selection.Select(gateway.ItemId));
        await host.RefreshToolboxPlacementAsync();
        Assert.Equal("crosshair", host.CaptureState().CssCursor);
        Assert.Empty(session.CaptureState().EditorState.TemporaryFeedback);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(770d, 450d));
        Assert.Equal(new SizeD(48d, 48d), AssertPresentedPlacement(host).Bounds.Size);
        await host.CancelToolboxPlacementAsync();
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Empty(session.CaptureState().EditorState.TemporaryFeedback);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task PlacementPreviewReevaluatesAtStationaryCssPointerAfterZoomWheelAndMiddlePan()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1.5d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        await ArmPlacementTask(host, selection);
        var session = Session(host);
        var before = session.CaptureState();
        var point = new PointD(750d, 450d);
        var css = before.CurrentScene!.ViewportTransform.TransformPoint(point);
        var pointer = PointerObserver(host);
        await pointer.MoveDocumentPointAsync(before.CurrentScene, point);
        AssertPresentedPlacement(host);
        await host.ZoomInAsync();
        AssertPlacementAtCss(host, css);
        await pointer.WheelAsync(25d, 40d);
        AssertPlacementAtCss(host, css);
        await pointer.MiddleDownCssPointAsync(css);
        Assert.Equal("grabbing", host.CaptureState().CssCursor);
        Assert.Empty(session.CaptureState().EditorState.TemporaryFeedback);
        var moved = css + new VectorD(-30d, -20d);
        await pointer.MiddleMoveCssPointAsync(moved);
        await pointer.MiddleUpCssPointAsync(moved);
        AssertPlacementAtCss(host, moved);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task PlacementPreviewRenderFailureRestoresVisibleCursorAndNeverCommits()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        await ArmPlacementTask(host, selection);
        var before = Session(host).CaptureState();
        await PointerObserver(host).MoveDocumentPointAsync(before.CurrentScene!, new PointD(750d, 450d));
        AssertPresentedPlacement(host);
        execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = false, Code = "TEST_RENDER_FAILED" };
        await PointerObserver(host).MoveDocumentPointAsync(Session(host).CaptureState().CurrentScene!, new PointD(760d, 450d));
        Assert.NotEqual("none", host.CaptureState().CssCursor);
        Assert.False(Session(host).CaptureState().IsCurrentScenePresented);
        Assert.Equal(before.DocumentRevision, Session(host).CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, Session(host).CaptureState().HistoryStatus);
        Assert.NotNull(selection.SelectedItemId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementPreviewRetiresAcrossImportAndNewDocument(bool newDiagram)
    {
        var selection = new ToolboxSelectionState();
        var first = new RecordingRenderExecution();
        var replacement = new RecordingRenderExecution();
        await using var host = CreateHost(first,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: selection,
            replacementRendererFactory: () => CreateRenderer(replacement));
        await host.InitializeAsync("active", "standby", "container");
        var previous = Session(host);
        var native = NativeDocumentSerializer.Export(AttachedDocument(previous));
        await ArmPlacementTask(host, selection);
        await PointerObserver(host).MoveDocumentPointAsync(previous.CaptureState().CurrentScene!, new PointD(750d, 450d));
        AssertPresentedPlacement(host);
        if (newDiagram)
        {
            Assert.True((await host.NewDiagramAsync()).Succeeded);
        }
        else
        {
            Assert.True((await host.ImportNativeDocumentAsync(native.AsMemory())).Succeeded);
        }
        Assert.NotSame(previous, Session(host));
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Null(selection.SelectedItemId);
        Assert.Empty(Session(host).CaptureState().EditorState.TemporaryFeedback);
        Assert.DoesNotContain(Session(host).CaptureState().CurrentScene!.Items,
            item => item.Origin.StableSourceKey?.StartsWith("feedback:inceptus:toolbox-placement-candidate:", StringComparison.Ordinal) == true);
        Assert.Equal(1, first.DisposeCount);
    }

    [Fact]
    public async Task PlacementPreviewRetiresOnScopeNavigationAndDisposal()
    {
        var selection = new ToolboxSelectionState();
        var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await using var lifetime = host;
        await host.InitializeAsync("canvas", "container");
        await ArmPlacementTask(host, selection);
        var session = Session(host);
        await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(750d, 450d));
        AssertPresentedPlacement(host);
        Assert.True((await session.NavigateToScopeAsync(
            Inceptus.DocumentEngine.Blazor.Demo.BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await session.WaitForIdleAsync();
        await WaitForCursorAsync(host, "default");
        Assert.DoesNotContain(session.CaptureState().EditorState.TemporaryFeedback,
            item => item.PlacementPreview is not null);
        if (selection.SelectedItemId is null)
        {
            await ArmPlacementTask(host, selection);
        }
        await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(750d, 450d));
        AssertPresentedPlacement(host);
        var pointer = PointerObserver(host);
        await host.DisposeAsync();
        Assert.Equal("default", pointer.CursorValues.Last());
        Assert.Equal(1, pointer.DisposeCount);
    }

    [Fact]
    public async Task PlacementPreviewNeverEntersNativeExportOrPublishedProcessPackage()
    {
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var nativeBefore = NativeDocumentSerializer.Export(document);
        var publishedBefore = await host.PublishProcessAsync();
        Assert.True(publishedBefore.Succeeded, PublishDiagnostics(publishedBefore.Diagnostics));
        await ArmPlacementTask(host, selection);
        await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(750d, 450d));
        AssertPresentedPlacement(host);
        var publishedDuring = await host.PublishProcessAsync();
        Assert.True(publishedDuring.Succeeded, PublishDiagnostics(publishedDuring.Diagnostics));
        Assert.True(nativeBefore.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(document).AsSpan()));
        Assert.True(publishedBefore.Payload.AsSpan().SequenceEqual(publishedDuring.Payload.AsSpan()));
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        AssertPresentedPlacement(host);
    }

    private static async Task ArmPlacementTask(DocumentCanvasHost host, ToolboxSelectionState selection)
    {
        var task = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions)
            .Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        Assert.True(selection.Select(task.ItemId));
        await host.RefreshToolboxPlacementAsync();
    }

    private static ToolboxPlacementPreview AssertPresentedPlacement(DocumentCanvasHost host)
    {
        var state = Session(host).CaptureState();
        Assert.True(state.IsCurrentScenePresented);
        Assert.Equal("none", host.CaptureState().CssCursor);
        var feedback = Assert.Single(state.EditorState.TemporaryFeedback, item => item.PlacementPreview is not null);
        Assert.Contains(state.CurrentScene!.Items, item => item.Bounds == feedback.Bounds &&
            item.Origin.StableSourceKey == $"feedback:{feedback.Id}:body" &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None &&
            item.Origin.SemanticElementId is null && item.Origin.VisualStateId is null);
        return feedback.PlacementPreview!;
    }

    private static void AssertPlacementAtCss(DocumentCanvasHost host, PointD css)
    {
        AssertPresentedPlacement(host);
        var state = Session(host).CaptureState();
        var feedback = Assert.Single(state.EditorState.TemporaryFeedback, item => item.PlacementPreview is not null);
        var actual = state.CurrentScene!.ViewportTransform.TransformPoint(Center(feedback.Bounds!.Value));
        Assert.Equal(css.X, actual.X, 8);
        Assert.Equal(css.Y, actual.Y, 8);
    }
}
