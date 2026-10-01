using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class BpmnRoutingAlgorithmTests
{
    // Every existing routing fixture using Engine now also compares the complete algorithm
    // result with the frozen pre-optimization implementation, before Runtime validation.
    private sealed class ExactReferenceCheckingRouter : IRoutingAlgorithm
    {
        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken)
        {
            var expected = new RoutingReference.BpmnRoutingAlgorithm().Route(
                graph, layout, context, cancellationToken);
            var actual = new BpmnRoutingAlgorithm().Route(
                graph, layout, context, cancellationToken);
            Assert.Equal(expected.Succeeded, actual.Succeeded);
            AssertDiagnosticsEqual(expected.Diagnostics, actual.Diagnostics);
            Assert.Equal(expected.Computation, actual.Computation);
            return actual;
        }
    }

    [Theory]
    [InlineData(1210)]
    [InlineData(73)]
    [InlineData(401)]
    public void DeterministicObstacleAndGuidanceCorpusMatchesCompleteReference(int seed)
    {
        var random = new Random(seed);
        for (var sample = 0; sample < 24; sample++)
        {
            var source = Node($"generated-source-{sample}", BpmnSemanticTypes.Task);
            var target = Node($"generated-target-{sample}", BpmnSemanticTypes.Task);
            var sourceSide = (ConnectorAnchorSide)(sample % 4);
            var targetSide = (ConnectorAnchorSide)((sample / 4) % 4);
            var sourcePort = Port("generated-source", source, sourceSide, ConnectorAnchorRoleCapability.Source);
            var targetPort = Port("generated-target", target, targetSide, ConnectorAnchorRoleCapability.Target);
            var nodes = new List<ProjectedNode> { source, target };
            var placements = new List<(ProjectedNode Node, RectD Bounds)>
            {
                (source, new RectD(sample % 6 == 0 ? 0 : 20, random.Next(0, 5) * 30, 40, 40)),
                (target, new RectD(340, random.Next(0, 6) * 30, 40, 40)),
            };
            for (var obstacle = 0; obstacle < sample % 6; obstacle++)
            {
                var node = Node($"generated-obstacle-{sample}-{obstacle}", BpmnSemanticTypes.Task);
                nodes.Add(node);
                placements.Add((node, new RectD(
                    random.Next(90, 280), random.Next(0, 220), random.Next(15, 60), random.Next(15, 90))));
            }

            PointD[]? guidance = sample % 3 == 0
                ? [new(0, 0), new(80, random.Next(0, 250)), new(300, random.Next(0, 250)), new(0, 0)]
                : null;
            var edge = Edge("generated", source, target, guidance, sourcePort.Id, targetPort.Id);
            var graph = Graph(nodes, [edge], [sourcePort, targetPort]);
            var layout = Layout(graph, [.. placements]);
            var first = Engine().Route(graph, layout, Inceptus.DocumentEngine.Bpmn.BpmnAlgorithmIds.DefaultRouting);
            var second = Engine().Route(graph, layout, Inceptus.DocumentEngine.Bpmn.BpmnAlgorithmIds.DefaultRouting);
            Assert.True(first.IsSuccessful);
            Assert.True(second.IsSuccessful);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.Result, second.Result);
            AssertDiagnosticsEqual(first.Diagnostics, second.Diagnostics);
        }
    }

    [Fact]
    public void SharedObstacleContextDoesNotReuseRoutesAcrossObstacleMovesOrRemoval()
    {
        var source = Node("reuse-source", BpmnSemanticTypes.Task);
        var target = Node("reuse-target", BpmnSemanticTypes.Task);
        var obstacle = Node("reuse-obstacle", BpmnSemanticTypes.Task);
        var edge = Edge("reuse-edge", source, target);
        var graph = Graph([source, target, obstacle], [edge]);
        var blocking = Layout(graph,
            (source, new RectD(20, 100, 40, 40)),
            (target, new RectD(320, 100, 40, 40)),
            (obstacle, new RectD(150, 80, 60, 80)));
        var clear = Layout(graph,
            (source, new RectD(20, 100, 40, 40)),
            (target, new RectD(320, 100, 40, 40)),
            (obstacle, new RectD(150, 250, 60, 80)));
        var router = new ExactReferenceCheckingRouter();
        var first = router.Route(graph, blocking, RoutingContext.Empty, CancellationToken.None);
        var second = router.Route(graph, clear, RoutingContext.Empty, CancellationToken.None);
        Assert.NotEqual(first.Computation, second.Computation);
        Assert.Equal(first.Computation, router.Route(graph, blocking, RoutingContext.Empty, CancellationToken.None).Computation);
        var removed = Graph([source, target], [edge]);
        var withoutObstacle = Layout(removed,
            (source, new RectD(20, 100, 40, 40)),
            (target, new RectD(320, 100, 40, 40)));
        Assert.Equal(second.Computation, router.Route(removed, withoutObstacle, RoutingContext.Empty, CancellationToken.None).Computation);
    }

    private static void AssertDiagnosticsEqual(IEnumerable<Diagnostic> expected, IEnumerable<Diagnostic> actual)
    {
        Assert.Equal(
            expected.Select(static d => (d.Code, d.Severity, d.Message, d.SourceIdentity)),
            actual.Select(static d => (d.Code, d.Severity, d.Message, d.SourceIdentity)));
        foreach (var pair in expected.Zip(actual))
        {
            Assert.Equal(pair.First.Context.AsEnumerable(), pair.Second.Context.AsEnumerable());
        }
    }
}
