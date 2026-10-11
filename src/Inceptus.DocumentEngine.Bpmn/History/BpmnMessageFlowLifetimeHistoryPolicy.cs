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

/// <summary>Owns only one relationship, its visual/routing record, and an optional atomically created target anchor.</summary>
internal sealed class BpmnMessageFlowLifetimeHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        var identity = command switch
        {
            CreateBpmnMessageFlowCommand creation => (creation.RelationshipId, creation.VisualStateId, (VisualStateId?)null, (ConnectorAnchorId?)null),
            CreateBpmnMessageFlowWithTargetAnchorCommand creation => (creation.RelationshipId, creation.VisualStateId, (VisualStateId?)creation.TargetVisualStateId, (ConnectorAnchorId?)creation.TargetAnchorId),
            DeleteBpmnMessageFlowCommand deletion => (deletion.RelationshipId, deletion.ConnectorVisualStateId, (VisualStateId?)null, (ConnectorAnchorId?)null),
            _ => default,
        };
        if (identity.Item1 is null || identity.Item2 is null)
            return CommandHistoryPreparationResult.Failure(BpmnMessageFlowCommandSupport.Invalid(command));
        var previous = Capture(before, identity.Item1, identity.Item2, identity.Item3, identity.Item4);
        var current = Capture(committed, identity.Item1, identity.Item2, identity.Item3, identity.Item4);
        if ((previous is null) == (current is null) ||
            !before.SemanticModel.Elements.AsSpan().SequenceEqual(committed.SemanticModel.Elements.AsSpan()) ||
            !before.SemanticModel.Relationships.Where(flow => flow.Id != identity.Item1)
                .SequenceEqual(committed.SemanticModel.Relationships.Where(flow => flow.Id != identity.Item1)) ||
            !before.VisualModel.VisualStates.Where(visual => visual.Id != identity.Item2 && visual.Id != identity.Item3)
                .SequenceEqual(committed.VisualModel.VisualStates.Where(visual => visual.Id != identity.Item2 && visual.Id != identity.Item3)))
            return CommandHistoryPreparationResult.Failure(BpmnMessageFlowCommandSupport.Invalid(command));
        var owned = current ?? previous!;
        return CommandHistoryPreparationResult.Undoable(new Factory(owned, previous is not null), new Factory(owned, current is not null));
    }

    private static MessageFlowOwnedState? Capture(DocumentSnapshot document, SemanticElementId id, VisualStateId visualId,
        VisualStateId? targetVisualId, ConnectorAnchorId? targetAnchorId)
    {
        if (!BpmnMessageFlowCommandSupport.TryGet(document, id, visualId, out var relationship, out var visual)) return null;
        var owner = document.VisualModel.DocumentRoutingStates.SingleOrDefault(state => state.Connectors.Any(record => record.VisualStateId == visualId));
        var index = owner is null ? -1 : owner.Connectors.IndexOf(owner.Connectors.Single(record => record.VisualStateId == visualId));
        ConnectorAnchor? anchor = null;
        if (targetVisualId is not null && targetAnchorId is not null && document.VisualModel.TryGetVisualState(targetVisualId, out var target))
            anchor = target!.ConnectorAnchors.Single(candidate => candidate.Id == targetAnchorId);
        return new MessageFlowOwnedState(relationship!, visual!, owner?.OwnerId, index,
            owner?.Connectors.Single(record => record.VisualStateId == visualId), targetVisualId, anchor);
    }

    private sealed class Factory(MessageFlowOwnedState state, bool exists) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new RestoreBpmnMessageFlowCommand(documentId, expectedRevision, state, exists);
    }
}

internal sealed record MessageFlowOwnedState(SemanticRelationshipSnapshot Relationship, VisualStateSnapshot Visual,
    SemanticElementId? RoutingOwner, int RoutingIndex, ConnectorRoutingRecord? RoutingRecord,
    VisualStateId? TargetVisualId, ConnectorAnchor? OwnedTargetAnchor);

internal sealed class RestoreBpmnMessageFlowCommand(DocumentId documentId, DocumentRevision revision,
    MessageFlowOwnedState state, bool exists) : ICommand, ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } = new("bpmn:command/restore-message-flow");
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId => documentId;
    public DocumentRevision ExpectedRevision => revision;
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel | AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.ConnectorOnly;
    internal MessageFlowOwnedState State => state;
    internal bool Exists => exists;
}

internal sealed class RestoreBpmnMessageFlowCommandHandler : ICommandHandler, ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command) => command is RestoreBpmnMessageFlowCommand
        ? [] : BpmnMessageFlowCommandSupport.Invalid(command);

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = (RestoreBpmnMessageFlowCommand)command;
        var state = restore.State;
        var exists = document.SemanticModel.TryGetRelationship(state.Relationship.Id, out var current);
        if (exists == restore.Exists || exists && current!.TypeId != BpmnSemanticTypes.MessageFlow ||
            !exists && (document.SemanticModel.TryGetElement(state.Relationship.Id, out _) ||
                document.VisualModel.TryGetVisualState(state.Visual.Id, out _)))
            return ValueTask.FromResult(CommandHandlerResult.Failure(BpmnMessageFlowCommandSupport.Invalid(command)));
        var relationships = document.SemanticModel.Relationships.Where(flow => flow.Id != state.Relationship.Id);
        var visuals = document.VisualModel.VisualStates.Where(visual => visual.Id != state.Visual.Id).ToList();
        if (state.TargetVisualId is { } targetVisualId && state.OwnedTargetAnchor is { } ownedAnchor)
        {
            var target = visuals.SingleOrDefault(visual => visual.Id == targetVisualId);
            if (target is null) return Failure();
            VisualStateSnapshot changed;
            if (restore.Exists)
            {
                if (visuals.SelectMany(visual => visual.ConnectorAnchors).Any(anchor => anchor.Id == ownedAnchor.Id)) return Failure();
                changed = ConnectorAnchorInsertion.Insert(target, ownedAnchor.Id, ownedAnchor.Side, ownedAnchor.Role,
                    Math.Min(ownedAnchor.Order, target.ConnectorAnchors.Count(anchor => anchor.Side == ownedAnchor.Side)));
            }
            else
            {
                if (document.VisualModel.VisualStates.Any(visual => visual.Id != state.Visual.Id &&
                    (visual.SourceAnchorId == ownedAnchor.Id || visual.TargetAnchorId == ownedAnchor.Id))) return Failure();
                var removed = target.ConnectorAnchors.SingleOrDefault(anchor => anchor.Id == ownedAnchor.Id);
                if (removed is null) return Failure();
                var anchors = target.ConnectorAnchors.Where(anchor => anchor.Id != ownedAnchor.Id).Select(anchor =>
                    anchor.Side == removed.Side && anchor.Order > removed.Order
                        ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order - 1, anchor.NormalizedPosition) : anchor);
                changed = new VisualStateSnapshot(target.Id, target.SemanticElementId, target.Position, target.Size, target.PlacementMode,
                    target.Route, target.Properties, anchors, target.SourceAnchorId, target.TargetAnchorId, target.BoundaryAttachment);
            }
            visuals[visuals.IndexOf(target)] = changed;
        }
        var routing = BpmnMessageFlowCommandSupport.WithoutRoutingRecord(document, state.Visual.Id).ToList();
        if (restore.Exists)
        {
            relationships = relationships.Append(state.Relationship);
            visuals.Add(state.Visual);
            if (state.RoutingOwner is { } owner && state.RoutingRecord is { } record)
            {
                var currentOwner = routing.SingleOrDefault(candidate => candidate.OwnerId == owner);
                var records = currentOwner?.Connectors.ToList() ?? [];
                records.Insert(Math.Clamp(state.RoutingIndex, 0, records.Count), record);
                routing.RemoveAll(candidate => candidate.OwnerId == owner);
                routing.Add(new DocumentRoutingSnapshot(owner, records));
            }
        }
        var proposed = BpmnMessageFlowCommandSupport.Replace(document, relationships, visuals, routing);
        if (restore.Exists)
        {
            var diagnostics = BpmnMessageFlowSemantics.ValidatePresentation(proposed, state.Relationship.SourceId, state.Relationship.TargetId)
                .AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(proposed, state.Relationship.SourceId,
                    state.Visual.SourceAnchorId!, ConnectorAnchorRole.Source, state.Visual.Id, ConnectorEndpointKind.Source))
                .AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(proposed, state.Relationship.TargetId,
                    state.Visual.TargetAnchorId!, ConnectorAnchorRole.Target, state.Visual.Id, ConnectorEndpointKind.Target));
            if (!diagnostics.IsEmpty) return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }
        return ValueTask.FromResult(CommandHandlerResult.Success(proposed, pipelineInvalidation: CommandPipelineInvalidation.ConnectorOnly,
            nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));

        ValueTask<CommandHandlerResult> Failure() => ValueTask.FromResult(CommandHandlerResult.Failure(BpmnMessageFlowCommandSupport.Invalid(command)));
    }
}
