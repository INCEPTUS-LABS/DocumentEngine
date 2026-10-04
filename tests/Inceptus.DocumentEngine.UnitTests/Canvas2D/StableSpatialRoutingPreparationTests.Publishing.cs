using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class StableSpatialRoutingPreparationTests
{
    [Fact]
    public async Task PublishedCurrentCompactGeometryKeepsNodesAndPathsTogetherAndRejectsHiddenContent()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var emptyPool = new SemanticElementId("pool-empty");
        var created = await fixture.Processor.ExecuteAsync(fixture.Document, new CreateOrganizationalPoolCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, emptyPool,
            fixture.Document.CaptureSnapshot().SemanticModel.RootScopeId, OrganizationalPoolCreationMode.Empty, "Empty Pool"));
        Assert.True(created.IsCommitted, Describe(created.Diagnostics));
        var publication = await fixture.Processor.ExecuteAsync(fixture.Document, new UpdateDocumentPublicationCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, "spatial", "Spatial publication", string.Empty));
        Assert.True(publication.IsCommitted, Describe(publication.Diagnostics));
        var document = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(document.VisualModel.RoutingScopes!.Value);
        var before = NativeDocumentSerializer.Export(document);
        fixture.Policy.Searches.Clear();
        var builder = new PublishedProcessPackageBuilder(new TwoBranchesTokenClassifier());
        var expanded = CaptureSpatialPresentation(fixture, ModelProfileElementViewStateSnapshot.Empty);
        var expandedPackage = builder.Build(expanded);
        Assert.True(expandedPackage.Succeeded, Describe(expandedPackage.Diagnostics));
        foreach (var edge in expanded.ProjectedGraph.Edges)
        {
            var route = scope.Connectors.Single(record => record.VisualStateId == edge.Source.VisualStateId);
            var published = expandedPackage.Package!.Snapshot.Presentation.Connectors.Single(
                connector => connector.Id == edge.Source.SemanticElementId.Value);
            Assert.Equal(route.Path.Select(static point => new PublishedPoint(point.X, point.Y)), published.Points);
        }

        var compact = CaptureSpatialPresentation(fixture, ModelProfileElementViewStateSnapshot.Empty
            .WithCollapsed(OrganizationalModelProfile.Id, emptyPool, true));
        var result = builder.Build(compact);
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var package = result.Package!;
        Assert.Equal(1, package.Snapshot.FormatVersion);
        Assert.False(compact.Scene.SpatialPresentationPlan!.CoordinateMap.IsIdentity);
        foreach (var edge in compact.ProjectedGraph.Edges)
        {
            var saved = scope.Connectors.Single(record => record.VisualStateId == edge.Source.VisualStateId);
            var displayed = compact.Scene.SpatialPresentationPlan.CoordinateMap.MapPath(saved.Path);
            var published = package.Snapshot.Presentation.Connectors.Single(
                connector => connector.Id == edge.Source.SemanticElementId.Value);
            Assert.Equal(displayed.Select(static point => new PublishedPoint(point.X, point.Y)), published.Points);
        }
        foreach (var node in compact.ProjectedGraph.Nodes)
        {
            var body = compact.Scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"));
            var published = package.Snapshot.Presentation.Nodes.Single(item => item.Id == node.Source.SemanticElementId.Value);
            Assert.Equal(new PublishedRect(body.Bounds.X, body.Bounds.Y, body.Bounds.Width, body.Bounds.Height), published.Bounds);
        }
        var unassigned = package.Snapshot.Presentation.Nodes.Single(item => item.Id == "b-source");
        var expandedUnassigned = expandedPackage.Package!.Snapshot.Presentation.Nodes.Single(item => item.Id == "b-source");
        Assert.Equal(expandedUnassigned.Bounds.Y - 198.4d, unassigned.Bounds.Y, 10);

        var hidden = CaptureSpatialPresentation(fixture, ModelProfileElementViewStateSnapshot.Empty
            .WithCollapsed(OrganizationalModelProfile.Id, PoolA, true));
        var rejected = builder.Build(hidden);
        Assert.False(rejected.Succeeded);
        Assert.Null(rejected.Package);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "PUBLISH_CONNECTOR_ROUTE_MISSING");
        var hiddenEdge = hidden.ProjectedGraph.Edges.Single(edge => edge.Source.SemanticElementId == new SemanticElementId("flow-a"));
        Assert.All(hidden.Scene.Items.Where(item => item.Origin.ProjectedObjectId == hiddenEdge.Id),
            item => Assert.False(item.IsVisible));
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(before.AsEnumerable(), NativeDocumentSerializer.Export(fixture.Document.CaptureSnapshot()).AsEnumerable());
    }

    private static EditingSessionPresentationCapture CaptureSpatialPresentation(
        StableRoutingPreparationTests.Fixture fixture, ModelProfileElementViewStateSnapshot view)
    {
        var document = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(document.VisualModel.RoutingScopes!.Value);
        var graph = fixture.Configuration.ProjectionEngine.Project(document, fixture.Configuration.ProjectionContext).Graph!;
        var local = ConnectorRoutingStatePreparer.RestoreLocalLayout(graph, scope.Geometry);
        var routing = ConnectorRoutingStatePreparer.RestoreRouting(graph, local, scope, fixture.Configuration.RoutingAlgorithmId);
        var scene = fixture.Configuration.SceneBuilder.Build(document, scope.ScopeId,
            ModelProfileViewStateSnapshot.Empty, view, graph, local, routing, document.VisualModel, EditorStateSnapshot.Empty);
        Assert.True(scene.Succeeded, Describe(scene.Diagnostics));
        return new EditingSessionPresentationCapture(document, scope.ScopeId, new EditingSessionGeneration(0),
            graph, local, routing, scene.Scene!);
    }

    private sealed class TwoBranchesTokenClassifier : IPublishedTokenRoleClassifier
    {
        public PublishedTokenRoleClassification Classify(PublishedTokenRoleClassificationRequest request) =>
            PublishedTokenRoleClassification.Success(request.SemanticElementId.Value.EndsWith("-source", StringComparison.Ordinal)
                ? PublishedTokenRole.Start : PublishedTokenRole.End);
    }
}
