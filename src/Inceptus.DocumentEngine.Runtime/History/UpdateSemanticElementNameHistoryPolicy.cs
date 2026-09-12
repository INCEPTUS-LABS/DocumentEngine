using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class UpdateSemanticElementNameHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            UpdateSemanticElementNameCommand.KnownTypeId,
            new UpdateSemanticElementNameHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not UpdateSemanticElementNameCommand update ||
            !TryGetName(before, update, out var oldName) ||
            !TryGetName(committed, update, out var newName))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Semantic Name History requires the target element's text Name property before and after commit.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new UpdateSemanticElementNameHistoryCommandFactory(
                update.TargetSemanticElementId,
                update.NamePropertyKey,
                oldName!),
            new UpdateSemanticElementNameHistoryCommandFactory(
                update.TargetSemanticElementId,
                update.NamePropertyKey,
                newName!));
    }

    private static bool TryGetName(
        DocumentSnapshot document,
        UpdateSemanticElementNameCommand command,
        out string? name)
    {
        name = null;
        if (!document.SemanticModel.TryGetElement(
                command.TargetSemanticElementId,
                out var element) ||
            element is null ||
            !element.Properties.TryGetValue(command.NamePropertyKey, out var value) ||
            value.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        name = value.TextValue;
        return !string.IsNullOrWhiteSpace(name);
    }
}

internal sealed class UpdateSemanticElementNameHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly string _name;
    private readonly string _namePropertyKey;
    private readonly SemanticElementId _semanticElementId;

    internal UpdateSemanticElementNameHistoryCommandFactory(
        SemanticElementId semanticElementId,
        string namePropertyKey,
        string name)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePropertyKey);
        ArgumentNullException.ThrowIfNull(name);
        _semanticElementId = semanticElementId;
        _namePropertyKey = namePropertyKey;
        _name = name;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new UpdateSemanticElementNameCommand(
            documentId,
            expectedRevision,
            _semanticElementId,
            _namePropertyKey,
            _name);
}
