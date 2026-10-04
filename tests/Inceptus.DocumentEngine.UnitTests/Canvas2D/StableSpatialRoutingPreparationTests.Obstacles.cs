using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class StableSpatialRoutingPreparationTests
{
    [Fact]
    public async Task SameRegionLocalCoordinatesInDifferentPoolsOnlyObstructTheMatchingLogicalBand()
    {
        var fixture = await CreateParallelPoolFixtureAsync();
        var before = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        var routeA = before.Connectors.Single(record => record.VisualStateId == new VisualStateId("v:flow-a"));
        var routeB = before.Connectors.Single(record => record.VisualStateId == new VisualStateId("v:flow-b"));
        var obstacle = new RectD(370, 356, 120, 80);
        Assert.False(CrossesBody(routeA.Path, obstacle));
        Assert.True(CrossesBody(routeB.Path, obstacle));
        fixture.Policy.Searches.Clear();
        var document = fixture.Document;
        var added = await fixture.Processor.ExecuteAsync(document, new CompoundDocumentCommand(document.DocumentId,
            document.Revision,
            [new CreateBpmnTaskCommand(document.DocumentId, document.Revision, new SemanticElementId("b-obstacle"),
                new VisualStateId("v:b-obstacle"), new PointD(260, 20), new SizeD(120, 80), "OBSTACLE", "Obstacle", 99,
                VisualPlacementMode.Pinned),
             new AssignOrganizationalElementCommand(document.DocumentId, document.Revision,
                new SemanticElementId("b-obstacle"), PoolB)]));
        Assert.True(added.IsCommitted, Describe(added.Diagnostics));
        var after = Assert.Single(document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        Assert.Equal([new VisualStateId("v:flow-b")], fixture.Policy.Searches);
        Assert.Equal(routeA, after.Connectors.Single(record => record.VisualStateId == routeA.VisualStateId));
        var repaired = after.Connectors.Single(record => record.VisualStateId == routeB.VisualStateId);
        Assert.False(CrossesBody(repaired.Path, obstacle));
        Assert.NotEqual(routeB.Path.AsEnumerable(), repaired.Path.AsEnumerable());
        Assert.Equal(new RectD(20, 20, 120, 80), after.Geometry.Nodes.Single(node => node.VisualStateId == new VisualStateId("v:a-source")).LocalBounds);
        Assert.Equal(new RectD(20, 20, 120, 80), after.Geometry.Nodes.Single(node => node.VisualStateId == new VisualStateId("v:b-source")).LocalBounds);
    }

    [Fact]
    public async Task HeightEditRepairsOnlyTheSavedOverflowPathReachedByANeighboringPoolBody()
    {
        var fixture = await CreateParallelPoolFixtureAsync();
        var width = await fixture.Processor.ExecuteAsync(fixture.Document, new SetOrganizationalScopeWidthCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, fixture.Document.CaptureSnapshot().SemanticModel.RootScopeId, 1000));
        Assert.True(width.IsCommitted, Describe(width.Diagnostics));
        await MoveNode(fixture, "b-source", new PointD(260, 20));
        await MoveNode(fixture, "b-target", new PointD(740, 20));
        var poolRegion = Regions(fixture.Document.CaptureSnapshot()).Single(region => region.ContainerSemanticElementId == PoolA);
        await CommitHeight(fixture, poolRegion.Id, 400);
        var snapshot = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(snapshot.VisualModel.RoutingScopes!.Value);
        var original = scope.Connectors.Single(record => record.VisualStateId == new VisualStateId("v:flow-a"));
        PointD[] detour = [new(250, 132), new(278, 132), new(278, 500), new(582, 500), new(582, 132), new(610, 132)];
        var candidate = new ConnectorRoutingRecord(original.VisualStateId, ConnectorRoutingType.Automatic,
            ConnectorRoutingOutcome.Path, detour, automaticProof: original.AutomaticProof);
        var graph = fixture.Configuration.ProjectionEngine.Project(snapshot, fixture.Configuration.ProjectionContext).Graph!;
        var local = ConnectorRoutingStatePreparer.RestoreLocalLayout(graph, scope.Geometry);
        var logical = ConnectorRoutingStatePreparer.CreateLogicalLayout(graph, local, scope.Geometry);
        var domain = new RoutingObstacleDomain(graph.Nodes.Select(static node => node.Id));
        var context = new RoutingContext(fixture.Configuration.RoutingContext.Options,
            new PreparedRoutingInput(graph, logical, graph.Edges.Select(edge => KeyValuePair.Create(edge.Id, domain)),
                new RoutingLogicalGeometry(scope.ScopeId, scope.Geometry, logical)));
        var assessment = fixture.Policy.Assess(graph, logical, context,
            graph.Edges.Single(edge => edge.Source.VisualStateId == original.VisualStateId).Id, candidate);
        Assert.True(assessment.IsValidAutomaticPath, Describe(assessment.Diagnostics));
        var certified = new ConnectorRoutingRecord(original.VisualStateId, ConnectorRoutingType.Automatic,
            ConnectorRoutingOutcome.Path, detour, automaticProof: assessment.AcceptedProof);
        var other = scope.Connectors.Single(record => record.VisualStateId != original.VisualStateId);
        var savedScope = new ScopeRoutingSnapshot(scope.ScopeId, scope.Geometry, [certified, other]);
        var seeded = new DocumentSnapshot(snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates,
                snapshot.VisualModel.ProfileElementPresentations, [savedScope]), snapshot.Metadata);
        var strict = await fixture.Preparer.PrepareAsync(new(seeded, seeded, [], [], null, false,
            ConnectorRoutingPreparationPurpose.ValidateSavedState), CancellationToken.None);
        Assert.True(strict.Succeeded, Describe(strict.Diagnostics));
        var reconstructed = DocumentReconstructor.Reconstruct(seeded, fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(reconstructed.Succeeded, Describe(reconstructed.Diagnostics));
        fixture = fixture with { Document = reconstructed.Document! };
        Assert.True(detour.Max(static point => point.Y) > 440);
        Assert.False(CrossesBody(detour, new RectD(370, 532, 120, 80)));
        Assert.True(CrossesBody(detour, new RectD(370, 482, 120, 80)));
        fixture.Policy.Searches.Clear();
        await CommitHeight(fixture, poolRegion.Id, 350);
        var after = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        Assert.Equal([original.VisualStateId], fixture.Policy.Searches);
        Assert.Equal([other.VisualStateId, original.VisualStateId], after.Connectors.Select(static record => record.VisualStateId));
        Assert.Equal(other.Path.Select(static point => new PointD(point.X, point.Y - 50)), after.Connectors[0].Path);
        Assert.False(CrossesBody(after.Connectors[1].Path, new RectD(370, 482, 120, 80)));
        Assert.NotEqual(detour, after.Connectors[1].Path.AsEnumerable());
        Assert.Equal(350, after.Geometry.Regions.Single(region => region.Id == poolRegion.Id).ExpandedHeight);
        Assert.Equal(224, after.Geometry.Regions.Single(region => region.ContainerSemanticElementId == PoolB).ExpandedHeight);
    }

    [Fact]
    public async Task DeletingLastPoolKeepsOnlyDormantUnassignedCapacityAndUndoRestoresTheSamePoolFrame()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var deleted = await fixture.Processor.ExecuteAsync(fixture.Document, new DeleteOrganizationalPoolCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, PoolA));
        Assert.True(deleted.IsCommitted, Describe(deleted.Diagnostics));
        var history = new HistoryManager(fixture.Document);
        var result = await history.ExecuteAsync(fixture.Processor, new DeleteOrganizationalPoolCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, PoolB));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var dormant = Assert.Single(Regions(fixture.Document.CaptureSnapshot()));
        Assert.Null(dormant.ContainerSemanticElementId);
        Assert.False(dormant.IsActive);
        Assert.Equal(374, dormant.ExpandedHeight);
        var undone = await history.UndoAsync(fixture.Processor);
        Assert.True(undone.Succeeded, Describe(undone.Diagnostics));
        var active = Regions(fixture.Document.CaptureSnapshot());
        Assert.Equal(2, active.Length);
        Assert.All(active, region => Assert.True(region.IsActive));
        Assert.Equal(374, active.Single(region => region.Id == dormant.Id).ExpandedHeight);
        Assert.Equal(224, active.Single(region => region.ContainerSemanticElementId == PoolB).ExpandedHeight);
    }

    private static async Task<StableRoutingPreparationTests.Fixture> CreateParallelPoolFixtureAsync()
    {
        var fixture = await CreateSpatialFixtureAsync();
        foreach (var name in new[] { "b-source", "b-target" })
            await MoveNode(fixture, name, new PointD(name == "b-source" ? 20 : 500, 20));
        foreach (var (name, pool) in new[] { ("a-target", PoolA), ("b-source", PoolB), ("b-target", PoolB) })
        {
            var assigned = await fixture.Processor.ExecuteAsync(fixture.Document, new AssignOrganizationalElementCommand(
                fixture.Document.DocumentId, fixture.Document.Revision, new SemanticElementId(name), pool));
            Assert.True(assigned.IsCommitted, Describe(assigned.Diagnostics));
        }
        return fixture;
    }

    private static async Task MoveNode(StableRoutingPreparationTests.Fixture fixture, string name, PointD position)
    {
        var moved = await fixture.Processor.ExecuteAsync(fixture.Document, new MoveVisualStateCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, new VisualStateId($"v:{name}"), position, VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted, Describe(moved.Diagnostics));
    }

    private static bool CrossesBody(IEnumerable<PointD> path, RectD body) => path.Zip(path.Skip(1)).Any(segment =>
        segment.First.X == segment.Second.X
            ? segment.First.X > body.Left && segment.First.X < body.Right &&
              Math.Max(segment.First.Y, segment.Second.Y) > body.Top && Math.Min(segment.First.Y, segment.Second.Y) < body.Bottom
            : segment.First.Y == segment.Second.Y && segment.First.Y > body.Top && segment.First.Y < body.Bottom &&
              Math.Max(segment.First.X, segment.Second.X) > body.Left && Math.Min(segment.First.X, segment.Second.X) < body.Right);
}
