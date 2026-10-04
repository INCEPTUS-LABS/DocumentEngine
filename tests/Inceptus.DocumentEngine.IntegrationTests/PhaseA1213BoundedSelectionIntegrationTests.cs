using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA1213BoundedSelectionIntegrationTests
{
    [Theory]
    [InlineData("task")]
    [InlineData("user-task")]
    [InlineData("service-task")]
    [InlineData("manual-task")]
    [InlineData("send-task")]
    [InlineData("receive-task")]
    [InlineData("sub-process")]
    public async Task NormalToolboxPlacementSelectsWithOnePersistentPipelineAndNoSecondFullScene(string type)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await ClearAsync(test);
        var before = test.State;
        var full = test.Pipeline.FullRuns;
        var rebuilds = test.Pipeline.Rebuilds;
        var uploads = test.Execution.FullUploadCount;
        var reuse = test.Pipeline.SelectionReuses;
        var events = test.Events.Count;
        var visual = await test.PlaceActivityAsync(type, FreePlacementCenter(test));
        Assert.Equal(full + 1, test.Pipeline.FullRuns);
        Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
        Assert.Equal(uploads + 1, test.Execution.FullUploadCount);
        Assert.Equal(reuse + 1, test.Pipeline.SelectionReuses);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(visual.Id, Assert.Single(test.State.EditorState.Selection));
        Assert.NotNull(test.State.CurrentScene!.BoundedPresentation);
        await AssertFullSceneEquivalentAsync(test);
        await AssertActivityInteractionAsync(test, visual.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryHoverNameCrossingsClickContextSwitchAndClearRetainBase(bool organizational)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        if (organizational) { await test.EnablePoolsAsync(); }
        await ClearAsync(test);
        var id = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == BpmnDemoPipeline.TaskId).Id;
        await AssertActivityInteractionAsync(test, id);
        var other = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == BpmnDemoPipeline.ProcessOrderSubProcessId).Id;
        var content = test.State.CurrentScene!.RenderContent;
        var count = test.Pipeline.SelectionReuses;
        foreach (var selected in new[] { id, other, id, other })
        {
            Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [selected],
                viewport: test.State.EditorState.Viewport))).Succeeded);
            Assert.True(content == test.State.CurrentScene!.RenderContent);
            await AssertFullSceneEquivalentAsync(test);
        }
        Assert.Equal(count + 4, test.Pipeline.SelectionReuses);
        await test.AssertPanReusedAsync();
        await AssertFullSceneEquivalentAsync(test);
    }

    [Fact]
    public async Task GatewayEventAndConnectorLabelsKeepOrdinaryHoverAndConservativeSelection()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await using var interaction = test.CreateInteractionController();
        var namedEvent = new SemanticElementId("test:a1213:named-event");
        await test.ExecuteAsync(new CreateBpmnStartEventCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            namedEvent, new("test:a1213:named-event:visual"), new(650, 550), new(36, 36), name: "Named Event",
            targetScopeId: test.State.ActiveScopeId));
        foreach (var id in new[] { BpmnDemoPipeline.ExclusiveGatewayId, namedEvent })
        {
            await ClearAsync(test);
            var label = test.State.CurrentScene!.Items.First(item => item.Origin.SemanticElementId == id &&
                item.Layer == Canvas2DSceneLayer.Label && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                (item.Clip is null || item.Clip.Value.Contains(Center(item))));
            Assert.False(label.Metadata.TryGetValue(Canvas2DTransientInteractionMetadata.ExcludeFromHover, out _));
            var point = Center(label);
            var hit = new Canvas2DSceneHitTestService();
            Assert.True(hit.HitTest(test.State.CurrentScene, point) is not null,
                $"{id}: bounds={label.Bounds}; clip={label.Clip}; visible={label.IsVisible}; point={point}");
            Assert.Equal(hit.HitTest(test.State.CurrentScene, point)?.SceneObjectId,
                hit.HitTestForHover(test.State.CurrentScene, point)?.SceneObjectId);
            var reuses = test.Pipeline.SelectionReuses;
            await interaction.PointerMovedAsync(test.State.CurrentScene.ViewportTransform.TransformPoint(point));
            await interaction.PointerActivatedAsync(test.State.CurrentScene.ViewportTransform.TransformPoint(point));
            Assert.Equal(reuses, test.Pipeline.SelectionReuses);
            Assert.Single(test.State.EditorState.Selection);
            await AssertFullSceneEquivalentAsync(test);
        }
        var graph = test.State.ProjectedGraph!;
        var edgeIds = graph.Edges.Select(edge => edge.Id).ToHashSet();
        Assert.All(graph.Labels.Where(label => edgeIds.Contains(label.OwnerId)), label =>
            Assert.False(label.ProjectedProperties.TryGetValue(Canvas2DTransientInteractionMetadata.ExcludeFromHover, out _)));
    }

    [Theory]
    [InlineData("task")]
    [InlineData("manual-task")]
    [InlineData("sub-process")]
    public async Task ExternalActivityNameDoesNotCreateInvisibleOwnerHoverButKeepsClickAndContext(string type)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var visual = await test.PlaceActivityAsync(type, FreePlacementCenter(test));
        await test.ExecuteAsync(new UpdateNodeLabelVisualOverrideCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            visual.Id, new NodeLabelVisualOverride(0, 170, 200, 50)));
        await ClearAsync(test);
        var scene = test.State.CurrentScene!;
        var label = scene.Items.First(item => item.Origin.VisualStateId == visual.Id && item.Layer == Canvas2DSceneLayer.Label);
        var body = scene.Items.Single(item => item.Origin.VisualStateId == visual.Id && Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var point = Center(label);
        Assert.False(body.Bounds.Contains(point));
        var hits = new Canvas2DSceneHitTestService();
        Assert.NotEqual(visual.Id, hits.HitTestForHover(scene, point)?.Origin.VisualStateId);
        Assert.Equal(visual.Id, hits.HitTest(scene, point)?.Origin.VisualStateId);
        await using var interaction = test.CreateInteractionController();
        var css = scene.ViewportTransform.TransformPoint(point);
        await interaction.PointerMovedAsync(css);
        Assert.Null(test.State.EditorState.HoveredObjectId);
        await interaction.PointerActivatedAsync(css);
        Assert.Equal(visual.Id, Assert.Single(test.State.EditorState.Selection));
        await AssertFullSceneEquivalentAsync(test);
        await interaction.PointerContextMenuAsync(css);
        Assert.Equal(visual.Id, Assert.Single(test.State.EditorState.Selection));
        await AssertFullSceneEquivalentAsync(test);
    }

    [Fact]
    public async Task PoolZeroOneTwoAndUnassignedSelectionKeepExactDisplayedGeometry()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.EnablePoolsAsync();
        var third = new SemanticElementId("test:a1213:pool-c");
        await test.ExecuteAsync(new CreateOrganizationalPoolCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            third, test.State.ActiveScopeId, OrganizationalPoolCreationMode.Empty, "Third"));
        await PhaseN101OrganizationalPoolIntegrationTests.PrepareRegionCapacitiesAsync(test.Session, 1000d);
        var pools = new SemanticElementId?[] { new("test:a122:pool-a"), new("test:a122:pool-b"), third, null };
        var ids = new List<VisualStateId>();
        for (var index = 0; index < pools.Length; index++)
        {
            var semantic = new SemanticElementId($"test:a1213:region:{index}");
            var visual = new VisualStateId($"test:a1213:region:{index}:visual");
            ids.Add(visual);
            await test.ExecuteAsync(new CreateBpmnTaskCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                semantic, visual, new(1100, 500), new(160, 100), $"REGION{index}", "Region Task", index + 1,
                VisualPlacementMode.Pinned, targetScopeId: test.State.ActiveScopeId));
            if (pools[index] is { } pool)
            {
                await test.ExecuteAsync(new AssignOrganizationalElementCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, semantic, pool));
            }
        }
        for (var index = 0; index < pools.Length; index++)
        {
            var body = test.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == ids[index] && Canvas2DNodeBodyMetadata.IsNodeBody(item));
            Assert.Equal(pools[index], body.SpatialRegion!.ContainerSemanticElementId);
            await AssertActivityInteractionAsync(test, ids[index]);
        }
    }

    [Fact]
    public async Task FastSelectedResizeAndAuthoredAnchorHandlesAreImmediatelyAuthoritative()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var visual = await test.PlaceActivityAsync("task", FreePlacementCenter(test));
        await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            visual.Id, new("test:a1213:source"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
        await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            visual.Id, new("test:a1213:target"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
        await ClearAsync(test);
        var before = test.Pipeline.SelectionReuses;
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visual.Id], viewport: test.State.EditorState.Viewport));
        Assert.Equal(before + 1, test.Pipeline.SelectionReuses);
        await AssertFullSceneEquivalentAsync(test);
        var scene = test.State.CurrentScene!;
        Assert.Equal(11, scene.BoundedPresentation!.Items.Length);
        var handles = scene.Items.Where(item => item.Origin.VisualStateId == visual.Id &&
            item.Metadata.ContainsKey(Canvas2DConnectorAnchorMetadata.AnchorId)).ToArray();
        Assert.Equal(2, handles.Length);
        var hits = new Canvas2DSceneHitTestService();
        foreach (var handle in handles)
        {
            Assert.Equal(handle.Id, hits.HitTest(scene, Center(handle))?.SceneObjectId);
            Assert.Equal(handle.Id, hits.HitTestForHover(scene, Center(handle))?.SceneObjectId);
        }
        var resize = scene.Items.Single(item => item.Origin.VisualStateId == visual.Id &&
            item.Metadata.TryGetValue(Canvas2DResizeGestureMetadata.HandleRole, out var role) &&
            role.TextValue == Canvas2DResizeGestureMetadata.SouthEastRole);
        Assert.Equal(resize.Id, hits.HitTest(scene, Center(resize))?.SceneObjectId);
        await using var interaction = test.CreateInteractionController();
        var sourceHandle = handles.Single(item => item.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue == "test:a1213:source");
        var sourceCss = scene.ViewportTransform.TransformPoint(Center(sourceHandle));
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await interaction.PointerPressedAsync(new(12130, sourceCss, buttons: 1))).Status);
        Assert.Equal(Canvas2DAnchorConnectionGestureMetadata.Kind, test.State.EditorState.ActiveGesture!.Kind);
        await interaction.PointerCancelledAsync(12130);
        await AssertFullSceneEquivalentAsync(test);
        var start = Center(resize);
        PointD Css(PointD point) => scene.ViewportTransform.TransformPoint(point);
        var original = test.Snapshot;
        await interaction.PointerPressedAsync(new(1213, Css(start), buttons: 1));
        await interaction.PointerMovedAsync(new Canvas2DPointerInput(1213, Css(start + new VectorD(25, 20)), buttons: 1));
        Assert.Equal(Canvas2DResizeGestureMetadata.Kind, test.State.EditorState.ActiveGesture!.Kind);
        Assert.Same(original, test.Snapshot);
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await interaction.PointerReleasedAsync(new(1213, Css(start + new VectorD(25, 20))))).Status);
        await test.Session.WaitForIdleAsync();
        Assert.NotEqual(visual.Size, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == visual.Id).Size);
        await test.Session.UndoAsync();
        await test.Session.WaitForIdleAsync();
        Assert.Equal(original.VisualModel.VisualStates.ToArray(), test.Snapshot.VisualModel.VisualStates.ToArray());
    }

    [Fact]
    public async Task SemanticPoolSelectionRetainsFullContributorComposition()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.EnablePoolsAsync();
        await ClearAsync(test);
        var region = test.State.CurrentScene!.SpatialPresentationPlan!.Regions.First(item => item.ContainerSemanticElementId is not null);
        var rebuilds = test.Pipeline.Rebuilds;
        var contributions = test.Contributions.Sum(item => item.Calls);
        await using var interaction = test.CreateInteractionController();
        await interaction.SelectSemanticSceneTargetAsync(region.ContainerSemanticElementId!);
        Assert.NotNull(test.State.EditorState.SemanticSceneSelection);
        Assert.Equal(rebuilds + 1, test.Pipeline.Rebuilds);
        Assert.True(test.Contributions.Sum(item => item.Calls) > contributions);
        await AssertFullSceneEquivalentAsync(test);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedSelectionCannotInstallAfterGenerationOrSurfaceReplacement(bool resize)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await ClearAsync(test);
        var id = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == BpmnDemoPipeline.TaskId).Id;
        test.Pipeline.BlockMove = true;
        var pending = test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [id],
            viewport: test.State.EditorState.Viewport)).AsTask();
        await test.Pipeline.MoveBuilt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(test.Pipeline.BlockedResult!.ReusedSelectionContent);
        if (resize) { await test.Session.ResizeAsync(new(1000, 700, 2)); }
        else { await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: new ViewportSnapshot(2, default))); }
        test.Pipeline.ReleaseMove.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
        Assert.NotSame(test.Pipeline.BlockedResult.Scene, test.State.CurrentScene);
        Assert.Equal(EditingSessionStatus.Ready, test.State.Status);
        await AssertFullSceneEquivalentAsync(test);
    }

    private static async Task AssertActivityInteractionAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test, VisualStateId id)
    {
        await ClearAsync(test);
        var before = test.State;
        var snapshot = test.Snapshot;
        var scene = before.CurrentScene!;
        var body = scene.Items.First(item => item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Content);
        var name = scene.Items.First(item => item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Label);
        var bodyPoint = new PointD(body.Bounds.X + body.Bounds.Width * 0.75, body.Bounds.Y + body.Bounds.Height * 0.2);
        var namePoint = Center(name);
        var hit = new Canvas2DSceneHitTestService();
        Assert.Equal(body.Id, hit.HitTestForHover(scene, namePoint)?.SceneObjectId);
        Assert.Equal(id, hit.HitTest(scene, namePoint)?.Origin.VisualStateId);
        var uploads = test.Execution.FullUploadCount;
        var rebuilds = test.Pipeline.Rebuilds;
        var full = test.Pipeline.FullRuns;
        var contributions = test.Contributions.Sum(item => item.Calls + item.Routes);
        await using var interaction = test.CreateInteractionController();
        PointD Css(PointD point) => test.State.CurrentScene!.ViewportTransform.TransformPoint(point);
        await interaction.PointerMovedAsync(Css(bodyPoint));
        Assert.Equal(body.Id, test.State.EditorState.HoveredObjectId);
        await AssertFullSceneEquivalentAsync(test);
        var generation = test.State.Generation;
        var renders = test.Execution.RenderCount;
        await interaction.PointerMovedAsync(Css(namePoint));
        await interaction.PointerMovedAsync(Css(bodyPoint));
        Assert.Equal(generation, test.State.Generation);
        Assert.Equal(renders, test.Execution.RenderCount);
        await interaction.PointerActivatedAsync(Css(namePoint));
        Assert.Equal(id, Assert.Single(test.State.EditorState.Selection));
        Assert.Contains(test.State.CurrentScene!.Items, item => item.Origin.VisualStateId == id &&
            item.Origin.StableSourceKey?.StartsWith("resize-", StringComparison.Ordinal) == true);
        await AssertFullSceneEquivalentAsync(test);
        generation = test.State.Generation;
        await interaction.PointerActivatedAsync(Css(bodyPoint));
        Assert.Equal(generation, test.State.Generation);
        await interaction.PointerContextMenuAsync(Css(namePoint));
        Assert.Equal(id, Assert.Single(test.State.EditorState.Selection));
        await interaction.PointerLeftAsync();
        await AssertFullSceneEquivalentAsync(test);
        await interaction.PointerActivatedAsync(Css(new PointD(10000, 10000)));
        Assert.Empty(test.State.EditorState.Selection);
        Assert.Empty(test.State.CurrentScene!.BoundedPresentation!.Items);
        await AssertFullSceneEquivalentAsync(test);
        Assert.True(scene.RenderContent == test.State.CurrentScene.RenderContent);
        Assert.Same(snapshot, test.Snapshot);
        Assert.Same(before.ProjectedGraph, test.State.ProjectedGraph);
        Assert.Same(before.LayoutResult, test.State.LayoutResult);
        Assert.Same(before.RoutingResult, test.State.RoutingResult);
        Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
        Assert.Equal(full, test.Pipeline.FullRuns);
        Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
        Assert.Equal(contributions, test.Contributions.Sum(item => item.Calls + item.Routes));
        Assert.Equal(uploads, test.Execution.FullUploadCount);
    }

    private static PointD Center(Canvas2DSceneItem item) => new(item.Bounds.X + item.Bounds.Width / 2,
        item.Bounds.Y + item.Bounds.Height / 2);

    private static PointD FreePlacementCenter(PhaseA122PanSceneReuseIntegrationTests.Fixture test) =>
        new(test.State.CurrentScene!.Items.Where(Canvas2DNodeBodyMetadata.IsNodeBody)
            .Max(static item => item.Bounds.Right) + 200, 550);

    private static async Task ClearAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test) =>
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: test.State.EditorState.Viewport))).Succeeded);

    private static async Task AssertFullSceneEquivalentAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test)
    {
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
            OrganizationalPluginRegistration.Create(eligibility,
                OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
        var builder = new Canvas2DSceneBuilder(contributors: registrations);
        var state = test.State;
        var result = await builder.BuildMeasuredAsync(test.Snapshot, state.ActiveScopeId,
            state.ModelProfileViewState, state.ModelProfileElementViewState, state.ProjectedGraph!,
            state.LayoutResult!, state.RoutingResult!, test.Snapshot.VisualModel, state.EditorState,
            test.Renderer, test.Renderer.CreateTextMeasurementRequest, default);
        Assert.Equal(result.Scene, state.CurrentScene);
        var hits = new Canvas2DSceneHitTestService();
        foreach (var item in state.CurrentScene!.Items.Where(item => item.HitTestPolicy.Mode != Canvas2DHitTestMode.None))
        {
            var point = Center(item);
            Assert.Equal(hits.HitTest(result.Scene!, point)?.SceneObjectId, hits.HitTest(state.CurrentScene, point)?.SceneObjectId);
            Assert.Equal(hits.HitTestForHover(result.Scene!, point)?.SceneObjectId, hits.HitTestForHover(state.CurrentScene, point)?.SceneObjectId);
        }
    }
}
