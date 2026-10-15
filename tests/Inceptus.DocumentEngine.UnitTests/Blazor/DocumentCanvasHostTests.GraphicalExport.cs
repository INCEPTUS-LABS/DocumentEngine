using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Export;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(0.25, -5000, 7000)]
    [InlineData(1, 0, 0)]
    [InlineData(4, 10000, -10000)]
    public async Task GraphicalExportIsViewportIndependentAndPreservesEditor(double zoom, double panX, double panY)
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(220, 160, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("export", "container");
        var session = Session(host);
        var initial = (await host.CapturePresentationAsync())!.Capture!;
        var expected = SvgGraphicalExporter.Export(GraphicalExportPreparer.Prepare(initial).Scene!);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [BpmnDemoPipeline.TaskVisualId],
            viewport: new ViewportSnapshot(zoom, new(panX, panY), new RectD(90000, 90000, 20, 20))));
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var context = host.CaptureEditorContext();
        var saved = host.NativeSaveStatus;
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var prepared = GraphicalExportPreparer.Prepare(capture).Scene!;
        var result = SvgGraphicalExporter.Export(prepared);
        Assert.True(result.Succeeded);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(expected.Artifact!.Content.AsSpan()), System.Text.Encoding.UTF8.GetString(result.Artifact!.Content.AsSpan()));
        Assert.True(prepared.Bounds.Width > 220);
        Assert.DoesNotContain(prepared.Items, i => (i.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0);
        Assert.Contains(prepared.Items, i => i.Layer == Canvas2DSceneLayer.Decoration);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Same(before.EditorState, session.CaptureState().EditorState);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(context!.Document, host.CaptureEditorContext()!.Document);
        Assert.Equal(saved!.IsDirty, host.NativeSaveStatus!.IsDirty);
        Assert.Equal(saved.SavedCheckpoint, host.NativeSaveStatus.SavedCheckpoint);
        Assert.Equal(saved.CurrentCheckpoint.DocumentId, host.NativeSaveStatus.CurrentCheckpoint.DocumentId);
        Assert.Equal(saved.CurrentCheckpoint.Revision, host.NativeSaveStatus.CurrentCheckpoint.Revision);
        // The demo contains modeling issues; export succeeds without invoking the validation operation.
        Assert.Null(new DemoDocumentOperations(host).ValidationSnapshot);
    }

    [Fact]
    public async Task GraphicalExportCapturesOnlyActiveSubProcessAndDoesNotAddHistory()
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(900, 600, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("export-scope", "container");
        var session = Session(host);
        var root = (await host.CapturePresentationAsync())!.Capture!;
        Assert.Contains(GraphicalExportPreparer.Prepare(root).Scene!.Items, i => i.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId);
        Assert.True((await host.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId))!.Succeeded);
        var before = session.CaptureState();
        var nested = (await host.CapturePresentationAsync())!.Capture!;
        var result = GraphicalExportPreparer.Prepare(nested);
        Assert.True(result.Succeeded);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, nested.ActiveScopeId);
        Assert.DoesNotContain(result.Scene!.Items, i => i.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId);
        Assert.DoesNotContain(result.Scene.Items, i => i.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        Assert.NotEmpty(result.Scene.Items);
        Assert.True(SvgGraphicalExporter.Export(result.Scene).Succeeded);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        await host.UndoAsync();
        Assert.Equal(root.PresentationContext, session.CaptureState().PresentationContext);
        await host.RedoAsync();
        Assert.Equal(nested.PresentationContext, session.CaptureState().PresentationContext);
    }

    [Fact]
    public async Task GraphicalExportKeepsMultiplePoolsAndTheirPersistentDecorations()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("export-pools", "container");
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var result = GraphicalExportPreparer.Prepare(capture);
        Assert.True(result.Succeeded);
        Assert.Contains(result.Scene!.Items, i => i.Origin.SemanticElementId == A22ViewPoolId);
        Assert.Contains(result.Scene.Items, i => i.Origin.SemanticElementId?.Value == "a22:host:peer-pool");
        Assert.Contains(result.Scene.Items, i => i.Origin.SemanticElementId == A22ViewTaskId);
        Assert.Contains(capture.Scene.Items, Canvas2DSceneExportMetadata.IsExcluded);
        Assert.DoesNotContain(result.Scene.Items, Canvas2DSceneExportMetadata.IsExcluded);
        Assert.True(SvgGraphicalExporter.Export(result.Scene).Succeeded);
    }

    [Fact]
    public async Task GraphicalExportIncludesMessageFlowRouteAndDecorationsWithoutPublicationValidation()
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("export-message", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var visual = new Inceptus.DocumentEngine.Contracts.Primitives.VisualStateId("export:message:visual");
        Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            new("export:message"), visual, A22ViewTaskId, A22ToolPeerTask,
            A22SourceAnchor(A22ViewTaskVisualId), new(A22ToolPeerVisual.Value + ":target")))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var prepared = GraphicalExportPreparer.Prepare(capture).Scene!;
        var flow = prepared.Items.Where(item => item.Origin.VisualStateId == visual).ToArray();
        Assert.Contains(flow, item => item.Layer == Canvas2DSceneLayer.Connector && item.Geometry.Points.Length >= 2 && !item.Style.DashPattern.IsEmpty);
        Assert.True(flow.Length >= 3); // Route, source-circle and open target-arrow primitives.
        Assert.True(SvgGraphicalExporter.Export(prepared).Succeeded);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }
}
