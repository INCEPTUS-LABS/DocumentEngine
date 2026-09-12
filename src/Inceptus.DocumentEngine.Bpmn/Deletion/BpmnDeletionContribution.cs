using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Deletion;

internal static class BpmnDeletionContribution
{
    internal static DiagramDeletionId DeletionId { get; } =
        new("bpmn:deletion/flow-node-or-sequence-flow");

    internal static ImmutableArray<DiagramDeletionRegistration> Registrations { get; } =
    [
        new DiagramDeletionRegistration(
            DeletionId,
            new BpmnDeletionCommandFactory()),
    ];
}

internal sealed class BpmnDeletionCommandFactory : IDiagramDeletionCommandFactory
{
    public bool CanDelete(DiagramDeletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryCreateCommand(request, out var command) &&
            command is not null &&
            Validate(command, request.Document).All(static diagnostic =>
                diagnostic.Severity != DiagnosticSeverity.Error);
    }

    public DiagramDeletionPlanResult CreatePlan(DiagramDeletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryCreateCommand(request, out var command) || command is null)
        {
            return Failure(request, "The target is not a current supported BPMN flow node or Sequence Flow.");
        }

        var diagnostics = Validate(command, request.Document);
        return diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            ? DiagramDeletionPlanResult.Failure(diagnostics)
            : DiagramDeletionPlanResult.Success(
                new DiagramDeletionPlan(
                    command,
                    request.TargetKind,
                    request.SemanticId,
                    request.VisualStateId),
                diagnostics);
    }

    private static bool TryCreateCommand(
        DiagramDeletionRequest request,
        out Contracts.Commands.ICommand? command)
    {
        command = null;
        if (request.VisualStateId is null ||
            !request.Document.VisualModel.TryGetVisualState(
                request.VisualStateId,
                out var visual) ||
            visual is null ||
            visual.SemanticElementId != request.SemanticId)
        {
            return false;
        }

        if (request.TargetKind == DiagramDeletionTargetKind.Element &&
            request.Document.SemanticModel.TryGetElement(request.SemanticId, out var element) &&
            element is not null &&
            BpmnSemanticTypes.IsFlowNode(element.TypeId))
        {
            command = new DeleteBpmnFlowNodeCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                request.SemanticId,
                request.VisualStateId);
            return true;
        }

        if (request.TargetKind == DiagramDeletionTargetKind.Connection &&
            request.Document.SemanticModel.TryGetRelationship(
                request.SemanticId,
                out var relationship) &&
            relationship is not null &&
            relationship.TypeId == BpmnSemanticTypes.SequenceFlow &&
            visual.SourceAnchorId is { } sourceAnchorId &&
            visual.TargetAnchorId is { } targetAnchorId)
        {
            command = new DeleteBpmnSequenceFlowCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                relationship.Id,
                visual.Id,
                relationship.SourceId,
                relationship.TargetId,
                sourceAnchorId,
                targetAnchorId);
            return true;
        }

        return false;
    }

    private static ImmutableArray<Diagnostic> Validate(
        Contracts.Commands.ICommand command,
        DocumentSnapshot document) => command switch
        {
            DeleteBpmnSequenceFlowCommand deletion =>
                BpmnDeletionValidation.Validate(deletion, document),
            DeleteBpmnFlowNodeCommand deletion =>
                BpmnDeletionValidation.Validate(deletion, document),
            _ => throw new InvalidOperationException(
                "The BPMN deletion factory created an unsupported Command."),
        };

    private static DiagramDeletionPlanResult Failure(
        DiagramDeletionRequest request,
        string message) =>
        DiagramDeletionPlanResult.Failure(
        [
            new Diagnostic(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                DiagnosticSeverity.Error,
                message,
                request.SemanticId.Value),
        ]);
}
