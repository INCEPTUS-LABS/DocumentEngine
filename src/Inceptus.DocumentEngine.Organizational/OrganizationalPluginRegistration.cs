using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.ContextMenus;
using Inceptus.DocumentEngine.Organizational.Deletion;
using Inceptus.DocumentEngine.Organizational.History;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Projection;
using Inceptus.DocumentEngine.Organizational.Properties;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Spatial;
using Inceptus.DocumentEngine.Organizational.Validation;

namespace Inceptus.DocumentEngine.Organizational;

/// <summary>
/// Complete leaf-plugin registrations. The host supplies only notation eligibility and
/// the optional Scene implementation; all Organizational mutation policy stays here.
/// </summary>
public sealed class OrganizationalPluginRegistration
{
    private OrganizationalPluginRegistration(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        Canvas2DSceneContributorRegistration? sceneContributor)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        var restorationHandler = new RestoreOrganizationalSnapshotCommandHandler();
        CommandHandlers =
        [
            Handler<CreateOrganizationalPoolCommand>(
                CreateOrganizationalPoolCommand.KnownTypeId,
                new OrganizationalCommandHandler<CreateOrganizationalPoolCommand>(
                    eligibilityPolicy)),
            Handler<AssignOrganizationalElementCommand>(
                AssignOrganizationalElementCommand.KnownTypeId,
                new OrganizationalCommandHandler<AssignOrganizationalElementCommand>(
                    eligibilityPolicy)),
            Handler<UnassignOrganizationalElementCommand>(
                UnassignOrganizationalElementCommand.KnownTypeId,
                new OrganizationalCommandHandler<UnassignOrganizationalElementCommand>(
                    eligibilityPolicy)),
            Handler<MoveOrganizationalPoolCommand>(
                MoveOrganizationalPoolCommand.KnownTypeId,
                new OrganizationalCommandHandler<MoveOrganizationalPoolCommand>(
                    eligibilityPolicy)),
            Handler<DeleteOrganizationalPoolCommand>(
                DeleteOrganizationalPoolCommand.KnownTypeId,
                new OrganizationalCommandHandler<DeleteOrganizationalPoolCommand>(
                    eligibilityPolicy)),
            new CommandHandlerRegistration(
                RestoreOrganizationalSnapshotCommand.KnownTypeId,
                restorationHandler,
                restorationHandler),
        ];
        CommandValidators =
        [
            Validator<CreateOrganizationalPoolCommand>(
                CreateOrganizationalPoolCommand.KnownTypeId,
                "inceptus:organizational/validator/create-pool",
                eligibilityPolicy),
            Validator<AssignOrganizationalElementCommand>(
                AssignOrganizationalElementCommand.KnownTypeId,
                "inceptus:organizational/validator/assign-element",
                eligibilityPolicy),
            Validator<UnassignOrganizationalElementCommand>(
                UnassignOrganizationalElementCommand.KnownTypeId,
                "inceptus:organizational/validator/unassign-element",
                eligibilityPolicy),
            Validator<MoveOrganizationalPoolCommand>(
                MoveOrganizationalPoolCommand.KnownTypeId,
                "inceptus:organizational/validator/move-pool",
                eligibilityPolicy),
            Validator<DeleteOrganizationalPoolCommand>(
                DeleteOrganizationalPoolCommand.KnownTypeId,
                "inceptus:organizational/validator/delete-pool",
                eligibilityPolicy),
            new CommandValidatorRegistration(
                UpdateSemanticElementNameCommand.KnownTypeId,
                new CommandValidatorId(
                    "inceptus:organizational/validator/pool-name-update"),
                new OrganizationalPoolGenericUpdateValidator()),
            new CommandValidatorRegistration(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                new CommandValidatorId(
                    "inceptus:organizational/validator/pool-property-update"),
                new OrganizationalPoolGenericUpdateValidator()),
        ];
        HistoryPolicies =
        [
            History<CreateOrganizationalPoolCommand>(
                CreateOrganizationalPoolCommand.KnownTypeId,
                eligibilityPolicy),
            History<AssignOrganizationalElementCommand>(
                AssignOrganizationalElementCommand.KnownTypeId,
                eligibilityPolicy),
            History<UnassignOrganizationalElementCommand>(
                UnassignOrganizationalElementCommand.KnownTypeId,
                eligibilityPolicy),
            History<MoveOrganizationalPoolCommand>(
                MoveOrganizationalPoolCommand.KnownTypeId,
                eligibilityPolicy),
            History<DeleteOrganizationalPoolCommand>(
                DeleteOrganizationalPoolCommand.KnownTypeId,
                eligibilityPolicy),
        ];
        ProjectionRules = [OrganizationalPoolProjectionRule.Registration];
        PropertiesSchemas = [OrganizationalPoolPropertiesSchema.Definition];
        DiagramDeletionRegistrations =
        [
            OrganizationalPoolDeletionContribution.CreateRegistration(
                eligibilityPolicy),
        ];
        ModelValidationRules =
        [
            new OrganizationalStructuralValidationRule(eligibilityPolicy),
        ];
        SceneContributors = sceneContributor is null ? [] : [sceneContributor];
        SpatialEditPlannerRegistrations =
        [
            new Canvas2DSpatialEditPlannerRegistration(
                OrganizationalModelProfile.Id,
                new OrganizationalSpatialEditPlanner(eligibilityPolicy)),
        ];
        ModelProfileDefinitions = [OrganizationalModelProfile.Definition];
        BackgroundActions = OrganizationalCanvasBackgroundActions.Definitions;
        SemanticSceneViewActions = OrganizationalPoolActions.ViewDefinitions;
        SemanticSceneCommandActions = OrganizationalPoolActions.CommandDefinitions;
    }

    public ImmutableArray<CommandHandlerRegistration> CommandHandlers { get; }

    public ImmutableArray<CommandValidatorRegistration> CommandValidators { get; }

    public ImmutableArray<CommandHistoryPolicyRegistration> HistoryPolicies { get; }

    public ImmutableArray<ProjectionRuleRegistration> ProjectionRules { get; }

    public ImmutableArray<ElementPropertiesSchema> PropertiesSchemas { get; }

    public ImmutableArray<DiagramDeletionRegistration> DiagramDeletionRegistrations
    { get; }

    public ImmutableArray<IModelValidationRule> ModelValidationRules { get; }

    public ImmutableArray<Canvas2DSceneContributorRegistration> SceneContributors { get; }

    public ImmutableArray<Canvas2DSpatialEditPlannerRegistration>
        SpatialEditPlannerRegistrations
    { get; }

    public ImmutableArray<ModelProfileDefinition> ModelProfileDefinitions { get; }

    public ImmutableArray<CanvasBackgroundActionDefinition> BackgroundActions { get; }

    public ImmutableArray<SemanticSceneViewActionDefinition> SemanticSceneViewActions
    { get; }

    public ImmutableArray<SemanticSceneCommandActionDefinition> SemanticSceneCommandActions
    { get; }

    public static OrganizationalPluginRegistration Create(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy,
        Canvas2DSceneContributorRegistration? sceneContributor = null) =>
        new(eligibilityPolicy, sceneContributor);

    private static CommandHandlerRegistration Handler<TCommand>(
        CommandTypeId typeId,
        ICommandHandler handler)
        where TCommand : class, ICommand =>
        new(
            typeId,
            new OrganizationalCommandEnvelopeValidator<TCommand>(),
            handler);

    private static CommandValidatorRegistration Validator<TCommand>(
        CommandTypeId typeId,
        string validatorId,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
        where TCommand : OrganizationalCommand =>
        new(
            typeId,
            new CommandValidatorId(validatorId),
            new OrganizationalCommandValidator<TCommand>(eligibilityPolicy));

    private static CommandHistoryPolicyRegistration History<TCommand>(
        CommandTypeId typeId,
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
        where TCommand : OrganizationalCommand =>
        new(
            typeId,
            new OrganizationalHistoryPolicy<TCommand>(eligibilityPolicy));
}
