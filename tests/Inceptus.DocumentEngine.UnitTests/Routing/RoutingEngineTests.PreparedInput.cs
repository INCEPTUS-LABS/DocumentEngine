using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Routing;

public sealed partial class RoutingEngineTests
{
    [Theory]
    [InlineData("missing-edge")]
    [InlineData("extra-edge")]
    [InlineData("unknown-node")]
    [InlineData("source-owner")]
    [InlineData("target-owner")]
    [InlineData("revision")]
    [InlineData("document")]
    [InlineData("geometry")]
    public void PreparedInputRejectsInvalidProviderDataBeforeInvokingAlgorithm(string fault)
    {
        var graph = Graph();
        var layout = Layout(graph);
        var domain = new RoutingObstacleDomain(graph.Nodes.Select(node => node.Id));
        var entries = graph.Edges.ToDictionary(edge => edge.Id, _ => domain);
        var sourceGraph = graph;
        var sourceLayout = layout;
        var edge = graph.Edges[0];
        switch (fault)
        {
            case "missing-edge": entries.Remove(edge.Id); break;
            case "extra-edge": entries.Add(new ProjectedObjectId("unexpected"), domain); break;
            case "unknown-node":
                entries[edge.Id] = new RoutingObstacleDomain(domain.NodeIds.Append(new ProjectedObjectId("missing")));
                break;
            case "source-owner":
            case "target-owner":
                var excluded = fault == "source-owner" ? edge.SourceNodeId : edge.TargetNodeId;
                entries[edge.Id] = new RoutingObstacleDomain(domain.NodeIds.Where(id => id != excluded));
                break;
            case "revision":
                sourceGraph = new ProjectedGraph(graph.DocumentId, graph.SourceRevision.Increment(),
                    graph.Nodes, graph.Edges, graph.Groups, graph.Ports, graph.Labels);
                break;
            case "document":
                sourceLayout = new LayoutResult(new DocumentId("foreign"), layout.SourceRevision,
                    layout.AlgorithmId, layout.Computation);
                break;
            case "geometry":
                sourceLayout = new LayoutResult(layout.DocumentId, layout.SourceRevision, layout.AlgorithmId,
                    new LayoutComputation(layout.Nodes.Select(node => new LayoutNodeGeometry(
                        node.ProjectedObjectId, new RectD(node.Bounds.X + 1, node.Bounds.Y,
                            node.Bounds.Width, node.Bounds.Height), node.Transform))));
                break;
        }
        var calls = 0;
        var engine = Engine((AlgorithmAId, new DelegateAlgorithm((g, l, _, _) =>
        {
            calls++;
            return Complete(g, l);
        })));

        var result = engine.Route(graph, layout, AlgorithmAId,
            new RoutingContext(preparedInput: new PreparedRoutingInput(sourceGraph, sourceLayout, entries)));

        Assert.False(result.IsSuccessful);
        Assert.Null(result.Result);
        Assert.Equal(0, calls);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == RoutingDiagnosticCodes.InvalidInput);
    }

    [Fact]
    public void PreparedInputCopiesCollectionsAndPreservesExactCompleteRouting()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var nodes = graph.Nodes.Select(node => node.Id).ToList();
        var domain = new RoutingObstacleDomain(nodes);
        Assert.Equal(domain, new RoutingObstacleDomain(nodes.AsEnumerable().Reverse().Concat(nodes)));
        var entries = graph.Edges.ToDictionary(edge => edge.Id, _ => domain);
        var prepared = new PreparedRoutingInput(graph, layout, entries);
        nodes.Clear();
        entries.Clear();
        var engine = Engine((AlgorithmAId, new DelegateAlgorithm(CompleteWithContext)));

        var ordinary = engine.Route(graph, layout, AlgorithmAId);
        var actual = engine.Route(graph, layout, AlgorithmAId, new RoutingContext(preparedInput: prepared));

        Assert.True(actual.IsSuccessful);
        Assert.Equal(ordinary.Result, actual.Result);
        Assert.Equal(graph.NodeCount, domain.NodeIds.Length);
        Assert.Equal(graph.EdgeCount, prepared.EdgeDomains.Count);
    }
}
