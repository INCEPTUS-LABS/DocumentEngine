using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.Organizational.Semantics;

/// <summary>
/// Read-only Organizational queries shared by commands, Scene contribution, and hosts.
/// </summary>
public static class OrganizationalSemantics
{
    public static bool IsPool(SemanticElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.TypeId == OrganizationalSemanticTypes.Pool &&
            element.ContainmentKind == SemanticElementContainmentKind.Scope &&
            element.AttachedToElementId is null;
    }

    public static ImmutableArray<SemanticElementSnapshot> GetPoolsInScope(
        DocumentSnapshot document,
        DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scopeId);
        if (!document.SemanticModel.TryGetScope(scopeId, out _))
        {
            return [];
        }

        return document.SemanticModel.Elements
            .Where(IsPool)
            .Where(pool => document.SemanticModel.GetScope(pool.Id).Id == scopeId)
            .OrderBy(static pool => pool.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public static ImmutableArray<SemanticElementSnapshot> GetOrderedPoolsInScope(
        DocumentSnapshot document,
        DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var pools = GetPoolsInScope(document, scopeId);
        var orderByPoolId = document.VisualModel.ProfileElementPresentations
            .Where(static presentation =>
                presentation.ProfileId == OrganizationalModelProfile.Id)
            .ToDictionary(
                static presentation => presentation.SemanticElementId,
                static presentation => presentation.Order);

        // A partial legacy order has no complete authority. Fall back for the entire
        // scope so the result remains stable and collision-free until the next create/reorder.
        return pools.All(pool => orderByPoolId.ContainsKey(pool.Id))
            ? pools.OrderBy(pool => orderByPoolId[pool.Id])
                .ThenBy(static pool => pool.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray()
            : pools;
    }

    public static bool TryGetAssignedPoolId(
        ISemanticModelView semanticModel,
        SemanticElementId semanticElementId,
        out SemanticElementId? poolId)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        var assignment = semanticModel.ProfileAssignments.FirstOrDefault(candidate =>
            candidate.ProfileId == OrganizationalModelProfile.Id &&
            candidate.SemanticElementId == semanticElementId);
        poolId = assignment?.ContainerSemanticElementId;
        return poolId is not null;
    }

    public static ImmutableArray<SemanticElementSnapshot> GetAssignedElements(
        DocumentSnapshot document,
        SemanticElementId poolId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(poolId);
        var assignedIds = document.SemanticModel.ProfileAssignments
            .Where(assignment =>
                assignment.ProfileId == OrganizationalModelProfile.Id &&
                assignment.ContainerSemanticElementId == poolId)
            .Select(static assignment => assignment.SemanticElementId)
            .ToHashSet();
        return document.SemanticModel.Elements
            .Where(element => assignedIds.Contains(element.Id))
            .OrderBy(static element => element.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public static bool IsDirectlyAssignable(
        SemanticElementSnapshot element,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        return element.ContainmentKind == SemanticElementContainmentKind.Scope &&
            element.AttachedToElementId is null &&
            eligibilityPolicy.IsEligible(element);
    }
}
