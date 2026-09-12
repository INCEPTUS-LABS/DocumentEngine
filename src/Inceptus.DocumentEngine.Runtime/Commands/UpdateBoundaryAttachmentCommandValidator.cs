using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateBoundaryAttachmentCommandValidator : ICommandValidator
{
    internal static CommandValidatorRegistration Registration { get; } =
        new(
            UpdateBoundaryAttachmentCommand.KnownTypeId,
            new CommandValidatorId("inceptus:validator/update-boundary-attachment"),
            new UpdateBoundaryAttachmentCommandValidator());

    public ImmutableArray<Diagnostic> Validate(
        ICommand command,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);

        if (!BoundaryAttachmentCommandRequest.TryCreate(command, out var update))
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.InvalidCommandStructure,
                    $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                    command.TypeId.Value),
            ];
        }

        if (!document.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateNotFound,
                    $"Visual state '{update.TargetVisualStateId}' does not exist.",
                    update.TargetVisualStateId.Value),
            ];
        }

        if (!document.SemanticModel.TryGetElement(
                existing.SemanticElementId,
                out var attachedElement) ||
            attachedElement?.AttachedToElementId is not { } ownerSemanticElementId ||
            existing.BoundaryAttachment is null)
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes
                        .VisualStateDoesNotSupportBoundaryAttachment,
                    $"Visual state '{update.TargetVisualStateId}' does not represent a structurally boundary-attached semantic element.",
                    update.TargetVisualStateId.Value),
            ];
        }

        if (existing.BoundaryAttachment == update.TargetAttachment)
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.BoundaryAttachmentUnchanged,
                    $"Visual state '{update.TargetVisualStateId}' already has the requested boundary attachment.",
                    update.TargetVisualStateId.Value),
            ];
        }

        if (!BoundaryAttachmentGeometrySynchronizer.TryResolveOwnerVisual(
                document,
                ownerSemanticElementId,
                existing.Id,
                out var ownerVisual,
                out var ownerDiagnostic))
        {
            return [ownerDiagnostic!];
        }

        if (ownerVisual!.PlacementMode == VisualPlacementMode.Pinned &&
            BoundaryAttachmentGeometrySynchronizer.PersistentBounds(ownerVisual) !=
                update.EffectiveOwnerBounds)
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"The effective owner bounds supplied for boundary-attached visual state '{update.TargetVisualStateId}' do not match its pinned owner.",
                    update.TargetVisualStateId.Value),
            ];
        }

        if (!BoundaryAttachmentGeometrySynchronizer.TryResolveAttachedBounds(
                update.EffectiveOwnerBounds,
                existing,
                update.TargetAttachment,
                out var effectiveTargetBounds))
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"The requested boundary attachment for visual state '{update.TargetVisualStateId}' would produce non-renderable bounds.",
                    update.TargetVisualStateId.Value),
            ];
        }

        if (update.HistoryFallbackBounds is { } fallbackBounds &&
            (!VisualCommandGeometry.HasRenderableBounds(
                    fallbackBounds.TopLeft,
                    fallbackBounds.Size) ||
                fallbackBounds.Size != existing.Size ||
                ownerVisual.PlacementMode == VisualPlacementMode.Pinned &&
                    fallbackBounds != effectiveTargetBounds))
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"The requested fallback bounds for boundary-attached visual state '{update.TargetVisualStateId}' are invalid.",
                    update.TargetVisualStateId.Value),
            ];
        }

        return [];
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
