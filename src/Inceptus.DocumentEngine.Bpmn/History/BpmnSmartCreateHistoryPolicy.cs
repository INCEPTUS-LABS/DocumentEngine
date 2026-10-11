using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnSmartCreateHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        var create = (BpmnSmartCreateCommand)command;
        committed.VisualModel.TryGetVisualState(create.CreatedVisualStateId, out var node);
        committed.VisualModel.TryGetVisualState(create.ConnectorVisualStateId, out var connector);
        committed.SemanticModel.TryGetElement(node!.SemanticElementId, out var element);
        committed.SemanticModel.TryGetRelationship(connector!.SemanticElementId, out var flow);
        var owner = committed.VisualModel.RoutingScopes!.Value.Single(scope => scope.Connectors.Any(record => record.VisualStateId == connector.Id));
        var record = owner.Connectors.Single(record => record.VisualStateId == connector.Id);
        if (record.Outcome != ConnectorRoutingOutcome.Path)
            return CommandHistoryPreparationResult.Failure([new Diagnostic("BPMN_ELEMENT_TOOLBOX_FAILED", DiagnosticSeverity.Error,
                "The new Sequence Flow could not be routed.")]);
        var delta = new BpmnSmartCreationDelta(element!, node, flow!, connector,
            [.. committed.SemanticModel.NestedScopes.Where(scope => scope.OwnerSemanticElementId == element!.Id)],
            [.. committed.SemanticModel.ScopeMemberships.Where(membership => membership.SemanticElementId == element!.Id)],
            [.. committed.SemanticModel.ProfileAssignments.Where(assignment => assignment.SemanticElementId == element!.Id)],
            owner.ScopeId, record, owner.Connectors.IndexOf(record), committed.Revision);
        return CommandHistoryPreparationResult.Undoable(
            new Factory(delta, false, BpmnArrangeHistoryDelta.Capture(committed, before)),
            new Factory(delta, true, BpmnArrangeHistoryDelta.Capture(before, committed)));
    }

    private sealed class Factory(BpmnSmartCreationDelta delta, bool exists, BpmnArrangeHistoryDelta geometry) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) => new RestoreBpmnSmartCreateCommand(documentId, expectedRevision, delta, exists, geometry);
    }
}

internal sealed class BpmnSmartCreateReplayHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed) =>
        CommandHistoryPreparationResult.PreserveExistingHistory();
}

internal sealed record BpmnSmartCreationDelta(SemanticElementSnapshot Element, VisualStateSnapshot Node,
    SemanticRelationshipSnapshot Flow, VisualStateSnapshot Connector, ImmutableArray<DocumentScopeSnapshot> Scopes,
    ImmutableArray<SemanticElementScopeMembershipSnapshot> Memberships, ImmutableArray<ModelProfileElementAssignmentSnapshot> Assignments,
    DocumentScopeId ScopeId, ConnectorRoutingRecord Record, int RouteIndex, DocumentRevision CreatedRevision);

internal sealed class RestoreBpmnSmartCreateCommand(DocumentId id, DocumentRevision revision, BpmnSmartCreationDelta delta,
    bool exists, BpmnArrangeHistoryDelta geometry) : ICommand
{
    internal static CommandTypeId KnownTypeId { get; } = new("bpmn:command/restore-smart-create");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId => id;
    public DocumentRevision ExpectedRevision => revision;
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel;
    internal BpmnSmartCreationDelta Delta => delta;
    internal bool Exists => exists;
    internal BpmnArrangeHistoryDelta Geometry => geometry;
}

internal sealed class RestoreBpmnSmartCreateCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = (RestoreBpmnSmartCreateCommand)command;
        var delta = restore.Delta;
        var semantic = document.SemanticModel;
        if (semantic.TryGetElement(delta.Element.Id, out _) == restore.Exists ||
            semantic.TryGetRelationship(delta.Flow.Id, out _) == restore.Exists ||
            !restore.Exists && semantic.Relationships.Any(flow => flow.Id != delta.Flow.Id &&
                (flow.SourceId == delta.Element.Id || flow.TargetId == delta.Element.Id)))
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic("BPMN_SMART_CREATE_HISTORY_CONFLICT",
                DiagnosticSeverity.Error, "The created element or flow has incompatible subsequent changes.")]));
        var geometryResult = restore.Geometry.Apply(document);
        var visual = geometryResult.ProposedDocument!.VisualModel;
        var nodes = visual.VisualStates.Where(state => state.Id != delta.Node.Id && state.Id != delta.Connector.Id);
        var elements = semantic.Elements.Where(element => element.Id != delta.Element.Id);
        var flows = semantic.Relationships.Where(flow => flow.Id != delta.Flow.Id);
        var scopes = semantic.NestedScopes.Where(scope => !delta.Scopes.Any(added => added.Id == scope.Id));
        var members = semantic.ScopeMemberships.Where(member => member.SemanticElementId != delta.Element.Id);
        var assignments = semantic.ProfileAssignments.Where(assignment => assignment.SemanticElementId != delta.Element.Id);
        if (restore.Exists)
        {
            nodes = nodes.Append(delta.Node).Append(delta.Connector);
            elements = elements.Append(delta.Element); flows = flows.Append(delta.Flow);
            scopes = scopes.Concat(delta.Scopes); members = members.Concat(delta.Memberships); assignments = assignments.Concat(delta.Assignments);
        }
        var routingScopes = visual.RoutingScopes?.Where(scope => restore.Exists || !delta.Scopes.Any(added => added.Id == scope.ScopeId))
            .Select(scope =>
            {
                var records = scope.Connectors.Where(record => record.VisualStateId != delta.Connector.Id).ToList();
                if (restore.Exists && scope.ScopeId == delta.ScopeId) records.Insert(Math.Min(delta.RouteIndex, records.Count), delta.Record);
                return new ScopeRoutingSnapshot(scope.ScopeId, scope.Geometry, records);
            });
        var proposed = new DocumentSnapshot(new SemanticModelSnapshot(document.DocumentId, document.Revision, elements, flows,
                scopes, members, semantic.ModelProfiles, assignments),
            new VisualModelSnapshot(document.DocumentId, document.Revision, nodes, visual.ProfileElementPresentations,
                routingScopes, visual.DocumentRoutingStates), document.Metadata, document.Publication);
        return ValueTask.FromResult(CommandHandlerResult.SuccessWithPreparation(proposed, [], restore.Geometry.Heights, restore.Geometry.Widths,
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: restore.Exists
                ? NodeGeometryPipelineImpact.ForHistoricalRestoration([delta.Node.Id], delta.CreatedRevision)
                : NodeGeometryPipelineImpact.ForRemovedVisualStates([delta.Node.Id])));
    }
}
