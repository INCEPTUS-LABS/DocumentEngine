using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    internal Canvas2DScene? TryReuseForSelection(
        Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { } prior)
        {
            return null;
        }
        var source = prior.Source;
        var provenance = source.Provenance;
        if (!ReferenceEquals(provenance.Builder, this) ||
            !ReferenceEquals(provenance.Document, document) || provenance.ScopeId != scopeId ||
            !ReferenceEquals(provenance.Graph, graph) || !ReferenceEquals(provenance.Layout, layout) ||
            !ReferenceEquals(provenance.Routing, routing) ||
            !ReferenceEquals(provenance.ProfileViewState, profileViewState) ||
            !ReferenceEquals(provenance.ProfileElementViewState, profileElementViewState) ||
            previous.DocumentId != document.DocumentId || previous.SourceRevision != document.Revision ||
            !IsSelectionTransition(prior.EditorState, editorState) ||
            !IsSupportedSelectionEditorState(source, prior.EditorState) ||
            !IsSupportedSelectionEditorState(source, editorState))
        {
            return null;
        }
        return TryComposeBoundedPresentation(previous, prior, document, scopeId, profileViewState,
            profileElementViewState, graph, layout, routing, editorState, cancellationToken);
    }

    private bool IsSelectionTransition(EditorStateSnapshot previous, EditorStateSnapshot current)
    {
        if (!previous.Viewport.Equals(current.Viewport) ||
            !StringComparer.Ordinal.Equals(previous.ActiveToolId, current.ActiveToolId) ||
            !StringComparer.Ordinal.Equals(previous.FocusTargetId, current.FocusTargetId) ||
            !previous.ToolState.Equals(current.ToolState))
        {
            return false;
        }
        var hoverChanged = previous.HoveredObjectId != current.HoveredObjectId;
        var selectionChanged = !previous.Selection.AsSpan().SequenceEqual(current.Selection.AsSpan());
        return (hoverChanged || selectionChanged) && _contributors.Descriptors.All(descriptor =>
            (!hoverChanged || descriptor.HoverDependency == Canvas2DSceneTransientDependency.Invariant) &&
            (!selectionChanged || descriptor.VisualSelectionDependency == Canvas2DSceneTransientDependency.Invariant));
    }

    private static bool IsSupportedSelectionEditorState(
        Canvas2DBoundedPresentationSource source, EditorStateSnapshot state)
    {
        if (state.Selection.Length > 1 || state.ActiveGesture is not null ||
            !state.TemporaryFeedback.IsEmpty || state.SemanticSceneSelection is not null)
        {
            return false;
        }
        if (state.Selection.Length == 1 && !IsEligibleSelectionFamily(source, state.Selection[0]))
        {
            return false;
        }
        return state.HoveredObjectId is not { } hover ||
            (source.ItemsById.TryGetValue(hover, out var item) &&
             item.IsVisible && item.Layer == Canvas2DSceneLayer.Content &&
             HasBooleanMetadata(item, Canvas2DNodeBodyMetadata.NodeBody) &&
             HasBooleanMetadata(item, Canvas2DTransientInteractionMetadata.BoundedSelectionEligible) &&
             item.Origin.VisualStateId is { } visual && IsEligibleSelectionFamily(source, visual));
    }

    private static bool IsEligibleSelectionFamily(Canvas2DBoundedPresentationSource source, VisualStateId id) =>
        source.Families.TryGetValue(id, out var family) && family.Length <= 64 &&
        ResolveLogicalSelectionTarget(family, id) is { IsVisible: true } target &&
        target.Layer == Canvas2DSceneLayer.Content &&
        HasBooleanMetadata(target, Canvas2DNodeBodyMetadata.NodeBody) &&
        HasBooleanMetadata(target, Canvas2DTransientInteractionMetadata.BoundedSelectionEligible) &&
        target.Origin.ProjectedObjectId is { } nodeId &&
        (!source.OverlayInputs.Anchors.TryGetValue(nodeId, out var anchors) || anchors.Length <= 32);
}
