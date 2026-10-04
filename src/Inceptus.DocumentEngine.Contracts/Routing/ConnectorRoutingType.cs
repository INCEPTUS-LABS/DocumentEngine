namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>The authored policy used to maintain a connector's complete logical path.</summary>
public enum ConnectorRoutingType
{
    Automatic,
    Straight,
    Manual,
}

public enum ConnectorRoutingOutcome
{
    Path,
    NoRoute,
}

public enum ConnectorNoRouteReason
{
    NoFeasiblePath,
}
