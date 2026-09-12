using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Layout;

public sealed class LayoutAlgorithmContractTests
{
    [Fact]
    public void ContextDefensivelyCopiesCanonicallyOrdersAndStructurallyCompares()
    {
        var options = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:z", PropertyValue.FromInteger(2)),
            new("test:a", PropertyValue.FromInteger(1)),
        };
        var context = new LayoutContext(options);
        options.Clear();
        var same = new LayoutContext(
        [
            new("test:a", PropertyValue.FromInteger(1)),
            new("test:z", PropertyValue.FromInteger(2)),
        ]);

        Assert.Equal(["test:a", "test:z"], context.Options.Keys);
        Assert.Equal(context, same);
        Assert.Equal(context.GetHashCode(), same.GetHashCode());
        Assert.Equal(new LayoutContext(), LayoutContext.Empty);
        Assert.DoesNotContain(
            typeof(IDictionary<string, PropertyValue>),
            context.Options.GetType().GetInterfaces());
    }

    [Fact]
    public void RegistrationRequiresAndPreservesExplicitStableIdentityAndAlgorithm()
    {
        var algorithmId = new AlgorithmId("test:layout:explicit");
        var algorithm = new EmptyAlgorithm();
        var registration = new LayoutAlgorithmRegistration(algorithmId, algorithm);

        Assert.Same(algorithmId, registration.AlgorithmId);
        Assert.Same(algorithm, registration.Algorithm);
        Assert.Throws<ArgumentNullException>(() =>
            new LayoutAlgorithmRegistration(null!, algorithm));
        Assert.Throws<ArgumentNullException>(() =>
            new LayoutAlgorithmRegistration(algorithmId, null!));
    }

    private sealed class EmptyAlgorithm : ILayoutAlgorithm
    {
        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return LayoutAlgorithmResult.Success(LayoutComputation.Empty);
        }
    }
}
