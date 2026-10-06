using System.Collections.Immutable;
using System.Diagnostics;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA122PanSceneReuseIntegrationTests
{
    private static readonly SemanticElementId PoolA = new("test:a122:pool-a");
    private static readonly SemanticElementId PoolB = new("test:a122:pool-b");

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task PanSharesFinalLabelsRoutesOverlaysAndSpatialContentWithoutPersistentWork(
        bool organizational, bool hidden, bool collapsed)
    {
        await using var test = await Fixture.CreateAsync();
        var branch = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, branch, "Accepted branch with wrapped label"));
        var manualFlow = test.Snapshot.SemanticModel.Relationships.First(flow =>
            flow.Id != branch && test.Snapshot.SemanticModel.GetScope(flow.SourceId).Id == test.State.ActiveScopeId);
        await test.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, manualFlow.Id, "Manual branch"));
        var manualVisual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == manualFlow.Id);
        await test.ExecuteAsync(new MoveLabelCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            manualVisual.Id, ConnectorLabelPlacement.Default));
        if (organizational)
        {
            await test.EnablePoolsAsync();
            if (hidden)
            {
                Assert.True((await test.Session.UpdateModelProfileViewStateAsync(
                    new ModelProfileViewStateSnapshot([OrganizationalModelProfile.Id]))).Succeeded);
            }
            if (collapsed)
            {
                Assert.True((await test.Session.UpdateModelProfileElementViewStateAsync(
                    new ModelProfileElementViewStateSnapshot([new(OrganizationalModelProfile.Id, PoolB)]))).Succeeded);
            }
            Assert.NotNull(test.State.CurrentScene!.SpatialPresentationPlan);
            Assert.Contains(test.State.CurrentScene.SpatialPresentationPlan.Regions,
                region => region.ContainerSemanticElementId is null); // Unassigned is retained.
        }

        var target = test.State.CurrentScene!.Items.First(item => item.IsVisible &&
            item.Layer == Canvas2DSceneLayer.Content && item.Origin.VisualStateId is not null &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [target.Origin.VisualStateId!], hoveredObjectId: target.Id,
            viewport: test.State.EditorState.Viewport))).Succeeded);
        Assert.True((await test.Session.PanViewportAsync(new VectorD(-1000, -1000))).Succeeded);
        var before = test.State;
        var snapshot = test.Snapshot;
        var events = test.Events.Count;
        var contributions = test.Contributions.Sum(item => item.Calls);
        var routes = test.Contributions.Sum(item => item.Routes);
        var renders = test.Execution.RenderCount;
        var uploads = test.Execution.FullUploadCount;
        var viewportRenders = test.Execution.ViewportRenderCount;
        var rebuilds = test.Pipeline.Rebuilds;
        var fast = test.Pipeline.Reuses;
        var full = test.Pipeline.FullRuns;
        var originalItems = before.CurrentScene!.Items.Where(item => !IsGuide(item)).ToDictionary(item => item.Id);

        for (var i = 0; i < 12; i++)
        {
            // Exercise steady top/left edges and guide appearance/disappearance with
            // labels, selection, Organizational presentation and cross-Pool routes retained.
            var targetPan = i < 4 ? new VectorD(-100d - (i * 29.5d), 0d) :
                i < 8 ? new VectorD(0d, -100d - (i * 7.125d)) :
                i % 2 == 0 ? new VectorD(-100d, -100d) : default;
            Assert.True((await test.Session.PanViewportAsync(
                targetPan - test.State.EditorState.Viewport.Pan)).Succeeded);
            var state = test.State;
            Assert.Equal(targetPan, state.EditorState.Viewport.Pan);
            Assert.True(before.CurrentScene.Items == state.CurrentScene!.Items);
            Assert.Same(snapshot, test.Snapshot);
            Assert.Same(snapshot.SemanticModel, test.Snapshot.SemanticModel);
            Assert.Same(snapshot.VisualModel, test.Snapshot.VisualModel);
            Assert.Same(snapshot.Metadata, test.Snapshot.Metadata);
            Assert.Equal(before.DocumentRevision, state.DocumentRevision);
            Assert.Equal(before.HistoryStatus, state.HistoryStatus);
            Assert.Same(before.ProjectedGraph, state.ProjectedGraph);
            Assert.Same(before.LayoutResult, state.LayoutResult);
            Assert.Same(before.RoutingResult, state.RoutingResult);
            Assert.Same(before.CurrentScene.SpatialPresentationPlan, state.CurrentScene!.SpatialPresentationPlan);
            Assert.Equal(before.EditorState.Selection.AsEnumerable(), state.EditorState.Selection);
            Assert.Equal(before.EditorState.HoveredObjectId, state.EditorState.HoveredObjectId);
            Assert.All(state.CurrentScene.Items.Where(item => !IsGuide(item)), item => Assert.Same(originalItems[item.Id], item));
            Assert.Equal(before.ModelProfileViewState, state.ModelProfileViewState);
            Assert.Equal(before.ModelProfileElementViewState, state.ModelProfileElementViewState);
            Assert.True(state.Generation.Value > before.Generation.Value);
        }

        Assert.Equal(fast + 12, test.Pipeline.Reuses);
        Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
        Assert.Equal(full, test.Pipeline.FullRuns);
        Assert.Equal(contributions, test.Contributions.Sum(item => item.Calls));
        Assert.Equal(routes, test.Contributions.Sum(item => item.Routes));
        Assert.Equal(renders + 12, test.Execution.RenderCount);
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(viewportRenders + 12, test.Execution.ViewportRenderCount);
        Assert.Equal(events, test.Events.Count);

        // The next interaction consumes the new viewport with exact document-space targets.
        await using var interaction = new Canvas2DInteractionController(test.Session);
        var center = new PointD(target.Bounds.X + target.Bounds.Width / 2, target.Bounds.Y + target.Bounds.Height / 2);
        await interaction.PointerActivatedAsync(test.State.CurrentScene!.ViewportTransform.TransformPoint(center));
        Assert.Contains(target.Origin.VisualStateId!, test.State.EditorState.Selection);
    }

    [Theory]
    [InlineData("zoom")]
    [InlineData("resize")]
    [InlineData("dpr")]
    [InlineData("selection")]
    [InlineData("hover")]
    [InlineData("feedback")]
    [InlineData("mutation")]
    [InlineData("visibility")]
    [InlineData("collapse")]
    [InlineData("navigation")]
    public async Task NonPanTransitionsUseTheirCertifiedPathAndLaterPanCanReuse(string transition)
    {
        await using var test = await Fixture.CreateAsync();
        if (transition is "visibility" or "collapse")
        {
            await test.EnablePoolsAsync();
        }
        await test.AssertPanReusedAsync();
        var before = test.State;
        var reuses = test.Pipeline.Reuses;
        var rebuilds = test.Pipeline.Rebuilds + test.Pipeline.FullRuns;
        var selectionReuses = test.Pipeline.SelectionReuses;
        var viewport = before.EditorState.Viewport;
        var uploads = test.Execution.FullUploadCount;
        var target = before.CurrentScene!.Items.First(item => item.IsVisible &&
            item.Layer == Canvas2DSceneLayer.Content && item.Origin.VisualStateId is not null &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        switch (transition)
        {
            case "zoom": Assert.True((await test.Session.UpdateViewportAsync(new ViewportSnapshot(1.5, viewport.Pan))).Succeeded); break;
            case "resize": Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1100, 650, 1))).Succeeded); break;
            case "dpr":
                Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, 2))).Succeeded);
                Assert.True((await test.Session.PanViewportAsync(new VectorD(10, 20))).Succeeded);
                break;
            case "selection": Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [target.Origin.VisualStateId!], viewport: viewport))).Succeeded); break;
            case "hover": Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(hoveredObjectId: target.Id, viewport: viewport))).Succeeded); break;
            case "feedback":
                Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: viewport,
                    temporaryFeedback: [new EditorFeedbackSnapshot("a122-preview", "placement", new RectD(20, 30, 80, 50))]))).Succeeded);
                Assert.True((await test.Session.PanViewportAsync(new VectorD(10, 20))).Succeeded);
                break;
            case "mutation": await test.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, BpmnDemoPipeline.ThirdSequenceFlowId, "Changed")); break;
            case "visibility": Assert.True((await test.Session.UpdateModelProfileViewStateAsync(new ModelProfileViewStateSnapshot([OrganizationalModelProfile.Id]))).Succeeded); break;
            case "collapse": Assert.True((await test.Session.UpdateModelProfileElementViewStateAsync(new ModelProfileElementViewStateSnapshot([new(OrganizationalModelProfile.Id, PoolA)]))).Succeeded); break;
            case "navigation": Assert.True((await test.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded); break;
        }
        Assert.Equal(reuses, test.Pipeline.Reuses);
        if (transition is "selection" or "hover")
        {
            Assert.Equal(selectionReuses + 1, test.Pipeline.SelectionReuses);
            Assert.Equal(rebuilds, test.Pipeline.Rebuilds + test.Pipeline.FullRuns);
            Assert.Equal(uploads, test.Execution.FullUploadCount);
        }
        else
        {
            Assert.True(test.Pipeline.Rebuilds + test.Pipeline.FullRuns > rebuilds);
            Assert.True(test.Execution.FullUploadCount > uploads);
        }
        Assert.NotSame(before.CurrentScene, test.State.CurrentScene);
        if (transition == "feedback")
        {
            Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: test.State.EditorState.Viewport))).Succeeded);
        }
        await test.AssertPanReusedAsync();
    }

    [Fact]
    public async Task ActiveGestureRetainsExistingPanRejectionAndPersistentState()
    {
        await using var test = await Fixture.CreateAsync();
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: test.State.EditorState.Viewport,
            activeGesture: new EditorGestureSnapshot("test:pan:gesture", "test:preview", new PointD(10, 20), new PointD(30, 40))))).Succeeded);
        var before = test.State;
        var snapshot = test.Snapshot;
        Assert.Equal(EditingSessionOperationStatus.Rejected,
            (await test.Session.PanViewportAsync(new VectorD(15, 10))).Status);
        Assert.Same(before.CurrentScene, test.State.CurrentScene);
        Assert.Same(snapshot, test.Snapshot);
        Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
    }

    [Fact]
    public async Task RootChildAndNestedScopesReuseOnlyWithinTheirOwnNavigationGeneration()
    {
        await using var test = await Fixture.CreateAsync();
        var root = test.State.ActiveScopeId;
        await test.AssertPanReusedAsync();
        var rootViewport = test.State.EditorState.Viewport;
        Assert.True((await test.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await test.AssertPanReusedAsync();
        var childViewport = test.State.EditorState.Viewport;
        var nested = new DocumentScopeId("test:a122:nested");
        await test.ExecuteAsync(new CreateBpmnSubProcessCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            new SemanticElementId("test:a122:nested-node"), new VisualStateId("test:a122:nested-visual"),
            test.State.ActiveScopeId, nested, new PointD(520, 260), new SizeD(120, 80),
            "A122_NESTED", "Nested", VisualPlacementMode.Pinned));
        Assert.True((await test.Session.NavigateToScopeAsync(nested)).Succeeded);
        await test.AssertPanReusedAsync();
        var nestedViewport = test.State.EditorState.Viewport;
        var reuses = test.Pipeline.Reuses;
        Assert.True((await test.Session.NavigateToScopeAsync(root)).Succeeded);
        Assert.Equal(rootViewport, test.State.EditorState.Viewport);
        Assert.True((await test.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        Assert.Equal(childViewport, test.State.EditorState.Viewport);
        Assert.True((await test.Session.NavigateToScopeAsync(nested)).Succeeded);
        Assert.Equal(nestedViewport, test.State.EditorState.Viewport);
        Assert.Equal(reuses, test.Pipeline.Reuses);
        await test.AssertPanReusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedPanCannotInstallOverNewEditorOrSurfaceState(bool resize)
    {
        await using var test = await Fixture.CreateAsync();
        test.Pipeline.BlockPan = true;
        var pan = test.Session.PanViewportAsync(new VectorD(30, -10)).AsTask();
        await test.Pipeline.PanBuilt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(test.Pipeline.BlockedResult!.ReusedPanContent);
        if (resize)
        {
            // DPR-only resize invalidates reuse without changing EditorState or generation.
            // Rendering while Rebuilding can return unavailable; the pending Pan must recover.
            await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, 2));
        }
        else
        {
            Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
                viewport: new ViewportSnapshot(2, new VectorD(90, 80))))).Succeeded);
        }
        test.Pipeline.ReleasePan.TrySetResult();
        await pan.WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
        Assert.Equal(EditingSessionStatus.Ready, test.State.Status);
        Assert.NotSame(test.Pipeline.BlockedResult.Scene, test.State.CurrentScene);
        Assert.Equal(resize ? 1 : 2, test.State.CurrentScene!.Viewport.Zoom);
        Assert.Equal(test.State.EditorState.Viewport, test.State.CurrentScene.Viewport);
        await test.AssertPanReusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReusedResizeAndBendHandlesCommitAndRestoreExactHistory(bool bend)
    {
        await using var test = await Fixture.CreateAsync();
        var semanticId = bend ? BpmnDemoPipeline.ThirdSequenceFlowId : BpmnDemoPipeline.TaskId;
        var visual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == semanticId);
        if (bend)
        {
            await BpmnModelerTestComposition.SetRoutingTypeAsync(test.Session, visual.Id, ConnectorRoutingType.Manual);
            var edge = test.State.ProjectedGraph!.Edges.Single(item => item.Source.SemanticElementId == semanticId);
            var route = test.State.RoutingResult!.Routes.Single(item => item.ProjectedEdgeId == edge.Id).Path;
            await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                visual.Id, [route[0], new PointD(route[0].X + 70, route[0].Y),
                    new PointD(route[0].X + 70, route[^1].Y), route[^1]]));
        }
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [visual.Id], viewport: test.State.EditorState.Viewport))).Succeeded);
        var roleKey = bend ? Canvas2DRouteGestureMetadata.HandleRole : Canvas2DResizeGestureMetadata.HandleRole;
        var roleValue = bend ? Canvas2DRouteGestureMetadata.BendRole : Canvas2DResizeGestureMetadata.SouthEastRole;
        var handle = test.State.CurrentScene!.Items.First(item => item.Origin.VisualStateId == visual.Id &&
            item.Metadata.TryGetValue(roleKey, out var role) && role.TextValue == roleValue);
        await test.AssertPanReusedAsync();
        Assert.Same(handle, test.State.CurrentScene!.Items.Single(item => item.Id == handle.Id));
        var before = test.Snapshot;
        var history = test.State.HistoryStatus.EntryCount;
        var start = new PointD(handle.Bounds.X + handle.Bounds.Width / 2, handle.Bounds.Y + handle.Bounds.Height / 2);
        var end = start + new VectorD(24, 16);
        PointD Css(PointD point) => test.State.CurrentScene!.ViewportTransform.TransformPoint(point);
        await using var interaction = new Canvas2DInteractionController(test.Session);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await interaction.PointerPressedAsync(new Canvas2DPointerInput(122, Css(start), buttons: 1))).Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await interaction.PointerMovedAsync(new Canvas2DPointerInput(122, Css(end), buttons: 1))).Status);
        Assert.Same(before, test.Snapshot);
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await interaction.PointerReleasedAsync(new Canvas2DPointerInput(122, Css(end)))).Status);
        await test.Session.WaitForIdleAsync();
        var changed = test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == visual.Id);
        var original = before.VisualModel.VisualStates.Single(item => item.Id == visual.Id);
        var changedRoute = bend ? BpmnModelerTestComposition.SavedRoute(test.Snapshot, visual.Id) : null;
        if (bend)
        {
            Assert.Equal(original, changed);
            Assert.NotEqual(BpmnModelerTestComposition.SavedRoute(before, visual.Id), changedRoute);
        }
        else Assert.NotEqual(original, changed);
        Assert.Equal(history + 1, test.State.HistoryStatus.EntryCount);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        if (bend)
            Assert.Equal(BpmnModelerTestComposition.SavedRoute(before, visual.Id),
                BpmnModelerTestComposition.SavedRoute(test.Snapshot, visual.Id));
        else Assert.Equal(original, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == visual.Id));
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(changed, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == visual.Id));
        if (bend) Assert.Equal(changedRoute, BpmnModelerTestComposition.SavedRoute(test.Snapshot, visual.Id));
    }

    private static bool IsGuide(Canvas2DSceneItem item) =>
        item.Origin.StableSourceKey?.StartsWith("document-boundary:", StringComparison.Ordinal) == true;

    internal sealed class Fixture(DocumentCanvasComposition composition, EditingSession session,
        Canvas2DRenderer renderer, PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution execution,
        PipelineProbe pipeline, ContributionProbe[] contributions, EventProbe events) : IAsyncDisposable
    {
        internal EditingSession Session => session;
        internal Canvas2DRenderer Renderer => renderer;
        internal EditingSessionState State => session.CaptureState();
        internal DocumentSnapshot Snapshot => session.TryCaptureDocumentSnapshot(out var snapshot)
            ? snapshot : throw new InvalidOperationException("The test session is no longer attached.");
        internal PipelineProbe Pipeline => pipeline;
        internal ContributionProbe[] Contributions => contributions;
        internal EventProbe Events => events;
        internal PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution Execution => execution;

        internal Canvas2DInteractionController CreateInteractionController() => new(session,
            connectionCreationCatalog: composition.AnchorConnectionCreationCatalog,
            creationIdentityProvider: composition.DocumentCreationIdentityProvider,
            endpointReconnectionCatalog: composition.EndpointReconnectionCatalog,
            spatialEditPlanners: composition.SpatialEditPlanners);

        internal async Task<VisualStateSnapshot> PlaceServiceTaskAsync(SemanticElementId poolId, PointD localCenter)
        {
            var region = State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(item => item.ContainerSemanticElementId == poolId);
            var scenePoint = region.MapLocalToScene(localCenter);
            Assert.True(region.Bounds.Contains(scenePoint));
            return await PlaceActivityAsync("service-task", scenePoint);
        }

        internal async Task<VisualStateSnapshot> PlaceActivityAsync(string toolboxName, PointD scenePoint)
        {
            var selection = new ToolboxSelectionState();
            selection.Select(new ToolboxItemId($"bpmn:toolbox:{toolboxName}"));
            var placement = new ToolboxPlacementController(composition.ToolboxPlacementCatalog,
                selection, composition.DocumentCreationIdentityProvider, composition.SpatialEditPlanners);
            var css = State.CurrentScene!.ViewportTransform.TransformPoint(scenePoint);
            await placement.UpdatePreviewAtCssPointAsync(session, css);
            var result = await placement.TryPlaceAtCssPointAsync(session, css);
            Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            await session.WaitForIdleAsync();
            return Snapshot.VisualModel.VisualStates.Single(item => item.Id == result.CreatedVisualStateId);
        }

        internal async Task AssertPanReusedAsync()
        {
            // Establish stable content away from the two viewport-dependent origin guides.
            Assert.True((await session.PanViewportAsync(new VectorD(-1000, -1000))).Succeeded);
            var before = State;
            var snapshot = Snapshot;
            var reuses = pipeline.Reuses;
            var uploads = execution.FullUploadCount;
            var viewportRenders = execution.ViewportRenderCount;
            Assert.True((await session.PanViewportAsync(new VectorD(17.5, -8.25))).Succeeded);
            Assert.Equal(reuses + 1, pipeline.Reuses);
            Assert.Same(snapshot, Snapshot);
            Assert.Equal(before.HistoryStatus, State.HistoryStatus);
            Assert.Equal(uploads, execution.FullUploadCount);
            Assert.Equal(viewportRenders + 1, execution.ViewportRenderCount);
        }

        internal async Task ExecuteAsync(ICommand command)
        {
            var result = await session.ExecuteAsync(command);
            Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            await session.WaitForIdleAsync();
            Assert.True(State.Status == EditingSessionStatus.Ready,
                string.Join("; ", State.RuntimeDiagnostics.Concat(State.PresentationDiagnostics).Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        }

        internal async Task EnablePoolsAsync()
        {
            await ExecuteAsync(new SetModelProfileAvailabilityCommand(Snapshot.DocumentId, Snapshot.Revision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            await ExecuteAsync(new CreateOrganizationalPoolCommand(Snapshot.DocumentId, Snapshot.Revision,
                PoolA, State.ActiveScopeId, OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Operations"));
            await ExecuteAsync(new CreateOrganizationalPoolCommand(Snapshot.DocumentId, Snapshot.Revision,
                PoolB, State.ActiveScopeId, OrganizationalPoolCreationMode.Empty, "Fulfillment"));
            var regions = Snapshot.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == State.ActiveScopeId).Geometry.Regions;
            var capacity = regions.Single(region => region.ContainerSemanticElementId == PoolA).ExpandedHeight;
            foreach (var region in regions.Where(region => region.ContainerSemanticElementId != PoolA && region.ExpandedHeight < capacity))
                await ExecuteAsync(new SetOrganizationalRegionExpandedHeightCommand(Snapshot.DocumentId,
                    Snapshot.Revision, State.ActiveScopeId, region.Id, capacity));
            var flow = Snapshot.SemanticModel.Relationships.Single(item => item.Id == BpmnDemoPipeline.ThirdSequenceFlowId);
            await ExecuteAsync(new AssignOrganizationalElementCommand(Snapshot.DocumentId, Snapshot.Revision, flow.TargetId, PoolB));
            var unassigned = Snapshot.SemanticModel.Elements.First(item => item.Id != flow.SourceId &&
                item.Id != flow.TargetId && BpmnSemanticTypes.IsFlowNode(item.TypeId) &&
                item.AttachedToElementId is null && Snapshot.SemanticModel.GetScope(item.Id).Id == State.ActiveScopeId);
            await ExecuteAsync(new UnassignOrganizationalElementCommand(Snapshot.DocumentId, Snapshot.Revision, unassigned.Id));
        }

        internal static async Task<Fixture> CreateAsync(DocumentCanvasComposition? suppliedComposition = null)
        {
            var composition = suppliedComposition ?? await BpmnModelerTestComposition.CreateDemoAsync();
            var source = composition.Configuration;
            var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
            var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
                OrganizationalPluginRegistration.Create(eligibility,
                    OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
            var contributions = registrations.Select(item => item.Contributor is ICanvas2DScopeGeometryContributor geometry
                ? new GeometryContributionProbe(item.Contributor, geometry, item.Descriptor.PlacementDependency)
                : new ContributionProbe(item.Contributor, item.Descriptor.PlacementDependency)).ToArray();
            var events = new EventProbe();
            var configuration = new EditingSessionConfiguration(source.ProjectionEngine, source.LayoutEngine,
                source.LayoutAlgorithmId, source.RoutingEngine, source.RoutingAlgorithmId,
                new Canvas2DSceneBuilder(contributors: registrations.Select((item, index) =>
                    new Canvas2DSceneContributorRegistration(item.Descriptor, contributions[index], item.Stage))),
                initialEditorState: source.InitialEditorState,
                commandHandlers: source.CommandHandlers, commandValidators: source.CommandValidators,
                historyPolicies: source.HistoryPolicies, documentChangedSubscribers: [events],
                connectorAnchorPolicyProvider: source.ConnectorAnchorPolicyProvider,
                modelProfileCatalog: source.ModelProfileCatalog,
                routingInputPreparer: source.RoutingInputPreparer);
            var execution = new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution();
            var renderer = new Canvas2DRenderer(execution, new Canvas2DRendererConfiguration(fontResources:
                [new Canvas2DFontResource("org.dejavu.DejaVuSans", "2.37", "DejaVu Sans", "fonts/DejaVuSans-2.37.ttf")], defaultFontFamily: "DejaVu Sans"));
            Assert.True((await renderer.InitializeAsync("a122", new Canvas2DSurfaceSize(1000, 700, 1))).Succeeded);
            var pipeline = new PipelineProbe(new EditingSessionPipeline(configuration, renderer));
            var attached = await EditingSession.AttachAsync(composition.Document, renderer, configuration, pipeline);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            var session = attached.Session!;
            Assert.True((await session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, 1))).Succeeded);
            return new(composition, session, renderer, execution, pipeline, contributions, events);
        }

        public async ValueTask DisposeAsync()
        {
            pipeline.ReleasePan.TrySetResult();
            pipeline.ReleaseMove.TrySetResult();
            await session.DisposeAsync();
            await renderer.DisposeAsync();
        }
    }

    internal sealed class EventProbe : IDocumentChangedSubscriber
    {
        internal int Count { get; private set; }
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    internal class ContributionProbe(ICanvas2DSceneContributor inner,
        Canvas2DScenePlacementDependency placementDependency = Canvas2DScenePlacementDependency.Unknown) : ICanvas2DSceneContributor, ICanvas2DConnectorPresentationRouter
    {
        internal Canvas2DScenePlacementDependency PlacementDependency => placementDependency;
        internal int Calls { get; private set; }
        internal int Routes { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return inner.Contribute(context);
        }
        public Canvas2DConnectorPresentationRoute Route(Canvas2DConnectorPresentationRoutingRequest context)
        {
            Routes++;
            return ((ICanvas2DConnectorPresentationRouter)inner).Route(context);
        }
    }

    internal sealed class GeometryContributionProbe(ICanvas2DSceneContributor inner,
        ICanvas2DScopeGeometryContributor geometry, Canvas2DScenePlacementDependency placementDependency)
        : ContributionProbe(inner, placementDependency), ICanvas2DScopeGeometryContributor
    {
        internal int BasePreparations { get; private set; }
        internal int PresentationPreparations { get; private set; }

        public Canvas2DScopeGeometryBaseResult PrepareBase(Canvas2DScopeGeometryBaseContext context)
        {
            BasePreparations++;
            return geometry.PrepareBase(context);
        }

        public Canvas2DScopeGeometryPresentationResult PreparePresentation(Canvas2DScopeGeometryPresentationContext context)
        {
            PresentationPreparations++;
            return geometry.PreparePresentation(context);
        }
    }

    internal sealed class PipelineProbe(ISessionPipelineProcessing inner) : ISessionPipelineProcessing
    {
        internal int Reuses { get; private set; }
        internal int MoveReuses { get; private set; }
        internal int ConnectorLabelMoveReuses { get; private set; }
        internal int SelectionReuses { get; private set; }
        internal int PlacementReuses { get; private set; }
        internal int SpatialResizeReuses { get; private set; }
        internal TimeSpan PlacementCompositionElapsed { get; private set; }
        internal int Rebuilds { get; private set; }
        internal int FullRuns { get; private set; }
        internal bool BlockPan { get; set; }
        internal bool BlockMove { get; set; }
        internal TaskCompletionSource MoveBuilt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseMove { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PanBuilt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleasePan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal EditingSessionPipelineResult? BlockedResult { get; private set; }

        public async ValueTask<EditingSessionPipelineResult> RebuildSceneForTransientPresentationAsync(Canvas2DScene previousScene,
            DocumentSnapshot document, EditingSessionPipelineArtifacts artifacts, EditorStateSnapshot editorState,
            ModelProfileViewStateSnapshot view, ModelProfileElementViewStateSnapshot elements, CancellationToken cancellationToken)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await inner.RebuildSceneForTransientPresentationAsync(previousScene, document, artifacts, editorState, view, elements, cancellationToken);
            if (result.ReusedMoveContent) { MoveReuses++; }
            else if (result.ReusedConnectorLabelMoveContent) { ConnectorLabelMoveReuses++; }
            else if (result.ReusedSelectionContent) { SelectionReuses++; }
            else if (result.ReusedSpatialResizeContent) { SpatialResizeReuses++; }
            else if (result.ReusedPlacementContent)
            {
                PlacementReuses++;
                PlacementCompositionElapsed += Stopwatch.GetElapsedTime(started);
            }
            else { Rebuilds++; }
            if (BlockMove)
            {
                BlockMove = false;
                BlockedResult = result;
                MoveBuilt.TrySetResult();
                await ReleaseMove.Task;
            }
            return result;
        }

        public async ValueTask<EditingSessionPipelineResult> RebuildSceneForPanAsync(Canvas2DScene previousScene,
            DocumentSnapshot document, EditingSessionPipelineArtifacts artifacts, EditorStateSnapshot editorState,
            ModelProfileViewStateSnapshot view, ModelProfileElementViewStateSnapshot elements, CancellationToken cancellationToken)
        {
            var result = await inner.RebuildSceneForPanAsync(previousScene, document, artifacts, editorState, view, elements, cancellationToken);
            if (result.ReusedPanContent) { Reuses++; } else { Rebuilds++; }
            if (BlockPan)
            {
                BlockPan = false;
                BlockedResult = result;
                PanBuilt.TrySetResult();
                await ReleasePan.Task;
            }
            return result;
        }

        public ValueTask<EditingSessionPipelineResult> RunFullAsync(DocumentSnapshot document, DocumentScopeId scope,
            EditorStateSnapshot editor, CancellationToken cancellationToken) =>
            RunFullAsync(document, scope, editor, ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, cancellationToken);

        public ValueTask<EditingSessionPipelineResult> RunFullAsync(DocumentSnapshot document, DocumentScopeId scope,
            EditorStateSnapshot editor, ModelProfileViewStateSnapshot view, ModelProfileElementViewStateSnapshot elements, CancellationToken cancellationToken)
        {
            FullRuns++;
            return inner.RunFullAsync(document, scope, editor, view, elements, cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(DocumentSnapshot document,
            DocumentScopeId scope, EditingSessionPipelineArtifacts previousArtifacts, ImmutableArray<EditingSessionPipelineArtifacts> history,
            NodeGeometryPipelineImpact impact, EditorStateSnapshot editor, CancellationToken cancellationToken) =>
            RunPreservingNodeLayoutAsync(document, scope, previousArtifacts, history, impact, editor,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, cancellationToken);

        public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(DocumentSnapshot document,
            DocumentScopeId scope, EditingSessionPipelineArtifacts previousArtifacts, ImmutableArray<EditingSessionPipelineArtifacts> history,
            NodeGeometryPipelineImpact impact, EditorStateSnapshot editor, ModelProfileViewStateSnapshot view,
            ModelProfileElementViewStateSnapshot elements, CancellationToken cancellationToken)
        {
            FullRuns++;
            return inner.RunPreservingNodeLayoutAsync(document, scope, previousArtifacts, history, impact, editor, view, elements, cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(EditingSessionPipelineArtifacts artifacts,
            VisualModelSnapshot visual, EditorStateSnapshot editor, CancellationToken cancellationToken)
        {
            Rebuilds++;
            return inner.RebuildSceneAsync(artifacts, visual, editor, cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(DocumentSnapshot document,
            EditingSessionPipelineArtifacts artifacts, EditorStateSnapshot editor, ModelProfileViewStateSnapshot view,
            ModelProfileElementViewStateSnapshot elements, CancellationToken cancellationToken)
        {
            Rebuilds++;
            return inner.RebuildSceneAsync(document, artifacts, editor, view, elements, cancellationToken);
        }
    }
}
