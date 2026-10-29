using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnEventNameHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not UpdateBpmnEventNameCommand update ||
            !TryRead(before, update.ElementId, out var previous) ||
            !TryRead(committed, update.ElementId, out var current) ||
            StringComparer.Ordinal.Equals(previous, current) ||
            !StringComparer.Ordinal.Equals(current, update.TargetName))
        {
            return CommandHistoryPreparationResult.Failure(
                [new Diagnostic(BpmnCommandDiagnosticCodes.HistoryInvalid, DiagnosticSeverity.Error,
                    "Name History requires the exact requested optional text change.", command.TypeId.Value)]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new NameCommandFactory(update.ElementId, previous),
            new NameCommandFactory(update.ElementId, current));
    }

    private static bool TryRead(DocumentSnapshot document, SemanticElementId id, out string? name)
    {
        name = null;
        if (!document.SemanticModel.TryGetElement(id, out var element) ||
            element is null || !BpmnEventNameCommandHandler.Supports(element.TypeId))
        {
            return false;
        }

        if (!element.Properties.TryGetValue(BpmnSemanticProperties.Name, out var value))
        {
            return true;
        }

        if (value.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        name = value.TextValue;
        return true;
    }

    private sealed class NameCommandFactory(SemanticElementId elementId, string? name) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new UpdateBpmnEventNameCommand(documentId, expectedRevision, elementId, name);
    }
}
