using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnCreationHistoryPolicy<TCommand> : ICommandHistoryPolicy
    where TCommand : class, ICommand
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not TCommand ||
            committed.SemanticModel.ElementCount + committed.SemanticModel.RelationshipCount !=
                before.SemanticModel.ElementCount + before.SemanticModel.RelationshipCount + 1 ||
            committed.VisualModel.Count != before.VisualModel.Count + 1 ||
            before.DocumentId != committed.DocumentId ||
            !before.SemanticModel.ModelProfiles.Equals(
                committed.SemanticModel.ModelProfiles) ||
            !BpmnSequenceFlowDeletionHistoryPolicy.ProfileRecordsMatch(before, committed) ||
            !before.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
                committed.SemanticModel.NestedScopes.AsSpan()) ||
            !HasExactScopeMembershipDelta(command, before, committed) ||
            !BpmnSequenceFlowDeletionHistoryPolicy.MetadataMatches(before, committed))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    BpmnCommandDiagnosticCodes.HistoryInvalid,
                    DiagnosticSeverity.Error,
                    "BPMN creation History requires exactly one semantic and one visual addition, plus only its required sparse process-scope membership.",
                    command.TypeId.Value),
            ]);
        }

        var pipelineInvalidation = CommandPipelineInvalidation.Resolve(command);
        var pinnedCreation = command as BpmnElementCreationCommand;
        var createdVisualStateId = pinnedCreation?.PlacementMode == VisualPlacementMode.Pinned
            ? pinnedCreation.VisualStateId
            : null;
        return CommandHistoryPreparationResult.Undoable(
            new BpmnSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel,
                pipelineInvalidation,
                createdVisualStateId is null ? null :
                    NodeGeometryPipelineImpact.ForRemovedVisualStates([createdVisualStateId])),
            new BpmnSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel,
                pipelineInvalidation,
                createdVisualStateId is null ? null :
                    NodeGeometryPipelineImpact.ForHistoricalRestoration(
                        [createdVisualStateId], committed.Revision)));
    }

    private static bool HasExactScopeMembershipDelta(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (command is not BpmnElementCreationCommand creation ||
            creation.TargetScopeId is null ||
            creation.TargetScopeId == before.SemanticModel.RootScopeId)
        {
            return before.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
                committed.SemanticModel.ScopeMemberships.AsSpan());
        }

        var committedMemberships = committed.SemanticModel.ScopeMemberships;
        var expectedMembership = new SemanticElementScopeMembershipSnapshot(
            creation.ElementId,
            creation.TargetScopeId);
        return committedMemberships.Length ==
                before.SemanticModel.ScopeMemberships.Length + 1 &&
            committedMemberships.Count(membership => membership == expectedMembership) == 1 &&
            before.SemanticModel.ScopeMemberships.SequenceEqual(
                committedMemberships.Where(membership => membership != expectedMembership));
    }
}

internal sealed class BpmnSubProcessCreationHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not CreateBpmnSubProcessCommand creation ||
            !IsExactSubProcessCreation(creation, before, committed))
        {
            return BpmnSequenceFlowDeletionHistoryPolicy.Failure(
                command,
                "BPMN SubProcess creation History requires one exact semantic element, Visual State, and owned child-scope addition.");
        }

        var pipelineInvalidation = CommandPipelineInvalidation.Resolve(command);
        var preservesNodeGeometry = creation.PlacementMode == VisualPlacementMode.Pinned;
        return CommandHistoryPreparationResult.Undoable(
            new BpmnSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel,
                pipelineInvalidation,
                preservesNodeGeometry
                    ? NodeGeometryPipelineImpact.ForRemovedVisualStates([creation.VisualStateId])
                    : null),
            new BpmnSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel,
                pipelineInvalidation,
                preservesNodeGeometry
                    ? NodeGeometryPipelineImpact.ForHistoricalRestoration(
                        [creation.VisualStateId], committed.Revision)
                    : null));
    }

    private static bool IsExactSubProcessCreation(
        CreateBpmnSubProcessCommand creation,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (before.DocumentId != committed.DocumentId ||
            before.SemanticModel.TryGetElement(creation.ElementId, out _) ||
            before.SemanticModel.TryGetRelationship(creation.ElementId, out _) ||
            before.VisualModel.TryGetVisualState(creation.VisualStateId, out _) ||
            before.SemanticModel.NestedScopes.Any(scope =>
                scope.Id == creation.ChildScopeId ||
                scope.OwnerSemanticElementId == creation.ElementId) ||
            committed.SemanticModel.ElementCount != before.SemanticModel.ElementCount + 1 ||
            committed.SemanticModel.RelationshipCount !=
                before.SemanticModel.RelationshipCount ||
            committed.VisualModel.Count != before.VisualModel.Count + 1 ||
            committed.SemanticModel.NestedScopes.Length !=
                before.SemanticModel.NestedScopes.Length + 1)
        {
            return false;
        }

        var membershipDelta = creation.ParentScopeId == before.SemanticModel.RootScopeId
            ? 0
            : 1;
        if (committed.SemanticModel.ScopeMemberships.Length !=
            before.SemanticModel.ScopeMemberships.Length + membershipDelta ||
            !committed.SemanticModel.TryGetElement(creation.ElementId, out var element) ||
            element is null ||
            !committed.VisualModel.TryGetVisualState(
                creation.VisualStateId,
                out var visual) ||
            visual is null)
        {
            return false;
        }

        var expectedElement = BpmnSemanticFactory.CreateSubProcess(
            creation.ElementId,
            creation.Code,
            creation.Name,
            creation.Description);
        var expectedVisual = new VisualStateSnapshot(
            creation.VisualStateId,
            creation.ElementId,
            creation.Position,
            creation.Size,
            creation.PlacementMode);
        var expectedChildScope = new DocumentScopeSnapshot(
            creation.ChildScopeId,
            creation.ParentScopeId,
            creation.ElementId);
        var childScope = committed.SemanticModel.NestedScopes.SingleOrDefault(scope =>
            scope.Id == creation.ChildScopeId);
        var membershipIsExact = creation.ParentScopeId == before.SemanticModel.RootScopeId
            ? committed.SemanticModel.ScopeMemberships.All(membership =>
                membership.SemanticElementId != creation.ElementId)
            : committed.SemanticModel.ScopeMemberships.Any(membership =>
                membership.SemanticElementId == creation.ElementId &&
                membership.ScopeId == creation.ParentScopeId);

        return element.Equals(expectedElement) &&
            visual.Equals(expectedVisual) &&
            childScope == expectedChildScope &&
            membershipIsExact &&
            before.SemanticModel.Elements.SequenceEqual(
                committed.SemanticModel.Elements.Where(candidate =>
                    candidate.Id != creation.ElementId)) &&
            before.SemanticModel.Relationships.AsSpan().SequenceEqual(
                committed.SemanticModel.Relationships.AsSpan()) &&
            before.SemanticModel.NestedScopes.SequenceEqual(
                committed.SemanticModel.NestedScopes.Where(scope =>
                    scope.Id != creation.ChildScopeId)) &&
            before.SemanticModel.ScopeMemberships.SequenceEqual(
                committed.SemanticModel.ScopeMemberships.Where(membership =>
                    membership.SemanticElementId != creation.ElementId)) &&
            before.SemanticModel.ModelProfiles.Equals(
                committed.SemanticModel.ModelProfiles) &&
            BpmnSequenceFlowDeletionHistoryPolicy.ProfileRecordsMatch(before, committed) &&
            before.VisualModel.VisualStates.SequenceEqual(
                committed.VisualModel.VisualStates.Where(candidate =>
                    candidate.Id != creation.VisualStateId)) &&
            BpmnSequenceFlowDeletionHistoryPolicy.MetadataMatches(before, committed);
    }
}

internal abstract class BpmnBoundaryEventCreationHistoryPolicy<TCommand> :
    ICommandHistoryPolicy
    where TCommand : class, ICommand
{
    private readonly SemanticTypeId _semanticTypeId;

    protected BpmnBoundaryEventCreationHistoryPolicy(SemanticTypeId semanticTypeId)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        if (!BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            throw new ArgumentException(
                $"Semantic type '{semanticTypeId}' is not a supported BPMN Boundary Event.",
                nameof(semanticTypeId));
        }

        _semanticTypeId = semanticTypeId;
    }

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not TCommand ||
            !BpmnBoundaryEventCreationCommandData.TryResolve(command, out var creation) ||
            creation is null ||
            creation.SemanticTypeId != _semanticTypeId ||
            !IsExactCreation(creation, before, committed))
        {
            return BpmnSequenceFlowDeletionHistoryPolicy.Failure(
                command,
                $"BPMN {BpmnBoundaryEventSemanticTypes.DisplayName(_semanticTypeId)} creation History requires one exact attached semantic element, Visual State, and derived sparse process-scope membership.");
        }

        var pipelineInvalidation = CommandPipelineInvalidation.Resolve(command);
        return CommandHistoryPreparationResult.Undoable(
            new BpmnSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel,
                pipelineInvalidation),
            new BpmnSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel,
                pipelineInvalidation));
    }

    private static bool IsExactCreation(
        BpmnBoundaryEventCreationCommandData creation,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (before.DocumentId != committed.DocumentId ||
            before.SemanticModel.TryGetElement(creation.ElementId, out _) ||
            before.SemanticModel.TryGetRelationship(creation.ElementId, out _) ||
            before.VisualModel.TryGetVisualState(creation.VisualStateId, out _) ||
            !before.SemanticModel.TryGetElement(
                creation.AttachedToActivityId,
                out var owner) ||
            owner is null ||
            !BpmnActivitySemanticTypes.IsActivity(owner.TypeId))
        {
            return false;
        }

        var ownerVisuals = before.VisualModel.VisualStates
            .Where(visual => visual.SemanticElementId == owner.Id)
            .ToArray();
        if (ownerVisuals.Length != 1)
        {
            return false;
        }

        var ownerScopeId = before.SemanticModel.GetScope(owner.Id).Id;
        var membershipDelta = ownerScopeId == before.SemanticModel.RootScopeId ? 0 : 1;
        if (committed.SemanticModel.ElementCount != before.SemanticModel.ElementCount + 1 ||
            committed.SemanticModel.RelationshipCount !=
                before.SemanticModel.RelationshipCount ||
            committed.VisualModel.Count != before.VisualModel.Count + 1 ||
            !before.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
                committed.SemanticModel.NestedScopes.AsSpan()) ||
            committed.SemanticModel.ScopeMemberships.Length !=
                before.SemanticModel.ScopeMemberships.Length + membershipDelta ||
            !committed.SemanticModel.TryGetElement(creation.ElementId, out var element) ||
            element is null ||
            !committed.VisualModel.TryGetVisualState(
                creation.VisualStateId,
                out var visual) ||
            visual is null)
        {
            return false;
        }

        var attachedBounds = creation.BoundaryAttachment.ResolveBounds(
            creation.EffectiveOwnerBounds,
            BpmnNodeLogicalSizePolicy.EventSize);
        var expectedElement = BpmnSemanticFactory.CreateBoundaryEvent(
            creation.ElementId,
            creation.SemanticTypeId,
            creation.AttachedToActivityId,
            creation.Name,
            creation.TimerDefinition,
            creation.CancelActivity,
            creation.Description);
        var expectedVisual = new VisualStateSnapshot(
            creation.VisualStateId,
            creation.ElementId,
            attachedBounds.TopLeft,
            BpmnNodeLogicalSizePolicy.EventSize,
            VisualPlacementMode.Manual,
            boundaryAttachment: creation.BoundaryAttachment);
        var membershipIsExact = ownerScopeId == before.SemanticModel.RootScopeId
            ? committed.SemanticModel.ScopeMemberships.All(membership =>
                membership.SemanticElementId != creation.ElementId)
            : committed.SemanticModel.ScopeMemberships.Count(membership =>
                membership.SemanticElementId == creation.ElementId &&
                membership.ScopeId == ownerScopeId) == 1;

        return element.Equals(expectedElement) &&
            visual.Equals(expectedVisual) &&
            membershipIsExact &&
            before.SemanticModel.Elements.SequenceEqual(
                committed.SemanticModel.Elements.Where(candidate =>
                    candidate.Id != creation.ElementId)) &&
            before.SemanticModel.Relationships.AsSpan().SequenceEqual(
                committed.SemanticModel.Relationships.AsSpan()) &&
            before.SemanticModel.ScopeMemberships.SequenceEqual(
                committed.SemanticModel.ScopeMemberships.Where(membership =>
                    membership.SemanticElementId != creation.ElementId)) &&
            before.SemanticModel.ModelProfiles.Equals(
                committed.SemanticModel.ModelProfiles) &&
            BpmnSequenceFlowDeletionHistoryPolicy.ProfileRecordsMatch(before, committed) &&
            before.VisualModel.VisualStates.SequenceEqual(
                committed.VisualModel.VisualStates.Where(candidate =>
                    candidate.Id != creation.VisualStateId)) &&
            BpmnSequenceFlowDeletionHistoryPolicy.MetadataMatches(before, committed);
    }
}

internal sealed class BpmnMessageBoundaryEventCreationHistoryPolicy :
    BpmnBoundaryEventCreationHistoryPolicy<CreateBpmnMessageBoundaryEventCommand>
{
    internal BpmnMessageBoundaryEventCreationHistoryPolicy()
        : base(BpmnSemanticTypes.MessageBoundaryEvent)
    {
    }
}

internal sealed class BpmnTimerBoundaryEventCreationHistoryPolicy :
    BpmnBoundaryEventCreationHistoryPolicy<CreateBpmnTimerBoundaryEventCommand>
{
    internal BpmnTimerBoundaryEventCreationHistoryPolicy()
        : base(BpmnSemanticTypes.TimerBoundaryEvent)
    {
    }
}

internal sealed class BpmnSignalBoundaryEventCreationHistoryPolicy :
    BpmnBoundaryEventCreationHistoryPolicy<CreateBpmnSignalBoundaryEventCommand>
{
    internal BpmnSignalBoundaryEventCreationHistoryPolicy()
        : base(BpmnSemanticTypes.SignalBoundaryEvent)
    {
    }
}

internal sealed class BpmnSnapshotRestoreCommandFactory : IHistoryCommandFactory
{
    private readonly SemanticModelSnapshot _semanticModel;
    private readonly VisualModelSnapshot _visualModel;
    private readonly PipelineInvalidation _pipelineInvalidation;
    private readonly NodeGeometryPipelineImpact? _nodeGeometryImpact;

    internal BpmnSnapshotRestoreCommandFactory(
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel,
        PipelineInvalidation pipelineInvalidation,
        NodeGeometryPipelineImpact? nodeGeometryImpact = null)
    {
        _semanticModel = semanticModel;
        _visualModel = visualModel;
        _pipelineInvalidation = CommandPipelineInvalidation.Validate(pipelineInvalidation);
        _nodeGeometryImpact = nodeGeometryImpact;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new RestoreBpmnCreationCommand(
            documentId,
            expectedRevision,
            new SemanticModelSnapshot(
                documentId,
                expectedRevision,
                _semanticModel.Elements,
                _semanticModel.Relationships,
                _semanticModel.NestedScopes,
                _semanticModel.ScopeMemberships,
                _semanticModel.ModelProfiles,
                _semanticModel.ProfileAssignments),
            new VisualModelSnapshot(
                documentId,
                expectedRevision,
                _visualModel.VisualStates,
                _visualModel.ProfileElementPresentations),
            _pipelineInvalidation,
            _nodeGeometryImpact);
}

internal sealed class RestoreBpmnCreationCommand : ICommand, ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/restore-creation-snapshot");

    internal RestoreBpmnCreationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel,
        PipelineInvalidation pipelineInvalidation,
        NodeGeometryPipelineImpact? nodeGeometryImpact = null)
    {
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        SemanticModel = semanticModel;
        VisualModel = visualModel;
        PipelineInvalidation = CommandPipelineInvalidation.Validate(pipelineInvalidation);
        NodeGeometryImpact = nodeGeometryImpact;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public PipelineInvalidation PipelineInvalidation { get; }

    internal NodeGeometryPipelineImpact? NodeGeometryImpact { get; }

    internal SemanticModelSnapshot SemanticModel { get; }

    internal VisualModelSnapshot VisualModel { get; }
}

internal sealed class RestoreBpmnCreationCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return command is RestoreBpmnCreationCommand restore
            ? ValueTask.FromResult(CommandHandlerResult.Success(
                BpmnDocumentReplacement.Replace(
                    document,
                    restore.SemanticModel,
                    restore.VisualModel),
                pipelineInvalidation: restore.PipelineInvalidation,
                nodeGeometryImpact: restore.NodeGeometryImpact))
            : ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN History restore request has an invalid shape.",
                    command.TypeId.Value),
            ]));
    }
}
