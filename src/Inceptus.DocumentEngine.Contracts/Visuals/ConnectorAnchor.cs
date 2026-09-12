using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Identifies the activity edge that owns one persistent connector anchor.
/// </summary>
public enum ConnectorAnchorSide
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3,
}

/// <summary>
/// Identifies whether an anchor accepts a connector's source or target endpoint.
/// </summary>
public enum ConnectorAnchorRole
{
    Source = 0,
    Target = 1,
}

/// <summary>
/// Describes one persistent, route-independent connector anchor. Its absolute
/// document position is derived from the owning Visual State bounds and its
/// per-side order.
/// </summary>
public sealed class ConnectorAnchor : IEquatable<ConnectorAnchor>
{
    public ConnectorAnchor(
        ConnectorAnchorId id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int order)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(side),
                side,
                "The connector-anchor side must be defined.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "The connector-anchor role must be defined.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(order);

        Id = id;
        Side = side;
        Role = role;
        Order = order;
    }

    public ConnectorAnchorId Id { get; }

    public ConnectorAnchorSide Side { get; }

    public ConnectorAnchorRole Role { get; }

    /// <summary>
    /// Gets the zero-based persistent order among all Source and Target anchors
    /// on the same side.
    /// </summary>
    public int Order { get; }

    public bool Equals(ConnectorAnchor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Side == other.Side &&
        Role == other.Role &&
        Order == other.Order;

    public override bool Equals(object? obj) => Equals(obj as ConnectorAnchor);

    public override int GetHashCode() => HashCode.Combine(Id, Side, Role, Order);

    public static bool operator ==(ConnectorAnchor? left, ConnectorAnchor? right) =>
        EqualityComparer<ConnectorAnchor>.Default.Equals(left, right);

    public static bool operator !=(ConnectorAnchor? left, ConnectorAnchor? right) =>
        !(left == right);
}
