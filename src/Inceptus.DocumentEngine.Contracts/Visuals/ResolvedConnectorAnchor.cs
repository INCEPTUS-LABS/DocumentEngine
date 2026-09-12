using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public enum ResolvedConnectorAnchorKind
{
    Dynamic = 0,
    Predefined = 1,
}

public sealed class ResolvedConnectorAnchor : IEquatable<ResolvedConnectorAnchor>
{
    public ResolvedConnectorAnchor(
        ConnectorAnchorId id,
        VisualStateId ownerVisualStateId,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roleCapability,
        int order,
        ResolvedConnectorAnchorKind kind,
        PredefinedConnectorAnchorDefinitionId? definitionId = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ownerVisualStateId);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "The side must be defined.");
        }

        PredefinedConnectorAnchorDefinition.ValidateRoles(
            roleCapability,
            nameof(roleCapability));
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind must be defined.");
        }

        if (kind == ResolvedConnectorAnchorKind.Dynamic)
        {
            if (roleCapability is not ConnectorAnchorRoleCapability.Source and
                not ConnectorAnchorRoleCapability.Target)
            {
                throw new ArgumentException(
                    "A dynamic resolved anchor must allow exactly one persistent connector role.",
                    nameof(roleCapability));
            }

            if (ConnectorAnchorReferenceIdentity.IsPredefinedReference(id))
            {
                throw new ArgumentException(
                    "A dynamic resolved anchor cannot use the predefined-reference identity namespace.",
                    nameof(id));
            }

            if (definitionId is not null)
            {
                throw new ArgumentException(
                    "A dynamic resolved anchor cannot identify a predefined definition.",
                    nameof(definitionId));
            }
        }
        else
        {
            if (definitionId is null)
            {
                throw new ArgumentException(
                    "A predefined resolved anchor must identify its predefined definition.",
                    nameof(definitionId));
            }

            var canonicalId = ConnectorAnchorReferenceIdentity.ForPredefined(
                ownerVisualStateId,
                definitionId);
            if (id != canonicalId)
            {
                throw new ArgumentException(
                    "A predefined resolved anchor ID must be derived from its owner and definition identities.",
                    nameof(id));
            }
        }

        Id = id;
        OwnerVisualStateId = ownerVisualStateId;
        Side = side;
        RoleCapability = roleCapability;
        Order = order;
        Kind = kind;
        DefinitionId = definitionId;
    }

    public ConnectorAnchorId Id { get; }

    public VisualStateId OwnerVisualStateId { get; }

    public ConnectorAnchorSide Side { get; }

    public ConnectorAnchorRoleCapability RoleCapability { get; }

    public int Order { get; }

    public ResolvedConnectorAnchorKind Kind { get; }

    public PredefinedConnectorAnchorDefinitionId? DefinitionId { get; }

    public bool IsDynamic => Kind == ResolvedConnectorAnchorKind.Dynamic;

    public bool Allows(ConnectorAnchorRole role) => RoleCapability.Allows(role);

    public bool Equals(ResolvedConnectorAnchor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        OwnerVisualStateId == other.OwnerVisualStateId &&
        Side == other.Side &&
        RoleCapability == other.RoleCapability &&
        Order == other.Order &&
        Kind == other.Kind &&
        DefinitionId == other.DefinitionId;

    public override bool Equals(object? obj) => Equals(obj as ResolvedConnectorAnchor);

    public override int GetHashCode() => HashCode.Combine(
        Id,
        OwnerVisualStateId,
        Side,
        RoleCapability,
        Order,
        Kind,
        DefinitionId);
}

public static class ConnectorAnchorReferenceIdentity
{
    private const string PredefinedPrefix = "inceptus:predefined-connector-anchor:";

    public static ConnectorAnchorId ForPredefined(
        VisualStateId ownerVisualStateId,
        PredefinedConnectorAnchorDefinitionId definitionId)
    {
        ArgumentNullException.ThrowIfNull(ownerVisualStateId);
        ArgumentNullException.ThrowIfNull(definitionId);
        return new ConnectorAnchorId(string.Concat(
            PredefinedPrefix,
            ownerVisualStateId.Value.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            ownerVisualStateId.Value,
            ":",
            definitionId.Value.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            definitionId.Value));
    }

    public static bool IsPredefinedReference(ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(anchorId);
        return anchorId.Value.StartsWith(PredefinedPrefix, StringComparison.Ordinal);
    }

    internal static bool IsCanonicalPredefinedReference(ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(anchorId);
        if (!IsPredefinedReference(anchorId))
        {
            return false;
        }

        var remaining = anchorId.Value.AsSpan(PredefinedPrefix.Length);
        if (!TryReadLength(ref remaining, out var ownerLength) ||
            remaining.Length <= ownerLength ||
            remaining[ownerLength] != ':')
        {
            return false;
        }

        var ownerValue = remaining[..ownerLength].ToString();
        remaining = remaining[(ownerLength + 1)..];
        if (!TryReadLength(ref remaining, out var definitionLength) ||
            remaining.Length != definitionLength)
        {
            return false;
        }

        try
        {
            var ownerId = new VisualStateId(ownerValue);
            var definitionId = new PredefinedConnectorAnchorDefinitionId(
                remaining.ToString());
            return ForPredefined(ownerId, definitionId) == anchorId;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadLength(
        ref ReadOnlySpan<char> remaining,
        out int length)
    {
        var separator = remaining.IndexOf(':');
        if (separator <= 0 ||
            !int.TryParse(
                remaining[..separator],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out length) ||
            length <= 0)
        {
            length = 0;
            return false;
        }

        remaining = remaining[(separator + 1)..];
        return true;
    }
}

public static class ElementConnectorAnchorResolver
{
    public static ImmutableArray<ResolvedConnectorAnchor> Resolve(
        VisualStateSnapshot visualState,
        SemanticTypeId elementTypeId,
        IElementConnectorAnchorPolicyProvider? policyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(visualState);
        ArgumentNullException.ThrowIfNull(elementTypeId);
        var policy = (policyProvider ?? ElementConnectorAnchorPolicyRegistry.Default)
            .Resolve(elementTypeId);
        return Resolve(visualState, policy);
    }

    public static ImmutableArray<ResolvedConnectorAnchor> Resolve(
        VisualStateSnapshot visualState,
        ElementConnectorAnchorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(visualState);
        ArgumentNullException.ThrowIfNull(policy);
        var resolved = ImmutableArray.CreateBuilder<ResolvedConnectorAnchor>();
        foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
        {
            var edgePolicy = policy.ForSide(side);
            var dynamicAnchors = visualState.ConnectorAnchors
                .Where(anchor => anchor.Side == side)
                .OrderBy(static anchor => anchor.Order)
                .ToArray();
            switch (edgePolicy.Mode)
            {
                case ConnectorAnchorPolicyMode.Disabled:
                    if (dynamicAnchors.Length != 0)
                    {
                        throw InvalidState(visualState, side, "Disabled edges cannot own dynamic anchors.");
                    }

                    break;
                case ConnectorAnchorPolicyMode.DynamicUnlimited:
                case ConnectorAnchorPolicyMode.DynamicSingle:
                    if (edgePolicy.Mode == ConnectorAnchorPolicyMode.DynamicSingle &&
                        dynamicAnchors.Length > 1)
                    {
                        throw InvalidState(visualState, side, "DynamicSingle edges cannot own more than one anchor.");
                    }

                    foreach (var anchor in dynamicAnchors)
                    {
                        if (ConnectorAnchorReferenceIdentity.IsPredefinedReference(anchor.Id))
                        {
                            throw InvalidState(
                                visualState,
                                side,
                                "Dynamic anchors cannot use the reserved predefined-reference identity namespace.");
                        }

                        if (!edgePolicy.Allows(anchor.Role))
                        {
                            throw InvalidState(visualState, side, "A dynamic anchor uses a role prohibited by policy.");
                        }

                        resolved.Add(new ResolvedConnectorAnchor(
                            anchor.Id,
                            visualState.Id,
                            anchor.Side,
                            anchor.Role == ConnectorAnchorRole.Source
                                ? ConnectorAnchorRoleCapability.Source
                                : ConnectorAnchorRoleCapability.Target,
                            anchor.Order,
                            ResolvedConnectorAnchorKind.Dynamic));
                    }

                    break;
                case ConnectorAnchorPolicyMode.Predefined:
                    if (dynamicAnchors.Length != 0)
                    {
                        throw InvalidState(visualState, side, "Predefined edges cannot own dynamic anchors.");
                    }

                    foreach (var definition in edgePolicy.PredefinedAnchors)
                    {
                        resolved.Add(new ResolvedConnectorAnchor(
                            ConnectorAnchorReferenceIdentity.ForPredefined(
                                visualState.Id,
                                definition.Id),
                            visualState.Id,
                            definition.Side,
                            definition.RoleCapability,
                            definition.Order,
                            ResolvedConnectorAnchorKind.Predefined,
                            definition.Id));
                    }

                    break;
                default:
                    throw new InvalidOperationException("The connector-anchor policy mode is invalid.");
            }
        }

        return resolved.ToImmutable();
    }

    private static InvalidOperationException InvalidState(
        VisualStateSnapshot visualState,
        ConnectorAnchorSide side,
        string detail) =>
        new($"Visual state '{visualState.Id}' is incompatible with connector-anchor policy on side '{side}'. {detail}");
}

public static class ElementConnectorAnchorPolicyEvaluator
{
    public static bool CanAdd(
        ElementConnectorAnchorPolicy policy,
        VisualStateSnapshot visualState,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(visualState);
        var edgePolicy = policy.ForSide(side);
        if (!edgePolicy.Allows(role))
        {
            return false;
        }

        var sideAnchors = visualState.ConnectorAnchors
            .Where(anchor => anchor.Side == side)
            .ToArray();
        if (sideAnchors.Any(anchor => !edgePolicy.Allows(anchor.Role)))
        {
            return false;
        }

        return edgePolicy.Mode switch
        {
            ConnectorAnchorPolicyMode.DynamicUnlimited => true,
            ConnectorAnchorPolicyMode.DynamicSingle => sideAnchors.Length == 0,
            _ => false,
        };
    }

    public static bool CanRemove(
        ElementConnectorAnchorPolicy policy,
        VisualStateSnapshot visualState,
        ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(visualState);
        ArgumentNullException.ThrowIfNull(anchorId);
        var anchor = visualState.ConnectorAnchors.SingleOrDefault(candidate =>
            candidate.Id == anchorId);
        if (anchor is null)
        {
            return false;
        }

        var edgePolicy = policy.ForSide(anchor.Side);
        if (!edgePolicy.Allows(anchor.Role))
        {
            return false;
        }

        var sideCount = visualState.ConnectorAnchors.Count(candidate =>
            candidate.Side == anchor.Side);
        return edgePolicy.Mode switch
        {
            ConnectorAnchorPolicyMode.DynamicUnlimited => true,
            ConnectorAnchorPolicyMode.DynamicSingle => sideCount == 1,
            _ => false,
        };
    }
}
