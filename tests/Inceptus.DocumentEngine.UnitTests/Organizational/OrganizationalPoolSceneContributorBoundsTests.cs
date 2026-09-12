using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed partial class OrganizationalPoolSceneContributorBoundsTests
{
    private static readonly SemanticElementId PoolAId = new("test:pool-bounds:a");
    private static readonly SemanticElementId PoolBId = new("test:pool-bounds:b");

    [Fact]
    public void SamePoolManualRouteAndLabelExpandEqualPoolBoundsWithoutSelfGrowth()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute()
            .WithCrossingConnector();
        var originalSamePoolEdge = inputs.Graph.Edges.Single(edge =>
            edge.Source.SemanticElementId.Value == "test:semantic:ab");
        var crossPoolEdge = inputs.Graph.Edges.Single(edge =>
            edge.Id != originalSamePoolEdge.Id);
        var manualRoute = new[]
        {
            new PointD(-300d, -120d),
            new PointD(100d, -120d),
            new PointD(100d, 360d),
            new PointD(200d, 360d),
            new PointD(200d, 25d),
        };
        var samePoolEdge = CopyWithRoute(originalSamePoolEdge, manualRoute);
        var samePoolLabel = new ProjectedLabel(
            samePoolEdge.Source,
            samePoolEdge.Id,
            "Same-Pool connector label");
        var crossPoolLabel = new ProjectedLabel(
            crossPoolEdge.Source,
            crossPoolEdge.Id,
            "Cross-Pool connector label");
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges.Select(edge =>
                edge.Id == samePoolEdge.Id ? samePoolEdge : edge),
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [samePoolLabel, crossPoolLabel]);
        inputs = inputs.WithGraph(graph);

        var nodesById = graph.Nodes.ToDictionary(static node => node.Id);
        var sameSource = nodesById[samePoolEdge.SourceNodeId];
        var sameTarget = nodesById[samePoolEdge.TargetNodeId];
        var crossSource = nodesById[crossPoolEdge.SourceNodeId];
        var crossTarget = nodesById[crossPoolEdge.TargetNodeId];
        var nodeBounds = new Dictionary<ProjectedObjectId, RectD>
        {
            [sameSource.Id] = new RectD(0d, 0d, 100d, 50d),
            [sameTarget.Id] = new RectD(200d, 0d, 100d, 50d),
            [crossSource.Id] = new RectD(0d, 200d, 100d, 50d),
            [crossTarget.Id] = new RectD(200d, 200d, 100d, 50d),
        };
        var samePoolLabelBounds = new RectD(650d, 420d, 180d, 40d);
        var crossPoolRoute = new[]
        {
            new PointD(-5000d, -4000d),
            new PointD(5000d, 4000d),
        };
        var baseItems = graph.Nodes
            .Select(node => NodeItem(node, nodeBounds[node.Id]))
            .Concat(
            [
                ConnectorItem(samePoolEdge, manualRoute),
                LabelItem(samePoolLabel, samePoolLabelBounds),
                ConnectorItem(crossPoolEdge, crossPoolRoute),
                LabelItem(crossPoolLabel, new RectD(8000d, 8000d, 400d, 80d)),
            ])
            .ToArray();
        var document = CreateDocument(
            inputs,
            sameSource.Source.SemanticElementId,
            sameTarget.Source.SemanticElementId,
            crossSource.Source.SemanticElementId,
            crossTarget.Source.SemanticElementId);
        var registration = OrganizationalPoolSceneContributor.CreateRegistration(
            new OrganizationalElementEligibilityPolicy(static _ => true));
        var contributor = Assert.IsType<OrganizationalPoolSceneContributor>(
            registration.Contributor);
        var context = Context(inputs, graph, document, registration.Descriptor, baseItems);

        var first = contributor.Contribute(context);

        Assert.True(first.Succeeded);
        var contribution = Assert.IsType<Canvas2DSceneContribution>(first.Contribution);
        var plan = Assert.IsType<Canvas2DSpatialPresentationPlan>(
            contribution.SpatialPresentationPlan);
        var poolARegion = plan.Regions.Single(region =>
            region.ContainerSemanticElementId == PoolAId);
        var poolBRegion = plan.Regions.Single(region =>
            region.ContainerSemanticElementId == PoolBId);
        var canonicalOwnedBounds = new RectD(-300d, -120d, 1130d, 580d);

        Assert.Equal(
            canonicalOwnedBounds.Right + 64d,
            poolARegion.Bounds.Width);
        Assert.Equal(
            canonicalOwnedBounds.Bottom + 64d,
            poolARegion.Bounds.Height);
        Assert.Equal(poolARegion.Bounds.Width, poolBRegion.Bounds.Width);
        var poolAFrame = PoolBackground(contribution, PoolAId).Bounds;
        Assert.Equal(canonicalOwnedBounds.Width + 64d + 38d, poolAFrame.Width);
        Assert.Equal(canonicalOwnedBounds.Height + 64d, poolAFrame.Height);
        Assert.True(poolAFrame.Contains(poolARegion.MapLocalToScene(
            Canvas2DSceneGeometry.Path(manualRoute).Bounds)));
        Assert.True(poolAFrame.Contains(poolARegion.MapLocalToScene(
            samePoolLabelBounds)));
        Assert.Equal(new PointD(110d, 72d), poolARegion.MapLocalToScene(new PointD(0d, 0d)));
        Assert.True(poolARegion.Bounds.Bottom < poolBRegion.Bounds.Top);
        Assert.Equal(
            PoolBackground(contribution, PoolAId).Bounds.Width,
            PoolBackground(contribution, PoolBId).Bounds.Width);

        var secondContext = Context(
            inputs,
            graph,
            document,
            registration.Descriptor,
            baseItems.Concat(contribution.Items));
        var second = contributor.Contribute(secondContext);

        Assert.Equal(first, second);

        // Even changing signed decoration must not move the canonical origin or expand an
        // interaction band over another row. Only the non-hittable enclosure grows.
        var extendedRoute = manualRoute.Prepend(new PointD(-600d, -220d)).ToArray();
        var extendedConnector = ConnectorItem(samePoolEdge, extendedRoute);
        var expanded = contributor.Contribute(Context(inputs, graph, document, registration.Descriptor,
            baseItems.Select(item => item.Id == extendedConnector.Id ? extendedConnector : item)));
        Assert.True(expanded.Succeeded);
        var expandedContribution = Assert.IsType<Canvas2DSceneContribution>(expanded.Contribution);
        var expandedPlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(expandedContribution.SpatialPresentationPlan);
        foreach (var region in plan.Regions)
        {
            Assert.Equal(region, expandedPlan.Regions.Single(candidate => candidate.Id == region.Id));
        }
        var expandedFrame = PoolBackground(expandedContribution, PoolAId).Bounds;
        Assert.True(expandedFrame.Contains(poolARegion.MapLocalToScene(
            Canvas2DSceneGeometry.Path(extendedRoute).Bounds)));
        Assert.Equal(poolAFrame.Width + 300d, expandedFrame.Width);
        Assert.Equal(poolAFrame.Height + 100d, expandedFrame.Height);
        Assert.Equal(expandedFrame.Width, PoolBackground(expandedContribution, PoolBId).Bounds.Width);
    }

    private static ProjectedEdge CopyWithRoute(
        ProjectedEdge source,
        IEnumerable<PointD> route) =>
        new(
            source.Source,
            source.SourceNodeId,
            source.TargetNodeId,
            source.SourcePortId,
            source.TargetPortId,
            route,
            source.SemanticProperties,
            source.ProjectedProperties,
            source.LayoutHints,
            source.RoutingHints,
            source.AlgorithmMetadata);

    private static DocumentSnapshot CreateDocument(
        Canvas2DSceneTestData inputs,
        SemanticElementId sameSourceId,
        SemanticElementId sameTargetId,
        SemanticElementId crossSourceId,
        SemanticElementId crossTargetId)
    {
        var source = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                source.DocumentId,
                source.Revision,
                source.SemanticModel.Elements.Concat(
                [
                    OrganizationalSemanticFactory.CreatePool(PoolAId, "Pool A"),
                    OrganizationalSemanticFactory.CreatePool(PoolBId, "Pool B"),
                ]),
                source.SemanticModel.Relationships,
                source.SemanticModel.NestedScopes,
                source.SemanticModel.ScopeMemberships,
                new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
                [
                    Assignment(sameSourceId, PoolAId),
                    Assignment(sameTargetId, PoolAId),
                    Assignment(crossSourceId, PoolAId),
                    Assignment(crossTargetId, PoolBId),
                ]),
            new VisualModelSnapshot(
                source.DocumentId,
                source.Revision,
                source.VisualModel.VisualStates,
                [
                    new ModelProfileElementPresentationSnapshot(
                        OrganizationalModelProfile.Id,
                        PoolAId,
                        0),
                    new ModelProfileElementPresentationSnapshot(
                        OrganizationalModelProfile.Id,
                        PoolBId,
                        1),
                ]),
            new DocumentMetadataSnapshot(source.DocumentId, source.Revision));
    }

    private static ModelProfileElementAssignmentSnapshot Assignment(
        SemanticElementId elementId,
        SemanticElementId poolId) =>
        new(OrganizationalModelProfile.Id, elementId, poolId);

    private static Canvas2DSceneContributionContext Context(
        Canvas2DSceneTestData inputs,
        ProjectedGraph graph,
        DocumentSnapshot document,
        Canvas2DSceneContributorDescriptor descriptor,
        IEnumerable<Canvas2DSceneItem> baseItems) =>
        new(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty,
            Canvas2DSceneConfiguration.Default,
            descriptor,
            new Canvas2DScenePresentationContext(
                document,
                document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty,
                baseItems));

    private static Canvas2DSceneItem NodeItem(ProjectedNode node, RectD bounds) =>
        new(
            Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"),
            Canvas2DSceneLayer.Content,
            0,
            Canvas2DSceneGeometry.Rectangle(bounds),
            Origin(node.Source, node.Id));

    private static Canvas2DSceneItem ConnectorItem(
        ProjectedEdge edge,
        IEnumerable<PointD> path) =>
        new(
            Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"),
            Canvas2DSceneLayer.Connector,
            0,
            Canvas2DSceneGeometry.Path(path),
            Origin(edge.Source, edge.Id));

    private static Canvas2DSceneItem LabelItem(ProjectedLabel label, RectD bounds) =>
        new(
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"),
            Canvas2DSceneLayer.Label,
            0,
            Canvas2DSceneGeometry.Text(bounds, label.Text),
            Origin(label.Source, label.Id));

    private static Canvas2DSceneOriginTrace Origin(
        ProjectionSourceTrace source,
        ProjectedObjectId projectedObjectId) =>
        new(
            Canvas2DSceneOriginCategory.SemanticElement |
            Canvas2DSceneOriginCategory.VisualState |
            Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
            source.SemanticElementId,
            source.VisualStateId,
            projectedObjectId);

    private static Canvas2DSceneItem PoolBackground(
        Canvas2DSceneContribution contribution,
        SemanticElementId poolId) =>
        Assert.Single(contribution.Items, item =>
            item.Origin.SemanticElementId == poolId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None);
}
