using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnSequenceFlowDeletionHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not DeleteBpmnSequenceFlowCommand deletion ||
            !IsExactConnectionDeletion(deletion, before, committed))
        {
            return Failure(command, "BPMN Sequence Flow deletion History requires one exact relationship and connector Visual State removal.");
        }

        return CommandHistoryPreparationResult.Undoable(
            new BpmnDeletionSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel,
                NodeGeometryPipelineImpact.PreserveAll),
            new BpmnDeletionSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel,
                NodeGeometryPipelineImpact.PreserveAll));
    }

    private static bool IsExactConnectionDeletion(
        DeleteBpmnSequenceFlowCommand deletion,
        DocumentSnapshot before,
        DocumentSnapshot committed) =>
        before.DocumentId == committed.DocumentId &&
        before.SemanticModel.TryGetRelationship(deletion.RelationshipId, out _) &&
        before.VisualModel.TryGetVisualState(deletion.ConnectorVisualStateId, out _) &&
        before.SemanticModel.Elements.AsSpan().SequenceEqual(
            committed.SemanticModel.Elements.AsSpan()) &&
        before.SemanticModel.Relationships
            .Where(relationship => relationship.Id != deletion.RelationshipId)
            .SequenceEqual(committed.SemanticModel.Relationships) &&
        before.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
            committed.SemanticModel.NestedScopes.AsSpan()) &&
        before.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
            committed.SemanticModel.ScopeMemberships.AsSpan()) &&
        before.SemanticModel.ModelProfiles.Equals(
            committed.SemanticModel.ModelProfiles) &&
        ProfileRecordsMatch(before, committed) &&
        before.VisualModel.VisualStates
            .Where(visual => visual.Id != deletion.ConnectorVisualStateId)
            .SequenceEqual(committed.VisualModel.VisualStates) &&
        MetadataMatches(before, committed);

    internal static bool MetadataMatches(DocumentSnapshot before, DocumentSnapshot committed) =>
        before.Metadata.SystemManagedProperties.Equals(
            committed.Metadata.SystemManagedProperties) &&
        before.Metadata.ExtensionProperties.Equals(committed.Metadata.ExtensionProperties);

    internal static bool ProfileRecordsMatch(DocumentSnapshot before, DocumentSnapshot committed) =>
        before.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(
            committed.SemanticModel.ProfileAssignments.AsSpan()) &&
        before.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            committed.VisualModel.ProfileElementPresentations.AsSpan());

    internal static CommandHistoryPreparationResult Failure(ICommand command, string message) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                BpmnCommandDiagnosticCodes.HistoryInvalid,
                DiagnosticSeverity.Error,
                message,
                command.TypeId.Value),
        ]);
}

internal sealed class BpmnFlowNodeDeletionHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not DeleteBpmnFlowNodeCommand deletion ||
            !IsExactNodeCascade(deletion, before, committed, out var deletionPlan) ||
            deletionPlan is null)
        {
            return BpmnSequenceFlowDeletionHistoryPolicy.Failure(
                command,
                "BPMN flow-node deletion History requires one exact node and incident-relationship cascade.");
        }

        return CommandHistoryPreparationResult.Undoable(
            new BpmnDeletionSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel,
                NodeGeometryPipelineImpact.ForHistoricalRestoration(
                    deletionPlan.RemovedNodeVisualStateIds,
                    before.Revision)),
            new BpmnDeletionSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel,
                NodeGeometryPipelineImpact.ForRemovedVisualStates(
                    deletionPlan.RemovedNodeVisualStateIds)));
    }

    private static bool IsExactNodeCascade(
        DeleteBpmnFlowNodeCommand deletion,
        DocumentSnapshot before,
        DocumentSnapshot committed,
        out BpmnFlowNodeDeletionPlan? deletionPlan)
    {
        if (before.DocumentId != committed.DocumentId ||
            !before.SemanticModel.TryGetElement(deletion.ElementId, out var element) ||
            element is null ||
            !before.VisualModel.TryGetVisualState(deletion.VisualStateId, out _))
        {
            deletionPlan = null;
            return false;
        }

        if (!BpmnFlowNodeDeletionPlan.TryCreate(
                before,
                element,
                out deletionPlan,
                out _) ||
            deletionPlan is null)
        {
            return false;
        }

        var plan = deletionPlan;
        return before.SemanticModel.Elements
                .Where(candidate => !plan.RemovedElementIds.Contains(candidate.Id))
                .SequenceEqual(committed.SemanticModel.Elements) &&
            before.SemanticModel.Relationships
                .Where(relationship =>
                    !plan.RemovedRelationshipIds.Contains(relationship.Id))
                .SequenceEqual(committed.SemanticModel.Relationships) &&
            before.SemanticModel.NestedScopes
                .Where(scope => !plan.RemovedScopeIds.Contains(scope.Id))
                .SequenceEqual(committed.SemanticModel.NestedScopes) &&
            before.SemanticModel.ScopeMemberships
                .Where(membership =>
                    !plan.RemovedElementIds.Contains(membership.SemanticElementId))
                .SequenceEqual(committed.SemanticModel.ScopeMemberships) &&
            before.SemanticModel.ModelProfiles.Equals(
                committed.SemanticModel.ModelProfiles) &&
            before.SemanticModel.ProfileAssignments
                .Where(assignment =>
                    !plan.RemovedElementIds.Contains(assignment.SemanticElementId) &&
                    !plan.RemovedElementIds.Contains(assignment.ContainerSemanticElementId))
                .SequenceEqual(committed.SemanticModel.ProfileAssignments) &&
            before.VisualModel.ProfileElementPresentations
                .Where(presentation =>
                    !plan.RemovedElementIds.Contains(presentation.SemanticElementId))
                .SequenceEqual(committed.VisualModel.ProfileElementPresentations) &&
            before.VisualModel.VisualStates
                .Where(visual =>
                    !plan.RemovedSemanticIds.Contains(visual.SemanticElementId))
                .SequenceEqual(committed.VisualModel.VisualStates) &&
            BpmnSequenceFlowDeletionHistoryPolicy.MetadataMatches(before, committed);
    }
}

internal sealed class BpmnDeletionSnapshotRestoreCommandFactory : IHistoryCommandFactory
{
    private readonly SemanticModelSnapshot _semanticModel;
    private readonly VisualModelSnapshot _visualModel;
    private readonly NodeGeometryPipelineImpact _nodeGeometryImpact;

    internal BpmnDeletionSnapshotRestoreCommandFactory(
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel,
        NodeGeometryPipelineImpact nodeGeometryImpact)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(nodeGeometryImpact);
        _semanticModel = semanticModel;
        _visualModel = visualModel;
        _nodeGeometryImpact = nodeGeometryImpact;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new RestoreBpmnDeletionCommand(
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
            _nodeGeometryImpact);
}
