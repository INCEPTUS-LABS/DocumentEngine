using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnElementTypeHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not ChangeBpmnElementTypeCommand change ||
            !before.SemanticModel.TryGetElement(change.ElementId, out var previous) || previous is null ||
            !committed.SemanticModel.TryGetElement(change.ElementId, out var current) || current is null ||
            previous.TypeId == current.TypeId || current.TypeId != change.TargetSemanticTypeId ||
            !BpmnElementTypeSwitching.CanSwitch(previous.TypeId, current.TypeId) ||
            !previous.Properties.Equals(current.Properties) ||
            previous.AttachedToElementId != current.AttachedToElementId ||
            previous.ContainmentKind != current.ContainmentKind)
        {
            return CommandHistoryPreparationResult.Failure(
                [new Diagnostic(BpmnCommandDiagnosticCodes.HistoryInvalid, DiagnosticSeverity.Error,
                    "Element Type History requires the exact requested concrete type change on the same element.", command.TypeId.Value)]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new TypeCommandFactory(change.ElementId, previous.TypeId),
            new TypeCommandFactory(change.ElementId, current.TypeId));
    }

    private sealed class TypeCommandFactory(SemanticElementId elementId, SemanticTypeId typeId) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new ChangeBpmnElementTypeCommand(documentId, expectedRevision, elementId, typeId);
    }
}
