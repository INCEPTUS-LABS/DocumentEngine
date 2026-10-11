using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Organizational.ContextMenus;

public static class OrganizationalPoolActions
{
    public static SemanticSceneViewActionId ExpandId { get; } =
        new("inceptus:organizational/scene-view-action/expand-pool");

    public static SemanticSceneViewActionId CollapseId { get; } =
        new("inceptus:organizational/scene-view-action/collapse-pool");

    public static SemanticSceneCommandActionId MoveUpId { get; } =
        new("inceptus:organizational/scene-command-action/move-pool-up");

    public static SemanticSceneCommandActionId MoveDownId { get; } =
        new("inceptus:organizational/scene-command-action/move-pool-down");

    public static SemanticSceneCommandActionId MoveLaneUpId { get; } =
        new("inceptus:organizational/scene-command-action/move-lane-up");

    public static SemanticSceneCommandActionId MoveLaneDownId { get; } =
        new("inceptus:organizational/scene-command-action/move-lane-down");

    public static SemanticSceneViewActionDefinition Expand { get; } = new(
        ExpandId,
        "Expand",
        request => new SemanticSceneViewActionPlan(
            OrganizationalModelProfile.Id,
            request.TargetSemanticElementId,
            isCollapsed: false),
        order: 100,
        applicability: request => ViewActionIsApplicable(
            request,
            isCurrentlyCollapsed: true));

    public static SemanticSceneViewActionDefinition Collapse { get; } = new(
        CollapseId,
        "Collapse",
        request => new SemanticSceneViewActionPlan(
            OrganizationalModelProfile.Id,
            request.TargetSemanticElementId,
            isCollapsed: true),
        order: 100,
        applicability: request => ViewActionIsApplicable(
            request,
            isCurrentlyCollapsed: false));

    public static SemanticSceneCommandActionDefinition MoveUp { get; } =
        CreateMoveAction(
            MoveUpId,
            "Move up",
            OrganizationalPoolMoveDirection.Up,
            order: 200);

    public static SemanticSceneCommandActionDefinition MoveDown { get; } =
        CreateMoveAction(
            MoveDownId,
            "Move down",
            OrganizationalPoolMoveDirection.Down,
            order: 300);

    public static ImmutableArray<SemanticSceneViewActionDefinition> ViewDefinitions
    { get; } = [Expand, Collapse];

    public static ImmutableArray<SemanticSceneCommandActionDefinition> CommandDefinitions
    { get; } = [MoveUp, MoveDown,
        CreateMoveAction(MoveLaneUpId, "Move Lane Up", OrganizationalPoolMoveDirection.Up, 400, laneOnly: true),
        CreateMoveAction(MoveLaneDownId, "Move Lane Down", OrganizationalPoolMoveDirection.Down, 500, laneOnly: true)];

    private static SemanticSceneCommandActionDefinition CreateMoveAction(
        SemanticSceneCommandActionId id,
        string displayName,
        OrganizationalPoolMoveDirection direction,
        int order,
        bool laneOnly = false) =>
        new(
            id,
            displayName,
            request => new SemanticSceneCommandActionPlan(
                laneOnly ? new MoveOrganizationalLaneCommand(
                    request.Document.DocumentId, request.Document.Revision, request.TargetSemanticElementId, direction)
                : new MoveOrganizationalPoolCommand(
                    request.Document.DocumentId,
                    request.Document.Revision,
                    request.TargetSemanticElementId,
                    direction),
                request.TargetSemanticElementId),
            order,
            request => CommandActionIsApplicable(request, direction, laneOnly));

    private static bool ViewActionIsApplicable(
        SemanticSceneViewActionRequest request,
        bool isCurrentlyCollapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsExactActivePool(
                request.Document,
                request.ActiveScopeId,
                request.TargetSemanticElementId) &&
            request.ModelProfileViewState.IsEffectivelyVisible(
                OrganizationalModelProfile.Id,
                request.Document.SemanticModel.ModelProfiles) &&
            request.ModelProfileElementViewState.IsCollapsed(
                OrganizationalModelProfile.Id,
                request.TargetSemanticElementId) == isCurrentlyCollapsed;
    }

    private static bool CommandActionIsApplicable(
        SemanticSceneCommandActionRequest request,
        OrganizationalPoolMoveDirection direction,
        bool laneOnly)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Document.SemanticModel.ModelProfiles.IsAvailable(
                OrganizationalModelProfile.Id) ||
            !IsExactActivePool(
                request.Document,
                request.ActiveScopeId,
                request.TargetSemanticElementId))
        {
            return false;
        }

        return OrganizationalPoolOrdering.CanMove(request.Document, request.ActiveScopeId,
            request.TargetSemanticElementId, direction, laneOnly);
    }

    private static bool IsExactActivePool(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        SemanticElementId poolId) =>
        document.SemanticModel.TryGetElement(poolId, out var pool) &&
        pool is not null &&
        OrganizationalSemantics.IsPool(pool) &&
        document.SemanticModel.GetScope(pool.Id).Id == activeScopeId;
}
