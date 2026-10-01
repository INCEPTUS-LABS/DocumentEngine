using System.Collections;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnRoutingOperationContextTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type Router = typeof(BpmnRoutingAlgorithm).Assembly.GetType(
        "Inceptus.DocumentEngine.Bpmn.Routing.BpmnOrthogonalRouter", throwOnError: true)!;
    private static readonly Type Obstacle = typeof(BpmnRoutingAlgorithm).Assembly.GetType(
        "Inceptus.DocumentEngine.Bpmn.Routing.BpmnRoutingObstacle", throwOnError: true)!;

    [Fact]
    public void PreferredObstacleAndCoordinateTopologyAreSharedOnlyWithinOneOperation()
    {
        var obstacles = CreateObstacles(4);
        var operation = CreateOperation(obstacles);
        var first = Attempt(operation, "0", "1", 10, 10);
        var second = Attempt(operation, "2", "3", 10, 10);
        Assert.Same(first, second);
        Assert.NotSame(first, Attempt(CreateOperation(obstacles), "0", "1", 10, 10));

        var graph = Graph(first, new PointD(0, 0), new PointD(500, 500));
        Assert.Same(graph, Graph(second, new PointD(500, 500), new PointD(0, 0)));
        Assert.NotSame(graph, Graph(second, new PointD(1, 1), new PointD(501, 501)));
        // Only the most recent topology is retained, even when an older coordinate set returns.
        Assert.NotSame(graph, Graph(first, new PointD(0, 0), new PointD(500, 500)));
        Assert.True(operation.GetType().IsNestedAssembly);
        Assert.True(first.GetType().IsNestedAssembly);
    }

    [Fact]
    public void RelaxedEndpointInflationNeverSharesDifferentOwnersOrPreferredIntervals()
    {
        var operation = CreateOperation(CreateObstacles(4));
        var preferred = Attempt(operation, "0", "1", 10, 10);
        var first = Attempt(operation, "0", "1", 10, 7.5);
        var second = Attempt(operation, "2", "3", 10, 7.5);
        Assert.NotSame(preferred, first);
        Assert.NotSame(first, second);
        Assert.Equal(new RectD(12.5, 12.5, 25, 25), Bounds(first, 0));
        Assert.Equal(new RectD(10, 10, 30, 30), Bounds(second, 0));
        Assert.Equal(new RectD(10, 10, 30, 30), Bounds(preferred, 0));
        Assert.Same(preferred, Attempt(operation, "2", "3", 10, 10));
    }

    [Fact]
    public void AuthoredEndpointCoordinatesDoNotGrowRetainedIntervalCaches()
    {
        var context = Attempt(CreateOperation(CreateObstacles(4)), "0", "1", 10, 10);
        for (var index = 0; index < 32; index++)
        {
            Graph(context, new PointD(index + 0.25, index + 0.5), new PointD(500 + index, 501 + index));
        }

        foreach (var name in new[] { "_rowBlocks", "_columnBlocks" })
        {
            var cache = Assert.IsAssignableFrom<IDictionary>(context.GetType().GetField(name, InstanceMembers)!.GetValue(context));
            Assert.InRange(cache.Count, 1, 9); // zero plus at most two boundaries per obstacle
        }
    }

    [Fact]
    public void CancellationDuringObstaclePreprocessingDoesNotPublishPartialContext()
    {
        using var cancellation = new CancellationTokenSource();
        var obstacles = CreateObstacles(16);
        var method = typeof(BpmnRoutingOperationContextTests).GetMethod(nameof(CancelDuringRead), BindingFlags.NonPublic | BindingFlags.Static)!;
        var source = method.MakeGenericMethod(Obstacle).Invoke(null, [obstacles, cancellation])!;
        var operation = CreateOperation(source);
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Attempt(operation, "0", "1", 10, 10, cancellation.Token));
        Assert.IsType<OperationCanceledException>(exception.InnerException);
        Assert.Null(operation.GetType().GetField("_preferred", InstanceMembers)!.GetValue(operation));
        var complete = Attempt(operation, "0", "1", 10, 10);
        Assert.Equal(16, Assert.IsAssignableFrom<IEnumerable>(complete.GetType().GetProperty("Obstacles", InstanceMembers)!.GetValue(complete)).Cast<object>().Count());
    }

    [Fact]
    public void CancellationIsObservedEvenWhenCandidateGraphIsReusable()
    {
        var context = Attempt(CreateOperation(CreateObstacles(4)), "0", "1", 10, 10);
        Graph(context, new PointD(0, 0), new PointD(500, 500));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Graph(context, new PointD(0, 0), new PointD(500, 500), cancellation.Token));
        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    private static CancellingList<T> CancelDuringRead<T>(Array source, CancellationTokenSource cancellation) =>
        new CancellingList<T>(source.Cast<T>().ToArray(), cancellation);

    private sealed class CancellingList<T>(T[] values, CancellationTokenSource cancellation) : IReadOnlyList<T>
    {
        public int Count => values.Length;
        public T this[int index]
        {
            get
            {
                if (index == 3)
                {
                    cancellation.Cancel();
                }

                return values[index];
            }
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static Array CreateObstacles(int count)
    {
        var result = Array.CreateInstance(Obstacle, count);
        for (var index = 0; index < count; index++)
        {
            result.SetValue(Activator.CreateInstance(Obstacle,
                new ProjectedObjectId(index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new RectD(20 + (index * 40), 20 + (index * 40), 10, 10)), index);
        }

        return result;
    }

    private static object CreateOperation(object obstacles) => Activator.CreateInstance(
        Router.GetNestedType("OperationContext", BindingFlags.NonPublic)!,
        InstanceMembers, null, [obstacles], null)!;

    private static object Attempt(object operation, string source, string target, double clearance, double lead, CancellationToken token = default) =>
        operation.GetType().GetMethod("GetAttempt", InstanceMembers)!.Invoke(operation,
            [new ProjectedObjectId(source), new ProjectedObjectId(target), clearance, lead, token])!;

    private static object Graph(object context, PointD start, PointD end, CancellationToken token = default) =>
        context.GetType().GetMethod("GetGraph", InstanceMembers)!.Invoke(context, [start, end, token])!;

    private static RectD Bounds(object context, int index)
    {
        var values = (IEnumerable)context.GetType().GetProperty("Obstacles", InstanceMembers)!.GetValue(context)!;
        return (RectD)Obstacle.GetProperty("Bounds")!.GetValue(values.Cast<object>().ElementAt(index))!;
    }
}
