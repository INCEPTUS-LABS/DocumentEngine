using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class NeutralDemoConnectorProjectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceConnectorNameRemainsSemanticAndProducesNoLabel(string name)
    {
        var (composition, snapshot, relationship) = CaptureBetaGamma();
        var document = WithName(snapshot, relationship, name);

        var result = composition.Configuration.ProjectionEngine.Project(document);

        Assert.True(result.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(result.Graph);
        var edge = graph.Edges.Single(item =>
            item.Source.SemanticElementId == relationship.Id);
        Assert.Equal(name, edge.SemanticProperties[NeutralDemoPipeline.LabelPropertyKey].TextValue);
        Assert.DoesNotContain(graph.Labels, label => label.OwnerId == edge.Id);
    }

    [Fact]
    public void NonEmptyConnectorNameProducesOneDerivedOwnerTracedLabel()
    {
        var (composition, snapshot, relationship) = CaptureBetaGamma();
        var document = WithName(snapshot, relationship, "Approved");

        var result = composition.Configuration.ProjectionEngine.Project(document);

        Assert.True(result.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(result.Graph);
        var edge = graph.Edges.Single(item =>
            item.Source.SemanticElementId == relationship.Id);
        var label = Assert.Single(graph.Labels, item => item.OwnerId == edge.Id);
        Assert.Equal("Approved", label.Text);
        Assert.Equal(relationship.Id, label.Source.SemanticElementId);
        Assert.Equal(edge.Source.VisualStateId, label.Source.VisualStateId);
        Assert.Equal(
            relationship.Properties[NeutralDemoPipeline.DescriptionPropertyKey],
            label.SemanticProperties[NeutralDemoPipeline.DescriptionPropertyKey]);
    }

    [Fact]
    public void DefaultDemoExposesOneDeclarativeMixedPolicyNodeWithoutPersistentAnchorCopies()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var snapshot = composition.Document.CaptureSnapshot();
        var gamma = snapshot.SemanticModel.Elements.Single(element =>
            element.Id.Value == "demo:gamma");
        var gammaVisual = snapshot.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId == gamma.Id);
        var provider = composition.Configuration.ConnectorAnchorPolicyProvider;
        var policy = provider.Resolve(gamma.TypeId);

        Assert.Same(NeutralDemoPipeline.ConnectorAnchorPolicyProvider, provider);
        Assert.Equal(NeutralDemoPipeline.NeutralMixedPolicyNodeTypeId, gamma.TypeId);
        Assert.Equal(ConnectorAnchorPolicyMode.Disabled, policy.Top.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.Predefined, policy.Right.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.DynamicSingle, policy.Bottom.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, policy.Left.Mode);
        Assert.Empty(gammaVisual.ConnectorAnchors);
        Assert.Collection(
            policy.Right.PredefinedAnchors,
            anchor => Assert.Equal(ConnectorAnchorRoleCapability.Source, anchor.RoleCapability),
            anchor => Assert.Equal(ConnectorAnchorRoleCapability.Target, anchor.RoleCapability));

        var (graph, layout, routing) = RunPipeline(composition, snapshot);
        var gammaNode = graph.Nodes.Single(node => node.Source.SemanticElementId == gamma.Id);
        var anchors = graph.Ports
            .Where(port => port.OwnerNodeId == gammaNode.Id)
            .Select(port =>
            {
                Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor));
                return Assert.IsType<ProjectedConnectorAnchor>(anchor);
            })
            .OrderBy(static anchor => anchor.Order)
            .ToArray();
        Assert.Collection(
            anchors,
            anchor => AssertResolved(anchor, ConnectorAnchorRoleCapability.Source, 0),
            anchor => AssertResolved(anchor, ConnectorAnchorRoleCapability.Target, 1));

        var scene = Assert.IsType<Canvas2DScene>(composition.Configuration.SceneBuilder.Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            new EditorStateSnapshot(selection: [gammaVisual.Id])).Scene);
        var handles = scene.Items.Where(item =>
            item.Origin.VisualStateId == gammaVisual.Id &&
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorKind, out var kind) &&
            kind.Kind == PropertyValueKind.Integer &&
            kind.IntegerValue == (long)ResolvedConnectorAnchorKind.Predefined).ToArray();
        Assert.Equal(2, handles.Length);

        static void AssertResolved(
            ProjectedConnectorAnchor anchor,
            ConnectorAnchorRoleCapability roleCapability,
            int order)
        {
            Assert.Equal(ResolvedConnectorAnchorKind.Predefined, anchor.Kind);
            Assert.Equal(ConnectorAnchorSide.Right, anchor.Side);
            Assert.Equal(roleCapability, anchor.RoleCapability);
            Assert.Equal(order, anchor.Order);
            Assert.Equal(2, anchor.SideCount);
        }
    }

    [Fact]
    public void ConnectorAnchorReferencesProjectAsStablePortsAndResolveFromCurrentBounds()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var original = composition.Document.CaptureSnapshot();
        var sourceAnchor = new ConnectorAnchor(
            new ConnectorAnchorId("test:anchor:alpha-source"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        var targetAnchor = new ConnectorAnchor(
            new ConnectorAnchorId("test:anchor:beta-target"),
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        var anchored = WithAlphaBetaAnchors(original, [sourceAnchor], [targetAnchor]);

        var (graph, layout, routing) = RunPipeline(composition, anchored);
        var edge = graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId.Value == "demo:alpha-beta");
        var route = routing.Routes.Single(candidate => candidate.ProjectedEdgeId == edge.Id);

        Assert.Equal(2, graph.Ports.Count(port =>
            ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) &&
            anchor?.Kind == ResolvedConnectorAnchorKind.Dynamic));
        Assert.Equal(2, graph.Ports.Count(port =>
            ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) &&
            anchor?.Kind == ResolvedConnectorAnchorKind.Predefined));
        Assert.NotNull(edge.SourcePortId);
        Assert.NotNull(edge.TargetPortId);
        Assert.Equal(edge.SourcePortId, route.SourcePortId);
        Assert.Equal(edge.TargetPortId, route.TargetPortId);
        Assert.Equal(new PointD(210d, 105d), route.SourceAnchor);
        Assert.Equal(new PointD(300d, 230d), route.DestinationAnchor);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                layout.Nodes.Single(node => node.ProjectedObjectId == edge.SourceNodeId).Bounds,
                ConnectorAnchorSide.Right,
                0,
                1),
            route.SourceAnchor);
    }

    [Fact]
    public void RedistributionMovesReferencedEndpointWithoutReplacingPortIdentityOrSemantics()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var original = composition.Document.CaptureSnapshot();
        var referencedId = new ConnectorAnchorId("test:anchor:alpha-source");
        var targetId = new ConnectorAnchorId("test:anchor:beta-target");
        var before = WithAlphaBetaAnchors(
            original,
            [new ConnectorAnchor(referencedId, ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source, 0)],
            [new ConnectorAnchor(targetId, ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target, 0)]);
        var after = WithAlphaBetaAnchors(
            original,
            [
                new ConnectorAnchor(new ConnectorAnchorId("test:anchor:inserted"),
                    ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 0),
                new ConnectorAnchor(referencedId, ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 1),
            ],
            [new ConnectorAnchor(targetId, ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target, 0)]);

        var (beforeGraph, _, beforeRouting) = RunPipeline(composition, before);
        var (afterGraph, _, afterRouting) = RunPipeline(composition, after);
        var beforeEdge = beforeGraph.Edges.Single(edge =>
            edge.Source.SemanticElementId.Value == "demo:alpha-beta");
        var afterEdge = afterGraph.Edges.Single(edge =>
            edge.Source.SemanticElementId.Value == "demo:alpha-beta");
        var beforeRoute = beforeRouting.Routes.Single(route =>
            route.ProjectedEdgeId == beforeEdge.Id);
        var afterRoute = afterRouting.Routes.Single(route =>
            route.ProjectedEdgeId == afterEdge.Id);

        Assert.Equal(beforeEdge.SourcePortId, afterEdge.SourcePortId);
        Assert.Equal(referencedId, after.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "demo:alpha-beta").SourceAnchorId);
        Assert.Equal(new PointD(210d, 105d), beforeRoute.SourceAnchor);
        Assert.Equal(new PointD(210d, 70d + ((70d * 2d) / 3d)), afterRoute.SourceAnchor);
        Assert.Equal(before.SemanticModel, after.SemanticModel);
    }

    [Fact]
    public void ConnectorsWithoutAnchorReferencesKeepExistingMidpointEndpointResolution()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var snapshot = composition.Document.CaptureSnapshot();

        var (graph, _, routing) = RunPipeline(composition, snapshot);
        var edge = graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId.Value == "demo:alpha-beta");
        var route = routing.Routes.Single(candidate => candidate.ProjectedEdgeId == edge.Id);

        Assert.Null(edge.SourcePortId);
        Assert.Null(edge.TargetPortId);
        Assert.Equal(new PointD(210d, 105d), route.SourceAnchor);
        Assert.Equal(new PointD(300d, 230d), route.DestinationAnchor);
    }

    [Fact]
    public void PredefinedReferencesUseSamePortsGeometryAndArrowWithoutPersistentAnchorCopies()
    {
        var rightFirst = new PredefinedConnectorAnchorDefinitionId("right:p1");
        var rightMiddle = new PredefinedConnectorAnchorDefinitionId("right:p2");
        var rightLast = new PredefinedConnectorAnchorDefinitionId("right:p3");
        var leftMiddle = new PredefinedConnectorAnchorDefinitionId("left:p1");
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined(
            [
                new(rightFirst, ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Source, 0),
                new(rightMiddle, ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.SourceOrTarget, 1),
                new(rightLast, ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Target, 2),
            ]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined(
            [
                new(leftMiddle, ConnectorAnchorSide.Left,
                    ConnectorAnchorRoleCapability.Target, 0),
            ]));
        var provider = new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(
                NeutralDemoPipeline.NeutralNodeTypeId,
                policy),
        ]);
        var composition = NeutralDemoPipeline.CreateComposition(provider);
        var original = composition.Document.CaptureSnapshot();
        var alphaVisual = original.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "demo:alpha");
        var betaVisual = original.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "demo:beta");
        var sourceAnchorId = ConnectorAnchorReferenceIdentity.ForPredefined(
            alphaVisual.Id,
            rightMiddle);
        var targetAnchorId = ConnectorAnchorReferenceIdentity.ForPredefined(
            betaVisual.Id,
            leftMiddle);
        var anchored = WithAlphaBetaReferences(
            original,
            sourceAnchorId,
            targetAnchorId);

        var (graph, layout, routing) = RunPipeline(composition, anchored);
        var edge = graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId.Value == "demo:alpha-beta");
        var route = routing.Routes.Single(candidate => candidate.ProjectedEdgeId == edge.Id);
        var sourcePort = graph.Ports.Single(port =>
            ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) &&
            anchor!.Id == sourceAnchorId);
        var targetPort = graph.Ports.Single(port =>
            ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) &&
            anchor!.Id == targetAnchorId);
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(sourcePort, out var sourceAnchor));
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(targetPort, out var targetAnchor));

        Assert.Equal(ResolvedConnectorAnchorKind.Predefined, sourceAnchor!.Kind);
        Assert.True(sourceAnchor.Allows(ConnectorAnchorRole.Source));
        Assert.True(sourceAnchor.Allows(ConnectorAnchorRole.Target));
        Assert.Equal(ResolvedConnectorAnchorKind.Predefined, targetAnchor!.Kind);
        Assert.False(targetAnchor.Allows(ConnectorAnchorRole.Source));
        Assert.True(targetAnchor.Allows(ConnectorAnchorRole.Target));
        Assert.Equal(edge.SourcePortId, route.SourcePortId);
        Assert.Equal(edge.TargetPortId, route.TargetPortId);
        Assert.Equal(new PointD(210d, 105d), route.SourceAnchor);
        Assert.Equal(new PointD(300d, 230d), route.DestinationAnchor);
        Assert.Empty(anchored.VisualModel.VisualStates.SelectMany(static visual =>
            visual.ConnectorAnchors));

        var repeated = RunPipeline(composition, anchored);
        Assert.Equal(
            graph.Ports.Select(static port => port.Id),
            repeated.Graph.Ports.Select(static port => port.Id));
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            layout,
            routing,
            anchored.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var arrow = Assert.Single(scene.Items, item =>
            item.Origin.ProjectedObjectId == edge.Id &&
            item.Metadata.TryGetValue(
                Canvas2DConnectorArrowMetadata.TargetArrow,
                out var marker) &&
            marker.Kind == PropertyValueKind.Boolean &&
            marker.BooleanValue);
        Assert.Equal(route.DestinationAnchor, arrow.Geometry.Points[0]);

        var resized = WithNodeBounds(
            WithNodeBounds(
                anchored,
                "demo:alpha",
                new PointD(60d, 70d),
                new SizeD(190d, 110d)),
            "demo:beta",
            new PointD(300d, 190d),
            new SizeD(200d, 120d));
        var (resizedGraph, _, resizedRouting) = RunPipeline(composition, resized);
        var resizedEdge = resizedGraph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId.Value == "demo:alpha-beta");
        var resizedRoute = resizedRouting.Routes.Single(candidate =>
            candidate.ProjectedEdgeId == resizedEdge.Id);
        Assert.Equal(new PointD(250d, 125d), resizedRoute.SourceAnchor);
        Assert.Equal(new PointD(300d, 250d), resizedRoute.DestinationAnchor);
        Assert.Equal(sourceAnchorId, resized.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "demo:alpha-beta").SourceAnchorId);
        Assert.Equal(targetAnchorId, resized.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "demo:alpha-beta").TargetAnchorId);
        Assert.Empty(resized.VisualModel.VisualStates.SelectMany(static visual =>
            visual.ConnectorAnchors));
    }

    [Fact]
    public void ReferencedEndpointsAndArrowFollowMovedNodeBounds()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var original = composition.Document.CaptureSnapshot();
        var anchored = WithAlphaBetaAnchors(
            original,
            [new ConnectorAnchor(new ConnectorAnchorId("test:anchor:alpha-source"),
                ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0)],
            [new ConnectorAnchor(new ConnectorAnchorId("test:anchor:beta-target"),
                ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]);
        var moved = WithNodeBounds(
            WithNodeBounds(
                anchored,
                "demo:alpha",
                new PointD(95d, 105d),
                new SizeD(150d, 70d)),
            "demo:beta",
            new PointD(340d, 230d),
            new SizeD(160d, 80d));

        AssertAnchoredRouteAndArrow(
            composition,
            moved,
            new PointD(245d, 140d),
            new PointD(340d, 270d));
        Assert.Equal(
            anchored.VisualModel.VisualStates.Select(static visual => visual.ConnectorAnchors),
            moved.VisualModel.VisualStates.Select(static visual => visual.ConnectorAnchors));
    }

    [Fact]
    public void ReferencedEndpointsAndArrowFollowResizedNodeBounds()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var original = composition.Document.CaptureSnapshot();
        var anchored = WithAlphaBetaAnchors(
            original,
            [new ConnectorAnchor(new ConnectorAnchorId("test:anchor:alpha-source"),
                ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0)],
            [new ConnectorAnchor(new ConnectorAnchorId("test:anchor:beta-target"),
                ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]);
        var resized = WithNodeBounds(
            WithNodeBounds(
                anchored,
                "demo:alpha",
                new PointD(60d, 70d),
                new SizeD(190d, 110d)),
            "demo:beta",
            new PointD(300d, 190d),
            new SizeD(200d, 120d));

        AssertAnchoredRouteAndArrow(
            composition,
            resized,
            new PointD(250d, 125d),
            new PointD(300d, 250d));
        Assert.Equal(
            anchored.VisualModel.VisualStates.Select(static visual => visual.ConnectorAnchors),
            resized.VisualModel.VisualStates.Select(static visual => visual.ConnectorAnchors));
    }

    private static (
        NeutralDemoComposition Composition,
        DocumentSnapshot Snapshot,
        SemanticRelationshipSnapshot Relationship) CaptureBetaGamma()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var snapshot = composition.Document.CaptureSnapshot();
        var relationship = snapshot.SemanticModel.Relationships.Single(item =>
            item.Id.Value == "demo:beta-gamma");
        return (composition, snapshot, relationship);
    }

    private static DocumentSnapshot WithName(
        DocumentSnapshot snapshot,
        SemanticRelationshipSnapshot relationship,
        string name)
    {
        var replacement = new SemanticRelationshipSnapshot(
            relationship.Id,
            relationship.TypeId,
            relationship.SourceId,
            relationship.TargetId,
            relationship.Properties.Select(entry =>
                StringComparer.Ordinal.Equals(
                    entry.Key,
                    NeutralDemoPipeline.LabelPropertyKey)
                        ? new KeyValuePair<string, PropertyValue>(
                            entry.Key,
                            PropertyValue.FromText(name))
                        : entry));
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                snapshot.DocumentId,
                snapshot.Revision,
                snapshot.SemanticModel.Elements,
                snapshot.SemanticModel.Relationships.Select(item =>
                    item.Id == replacement.Id ? replacement : item)),
            snapshot.VisualModel,
            snapshot.Metadata);
    }

    private static DocumentSnapshot WithAlphaBetaAnchors(
        DocumentSnapshot snapshot,
        IEnumerable<ConnectorAnchor> sourceAnchors,
        IEnumerable<ConnectorAnchor> targetAnchors)
    {
        var sourceAnchorId = sourceAnchors.Single(anchor =>
            anchor.Id.Value == "test:anchor:alpha-source").Id;
        var targetAnchorId = targetAnchors.Single(anchor =>
            anchor.Id.Value == "test:anchor:beta-target").Id;
        var visuals = snapshot.VisualModel.VisualStates.Select(visual =>
        {
            var anchors = visual.SemanticElementId.Value switch
            {
                "demo:alpha" => sourceAnchors,
                "demo:beta" => targetAnchors,
                _ => visual.ConnectorAnchors,
            };
            return new VisualStateSnapshot(
                visual.Id,
                visual.SemanticElementId,
                visual.Position,
                visual.Size,
                visual.PlacementMode,
                visual.Route,
                visual.Properties,
                anchors,
                visual.SemanticElementId.Value == "demo:alpha-beta"
                    ? sourceAnchorId
                    : visual.SourceAnchorId,
                visual.SemanticElementId.Value == "demo:alpha-beta"
                    ? targetAnchorId
                    : visual.TargetAnchorId);
        });
        return new DocumentSnapshot(
            snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, visuals),
            snapshot.Metadata);
    }

    private static DocumentSnapshot WithNodeBounds(
        DocumentSnapshot snapshot,
        string semanticElementId,
        PointD position,
        SizeD size)
    {
        var visuals = snapshot.VisualModel.VisualStates.Select(visual =>
            StringComparer.Ordinal.Equals(visual.SemanticElementId.Value, semanticElementId)
                ? new VisualStateSnapshot(
                    visual.Id,
                    visual.SemanticElementId,
                    position,
                    size,
                    visual.PlacementMode,
                    visual.Route,
                    visual.Properties,
                    visual.ConnectorAnchors,
                    visual.SourceAnchorId,
                    visual.TargetAnchorId)
                : visual);
        return new DocumentSnapshot(
            snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, visuals),
            snapshot.Metadata);
    }

    private static DocumentSnapshot WithAlphaBetaReferences(
        DocumentSnapshot snapshot,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId)
    {
        var visuals = snapshot.VisualModel.VisualStates.Select(visual =>
            visual.SemanticElementId.Value == "demo:alpha-beta"
                ? new VisualStateSnapshot(
                    visual.Id,
                    visual.SemanticElementId,
                    visual.Position,
                    visual.Size,
                    visual.PlacementMode,
                    visual.Route,
                    visual.Properties,
                    visual.ConnectorAnchors,
                    sourceAnchorId,
                    targetAnchorId)
                : visual);
        return new DocumentSnapshot(
            snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, visuals),
            snapshot.Metadata);
    }

    private static void AssertAnchoredRouteAndArrow(
        NeutralDemoComposition composition,
        DocumentSnapshot snapshot,
        PointD expectedSource,
        PointD expectedTarget)
    {
        var (graph, layout, routing) = RunPipeline(composition, snapshot);
        var edge = graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId.Value == "demo:alpha-beta");
        var route = routing.Routes.Single(candidate => candidate.ProjectedEdgeId == edge.Id);
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var arrow = Assert.Single(scene.Items, item =>
            item.Origin.ProjectedObjectId == edge.Id &&
            item.Metadata.TryGetValue(
                Canvas2DConnectorArrowMetadata.TargetArrow,
                out var marker) &&
            marker.Kind == PropertyValueKind.Boolean &&
            marker.BooleanValue);

        Assert.Equal(expectedSource, route.SourceAnchor);
        Assert.Equal(expectedTarget, route.DestinationAnchor);
        Assert.Equal(expectedTarget, arrow.Geometry.Points[0]);
    }

    private static (ProjectedGraph Graph, LayoutResult Layout, RoutingResult Routing) RunPipeline(
        NeutralDemoComposition composition,
        DocumentSnapshot snapshot)
    {
        var projectionResult = composition.Configuration.ProjectionEngine.Project(snapshot);
        var graph = Assert.IsType<ProjectedGraph>(projectionResult.Graph);
        var layoutResult = composition.Configuration.LayoutEngine.Layout(
            graph,
            composition.Configuration.LayoutAlgorithmId);
        var layout = Assert.IsType<LayoutResult>(layoutResult.Result);
        var routingResult = composition.Configuration.RoutingEngine.Route(
            graph,
            layout,
            composition.Configuration.RoutingAlgorithmId);
        var routing = Assert.IsType<RoutingResult>(routingResult.Result);
        return (graph, layout, routing);
    }
}
