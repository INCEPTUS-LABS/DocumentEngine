using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class ProjectedGraphTests
{
    private static readonly DocumentId DocumentId = new("test:document");
    private static readonly ProjectionRuleId RuleId = new("test:rule");

    [Fact]
    public void GraphDefensivelyCopiesAndCanonicallyOrdersAllFiveCategories()
    {
        var contribution = CompleteContribution(reverse: true);
        var nodes = contribution.Nodes.ToList();
        var edges = contribution.Edges.ToList();
        var groups = contribution.Groups.ToList();
        var ports = contribution.Ports.ToList();
        var labels = contribution.Labels.ToList();

        var graph = new ProjectedGraph(
            DocumentId,
            new DocumentRevision(9),
            nodes,
            edges,
            groups,
            ports,
            labels);
        nodes.Clear();
        edges.Clear();
        groups.Clear();
        ports.Clear();
        labels.Clear();

        Assert.Equal(DocumentId, graph.DocumentId);
        Assert.Equal(new DocumentRevision(9), graph.SourceRevision);
        Assert.Equal(2, graph.NodeCount);
        Assert.Equal(1, graph.EdgeCount);
        Assert.Equal(1, graph.GroupCount);
        Assert.Equal(1, graph.PortCount);
        Assert.Equal(1, graph.LabelCount);
        Assert.True(graph.Nodes.Select(node => node.Id.Value).SequenceEqual(
            graph.Nodes.Select(node => node.Id.Value).Order(StringComparer.Ordinal)));
        AssertReadOnly(graph.Nodes);
        AssertReadOnly(graph.Edges);
        AssertReadOnly(graph.Groups);
        AssertReadOnly(graph.Ports);
        AssertReadOnly(graph.Labels);
    }

    [Fact]
    public void IndependentGraphsUseDeepStructuralEqualityAndHashing()
    {
        var firstContribution = CompleteContribution(reverse: false);
        var sameContribution = CompleteContribution(reverse: true);
        var first = Graph(firstContribution);
        var same = Graph(sameContribution);
        var different = new ProjectedGraph(
            DocumentId,
            new DocumentRevision(8),
            same.Nodes,
            same.Edges,
            same.Groups,
            same.Ports,
            same.Labels);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void GraphRejectsCrossCategoryDuplicateProjectedIds()
    {
        var nodeA = Node("test:a", "node-a");
        var nodeB = Node("test:b", "node-b");
        var edge = Edge("test:edge", "edge", nodeA.Id, nodeB.Id);
        var idField = typeof(ProjectedEdge).GetField(
            "<Id>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(idField);
        idField.SetValue(edge, nodeA.Id);

        Assert.Throws<ArgumentException>(() => new ProjectedGraph(
            DocumentId,
            DocumentRevision.Zero,
            [nodeA, nodeB],
            [edge]));
    }

    [Fact]
    public void GraphRejectsProjectedObjectsTracingToAnotherDocument()
    {
        var foreignNode = new ProjectedNode(new ProjectionSourceTrace(
            new DocumentId("test:other-document"),
            RuleId,
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId("test:a"),
            new SemanticTypeId("test:node-type"),
            "node-a"));

        Assert.Throws<ArgumentException>(() => new ProjectedGraph(
            DocumentId,
            DocumentRevision.Zero,
            [foreignNode]));
    }

    [Fact]
    public void GraphRejectsEveryInvalidTopologyReference()
    {
        var nodeA = Node("test:a", "node-a");
        var nodeB = Node("test:b", "node-b");
        var missing = new ProjectedObjectId("test:missing");
        var portA = Port("test:a", "port-a", nodeA.Id);
        var portB = Port("test:b", "port-b", nodeB.Id);

        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA],
            ports: [Port("test:a", "missing-owner", missing)]));
        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA],
            groups: [Group("test:a", "missing-member", [missing])]));
        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA, nodeB],
            edges: [Edge("test:edge", "missing-source", missing, nodeB.Id)]));
        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA, nodeB],
            edges: [Edge("test:edge", "missing-target", nodeA.Id, missing)]));
        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA, nodeB],
            edges: [Edge("test:edge", "missing-port", nodeA.Id, nodeB.Id, missing)]));
        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA, nodeB],
            edges: [Edge("test:edge", "wrong-port-owner", nodeA.Id, nodeB.Id, portB.Id)],
            ports: [portB]));
        Assert.Throws<ArgumentException>(() => Graph(
            nodes: [nodeA],
            labels: [Label("test:a", "missing-label-owner", missing, "missing")]));

        var valid = Graph(
            nodes: [nodeA, nodeB],
            edges: [Edge("test:edge", "valid", nodeA.Id, nodeB.Id, portA.Id)],
            ports: [portA]);
        Assert.Equal(2, valid.NodeCount);
        Assert.Single(valid.Edges);
    }

    [Fact]
    public void ContributionDefensivelyCopiesAndCanonicallyOrdersAllFiveCategories()
    {
        var nodeA = Node("test:a", "node-a");
        var nodeB = Node("test:b", "node-b");
        var portA = Port("test:a", "port-a", nodeA.Id);
        var group = Group("test:a", "group", [nodeB.Id, nodeA.Id]);
        var edge = Edge("test:edge", "edge", nodeA.Id, nodeB.Id, portA.Id);
        var label = Label("test:a", "label", nodeA.Id, "Node A");
        var nodes = new List<ProjectedNode> { nodeB, nodeA };
        var edges = new List<ProjectedEdge> { edge };
        var groups = new List<ProjectedGroup> { group };
        var ports = new List<ProjectedPort> { portA };
        var labels = new List<ProjectedLabel> { label };

        var contribution = new ProjectionRuleContribution(nodes, edges, groups, ports, labels);
        nodes.Clear();
        edges.Clear();
        groups.Clear();
        ports.Clear();
        labels.Clear();

        Assert.Equal(
            new[] { nodeA.Id.Value, nodeB.Id.Value }.Order(StringComparer.Ordinal),
            contribution.Nodes.Select(node => node.Id.Value));
        Assert.Equal(edge, Assert.Single(contribution.Edges));
        Assert.Equal(group, Assert.Single(contribution.Groups));
        Assert.Equal(portA, Assert.Single(contribution.Ports));
        Assert.Equal(label, Assert.Single(contribution.Labels));
        AssertReadOnly(contribution.Nodes);
        AssertReadOnly(contribution.Edges);
        AssertReadOnly(contribution.Groups);
        AssertReadOnly(contribution.Ports);
        AssertReadOnly(contribution.Labels);
    }

    [Fact]
    public void ProjectedObjectsDefensivelyCopyPersistentDataAndPropertyMaps()
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:value", PropertyValue.FromText("original")),
        };
        var route = new List<PointD> { new(10d, 20d), new(30d, 40d) };
        var nodeA = new ProjectedNode(
            Source("test:a", "node-a"),
            new ProjectedPlacementHint(
                new PointD(25d, 50d),
                new SizeD(100d, 60d),
                VisualPlacementMode.Pinned),
            properties,
            properties,
            properties,
            properties,
            properties);
        var nodeB = Node("test:b", "node-b");
        var edge = new ProjectedEdge(
            Source("test:edge", "edge", ProjectionSourceKind.SemanticRelationship),
            nodeA.Id,
            nodeB.Id,
            persistentRoute: route,
            semanticProperties: properties,
            projectedProperties: properties,
            layoutHints: properties,
            routingHints: properties,
            algorithmMetadata: properties);
        properties.Clear();
        route.Clear();

        Assert.Equal(new PointD(25d, 50d), nodeA.PlacementHint!.Position);
        Assert.Equal(new SizeD(100d, 60d), nodeA.PlacementHint.Size);
        Assert.Equal(VisualPlacementMode.Pinned, nodeA.PlacementHint.PlacementMode);
        Assert.Equal("original", nodeA.SemanticProperties["test:value"].TextValue);
        Assert.Equal("original", nodeA.ProjectedProperties["test:value"].TextValue);
        Assert.Equal("original", nodeA.LayoutHints["test:value"].TextValue);
        Assert.Equal("original", nodeA.RoutingHints["test:value"].TextValue);
        Assert.Equal("original", nodeA.AlgorithmMetadata["test:value"].TextValue);
        Assert.Equal(
            [new PointD(10d, 20d), new PointD(30d, 40d)],
            edge.PersistentRoute.ToArray());
        Assert.True(((IList<PointD>)edge.PersistentRoute).IsReadOnly);
        Assert.Throws<NotSupportedException>(() => ((IList<PointD>)edge.PersistentRoute).Clear());
    }

    [Fact]
    public void GroupMembersAreDefensivelyCopiedCanonicallyOrderedAndUnique()
    {
        var nodeA = Node("test:a", "node-a");
        var nodeB = Node("test:b", "node-b");
        var members = new List<ProjectedObjectId> { nodeB.Id, nodeA.Id };

        var group = Group("test:a", "group", members);
        members.Clear();

        Assert.Equal(
            new[] { nodeA.Id.Value, nodeB.Id.Value }.Order(StringComparer.Ordinal),
            group.MemberNodeIds.Select(id => id.Value));
        Assert.True(((IList<ProjectedObjectId>)group.MemberNodeIds).IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ProjectedObjectId>)group.MemberNodeIds).Clear());
        Assert.Throws<ArgumentException>(() => Group(
            "test:a",
            "duplicate-group",
            [nodeA.Id, nodeA.Id]));
    }

    [Fact]
    public void IndependentContributionsUseDeepStructuralEqualityAndHashing()
    {
        var first = CompleteContribution(reverse: false);
        var same = CompleteContribution(reverse: true);
        var differentNode = new ProjectedNode(
            Source("test:a", "node-a"),
            projectedProperties:
            [
                new("test:changed", PropertyValue.FromBoolean(true)),
            ]);
        var different = new ProjectionRuleContribution(
            [differentNode, Node("test:b", "node-b")],
            first.Edges,
            first.Groups,
            first.Ports,
            first.Labels);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void DuplicateProjectedIdsWithinAContributionCategoryAreRejected()
    {
        var node = Node("test:a", "node-a");

        Assert.Throws<ArgumentException>(() =>
            new ProjectionRuleContribution(nodes: [node, Node("test:a", "node-a")]));
    }

    [Fact]
    public void EveryProjectedObjectCategoryPreservesIdentityKindAndSourceTraceability()
    {
        var nodeA = Node("test:a", "node-a", new VisualStateId("test:visual-a"));
        var nodeB = Node("test:b", "node-b");
        var group = Group("test:a", "group", [nodeA.Id, nodeB.Id]);
        var port = Port("test:a", "port", nodeA.Id);
        var edge = Edge("test:edge", "edge", nodeA.Id, nodeB.Id, port.Id);
        var label = Label("test:a", "label", nodeA.Id, "A");
        IProjectedObject[] projectedObjects = [nodeA, nodeB, edge, group, port, label];

        Assert.Equal(
            [
                ProjectedObjectKind.Node,
                ProjectedObjectKind.Node,
                ProjectedObjectKind.Edge,
                ProjectedObjectKind.Group,
                ProjectedObjectKind.Port,
                ProjectedObjectKind.Label,
            ],
            projectedObjects.Select(projectedObject => projectedObject.Kind));
        Assert.All(projectedObjects, projectedObject =>
        {
            Assert.Equal(DocumentId, projectedObject.Source.DocumentId);
            Assert.Equal(RuleId, projectedObject.Source.RuleId);
            Assert.Equal(
                ProjectedObjectIdentity.Create(projectedObject.Source, projectedObject.Kind),
                projectedObject.Id);
        });
        Assert.Equal(new VisualStateId("test:visual-a"), nodeA.Source.VisualStateId);
        Assert.Null(nodeB.Source.VisualStateId);
    }

    [Fact]
    public void PlacementHintUsesStructuralEqualityAndRejectsUndefinedMode()
    {
        var first = new ProjectedPlacementHint(
            new PointD(10d, 20d),
            new SizeD(30d, 40d),
            VisualPlacementMode.Manual);
        var same = new ProjectedPlacementHint(
            new PointD(10d, 20d),
            new SizeD(30d, 40d),
            VisualPlacementMode.Manual);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectedPlacementHint(
            new PointD(10d, 20d),
            new SizeD(30d, 40d),
            (VisualPlacementMode)99));
    }

    [Fact]
    public void BoundaryAttachmentAndGeometryInteractionParticipateInProjectedEquality()
    {
        var ownerId = new SemanticElementId("test:owner");
        var placement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        var attachment = new ProjectedBoundaryAttachment(ownerId, placement);
        var firstHint = new ProjectedPlacementHint(
            new PointD(10d, 20d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Manual,
            attachment);
        var sameHint = new ProjectedPlacementHint(
            new PointD(10d, 20d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Manual,
            new ProjectedBoundaryAttachment(
                new SemanticElementId(ownerId.Value),
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d)));
        var changedHint = new ProjectedPlacementHint(
            new PointD(10d, 20d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Manual,
            new ProjectedBoundaryAttachment(
                ownerId,
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Right, 0.5d)));
        var source = Source("test:attached", "attached");
        var firstNode = new ProjectedNode(
            source,
            firstHint,
            geometryInteractionPolicy:
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
        var sameNode = new ProjectedNode(
            source,
            sameHint,
            geometryInteractionPolicy:
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
        var freeNode = new ProjectedNode(source, firstHint);

        Assert.Equal(firstHint, sameHint);
        Assert.Equal(firstHint.GetHashCode(), sameHint.GetHashCode());
        Assert.NotEqual(firstHint, changedHint);
        Assert.Equal(firstNode, sameNode);
        Assert.Equal(firstNode.GetHashCode(), sameNode.GetHashCode());
        Assert.NotEqual(firstNode, freeNode);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectedNode(
            source,
            geometryInteractionPolicy: (NodeGeometryInteractionPolicy)99));
    }

    private static ProjectionRuleContribution CompleteContribution(bool reverse)
    {
        var nodeA = Node("test:a", "node-a");
        var nodeB = Node("test:b", "node-b");
        var port = Port("test:a", "port", nodeA.Id);
        var edge = Edge("test:edge", "edge", nodeA.Id, nodeB.Id, port.Id);
        var group = Group("test:a", "group", [nodeB.Id, nodeA.Id]);
        var label = Label("test:a", "label", nodeA.Id, "A");

        return new ProjectionRuleContribution(
            reverse ? [nodeB, nodeA] : [nodeA, nodeB],
            [edge],
            [group],
            [port],
            [label]);
    }

    private static ProjectedGraph Graph(ProjectionRuleContribution contribution) =>
        new(
            DocumentId,
            new DocumentRevision(7),
            contribution.Nodes,
            contribution.Edges,
            contribution.Groups,
            contribution.Ports,
            contribution.Labels);

    private static ProjectedGraph Graph(
        IEnumerable<ProjectedNode>? nodes = null,
        IEnumerable<ProjectedEdge>? edges = null,
        IEnumerable<ProjectedGroup>? groups = null,
        IEnumerable<ProjectedPort>? ports = null,
        IEnumerable<ProjectedLabel>? labels = null) =>
        new(
            DocumentId,
            DocumentRevision.Zero,
            nodes,
            edges,
            groups,
            ports,
            labels);

    private static ProjectedNode Node(
        string semanticId,
        string localKey,
        VisualStateId? visualStateId = null) =>
        new(Source(semanticId, localKey, visualStateId: visualStateId));

    private static ProjectedEdge Edge(
        string semanticId,
        string localKey,
        ProjectedObjectId sourceNodeId,
        ProjectedObjectId targetNodeId,
        ProjectedObjectId? sourcePortId = null) =>
        new(
            Source(semanticId, localKey, ProjectionSourceKind.SemanticRelationship),
            sourceNodeId,
            targetNodeId,
            sourcePortId);

    private static ProjectedGroup Group(
        string semanticId,
        string localKey,
        IEnumerable<ProjectedObjectId> memberNodeIds) =>
        new(Source(semanticId, localKey), memberNodeIds);

    private static ProjectedPort Port(
        string semanticId,
        string localKey,
        ProjectedObjectId ownerNodeId) =>
        new(Source(semanticId, localKey), ownerNodeId);

    private static ProjectedLabel Label(
        string semanticId,
        string localKey,
        ProjectedObjectId ownerId,
        string text) =>
        new(Source(semanticId, localKey), ownerId, text);

    private static ProjectionSourceTrace Source(
        string semanticId,
        string localKey,
        ProjectionSourceKind sourceKind = ProjectionSourceKind.SemanticElement,
        VisualStateId? visualStateId = null) =>
        new(
            DocumentId,
            RuleId,
            sourceKind,
            new SemanticElementId(semanticId),
            new SemanticTypeId(sourceKind == ProjectionSourceKind.SemanticElement
                ? "test:node-type"
                : "test:edge-type"),
            localKey,
            visualStateId);

    private static void AssertReadOnly<T>(System.Collections.Immutable.ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }
}
