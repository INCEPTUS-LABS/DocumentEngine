using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>One atomic flow creation, including only the endpoint anchors that do not yet exist.</summary>
public sealed class CreateBpmnElementFlowCommand(DocumentId targetDocumentId, DocumentRevision expectedRevision,
    SemanticTypeId flowType, SemanticElementId relationshipId, VisualStateId visualStateId,
    SemanticElementId sourceId, SemanticElementId targetId, VisualStateId sourceVisualId, VisualStateId targetVisualId,
    ConnectorAnchorId sourceAnchorId, ConnectorAnchorId targetAnchorId,
    ConnectorAnchor? newSourceAnchor = null, ConnectorAnchor? newTargetAnchor = null) : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/create-element-flow");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId => targetDocumentId;
    public DocumentRevision ExpectedRevision => expectedRevision;
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.ConnectorOnly;
    public SemanticTypeId FlowType => flowType;
    public SemanticElementId RelationshipId => relationshipId;
    public VisualStateId VisualStateId => visualStateId;
    public SemanticElementId SourceId => sourceId;
    public SemanticElementId TargetId => targetId;
    public VisualStateId SourceVisualId => sourceVisualId;
    public VisualStateId TargetVisualId => targetVisualId;
    public ConnectorAnchorId SourceAnchorId => sourceAnchorId;
    public ConnectorAnchorId TargetAnchorId => targetAnchorId;
    public ConnectorAnchor? NewSourceAnchor => newSourceAnchor;
    public ConnectorAnchor? NewTargetAnchor => newTargetAnchor;
}

internal sealed class BpmnElementFlowCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not CreateBpmnElementFlowCommand flow || !TryPrepare(flow, document, out var prepared))
            return Invalid();
        return flow.FlowType == BpmnSemanticTypes.MessageFlow
            ? new BpmnMessageFlowCreationCommandHandler().Validate(Message(flow), prepared!)
            : BpmnSequenceFlowCreationValidation.Validate(Sequence(flow), prepared!);
    }

    public async ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        if (!diagnostics.IsEmpty) return CommandHandlerResult.Failure(diagnostics);
        var flow = (CreateBpmnElementFlowCommand)command;
        TryPrepare(flow, document, out var prepared);
        return flow.FlowType == BpmnSemanticTypes.MessageFlow
            ? await new BpmnMessageFlowCreationCommandHandler().HandleAsync(Message(flow), prepared!, cancellationToken).ConfigureAwait(false)
            : await new BpmnSequenceFlowCreationCommandHandler().HandleAsync(Sequence(flow), prepared!, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryPrepare(CreateBpmnElementFlowCommand flow, DocumentSnapshot document, out DocumentSnapshot? prepared)
    {
        prepared = document;
        if (flow.TargetDocumentId != document.DocumentId || flow.ExpectedRevision != document.Revision ||
            flow.FlowType != BpmnSemanticTypes.SequenceFlow && flow.FlowType != BpmnSemanticTypes.MessageFlow ||
            flow.SourceAnchorId == flow.TargetAnchorId) return false;
        return Endpoint(flow.SourceId, flow.SourceVisualId, flow.SourceAnchorId, flow.NewSourceAnchor, ConnectorAnchorRole.Source, ref prepared) &&
            Endpoint(flow.TargetId, flow.TargetVisualId, flow.TargetAnchorId, flow.NewTargetAnchor, ConnectorAnchorRole.Target, ref prepared);
    }

    private static bool Endpoint(SemanticElementId id, VisualStateId visualId, ConnectorAnchorId anchorId,
        ConnectorAnchor? added, ConnectorAnchorRole role, ref DocumentSnapshot? document)
    {
        if (!document!.SemanticModel.TryGetElement(id, out var element) || element is null) return false;
        if (!document.VisualModel.TryGetVisualState(visualId, out var visual))
        {
            if (added is null || !(OrganizationalSemantics.IsPool(element) || element.TypeId == BpmnSemanticTypes.Participant) ||
                document.VisualModel.VisualStates.Any(existing => existing.SemanticElementId == id)) return false;
            visual = new VisualStateSnapshot(visualId, id, default, default, VisualPlacementMode.Derived);
            document = BpmnMessageFlowCommandSupport.Replace(document, document.SemanticModel.Relationships,
                document.VisualModel.VisualStates.Append(visual));
        }
        if (visual?.SemanticElementId != id) return false;
        var policy = BpmnConnectorAnchorPolicies.Provider.Resolve(element.TypeId);
        var visualModel = document.VisualModel;
        var free = ElementConnectorAnchorResolver.Resolve(visual, policy).Where(anchor => anchor.Allows(role) &&
            !ConnectorAnchorOccupancy.IsOccupied(visualModel, anchor.Id)).ToArray();
        if (added is null) return free.Any(anchor => anchor.Id == anchorId);
        if (free.Length != 0 || added.Id != anchorId || added.Role != role || added.NormalizedPosition is null ||
            ConnectorAnchorReferenceIdentity.IsPredefinedReference(anchorId) ||
            document.VisualModel.VisualStates.SelectMany(candidate => candidate.ConnectorAnchors).Any(anchor => anchor.Id == anchorId) ||
            !ElementConnectorAnchorPolicyEvaluator.CanAdd(policy, visual, added.Side, role) ||
            added.Order != visual.ConnectorAnchors.Count(anchor => anchor.Side == added.Side)) return false;
        var count = visual.ConnectorAnchors.Count(anchor => anchor.Side == added.Side);
        var retained = visual.ConnectorAnchors.Select(anchor => anchor.Side == added.Side && anchor.NormalizedPosition is null
            ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order,
                ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order, count)) : anchor).ToArray();
        if (retained.Any(anchor => anchor.Side == added.Side && anchor.NormalizedPosition == added.NormalizedPosition)) return false;
        document = BpmnDocumentReplacement.ReplaceVisual(document, CopyAnchors(visual, retained.Append(added)));
        return true;
    }

    internal static VisualStateSnapshot CopyAnchors(VisualStateSnapshot visual, IEnumerable<ConnectorAnchor> anchors) =>
        new(visual.Id, visual.SemanticElementId, visual.Position, visual.Size, visual.PlacementMode, visual.Route,
            visual.Properties, anchors, visual.SourceAnchorId, visual.TargetAnchorId, visual.BoundaryAttachment);
    private static CreateBpmnSequenceFlowCommand Sequence(CreateBpmnElementFlowCommand flow) =>
        new(flow.TargetDocumentId, flow.ExpectedRevision, flow.RelationshipId, flow.VisualStateId,
            flow.SourceId, flow.TargetId, flow.SourceAnchorId, flow.TargetAnchorId);
    private static CreateBpmnMessageFlowCommand Message(CreateBpmnElementFlowCommand flow) =>
        new(flow.TargetDocumentId, flow.ExpectedRevision, flow.RelationshipId, flow.VisualStateId,
            flow.SourceId, flow.TargetId, flow.SourceAnchorId, flow.TargetAnchorId);
    internal static ImmutableArray<Diagnostic> Invalid() => [new("BPMN_ELEMENT_FLOW_POINT_PLAN_INVALID", DiagnosticSeverity.Error,
        "The endpoint point plan is stale, occupied, incompatible, or would create a point while a reusable point exists.")];
}
