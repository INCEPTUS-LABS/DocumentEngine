using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class MoveVisualStatesCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not MoveVisualStatesCommand move)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.HandlerFailure,
                    $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                    command.TypeId.Value),
            ]));
        }

        var diagnostics = new List<Diagnostic>();
        var replacements = new Dictionary<VisualStateId, VisualStateSnapshot>();
        foreach (var target in move.Moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.VisualModel.TryGetVisualState(target.VisualStateId, out var existing) ||
                existing is null)
            {
                diagnostics.Add(Error(
                    CommandExecutionDiagnosticCodes.VisualStateNotFound,
                    $"Visual state '{target.VisualStateId}' does not exist.",
                    target.VisualStateId.Value));
                continue;
            }

            if (!document.SemanticModel.TryGetElement(existing.SemanticElementId, out _))
            {
                diagnostics.Add(Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                    $"Visual state '{target.VisualStateId}' does not represent a persistently positionable semantic element.",
                    target.VisualStateId.Value));
                continue;
            }

            if (existing.BoundaryAttachment is not null)
            {
                diagnostics.Add(Error(
                    CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                    $"Boundary-attached visual state '{target.VisualStateId}' cannot be moved by an independent free-position command.",
                    target.VisualStateId.Value));
                continue;
            }

            if (!VisualCommandGeometry.HasRenderableBounds(target.TargetPosition, existing.Size))
            {
                diagnostics.Add(Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"Moving visual state '{target.VisualStateId}' would produce non-renderable bounds.",
                    target.VisualStateId.Value));
                continue;
            }

            replacements.Add(
                existing.Id,
                new VisualStateSnapshot(
                    existing.Id,
                    existing.SemanticElementId,
                    target.TargetPosition,
                    existing.Size,
                    target.RequestedPlacementMode ?? existing.PlacementMode,
                    existing.Route,
                    existing.Properties,
                    existing.ConnectorAnchors,
                    existing.SourceAnchorId,
                    existing.TargetAnchorId,
                    existing.BoundaryAttachment));
        }

        if (diagnostics.Count != 0)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var synchronizationDiagnostics =
            BoundaryAttachmentGeometrySynchronizer.SynchronizeDependents(
                document,
                replacements);
        if (!synchronizationDiagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
                synchronizationDiagnostics));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var proposedVisualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Select(visualState =>
                replacements.TryGetValue(visualState.Id, out var replacement)
                    ? replacement
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
