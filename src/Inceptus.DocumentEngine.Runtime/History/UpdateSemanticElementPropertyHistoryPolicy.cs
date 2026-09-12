using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class UpdateSemanticElementPropertyHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            UpdateSemanticElementPropertyCommand.KnownTypeId,
            new UpdateSemanticElementPropertyHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not UpdateSemanticElementPropertyCommand update ||
            !TryGetProperty(before, update, out var oldValue) ||
            !TryGetProperty(committed, update, out var newValue) ||
            oldValue!.Kind != newValue!.Kind ||
            !newValue.Equals(update.TargetValue) ||
            oldValue.Equals(newValue))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Semantic-property History requires different, same-kind target values before and after commit.",
                    command.TypeId.Value),
            ]);
        }

        var beforeValue = oldValue!;
        var afterValue = newValue!;
        return CommandHistoryPreparationResult.Undoable(
            new UpdateSemanticElementPropertyHistoryCommandFactory(
                update.TargetSemanticElementId,
                update.PropertyKey,
                beforeValue),
            new UpdateSemanticElementPropertyHistoryCommandFactory(
                update.TargetSemanticElementId,
                update.PropertyKey,
                afterValue));
    }

    private static bool TryGetProperty(
        DocumentSnapshot document,
        UpdateSemanticElementPropertyCommand command,
        out PropertyValue? value)
    {
        value = null;
        if (document.SemanticModel.TryGetElement(
                command.TargetSemanticElementId,
                out var element) &&
            element is not null)
        {
            return element.Properties.TryGetValue(command.PropertyKey, out value);
        }

        return document.SemanticModel.TryGetRelationship(
                command.TargetSemanticElementId,
                out var relationship) &&
            relationship is not null &&
            relationship.Properties.TryGetValue(command.PropertyKey, out value);
    }
}

internal sealed class UpdateSemanticElementPropertyHistoryCommandFactory :
    IHistoryCommandFactory
{
    private readonly string _propertyKey;
    private readonly SemanticElementId _semanticElementId;
    private readonly PropertyValue _value;

    internal UpdateSemanticElementPropertyHistoryCommandFactory(
        SemanticElementId semanticElementId,
        string propertyKey,
        PropertyValue value)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);
        ArgumentNullException.ThrowIfNull(value);
        _semanticElementId = semanticElementId;
        _propertyKey = propertyKey;
        _value = value;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new UpdateSemanticElementPropertyCommand(
            documentId,
            expectedRevision,
            _semanticElementId,
            _propertyKey,
            _value);
}
