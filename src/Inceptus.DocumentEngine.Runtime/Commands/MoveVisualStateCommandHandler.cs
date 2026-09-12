using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class MoveVisualStateCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not MoveVisualStateCommand move)
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

        if (!document.SemanticModel.TryGetElement(existing.SemanticElementId, out _))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                    $"Visual state '{move.TargetVisualStateId}' does not represent a persistently positionable semantic element.",
                    move.TargetVisualStateId.Value),
            ]));
        }

        if (existing.BoundaryAttachment is not null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                    $"Boundary-attached visual state '{move.TargetVisualStateId}' cannot be moved by an independent free-position command.",
                    move.TargetVisualStateId.Value),
            ]));
        }

        if (!VisualCommandGeometry.HasRenderableBounds(move.TargetPosition, existing.Size))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"Moving visual state '{move.TargetVisualStateId}' would produce non-renderable bounds.",
                    move.TargetVisualStateId.Value),
            ]));
        }

        var replacement = new VisualStateSnapshot(
            existing.Id,
            existing.SemanticElementId,
            move.TargetPosition,
            existing.Size,
            move.RequestedPlacementMode ?? existing.PlacementMode,
            existing.Route,
            existing.Properties,
            existing.ConnectorAnchors,
            existing.SourceAnchorId,
            existing.TargetAnchorId,
            existing.BoundaryAttachment);
        var replacements = new Dictionary<
            Inceptus.DocumentEngine.Contracts.Primitives.VisualStateId,
            VisualStateSnapshot>
        {
            [replacement.Id] = replacement,
        };
        var synchronizationDiagnostics =
            BoundaryAttachmentGeometrySynchronizer.SynchronizeDependents(
                document,
                replacements);
        if (!synchronizationDiagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
                synchronizationDiagnostics));
        }

        var proposedVisualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Select(visualState =>
                replacements.TryGetValue(visualState.Id, out var synchronized)
                    ? synchronized
                    : visualState),
            document.VisualModel.ProfileElementPresentations);
        var proposedDocument = new DocumentSnapshot(
            document.SemanticModel,
            proposedVisualModel,
            document.Metadata,
            document.Publication);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            proposedDocument,
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.ForChangedVisualStates(
                replacements.Keys)));
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
