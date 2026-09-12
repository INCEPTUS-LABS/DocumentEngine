using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateConnectionRouteCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not UpdateConnectionRouteCommand update)
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
                update.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateNotFound,
                    $"Visual state '{update.TargetVisualStateId}' does not exist.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        if (!document.SemanticModel.TryGetRelationship(existing.SemanticElementId, out _) ||
            existing.Route.Length == 1)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportRoute,
                    $"Visual state '{update.TargetVisualStateId}' does not represent a semantic relationship with valid persistent route state.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        var targetIsCompleteRoute = update.TargetRoute.Length >= 2;
        var existingIsCompleteRoute = existing.Route.Length >= 2;
        if ((targetIsCompleteRoute &&
             !VisualCommandGeometry.HasRenderableRoute(update.TargetRoute)) ||
            (existingIsCompleteRoute &&
             targetIsCompleteRoute &&
             (update.TargetRoute[0] != existing.Route[0] ||
              update.TargetRoute[^1] != existing.Route[^1])))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"Route update for visual state '{update.TargetVisualStateId}' must be empty or have renderable document-space extents, and replacements of existing routes must preserve their endpoints.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        var replacement = new VisualStateSnapshot(
            existing.Id,
            existing.SemanticElementId,
            existing.Position,
            existing.Size,
            existing.PlacementMode,
            update.TargetRoute,
            existing.Properties,
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
        var proposedDocument = new DocumentSnapshot(
            document.SemanticModel,
            proposedVisualModel,
            document.Metadata,
            document.Publication);

        return ValueTask.FromResult(CommandHandlerResult.Success(proposedDocument));
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
