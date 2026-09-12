using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Immutable model, Scene-origin, and transient View context for one semantic Scene action.
/// </summary>
public sealed record SemanticSceneViewActionRequest
{
    public SemanticSceneViewActionRequest(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        Canvas2DSceneItem targetItem)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        ArgumentNullException.ThrowIfNull(targetItem);
        if (targetItem.Origin.SemanticElementId is null ||
            targetItem.Origin.VisualStateId is not null ||
            !Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(targetItem))
        {
            throw new ArgumentException(
                "A semantic Scene View action requires a semantic-only Scene target.",
                nameof(targetItem));
        }

        Document = document;
        ActiveScopeId = activeScopeId;
        ModelProfileViewState = modelProfileViewState;
        ModelProfileElementViewState = modelProfileElementViewState;
        TargetItem = targetItem;
    }

    public DocumentSnapshot Document { get; }

    public DocumentScopeId ActiveScopeId { get; }

    public ModelProfileViewStateSnapshot ModelProfileViewState { get; }

    public ModelProfileElementViewStateSnapshot ModelProfileElementViewState { get; }

    public Canvas2DSceneItem TargetItem { get; }

    public SemanticElementId TargetSemanticElementId =>
        TargetItem.Origin.SemanticElementId!;
}
