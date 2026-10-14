using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnElementDescriptionHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not UpdateBpmnElementDescriptionCommand update ||
            !TryRead(before, update.ElementId, out var previous) ||
            !TryRead(committed, update.ElementId, out var current) ||
            StringComparer.Ordinal.Equals(previous, current) ||
            !StringComparer.Ordinal.Equals(current, update.TargetDescription))
        {
            return CommandHistoryPreparationResult.Failure(
                [new Diagnostic(BpmnCommandDiagnosticCodes.HistoryInvalid, DiagnosticSeverity.Error,
                    "Description History requires the exact requested optional text change.", command.TypeId.Value)]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new DescriptionCommandFactory(update.ElementId, previous),
            new DescriptionCommandFactory(update.ElementId, current));
    }

    private static bool TryRead(DocumentSnapshot document, SemanticElementId id, out string? description)
    {
        description = null;
        if (!document.SemanticModel.TryGetElement(id, out var element) ||
            element is null || !BpmnElementDescriptionCommandHandler.Supports(element.TypeId))
        {
            return false;
        }

        if (!element.Properties.TryGetValue(BpmnSemanticProperties.Description, out var value))
        {
            return true;
        }

        if (value.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        description = value.TextValue;
        return true;
    }

    private sealed class DescriptionCommandFactory(SemanticElementId elementId, string? description) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new UpdateBpmnElementDescriptionCommand(documentId, expectedRevision, elementId, description);
    }
}
