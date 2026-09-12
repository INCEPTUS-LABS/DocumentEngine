using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class UpdateBoundaryAttachmentHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            UpdateBoundaryAttachmentCommand.KnownTypeId,
            new UpdateBoundaryAttachmentHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not UpdateBoundaryAttachmentCommand update ||
            !before.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var oldState) ||
            oldState?.BoundaryAttachment is not { } oldAttachment ||
            !committed.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var newState) ||
            newState?.BoundaryAttachment is not { } newAttachment)
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Boundary-attachment History requires the target Visual State and its placement before and after commit.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new UpdateBoundaryAttachmentHistoryCommandFactory(
                update.TargetVisualStateId,
                oldAttachment,
                update.EffectiveOwnerBounds,
                Bounds(oldState)),
            new UpdateBoundaryAttachmentHistoryCommandFactory(
                update.TargetVisualStateId,
                newAttachment,
                update.EffectiveOwnerBounds,
                Bounds(newState)));
    }

    private static RectD Bounds(VisualStateSnapshot visualState) => new(
        visualState.Position.X,
        visualState.Position.Y,
        visualState.Size.Width,
        visualState.Size.Height);
}

internal sealed class UpdateBoundaryAttachmentHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly BoundaryAttachmentPlacement _attachment;
    private readonly RectD _effectiveOwnerBounds;
    private readonly RectD _fallbackBounds;
    private readonly VisualStateId _visualStateId;

    internal UpdateBoundaryAttachmentHistoryCommandFactory(
        VisualStateId visualStateId,
        BoundaryAttachmentPlacement attachment,
        RectD effectiveOwnerBounds,
        RectD fallbackBounds)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(attachment);

        _visualStateId = visualStateId;
        _attachment = attachment;
        _effectiveOwnerBounds = effectiveOwnerBounds;
        _fallbackBounds = fallbackBounds;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new RestoreBoundaryAttachmentCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _attachment,
            _effectiveOwnerBounds,
            _fallbackBounds);
}
