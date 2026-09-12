using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateBoundaryAttachmentCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (!BoundaryAttachmentCommandRequest.TryCreate(command, out var update))
        {
            return Failure(
                command.TypeId.Value,
                CommandExecutionDiagnosticCodes.HandlerFailure,
                $"Command type '{command.TypeId}' does not use the expected immutable request shape.");
        }

        if (!document.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return Failure(
                update.TargetVisualStateId.Value,
                CommandExecutionDiagnosticCodes.VisualStateNotFound,
                $"Visual state '{update.TargetVisualStateId}' does not exist.");
        }

        if (!document.SemanticModel.TryGetElement(
                existing.SemanticElementId,
                out var attachedElement) ||
            attachedElement?.AttachedToElementId is not { } ownerSemanticElementId ||
            existing.BoundaryAttachment is null)
        {
            return Failure(
                update.TargetVisualStateId.Value,
                CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportBoundaryAttachment,
                $"Visual state '{update.TargetVisualStateId}' does not represent a structurally boundary-attached semantic element.");
        }

        if (existing.BoundaryAttachment == update.TargetAttachment)
        {
            return Failure(
                update.TargetVisualStateId.Value,
                CommandExecutionDiagnosticCodes.BoundaryAttachmentUnchanged,
                $"Visual state '{update.TargetVisualStateId}' already has the requested boundary attachment.");
        }

        if (!BoundaryAttachmentGeometrySynchronizer.TryResolveOwnerVisual(
                document,
                ownerSemanticElementId,
                existing.Id,
                out var ownerVisual,
                out var ownerDiagnostic))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure([ownerDiagnostic!]));
        }

        if (ownerVisual!.PlacementMode == VisualPlacementMode.Pinned &&
            BoundaryAttachmentGeometrySynchronizer.PersistentBounds(ownerVisual) !=
                update.EffectiveOwnerBounds)
        {
            return Failure(
                update.TargetVisualStateId.Value,
                CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                $"The effective owner bounds supplied for boundary-attached visual state '{update.TargetVisualStateId}' do not match its pinned owner.");
        }

        if (!BoundaryAttachmentGeometrySynchronizer.TryResolveAttachedBounds(
                update.EffectiveOwnerBounds,
                existing,
                update.TargetAttachment,
                out var effectiveTargetBounds))
        {
            return Failure(
                update.TargetVisualStateId.Value,
                CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                $"The requested boundary attachment for visual state '{update.TargetVisualStateId}' would produce non-renderable bounds.");
        }

        // Editor commands can only persist the compatibility bounds derived from
        // effective owner geometry. The alternative value exists solely on the
        // runtime-private History restoration command.
        var targetBounds = update.HistoryFallbackBounds ?? effectiveTargetBounds;
        if (!VisualCommandGeometry.HasRenderableBounds(
                targetBounds.TopLeft,
                targetBounds.Size) ||
            targetBounds.Size != existing.Size ||
            ownerVisual.PlacementMode == VisualPlacementMode.Pinned &&
                targetBounds != effectiveTargetBounds)
        {
            return Failure(
                update.TargetVisualStateId.Value,
                CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                $"The requested fallback bounds for boundary-attached visual state '{update.TargetVisualStateId}' are invalid.");
        }

        var replacement = BoundaryAttachmentGeometrySynchronizer.WithBounds(
            existing,
            targetBounds,
            update.TargetAttachment);
        var replacements = new Dictionary<VisualStateId, VisualStateSnapshot>
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
        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                document.SemanticModel,
                proposedVisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.ForChangedVisualStates(
                replacements.Keys)));
    }

    private static ValueTask<CommandHandlerResult> Failure(
        string sourceIdentity,
        string code,
        string message) =>
        ValueTask.FromResult(CommandHandlerResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, sourceIdentity),
        ]));
}
