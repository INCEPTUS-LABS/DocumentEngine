using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Projection;

internal sealed class BpmnMessageFlowProjectionRule : IProjectionRule
{
    internal static ProjectionRuleId RuleId { get; } = new("bpmn:projection/message-flow");
    internal static ProjectionRuleRegistration Registration { get; } = new(RuleId,
        ProjectionSourceKind.SemanticRelationship, BpmnSemanticTypes.MessageFlow, new BpmnMessageFlowProjectionRule());

    public ProjectionRuleResult Project(ProjectionRuleInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input is not RelationshipProjectionRuleInput relationship)
            throw new ArgumentException("A Message Flow projection requires a relationship input.", nameof(input));
        var visual = relationship.VisualStates.SingleOrDefault();
        var edge = new ProjectedEdge(Trace("edge"), NodeId(relationship.SourceElement), NodeId(relationship.TargetElement),
            visual?.SourceAnchorId is { } source ? PortId(relationship.SourceElement, source) : null,
            visual?.TargetAnchorId is { } target ? PortId(relationship.TargetElement, target) : null,
            persistentRoute: visual?.Route, semanticProperties: relationship.Relationship.Properties,
            projectedProperties: ConnectorVisualMetadata.Encode(BpmnMessageFlowVisuals.Descriptor).Append(
                KeyValuePair.Create(BpmnProjectionIdentities.ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(BpmnSemanticTypes.MessageFlow.Value))));
        var labels = new List<ProjectedLabel>();
        if (relationship.Relationship.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) &&
            name.Kind == PropertyValueKind.Text && !string.IsNullOrWhiteSpace(name.TextValue))
            labels.Add(new ProjectedLabel(Trace("name"), edge.Id, name.TextValue,
                semanticProperties: relationship.Relationship.Properties, connectorPlacement: new ConnectorLabelPlacementIntent(32d, 8d)));
        return ProjectionRuleResult.Success(new ProjectionRuleContribution(edges: [edge], labels: labels));

        ProjectionSourceTrace Trace(string key) => new(input.DocumentId, RuleId, input.SourceKind,
            input.SemanticId, input.SemanticTypeId, key, visual?.Id);
        ProjectedObjectId NodeId(SemanticElementSnapshot element) => ProjectedObjectIdentity.Create(input.DocumentId,
            NodeRuleId(element.TypeId), ProjectionSourceKind.SemanticElement, element.Id, ProjectedObjectKind.Node, "node");
        ProjectedObjectId PortId(SemanticElementSnapshot element, ConnectorAnchorId anchor) => ProjectedObjectIdentity.Create(input.DocumentId,
            NodeRuleId(element.TypeId), ProjectionSourceKind.SemanticElement, element.Id, ProjectedObjectKind.Port,
            BpmnProjectionIdentities.AnchorLocalKey(anchor));
    }

    private static ProjectionRuleId NodeRuleId(SemanticTypeId type) => type == BpmnSemanticTypes.Participant
        ? BpmnParticipantPresentationContribution.ProjectionRuleId : type == Inceptus.DocumentEngine.Organizational.Semantics.OrganizationalSemanticTypes.Pool
            ? Inceptus.DocumentEngine.Organizational.Projection.OrganizationalContainerProjection.PoolRuleId : BpmnProjectionIdentities.NodeRuleId(type);
}
