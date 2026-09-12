using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Immutable model and exact Scene-origin context for one command-backed semantic action.
/// </summary>
public sealed record SemanticSceneCommandActionRequest
{
    public SemanticSceneCommandActionRequest(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        Canvas2DSceneItem targetItem)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(targetItem);
        if (targetItem.Origin.SemanticElementId is null ||
            targetItem.Origin.VisualStateId is not null ||
            !Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(targetItem))
        {
            throw new ArgumentException(
                "A semantic Scene command action requires a semantic-only Scene target.",
                nameof(targetItem));
        }

        Document = document;
        ActiveScopeId = activeScopeId;
        TargetItem = targetItem;
    }

    public DocumentSnapshot Document { get; }

    public DocumentScopeId ActiveScopeId { get; }

    public Canvas2DSceneItem TargetItem { get; }

    public SemanticElementId TargetSemanticElementId =>
        TargetItem.Origin.SemanticElementId!;
}
