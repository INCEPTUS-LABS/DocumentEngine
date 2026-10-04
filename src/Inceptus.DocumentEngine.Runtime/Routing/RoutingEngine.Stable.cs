using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.Routing;

public sealed partial class RoutingEngine
{
    /// <summary>Resolves the optional stable-path policy on this engine's registered algorithm.</summary>
    public bool TryGetStablePolicy(AlgorithmId algorithmId,
        [NotNullWhen(true)] out IStableConnectorRoutingPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        policy = _registry.TryGet(algorithmId, out var registration)
            ? registration!.Algorithm as IStableConnectorRoutingPolicy : null;
        return policy is not null;
    }
}
