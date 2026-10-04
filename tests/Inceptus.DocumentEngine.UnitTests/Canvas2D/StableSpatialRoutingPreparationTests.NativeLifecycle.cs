using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class StableSpatialRoutingPreparationTests
{
    [Fact]
    public async Task AllScopesKeepExactAuthoredHeightsAndPathsThroughDormantNativeReopenAndCapacityRepair()
    {
        var peerId = new DocumentScopeId("stable:inactive-peer");
        var peerPool = new SemanticElementId("peer-pool");
        var fixture = await CreateSpatialFixtureAsync(await StableRoutingPreparationTests.Fixture.CreateAsync(
            transformSource: source => WithPeerScope(source, peerId)));
        var created = await fixture.Processor.ExecuteAsync(fixture.Document, new CreateOrganizationalPoolCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, peerPool, peerId,
            OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Inactive peer Pool"));
        Assert.True(created.IsCommitted, Describe(created.Diagnostics));
        var rootId = fixture.Document.CaptureSnapshot().SemanticModel.RootScopeId;
        var expected = new[] { (rootId, PoolA, 300.25), (rootId, (SemanticElementId?)null, 390.75),
            (peerId, peerPool, 280.5), (peerId, (SemanticElementId?)null, 264.125) };
        foreach (var (scopeId, container, height) in expected)
            await CommitScopedHeight(scopeId, container, height);
        Assert.Equal(2, fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Length);
        fixture = await AssertColdRoundTrip(fixture);

        await SetAvailability(fixture, false);
        await CommitScopedHeight(rootId, PoolA, 310.125);
        await CommitScopedHeight(peerId, peerPool, 290.875);
        Assert.All(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value,
            scope => Assert.All(scope.Geometry.Regions, region =>
            {
                Assert.Null(region.ContentBounds);
                Assert.Null(region.LocalToScopeTransform);
            }));
        fixture = await AssertColdRoundTrip(fixture);
        await SetAvailability(fixture, true);
        AssertHeight(rootId, PoolA, 310.125);
        AssertHeight(peerId, peerPool, 290.875);
        AssertHeight(rootId, null, 390.75);
        AssertHeight(peerId, null, 264.125);

        await SetAvailability(fixture, false);
        await MoveNode(fixture, "a-source", new PointD(20, 500));
        fixture = await AssertColdRoundTrip(fixture);
        var before = fixture.Document.CaptureSnapshot();
        var rejected = await fixture.Processor.ExecuteAsync(fixture.Document,
            new Inceptus.DocumentEngine.Contracts.Commands.SetModelProfileAvailabilityCommand(before.DocumentId,
                before.Revision, [new(OrganizationalModelProfile.Id, true)]));
        Assert.False(rejected.IsCommitted);
        Assert.Equal(before, fixture.Document.CaptureSnapshot());
        await CommitScopedHeight(rootId, PoolA, 620.25);
        await SetAvailability(fixture, true);
        AssertHeight(rootId, PoolA, 620.25);
        AssertHeight(peerId, peerPool, 290.875);
        fixture = await AssertColdRoundTrip(fixture);
        Assert.All(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value,
            scope => Assert.All(scope.Geometry.Regions, region => Assert.True(region.IsActive)));

        async Task CommitScopedHeight(DocumentScopeId scopeId, SemanticElementId? container, double height)
        {
            var snapshot = fixture.Document.CaptureSnapshot();
            var region = snapshot.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == scopeId)
                .Geometry.Regions.Single(region => region.ContainerSemanticElementId == container);
            var result = await fixture.Processor.ExecuteAsync(fixture.Document,
                new SetOrganizationalRegionExpandedHeightCommand(snapshot.DocumentId, snapshot.Revision,
                    scopeId, region.Id, height));
            Assert.True(result.IsCommitted, Describe(result.Diagnostics));
            AssertHeight(scopeId, container, height);
        }

        void AssertHeight(DocumentScopeId scopeId, SemanticElementId? container, double height) =>
            Assert.Equal(height, fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value
                .Single(scope => scope.ScopeId == scopeId).Geometry.Regions
                .Single(region => region.ContainerSemanticElementId == container).ExpandedHeight);
    }

    private static async Task<StableRoutingPreparationTests.Fixture> AssertColdRoundTrip(
        StableRoutingPreparationTests.Fixture fixture)
    {
        var before = fixture.Document.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(before);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory(), fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, Describe(imported.Diagnostics));
        var after = imported.Document!.CaptureSnapshot();
        var coldPreparer = new ConnectorRoutingStatePreparer(fixture.Configuration,
            new Canvas2DTextLayoutServiceTests.FixedAdvanceTextMetricsService(),
            StableRoutingPreparationTests.Fixture.CreateRequest);
        fixture.Policy.Searches.Clear();
        var validated = await coldPreparer.PrepareAsync(new(after, after, [], [], null, false,
            ConnectorRoutingPreparationPurpose.ValidateSavedState), CancellationToken.None);
        Assert.True(validated.Succeeded, Describe(validated.Diagnostics));
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), validated.RoutingScopes.AsEnumerable());
        Assert.Equal(before, after);
        Assert.Equal(bytes.AsEnumerable(), NativeDocumentSerializer.Export(after).AsEnumerable());
        return fixture with { Document = imported.Document, Preparer = coldPreparer };
    }

    private static DocumentSnapshot WithPeerScope(DocumentSnapshot source, DocumentScopeId peerId)
    {
        var names = new[] { "peer-source", "peer-target" };
        var elements = names.Select((name, index) => BpmnSemanticFactory.CreateTask(new(name), name, name, index + 10));
        var flow = new SemanticRelationshipSnapshot(new("peer-flow"), BpmnSemanticTypes.SequenceFlow,
            new(names[0]), new(names[1]));
        var visuals = names.Select((name, index) => new VisualStateSnapshot(new($"v:{name}"), new(name),
            new PointD(index == 0 ? 20 : 500, 20), new SizeD(120, 80), VisualPlacementMode.Pinned,
            connectorAnchors: [new(new($"{name}:source"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                new(new($"{name}:target"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]))
            .Append(new VisualStateSnapshot(new("v:peer-flow"), flow.Id, default, default,
                VisualPlacementMode.Automatic, sourceAnchorId: new("peer-source:source"), targetAnchorId: new("peer-target:target")));
        return new DocumentSnapshot(new SemanticModelSnapshot(source.DocumentId, source.Revision,
            source.SemanticModel.Elements.Concat(elements), source.SemanticModel.Relationships.Append(flow),
            [new DocumentScopeSnapshot(peerId)], names.Select(name =>
                new SemanticElementScopeMembershipSnapshot(new(name), peerId))),
            new VisualModelSnapshot(source.DocumentId, source.Revision, source.VisualModel.VisualStates.Concat(visuals)), source.Metadata);
    }
}
