using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Provides read-only model/view context and the canonical Process Scene snapshot that existed
/// before registered contributor items and editor overlays were composed.
/// </summary>
public sealed class Canvas2DScenePresentationContext
{
    public Canvas2DScenePresentationContext(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        IEnumerable<Canvas2DSceneItem> baseSceneItems)
        : this(
            document,
            activeScopeId,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            baseSceneItems)
    {
    }

    public Canvas2DScenePresentationContext(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        IEnumerable<Canvas2DSceneItem> baseSceneItems)
        : this(document, new ProcessScopePresentationContext(activeScopeId), modelProfileViewState,
            modelProfileElementViewState, baseSceneItems)
    {
    }

    public Canvas2DScenePresentationContext(DocumentSnapshot document, PresentationViewContext context,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        IEnumerable<Canvas2DSceneItem> baseSceneItems)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        ArgumentNullException.ThrowIfNull(baseSceneItems);

        var items = baseSceneItems.ToArray();
        if (Array.Exists(items, static item => item is null))
        {
            throw new ArgumentException(
                "Base Scene items cannot contain null values.",
                nameof(baseSceneItems));
        }

        Document = document;
        PresentationContext = context;
        ModelProfileViewState = modelProfileViewState;
        ModelProfileElementViewState = modelProfileElementViewState;
        BaseSceneItems = [.. items.OrderBy(static item => item.Layer)
            .ThenBy(static item => item.ZIndex)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)];

    }

    public DocumentSnapshot Document { get; }

    public PresentationViewContext PresentationContext { get; }

    public DocumentScopeId ActiveScopeId => PresentationContext is ProcessScopePresentationContext process
        ? process.ScopeId : throw new InvalidOperationException("A document presentation has no active process scope.");

    public ModelProfileViewStateSnapshot ModelProfileViewState { get; }

    public ModelProfileElementViewStateSnapshot ModelProfileElementViewState { get; }

    public ImmutableArray<Canvas2DSceneItem> BaseSceneItems { get; }

}
