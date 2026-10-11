using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnElementFlowHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (command is not CreateBpmnElementFlowCommand flow ||
            !committed.SemanticModel.TryGetRelationship(flow.RelationshipId, out var relationship) ||
            !committed.VisualModel.TryGetVisualState(flow.VisualStateId, out var visual))
            return CommandHistoryPreparationResult.Failure(BpmnElementFlowCommandHandler.Invalid());
        var anchors = ImmutableArray.CreateBuilder<ElementFlowAnchorDelta>();
        Capture(flow.SourceVisualId, flow.NewSourceAnchor);
        Capture(flow.TargetVisualId, flow.NewTargetAnchor);
        var scope = committed.VisualModel.RoutingScopes?.SingleOrDefault(scope => scope.Connectors.Any(record => record.VisualStateId == flow.VisualStateId));
        var owner = committed.VisualModel.DocumentRoutingStates.SingleOrDefault(owner => owner.Connectors.Any(record => record.VisualStateId == flow.VisualStateId));
        var records = scope?.Connectors ?? owner?.Connectors ?? [];
        var record = records.SingleOrDefault(record => record.VisualStateId == flow.VisualStateId);
        var delta = new ElementFlowDelta(relationship!, visual!, scope?.ScopeId, owner?.OwnerId,
            record is null ? -1 : records.IndexOf(record), record, anchors.ToImmutable());
        return CommandHistoryPreparationResult.Undoable(new Factory(delta, false), new Factory(delta, true));

        void Capture(VisualStateId visualId, ConnectorAnchor? added)
        {
            if (added is null) return;
            before.VisualModel.TryGetVisualState(visualId, out var original);
            committed.VisualModel.TryGetVisualState(visualId, out var current);
            anchors.Add(new(visualId, added, original?.ConnectorAnchors ?? [], current!.ConnectorAnchors,
                original is null ? BpmnElementFlowCommandHandler.CopyAnchors(current, []) : null));
        }
    }

    private sealed class Factory(ElementFlowDelta delta, bool exists) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) => new RestoreBpmnElementFlowCommand(documentId, expectedRevision, delta, exists);
    }
}

internal sealed record ElementFlowAnchorDelta(VisualStateId VisualId, ConnectorAnchor Added,
    ImmutableArray<ConnectorAnchor> Before, ImmutableArray<ConnectorAnchor> After, VisualStateSnapshot? CreatedOwner);
internal sealed record ElementFlowDelta(SemanticRelationshipSnapshot Relationship, VisualStateSnapshot Visual,
    DocumentScopeId? ScopeId, SemanticElementId? OwnerId, int Index, ConnectorRoutingRecord? Record,
    ImmutableArray<ElementFlowAnchorDelta> Anchors);
internal sealed class RestoreBpmnElementFlowCommand(DocumentId documentId, DocumentRevision revision, ElementFlowDelta delta, bool exists)
    : ICommand, ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } = new("bpmn:command/restore-element-flow");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId => documentId;
    public DocumentRevision ExpectedRevision => revision;
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.ConnectorOnly;
    internal ElementFlowDelta Delta => delta;
    internal bool Exists => exists;
}

internal sealed class RestoreBpmnElementFlowCommandHandler : ICommandHandler, ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command) => command is RestoreBpmnElementFlowCommand ? [] : BpmnElementFlowCommandHandler.Invalid();

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = (RestoreBpmnElementFlowCommand)command;
        var delta = restore.Delta;
        var exists = document.SemanticModel.TryGetRelationship(delta.Relationship.Id, out var relationship);
        if (exists == restore.Exists || exists && !relationship!.Equals(delta.Relationship) ||
            !exists && (document.SemanticModel.TryGetElement(delta.Relationship.Id, out _) || document.VisualModel.TryGetVisualState(delta.Visual.Id, out _))) return Failure();
        var visuals = document.VisualModel.VisualStates.Where(visual => visual.Id != delta.Visual.Id).ToList();
        foreach (var anchorDelta in delta.Anchors)
        {
            var visual = visuals.SingleOrDefault(visual => visual.Id == anchorDelta.VisualId);
            if (visual is null && restore.Exists && anchorDelta.CreatedOwner is { } created &&
                document.SemanticModel.TryGetElement(created.SemanticElementId, out _) &&
                !visuals.Any(item => item.SemanticElementId == created.SemanticElementId))
            {
                visual = created;
                visuals.Add(visual);
            }
            if (visual is null) return Failure();
            var anchors = visual.ConnectorAnchors.ToList();
            if (restore.Exists)
            {
                if (visuals.SelectMany(item => item.ConnectorAnchors).Any(anchor => anchor.Id == anchorDelta.Added.Id)) return Failure();
                var count = anchors.Count(anchor => anchor.Side == anchorDelta.Added.Side);
                anchors = anchors.Select(anchor => anchor.Side == anchorDelta.Added.Side && anchor.NormalizedPosition is null
                    ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order,
                        ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order, count)) : anchor).ToList();
                anchors.Add(new(anchorDelta.Added.Id, anchorDelta.Added.Side, anchorDelta.Added.Role, count, anchorDelta.Added.NormalizedPosition));
            }
            else
            {
                if (visuals.Any(item => item.SourceAnchorId == anchorDelta.Added.Id || item.TargetAnchorId == anchorDelta.Added.Id) ||
                    !anchors.Any(anchor => anchor.Id == anchorDelta.Added.Id)) return Failure();
                var removed = anchors.Single(anchor => anchor.Id == anchorDelta.Added.Id);
                anchors = anchors.Where(anchor => anchor.Id != removed.Id).Select(anchor => anchor.Side == removed.Side && anchor.Order > removed.Order
                    ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order - 1, anchor.NormalizedPosition) : anchor).ToList();
                var ownedIds = delta.Anchors.Where(item => item.VisualId == anchorDelta.VisualId).Select(item => item.Added.Id).ToHashSet();
                if (anchors.SequenceEqual(anchorDelta.After.Where(anchor => !ownedIds.Contains(anchor.Id))))
                    anchors = anchorDelta.Before.ToList();
            }
            if (!restore.Exists && anchorDelta.CreatedOwner is not null && anchors.Count == 0)
                visuals.Remove(visual);
            else visuals[visuals.IndexOf(visual)] = BpmnElementFlowCommandHandler.CopyAnchors(visual, anchors);
        }
        var relationships = document.SemanticModel.Relationships.Where(item => item.Id != delta.Relationship.Id);
        if (restore.Exists) { visuals.Add(delta.Visual); relationships = relationships.Append(delta.Relationship); }
        IEnumerable<ConnectorRoutingRecord> Records(IEnumerable<ConnectorRoutingRecord> current, bool owner)
        {
            var records = current.Where(record => record.VisualStateId != delta.Visual.Id).ToList();
            if (restore.Exists && owner && delta.Record is not null) records.Insert(Math.Clamp(delta.Index, 0, records.Count), delta.Record);
            return records;
        }
        var scopes = document.VisualModel.RoutingScopes?.Select(scope => new ScopeRoutingSnapshot(scope.ScopeId, scope.Geometry,
            Records(scope.Connectors, scope.ScopeId == delta.ScopeId)));
        var owners = document.VisualModel.DocumentRoutingStates.Select(owner => new DocumentRoutingSnapshot(owner.OwnerId,
            Records(owner.Connectors, owner.OwnerId == delta.OwnerId))).ToList();
        if (restore.Exists && delta.OwnerId is { } ownerId && owners.All(owner => owner.OwnerId != ownerId))
            owners.Add(new(ownerId, Records([], true)));
        var semantics = document.SemanticModel;
        var proposed = new DocumentSnapshot(new SemanticModelSnapshot(document.DocumentId, document.Revision, semantics.Elements,
            relationships, semantics.NestedScopes, semantics.ScopeMemberships, semantics.ModelProfiles, semantics.ProfileAssignments),
            new VisualModelSnapshot(document.DocumentId, document.Revision, visuals, document.VisualModel.ProfileElementPresentations,
                scopes, owners), document.Metadata, document.Publication);
        if (restore.Exists)
        {
            var diagnostics = delta.Relationship.TypeId == BpmnSemanticTypes.MessageFlow
                ? BpmnMessageFlowSemantics.ValidatePresentation(proposed, delta.Relationship.SourceId, delta.Relationship.TargetId)
                : BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(proposed, delta.Relationship.Id, delta.Relationship.SourceId, delta.Relationship.TargetId);
            diagnostics = diagnostics.AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(proposed, delta.Relationship.SourceId,
                delta.Visual.SourceAnchorId!, ConnectorAnchorRole.Source, delta.Visual.Id, ConnectorEndpointKind.Source))
                .AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(proposed, delta.Relationship.TargetId,
                    delta.Visual.TargetAnchorId!, ConnectorAnchorRole.Target, delta.Visual.Id, ConnectorEndpointKind.Target));
            if (!diagnostics.IsEmpty) return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }
        return ValueTask.FromResult(CommandHandlerResult.Success(proposed, pipelineInvalidation: CommandPipelineInvalidation.ConnectorOnly,
            nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
        static ValueTask<CommandHandlerResult> Failure() => ValueTask.FromResult(CommandHandlerResult.Failure(BpmnElementFlowCommandHandler.Invalid()));
    }
}
