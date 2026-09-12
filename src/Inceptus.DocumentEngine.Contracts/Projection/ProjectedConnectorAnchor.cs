using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Immutable, geometry-free connector-anchor data shared by projection, routing and Scene
/// construction. Absolute position remains derived from the owning node bounds.
/// </summary>
public sealed class ProjectedConnectorAnchor : IEquatable<ProjectedConnectorAnchor>
{
    public ProjectedConnectorAnchor(
        ConnectorAnchorId id,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roleCapability,
        int order,
        int sideCount,
        ResolvedConnectorAnchorKind kind)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(side),
                side,
                "The projected connector-anchor side must be defined.");
        }

        PredefinedConnectorAnchorDefinition.ValidateRoles(
            roleCapability,
            nameof(roleCapability));

        if (sideCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sideCount),
                sideCount,
                "The projected connector-anchor side count must be greater than zero.");
        }

        if (order < 0 || order >= sideCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order),
                order,
                "The projected connector-anchor order must identify an anchor on the side.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The projected connector-anchor kind must be defined.");
        }

        if (kind == ResolvedConnectorAnchorKind.Dynamic)
        {
            if (roleCapability is not ConnectorAnchorRoleCapability.Source and
                not ConnectorAnchorRoleCapability.Target)
            {
                throw new ArgumentException(
                    "A dynamic projected connector anchor must allow exactly one persistent connector role.",
                    nameof(roleCapability));
            }

            if (ConnectorAnchorReferenceIdentity.IsPredefinedReference(id))
            {
                throw new ArgumentException(
                    "A dynamic projected connector anchor cannot use the predefined-reference identity namespace.",
                    nameof(id));
            }
        }
        else if (!ConnectorAnchorReferenceIdentity.IsCanonicalPredefinedReference(id))
        {
            throw new ArgumentException(
                "A predefined projected connector-anchor ID must be canonically derived from owner and definition identities.",
                nameof(id));
        }

        Id = id;
        Side = side;
        RoleCapability = roleCapability;
        Order = order;
        SideCount = sideCount;
        Kind = kind;
    }

    public ConnectorAnchorId Id { get; }

    public ConnectorAnchorSide Side { get; }

    public ConnectorAnchorRoleCapability RoleCapability { get; }

    public bool AllowsSource =>
        (RoleCapability & ConnectorAnchorRoleCapability.Source) != 0;

    public bool AllowsTarget =>
        (RoleCapability & ConnectorAnchorRoleCapability.Target) != 0;

    public int Order { get; }

    public int SideCount { get; }

    public ResolvedConnectorAnchorKind Kind { get; }

    public static ProjectedConnectorAnchor FromResolved(
        ResolvedConnectorAnchor anchor,
        int sideCount)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return new ProjectedConnectorAnchor(
            anchor.Id,
            anchor.Side,
            anchor.RoleCapability,
            anchor.Order,
            sideCount,
            anchor.Kind);
    }

    public bool Allows(ConnectorAnchorRole role) => RoleCapability.Allows(role);

    public bool Equals(ProjectedConnectorAnchor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Side == other.Side &&
        RoleCapability == other.RoleCapability &&
        Order == other.Order &&
        SideCount == other.SideCount &&
        Kind == other.Kind;

    public override bool Equals(object? obj) => Equals(obj as ProjectedConnectorAnchor);

    public override int GetHashCode() => HashCode.Combine(
        Id,
        Side,
        RoleCapability,
        Order,
        SideCount,
        Kind);
}

/// <summary>
/// Canonical projected-port encoding for a resolved connector anchor. Consumers use this
/// codec instead of independently interpreting plugin-specific metadata.
/// </summary>
public static class ProjectedConnectorAnchorMetadata
{
    public const string AnchorId = "inceptus.projection:connector-anchor-id";
    public const string Side = "inceptus.projection:connector-anchor-side";
    public const string RoleCapability =
        "inceptus.projection:connector-anchor-role-capability";
    public const string Order = "inceptus.projection:connector-anchor-order";
    public const string SideCount = "inceptus.projection:connector-anchor-side-count";
    public const string Kind = "inceptus.projection:connector-anchor-kind";

    public static PropertyMap Encode(ProjectedConnectorAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return new PropertyMap(
        [
            new(AnchorId, PropertyValue.FromText(anchor.Id.Value)),
            new(Side, PropertyValue.FromInteger((int)anchor.Side)),
            new(RoleCapability, PropertyValue.FromInteger((long)anchor.RoleCapability)),
            new(Order, PropertyValue.FromInteger(anchor.Order)),
            new(SideCount, PropertyValue.FromInteger(anchor.SideCount)),
            new(Kind, PropertyValue.FromInteger((int)anchor.Kind)),
        ]);
    }

    public static bool TryDecode(
        ProjectedPort port,
        out ProjectedConnectorAnchor? anchor)
    {
        ArgumentNullException.ThrowIfNull(port);
        return TryDecode(port.RoutingHints, out anchor);
    }

    public static bool TryDecode(
        PropertyMap properties,
        out ProjectedConnectorAnchor? anchor)
    {
        ArgumentNullException.ThrowIfNull(properties);
        anchor = null;
        if (!TryReadText(properties, AnchorId, out var anchorId) ||
            !TryReadInteger(properties, Side, out var sideValue) ||
            !TryReadInteger(properties, RoleCapability, out var roleCapabilityValue) ||
            !TryReadInteger(properties, Order, out var order) ||
            !TryReadInteger(properties, SideCount, out var sideCount) ||
            !TryReadInteger(properties, Kind, out var kindValue) ||
            sideValue is < int.MinValue or > int.MaxValue ||
            roleCapabilityValue is < int.MinValue or > int.MaxValue ||
            order is < int.MinValue or > int.MaxValue ||
            sideCount is < int.MinValue or > int.MaxValue ||
            kindValue is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        var side = (ConnectorAnchorSide)(int)sideValue;
        var roleCapability = (ConnectorAnchorRoleCapability)(int)roleCapabilityValue;
        var kind = (ResolvedConnectorAnchorKind)(int)kindValue;
        if (!Enum.IsDefined(side) ||
            !Enum.IsDefined(kind) ||
            roleCapability == ConnectorAnchorRoleCapability.None ||
            (roleCapability & ~ConnectorAnchorRoleCapability.SourceOrTarget) != 0 ||
            sideCount <= 0 ||
            order < 0 ||
            order >= sideCount)
        {
            return false;
        }

        try
        {
            anchor = new ProjectedConnectorAnchor(
                new ConnectorAnchorId(anchorId),
                side,
                roleCapability,
                (int)order,
                (int)sideCount,
                kind);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadText(PropertyMap properties, string key, out string value)
    {
        if (properties.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Text)
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadInteger(PropertyMap properties, string key, out long value)
    {
        if (properties.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Integer)
        {
            value = property.IntegerValue;
            return true;
        }

        value = default;
        return false;
    }
}
