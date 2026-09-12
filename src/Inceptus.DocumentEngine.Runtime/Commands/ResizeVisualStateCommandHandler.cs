using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class ResizeVisualStateCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not ResizeVisualStateCommand resize)
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
                resize.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateNotFound,
                    $"Visual state '{resize.TargetVisualStateId}' does not exist.",
                    resize.TargetVisualStateId.Value),
            ]));
        }

        if (!document.SemanticModel.TryGetElement(existing.SemanticElementId, out _))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                    $"Visual state '{resize.TargetVisualStateId}' does not represent a persistently resizable semantic element.",
                    resize.TargetVisualStateId.Value),
            ]));
        }

        if (existing.BoundaryAttachment is not null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                    $"Boundary-attached visual state '{resize.TargetVisualStateId}' cannot be independently resized.",
                    resize.TargetVisualStateId.Value),
            ]));
        }

        if (!VisualCommandGeometry.HasRenderableBounds(
                resize.TargetBounds.TopLeft,
                resize.TargetBounds.Size))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"Resizing visual state '{resize.TargetVisualStateId}' would produce non-renderable bounds.",
                    resize.TargetVisualStateId.Value),
            ]));
        }

        var replacement = new VisualStateSnapshot(
            existing.Id,
            existing.SemanticElementId,
            resize.TargetBounds.TopLeft,
            resize.TargetBounds.Size,
            resize.RequestedPlacementMode ?? existing.PlacementMode,
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
