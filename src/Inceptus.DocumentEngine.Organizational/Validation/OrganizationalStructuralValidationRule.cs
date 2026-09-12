using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Validation;

/// <summary>
/// Validates active-scope notation-owned policy on top of the document-wide
/// generic profile-record invariants.
/// </summary>
public sealed class OrganizationalStructuralValidationRule : IModelValidationRule
{
    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    public OrganizationalStructuralValidationRule(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public static ModelValidationRuleId KnownRuleId { get; } =
        new("inceptus:organizational/validation/structural");

    public ModelValidationRuleId RuleId => KnownRuleId;

    public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var document = context.Document;
        var issues = ImmutableArray.CreateBuilder<ModelValidationIssue>();
        var poolsById = document.SemanticModel.Elements
            .Where(static element => element.TypeId == OrganizationalSemanticTypes.Pool)
            .ToDictionary(static element => element.Id);

        foreach (var pool in poolsById.Values
                     .Where(pool => IsInActiveScope(context, pool.Id))
                     .OrderBy(
                         static element => element.Id.Value,
                         StringComparer.Ordinal))
        {
            if (!OrganizationalSemantics.IsPool(pool) ||
                !HasTextProperty(pool, OrganizationalSemanticProperties.Name) ||
                !HasTextProperty(pool, OrganizationalSemanticProperties.Description))
            {
                issues.Add(Error(
                    "ORGANIZATIONAL_POOL_SHAPE_INVALID",
                    $"Organizational Pool '{pool.Id}' must be unattached, scope-contained, and expose text Name and Description properties.",
                    pool.Id));
            }

            if (document.VisualModel.VisualStates.Any(visual =>
                    visual.SemanticElementId == pool.Id))
            {
                issues.Add(Error(
                    "ORGANIZATIONAL_POOL_VISUAL_STATE_INVALID",
                    $"Organizational Pool '{pool.Id}' must not own a persistent Visual State.",
                    pool.Id));
            }
        }

        foreach (var assignment in document.SemanticModel.ProfileAssignments.Where(
                     assignment =>
                         assignment.ProfileId == OrganizationalModelProfile.Id &&
                         IsInActiveScope(context, assignment.SemanticElementId)))
        {
            document.SemanticModel.TryGetElement(
                assignment.SemanticElementId,
                out var source);
            poolsById.TryGetValue(
                assignment.ContainerSemanticElementId,
                out var pool);
            var valid = source is not null &&
                pool is not null &&
                OrganizationalSemantics.IsPool(pool) &&
                OrganizationalSemantics.IsDirectlyAssignable(
                    source,
                    _eligibilityPolicy) &&
                document.SemanticModel.TryGetScope(source.Id, out var sourceScope) &&
                sourceScope is not null &&
                document.SemanticModel.TryGetScope(pool.Id, out var poolScope) &&
                poolScope is not null &&
                sourceScope.Id == poolScope.Id;
            if (!valid)
            {
                issues.Add(Error(
                    "ORGANIZATIONAL_ASSIGNMENT_INVALID",
                    $"Organizational assignment for '{assignment.SemanticElementId}' must join one eligible source to a Pool in the same semantic scope.",
                    assignment.SemanticElementId));
            }
        }

        foreach (var presentation in document.VisualModel.ProfileElementPresentations.Where(
                     presentation =>
                         presentation.ProfileId == OrganizationalModelProfile.Id &&
                         IsInActiveScope(context, presentation.SemanticElementId)))
        {
            if (!poolsById.TryGetValue(presentation.SemanticElementId, out var pool) ||
                !OrganizationalSemantics.IsPool(pool))
            {
                issues.Add(Error(
                    "ORGANIZATIONAL_PRESENTATION_TARGET_INVALID",
                    $"Organizational presentation order targets missing or malformed Pool '{presentation.SemanticElementId}'.",
                    presentation.SemanticElementId));
            }
        }

        foreach (var duplicateOrder in document.VisualModel.ProfileElementPresentations
                     .Where(static presentation =>
                         presentation.ProfileId == OrganizationalModelProfile.Id)
                     .Where(presentation =>
                         IsInActiveScope(context, presentation.SemanticElementId))
                     .Where(presentation =>
                         poolsById.TryGetValue(presentation.SemanticElementId, out var pool) &&
                         OrganizationalSemantics.IsPool(pool))
                     .GroupBy(presentation => new
                     {
                         ScopeId = document.SemanticModel.GetScope(
                             presentation.SemanticElementId).Id,
                         presentation.Order,
                     })
                     .Where(static group => group.Count() > 1))
        {
            foreach (var presentation in duplicateOrder)
            {
                issues.Add(Error(
                    "ORGANIZATIONAL_PRESENTATION_ORDER_DUPLICATE",
                    $"Pool '{presentation.SemanticElementId}' has a duplicate scope-local presentation order.",
                    presentation.SemanticElementId));
            }
        }

        return issues.ToImmutable();
    }

    // An N5 finding belongs to its semantic target's exact Process scope. Missing
    // or document-contained targets remain document/command validation concerns;
    // they must never be attributed to the implicit Main Process as a fallback.
    private static bool IsInActiveScope(
        ModelValidationContext context,
        SemanticElementId semanticElementId) =>
        context.Document.SemanticModel.TryGetScope(semanticElementId, out var scope) &&
        scope is not null &&
        scope.Id == context.ActiveScopeId;

    private static bool HasTextProperty(
        SemanticElementSnapshot element,
        string propertyKey) =>
        element.Properties.TryGetValue(propertyKey, out var value) &&
        value.Kind == PropertyValueKind.Text;

    private static ModelValidationIssue Error(
        string code,
        string message,
        SemanticElementId semanticElementId) =>
        new(
            KnownRuleId,
            ModelValidationSeverity.Error,
            code,
            message,
            ModelValidationTarget.ForSemanticElement(semanticElementId));
}
