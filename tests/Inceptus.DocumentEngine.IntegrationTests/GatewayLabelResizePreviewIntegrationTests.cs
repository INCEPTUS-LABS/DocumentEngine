using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Xunit.Abstractions;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class GatewayLabelResizePreviewIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [MemberData(nameof(GatewayLabelResizeIntegrationTests.MinimumCases), MemberType = typeof(GatewayLabelResizeIntegrationTests))]
    public async Task BoundaryTracksEveryResizePreviewFrame(int family, double zoom, string role, string cursor)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var visual = family switch
        {
            0 => BpmnDemoPipeline.ExclusiveGatewayVisualId,
            1 => BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            2 => BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            _ => BpmnDemoPipeline.EventBasedGatewayVisualId,
        };
        await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, zoom == 1 ? 1 : 2));
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visual],
            viewport: new ViewportSnapshot(zoom, new VectorD(35, 19))));
        var label = test.State.CurrentScene!.Items.Single(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visual &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        var zone = test.State.CurrentScene.Items.Single(item =>
            item.Origin.StableSourceKey == $"node-label-resize-zone:{role}:{label.Id.Value}");
        var start = new PointD(zone.Bounds.X + zone.Bounds.Width / 2, zone.Bounds.Y + zone.Bounds.Height / 2);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(start));
        await controller.PointerPressedAsync(Pointer(start, 1));
        var captured = test.State.EditorState.ActiveGesture!;
        var before = test.Snapshot;
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        var x = role.Contains("west", StringComparison.Ordinal) ? -1 : role.Contains("east", StringComparison.Ordinal) ? 1 : 0;
        var y = role.Contains("north", StringComparison.Ordinal) ? -1 : role.Contains("south", StringComparison.Ordinal) ? 1 : 0;
        foreach (var delta in new[] { 15d, 50d, -200d, -240d })
        {
            var result = await controller.PointerMovedAsync(Pointer(start + new VectorD(x * delta, y * delta), 1));
            Assert.Equal(cursor, result.CssCursor);
            var scene = test.State.CurrentScene!;
            var preview = scene.Items.Single(item => item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId &&
                item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
            var boundary = Assert.Single(scene.Items, item => item.Origin.StableSourceKey == $"hover:{label.Id.Value}");
            output.WriteLine($"Pointer delta={delta}; preview={preview.Bounds}; boundary={boundary.Bounds}");
            Assert.True(boundary.IsVisible);
            Assert.Equal(preview.Bounds, boundary.Bounds);
            Assert.True(preview.Bounds.Width >= 20 && preview.Bounds.Height >= 20);
            Assert.Equal(captured.Properties, test.State.EditorState.ActiveGesture!.Properties);
            Assert.Equal(captured.Id, test.State.EditorState.ActiveGesture.Id);
            Assert.Contains(label.Id, boundary.Origin.RelatedSceneObjectIds);
            Assert.Same(before, test.Snapshot);
            Assert.Equal(history, test.State.HistoryStatus);
            Assert.Equal(events, test.Events.Count);
        }

        Canvas2DPointerInput Pointer(PointD point, int buttons = 0) =>
            new(82, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
    }

    [Fact]
    public async Task HundredResizeMovesReportRemainingPresentationWork()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [BpmnDemoPipeline.ExclusiveGatewayVisualId]));
        var label = test.State.CurrentScene!.Items.Single(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        var zone = test.State.CurrentScene.Items.Single(item =>
            item.Origin.StableSourceKey == $"node-label-resize-zone:east:{label.Id.Value}");
        var start = new PointD(zone.Bounds.X + zone.Bounds.Width / 2, zone.Bounds.Y + zone.Bounds.Height / 2);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(start));
        await controller.PointerPressedAsync(Pointer(start, 1));
        var snapshot = test.Snapshot;
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        var uploads = test.Execution.FullUploadCount;
        var frames = test.Execution.RenderCount;
        var rebuilds = test.Pipeline.Rebuilds;
        var contributions = test.Contributions.Sum(item => item.Calls);
        var interopMeasurements = test.Execution.MeasurementRequests.Count;
        var notifications = 0;
        test.Session.StateChanged += (_, _) => Interlocked.Increment(ref notifications);
        var recreatedNodes = 0;
        var recreatedConnectors = 0;
        var recreatedLabels = 0;
        for (var index = 1; index <= 100; index++)
        {
            var previous = test.State.CurrentScene!.Items.ToDictionary(item => item.Id);
            await controller.PointerMovedAsync(Pointer(start + new VectorD(index, 0), 1));
            foreach (var item in test.State.CurrentScene!.Items.Where(item =>
                         item.Origin.VisualStateId != BpmnDemoPipeline.ExclusiveGatewayVisualId &&
                         previous.TryGetValue(item.Id, out var old) && !ReferenceEquals(item, old)))
            {
                if (item.Layer == Canvas2DSceneLayer.Content) recreatedNodes++;
                if (item.Layer == Canvas2DSceneLayer.Connector) recreatedConnectors++;
                if (item.Layer == Canvas2DSceneLayer.Label) recreatedLabels++;
            }
        }
        await test.Session.WaitForIdleAsync();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            moves = 100,
            sceneRebuilds = test.Pipeline.Rebuilds - rebuilds,
            contributorCalls = test.Contributions.Sum(item => item.Calls) - contributions,
            fullUploads = test.Execution.FullUploadCount - uploads,
            rendererFrames = test.Execution.RenderCount - frames,
            notifications,
            recreatedNodes,
            recreatedConnectors,
            recreatedLabels,
            textMeasurementInterop = test.Execution.MeasurementRequests.Count - interopMeasurements,
        }));
        Assert.Same(snapshot, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        Assert.Equal(0, test.Execution.FullUploadCount - uploads);
        Assert.Equal(100, test.Execution.RenderCount - frames);
        Assert.Equal(0, recreatedNodes + recreatedConnectors + recreatedLabels);
        await controller.PointerReleasedAsync(Pointer(start + new VectorD(100, 0)));
        await test.Session.WaitForIdleAsync();
        Assert.Equal(snapshot.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(events + 1, test.Events.Count);

        Canvas2DPointerInput Pointer(PointD point, int buttons = 0) =>
            new(83, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
    }

    [Fact]
    public async Task FullResizeCompositionMeasuresOnlyActiveNodeLabelAndIdentifiesConnectorReflow()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [BpmnDemoPipeline.ExclusiveGatewayVisualId]));
        var scene = test.State.CurrentScene!;
        var zone = scene.Items.Single(item => item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith("node-label-resize-zone:east:", StringComparison.Ordinal) == true);
        var start = new PointD(zone.Bounds.X + zone.Bounds.Width / 2, zone.Bounds.Y + zone.Bounds.Height / 2);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(new Canvas2DPointerInput(84, scene.ViewportTransform.TransformPoint(start)));
        await controller.PointerPressedAsync(new Canvas2DPointerInput(84, scene.ViewportTransform.TransformPoint(start), buttons: 1));
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
            OrganizationalPluginRegistration.Create(eligibility,
                OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
        var builder = new Canvas2DSceneBuilder(contributors: registrations);
        var metrics = new MetricsProbe(test.Renderer);
        for (var index = 1; index <= 100; index++)
        {
            await controller.PointerMovedAsync(new Canvas2DPointerInput(84,
                scene.ViewportTransform.TransformPoint(start + new VectorD(index, 0)), buttons: 1));
            var state = test.State;
            var independent = await builder.BuildMeasuredAsync(test.Snapshot, state.ActiveScopeId,
                state.ModelProfileViewState, state.ModelProfileElementViewState, state.ProjectedGraph!,
                state.LayoutResult!, state.RoutingResult!, test.Snapshot.VisualModel, state.EditorState,
                metrics, test.Renderer.CreateTextMeasurementRequest, CancellationToken.None);
            Assert.Equal(state.CurrentScene, independent.Scene);
        }
        output.WriteLine(JsonSerializer.Serialize(metrics.Requests.GroupBy(item => item.Text)
            .ToDictionary(group => group.Key, group => group.Count())));
        Assert.DoesNotContain(metrics.Requests, request => request.Text.Contains("task", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100, metrics.Requests.Count(request => request.Text == "Approved?"));
    }

    private sealed class MetricsProbe(ITextMetricsService inner) : ITextMetricsService
    {
        internal List<TextMeasurementRequest> Requests { get; } = [];
        public ValueTask<TextMeasurementResult> MeasureAsync(TextMeasurementRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.MeasureAsync(request, cancellationToken);
        }
    }
}
