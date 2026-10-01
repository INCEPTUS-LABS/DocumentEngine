using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class BpmnRoutingAlgorithmTests
{
    [Fact]
    public void PreparedFullDomainPreservesOrdinaryRoutesAndDomainPreprocessingIsOperationLocal()
    {
        var a = Node("a", BpmnSemanticTypes.Task);
        var b = Node("b", BpmnSemanticTypes.Task);
        var c = Node("c", BpmnSemanticTypes.Task);
        var ab = Edge("ab", a, b);
        var ba = Edge("ba", b, a);
        var graph = Graph([a, b, c], [ab, ba]);
        var layout = Layout(graph, (a, new RectD(0, 0, 100, 80)),
            (b, new RectD(400, 0, 100, 80)), (c, new RectD(200, 0, 100, 80)));
        var full = new RoutingObstacleDomain([a.Id, b.Id, c.Id]);
        var equal = new RoutingObstacleDomain([c.Id, a.Id, b.Id]);
        var prepared = new PreparedRoutingInput(graph, layout,
            new Dictionary<ProjectedObjectId, RoutingObstacleDomain> { [ab.Id] = full, [ba.Id] = equal });
        var algorithm = new BpmnRoutingAlgorithm();
        var ordinary = algorithm.Route(graph, layout, RoutingContext.Empty, CancellationToken.None);
        var actual = algorithm.Route(graph, layout, new RoutingContext(preparedInput: prepared), CancellationToken.None);
        Assert.Equal(ordinary.Computation, actual.Computation);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(ordinary.Diagnostics),
            System.Text.Json.JsonSerializer.Serialize(actual.Diagnostics));
        Assert.Equal(2, actual.Computation!.Routes.Length + actual.Computation.NoRouteEdgeIds.Length);

        var type = typeof(BpmnRoutingAlgorithm).Assembly.GetType(
            "Inceptus.DocumentEngine.Bpmn.Routing.BpmnRoutingOperation", throwOnError: true)!;
        var first = Activator.CreateInstance(type, layout, prepared)!;
        var second = Activator.CreateInstance(type, layout, prepared)!;
        var get = type.GetMethod("GetDomain", BindingFlags.Public | BindingFlags.Instance)!;
        var abContext = get.Invoke(first, [ab.Id]);
        Assert.Same(abContext, get.Invoke(first, [ba.Id]));
        Assert.NotSame(abContext, get.Invoke(second, [ab.Id]));
        var isolated = new PreparedRoutingInput(graph, layout,
            new Dictionary<ProjectedObjectId, RoutingObstacleDomain>
            {
                [ab.Id] = full,
                [ba.Id] = new RoutingObstacleDomain([a.Id, b.Id]),
            });
        var distinct = Activator.CreateInstance(type, layout, isolated)!;
        Assert.NotSame(get.Invoke(distinct, [ab.Id]), get.Invoke(distinct, [ba.Id]));
    }
}
