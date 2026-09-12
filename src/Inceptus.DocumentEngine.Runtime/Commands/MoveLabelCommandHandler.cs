using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class MoveLabelCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not MoveLabelCommand move)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.HandlerFailure,
                    $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                    command.TypeId.Value),
            ]));
        }

        if (!document.VisualModel.TryGetVisualState(
                move.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateNotFound,
                    $"Visual state '{move.TargetVisualStateId}' does not exist.",
                    move.TargetVisualStateId.Value),
            ]));
        }

        if (!document.SemanticModel.TryGetRelationship(existing.SemanticElementId, out _))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportLabelPlacement,
                    $"Visual state '{move.TargetVisualStateId}' does not represent a semantic relationship with route-relative label placement.",
                    move.TargetVisualStateId.Value),
            ]));
        }

        var targetPlacement = move.TargetPlacement;
        if (ConnectorLabelPlacement.Resolve(existing.Properties) ==
            (targetPlacement ?? ConnectorLabelPlacement.Default))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.LabelPlacementUnchanged,
                    $"Visual state '{move.TargetVisualStateId}' already has the requested label placement.",
                    move.TargetVisualStateId.Value),
            ]));
        }

        var replacement = new VisualStateSnapshot(
            existing.Id,
            existing.SemanticElementId,
            existing.Position,
            existing.Size,
            existing.PlacementMode,
            existing.Route,
            ConnectorLabelPlacement.UpdateProperties(
                existing.Properties,
                targetPlacement),
            existing.ConnectorAnchors,
            existing.SourceAnchorId,
            existing.TargetAnchorId,
            existing.BoundaryAttachment);
        var proposedVisualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Select(visualState =>
                visualState.Id == replacement.Id ? replacement : visualState),
            document.VisualModel.ProfileElementPresentations);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                document.SemanticModel,
                proposedVisualModel,
                document.Metadata,
                document.Publication)));
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
