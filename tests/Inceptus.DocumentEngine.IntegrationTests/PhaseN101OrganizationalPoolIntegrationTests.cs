using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN101OrganizationalPoolIntegrationTests
{
    private static readonly SemanticElementId PoolAId =
        new("inceptus:n10.1:integration:pool:a");
    private static readonly SemanticElementId PoolBId =
        new("inceptus:n10.1:integration:pool:b");

    [Fact]
    public async Task PoolsPartitionOneActiveProcessAndDeleteWithoutCopyingProcessContent()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var document = composition.Document;
        var renderer = await CreateRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var activeScopeId = session.CaptureState().ActiveScopeId;
        var initial = document.CaptureSnapshot();
        var initialElements = initial.SemanticModel.Elements;
        var initialRelationships = initial.SemanticModel.Relationships;
        var initialVisuals = initial.VisualModel.VisualStates;
        var eligibleIds = initialElements
            .Where(element =>
                element.ContainmentKind == SemanticElementContainmentKind.Scope &&
                element.AttachedToElementId is null &&
                BpmnSemanticTypes.IsFlowNode(element.TypeId) &&
                initial.SemanticModel.GetScope(element.Id).Id == activeScopeId)
            .Select(static element => element.Id)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();

        await ExecuteAsync(
            session,
            new SetModelProfileAvailabilityCommand(
                document.DocumentId,
                document.Revision,
                [new ModelProfileAvailabilityChange(
                    OrganizationalModelProfile.Id,
                    isAvailable: true)]));
        await ExecuteAsync(
            session,
            new CreateOrganizationalPoolCommand(
                document.DocumentId,
                document.Revision,
                PoolAId,
                activeScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned,
                "Operations"));

        var firstPool = document.CaptureSnapshot();
        Assert.Equal(activeScopeId, session.CaptureState().ActiveScopeId);
        Assert.True(initialElements.AsSpan().SequenceEqual(
            firstPool.SemanticModel.Elements
                .Where(element => element.Id != PoolAId)
                .ToArray()));
        Assert.True(initialRelationships.AsSpan().SequenceEqual(
            firstPool.SemanticModel.Relationships.AsSpan()));
        Assert.True(initialVisuals.AsSpan().SequenceEqual(
            firstPool.VisualModel.VisualStates.AsSpan()));
        Assert.Equal(
            eligibleIds,
            AssignedIds(firstPool, PoolAId));
        Assert.DoesNotContain(
            firstPool.VisualModel.VisualStates,
            visual => visual.SemanticElementId == PoolAId);
        Assert.Equal(0, PresentationOrder(firstPool, PoolAId));

        await ExecuteAsync(
            session,
            new CreateOrganizationalPoolCommand(
                document.DocumentId,
                document.Revision,
                PoolBId,
                activeScopeId,
                OrganizationalPoolCreationMode.Empty,
                "Fulfillment"));
        var secondPool = document.CaptureSnapshot();
        Assert.Empty(AssignedIds(secondPool, PoolBId));
        Assert.Equal(0, PresentationOrder(secondPool, PoolAId));
        Assert.Equal(1, PresentationOrder(secondPool, PoolBId));

        var reassignedId = Assert.Single(eligibleIds.Take(1));
        await ExecuteAsync(
            session,
            new AssignOrganizationalElementCommand(
                document.DocumentId,
                document.Revision,
                reassignedId,
                PoolBId));
        Assert.Equal(
            PoolBId,
            AssignedPool(document.CaptureSnapshot(), reassignedId));

        var beforeDelete = document.CaptureSnapshot();
        await ExecuteAsync(
            session,
            new DeleteOrganizationalPoolCommand(
                document.DocumentId,
                document.Revision,
                PoolBId));
        var deleted = document.CaptureSnapshot();
        Assert.False(deleted.SemanticModel.TryGetElement(PoolBId, out _));
        Assert.Null(AssignedPool(deleted, reassignedId));
        Assert.True(initialElements.AsSpan().SequenceEqual(
            deleted.SemanticModel.Elements
                .Where(element => element.Id != PoolAId)
                .ToArray()));
        Assert.True(initialRelationships.AsSpan().SequenceEqual(
            deleted.SemanticModel.Relationships.AsSpan()));
        Assert.True(initialVisuals.AsSpan().SequenceEqual(
            deleted.VisualModel.VisualStates.AsSpan()));
        Assert.Equal(activeScopeId, session.CaptureState().ActiveScopeId);

        var undo = await session.UndoAsync();
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        await WaitForReadyAsync(session);
        AssertEquivalentIgnoringRevision(beforeDelete, document.CaptureSnapshot());
        Assert.Equal(activeScopeId, session.CaptureState().ActiveScopeId);
    }

    [Fact]
    public async Task SixIndependentNodeDragsPreserveDroppedPositionsSiblingsAndAssignmentsAcrossAllRegions()
    {
        await using var fixture = await DragFixture.CreateAsync();
        var activeScope = fixture.State.ActiveScopeId;
        var movement = new VectorD(24d, 12d);

        // Move the leftmost node before its right-hand sibling in each region. Comparing only
        // persisted geometry misses a content-minimum normalization that cancels the visible move.
        foreach (var id in DragFixture.TaskIds)
        {
            var before = fixture.Document;
            var beforeState = fixture.State;
            var original = fixture.Visual(id);
            var originalScene = fixture.Node(id).Bounds;
            var pool = AssignedPool(before, original.SemanticElementId);
            var siblings = DragFixture.TaskIds
                .Where(other => other != id &&
                    AssignedPool(before, fixture.Visual(other).SemanticElementId) == pool)
                .ToDictionary(other => other, other => fixture.Node(other).Bounds);

            await fixture.DragNodeAsync(id, movement);

            Assert.Equal(original.Position + movement, fixture.Visual(id).Position);
            Assert.Equal(original.Size, fixture.Visual(id).Size);
            Assert.Equal(originalScene.Translate(movement), fixture.Node(id).Bounds);
            AssertOnlyVisualChanged(before, fixture.Document, id);
            Assert.True(before.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(
                fixture.Document.SemanticModel.ProfileAssignments.AsSpan()));
            Assert.Equal(beforeState.DocumentRevision.Increment(), fixture.State.DocumentRevision);
            Assert.Equal(beforeState.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
            Assert.Equal(id, Assert.Single(fixture.State.EditorState.Selection));
            Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
            Assert.Equal(activeScope, fixture.State.ActiveScopeId);
            foreach (var sibling in siblings)
            {
                Assert.Equal(sibling.Value, fixture.Node(sibling.Key).Bounds);
            }

            var moved = fixture.Document;
            var movedScene = fixture.Node(id).Bounds;
            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(before, fixture.Document);
            Assert.Equal(originalScene, fixture.Node(id).Bounds);
            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(moved, fixture.Document);
            Assert.Equal(movedScene, fixture.Node(id).Bounds);
        }
    }

    [Fact]
    public async Task CrossRegionDragCommitsOneCanonicalMoveAndAssignmentAndRestoresExactPresentation()
    {
        await using var fixture = await DragFixture.CreateAsync();
        var id = DragFixture.TaskIds[0];
        foreach (var destinationPool in new SemanticElementId?[] { PoolBId, null, PoolAId })
        {
            var before = fixture.Document;
            var beforeState = fixture.State;
            var original = fixture.Visual(id);
            var originalScene = fixture.Node(id).Bounds;
            var destination = fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
                region => region.ContainerSemanticElementId == destinationPool);
            var targetPosition = new PointD(340d, 100d);
            var targetScenePoint = destination.MapLocalToScene(targetPosition + DragFixture.GrabOffset);
            var start = originalScene.TopLeft + DragFixture.GrabOffset;

            await fixture.DragAsync(start, targetScenePoint);

            Assert.Equal(targetPosition, fixture.Visual(id).Position);
            Assert.Equal(original.Id, fixture.Visual(id).Id);
            Assert.Equal(original.SemanticElementId, fixture.Visual(id).SemanticElementId);
            Assert.Equal(destinationPool, AssignedPool(fixture.Document, original.SemanticElementId));
            Assert.Equal(targetScenePoint - DragFixture.GrabOffset, fixture.Node(id).Bounds.TopLeft);
            AssertOnlyVisualChanged(before, fixture.Document, id);
            Assert.Equal(beforeState.DocumentRevision.Increment(), fixture.State.DocumentRevision);
            Assert.Equal(beforeState.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
            Assert.Equal(beforeState.ActiveScopeId, fixture.State.ActiveScopeId);
            var moved = fixture.Document;
            var movedScene = fixture.Node(id).Bounds;

            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(before, fixture.Document);
            Assert.Equal(originalScene, fixture.Node(id).Bounds);
            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(moved, fixture.Document);
            Assert.Equal(movedScene, fixture.Node(id).Bounds);
        }
    }

    [Fact]
    public async Task ShrinkingSourceRowReflowsLowerPoolWithoutRewritingCanonicalDropOrSiblings()
    {
        await using var fixture = await DragFixture.CreateAsync();
        var id = DragFixture.TaskIds[1];
        var siblingId = DragFixture.TaskIds[2];
        var before = fixture.Document;
        var originalScene = fixture.Node(id).Bounds;
        var siblingScene = fixture.Node(siblingId).Bounds;
        var destination = fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            region => region.ContainerSemanticElementId == PoolBId);
        var canonicalTarget = new PointD(340d, 100d);
        await fixture.DragAsync(originalScene.TopLeft + DragFixture.GrabOffset,
            destination.MapLocalToScene(canonicalTarget + DragFixture.GrabOffset));

        var currentDestination = fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            region => region.Id == destination.Id);
        var stackDelta = currentDestination.MapLocalToScene(new PointD(0d, 0d)) -
            destination.MapLocalToScene(new PointD(0d, 0d));
        Assert.True(stackDelta.Y < 0d);
        Assert.Equal(0d, stackDelta.X);
        Assert.Equal(canonicalTarget, fixture.Visual(id).Position);
        Assert.Equal(currentDestination.MapLocalToScene(canonicalTarget), fixture.Node(id).Bounds.TopLeft);
        Assert.Equal(siblingScene.Translate(stackDelta), fixture.Node(siblingId).Bounds);
        AssertOnlyVisualChanged(before, fixture.Document, id);
        Assert.Equal(PoolBId, AssignedPool(fixture.Document, fixture.Visual(id).SemanticElementId));
        var moved = fixture.Document;
        var movedScene = fixture.Node(id).Bounds;
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(before, fixture.Document);
        Assert.Equal(originalScene, fixture.Node(id).Bounds);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(moved, fixture.Document);
        Assert.Equal(movedScene, fixture.Node(id).Bounds);
    }

    [Fact]
    public async Task CrossPoolManualRouteAndEndpointDragsKeepCanonicalGuidanceAndOneDisplayedConnector()
    {
        await using var fixture = await DragFixture.CreateAsync();
        var sourceId = DragFixture.TaskIds[0];
        var targetId = DragFixture.TaskIds[2];
        var sourceAnchor = new ConnectorAnchorId("n101:drag:source-anchor");
        var targetAnchor = new ConnectorAnchorId("n101:drag:target-anchor");
        var flowId = new SemanticElementId("n101:drag:cross-pool-flow");
        var flowVisualId = new VisualStateId("n101:drag:cross-pool-flow:visual");
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(
            state.DocumentId, state.DocumentRevision, sourceId, sourceAnchor,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(
            state.DocumentId, state.DocumentRevision, targetId, targetAnchor,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
        await fixture.ExecuteAsync(state => new CreateBpmnSequenceFlowCommand(
            state.DocumentId, state.DocumentRevision, flowId, flowVisualId,
            fixture.Visual(sourceId).SemanticElementId, fixture.Visual(targetId).SemanticElementId,
            sourceAnchor, targetAnchor));

        var source = fixture.Visual(sourceId);
        var target = fixture.Visual(targetId);
        var route = new[]
        {
            new PointD(source.Position.X + source.Size.Width, source.Position.Y + source.Size.Height / 2d),
            new PointD(740d, 360d),
            new PointD(target.Position.X, target.Position.Y + target.Size.Height / 2d),
        };
        await fixture.ExecuteAsync(state => new UpdateConnectionRouteCommand(
            state.DocumentId, state.DocumentRevision, flowVisualId, route));
        var selected = await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [flowVisualId], viewport: fixture.State.EditorState.Viewport));
        Assert.True(selected.Succeeded, Diagnostics(selected.Diagnostics));
        await WaitForReadyAsync(fixture.Session);
        var bend = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == flowVisualId &&
            item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.BendIndex));
        var bendCenter = new PointD(bend.Bounds.Left + bend.Bounds.Width / 2d,
            bend.Bounds.Top + bend.Bounds.Height / 2d);
        await fixture.DragAsync(bendCenter, bendCenter + new VectorD(0d, 24d));
        Assert.Equal(route[1] + new VectorD(0d, 24d), fixture.Visual(flowVisualId).Route[1]);
        var editedRoute = fixture.Visual(flowVisualId).Route;
        var relationship = fixture.Document.SemanticModel.Relationships.Single(item => item.Id == flowId);

        foreach (var id in new[] { sourceId, targetId })
        {
            var before = fixture.Document;
            var beforeScene = fixture.Node(id).Bounds;
            await fixture.DragNodeAsync(id, new VectorD(28d, 16d));
            Assert.Equal(beforeScene.Translate(new VectorD(28d, 16d)), fixture.Node(id).Bounds);
            AssertOnlyVisualChanged(before, fixture.Document, id);
            Assert.Equal(editedRoute, fixture.Visual(flowVisualId).Route);
            Assert.Equal(relationship, fixture.Document.SemanticModel.Relationships.Single(item => item.Id == flowId));
            AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
            var moved = fixture.Document;

            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(before, fixture.Document);
            AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(moved, fixture.Document);
            AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
        }

        // Reuse the populated Unassigned destination with a connected node, not only an
        // isolated Task. Organizational transfer must not author presentation offsets into
        // the manual route or duplicate the existing SequenceFlow.
        var beforeTransfer = fixture.Document;
        var unassignedIdentity = UnassignedRegion(fixture).Id;
        await MoveToRegionAsync(fixture, targetId, null, new PointD(340d, 100d), unassignedIdentity);
        Assert.Equal(editedRoute, fixture.Visual(flowVisualId).Route);
        Assert.Equal(relationship, fixture.Document.SemanticModel.Relationships.Single(item => item.Id == flowId));
        AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
        var transferred = fixture.Document;
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(beforeTransfer, fixture.Document);
        AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(transferred, fixture.Document);
        AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
        await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(80d, 40d), unassignedIdentity);
        Assert.Equal(editedRoute, fixture.Visual(flowVisualId).Route);
        AssertConnectorEndpoints(fixture, flowVisualId, sourceId, targetId);
    }

    private static void AssertConnectorEndpoints(
        DragFixture fixture, VisualStateId flowId, VisualStateId sourceId, VisualStateId targetId)
    {
        var connector = fixture.Connector(flowId);
        var path = Canvas2DConnectorPathMetadata.Resolve(connector)
            .Select(connector.Transform.TransformPoint).ToArray();
        var source = fixture.Node(sourceId).Bounds;
        var target = fixture.Node(targetId).Bounds;
        Assert.Equal(new PointD(source.Right, source.Top + source.Height / 2d), path[0]);
        Assert.Equal(new PointD(target.Left, target.Top + target.Height / 2d), path[^1]);
        var mapping = Assert.IsType<Canvas2DConnectorPresentationMapping>(connector.ConnectorPresentationMapping);
        Assert.False(mapping.IsSameRegion);
        Assert.Equal(fixture.Visual(flowId).Route[1], mapping.CanonicalEditablePath[1]);
        Assert.Equal(mapping.CanonicalEditablePath[1], mapping.DisplayedEditablePath[1]);
    }

    private static void AssertOnlyVisualChanged(
        DocumentSnapshot before, DocumentSnapshot after, VisualStateId movedId)
    {
        Assert.Equal(before.VisualModel.VisualStates.Length, after.VisualModel.VisualStates.Length);
        foreach (var visual in before.VisualModel.VisualStates.Where(visual => visual.Id != movedId))
        {
            Assert.Equal(visual, after.VisualModel.VisualStates.Single(candidate => candidate.Id == visual.Id));
        }

        Assert.True(before.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            after.VisualModel.ProfileElementPresentations.AsSpan()));
    }

    private sealed partial class DragFixture : IAsyncDisposable
    {
        internal static VectorD GrabOffset { get; } = new(18d, 18d);
        internal static VisualStateId[] TaskIds { get; } =
            new[] { "a1", "a2", "b1", "b2", "u1", "u2" }.Select(
                name => new VisualStateId($"n101:drag:{name}:visual")).ToArray();

        private DragFixture(EditingSession session, DocumentCanvasComposition composition)
        {
            Session = session;
            SpatialEditPlanners = composition.SpatialEditPlanners;
            Placement = new ToolboxPlacementController(composition.ToolboxPlacementCatalog,
                ToolboxSelection, composition.DocumentCreationIdentityProvider, composition.SpatialEditPlanners);
            Controller = new Canvas2DInteractionController(session,
                connectionCreationCatalog: composition.AnchorConnectionCreationCatalog,
                creationIdentityProvider: composition.DocumentCreationIdentityProvider,
                endpointReconnectionCatalog: composition.EndpointReconnectionCatalog,
                spatialEditPlanners: composition.SpatialEditPlanners);
        }

        internal EditingSession Session { get; }
        internal ToolboxSelectionState ToolboxSelection { get; } = new();
        internal ToolboxPlacementController Placement { get; }
        internal Canvas2DSpatialEditPlannerCatalog SpatialEditPlanners { get; }
        internal Canvas2DInteractionController Controller { get; }
        internal EditingSessionState State => Session.CaptureState();
        internal DocumentSnapshot Document
        {
            get
            {
                Assert.True(Session.TryCaptureDocumentSnapshot(out var document));
                return Assert.IsType<DocumentSnapshot>(document);
            }
        }

        internal static async Task<DragFixture> CreateAsync(bool includeUnassignedTasks = true)
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            var attached = await EditingSession.AttachAsync(
                composition.Document, await CreateRendererAsync(), composition.Configuration);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            var fixture = new DragFixture(Assert.IsType<EditingSession>(attached.Session), composition);
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
                state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolAId, state.ActiveScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Pool A"));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolBId, state.ActiveScopeId,
                OrganizationalPoolCreationMode.Empty, "Pool B"));
            var positions = new[]
            {
                new PointD(40d, 400d), new PointD(520d, 480d),
                new PointD(180d, 160d), new PointD(520d, 240d),
                new PointD(160d, 160d), new PointD(520d, 240d),
            };
            for (var index = 0; index < (includeUnassignedTasks ? TaskIds.Length : 4); index++)
            {
                var id = TaskIds[index];
                var semanticId = new SemanticElementId($"n101:drag:task:{index}");
                await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(
                    state.DocumentId, state.DocumentRevision, semanticId, id,
                    positions[index], new SizeD(160d, 100d), $"T{index}", $"Task {index}", 500 + index,
                    VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
                if (index < 4)
                {
                    await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
                        state.DocumentId, state.DocumentRevision, semanticId,
                        index < 2 ? PoolAId : PoolBId));
                }
            }

            return fixture;
        }

        internal VisualStateSnapshot Visual(VisualStateId id) =>
            Document.VisualModel.VisualStates.Single(visual => visual.Id == id);

        internal Canvas2DSceneItem Node(VisualStateId id) =>
            State.CurrentScene!.Items.Single(item =>
                item.Origin.VisualStateId == id && Canvas2DNodeBodyMetadata.IsNodeBody(item));

        internal Canvas2DSceneItem Connector(VisualStateId id) =>
            Assert.Single(State.CurrentScene!.Items, item =>
                item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Connector &&
                item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount));

        internal PointD Css(PointD point) => State.CurrentScene!.ViewportTransform.TransformPoint(point);

        internal Task DragNodeAsync(VisualStateId id, VectorD delta)
        {
            var start = Node(id).Bounds.TopLeft + GrabOffset;
            return DragAsync(start, start + delta);
        }

        internal async Task DragAsync(PointD start, PointD finish)
        {
            await Controller.PointerPressedAsync(new Canvas2DPointerInput(7101, Css(start), buttons: 1));
            var preview = await Controller.PointerMovedAsync(new Canvas2DPointerInput(7101, Css(finish), buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Updated, preview.Status);
            var result = await Controller.PointerReleasedAsync(new Canvas2DPointerInput(7101, Css(finish)));
            Assert.True(result.Status == Canvas2DInteractionStatus.Committed, Diagnostics(result.Diagnostics));
            await WaitForReadyAsync(Session);
        }

        internal async Task ExecuteAsync(Func<EditingSessionState, ICommand> create) =>
            await PhaseN101OrganizationalPoolIntegrationTests.ExecuteAsync(Session, create(State));

        public async ValueTask DisposeAsync()
        {
            await Controller.DisposeAsync();
            await Session.DisposeAsync();
        }
    }

    private static SemanticElementId[] AssignedIds(
        Contracts.Documents.DocumentSnapshot document,
        SemanticElementId poolId) =>
        document.SemanticModel.ProfileAssignments
            .Where(assignment =>
                assignment.ProfileId == OrganizationalModelProfile.Id &&
                assignment.ContainerSemanticElementId == poolId)
            .Select(static assignment => assignment.SemanticElementId)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();

    private static SemanticElementId? AssignedPool(
        Contracts.Documents.DocumentSnapshot document,
        SemanticElementId semanticElementId) =>
        document.SemanticModel.ProfileAssignments.SingleOrDefault(assignment =>
            assignment.ProfileId == OrganizationalModelProfile.Id &&
            assignment.SemanticElementId == semanticElementId)?.ContainerSemanticElementId;

    private static int PresentationOrder(
        Contracts.Documents.DocumentSnapshot document,
        SemanticElementId poolId) =>
        document.VisualModel.ProfileElementPresentations.Single(presentation =>
            presentation.ProfileId == OrganizationalModelProfile.Id &&
            presentation.SemanticElementId == poolId).Order;

    private static async ValueTask<Canvas2DRenderer> CreateRendererAsync()
    {
        var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var initialization = await renderer.InitializeAsync(
            "phase-n101-organizational-pools",
            new Canvas2DSurfaceSize(1600d, 1000d, 1d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        return renderer;
    }

    private static async ValueTask ExecuteAsync(
        EditingSession session,
        ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await WaitForReadyAsync(session);
    }

    private static async ValueTask WaitForReadyAsync(EditingSession session)
    {
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static void AssertEquivalentIgnoringRevision(
        Contracts.Documents.DocumentSnapshot expected,
        Contracts.Documents.DocumentSnapshot actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.True(expected.SemanticModel.Elements.AsSpan().SequenceEqual(
            actual.SemanticModel.Elements.AsSpan()));
        Assert.True(expected.SemanticModel.Relationships.AsSpan().SequenceEqual(
            actual.SemanticModel.Relationships.AsSpan()));
        Assert.True(expected.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
            actual.SemanticModel.NestedScopes.AsSpan()));
        Assert.True(expected.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
            actual.SemanticModel.ScopeMemberships.AsSpan()));
        Assert.Equal(expected.SemanticModel.ModelProfiles, actual.SemanticModel.ModelProfiles);
        Assert.True(expected.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(
            actual.SemanticModel.ProfileAssignments.AsSpan()));
        Assert.True(expected.VisualModel.VisualStates.AsSpan().SequenceEqual(
            actual.VisualModel.VisualStates.AsSpan()));
        Assert.True(expected.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            actual.VisualModel.ProfileElementPresentations.AsSpan()));
        Assert.Equal(
            expected.Metadata.SystemManagedProperties,
            actual.Metadata.SystemManagedProperties);
        Assert.Equal(
            expected.Metadata.ExtensionProperties,
            actual.Metadata.ExtensionProperties);
    }

    private static string Diagnostics(
        IEnumerable<Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
}
