using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>The mode change owned by one History action; it contains no path or priority.</summary>
public sealed record ConnectorRoutingTypeHistoryDelta
{
    public ConnectorRoutingTypeHistoryDelta(VisualStateId visualStateId,
        ConnectorRoutingType beforeType, ConnectorRoutingType afterType)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        if (!Enum.IsDefined(beforeType)) { throw new ArgumentOutOfRangeException(nameof(beforeType)); }
        if (!Enum.IsDefined(afterType)) { throw new ArgumentOutOfRangeException(nameof(afterType)); }
        if (beforeType == afterType) { throw new ArgumentException("A type delta requires different values.", nameof(afterType)); }
        VisualStateId = visualStateId; BeforeType = beforeType; AfterType = afterType;
    }
    public VisualStateId VisualStateId { get; }
    public ConnectorRoutingType BeforeType { get; }
    public ConnectorRoutingType AfterType { get; }
}
