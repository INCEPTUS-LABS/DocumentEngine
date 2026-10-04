using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class StableSpatialRoutingPreparationTests
{
    private static readonly SemanticElementId PoolA = new("pool-a");
    private static readonly SemanticElementId PoolB = new("pool-b");

    [Fact]
    public async Task ActualPreparationPreservesAuthoredCapacitiesAcrossEditsDormancyAndActivation()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var initial = fixture.Document.CaptureSnapshot();
        var initialScope = Assert.Single(initial.VisualModel.RoutingScopes!.Value);
        var poolA = initialScope.Geometry.Regions.Single(region => region.ContainerSemanticElementId == PoolA);
        var unassigned = initialScope.Geometry.Regions.Single(region => region.ContainerSemanticElementId is null);
        Assert.Equal(224d, poolA.ExpandedHeight);
        Assert.Equal(374d, unassigned.ExpandedHeight);

        await CommitHeight(fixture, poolA.Id, 300d);
        var expanded = fixture.Document.CaptureSnapshot();
        Assert.Equal(300d, Regions(expanded).Single(region => region.Id == poolA.Id).ExpandedHeight);
        Assert.Equal(380d, Regions(expanded).Single(region => region.ContainerSemanticElementId == PoolB).ContentBounds!.Value.Top);
        Assert.Equal(644d, Regions(expanded).Single(region => region.Id == unassigned.Id).ContentBounds!.Value.Top);
        var bytes = NativeDocumentSerializer.Export(expanded);
        var rejected = await fixture.Processor.ExecuteAsync(fixture.Document,
            new SetOrganizationalRegionExpandedHeightCommand(expanded.DocumentId, expanded.Revision,
                initialScope.ScopeId, unassigned.Id, 300d));
        Assert.False(rejected.IsCommitted);
        Assert.Equal(bytes.AsEnumerable(), NativeDocumentSerializer.Export(fixture.Document.CaptureSnapshot()).AsEnumerable());
        await CommitHeight(fixture, unassigned.Id, 342d);

        await SetAvailability(fixture, false);
        var dormant = fixture.Document.CaptureSnapshot();
        Assert.All(Regions(dormant), region => Assert.False(region.IsActive));
        Assert.All(Assert.Single(dormant.VisualModel.RoutingScopes!.Value).Geometry.Nodes,
            node => Assert.Null(node.RegionId));
        await CommitHeight(fixture, poolA.Id, 400d);
        await SetAvailability(fixture, true);
        var active = fixture.Document.CaptureSnapshot();
        Assert.All(Regions(active), region => Assert.True(region.IsActive));
        Assert.Equal(400d, Regions(active).Single(region => region.Id == poolA.Id).ExpandedHeight);
        Assert.Equal(342d, Regions(active).Single(region => region.Id == unassigned.Id).ExpandedHeight);
        Assert.Equal(480d, Regions(active).Single(region => region.ContainerSemanticElementId == PoolB).ContentBounds!.Value.Top);
        Assert.Equal(744d, Regions(active).Single(region => region.Id == unassigned.Id).ContentBounds!.Value.Top);
    }

    [Fact]
    public async Task ActualSavedCompactSceneHasIndependentNumericGeometryAndColdRestoreNeverSearches()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var document = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(document.VisualModel.RoutingScopes!.Value);
        var beforeBytes = NativeDocumentSerializer.Export(document);
        fixture.Policy.Searches.Clear();
        var graph = fixture.Configuration.ProjectionEngine.Project(document,
            fixture.Configuration.ProjectionContext).Graph!;
        var local = ConnectorRoutingStatePreparer.RestoreLocalLayout(graph, scope.Geometry);
        var routing = ConnectorRoutingStatePreparer.RestoreRouting(graph, local, scope,
            fixture.Configuration.RoutingAlgorithmId);
        var expanded = fixture.Configuration.SceneBuilder.Build(document, scope.ScopeId,
            ModelProfileViewStateSnapshot.Empty, graph, local, routing, document.VisualModel, EditorStateSnapshot.Empty);
        var collapsed = fixture.Configuration.SceneBuilder.Build(document, scope.ScopeId,
            ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(OrganizationalModelProfile.Id, PoolA, true),
            graph, local, routing, document.VisualModel, EditorStateSnapshot.Empty);
        Assert.True(expanded.Succeeded, Describe(expanded.Diagnostics));
        Assert.True(collapsed.Succeeded, Describe(collapsed.Diagnostics));
        var plan = collapsed.Scene!.SpatialPresentationPlan!;
        var poolA = plan.Regions.Single(region => region.ContainerSemanticElementId == PoolA);
        var poolB = plan.Regions.Single(region => region.ContainerSemanticElementId == PoolB);
        var unassigned = plan.Regions.Single(region => region.ContainerSemanticElementId is null);
        Assert.Equal(40d, poolA.Bounds.Top);
        Assert.Equal(25.6d, poolA.Bounds.Height, 10);
        Assert.Equal(105.6d, poolB.Bounds.Top, 10);
        Assert.Equal(369.6d, unassigned.Bounds.Top, 10);
        Assert.Equal(743.6d, unassigned.Bounds.Bottom, 10);
        var visibleNode = graph.Nodes.Single(node => node.Source.VisualStateId == new VisualStateId("v:b-source"));
        var visibleBody = collapsed.Scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(visibleNode.Id, "node"));
        Assert.Equal(130d, visibleBody.Bounds.Left);
        Assert.Equal(631.6d, visibleBody.Bounds.Top, 10);
        var crossPoolEdge = graph.Edges.Single(edge => edge.Source.VisualStateId == new VisualStateId("v:flow-a"));
        Assert.All(collapsed.Scene.Items.Where(item => item.Origin.ProjectedObjectId == crossPoolEdge.Id),
            item => Assert.False(item.IsVisible));
        var visibleEdge = graph.Edges.Single(edge => edge.Source.VisualStateId == new VisualStateId("v:flow-b"));
        var displayed = collapsed.Scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(visibleEdge.Id, "connector"));
        var saved = scope.Connectors.Single(record => record.VisualStateId == new VisualStateId("v:flow-b"));
        Assert.True(displayed.IsVisible);
        var path = Canvas2DConnectorPathMetadata.Resolve(displayed);
        Assert.Equal(saved.Path.Length, path.Length);
        for (var index = 0; index < path.Length; index++)
        {
            Assert.Equal(saved.Path[index].X, path[index].X);
            Assert.Equal(saved.Path[index].Y - 198.4d, path[index].Y, 10);
        }
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(beforeBytes.AsEnumerable(), NativeDocumentSerializer.Export(document).AsEnumerable());

        var imported = NativeDocumentSerializer.Import(beforeBytes.AsMemory(), fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, Describe(imported.Diagnostics));
        var restored = await fixture.Preparer.PrepareAsync(new(document, imported.Document!.CaptureSnapshot(),
            [], [], null, false, ConnectorRoutingPreparationPurpose.ValidateSavedState), CancellationToken.None);
        Assert.True(restored.Succeeded, Describe(restored.Diagnostics));
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(scope, Assert.Single(restored.RoutingScopes));
        var expandedAgain = fixture.Configuration.SceneBuilder.Build(document, scope.ScopeId,
            ModelProfileViewStateSnapshot.Empty, graph, local, routing, document.VisualModel, EditorStateSnapshot.Empty);
        Assert.Equal(expanded.Scene, expandedAgain.Scene);
    }

    private static async Task<StableRoutingPreparationTests.Fixture> CreateSpatialFixtureAsync(
        StableRoutingPreparationTests.Fixture? fixture = null)
    {
        fixture ??= await StableRoutingPreparationTests.Fixture.CreateAsync();
        await SetAvailability(fixture, true);
        foreach (var pool in new[] { PoolA, PoolB })
        {
            var document = fixture.Document;
            var result = await fixture.Processor.ExecuteAsync(document, new CreateOrganizationalPoolCommand(
                document.DocumentId, document.Revision, pool,
                document.CaptureSnapshot().SemanticModel.RootScopeId,
                pool == PoolA ? OrganizationalPoolCreationMode.AdoptEligibleUnassigned : OrganizationalPoolCreationMode.Empty,
                pool == PoolA ? "First Pool" : "Second Pool"));
            Assert.True(result.IsCommitted, Describe(result.Diagnostics));
        }
        Assert.Equal(374d, Regions(fixture.Document.CaptureSnapshot())
            .Single(region => region.ContainerSemanticElementId == PoolA).ExpandedHeight);
        await CommitHeight(fixture, Regions(fixture.Document.CaptureSnapshot())
            .Single(region => region.ContainerSemanticElementId is null).Id, 374d);
        foreach (var element in new[] { "b-source", "b-target" })
        {
            var document = fixture.Document;
            var result = await fixture.Processor.ExecuteAsync(document, new UnassignOrganizationalElementCommand(
                document.DocumentId, document.Revision, new SemanticElementId(element)));
            Assert.True(result.IsCommitted, Describe(result.Diagnostics));
        }
        var assigned = await fixture.Processor.ExecuteAsync(fixture.Document, new AssignOrganizationalElementCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, new SemanticElementId("a-target"), PoolB));
        Assert.True(assigned.IsCommitted, Describe(assigned.Diagnostics));
        await CommitHeight(fixture, Regions(fixture.Document.CaptureSnapshot())
            .Single(region => region.ContainerSemanticElementId == PoolA).Id, 224d);
        return fixture;
    }

    private static async Task SetAvailability(StableRoutingPreparationTests.Fixture fixture, bool available)
    {
        var document = fixture.Document;
        var result = await fixture.Processor.ExecuteAsync(document, new SetModelProfileAvailabilityCommand(
            document.DocumentId, document.Revision, [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, available)]));
        Assert.True(result.IsCommitted, Describe(result.Diagnostics));
    }

    private static async Task CommitHeight(StableRoutingPreparationTests.Fixture fixture,
        Canvas2DSpatialRegionId regionId, double height)
    {
        var document = fixture.Document;
        var result = await fixture.Processor.ExecuteAsync(document, new SetOrganizationalRegionExpandedHeightCommand(
            document.DocumentId, document.Revision, document.CaptureSnapshot().SemanticModel.RootScopeId, regionId, height));
        Assert.True(result.IsCommitted, Describe(result.Diagnostics));
    }

    private static ImmutableArray<SpatialRegionGeometrySnapshot> Regions(DocumentSnapshot document) =>
        document.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == document.SemanticModel.RootScopeId).Geometry.Regions;

    private static string Describe(IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
