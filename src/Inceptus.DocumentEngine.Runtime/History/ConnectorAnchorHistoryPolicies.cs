using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class AddConnectorAnchorHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(AddConnectorAnchorCommand.KnownTypeId, new AddConnectorAnchorHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not AddConnectorAnchorCommand add ||
            !before.VisualModel.TryGetVisualState(add.TargetVisualStateId, out var oldState) ||
            oldState is null ||
            oldState.ConnectorAnchors.Any(anchor => anchor.Id == add.AnchorId) ||
            !committed.VisualModel.TryGetVisualState(add.TargetVisualStateId, out var newState) ||
            newState is null)
        {
            return Invalid(command, "Add-connector-anchor History requires the target Visual State before and after commit.");
        }

        var added = newState.ConnectorAnchors.SingleOrDefault(anchor =>
            anchor.Id == add.AnchorId);
        if (added is null ||
            added.Side != add.Side ||
            added.Role != add.Role ||
            added.Order != add.InsertionIndex)
        {
            return Invalid(command, "Add-connector-anchor History could not resolve the committed anchor.");
        }

        return CommandHistoryPreparationResult.Undoable(
            new RemoveConnectorAnchorHistoryCommandFactory(
                add.TargetVisualStateId,
                add.AnchorId),
            new AddConnectorAnchorHistoryCommandFactory(
                add.TargetVisualStateId,
                added));
    }

    private static CommandHistoryPreparationResult Invalid(ICommand command, string message) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                HistoryDiagnosticCodes.InvalidPreparation,
                DiagnosticSeverity.Error,
                message,
                command.TypeId.Value),
        ]);
}

internal sealed class RemoveConnectorAnchorHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(RemoveConnectorAnchorCommand.KnownTypeId, new RemoveConnectorAnchorHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not RemoveConnectorAnchorCommand remove ||
            !before.VisualModel.TryGetVisualState(remove.TargetVisualStateId, out var oldState) ||
            oldState is null ||
            !committed.VisualModel.TryGetVisualState(remove.TargetVisualStateId, out var newState) ||
            newState is null ||
            newState.ConnectorAnchors.Any(anchor => anchor.Id == remove.AnchorId))
        {
            return Invalid(command, "Remove-connector-anchor History requires the target Visual State before and after commit.");
        }

        var removed = oldState.ConnectorAnchors.SingleOrDefault(anchor =>
            anchor.Id == remove.AnchorId);
        if (removed is null)
        {
            return Invalid(command, "Remove-connector-anchor History could not resolve the removed anchor.");
        }

        return CommandHistoryPreparationResult.Undoable(
            new AddConnectorAnchorHistoryCommandFactory(
                remove.TargetVisualStateId,
                removed),
            new RemoveConnectorAnchorHistoryCommandFactory(
                remove.TargetVisualStateId,
                remove.AnchorId));
    }

    private static CommandHistoryPreparationResult Invalid(ICommand command, string message) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                HistoryDiagnosticCodes.InvalidPreparation,
                DiagnosticSeverity.Error,
                message,
                command.TypeId.Value),
        ]);
}

internal sealed class AddConnectorAnchorHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly ConnectorAnchor _anchor;
    private readonly VisualStateId _visualStateId;

    internal AddConnectorAnchorHistoryCommandFactory(
        VisualStateId visualStateId,
        ConnectorAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(anchor);
        _visualStateId = visualStateId;
        _anchor = anchor;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new AddConnectorAnchorCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _anchor.Id,
            _anchor.Side,
            _anchor.Role,
            _anchor.Order);
}

internal sealed class RemoveConnectorAnchorHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly ConnectorAnchorId _anchorId;
    private readonly VisualStateId _visualStateId;

    internal RemoveConnectorAnchorHistoryCommandFactory(
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(anchorId);
        _visualStateId = visualStateId;
        _anchorId = anchorId;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new RemoveConnectorAnchorCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _anchorId);
}
