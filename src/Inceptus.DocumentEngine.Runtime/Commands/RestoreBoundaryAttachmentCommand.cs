using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Runtime-only History replay request. Its fallback bounds are an exact immutable
/// snapshot restoration detail, never an editor-controlled geometry authority.
/// </summary>
internal sealed class RestoreBoundaryAttachmentCommand : ICommand
{
    internal RestoreBoundaryAttachmentCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        BoundaryAttachmentPlacement targetAttachment,
        RectD effectiveOwnerBounds,
        RectD targetFallbackBounds)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetAttachment);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        TargetAttachment = targetAttachment;
        EffectiveOwnerBounds = effectiveOwnerBounds;
        TargetFallbackBounds = targetFallbackBounds;
    }

    public CommandTypeId TypeId => UpdateBoundaryAttachmentCommand.KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    internal VisualStateId TargetVisualStateId { get; }

    internal BoundaryAttachmentPlacement TargetAttachment { get; }

    internal RectD EffectiveOwnerBounds { get; }

    internal RectD TargetFallbackBounds { get; }
}

internal readonly record struct BoundaryAttachmentCommandRequest(
    VisualStateId TargetVisualStateId,
    BoundaryAttachmentPlacement TargetAttachment,
    RectD EffectiveOwnerBounds,
    RectD? HistoryFallbackBounds)
{
    internal static bool TryCreate(
        ICommand command,
        out BoundaryAttachmentCommandRequest request)
    {
        switch (command)
        {
            case UpdateBoundaryAttachmentCommand update:
                request = new BoundaryAttachmentCommandRequest(
                    update.TargetVisualStateId,
                    update.TargetAttachment,
                    update.EffectiveOwnerBounds,
                    HistoryFallbackBounds: null);
                return true;
            case RestoreBoundaryAttachmentCommand restore:
                request = new BoundaryAttachmentCommandRequest(
                    restore.TargetVisualStateId,
                    restore.TargetAttachment,
                    restore.EffectiveOwnerBounds,
                    restore.TargetFallbackBounds);
                return true;
            default:
                request = default;
                return false;
        }
    }
}
