using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN101OrganizationalPoolIntegrationTests
{
    [Theory]
    [InlineData(0.6d)]
    [InlineData(0.8d)]
    [InlineData(1d)]
    [InlineData(1.1d)]
    public async Task VisibilityRepeatedlyHidesOnlyDecorationWithOverlappingCanonicalNodes(double zoom)
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        Assert.True((await fixture.Session.UpdateViewportAsync(
            new ViewportSnapshot(zoom, new VectorD(43d, -27d)))).Succeeded);
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [VisibilityFixture.B1], viewport: fixture.State.EditorState.Viewport))).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        var validation = Assert.IsType<ValidationSnapshot>(await fixture.Harness.Host.ValidateAsync());
        var document = fixture.Document;
        var original = fixture.State;
        var processItems = VisibilityProcessItems(original.CurrentScene!);
        var plan = original.CurrentScene!.SpatialPresentationPlan;
        var decorations = VisibilityGraphics(original.CurrentScene!);
        Assert.NotEmpty(decorations);
        Assert.Equal(fixture.Visual(VisibilityFixture.A1).Position, fixture.Visual(VisibilityFixture.B1).Position);
        Assert.Equal(fixture.Visual(VisibilityFixture.A1).Position, fixture.Visual(VisibilityFixture.U1).Position);
        Assert.False(fixture.Node(VisibilityFixture.A1).Bounds.Intersects(fixture.Node(VisibilityFixture.B1).Bounds));
        Assert.False(fixture.Node(VisibilityFixture.B1).Bounds.Intersects(fixture.Node(VisibilityFixture.U1).Bounds));

        foreach (var visible in new[] { false, true, false, true, false, true })
        {
            await fixture.SetVisibleAsync(visible);
            var current = fixture.State;
            Assert.Same(document, fixture.Document);
            Assert.Equal(original.DocumentRevision, current.DocumentRevision);
            Assert.Equal(original.HistoryStatus, current.HistoryStatus);
            Assert.Equal(original.ActiveScopeId, current.ActiveScopeId);
            Assert.Equal(original.EditorState.Viewport, current.EditorState.Viewport);
            Assert.Equal(VisibilityFixture.B1, Assert.Single(current.EditorState.Selection));
            Assert.Same(original.ProjectedGraph, current.ProjectedGraph);
            Assert.Same(original.LayoutResult, current.LayoutResult);
            Assert.Same(original.RoutingResult, current.RoutingResult);
            Assert.Same(validation, fixture.Harness.Host.CaptureState().ValidationSnapshot);
            Assert.NotNull(current.CurrentScene!.SpatialPresentationPlan);
            Assert.Equal(plan, current.CurrentScene.SpatialPresentationPlan);
            Assert.True(processItems.AsSpan().SequenceEqual(VisibilityProcessItems(current.CurrentScene)));
            if (visible)
            {
                Assert.True(decorations.AsSpan().SequenceEqual(VisibilityGraphics(current.CurrentScene)));
            }
            else
            {
                Assert.Empty(VisibilityGraphics(current.CurrentScene));
                Assert.DoesNotContain(current.CurrentScene.Items, item =>
                    item.Origin.SemanticElementId == PoolAId || item.Origin.SemanticElementId == PoolBId);
            }
        }
    }

    [Fact]
    public async Task VisibilityHiddenEditingKeepsAssignmentsDestinationsAndExactPresentationWhenShown()
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        await fixture.SetVisibleAsync(false);
        var initial = fixture.State;
        var route = fixture.Visual(VisibilityFixture.CrossFlow).Route;

        await fixture.MoveAsync(VisibilityFixture.B1, null, new PointD(340d, 100d));
        await fixture.MoveAsync(VisibilityFixture.U1, PoolAId, new PointD(500d, 100d));
        var newTask = await fixture.PlaceAsync("task", PoolBId, new PointD(400d, 220d));
        var newEvent = await fixture.PlaceAsync("timer-catch-event", null, new PointD(80d, 40d));
        await fixture.MoveAsync(VisibilityFixture.U1, PoolBId, new PointD(80d, 100d));

        Assert.Equal(initial.HistoryStatus.EntryCount + 5, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(initial.DocumentRevision.Value + 5, fixture.State.DocumentRevision.Value);
        Assert.Null(AssignedPool(fixture.Document, fixture.Visual(VisibilityFixture.B1).SemanticElementId));
        Assert.Equal(PoolBId, AssignedPool(fixture.Document, fixture.Visual(VisibilityFixture.U1).SemanticElementId));
        Assert.Equal(PoolBId, AssignedPool(fixture.Document, newTask.SemanticElementId));
        Assert.Null(AssignedPool(fixture.Document, newEvent.SemanticElementId));
        Assert.Equal(route, fixture.Visual(VisibilityFixture.CrossFlow).Route);
        Assert.Empty(VisibilityGraphics(fixture.State.CurrentScene!));
        var editedDocument = fixture.Document;
        var editedItems = VisibilityProcessItems(fixture.State.CurrentScene!);
        var editedPlan = fixture.State.CurrentScene!.SpatialPresentationPlan;

        await fixture.SetVisibleAsync(true);

        Assert.Same(editedDocument, fixture.Document);
        Assert.True(editedItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene!)));
        Assert.Equal(editedPlan, fixture.State.CurrentScene!.SpatialPresentationPlan);
        Assert.NotEmpty(VisibilityGraphics(fixture.State.CurrentScene));
    }

    [Fact]
    public async Task VisibilityOffRetainsSpatialCompositionWhileAvailabilityOffUsesCanonicalPresentation()
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        var original = fixture.Document;
        await fixture.SetVisibleAsync(false);
        var hiddenItems = VisibilityProcessItems(fixture.State.CurrentScene!);
        Assert.NotNull(fixture.State.CurrentScene!.SpatialPresentationPlan);
        Assert.Empty(VisibilityGraphics(fixture.State.CurrentScene));
        var hidden = fixture.State;

        await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
            state.DocumentId, state.DocumentRevision,
            [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, false)]));

        Assert.Null(fixture.State.CurrentScene!.SpatialPresentationPlan);
        Assert.Empty(VisibilityGraphics(fixture.State.CurrentScene));
        Assert.Equal(new RectD(100d, 100d, 120d, 80d), fixture.Node(VisibilityFixture.A1).Bounds);
        Assert.Equal(fixture.Node(VisibilityFixture.A1).Bounds, fixture.Node(VisibilityFixture.B1).Bounds);
        Assert.Equal(fixture.Node(VisibilityFixture.A1).Bounds, fixture.Node(VisibilityFixture.U1).Bounds);
        Assert.True(original.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(
            fixture.Document.SemanticModel.ProfileAssignments.AsSpan()));
        Assert.True(original.VisualModel.VisualStates.AsSpan().SequenceEqual(fixture.Document.VisualModel.VisualStates.AsSpan()));
        Assert.True(original.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            fixture.Document.VisualModel.ProfileElementPresentations.AsSpan()));
        Assert.Equal(hidden.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(hidden.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);

        await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
            state.DocumentId, state.DocumentRevision,
            [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));

        Assert.False(fixture.State.ModelProfileViewState.IsPreferredVisible(OrganizationalModelProfile.Id));
        Assert.NotNull(fixture.State.CurrentScene!.SpatialPresentationPlan);
        Assert.Empty(VisibilityGraphics(fixture.State.CurrentScene));
        Assert.True(hiddenItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene)));
        AssertEquivalentIgnoringRevision(original, fixture.Document);
    }

    [Fact]
    public async Task VisibilityHiddenPoolDeletionPreservesProcessAndUndoRestoresSpatialAssignments()
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        await fixture.SetVisibleAsync(false);
        var before = fixture.Document;
        var processItems = VisibilityProcessItems(fixture.State.CurrentScene!);
        await fixture.ExecuteAsync(state => new DeleteOrganizationalPoolCommand(
            state.DocumentId, state.DocumentRevision, PoolBId));
        Assert.True(before.VisualModel.VisualStates.AsSpan().SequenceEqual(fixture.Document.VisualModel.VisualStates.AsSpan()));
        Assert.Null(AssignedPool(fixture.Document, fixture.Visual(VisibilityFixture.B1).SemanticElementId));
        Assert.Null(AssignedPool(fixture.Document, fixture.Visual(VisibilityFixture.B2).SemanticElementId));
        Assert.Null(fixture.Node(VisibilityFixture.B1).SpatialRegion!.ContainerSemanticElementId);
        Assert.Empty(VisibilityGraphics(fixture.State.CurrentScene!));
        var deletedItems = VisibilityProcessItems(fixture.State.CurrentScene!);

        await fixture.SetVisibleAsync(true);
        Assert.True(deletedItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene!)));
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(before, fixture.Document);
        Assert.True(processItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene!)));
        Assert.Equal(PoolBId, AssignedPool(fixture.Document, fixture.Visual(VisibilityFixture.B1).SemanticElementId));
    }

    [Fact]
    public async Task VisibilityHiddenBoundaryOwnerMovementAndCollapsedPoolStateRemainExact()
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        var owner = fixture.Visual(VisibilityFixture.B1);
        var boundaryId = new VisualStateId("n101:visibility:boundary:visual");
        await fixture.ExecuteAsync(state => new CreateBpmnTimerBoundaryEventCommand(
            state.DocumentId, state.DocumentRevision, new SemanticElementId("n101:visibility:boundary"),
            boundaryId, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
            new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height),
            "Timer", "PT5M", targetScopeId: state.ActiveScopeId));
        var originalBoundary = fixture.Visual(boundaryId);
        var originalItems = VisibilityProcessItems(fixture.State.CurrentScene!);
        await fixture.SetVisibleAsync(false);
        Assert.True(originalItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene!)));
        var beforeMove = fixture.Document;
        var originalOwnerScene = fixture.Node(owner.Id).Bounds;
        var originalBoundaryScene = fixture.Node(boundaryId).Bounds;
        await fixture.MoveAsync(owner.Id, PoolBId, owner.Position + new VectorD(24d, 12d), boundaryId);
        Assert.Equal(originalOwnerScene.Translate(new VectorD(24d, 12d)), fixture.Node(owner.Id).Bounds);
        Assert.Equal(originalBoundaryScene.Translate(new VectorD(24d, 12d)), fixture.Node(boundaryId).Bounds);
        Assert.Equal(originalBoundary.BoundaryAttachment, fixture.Visual(boundaryId).BoundaryAttachment);
        Assert.True(beforeMove.SemanticModel.Elements.AsSpan().SequenceEqual(fixture.Document.SemanticModel.Elements.AsSpan()));

        Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
            fixture.State.ModelProfileElementViewState.WithCollapsed(OrganizationalModelProfile.Id, PoolBId, true))).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        var collapsed = fixture.State;
        foreach (var visible in new[] { true, false, true })
        {
            await fixture.SetVisibleAsync(visible);
            Assert.Equal(collapsed.ModelProfileElementViewState, fixture.State.ModelProfileElementViewState);
            Assert.Equal(collapsed.DocumentRevision, fixture.State.DocumentRevision);
            Assert.Equal(collapsed.HistoryStatus, fixture.State.HistoryStatus);
            Assert.Equal(collapsed.CurrentScene!.SpatialPresentationPlan, fixture.State.CurrentScene!.SpatialPresentationPlan);
            Assert.DoesNotContain(fixture.State.CurrentScene.Items, item =>
                item.IsVisible && item.Origin.VisualStateId == owner.Id);
        }
    }

    [Fact]
    public async Task VisibilityHiddenManualBendEndpointReconnectAndConnectedMoveKeepCanonicalGuidance()
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        await fixture.SetVisibleAsync(false);
        var flowId = VisibilityFixture.CrossFlow;
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [flowId]))).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        var route = fixture.Visual(flowId).Route;
        var bend = fixture.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == flowId &&
            item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.BendIndex));
        var bendCenter = VisibilityCenter(bend.Bounds);
        await fixture.DragPresentedAsync(bendCenter, bendCenter + new VectorD(0d, 24d));
        Assert.Equal(route[1] + new VectorD(0d, 24d), fixture.Visual(flowId).Route[1]);

        var newAnchor = new ConnectorAnchorId("n101:visibility:reconnect-target");
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId, state.DocumentRevision,
            VisibilityFixture.B2, newAnchor, ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Target, 0));
        var endpoint = fixture.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == flowId &&
            item.Metadata.TryGetValue(Canvas2DConnectorEndpointMetadata.HandleRole, out var role) &&
            role.TextValue == Canvas2DConnectorEndpointMetadata.EndEndpointRole);
        await fixture.ReconnectPresentedAsync(VisibilityCenter(endpoint.Bounds), VisibilityFixture.B2, newAnchor);
        Assert.Equal(newAnchor, fixture.Visual(flowId).TargetAnchorId);
        var reconnected = fixture.Visual(flowId);
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: fixture.State.EditorState.Viewport))).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        var beforeMove = fixture.Document;

        await fixture.MoveAsync(VisibilityFixture.A2, null, new PointD(360d, 100d));
        Assert.Equal(reconnected, fixture.Visual(flowId));
        var connector = Assert.Single(fixture.State.CurrentScene!.Items, item => item.Origin.VisualStateId == flowId &&
            item.Layer == Canvas2DSceneLayer.Connector && item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount));
        var displayed = Canvas2DConnectorPathMetadata.Resolve(connector).Select(connector.Transform.TransformPoint).ToArray();
        var sourceBounds = fixture.Node(VisibilityFixture.A2).Bounds;
        var targetBounds = fixture.Node(VisibilityFixture.B2).Bounds;
        Assert.Equal(new PointD(sourceBounds.Right, sourceBounds.Top + sourceBounds.Height / 2d), displayed[0]);
        Assert.Equal(new PointD(targetBounds.Left + targetBounds.Width / 2d, targetBounds.Bottom), displayed[^1]);
        var moved = fixture.Document;
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(beforeMove, fixture.Document);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(moved, fixture.Document);
        var hiddenItems = VisibilityProcessItems(fixture.State.CurrentScene!);
        await fixture.SetVisibleAsync(true);
        Assert.True(hiddenItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene!)));
    }

    [Fact]
    public async Task VisibilityIsScopeKeyedAcrossSubProcessNavigationWithRetainedViewportAndHistory()
    {
        await using var fixture = await VisibilityFixture.CreateAsync();
        await fixture.SetVisibleAsync(false);
        var viewport = new ViewportSnapshot(1.1d, new VectorD(43d, -27d));
        Assert.True((await fixture.Session.UpdateViewportAsync(viewport)).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        var main = fixture.State;
        var processItems = VisibilityProcessItems(main.CurrentScene!);
        Assert.True((await fixture.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        Assert.True(fixture.State.ModelProfileViewState.IsPreferredVisible(OrganizationalModelProfile.Id));
        var childPool = new SemanticElementId("n101:visibility:child-pool");
        await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
            state.DocumentId, state.DocumentRevision, childPool, state.ActiveScopeId,
            OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Child"));
        Assert.NotEmpty(VisibilityGraphics(fixture.State.CurrentScene!));
        Assert.True((await fixture.Session.UpdateViewportAsync(new ViewportSnapshot(0.8d, new VectorD(-12d, 25d)))).Succeeded);

        Assert.True((await fixture.Session.NavigateToScopeAsync(main.ActiveScopeId)).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        Assert.False(fixture.State.ModelProfileViewState.IsPreferredVisible(OrganizationalModelProfile.Id));
        Assert.Equal(main.EditorState.Viewport, fixture.State.EditorState.Viewport);
        // N8 records both scope navigations in global chronological History, without revisions.
        Assert.Equal(main.HistoryStatus.EntryCount + 3, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(main.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.NotNull(fixture.State.CurrentScene!.SpatialPresentationPlan);
        Assert.Empty(VisibilityGraphics(fixture.State.CurrentScene));
        Assert.True(processItems.AsSpan().SequenceEqual(VisibilityProcessItems(fixture.State.CurrentScene)));
    }

    private static PointD VisibilityCenter(RectD bounds) =>
        new(bounds.Left + bounds.Width / 2d, bounds.Top + bounds.Height / 2d);

    private static Canvas2DSceneItem[] VisibilityProcessItems(Canvas2DScene scene) =>
        scene.Items.Where(item => item.Origin.VisualStateId is not null || item.Origin.ProjectedObjectId is not null)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();

    private static Canvas2DSceneItem[] VisibilityGraphics(Canvas2DScene scene) =>
        scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Background && item.IsVisible &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.RegisteredExtension) != 0 &&
                item.Origin.VisualStateId is null && item.Origin.ProjectedObjectId is null &&
                item.Style.Opacity > 0d && (item.Style.Fill is not null || item.Style.Stroke is not null))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();

    private sealed class VisibilityFixture : IAsyncDisposable
    {
        internal static VisualStateId A1 { get; } = new("n101:visibility:a1:visual");
        internal static VisualStateId A2 { get; } = new("n101:visibility:a2:visual");
        internal static VisualStateId B1 { get; } = new("n101:visibility:b1:visual");
        internal static VisualStateId B2 { get; } = new("n101:visibility:b2:visual");
        internal static VisualStateId U1 { get; } = new("n101:visibility:u1:visual");
        internal static VisualStateId CrossFlow { get; } = new("n101:visibility:cross-flow:visual");
        private readonly ToolboxSelectionState _toolboxSelection = new();
        private readonly ToolboxPlacementController _placement;

        private VisibilityFixture(PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness)
        {
            Harness = harness;
            var composition = harness.Composition;
            _placement = new ToolboxPlacementController(composition.ToolboxPlacementCatalog,
                _toolboxSelection, composition.DocumentCreationIdentityProvider, composition.SpatialEditPlanners);
            Controller = new Canvas2DInteractionController(Session,
                connectionCreationCatalog: composition.AnchorConnectionCreationCatalog,
                creationIdentityProvider: composition.DocumentCreationIdentityProvider,
                endpointReconnectionCatalog: composition.EndpointReconnectionCatalog,
                spatialEditPlanners: composition.SpatialEditPlanners);
        }

        internal PhaseM31BpmnPropertiesIntegrationTests.HostHarness Harness { get; }
        internal EditingSession Session => Harness.Session;
        internal EditingSessionState State => Session.CaptureState();
        internal DocumentSnapshot Document => Harness.Composition.Document.CaptureSnapshot();
        private Canvas2DInteractionController Controller { get; }

        internal static async Task<VisibilityFixture> CreateAsync()
        {
            var fixture = new VisibilityFixture(await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync());
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
                state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            foreach (var poolId in new[] { PoolAId, PoolBId })
            {
                await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                    state.DocumentId, state.DocumentRevision, poolId, state.ActiveScopeId,
                    poolId == PoolAId ? OrganizationalPoolCreationMode.AdoptEligibleUnassigned : OrganizationalPoolCreationMode.Empty,
                    poolId == PoolAId ? "A" : "B"));
            }

            foreach (var id in new[] { A1, A2, B1, B2, U1 })
            {
                var semanticId = new SemanticElementId(id.Value.Replace(":visual", string.Empty, StringComparison.Ordinal));
                await fixture.ExecuteAsync(state => id == A2
                    ? new CreateBpmnTimerCatchEventCommand(state.DocumentId, state.DocumentRevision, semanticId, id,
                        new PointD(400d, 40d), new SizeD(36d, 36d), "A2", VisualPlacementMode.Pinned,
                        targetScopeId: state.ActiveScopeId)
                    : id == B2
                        ? new CreateBpmnExclusiveGatewayCommand(state.DocumentId, state.DocumentRevision, semanticId, id,
                            new PointD(540d, 240d), new SizeD(50d, 50d), "B2", "B2", VisualPlacementMode.Pinned,
                            targetScopeId: state.ActiveScopeId)
                        : new CreateBpmnTaskCommand(state.DocumentId, state.DocumentRevision, semanticId, id,
                            new PointD(100d, 100d), new SizeD(120d, 80d), id.Value, id.Value,
                            id == A1 ? 601 : id == B1 ? 602 : 603, VisualPlacementMode.Pinned,
                            targetScopeId: state.ActiveScopeId));
                if (id != U1)
                {
                    await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
                        state.DocumentId, state.DocumentRevision, semanticId, id == A1 || id == A2 ? PoolAId : PoolBId));
                }
            }

            foreach (var target in new[] { A2, B2 })
            {
                var source = target == A2 ? A1 : A2;
                var sourceAnchor = new ConnectorAnchorId($"{source.Value}:source-anchor");
                await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId, state.DocumentRevision,
                    source, sourceAnchor, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
                var anchor = new ConnectorAnchorId($"{target.Value}:target-anchor");
                await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId, state.DocumentRevision,
                    target, anchor, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
                var flowId = target == B2 ? CrossFlow : new VisualStateId("n101:visibility:same-flow:visual");
                await fixture.ExecuteAsync(state => new CreateBpmnSequenceFlowCommand(state.DocumentId, state.DocumentRevision,
                    new SemanticElementId($"{flowId.Value}:semantic"), flowId,
                    fixture.Visual(source).SemanticElementId, fixture.Visual(target).SemanticElementId, sourceAnchor, anchor));
            }
            await fixture.ExecuteAsync(state => new UpdateConnectionRouteCommand(state.DocumentId, state.DocumentRevision,
                CrossFlow, [new PointD(436d, 58d), new PointD(700d, 60d), new PointD(540d, 265d)]));
            return fixture;
        }

        internal VisualStateSnapshot Visual(VisualStateId id) => Document.VisualModel.VisualStates.Single(item => item.Id == id);
        internal Canvas2DSceneItem Node(VisualStateId id) => State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == id && Canvas2DNodeBodyMetadata.IsNodeBody(item));
        internal Task ExecuteAsync(Func<EditingSessionState, ICommand> factory) =>
            PhaseN101OrganizationalPoolIntegrationTests.ExecuteAsync(Session, factory(State)).AsTask();

        internal async Task SetVisibleAsync(bool visible)
        {
            var before = Document;
            var state = State;
            var updated = await Session.UpdateModelProfileViewStateAsync(state.ModelProfileViewState
                .WithPreferredVisibility(OrganizationalModelProfile.Id, visible));
            Assert.True(updated.Succeeded, Diagnostics(updated.Diagnostics));
            await WaitForReadyAsync(Session);
            Assert.Same(before, Document);
            Assert.Equal(state.HistoryStatus, State.HistoryStatus);
            Assert.Equal(state.DocumentRevision, State.DocumentRevision);
        }

        internal async Task MoveAsync(VisualStateId id, SemanticElementId? poolId, PointD canonicalPosition,
            VisualStateId? attachedVisualId = null)
        {
            var before = Document;
            var state = State;
            var region = State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(item => item.ContainerSemanticElementId == poolId);
            var start = Node(id).Bounds.TopLeft + new VectorD(18d, 18d);
            var finish = region.MapLocalToScene(canonicalPosition + new VectorD(18d, 18d));
            var viewport = State.CurrentScene.ViewportTransform;
            await Controller.PointerPressedAsync(new Canvas2DPointerInput(6101, viewport.TransformPoint(start), buttons: 1));
            await Controller.PointerMovedAsync(new Canvas2DPointerInput(6101, viewport.TransformPoint(finish), buttons: 1));
            var result = await Controller.PointerReleasedAsync(new Canvas2DPointerInput(6101, viewport.TransformPoint(finish)));
            Assert.True(result.Status == Canvas2DInteractionStatus.Committed, Diagnostics(result.Diagnostics));
            await WaitForReadyAsync(Session);
            Assert.Equal(id, Assert.Single(State.EditorState.Selection));
            Assert.Equal(canonicalPosition, Visual(id).Position);
            Assert.Equal(poolId, AssignedPool(Document, Visual(id).SemanticElementId));
            AssertUnrelatedVisualsUnchanged(before, Document, id, attachedVisualId);
            Assert.Equal(state.DocumentRevision.Increment(), State.DocumentRevision);
            Assert.Equal(state.HistoryStatus.EntryCount + 1, State.HistoryStatus.EntryCount);
            Assert.Empty(VisibilityGraphics(State.CurrentScene!));
        }

        internal async Task DragPresentedAsync(PointD start, PointD finish)
        {
            var state = State;
            var viewport = state.CurrentScene!.ViewportTransform;
            await Controller.PointerPressedAsync(new Canvas2DPointerInput(6201, viewport.TransformPoint(start), buttons: 1));
            await Controller.PointerMovedAsync(new Canvas2DPointerInput(6201, viewport.TransformPoint(finish), buttons: 1));
            var result = await Controller.PointerReleasedAsync(new Canvas2DPointerInput(6201, viewport.TransformPoint(finish)));
            Assert.True(result.Status == Canvas2DInteractionStatus.Committed, Diagnostics(result.Diagnostics));
            await WaitForReadyAsync(Session);
            Assert.Equal(state.DocumentRevision.Increment(), State.DocumentRevision);
            Assert.Equal(state.HistoryStatus.EntryCount + 1, State.HistoryStatus.EntryCount);
            Assert.Empty(VisibilityGraphics(State.CurrentScene!));
        }

        internal async Task ReconnectPresentedAsync(PointD start, VisualStateId targetId, ConnectorAnchorId anchorId)
        {
            var state = State;
            var viewport = state.CurrentScene!.ViewportTransform;
            var started = await Controller.PointerPressedAsync(new Canvas2DPointerInput(6301, viewport.TransformPoint(start), buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Updated, started.Status);
            var anchor = State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == targetId &&
                item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) && id.TextValue == anchorId.Value);
            var finish = viewport.TransformPoint(VisibilityCenter(anchor.Bounds));
            await Controller.PointerMovedAsync(new Canvas2DPointerInput(6301, finish, buttons: 1));
            var result = await Controller.PointerReleasedAsync(new Canvas2DPointerInput(6301, finish));
            Assert.True(result.Status == Canvas2DInteractionStatus.Committed, Diagnostics(result.Diagnostics));
            await WaitForReadyAsync(Session);
            Assert.Equal(state.DocumentRevision.Increment(), State.DocumentRevision);
            Assert.Equal(state.HistoryStatus.EntryCount + 1, State.HistoryStatus.EntryCount);
            Assert.Empty(VisibilityGraphics(State.CurrentScene!));
        }

        internal async Task<VisualStateSnapshot> PlaceAsync(string type, SemanticElementId? poolId, PointD center)
        {
            var before = Document;
            var state = State;
            var region = state.CurrentScene!.SpatialPresentationPlan!.Regions.Single(item => item.ContainerSemanticElementId == poolId);
            var point = region.MapLocalToScene(center);
            _toolboxSelection.Select(new ToolboxItemId($"bpmn:toolbox:{type}"));
            var result = await _placement.TryPlaceAtCssPointAsync(Session, state.CurrentScene.ViewportTransform.TransformPoint(point));
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            await WaitForReadyAsync(Session);
            var visual = Visual(Assert.IsType<VisualStateId>(result.CreatedVisualStateId));
            var halfSize = new VectorD(visual.Size.Width / 2d, visual.Size.Height / 2d);
            Assert.Equal(center - halfSize, visual.Position);
            Assert.Equal(point - halfSize, Node(visual.Id).Bounds.TopLeft);
            Assert.Equal(poolId, AssignedPool(Document, visual.SemanticElementId));
            AssertUnrelatedVisualsUnchanged(before, Document);
            Assert.Equal(state.DocumentRevision.Increment(), State.DocumentRevision);
            Assert.Equal(state.HistoryStatus.EntryCount + 1, State.HistoryStatus.EntryCount);
            Assert.Empty(VisibilityGraphics(State.CurrentScene!));
            return visual;
        }

        public async ValueTask DisposeAsync()
        {
            await Controller.DisposeAsync();
            await Harness.DisposeAsync();
        }
    }
}
