using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class NodeLabelPlacementTests
{
    private static readonly DocumentId DocumentId = new("test:node-label-placement");

    [Fact]
    public void ContractIsImmutableStructuralAndRejectsMalformedConfiguration()
    {
        var first = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 160d);
        var same = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 160d);

        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, first.Kind);
        Assert.Equal(8d, first.Gap);
        Assert.Equal(160d, first.MaximumWidth);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Equal(
            new NodeLabelPlacement(NodeLabelPlacementKind.InsideCentered),
            NodeLabelPlacement.InsideCentered);

        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeLabelPlacement(
            (NodeLabelPlacementKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            double.NaN,
            160d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            -1d,
            160d));
        Assert.Throws<ArgumentException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            maximumWidth: 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            maximumWidth: double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.InsideCentered,
            gap: 1d));
        Assert.Throws<ArgumentException>(() => new NodeLabelPlacement(
            NodeLabelPlacementKind.InsideCentered,
            maximumWidth: 120d));
    }

    [Fact]
    public void ProjectedLabelKeepsExplicitIntentOptionalAndStructural()
    {
        var owner = Node("owner");
        var source = LabelSource(owner, "name-label");
        var implicitPlacement = new ProjectedLabel(source, owner.Id, "Neutral");
        var sameImplicitPlacement = new ProjectedLabel(source, owner.Id, "Neutral");
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 160d);
        var explicitPlacement = new ProjectedLabel(
            source,
            owner.Id,
            "Neutral",
            nodePlacement: placement);

        Assert.Null(implicitPlacement.NodePlacement);
        Assert.Equal(NodeLabelPlacement.InsideCentered,
            implicitPlacement.NodePlacement ?? NodeLabelPlacement.InsideCentered);
        Assert.Equal(implicitPlacement, sameImplicitPlacement);
        Assert.Same(placement, explicitPlacement.NodePlacement);
        Assert.NotEqual(implicitPlacement, explicitPlacement);
    }

    [Fact]
    public void ProjectedLabelInteractionPolicyIsExplicitStructuralAndNodeOwned()
    {
        var owner = Node("interaction-owner");
        var source = LabelSource(owner, "interaction-label");
        var fixedLabel = new ProjectedLabel(source, owner.Id, "Fixed");
        var editableLabel = new ProjectedLabel(
            source,
            owner.Id,
            "Fixed",
            nodeInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize);

        Assert.Equal(NodeLabelInteractionPolicy.Fixed, fixedLabel.NodeInteractionPolicy);
        Assert.Equal(
            NodeLabelInteractionPolicy.MoveAndResize,
            editableLabel.NodeInteractionPolicy);
        Assert.NotEqual(fixedLabel, editableLabel);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectedLabel(
            source,
            owner.Id,
            "Invalid",
            nodeInteractionPolicy: (NodeLabelInteractionPolicy)99));

        var target = Node("interaction-target");
        var edge = new ProjectedEdge(
            new ProjectionSourceTrace(
                DocumentId,
                new ProjectionRuleId("test:interaction-edge-rule"),
                ProjectionSourceKind.SemanticRelationship,
                new SemanticElementId("test:interaction-relationship"),
                new SemanticTypeId("test:interaction-edge"),
                "interaction-edge"),
            owner.Id,
            target.Id);
        var connectorLabel = new ProjectedLabel(
            new ProjectionSourceTrace(
                DocumentId,
                edge.Source.RuleId,
                edge.Source.SourceKind,
                edge.Source.SemanticElementId,
                edge.Source.SemanticTypeId,
                "interaction-connector-label"),
            edge.Id,
            "Connector",
            nodeInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize);

        Assert.Throws<ArgumentException>(() => new ProjectedGraph(
            DocumentId,
            DocumentRevision.Zero,
            [owner, target],
            [edge],
            labels: [connectorLabel]));
    }

    [Fact]
    public void GraphAllowsImplicitConnectorLabelButRejectsNodePlacementOnNonNodeOwner()
    {
        var source = Node("source");
        var target = Node("target");
        var edge = new ProjectedEdge(
            new ProjectionSourceTrace(
                DocumentId,
                new ProjectionRuleId("test:edge-rule"),
                ProjectionSourceKind.SemanticRelationship,
                new SemanticElementId("test:relationship"),
                new SemanticTypeId("test:edge"),
                "edge"),
            source.Id,
            target.Id);
        var implicitConnectorLabel = new ProjectedLabel(
            new ProjectionSourceTrace(
                DocumentId,
                edge.Source.RuleId,
                edge.Source.SourceKind,
                edge.Source.SemanticElementId,
                edge.Source.SemanticTypeId,
                "label"),
            edge.Id,
            "Connector label");

        var graph = new ProjectedGraph(
            DocumentId,
            new DocumentRevision(0),
            [source, target],
            [edge],
            labels: [implicitConnectorLabel]);

        Assert.Null(Assert.Single(graph.Labels).NodePlacement);

        var externalConnectorLabel = new ProjectedLabel(
            implicitConnectorLabel.Source,
            edge.Id,
            implicitConnectorLabel.Text,
            nodePlacement: new NodeLabelPlacement(
                NodeLabelPlacementKind.OutsideBelow,
                gap: 8d,
                maximumWidth: 160d));
        Assert.Throws<ArgumentException>(() => new ProjectedGraph(
            DocumentId,
            new DocumentRevision(0),
            [source, target],
            [edge],
            labels: [externalConnectorLabel]));
    }

    private static ProjectedNode Node(string localKey)
    {
        var semanticId = new SemanticElementId($"test:{localKey}");
        return new ProjectedNode(new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId("test:node-rule"),
            ProjectionSourceKind.SemanticElement,
            semanticId,
            new SemanticTypeId("test:node"),
            localKey));
    }

    private static ProjectionSourceTrace LabelSource(ProjectedNode owner, string localKey) =>
        new(
            DocumentId,
            owner.Source.RuleId,
            owner.Source.SourceKind,
            owner.Source.SemanticElementId,
            owner.Source.SemanticTypeId,
            localKey);
}
