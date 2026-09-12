using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Associates one stable algorithm identity with a Routing Algorithm implementation.
/// </summary>
public sealed class RoutingAlgorithmRegistration
{
    public RoutingAlgorithmRegistration(
        AlgorithmId algorithmId,
        IRoutingAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        ArgumentNullException.ThrowIfNull(algorithm);

        AlgorithmId = algorithmId;
        Algorithm = algorithm;
    }

    public AlgorithmId AlgorithmId { get; }

    public IRoutingAlgorithm Algorithm { get; }
}
