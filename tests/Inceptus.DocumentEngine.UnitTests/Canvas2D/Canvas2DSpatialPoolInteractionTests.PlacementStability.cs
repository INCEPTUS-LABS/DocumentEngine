using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddingToolboxTaskToPopulatedRealDemoPoolPreservesEveryExistingNode(bool previouslyMovedReviewOrder)
    {
        await using var fixture = await Fixture.CreatePopulatedDemoPoolFixtureAsync();
        if (previouslyMovedReviewOrder)
        {
            await fixture.DragAsync(fixture.NodePoint(BpmnDemoPipeline.TaskVisualId), new VectorD(40d, 20d));
        }

        var before = fixture.Document;
        var stateBefore = fixture.State;
        var regionBefore = Destination(fixture, Fixture.PoolA);
        var beforeTrace = ObservePlacementStability("before", fixture);
        var projectedNodes = stateBefore.ProjectedGraph!.Nodes.ToDictionary(node => node.Source.VisualStateId!);
        var displayedBefore = projectedNodes.Keys.ToDictionary(id => id, id => fixture.Node(id).Bounds);
        var effectiveBefore = stateBefore.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId, node => node.Bounds);
        var point = new PointD(1100d, 300d);
        Assert.True(regionBefore.Bounds.Contains(regionBefore.MapLocalToScene(point)));

        var added = await PlaceInRegionAsync(fixture, Fixture.PoolA, "task", point);

        Assert.True(added.IsCommitted, Diagnostics(added.Diagnostics));
        var afterTrace = ObservePlacementStability("after", fixture);
        var changedCanonical = before.VisualModel.VisualStates.Where(original =>
            fixture.Visual(original.Id) != original && !fixture.Visual(original.Id).Equals(original)).ToArray();
        var changedDisplayed = projectedNodes.Keys.Where(id => displayedBefore[id] != fixture.Node(id).Bounds).ToArray();
        var changedLayout = fixture.State.LayoutResult!.Nodes.Where(node =>
            effectiveBefore.TryGetValue(node.ProjectedObjectId, out var old) && old != node.Bounds).ToArray();
        Assert.True(changedCanonical.Length == 0 && changedDisplayed.Length == 0 && changedLayout.Length == 0,
            $"Changed canonical: [{string.Join(",", changedCanonical.Select(visual => visual.Id))}]; " +
            $"changed displayed: [{string.Join(",", changedDisplayed)}]; " +
            $"changed retained layout: [{string.Join(",", changedLayout.Select(node => node.ProjectedObjectId))}]" +
            Environment.NewLine + beforeTrace + Environment.NewLine + afterTrace);
        Assert.Equal(regionBefore.Id, Destination(fixture, Fixture.PoolA).Id);
        Assert.Equal(regionBefore.LocalToSceneTransform, Destination(fixture, Fixture.PoolA).LocalToSceneTransform);
        Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(before.Revision.Increment(), fixture.Document.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedMixedToolboxInsertionsPreserveEditedDemoGeometryAndHistory(bool hidden)
    {
        await using var fixture = await Fixture.CreatePopulatedDemoPoolFixtureAsync();
        await fixture.DragAsync(fixture.NodePoint(BpmnDemoPipeline.TaskVisualId), new VectorD(40d, 20d));
        await fixture.SelectAsync(BpmnDemoPipeline.TaskVisualId);
        var handle = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Text(item, Canvas2DResizeGestureMetadata.HandleRole) == "southeast");
        await fixture.DragAsync(Center(handle.Bounds), new VectorD(28d, 18d));
        if (hidden)
        {
            await SetOrganizationalGraphicsAsync(fixture, false);
        }

        var insertions = new (string Tool, PointD? Point)[]
        {
            ("task", new PointD(1100d, 300d)),
            ("task", new PointD(1250d, 400d)),
            ("task", new PointD(1400d, 500d)),
            ("timer-catch-event", new PointD(30d, 30d)),
            ("exclusive-gateway", null),
            ("task", null),
        };
        foreach (var (tool, explicitPoint) in insertions)
        {
            var before = fixture.Document;
            var stateBefore = fixture.State;
            var region = Destination(fixture, Fixture.PoolA);
            var point = explicitPoint ?? region.MapSceneToLocal(new PointD(region.Bounds.Right - 2d, region.Bounds.Bottom - 2d));
            var oldNodes = stateBefore.ProjectedGraph!.Nodes.ToDictionary(node => node.Source.VisualStateId!);
            var oldDisplayed = oldNodes.Keys.ToDictionary(id => id, id => fixture.Node(id).Bounds);
            var oldGeometry = stateBefore.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);

            var result = await PlaceInRegionAsync(fixture, Fixture.PoolA, tool, point);

            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            var createdId = Assert.IsType<VisualStateId>(result.CreatedVisualStateId);
            var current = fixture.Document;
            var afterDisplayed = fixture.State.ProjectedGraph!.Nodes.ToDictionary(node => node.Source.VisualStateId!,
                node => fixture.Node(node.Source.VisualStateId!).Bounds);
            AssertUnrelatedVisualsUnchanged(before, current);
            Assert.Equal(region.Id, Destination(fixture, Fixture.PoolA).Id);
            Assert.Equal(region.LocalToSceneTransform, Destination(fixture, Fixture.PoolA).LocalToSceneTransform);
            foreach (var (id, projectedNode) in oldNodes)
            {
                Assert.Equal(oldDisplayed[id], fixture.Node(id).Bounds);
                Assert.Same(oldGeometry[projectedNode.Id], fixture.State.LayoutResult!.Nodes.Single(node =>
                    node.ProjectedObjectId == projectedNode.Id));
            }

            Assert.Equal(Fixture.PoolA, current.SemanticModel.ProfileAssignments.Single(assignment =>
                assignment.SemanticElementId == fixture.Visual(createdId).SemanticElementId).ContainerSemanticElementId);
            Assert.Equal(before.Revision.Increment(), current.Revision);
            Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            fixture.AssertReady();
            Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
            Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), fixture.Document.SemanticModel.ProfileAssignments.AsEnumerable());
            foreach (var (id, bounds) in oldDisplayed)
            {
                Assert.Equal(bounds, fixture.Node(id).Bounds);
            }

            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            fixture.AssertReady();
            Assert.Equal(current.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
            Assert.Equal(current.SemanticModel.ProfileAssignments.AsEnumerable(), fixture.Document.SemanticModel.ProfileAssignments.AsEnumerable());
            foreach (var (id, bounds) in afterDisplayed)
            {
                Assert.Equal(bounds, fixture.Node(id).Bounds);
            }
        }

        var beforeShow = fixture.State;
        await SetOrganizationalGraphicsAsync(fixture, true);
        Assert.Equal(beforeShow.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Equal(beforeShow.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Same(beforeShow.ProjectedGraph, fixture.State.ProjectedGraph);
        Assert.Same(beforeShow.LayoutResult, fixture.State.LayoutResult);
        Assert.Same(beforeShow.RoutingResult, fixture.State.RoutingResult);
    }

    private sealed partial class Fixture
    {
        internal static async Task<Fixture> CreatePopulatedDemoPoolFixtureAsync()
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            var attached = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
            var fixture = new Fixture(Assert.IsType<EditingSession>(attached.Session), composition);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(state.DocumentId, state.DocumentRevision,
                PoolA, state.ActiveScopeId, OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Populated demo"));
            return fixture;
        }
    }

    private static string ObservePlacementStability(string label, Fixture fixture)
    {
        var state = fixture.State;
        var region = Destination(fixture, Fixture.PoolA);
        var frame = state.CurrentScene!.Items.Single(item => item.Origin.SemanticElementId == Fixture.PoolA &&
            item.Origin.StableSourceKey?.EndsWith(":pool-background", StringComparison.Ordinal) == true);
        var layout = state.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId, node => node.Bounds);
        var nodeTrace = state.ProjectedGraph!.Nodes.OrderBy(node => node.Source.VisualStateId!.Value, StringComparer.Ordinal)
            .Select(node =>
            {
                var visual = fixture.Visual(node.Source.VisualStateId!);
                return $"{visual.Id}: canonical={VisualBounds(visual)}, mode={visual.PlacementMode}, " +
                    $"effective={layout[node.Id]}, displayed={fixture.Node(visual.Id).Bounds}";
            });
        var connectorTrace = state.CurrentScene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Connector &&
            item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal).Select(item =>
                $"{item.Origin.VisualStateId}: [{string.Join(",", Canvas2DConnectorPathMetadata.Resolve(item).Select(item.Transform.TransformPoint))}]");
        return $"{label}: revision={state.DocumentRevision}; history={state.HistoryStatus.EntryCount}; generation={state.Generation}; " +
            $"region={region.Id}; translation={region.LocalToSceneTransform}; destination={region.Bounds}; frame={frame.Bounds}" +
            Environment.NewLine + string.Join(Environment.NewLine, nodeTrace) +
            Environment.NewLine + string.Join(Environment.NewLine, connectorTrace);
    }
}
