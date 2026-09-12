using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Projection;

internal static class BpmnProjectionIdentities
{
    internal const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";

    internal static ProjectionRuleId StartEventRuleId { get; } =
        new("bpmn:projection/start-event");

    internal static ProjectionRuleId TaskRuleId { get; } =
        new("bpmn:projection/task");

    internal static ProjectionRuleId UserTaskRuleId { get; } =
        new("bpmn:projection/user-task");

    internal static ProjectionRuleId ManualTaskRuleId { get; } =
        new("bpmn:projection/manual-task");

    internal static ProjectionRuleId ServiceTaskRuleId { get; } =
        new("bpmn:projection/service-task");

    internal static ProjectionRuleId SendTaskRuleId { get; } =
        new("bpmn:projection/send-task");

    internal static ProjectionRuleId ReceiveTaskRuleId { get; } =
        new("bpmn:projection/receive-task");

    internal static ProjectionRuleId SubProcessRuleId { get; } =
        new("bpmn:projection/subprocess");

    internal static ProjectionRuleId ExclusiveGatewayRuleId { get; } =
        new("bpmn:projection/exclusive-gateway");

    internal static ProjectionRuleId ParallelGatewayRuleId { get; } =
        new("bpmn:projection/parallel-gateway");

    internal static ProjectionRuleId InclusiveGatewayRuleId { get; } =
        new("bpmn:projection/inclusive-gateway");

    internal static ProjectionRuleId EventBasedGatewayRuleId { get; } =
        new("bpmn:projection/event-based-gateway");

    internal static ProjectionRuleId MessageCatchEventRuleId { get; } =
        new("bpmn:projection/message-catch-event");

    internal static ProjectionRuleId MessageThrowEventRuleId { get; } =
        new("bpmn:projection/message-throw-event");

    internal static ProjectionRuleId MessageBoundaryEventRuleId { get; } =
        new("bpmn:projection/message-boundary-event");

    internal static ProjectionRuleId TimerCatchEventRuleId { get; } =
        new("bpmn:projection/timer-catch-event");

    internal static ProjectionRuleId TimerBoundaryEventRuleId { get; } =
        new("bpmn:projection/timer-boundary-event");

    internal static ProjectionRuleId SignalCatchEventRuleId { get; } =
        new("bpmn:projection/signal-catch-event");

    internal static ProjectionRuleId SignalThrowEventRuleId { get; } =
        new("bpmn:projection/signal-throw-event");

    internal static ProjectionRuleId SignalBoundaryEventRuleId { get; } =
        new("bpmn:projection/signal-boundary-event");

    internal static ProjectionRuleId EndEventRuleId { get; } =
        new("bpmn:projection/end-event");

    internal static ProjectionRuleId SequenceFlowRuleId { get; } =
        new("bpmn:projection/sequence-flow");

    internal static ProjectionRuleId NodeRuleId(SemanticTypeId typeId)
    {
        if (typeId == BpmnSemanticTypes.StartEvent)
        {
            return StartEventRuleId;
        }

        if (BpmnTaskSemanticTypes.IsTask(typeId))
        {
            return ResolveTaskRuleId(typeId);
        }

        if (typeId == BpmnSemanticTypes.SubProcess)
        {
            return SubProcessRuleId;
        }

        if (typeId == BpmnSemanticTypes.ExclusiveGateway)
        {
            return ExclusiveGatewayRuleId;
        }

        if (typeId == BpmnSemanticTypes.ParallelGateway)
        {
            return ParallelGatewayRuleId;
        }

        if (typeId == BpmnSemanticTypes.InclusiveGateway)
        {
            return InclusiveGatewayRuleId;
        }

        if (typeId == BpmnSemanticTypes.EventBasedGateway)
        {
            return EventBasedGatewayRuleId;
        }

        if (typeId == BpmnSemanticTypes.MessageCatchEvent)
        {
            return MessageCatchEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.MessageThrowEvent)
        {
            return MessageThrowEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.MessageBoundaryEvent)
        {
            return MessageBoundaryEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.TimerCatchEvent)
        {
            return TimerCatchEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            return TimerBoundaryEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.SignalCatchEvent)
        {
            return SignalCatchEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.SignalThrowEvent)
        {
            return SignalThrowEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.SignalBoundaryEvent)
        {
            return SignalBoundaryEventRuleId;
        }

        if (typeId == BpmnSemanticTypes.EndEvent)
        {
            return EndEventRuleId;
        }

        throw new ArgumentException("The semantic type is not a supported BPMN flow node.", nameof(typeId));
    }

    internal static ProjectionRuleId ResolveTaskRuleId(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (typeId == BpmnSemanticTypes.Task)
        {
            return TaskRuleId;
        }

        if (typeId == BpmnSemanticTypes.UserTask)
        {
            return UserTaskRuleId;
        }

        if (typeId == BpmnSemanticTypes.ManualTask)
        {
            return ManualTaskRuleId;
        }

        if (typeId == BpmnSemanticTypes.ServiceTask)
        {
            return ServiceTaskRuleId;
        }

        if (typeId == BpmnSemanticTypes.SendTask)
        {
            return SendTaskRuleId;
        }

        if (typeId == BpmnSemanticTypes.ReceiveTask)
        {
            return ReceiveTaskRuleId;
        }

        throw new ArgumentException(
            $"Semantic type '{typeId}' is not a supported BPMN Task.",
            nameof(typeId));
    }

    internal static string AnchorLocalKey(ConnectorAnchorId anchorId) =>
        $"connector-anchor:{anchorId.Value}";
}

internal sealed class BpmnNodeProjectionRule : IProjectionRule
{
    private readonly ProjectionRuleId _ruleId;
    private readonly bool _requiresNameLabel;
    private readonly bool _projectsNameLabel;
    private readonly NodeLabelPlacement? _nameLabelPlacement;
    private readonly NodeLabelInteractionPolicy _nameLabelInteractionPolicy;
    private readonly NodeGeometryInteractionPolicy _geometryInteractionPolicy;

    internal BpmnNodeProjectionRule(
        ProjectionRuleId ruleId,
        bool requiresNameLabel,
        bool projectsNameLabel = true,
        NodeLabelPlacement? nameLabelPlacement = null,
        NodeLabelInteractionPolicy nameLabelInteractionPolicy =
            NodeLabelInteractionPolicy.Fixed,
        NodeGeometryInteractionPolicy geometryInteractionPolicy =
            NodeGeometryInteractionPolicy.FreeMoveAndResize)
    {
        if (requiresNameLabel && !projectsNameLabel)
        {
            throw new ArgumentException(
                "A required BPMN Name label cannot be suppressed.",
                nameof(projectsNameLabel));
        }

        _ruleId = ruleId;
        _requiresNameLabel = requiresNameLabel;
        _projectsNameLabel = projectsNameLabel;
        _nameLabelPlacement = nameLabelPlacement;
        _nameLabelInteractionPolicy = nameLabelInteractionPolicy;
        _geometryInteractionPolicy = geometryInteractionPolicy;
    }

    public ProjectionRuleResult Project(
        ProjectionRuleInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input is not ElementProjectionRuleInput element)
        {
            throw new ArgumentException("A BPMN node rule requires an element input.", nameof(input));
        }

        var visual = element.VisualStates.SingleOrDefault();
        var trace = Trace(input, _ruleId, "node", visual?.Id);
        ProjectedBoundaryAttachment? boundaryAttachment = null;
        if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(element.Element.TypeId) &&
            element.Element.AttachedToElementId is { } attachedToElementId &&
            visual?.BoundaryAttachment is { } placement)
        {
            boundaryAttachment = new ProjectedBoundaryAttachment(
                attachedToElementId,
                placement);
        }

        var node = new ProjectedNode(
            trace,
            visual is null
                ? null
                : new ProjectedPlacementHint(
                    visual.Position,
                    visual.Size,
                    visual.PlacementMode,
                    boundaryAttachment),
            semanticProperties: element.Element.Properties,
            projectedProperties:
            [
                new(
                    BpmnProjectionIdentities.ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(element.Element.TypeId.Value)),
            ],
            geometryInteractionPolicy: _geometryInteractionPolicy);
        return ProjectionRuleResult.Success(new ProjectionRuleContribution(
            nodes: [node],
            ports: CreatePorts(element, node, visual),
            labels: CreateLabels(element, node, visual?.Id)));
    }

    private IEnumerable<ProjectedPort> CreatePorts(
        ElementProjectionRuleInput input,
        ProjectedNode node,
        VisualStateSnapshot? visual)
    {
        if (visual is null)
        {
            return [];
        }

        var anchors = ElementConnectorAnchorResolver.Resolve(
            visual,
            input.Element.TypeId);
        var sideCounts = anchors
            .GroupBy(static anchor => anchor.Side)
            .ToDictionary(static group => group.Key, static group => group.Count());
        return anchors.Select(anchor => new ProjectedPort(
            Trace(
                input,
                _ruleId,
                BpmnProjectionIdentities.AnchorLocalKey(anchor.Id),
                visual.Id),
            node.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(
                ProjectedConnectorAnchor.FromResolved(
                    anchor,
                    sideCounts[anchor.Side]))));
    }

    private IEnumerable<ProjectedLabel> CreateLabels(
        ElementProjectionRuleInput input,
        ProjectedNode node,
        VisualStateId? visualStateId)
    {
        if (!_projectsNameLabel)
        {
            return [];
        }

        if (!input.Element.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) ||
            name.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(name.TextValue))
        {
            return _requiresNameLabel
                ? throw new InvalidOperationException(
                    "A projected BPMN node requires its Name property.")
                : [];
        }

        return
        [
            new ProjectedLabel(
                Trace(input, _ruleId, "name-label", visualStateId),
                node.Id,
                name.TextValue,
                semanticProperties: input.Element.Properties,
                nodePlacement: _nameLabelPlacement,
                nodeInteractionPolicy: _nameLabelInteractionPolicy),
        ];
    }

    private static ProjectionSourceTrace Trace(
        ProjectionRuleInput input,
        ProjectionRuleId ruleId,
        string localKey,
        VisualStateId? visualStateId) =>
        new(
            input.DocumentId,
            ruleId,
            input.SourceKind,
            input.SemanticId,
            input.SemanticTypeId,
            localKey,
            visualStateId);
}

internal sealed class BpmnSequenceFlowProjectionRule : IProjectionRule
{
    public ProjectionRuleResult Project(
        ProjectionRuleInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input is not RelationshipProjectionRuleInput relationship)
        {
            throw new ArgumentException(
                "A BPMN Sequence Flow rule requires a relationship input.",
                nameof(input));
        }

        var visual = relationship.VisualStates.SingleOrDefault();
        var trace = new ProjectionSourceTrace(
            input.DocumentId,
            BpmnProjectionIdentities.SequenceFlowRuleId,
            input.SourceKind,
            input.SemanticId,
            input.SemanticTypeId,
            "edge",
            visual?.Id);
        var edge = new ProjectedEdge(
            trace,
            NodeId(input.DocumentId, relationship.SourceElement),
            NodeId(input.DocumentId, relationship.TargetElement),
            sourcePortId: visual?.SourceAnchorId is { } sourceAnchorId
                ? AnchorPortId(
                    input.DocumentId,
                    relationship.SourceElement,
                    sourceAnchorId)
                : null,
            targetPortId: visual?.TargetAnchorId is { } targetAnchorId
                ? AnchorPortId(
                    input.DocumentId,
                    relationship.TargetElement,
                    targetAnchorId)
                : null,
            persistentRoute: visual?.Route,
            semanticProperties: relationship.Relationship.Properties,
            projectedProperties:
            [
                new(
                    BpmnProjectionIdentities.ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(relationship.Relationship.TypeId.Value)),
            ]);
        return ProjectionRuleResult.Success(new ProjectionRuleContribution(edges: [edge]));
    }

    private static ProjectedObjectId NodeId(
        DocumentId documentId,
        SemanticElementSnapshot element) =>
        ProjectedObjectIdentity.Create(
            documentId,
            BpmnProjectionIdentities.NodeRuleId(element.TypeId),
            ProjectionSourceKind.SemanticElement,
            element.Id,
            ProjectedObjectKind.Node,
            "node");

    private static ProjectedObjectId AnchorPortId(
        DocumentId documentId,
        SemanticElementSnapshot element,
        ConnectorAnchorId anchorId) =>
        ProjectedObjectIdentity.Create(
            documentId,
            BpmnProjectionIdentities.NodeRuleId(element.TypeId),
            ProjectionSourceKind.SemanticElement,
            element.Id,
            ProjectedObjectKind.Port,
            BpmnProjectionIdentities.AnchorLocalKey(anchorId));
}
