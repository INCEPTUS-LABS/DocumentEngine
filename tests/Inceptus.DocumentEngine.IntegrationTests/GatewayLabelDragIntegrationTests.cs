using System.Diagnostics;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Xunit.Abstractions;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class GatewayLabelDragIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("gateway", 1d, false)]
    [InlineData("gateway", 1.75d, true)]
    [InlineData("event", 0.8d, false)]
    public async Task SteadyLabelMovementUsesBoundedPresentationWithoutPersistentOrPipelineWork(
        string shape, double zoom, bool pools)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        if (pools) await test.EnablePoolsAsync();
        await SetViewportAsync(test, zoom);
        var id = shape switch
        {
            "event" => BpmnDemoPipeline.MessageCatchEventId,
            _ => BpmnDemoPipeline.ExclusiveGatewayId,
        };
        var label = Label(test, id);
        var body = Body(test, id);
        var start = Center(label.Bounds);
        var snapshot = test.Snapshot;
        var before = test.State;
        await using var controller = test.CreateInteractionController();
        // Real browser acquisition includes a hover sample before pointer-down.
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        await controller.PointerMovedAsync(Pointer(test, start + new VectorD(12, 8)));
        Assert.Equal(Canvas2DNodeLabelGestureMetadata.Kind, test.State.EditorState.ActiveGesture?.Kind);
        Assert.NotNull(test.State.CurrentScene!.BoundedPresentation?.NodeLabelMove);
        var uploads = test.Execution.FullUploadCount;
        var bounded = test.Execution.ViewportRenderCount;
        var fullRuns = test.Pipeline.FullRuns;
        var contributions = test.Contributions.Sum(item => item.Calls + item.Routes);
        var content = test.State.CurrentScene.RenderContent;
        var samples = new List<double>();
        for (var index = 0; index < 20; index++)
        {
            // Includes reversal and overlap with a different real node body.
            var delta = index == 19 ? Center(Body(test, BpmnDemoPipeline.ApprovedTaskId).Bounds) - start
                : new VectorD(15 + index * 3, 12 + index);
            var started = Stopwatch.GetTimestamp();
            Assert.Equal(Canvas2DInteractionStatus.Updated,
                (await controller.PointerMovedAsync(Pointer(test, start + delta))).Status);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Assert.Same(snapshot, test.Snapshot);
            Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
            Assert.Same(before.ProjectedGraph, test.State.ProjectedGraph);
            Assert.Same(before.LayoutResult, test.State.LayoutResult);
            Assert.Same(before.RoutingResult, test.State.RoutingResult);
            Assert.Equal(body, Body(test, id));
            Assert.True(content == test.State.CurrentScene!.RenderContent);
            Assert.DoesNotContain(test.State.CurrentScene.Items, item => item.Layer == Canvas2DSceneLayer.Label &&
                item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId && item.IsVisible);
            var preview = test.State.CurrentScene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId &&
                item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true).ToArray();
            Assert.NotEmpty(preview);
            Assert.All(preview, item => Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode));
            var box = Assert.Single(preview, item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
            Assert.Equal(label.Bounds.X + delta.X, box.Bounds.X, 8);
            Assert.Equal(label.Bounds.Y + delta.Y, box.Bounds.Y, 8);
            Assert.Equal(label.Bounds.Width, box.Bounds.Width);
            Assert.Equal(label.Bounds.Height, box.Bounds.Height);
        }
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(bounded + 20, test.Execution.ViewportRenderCount);
        Assert.Equal(fullRuns, test.Pipeline.FullRuns);
        Assert.Equal(contributions, test.Contributions.Sum(item => item.Calls + item.Routes));
        await AssertIndependentFullSceneAsync(test);
        output.WriteLine(JsonSerializer.Serialize(new { shape, zoom, pools, samples }));
        await controller.CancelActiveGestureAsync();
        Assert.Same(snapshot, test.Snapshot);
        Assert.Equal(label.Bounds, Label(test, id).Bounds);
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.DoesNotContain(test.State.CurrentScene!.Items, item =>
            item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalPointerIsAuthoritativeAndCommitsOnlyLabelWithExactSavedRoutesAndFrames(bool crossPoolOrigin)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.EnablePoolsAsync();
        await SetViewportAsync(test, 1.5);
        var id = BpmnDemoPipeline.ExclusiveGatewayId;
        var label = Label(test, id);
        var body = Body(test, id);
        var start = Center(label.Bounds);
        var snapshot = test.Snapshot;
        var history = test.State.HistoryStatus.EntryCount;
        var events = test.Events.Count;
        var fullRuns = test.Pipeline.FullRuns;
        await using var controller = test.CreateInteractionController();
        await controller.PointerPressedAsync(Pointer(test, start));
        await controller.PointerMovedAsync(Pointer(test, start + new VectorD(14, 9)));
        var delta = crossPoolOrigin ? new VectorD(20 - label.Bounds.X, 20 - label.Bounds.Y) : new VectorD(58, 37);
        var result = await controller.PointerReleasedAsync(Pointer(test, start + delta));
        Assert.Equal(Canvas2DInteractionStatus.Committed, result.Status);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(snapshot.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(history + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(fullRuns, test.Pipeline.FullRuns);
        Assert.Equal(body.Bounds, Body(test, id).Bounds);
        Assert.Equal(label.Bounds.X + delta.X, Label(test, id).Bounds.X, 8);
        Assert.Equal(label.Bounds.Y + delta.Y, Label(test, id).Bounds.Y, 8);
        Assert.Equal(label.Bounds.Size, Label(test, id).Bounds.Size);
        var owner = body.Origin.VisualStateId!;
        foreach (var old in snapshot.VisualModel.VisualStates)
        {
            var next = test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == old.Id);
            if (old.Id != owner) Assert.Equal(old, next);
            else
            {
                Assert.Equal(old.Position, next.Position);
                Assert.Equal(old.Size, next.Size);
                Assert.Equal(old.ConnectorAnchors.ToArray(), next.ConnectorAnchors.ToArray());
                Assert.True(NodeLabelVisualOverride.TryRead(next.Properties, out _));
            }
        }
        foreach (var old in snapshot.VisualModel.RoutingScopes!.Value)
        {
            var next = test.Snapshot.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == old.ScopeId);
            Assert.Equal(old.Connectors.ToArray(), next.Connectors.ToArray());
            Assert.Equal(old.Geometry.Nodes.ToArray(), next.Geometry.Nodes.ToArray());
            Assert.Equal(old.Geometry.Regions.ToArray(), next.Geometry.Regions.ToArray());
            Assert.Equal(old.Geometry.SpatialWidths.ToArray(), next.Geometry.SpatialWidths.ToArray());
            foreach (var caption in old.Geometry.Captions.Where(item => item.OwnerVisualStateId != owner))
                Assert.Contains(caption, next.Geometry.Captions);
        }
        var configuration = (await BpmnModelerTestComposition.CreateDemoAsync()).Configuration;
        var validation = await new ConnectorRoutingStatePreparer(configuration, test.Renderer).PrepareAsync(
            new ConnectorRoutingStatePreparationRequest(test.Snapshot, test.Snapshot, [], [], null, false,
                ConnectorRoutingPreparationPurpose.ValidateSavedState), CancellationToken.None);
        Assert.True(validation.Succeeded, string.Join("; ", validation.Diagnostics.Select(item => item.Message)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerCancellationAndStaleViewportCannotCommit(bool stale)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var label = Label(test, BpmnDemoPipeline.ExclusiveGatewayId);
        var start = Center(label.Bounds);
        var snapshot = test.Snapshot;
        await using var controller = test.CreateInteractionController();
        await controller.PointerPressedAsync(Pointer(test, start));
        await controller.PointerMovedAsync(Pointer(test, start + new VectorD(20, 10)));
        if (stale)
        {
            Assert.False((await test.Session.PanViewportAsync(new VectorD(8, 12))).Succeeded);
            Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1001, 700, 2))).Succeeded);
            await controller.PointerReleasedAsync(Pointer(test, start + new VectorD(50, 20)));
        }
        else await controller.PointerCancelledAsync(42);
        Assert.Same(snapshot, test.Snapshot);
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.Equal(label.Bounds, Label(test, BpmnDemoPipeline.ExclusiveGatewayId).Bounds);
    }

    private static async Task SetViewportAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test, double zoom) =>
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: new ViewportSnapshot(zoom, new VectorD(35, 19))))).Succeeded);

    private static Canvas2DPointerInput Pointer(PhaseA122PanSceneReuseIntegrationTests.Fixture test, PointD point) =>
        new(42, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: 1);

    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static Canvas2DSceneItem Body(PhaseA122PanSceneReuseIntegrationTests.Fixture test, SemanticElementId id) =>
        test.State.CurrentScene!.Items.First(item => item.Origin.SemanticElementId == id &&
            item.Layer == Canvas2DSceneLayer.Content && item.IsVisible && Canvas2DNodeBodyMetadata.IsNodeBody(item));

    private static Canvas2DSceneItem Label(PhaseA122PanSceneReuseIntegrationTests.Fixture test, SemanticElementId id) =>
        test.State.CurrentScene!.Items.Single(item => item.Origin.SemanticElementId == id &&
            item.Layer == Canvas2DSceneLayer.Label && item.IsVisible &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));

    private static async Task AssertIndependentFullSceneAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test)
    {
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
            OrganizationalPluginRegistration.Create(eligibility,
                OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
        var state = test.State;
        var result = await new Canvas2DSceneBuilder(contributors: registrations).BuildMeasuredAsync(test.Snapshot,
            state.ActiveScopeId, state.ModelProfileViewState, state.ModelProfileElementViewState,
            state.ProjectedGraph!, state.LayoutResult!, state.RoutingResult!, test.Snapshot.VisualModel,
            state.EditorState, test.Renderer, test.Renderer.CreateTextMeasurementRequest, CancellationToken.None);
        Assert.Equal(result.Scene, state.CurrentScene);
    }
}
