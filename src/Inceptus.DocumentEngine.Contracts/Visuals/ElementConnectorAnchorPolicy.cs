using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public enum ConnectorAnchorPolicyMode
{
    Disabled = 0,
    DynamicUnlimited = 1,
    DynamicSingle = 2,
    Predefined = 3,
}

[Flags]
public enum ConnectorAnchorRoleCapability
{
    None = 0,
    Source = 1,
    Target = 2,
    SourceOrTarget = Source | Target,
}

public sealed class PredefinedConnectorAnchorDefinition :
    IEquatable<PredefinedConnectorAnchorDefinition>
{
    public PredefinedConnectorAnchorDefinition(
        PredefinedConnectorAnchorDefinitionId id,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roleCapability,
        int order)
    {
        ArgumentNullException.ThrowIfNull(id);
        ValidateSide(side);
        ValidateRoles(roleCapability, nameof(roleCapability));
        ArgumentOutOfRangeException.ThrowIfNegative(order);

        Id = id;
        Side = side;
        RoleCapability = roleCapability;
        Order = order;
    }

    public PredefinedConnectorAnchorDefinitionId Id { get; }

    public ConnectorAnchorSide Side { get; }

    public ConnectorAnchorRoleCapability RoleCapability { get; }

    public int Order { get; }

    public bool Allows(ConnectorAnchorRole role) => RoleCapability.Allows(role);

    public bool Equals(PredefinedConnectorAnchorDefinition? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Side == other.Side &&
        RoleCapability == other.RoleCapability &&
        Order == other.Order;

    public override bool Equals(object? obj) =>
        Equals(obj as PredefinedConnectorAnchorDefinition);

    public override int GetHashCode() => HashCode.Combine(Id, Side, RoleCapability, Order);

    private static void ValidateSide(ConnectorAnchorSide side)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(side),
                side,
                "The predefined connector-anchor side must be defined.");
        }
    }

    internal static void ValidateRoles(
        ConnectorAnchorRoleCapability roles,
        string parameterName)
    {
        if (roles == ConnectorAnchorRoleCapability.None ||
            (roles & ~ConnectorAnchorRoleCapability.SourceOrTarget) != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                roles,
                "Connector-anchor role capability must allow Source, Target, or both.");
        }
    }
}

public sealed class EdgeConnectorAnchorPolicy : IEquatable<EdgeConnectorAnchorPolicy>
{
    private EdgeConnectorAnchorPolicy(
        ConnectorAnchorPolicyMode mode,
        ConnectorAnchorRoleCapability allowedRoles,
        IEnumerable<PredefinedConnectorAnchorDefinition>? predefinedAnchors)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The connector-anchor policy mode must be defined.");
        }

        var definitions = CopyDefinitions(predefinedAnchors);
        if (mode is ConnectorAnchorPolicyMode.DynamicUnlimited or
            ConnectorAnchorPolicyMode.DynamicSingle)
        {
            PredefinedConnectorAnchorDefinition.ValidateRoles(
                allowedRoles,
                nameof(allowedRoles));
            if (!definitions.IsEmpty)
            {
                throw new ArgumentException(
                    "Dynamic connector-anchor policies cannot contain predefined definitions.",
                    nameof(predefinedAnchors));
            }
        }
        else if (allowedRoles != ConnectorAnchorRoleCapability.None)
        {
            throw new ArgumentException(
                "Disabled and Predefined edge policies do not declare edge-wide dynamic roles.",
                nameof(allowedRoles));
        }

        if (mode == ConnectorAnchorPolicyMode.Predefined && definitions.IsEmpty)
        {
            throw new ArgumentException(
                "A Predefined edge policy must contain at least one definition.",
                nameof(predefinedAnchors));
        }

        if (mode != ConnectorAnchorPolicyMode.Predefined && !definitions.IsEmpty)
        {
            throw new ArgumentException(
                "Only a Predefined edge policy may contain predefined definitions.",
                nameof(predefinedAnchors));
        }

        Mode = mode;
        AllowedRoles = allowedRoles;
        PredefinedAnchors = definitions;
    }

    public static EdgeConnectorAnchorPolicy Disabled { get; } =
        new(ConnectorAnchorPolicyMode.Disabled, ConnectorAnchorRoleCapability.None, null);

    public ConnectorAnchorPolicyMode Mode { get; }

    public ConnectorAnchorRoleCapability AllowedRoles { get; }

    public ImmutableArray<PredefinedConnectorAnchorDefinition> PredefinedAnchors { get; }

    public static EdgeConnectorAnchorPolicy DynamicUnlimited(
        ConnectorAnchorRoleCapability allowedRoles =
            ConnectorAnchorRoleCapability.SourceOrTarget) =>
        new(ConnectorAnchorPolicyMode.DynamicUnlimited, allowedRoles, null);

    public static EdgeConnectorAnchorPolicy DynamicSingle(
        ConnectorAnchorRoleCapability allowedRoles =
            ConnectorAnchorRoleCapability.SourceOrTarget) =>
        new(ConnectorAnchorPolicyMode.DynamicSingle, allowedRoles, null);

    public static EdgeConnectorAnchorPolicy Predefined(
        IEnumerable<PredefinedConnectorAnchorDefinition> definitions) =>
        new(
            ConnectorAnchorPolicyMode.Predefined,
            ConnectorAnchorRoleCapability.None,
            definitions);

    public bool Allows(ConnectorAnchorRole role) =>
        (Mode is ConnectorAnchorPolicyMode.DynamicUnlimited or
            ConnectorAnchorPolicyMode.DynamicSingle) &&
        AllowedRoles.Allows(role);

    public bool Equals(EdgeConnectorAnchorPolicy? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Mode == other.Mode &&
        AllowedRoles == other.AllowedRoles &&
        PredefinedAnchors.AsSpan().SequenceEqual(other.PredefinedAnchors.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as EdgeConnectorAnchorPolicy);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        hash.Add(AllowedRoles);
        foreach (var definition in PredefinedAnchors)
        {
            hash.Add(definition);
        }

        return hash.ToHashCode();
    }

    private static ImmutableArray<PredefinedConnectorAnchorDefinition> CopyDefinitions(
        IEnumerable<PredefinedConnectorAnchorDefinition>? definitions)
    {
        if (definitions is null)
        {
            return [];
        }

        var copy = definitions.ToArray();
        if (Array.Exists(copy, static definition => definition is null))
        {
            throw new ArgumentException(
                "Predefined connector-anchor definitions cannot contain null values.",
                nameof(definitions));
        }

        var duplicate = copy
            .GroupBy(static definition => definition.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Predefined connector-anchor definition ID '{duplicate.Key}' occurs more than once.",
                nameof(definitions));
        }

        var ordered = copy
            .OrderBy(static definition => definition.Order)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Order != index)
            {
                throw new ArgumentException(
                    "Predefined connector-anchor definition order must be contiguous from zero.",
                    nameof(definitions));
            }
        }

        return [.. ordered];
    }
}

public sealed class ElementConnectorAnchorPolicy : IEquatable<ElementConnectorAnchorPolicy>
{
    public ElementConnectorAnchorPolicy(
        EdgeConnectorAnchorPolicy top,
        EdgeConnectorAnchorPolicy right,
        EdgeConnectorAnchorPolicy bottom,
        EdgeConnectorAnchorPolicy left)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(bottom);
        ArgumentNullException.ThrowIfNull(left);

        ValidateDefinitions(top, ConnectorAnchorSide.Top, nameof(top));
        ValidateDefinitions(right, ConnectorAnchorSide.Right, nameof(right));
        ValidateDefinitions(bottom, ConnectorAnchorSide.Bottom, nameof(bottom));
        ValidateDefinitions(left, ConnectorAnchorSide.Left, nameof(left));
        var duplicate = new[] { top, right, bottom, left }
            .SelectMany(static edge => edge.PredefinedAnchors)
            .GroupBy(static definition => definition.Id)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Predefined connector-anchor definition ID '{duplicate.Key}' occurs on more than one edge.",
                nameof(top));
        }

        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    public static ElementConnectorAnchorPolicy DynamicUnlimitedAllEdges { get; } = new(
        EdgeConnectorAnchorPolicy.DynamicUnlimited(),
        EdgeConnectorAnchorPolicy.DynamicUnlimited(),
        EdgeConnectorAnchorPolicy.DynamicUnlimited(),
        EdgeConnectorAnchorPolicy.DynamicUnlimited());

    public EdgeConnectorAnchorPolicy Top { get; }

    public EdgeConnectorAnchorPolicy Right { get; }

    public EdgeConnectorAnchorPolicy Bottom { get; }

    public EdgeConnectorAnchorPolicy Left { get; }

    public EdgeConnectorAnchorPolicy ForSide(ConnectorAnchorSide side) => side switch
    {
        ConnectorAnchorSide.Top => Top,
        ConnectorAnchorSide.Right => Right,
        ConnectorAnchorSide.Bottom => Bottom,
        ConnectorAnchorSide.Left => Left,
        _ => throw new ArgumentOutOfRangeException(
            nameof(side),
            side,
            "The connector-anchor side must be defined."),
    };

    public bool Equals(ElementConnectorAnchorPolicy? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Top.Equals(other.Top) &&
        Right.Equals(other.Right) &&
        Bottom.Equals(other.Bottom) &&
        Left.Equals(other.Left);

    public override bool Equals(object? obj) => Equals(obj as ElementConnectorAnchorPolicy);

    public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);

    private static void ValidateDefinitions(
        EdgeConnectorAnchorPolicy policy,
        ConnectorAnchorSide side,
        string parameterName)
    {
        if (policy.PredefinedAnchors.Any(definition => definition.Side != side))
        {
            throw new ArgumentException(
                $"Predefined connector-anchor definitions for '{side}' must declare the same side.",
                parameterName);
        }
    }
}

internal static class ConnectorAnchorRoleCapabilityExtensions
{
    internal static bool Allows(
        this ConnectorAnchorRoleCapability capability,
        ConnectorAnchorRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "The connector-anchor role must be defined.");
        }

        return role switch
        {
            ConnectorAnchorRole.Source =>
                (capability & ConnectorAnchorRoleCapability.Source) != 0,
            ConnectorAnchorRole.Target =>
                (capability & ConnectorAnchorRoleCapability.Target) != 0,
            _ => false,
        };
    }
}
