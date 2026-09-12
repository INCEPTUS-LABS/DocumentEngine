using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.ConnectionCreation;
using Inceptus.DocumentEngine.Bpmn.ContextMenus;
using Inceptus.DocumentEngine.Bpmn.Deletion;
using Inceptus.DocumentEngine.Bpmn.EndpointReconnection;
using Inceptus.DocumentEngine.Bpmn.History;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Navigation;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Properties;
using Inceptus.DocumentEngine.Bpmn.Projection;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn;

public sealed class BpmnPluginRegistration
{
    private BpmnPluginRegistration()
    {
        CommandHandlers =
        [
            Handler<CreateBpmnStartEventCommand>(
                CreateBpmnStartEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnStartEventCommand>()),
            Handler<CreateBpmnTaskCommand>(
                CreateBpmnTaskCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnTaskCommand>()),
            Handler<CreateBpmnEndEventCommand>(
                CreateBpmnEndEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnEndEventCommand>()),
            Handler<CreateBpmnSequenceFlowCommand>(
                CreateBpmnSequenceFlowCommand.KnownTypeId,
                new BpmnSequenceFlowCreationCommandHandler()),
            Handler<RestoreBpmnCreationCommand>(
                RestoreBpmnCreationCommand.KnownTypeId,
                new RestoreBpmnCreationCommandHandler()),
        ];
        CommandValidators =
        [
            Validator(
                CreateBpmnStartEventCommand.KnownTypeId,
                "bpmn:validator/create-start-event",
                new BpmnElementCreationValidator<CreateBpmnStartEventCommand>()),
            Validator(
                CreateBpmnTaskCommand.KnownTypeId,
                "bpmn:validator/create-task",
                new BpmnElementCreationValidator<CreateBpmnTaskCommand>()),
            Validator(
                CreateBpmnEndEventCommand.KnownTypeId,
                "bpmn:validator/create-end-event",
                new BpmnElementCreationValidator<CreateBpmnEndEventCommand>()),
            Validator(
                CreateBpmnSequenceFlowCommand.KnownTypeId,
                "bpmn:validator/create-sequence-flow",
                new BpmnSequenceFlowCreationValidator()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/task-code-update",
                new BpmnTaskCodeUpdateValidator()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/task-element-number-update",
                new BpmnTaskElementNumberUpdateValidator()),
        ];
        HistoryPolicies =
        [
            History<CreateBpmnStartEventCommand>(CreateBpmnStartEventCommand.KnownTypeId),
            History<CreateBpmnTaskCommand>(CreateBpmnTaskCommand.KnownTypeId),
            History<CreateBpmnEndEventCommand>(CreateBpmnEndEventCommand.KnownTypeId),
            History<CreateBpmnSequenceFlowCommand>(CreateBpmnSequenceFlowCommand.KnownTypeId),
        ];
        ProjectionRules =
        [
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.StartEventRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.StartEvent,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.StartEventRuleId,
                    requiresNameLabel: false)),
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.TaskRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.Task,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.TaskRuleId,
                    requiresNameLabel: true)),
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.EndEventRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.EndEvent,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.EndEventRuleId,
                    requiresNameLabel: false)),
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.SequenceFlowRuleId,
                ProjectionSourceKind.SemanticRelationship,
                BpmnSemanticTypes.SequenceFlow,
                new BpmnSequenceFlowProjectionRule()),
        ];
        LayoutAlgorithms = [];
        RoutingAlgorithms = [];
        SceneContributors = [];
        ToolboxContributions = [];
        ToolboxPlacementRegistrations = [];
        PropertiesSchemas = [];
        ConnectorAnchorPolicies = [];
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m1)
    {
        ArgumentNullException.ThrowIfNull(m1);

        CommandHandlers = m1.CommandHandlers;
        CommandValidators = m1.CommandValidators;
        HistoryPolicies = m1.HistoryPolicies;
        ProjectionRules = m1.ProjectionRules;
        LayoutAlgorithms =
        [
            new LayoutAlgorithmRegistration(
                BpmnAlgorithmIds.DefaultLayout,
                new BpmnLayoutAlgorithm()),
        ];
        RoutingAlgorithms =
        [
            new RoutingAlgorithmRegistration(
                BpmnAlgorithmIds.DefaultRouting,
                new BpmnRoutingAlgorithm()),
        ];
        SceneContributors = [];
        ToolboxContributions = [];
        ToolboxPlacementRegistrations = m1.ToolboxPlacementRegistrations;
        PropertiesSchemas = [];
        ConnectorAnchorPolicies = m1.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m2, M3RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m2);

        CommandHandlers = m2.CommandHandlers;
        CommandValidators = m2.CommandValidators;
        HistoryPolicies = m2.HistoryPolicies;
        ProjectionRules = m2.ProjectionRules;
        LayoutAlgorithms = m2.LayoutAlgorithms;
        RoutingAlgorithms = m2.RoutingAlgorithms;
        SceneContributors = [BpmnCanvas2DSceneContributor.Registration];
        ToolboxContributions = [BpmnToolboxContribution.Definition];
        ToolboxPlacementRegistrations = m2.ToolboxPlacementRegistrations;
        PropertiesSchemas = [];
        ConnectorAnchorPolicies = m2.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m3, M31RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m3);

        CommandHandlers = m3.CommandHandlers;
        CommandValidators = m3.CommandValidators;
        HistoryPolicies = m3.HistoryPolicies;
        ProjectionRules = m3.ProjectionRules;
        LayoutAlgorithms = m3.LayoutAlgorithms;
        RoutingAlgorithms = m3.RoutingAlgorithms;
        SceneContributors = m3.SceneContributors;
        ToolboxContributions = m3.ToolboxContributions;
        ToolboxPlacementRegistrations = m3.ToolboxPlacementRegistrations;
        PropertiesSchemas = [BpmnTaskPropertiesSchema.Definition];
        ConnectorAnchorPolicies = m3.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m31, M32RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m31);

        CommandHandlers =
        [
            .. m31.CommandHandlers,
            Handler<CreateBpmnExclusiveGatewayCommand>(
                CreateBpmnExclusiveGatewayCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnExclusiveGatewayCommand>()),
        ];
        CommandValidators =
        [
            .. m31.CommandValidators,
            Validator(
                CreateBpmnExclusiveGatewayCommand.KnownTypeId,
                "bpmn:validator/create-exclusive-gateway",
                new BpmnElementCreationValidator<CreateBpmnExclusiveGatewayCommand>()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/exclusive-gateway-code-update",
                new BpmnExclusiveGatewayCodeUpdateValidator()),
        ];
        HistoryPolicies =
        [
            .. m31.HistoryPolicies,
            History<CreateBpmnExclusiveGatewayCommand>(
                CreateBpmnExclusiveGatewayCommand.KnownTypeId),
        ];
        ProjectionRules =
        [
            .. m31.ProjectionRules,
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.ExclusiveGatewayRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.ExclusiveGateway,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.ExclusiveGatewayRuleId,
                    requiresNameLabel: false,
                    projectsNameLabel: false)),
        ];
        LayoutAlgorithms = m31.LayoutAlgorithms;
        RoutingAlgorithms = m31.RoutingAlgorithms;
        SceneContributors = m31.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.M32Definition];
        ToolboxPlacementRegistrations = m31.ToolboxPlacementRegistrations;
        PropertiesSchemas =
        [
            .. m31.PropertiesSchemas,
            BpmnExclusiveGatewayPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = m31.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m32, M321RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m32);

        CommandHandlers = m32.CommandHandlers;
        CommandValidators = m32.CommandValidators;
        HistoryPolicies = m32.HistoryPolicies;
        ProjectionRules = m32.ProjectionRules;
        LayoutAlgorithms = m32.LayoutAlgorithms;
        RoutingAlgorithms = m32.RoutingAlgorithms;
        SceneContributors = m32.SceneContributors;
        ToolboxContributions = m32.ToolboxContributions;
        ToolboxPlacementRegistrations = m32.ToolboxPlacementRegistrations;
        PropertiesSchemas = m32.PropertiesSchemas;
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.Registrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m321, M322RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m321);

        CommandHandlers = m321.CommandHandlers;
        CommandValidators = m321.CommandValidators;
        HistoryPolicies = m321.HistoryPolicies;
        ProjectionRules =
        [
            .. m321.ProjectionRules.Where(static registration =>
                registration.RuleId != BpmnProjectionIdentities.ExclusiveGatewayRuleId),
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.ExclusiveGatewayRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.ExclusiveGateway,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.ExclusiveGatewayRuleId,
                    requiresNameLabel: false,
                    nameLabelPlacement: new NodeLabelPlacement(
                        NodeLabelPlacementKind.OutsideBelow,
                        gap: 8d,
                        maximumWidth: 160d))),
        ];
        LayoutAlgorithms = m321.LayoutAlgorithms;
        RoutingAlgorithms = m321.RoutingAlgorithms;
        SceneContributors = m321.SceneContributors;
        ToolboxContributions = m321.ToolboxContributions;
        ToolboxPlacementRegistrations = m321.ToolboxPlacementRegistrations;
        PropertiesSchemas = m321.PropertiesSchemas;
        ConnectorAnchorPolicies = m321.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m322, M323RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m322);

        CommandHandlers = m322.CommandHandlers;
        CommandValidators = m322.CommandValidators;
        HistoryPolicies = m322.HistoryPolicies;
        ProjectionRules =
        [
            .. m322.ProjectionRules.Where(static registration =>
                registration.RuleId != BpmnProjectionIdentities.ExclusiveGatewayRuleId),
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.ExclusiveGatewayRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.ExclusiveGateway,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.ExclusiveGatewayRuleId,
                    requiresNameLabel: false,
                    nameLabelPlacement: new NodeLabelPlacement(
                        NodeLabelPlacementKind.OutsideBelow,
                        gap: 8d,
                        maximumWidth: 160d),
                    nameLabelInteractionPolicy:
                        NodeLabelInteractionPolicy.MoveAndResize)),
        ];
        LayoutAlgorithms = m322.LayoutAlgorithms;
        RoutingAlgorithms = m322.RoutingAlgorithms;
        SceneContributors = m322.SceneContributors;
        ToolboxContributions = m322.ToolboxContributions;
        ToolboxPlacementRegistrations = m322.ToolboxPlacementRegistrations;
        PropertiesSchemas =
        [
            .. m322.PropertiesSchemas.Where(static schema =>
                schema.SemanticTypeId != BpmnSemanticTypes.ExclusiveGateway),
            BpmnExclusiveGatewayPropertiesSchema.M323Definition,
        ];
        ConnectorAnchorPolicies = m322.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m323, M33RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m323);

        CommandHandlers =
        [
            .. m323.CommandHandlers,
            Handler<CreateBpmnParallelGatewayCommand>(
                CreateBpmnParallelGatewayCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnParallelGatewayCommand>()),
        ];
        CommandValidators =
        [
            .. m323.CommandValidators,
            Validator(
                CreateBpmnParallelGatewayCommand.KnownTypeId,
                "bpmn:validator/create-parallel-gateway",
                new BpmnElementCreationValidator<CreateBpmnParallelGatewayCommand>()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/parallel-gateway-code-update",
                new BpmnParallelGatewayCodeUpdateValidator()),
        ];
        HistoryPolicies =
        [
            .. m323.HistoryPolicies,
            History<CreateBpmnParallelGatewayCommand>(
                CreateBpmnParallelGatewayCommand.KnownTypeId),
        ];
        ProjectionRules =
        [
            .. m323.ProjectionRules,
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.ParallelGatewayRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.ParallelGateway,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.ParallelGatewayRuleId,
                    requiresNameLabel: false,
                    nameLabelPlacement: new NodeLabelPlacement(
                        NodeLabelPlacementKind.OutsideBelow,
                        gap: 8d,
                        maximumWidth: 160d),
                    nameLabelInteractionPolicy:
                        NodeLabelInteractionPolicy.MoveAndResize)),
        ];
        LayoutAlgorithms = m323.LayoutAlgorithms;
        RoutingAlgorithms = m323.RoutingAlgorithms;
        SceneContributors = m323.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.M33Definition];
        ToolboxPlacementRegistrations = m323.ToolboxPlacementRegistrations;
        PropertiesSchemas =
        [
            .. m323.PropertiesSchemas,
            BpmnParallelGatewayPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.M33Registrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m33, M34RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m33);

        CommandHandlers =
        [
            .. m33.CommandHandlers,
            Handler<CreateBpmnInclusiveGatewayCommand>(
                CreateBpmnInclusiveGatewayCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnInclusiveGatewayCommand>()),
        ];
        CommandValidators =
        [
            .. m33.CommandValidators,
            Validator(
                CreateBpmnInclusiveGatewayCommand.KnownTypeId,
                "bpmn:validator/create-inclusive-gateway",
                new BpmnElementCreationValidator<CreateBpmnInclusiveGatewayCommand>()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/inclusive-gateway-code-update",
                new BpmnInclusiveGatewayCodeUpdateValidator()),
        ];
        HistoryPolicies =
        [
            .. m33.HistoryPolicies,
            History<CreateBpmnInclusiveGatewayCommand>(
                CreateBpmnInclusiveGatewayCommand.KnownTypeId),
        ];
        ProjectionRules =
        [
            .. m33.ProjectionRules,
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.InclusiveGatewayRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.InclusiveGateway,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.InclusiveGatewayRuleId,
                    requiresNameLabel: false,
                    nameLabelPlacement: new NodeLabelPlacement(
                        NodeLabelPlacementKind.OutsideBelow,
                        gap: 8d,
                        maximumWidth: 160d),
                    nameLabelInteractionPolicy:
                        NodeLabelInteractionPolicy.MoveAndResize)),
        ];
        LayoutAlgorithms = m33.LayoutAlgorithms;
        RoutingAlgorithms = m33.RoutingAlgorithms;
        SceneContributors = m33.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.M34Definition];
        ToolboxPlacementRegistrations = m33.ToolboxPlacementRegistrations;
        PropertiesSchemas =
        [
            .. m33.PropertiesSchemas,
            BpmnInclusiveGatewayPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.M34Registrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration m34, N1RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(m34);

        CommandHandlers = m34.CommandHandlers;
        CommandValidators = m34.CommandValidators;
        HistoryPolicies = m34.HistoryPolicies;
        ProjectionRules = m34.ProjectionRules;
        LayoutAlgorithms = m34.LayoutAlgorithms;
        RoutingAlgorithms = m34.RoutingAlgorithms;
        SceneContributors = m34.SceneContributors;
        ToolboxContributions = m34.ToolboxContributions;
        ToolboxPlacementRegistrations = BpmnToolboxPlacementContribution.Registrations;
        PropertiesSchemas = m34.PropertiesSchemas;
        ConnectorAnchorPolicies = m34.ConnectorAnchorPolicies;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n1, N2RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n1);

        CommandHandlers = n1.CommandHandlers;
        CommandValidators = n1.CommandValidators;
        HistoryPolicies = n1.HistoryPolicies;
        ProjectionRules = n1.ProjectionRules;
        LayoutAlgorithms = n1.LayoutAlgorithms;
        RoutingAlgorithms = n1.RoutingAlgorithms;
        SceneContributors = n1.SceneContributors;
        ToolboxContributions = n1.ToolboxContributions;
        ToolboxPlacementRegistrations = n1.ToolboxPlacementRegistrations;
        PropertiesSchemas = n1.PropertiesSchemas;
        ConnectorAnchorPolicies = n1.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
        [
            .. n1.AnchorConnectionCreationRegistrations,
            .. BpmnSequenceFlowConnectionCreationContribution.Registrations,
        ];
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n2, N3RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n2);

        CommandHandlers =
        [
            .. n2.CommandHandlers,
            Handler<ReconnectBpmnSequenceFlowEndpointCommand>(
                ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId,
                new BpmnSequenceFlowEndpointReconnectionCommandHandler()),
        ];
        CommandValidators =
        [
            .. n2.CommandValidators,
            Validator(
                ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId,
                "bpmn:validator/reconnect-sequence-flow-endpoint",
                new BpmnSequenceFlowEndpointReconnectionValidator()),
        ];
        HistoryPolicies =
        [
            .. n2.HistoryPolicies,
            new CommandHistoryPolicyRegistration(
                ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId,
                new BpmnSequenceFlowEndpointReconnectionHistoryPolicy()),
        ];
        ProjectionRules = n2.ProjectionRules;
        LayoutAlgorithms = n2.LayoutAlgorithms;
        RoutingAlgorithms = n2.RoutingAlgorithms;
        SceneContributors = n2.SceneContributors;
        ToolboxContributions = n2.ToolboxContributions;
        ToolboxPlacementRegistrations = n2.ToolboxPlacementRegistrations;
        PropertiesSchemas = n2.PropertiesSchemas;
        ConnectorAnchorPolicies = n2.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations = n2.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
        [
            .. n2.ConnectorEndpointReconnectionRegistrations,
            .. BpmnSequenceFlowEndpointReconnectionContribution.Registrations,
        ];
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n3, N31RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n3);
        CommandHandlers =
        [
            .. n3.CommandHandlers,
            Handler<CreateBpmnSequenceFlowWithTargetAnchorCommand>(
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId,
                new BpmnSequenceFlowWithTargetAnchorCreationCommandHandler()),
        ];
        CommandValidators =
        [
            .. n3.CommandValidators,
            Validator(
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId,
                "bpmn:validator/create-sequence-flow-with-target-anchor",
                new BpmnSequenceFlowWithTargetAnchorCreationValidator()),
        ];
        HistoryPolicies =
        [
            .. n3.HistoryPolicies,
            History<CreateBpmnSequenceFlowWithTargetAnchorCommand>(
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId),
        ];
        ProjectionRules = n3.ProjectionRules;
        LayoutAlgorithms = n3.LayoutAlgorithms;
        RoutingAlgorithms = n3.RoutingAlgorithms;
        SceneContributors = n3.SceneContributors;
        ToolboxContributions = n3.ToolboxContributions;
        ToolboxPlacementRegistrations = n3.ToolboxPlacementRegistrations;
        PropertiesSchemas = n3.PropertiesSchemas;
        ConnectorAnchorPolicies = n3.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
        [
            .. n3.AnchorConnectionCreationRegistrations.Where(registration =>
                registration.CreationId !=
                    BpmnSequenceFlowConnectionCreationContribution.SequenceFlowCreationId),
            .. BpmnSequenceFlowConnectionCreationContribution.N31Registrations,
        ];
        ConnectorEndpointReconnectionRegistrations =
            n3.ConnectorEndpointReconnectionRegistrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n31, N314RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n31);
        CommandHandlers =
        [
            .. n31.CommandHandlers,
            Handler<DeleteBpmnSequenceFlowCommand>(
                DeleteBpmnSequenceFlowCommand.KnownTypeId,
                new BpmnSequenceFlowDeletionCommandHandler()),
            Handler<DeleteBpmnFlowNodeCommand>(
                DeleteBpmnFlowNodeCommand.KnownTypeId,
                new BpmnFlowNodeDeletionCommandHandler()),
            Handler<RestoreBpmnDeletionCommand>(
                RestoreBpmnDeletionCommand.KnownTypeId,
                new RestoreBpmnDeletionCommandHandler()),
        ];
        CommandValidators =
        [
            .. n31.CommandValidators,
            Validator(
                DeleteBpmnSequenceFlowCommand.KnownTypeId,
                "bpmn:validator/delete-sequence-flow",
                new BpmnSequenceFlowDeletionValidator()),
            Validator(
                DeleteBpmnFlowNodeCommand.KnownTypeId,
                "bpmn:validator/delete-flow-node",
                new BpmnFlowNodeDeletionValidator()),
        ];
        HistoryPolicies =
        [
            .. n31.HistoryPolicies,
            new CommandHistoryPolicyRegistration(
                DeleteBpmnSequenceFlowCommand.KnownTypeId,
                new BpmnSequenceFlowDeletionHistoryPolicy()),
            new CommandHistoryPolicyRegistration(
                DeleteBpmnFlowNodeCommand.KnownTypeId,
                new BpmnFlowNodeDeletionHistoryPolicy()),
        ];
        ProjectionRules = n31.ProjectionRules;
        LayoutAlgorithms = n31.LayoutAlgorithms;
        RoutingAlgorithms = n31.RoutingAlgorithms;
        SceneContributors = n31.SceneContributors;
        ToolboxContributions = n31.ToolboxContributions;
        ToolboxPlacementRegistrations = n31.ToolboxPlacementRegistrations;
        PropertiesSchemas = n31.PropertiesSchemas;
        ConnectorAnchorPolicies = n31.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
            n31.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n31.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = BpmnDeletionContribution.Registrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n314, N4RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n314);
        CommandHandlers =
        [
            .. n314.CommandHandlers,
            Handler<CreateBpmnMessageCatchEventCommand>(
                CreateBpmnMessageCatchEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnMessageCatchEventCommand>()),
            Handler<CreateBpmnTimerCatchEventCommand>(
                CreateBpmnTimerCatchEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnTimerCatchEventCommand>()),
            Handler<CreateBpmnEventBasedGatewayCommand>(
                CreateBpmnEventBasedGatewayCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnEventBasedGatewayCommand>()),
        ];
        CommandValidators =
        [
            .. n314.CommandValidators,
            Validator(
                CreateBpmnMessageCatchEventCommand.KnownTypeId,
                "bpmn:validator/create-message-catch-event",
                new BpmnElementCreationValidator<CreateBpmnMessageCatchEventCommand>()),
            Validator(
                CreateBpmnTimerCatchEventCommand.KnownTypeId,
                "bpmn:validator/create-timer-catch-event",
                new BpmnElementCreationValidator<CreateBpmnTimerCatchEventCommand>()),
            Validator(
                CreateBpmnEventBasedGatewayCommand.KnownTypeId,
                "bpmn:validator/create-event-based-gateway",
                new BpmnElementCreationValidator<CreateBpmnEventBasedGatewayCommand>()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/event-based-gateway-code-update",
                new BpmnEventBasedGatewayCodeUpdateValidator()),
        ];
        HistoryPolicies =
        [
            .. n314.HistoryPolicies,
            History<CreateBpmnMessageCatchEventCommand>(
                CreateBpmnMessageCatchEventCommand.KnownTypeId),
            History<CreateBpmnTimerCatchEventCommand>(
                CreateBpmnTimerCatchEventCommand.KnownTypeId),
            History<CreateBpmnEventBasedGatewayCommand>(
                CreateBpmnEventBasedGatewayCommand.KnownTypeId),
        ];
        ProjectionRules =
        [
            .. n314.ProjectionRules,
            N4NodeProjection(
                BpmnProjectionIdentities.MessageCatchEventRuleId,
                BpmnSemanticTypes.MessageCatchEvent),
            N4NodeProjection(
                BpmnProjectionIdentities.TimerCatchEventRuleId,
                BpmnSemanticTypes.TimerCatchEvent),
            N4NodeProjection(
                BpmnProjectionIdentities.EventBasedGatewayRuleId,
                BpmnSemanticTypes.EventBasedGateway),
        ];
        LayoutAlgorithms = n314.LayoutAlgorithms;
        RoutingAlgorithms = n314.RoutingAlgorithms;
        SceneContributors = n314.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.N4Definition];
        ToolboxPlacementRegistrations = BpmnToolboxPlacementContribution.N4Registrations;
        PropertiesSchemas =
        [
            .. n314.PropertiesSchemas,
            BpmnMessageCatchEventPropertiesSchema.Definition,
            BpmnTimerCatchEventPropertiesSchema.Definition,
            BpmnEventBasedGatewayPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.N4Registrations;
        AnchorConnectionCreationRegistrations =
            n314.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n314.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n314.DiagramDeletionRegistrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n4, N5RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n4);
        CommandHandlers = n4.CommandHandlers;
        CommandValidators = n4.CommandValidators;
        HistoryPolicies = n4.HistoryPolicies;
        ProjectionRules = n4.ProjectionRules;
        LayoutAlgorithms = n4.LayoutAlgorithms;
        RoutingAlgorithms = n4.RoutingAlgorithms;
        SceneContributors = n4.SceneContributors;
        ToolboxContributions = n4.ToolboxContributions;
        ToolboxPlacementRegistrations = n4.ToolboxPlacementRegistrations;
        PropertiesSchemas = n4.PropertiesSchemas;
        ConnectorAnchorPolicies = n4.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
            n4.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n4.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n4.DiagramDeletionRegistrations;
        ModelValidationRules = [new BpmnStructuralValidationRule()];
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n5, N6RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n5);
        CommandHandlers = n5.CommandHandlers;
        CommandValidators = n5.CommandValidators;
        HistoryPolicies = n5.HistoryPolicies;
        ProjectionRules =
        [
            .. n5.ProjectionRules,
            TaskNodeProjection(BpmnSemanticTypes.UserTask),
            TaskNodeProjection(BpmnSemanticTypes.ManualTask),
            TaskNodeProjection(BpmnSemanticTypes.ServiceTask),
            TaskNodeProjection(BpmnSemanticTypes.SendTask),
            TaskNodeProjection(BpmnSemanticTypes.ReceiveTask),
        ];
        LayoutAlgorithms = n5.LayoutAlgorithms;
        RoutingAlgorithms = n5.RoutingAlgorithms;
        SceneContributors = n5.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.N6Definition];
        ToolboxPlacementRegistrations = BpmnToolboxPlacementContribution.N6Registrations;
        PropertiesSchemas =
        [
            .. n5.PropertiesSchemas,
            .. BpmnTaskPropertiesSchema.SpecializedDefinitions,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.N6Registrations;
        AnchorConnectionCreationRegistrations =
            n5.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n5.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n5.DiagramDeletionRegistrations;
        ModelValidationRules = n5.ModelValidationRules;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n6, N7RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n6);
        CommandHandlers =
        [
            .. n6.CommandHandlers,
            Handler<CreateBpmnMessageThrowEventCommand>(
                CreateBpmnMessageThrowEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnMessageThrowEventCommand>()),
            Handler<CreateBpmnSignalCatchEventCommand>(
                CreateBpmnSignalCatchEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnSignalCatchEventCommand>()),
            Handler<CreateBpmnSignalThrowEventCommand>(
                CreateBpmnSignalThrowEventCommand.KnownTypeId,
                new BpmnElementCreationCommandHandler<CreateBpmnSignalThrowEventCommand>()),
        ];
        CommandValidators =
        [
            .. n6.CommandValidators,
            Validator(
                CreateBpmnMessageThrowEventCommand.KnownTypeId,
                "bpmn:validator/create-message-throw-event",
                new BpmnElementCreationValidator<CreateBpmnMessageThrowEventCommand>()),
            Validator(
                CreateBpmnSignalCatchEventCommand.KnownTypeId,
                "bpmn:validator/create-signal-catch-event",
                new BpmnElementCreationValidator<CreateBpmnSignalCatchEventCommand>()),
            Validator(
                CreateBpmnSignalThrowEventCommand.KnownTypeId,
                "bpmn:validator/create-signal-throw-event",
                new BpmnElementCreationValidator<CreateBpmnSignalThrowEventCommand>()),
        ];
        HistoryPolicies =
        [
            .. n6.HistoryPolicies,
            History<CreateBpmnMessageThrowEventCommand>(
                CreateBpmnMessageThrowEventCommand.KnownTypeId),
            History<CreateBpmnSignalCatchEventCommand>(
                CreateBpmnSignalCatchEventCommand.KnownTypeId),
            History<CreateBpmnSignalThrowEventCommand>(
                CreateBpmnSignalThrowEventCommand.KnownTypeId),
        ];
        ProjectionRules =
        [
            .. n6.ProjectionRules,
            N4NodeProjection(
                BpmnProjectionIdentities.MessageThrowEventRuleId,
                BpmnSemanticTypes.MessageThrowEvent),
            N4NodeProjection(
                BpmnProjectionIdentities.SignalCatchEventRuleId,
                BpmnSemanticTypes.SignalCatchEvent),
            N4NodeProjection(
                BpmnProjectionIdentities.SignalThrowEventRuleId,
                BpmnSemanticTypes.SignalThrowEvent),
        ];
        LayoutAlgorithms = n6.LayoutAlgorithms;
        RoutingAlgorithms = n6.RoutingAlgorithms;
        SceneContributors = n6.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.N7Definition];
        ToolboxPlacementRegistrations = BpmnToolboxPlacementContribution.N7Registrations;
        PropertiesSchemas =
        [
            .. n6.PropertiesSchemas,
            BpmnMessageThrowEventPropertiesSchema.Definition,
            BpmnSignalCatchEventPropertiesSchema.Definition,
            BpmnSignalThrowEventPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.N7Registrations;
        AnchorConnectionCreationRegistrations =
            n6.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n6.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n6.DiagramDeletionRegistrations;
        ModelValidationRules = n6.ModelValidationRules;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n7, N81RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n7);
        CommandHandlers =
        [
            .. n7.CommandHandlers,
            Handler<CreateBpmnSubProcessCommand>(
                CreateBpmnSubProcessCommand.KnownTypeId,
                new BpmnSubProcessCreationCommandHandler()),
        ];
        CommandValidators =
        [
            .. n7.CommandValidators,
            Validator(
                CreateBpmnSubProcessCommand.KnownTypeId,
                "bpmn:validator/create-subprocess",
                new BpmnSubProcessCreationValidator()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/subprocess-code-update",
                new BpmnSubProcessCodeUpdateValidator()),
        ];
        HistoryPolicies =
        [
            .. n7.HistoryPolicies,
            new CommandHistoryPolicyRegistration(
                CreateBpmnSubProcessCommand.KnownTypeId,
                new BpmnSubProcessCreationHistoryPolicy()),
        ];
        ProjectionRules =
        [
            .. n7.ProjectionRules,
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.SubProcessRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.SubProcess,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.SubProcessRuleId,
                    requiresNameLabel: true)),
        ];
        LayoutAlgorithms = n7.LayoutAlgorithms;
        RoutingAlgorithms = n7.RoutingAlgorithms;
        SceneContributors = n7.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.N81Definition];
        ToolboxPlacementRegistrations = BpmnToolboxPlacementContribution.N81Registrations;
        PropertiesSchemas =
        [
            .. n7.PropertiesSchemas,
            BpmnSubProcessPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.N81Registrations;
        AnchorConnectionCreationRegistrations =
            n7.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n7.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n7.DiagramDeletionRegistrations;
        ModelValidationRules = n7.ModelValidationRules;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n81, N82RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n81);
        CommandHandlers = n81.CommandHandlers;
        CommandValidators = n81.CommandValidators;
        HistoryPolicies = n81.HistoryPolicies;
        ProjectionRules = n81.ProjectionRules;
        LayoutAlgorithms = n81.LayoutAlgorithms;
        RoutingAlgorithms = n81.RoutingAlgorithms;
        SceneContributors = n81.SceneContributors;
        ToolboxContributions = n81.ToolboxContributions;
        ToolboxPlacementRegistrations = n81.ToolboxPlacementRegistrations;
        PropertiesSchemas = n81.PropertiesSchemas;
        ConnectorAnchorPolicies = n81.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
            n81.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n81.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n81.DiagramDeletionRegistrations;
        ModelValidationRules = n81.ModelValidationRules;
        ScopeNavigationRegistrations =
        [
            new ScopeNavigationRegistration(
                BpmnSemanticTypes.SubProcess,
                "Open SubProcess",
                new BpmnSubProcessScopeNavigationContribution()),
        ];
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n82, N90RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n82);
        CommandHandlers =
        [
            .. n82.CommandHandlers,
            Handler<CreateBpmnTimerBoundaryEventCommand>(
                CreateBpmnTimerBoundaryEventCommand.KnownTypeId,
                new BpmnTimerBoundaryEventCreationCommandHandler()),
        ];
        CommandValidators =
        [
            .. n82.CommandValidators,
            Validator(
                CreateBpmnTimerBoundaryEventCommand.KnownTypeId,
                "bpmn:validator/create-timer-boundary-event",
                new BpmnTimerBoundaryEventCreationValidator()),
        ];
        HistoryPolicies =
        [
            .. n82.HistoryPolicies,
            new CommandHistoryPolicyRegistration(
                CreateBpmnTimerBoundaryEventCommand.KnownTypeId,
                new BpmnTimerBoundaryEventCreationHistoryPolicy()),
        ];
        ProjectionRules =
        [
            .. n82.ProjectionRules,
            new ProjectionRuleRegistration(
                BpmnProjectionIdentities.TimerBoundaryEventRuleId,
                ProjectionSourceKind.SemanticElement,
                BpmnSemanticTypes.TimerBoundaryEvent,
                new BpmnNodeProjectionRule(
                    BpmnProjectionIdentities.TimerBoundaryEventRuleId,
                    requiresNameLabel: true,
                    nameLabelPlacement: new NodeLabelPlacement(
                        NodeLabelPlacementKind.OutsideBelow,
                        gap: 8d,
                        maximumWidth: 160d),
                    nameLabelInteractionPolicy:
                        NodeLabelInteractionPolicy.MoveAndResize,
                    geometryInteractionPolicy:
                        NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize)),
        ];
        LayoutAlgorithms = n82.LayoutAlgorithms;
        RoutingAlgorithms = n82.RoutingAlgorithms;
        SceneContributors = n82.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.N90Definition];
        ToolboxPlacementRegistrations =
            BpmnToolboxPlacementContribution.N90Registrations;
        PropertiesSchemas =
        [
            .. n82.PropertiesSchemas,
            BpmnTimerBoundaryEventPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.N90Registrations;
        AnchorConnectionCreationRegistrations =
            n82.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n82.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n82.DiagramDeletionRegistrations;
        ModelValidationRules = n82.ModelValidationRules;
        ScopeNavigationRegistrations = n82.ScopeNavigationRegistrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n90, N91RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n90);
        CommandHandlers =
        [
            .. n90.CommandHandlers,
            Handler<CreateBpmnMessageBoundaryEventCommand>(
                CreateBpmnMessageBoundaryEventCommand.KnownTypeId,
                new BpmnMessageBoundaryEventCreationCommandHandler()),
            Handler<CreateBpmnSignalBoundaryEventCommand>(
                CreateBpmnSignalBoundaryEventCommand.KnownTypeId,
                new BpmnSignalBoundaryEventCreationCommandHandler()),
        ];
        CommandValidators =
        [
            .. n90.CommandValidators,
            Validator(
                CreateBpmnMessageBoundaryEventCommand.KnownTypeId,
                "bpmn:validator/create-message-boundary-event",
                new BpmnMessageBoundaryEventCreationValidator()),
            Validator(
                CreateBpmnSignalBoundaryEventCommand.KnownTypeId,
                "bpmn:validator/create-signal-boundary-event",
                new BpmnSignalBoundaryEventCreationValidator()),
        ];
        HistoryPolicies =
        [
            .. n90.HistoryPolicies,
            new CommandHistoryPolicyRegistration(
                CreateBpmnMessageBoundaryEventCommand.KnownTypeId,
                new BpmnMessageBoundaryEventCreationHistoryPolicy()),
            new CommandHistoryPolicyRegistration(
                CreateBpmnSignalBoundaryEventCommand.KnownTypeId,
                new BpmnSignalBoundaryEventCreationHistoryPolicy()),
        ];
        ProjectionRules =
        [
            .. n90.ProjectionRules,
            BoundaryNodeProjection(
                BpmnProjectionIdentities.MessageBoundaryEventRuleId,
                BpmnSemanticTypes.MessageBoundaryEvent),
            BoundaryNodeProjection(
                BpmnProjectionIdentities.SignalBoundaryEventRuleId,
                BpmnSemanticTypes.SignalBoundaryEvent),
        ];
        LayoutAlgorithms = n90.LayoutAlgorithms;
        RoutingAlgorithms = n90.RoutingAlgorithms;
        SceneContributors = n90.SceneContributors;
        ToolboxContributions = [BpmnToolboxContribution.N91Definition];
        ToolboxPlacementRegistrations =
            BpmnToolboxPlacementContribution.N91Registrations;
        PropertiesSchemas =
        [
            .. n90.PropertiesSchemas,
            BpmnMessageBoundaryEventPropertiesSchema.Definition,
            BpmnSignalBoundaryEventPropertiesSchema.Definition,
        ];
        ConnectorAnchorPolicies = BpmnConnectorAnchorPolicies.N91Registrations;
        AnchorConnectionCreationRegistrations =
            n90.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n90.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n90.DiagramDeletionRegistrations;
        ModelValidationRules = n90.ModelValidationRules;
        ScopeNavigationRegistrations = n90.ScopeNavigationRegistrations;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n91, N100RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n91);
        CommandHandlers =
        [
            .. n91.CommandHandlers,
            Handler<CreateBpmnCollaborationCommand>(
                CreateBpmnCollaborationCommand.KnownTypeId,
                new BpmnOrganizationalCommandHandler<CreateBpmnCollaborationCommand>()),
            Handler<CreateBpmnParticipantCommand>(
                CreateBpmnParticipantCommand.KnownTypeId,
                new BpmnOrganizationalCommandHandler<CreateBpmnParticipantCommand>()),
            Handler<UpdateBpmnCollaborationCommand>(
                UpdateBpmnCollaborationCommand.KnownTypeId,
                new BpmnOrganizationalCommandHandler<UpdateBpmnCollaborationCommand>()),
            Handler<UpdateBpmnParticipantCommand>(
                UpdateBpmnParticipantCommand.KnownTypeId,
                new BpmnOrganizationalCommandHandler<UpdateBpmnParticipantCommand>()),
            Handler<DeleteBpmnParticipantCommand>(
                DeleteBpmnParticipantCommand.KnownTypeId,
                new BpmnOrganizationalCommandHandler<DeleteBpmnParticipantCommand>()),
            Handler<DeleteBpmnCollaborationCommand>(
                DeleteBpmnCollaborationCommand.KnownTypeId,
                new BpmnOrganizationalCommandHandler<DeleteBpmnCollaborationCommand>()),
        ];
        CommandValidators =
        [
            .. n91.CommandValidators,
            Validator(
                CreateBpmnCollaborationCommand.KnownTypeId,
                "bpmn:validator/create-collaboration",
                new BpmnOrganizationalCommandValidator<CreateBpmnCollaborationCommand>()),
            Validator(
                CreateBpmnParticipantCommand.KnownTypeId,
                "bpmn:validator/create-participant",
                new BpmnOrganizationalCommandValidator<CreateBpmnParticipantCommand>()),
            Validator(
                UpdateBpmnCollaborationCommand.KnownTypeId,
                "bpmn:validator/update-collaboration",
                new BpmnOrganizationalCommandValidator<UpdateBpmnCollaborationCommand>()),
            Validator(
                UpdateBpmnParticipantCommand.KnownTypeId,
                "bpmn:validator/update-participant",
                new BpmnOrganizationalCommandValidator<UpdateBpmnParticipantCommand>()),
            Validator(
                DeleteBpmnParticipantCommand.KnownTypeId,
                "bpmn:validator/delete-participant",
                new BpmnOrganizationalCommandValidator<DeleteBpmnParticipantCommand>()),
            Validator(
                DeleteBpmnCollaborationCommand.KnownTypeId,
                "bpmn:validator/delete-collaboration",
                new BpmnOrganizationalCommandValidator<DeleteBpmnCollaborationCommand>()),
            Validator(
                UpdateSemanticElementNameCommand.KnownTypeId,
                "bpmn:validator/organizational-name-update-guard",
                new BpmnOrganizationalGenericUpdateGuard()),
            Validator(
                UpdateSemanticElementPropertyCommand.KnownTypeId,
                "bpmn:validator/organizational-property-update-guard",
                new BpmnOrganizationalGenericUpdateGuard()),
        ];
        HistoryPolicies =
        [
            .. n91.HistoryPolicies,
            OrganizationalHistory<CreateBpmnCollaborationCommand>(
                CreateBpmnCollaborationCommand.KnownTypeId),
            OrganizationalHistory<CreateBpmnParticipantCommand>(
                CreateBpmnParticipantCommand.KnownTypeId),
            OrganizationalHistory<UpdateBpmnCollaborationCommand>(
                UpdateBpmnCollaborationCommand.KnownTypeId),
            OrganizationalHistory<UpdateBpmnParticipantCommand>(
                UpdateBpmnParticipantCommand.KnownTypeId),
            OrganizationalHistory<DeleteBpmnParticipantCommand>(
                DeleteBpmnParticipantCommand.KnownTypeId),
            OrganizationalHistory<DeleteBpmnCollaborationCommand>(
                DeleteBpmnCollaborationCommand.KnownTypeId),
        ];
        ProjectionRules = n91.ProjectionRules;
        LayoutAlgorithms = n91.LayoutAlgorithms;
        RoutingAlgorithms = n91.RoutingAlgorithms;
        SceneContributors = n91.SceneContributors;
        ToolboxContributions = n91.ToolboxContributions;
        ToolboxPlacementRegistrations = n91.ToolboxPlacementRegistrations;
        PropertiesSchemas = n91.PropertiesSchemas;
        ConnectorAnchorPolicies = n91.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
            n91.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n91.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n91.DiagramDeletionRegistrations;
        ModelValidationRules = n91.ModelValidationRules;
        ScopeNavigationRegistrations = n91.ScopeNavigationRegistrations;
        ModelProfileDefinitions = BpmnModelProfiles.Definitions;
        BackgroundActions = BpmnCanvasBackgroundActions.Definitions;
    }

    private BpmnPluginRegistration(BpmnPluginRegistration n100, N101RegistrationMarker _)
    {
        ArgumentNullException.ThrowIfNull(n100);
        CommandHandlers = n100.CommandHandlers;
        CommandValidators = n100.CommandValidators;
        HistoryPolicies = n100.HistoryPolicies;
        ProjectionRules = n100.ProjectionRules;
        LayoutAlgorithms = n100.LayoutAlgorithms;
        RoutingAlgorithms = n100.RoutingAlgorithms;
        SceneContributors = n100.SceneContributors;
        ToolboxContributions = n100.ToolboxContributions;
        ToolboxPlacementRegistrations = n100.ToolboxPlacementRegistrations;
        PropertiesSchemas = n100.PropertiesSchemas;
        ConnectorAnchorPolicies = n100.ConnectorAnchorPolicies;
        AnchorConnectionCreationRegistrations =
            n100.AnchorConnectionCreationRegistrations;
        ConnectorEndpointReconnectionRegistrations =
            n100.ConnectorEndpointReconnectionRegistrations;
        DiagramDeletionRegistrations = n100.DiagramDeletionRegistrations;
        ModelValidationRules = n100.ModelValidationRules;
        ScopeNavigationRegistrations = n100.ScopeNavigationRegistrations;
        ModelProfileDefinitions = n100.ModelProfileDefinitions;
        BackgroundActions = [];
    }

    public static BpmnPluginRegistration M1 { get; } = new();

    public static BpmnPluginRegistration M2 { get; } = new(M1);

    public static BpmnPluginRegistration M3 { get; } = new(M2, default(M3RegistrationMarker));

    public static BpmnPluginRegistration M31 { get; } =
        new(M3, default(M31RegistrationMarker));

    public static BpmnPluginRegistration M32 { get; } =
        new(M31, default(M32RegistrationMarker));

    public static BpmnPluginRegistration M321 { get; } =
        new(M32, default(M321RegistrationMarker));

    public static BpmnPluginRegistration M322 { get; } =
        new(M321, default(M322RegistrationMarker));

    public static BpmnPluginRegistration M323 { get; } =
        new(M322, default(M323RegistrationMarker));

    public static BpmnPluginRegistration M33 { get; } =
        new(M323, default(M33RegistrationMarker));

    public static BpmnPluginRegistration M34 { get; } =
        new(M33, default(M34RegistrationMarker));

    public static BpmnPluginRegistration N1 { get; } =
        new(M34, default(N1RegistrationMarker));

    public static BpmnPluginRegistration N2 { get; } =
        new(N1, default(N2RegistrationMarker));

    public static BpmnPluginRegistration N3 { get; } =
        new(N2, default(N3RegistrationMarker));

    public static BpmnPluginRegistration N31 { get; } =
        new(N3, default(N31RegistrationMarker));

    public static BpmnPluginRegistration N314 { get; } =
        new(N31, default(N314RegistrationMarker));

    public static BpmnPluginRegistration N4 { get; } =
        new(N314, default(N4RegistrationMarker));

    public static BpmnPluginRegistration N5 { get; } =
        new(N4, default(N5RegistrationMarker));

    public static BpmnPluginRegistration N6 { get; } =
        new(N5, default(N6RegistrationMarker));

    public static BpmnPluginRegistration N7 { get; } =
        new(N6, default(N7RegistrationMarker));

    public static BpmnPluginRegistration N81 { get; } =
        new(N7, default(N81RegistrationMarker));

    public static BpmnPluginRegistration N82 { get; } =
        new(N81, default(N82RegistrationMarker));

    public static BpmnPluginRegistration N90 { get; } =
        new(N82, default(N90RegistrationMarker));

    public static BpmnPluginRegistration N91 { get; } =
        new(N90, default(N91RegistrationMarker));

    public static BpmnPluginRegistration N100 { get; } =
        new(N91, default(N100RegistrationMarker));

    public static BpmnPluginRegistration N101 { get; } =
        new(N100, default(N101RegistrationMarker));

    public ImmutableArray<CommandHandlerRegistration> CommandHandlers { get; }

    public ImmutableArray<CommandValidatorRegistration> CommandValidators { get; }

    public ImmutableArray<CommandHistoryPolicyRegistration> HistoryPolicies { get; }

    public ImmutableArray<ProjectionRuleRegistration> ProjectionRules { get; }

    public ImmutableArray<LayoutAlgorithmRegistration> LayoutAlgorithms { get; }

    public ImmutableArray<RoutingAlgorithmRegistration> RoutingAlgorithms { get; }

    public ImmutableArray<Canvas2DSceneContributorRegistration> SceneContributors { get; }

    public ImmutableArray<ToolboxContribution> ToolboxContributions { get; }

    public ImmutableArray<ToolboxPlacementRegistration> ToolboxPlacementRegistrations
    { get; }

    public ImmutableArray<ElementPropertiesSchema> PropertiesSchemas { get; }

    public ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        ConnectorAnchorPolicies
    { get; }

    public ImmutableArray<AnchorConnectionCreationRegistration>
        AnchorConnectionCreationRegistrations
    { get; } = [];

    public ImmutableArray<ConnectorEndpointReconnectionRegistration>
        ConnectorEndpointReconnectionRegistrations
    { get; } = [];

    public ImmutableArray<DiagramDeletionRegistration> DiagramDeletionRegistrations
    { get; } = [];

    public ImmutableArray<IModelValidationRule> ModelValidationRules { get; } = [];

    public ImmutableArray<ScopeNavigationRegistration> ScopeNavigationRegistrations
    { get; } = [];

    public ImmutableArray<ModelProfileDefinition> ModelProfileDefinitions
    { get; } = [];

    public ImmutableArray<CanvasBackgroundActionDefinition> BackgroundActions
    { get; } = [];

    public ImmutableArray<SemanticSceneViewActionDefinition> SemanticSceneViewActions
    { get; } = [];

    public ImmutableArray<SemanticSceneCommandActionDefinition> SemanticSceneCommandActions
    { get; } = [];

    private static CommandHandlerRegistration Handler<TCommand>(
        CommandTypeId typeId,
        ICommandHandler handler)
        where TCommand : class, ICommand =>
        new(typeId, new BpmnCommandEnvelopeValidator<TCommand>(), handler);

    private static CommandValidatorRegistration Validator(
        CommandTypeId typeId,
        string validatorId,
        ICommandValidator validator) =>
        new(typeId, new CommandValidatorId(validatorId), validator);

    private static CommandHistoryPolicyRegistration History<TCommand>(CommandTypeId typeId)
        where TCommand : class, ICommand =>
        new(typeId, new BpmnCreationHistoryPolicy<TCommand>());

    private static CommandHistoryPolicyRegistration OrganizationalHistory<TCommand>(
        CommandTypeId typeId)
        where TCommand : BpmnOrganizationalCommand =>
        new(typeId, new BpmnOrganizationalHistoryPolicy<TCommand>());

    private static ProjectionRuleRegistration N4NodeProjection(
        ProjectionRuleId ruleId,
        SemanticTypeId semanticTypeId) =>
        new(
            ruleId,
            ProjectionSourceKind.SemanticElement,
            semanticTypeId,
            new BpmnNodeProjectionRule(
                ruleId,
                requiresNameLabel: false,
                nameLabelPlacement: new NodeLabelPlacement(
                    NodeLabelPlacementKind.OutsideBelow,
                    gap: 8d,
                    maximumWidth: 160d),
                nameLabelInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize));

    private static ProjectionRuleRegistration TaskNodeProjection(
        SemanticTypeId semanticTypeId)
    {
        var ruleId = BpmnProjectionIdentities.ResolveTaskRuleId(semanticTypeId);
        return new ProjectionRuleRegistration(
            ruleId,
            ProjectionSourceKind.SemanticElement,
            semanticTypeId,
            new BpmnNodeProjectionRule(ruleId, requiresNameLabel: true));
    }

    private static ProjectionRuleRegistration BoundaryNodeProjection(
        ProjectionRuleId ruleId,
        SemanticTypeId semanticTypeId) =>
        new(
            ruleId,
            ProjectionSourceKind.SemanticElement,
            semanticTypeId,
            new BpmnNodeProjectionRule(
                ruleId,
                requiresNameLabel: true,
                nameLabelPlacement: new NodeLabelPlacement(
                    NodeLabelPlacementKind.OutsideBelow,
                    gap: 8d,
                    maximumWidth: 160d),
                nameLabelInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
                geometryInteractionPolicy:
                    NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize));

    private readonly struct M3RegistrationMarker;

    private readonly struct M31RegistrationMarker;

    private readonly struct M32RegistrationMarker;

    private readonly struct M321RegistrationMarker;

    private readonly struct M322RegistrationMarker;

    private readonly struct M323RegistrationMarker;

    private readonly struct M33RegistrationMarker;

    private readonly struct M34RegistrationMarker;

    private readonly struct N1RegistrationMarker;

    private readonly struct N2RegistrationMarker;

    private readonly struct N3RegistrationMarker;

    private readonly struct N31RegistrationMarker;

    private readonly struct N314RegistrationMarker;

    private readonly struct N4RegistrationMarker;

    private readonly struct N5RegistrationMarker;

    private readonly struct N6RegistrationMarker;

    private readonly struct N7RegistrationMarker;

    private readonly struct N81RegistrationMarker;

    private readonly struct N82RegistrationMarker;

    private readonly struct N90RegistrationMarker;

    private readonly struct N91RegistrationMarker;

    private readonly struct N100RegistrationMarker;

    private readonly struct N101RegistrationMarker;
}
