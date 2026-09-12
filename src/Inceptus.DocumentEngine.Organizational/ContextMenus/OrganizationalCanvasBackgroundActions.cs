using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.ContextMenus;

public static class OrganizationalCanvasBackgroundActions
{
    public static CanvasBackgroundActionId AddPoolId { get; } =
        new("inceptus:organizational/background-action/add-pool");

    public static CanvasBackgroundActionDefinition AddPool { get; } =
        new(
            AddPoolId,
            groupLabel: "Add",
            displayName: "Pool",
            request =>
            {
                var poolId = request.IdentityProvider.CreateIdentity().SemanticElementId;
                var mode = OrganizationalSemantics.GetPoolsInScope(
                    request.Document,
                    request.ActiveScopeId).IsEmpty
                    ? OrganizationalPoolCreationMode.AdoptEligibleUnassigned
                    : OrganizationalPoolCreationMode.Empty;
                return new CanvasBackgroundActionPlan(
                    new CreateOrganizationalPoolCommand(
                        request.Document.DocumentId,
                        request.Document.Revision,
                        poolId,
                        request.ActiveScopeId,
                        mode),
                    selectSemanticElementId: poolId);
            },
            order: 100,
            applicability: request =>
                request.Document.SemanticModel.ModelProfiles.IsAvailable(
                    OrganizationalModelProfile.Id) &&
                request.Document.SemanticModel.TryGetScope(
                    request.ActiveScopeId,
                    out _));

    public static ImmutableArray<CanvasBackgroundActionDefinition> Definitions { get; } =
        [AddPool];
}
