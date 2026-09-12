using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Associates one stable algorithm identity with a Layout Algorithm implementation.
/// </summary>
public sealed class LayoutAlgorithmRegistration
{
    public LayoutAlgorithmRegistration(
        AlgorithmId algorithmId,
        ILayoutAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        ArgumentNullException.ThrowIfNull(algorithm);

        AlgorithmId = algorithmId;
        Algorithm = algorithm;
    }

    public AlgorithmId AlgorithmId { get; }

    public ILayoutAlgorithm Algorithm { get; }
}
