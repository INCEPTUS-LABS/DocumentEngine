using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Routing;

public sealed class RoutingAlgorithmContractTests
{
    [Fact]
    public void ContextDefensivelyCopiesCanonicallyOrdersAndStructurallyCompares()
    {
        var options = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:z", PropertyValue.FromInteger(2)),
            new("test:a", PropertyValue.FromInteger(1)),
        };
        var context = new RoutingContext(options);
        options.Clear();
        var same = new RoutingContext(
        [
            new("test:a", PropertyValue.FromInteger(1)),
            new("test:z", PropertyValue.FromInteger(2)),
        ]);

        Assert.Equal(["test:a", "test:z"], context.Options.Keys);
        Assert.Equal(context, same);
        Assert.Equal(context.GetHashCode(), same.GetHashCode());
        Assert.Equal(new RoutingContext(), RoutingContext.Empty);
        Assert.DoesNotContain(
            typeof(IDictionary<string, PropertyValue>),
            context.Options.GetType().GetInterfaces());
    }

    [Fact]
    public void RegistrationRequiresAndPreservesExplicitStableIdentityAndAlgorithm()
    {
        var algorithmId = new AlgorithmId("test:routing:explicit");
        var algorithm = new EmptyAlgorithm();
        var registration = new RoutingAlgorithmRegistration(algorithmId, algorithm);

        Assert.Same(algorithmId, registration.AlgorithmId);
        Assert.Same(algorithm, registration.Algorithm);
        Assert.Throws<ArgumentNullException>(() =>
            new RoutingAlgorithmRegistration(null!, algorithm));
        Assert.Throws<ArgumentNullException>(() =>
            new RoutingAlgorithmRegistration(algorithmId, null!));
    }

    [Fact]
    public void AlgorithmContractUsesOnlyImmutableUpstreamInputs()
    {
        var method = Assert.Single(typeof(IRoutingAlgorithm).GetMethods());

        Assert.Equal(nameof(IRoutingAlgorithm.Route), method.Name);
        Assert.Equal(typeof(RoutingAlgorithmResult), method.ReturnType);
        Assert.Equal(
            [typeof(ProjectedGraph), typeof(LayoutResult), typeof(RoutingContext), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private sealed class EmptyAlgorithm : IRoutingAlgorithm
    {
        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return RoutingAlgorithmResult.Success(RoutingComputation.Empty);
        }
    }
}
