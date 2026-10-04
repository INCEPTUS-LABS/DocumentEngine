using System.Collections.Immutable;
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

internal sealed record Canvas2DBoundedRouteBend(
    ImmutableDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel> Labels,
    bool HasLineJumps,
    ImmutableDictionary<SceneObjectId, Canvas2DSceneItem> FixedHighlights);

public sealed partial class Canvas2DSceneBuilder
{
    private Canvas2DBoundedRouteBend? PrepareBoundedRouteBend(
        Canvas2DBoundedPresentationSource source, EditorStateSnapshot state,
        Canvas2DSceneItem[] overlays,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>? labels)
    {
        if (_contributors.Descriptors.Any(static descriptor =>
                descriptor.RouteBendDependency != Canvas2DSceneTransientDependency.Invariant) ||
            state.ActiveGesture is not { Kind: Canvas2DRouteGestureMetadata.Kind } gesture ||
            !TryGetRouteTarget(gesture, out var targetId, out var owner, out var bend) ||
            state.Selection.Length != 1 || state.Selection[0] != owner ||
            state.SemanticSceneSelection is not null || !state.TemporaryFeedback.IsEmpty ||
            !source.Families.TryGetValue(owner, out var family) || family.Length > 64 ||
            !source.ItemsById.TryGetValue(targetId, out var target) ||
            target.Origin.VisualStateId != owner || target.Origin.ProjectedObjectId is not { } connector ||
            !HasBooleanMetadata(target, Canvas2DRouteGestureMetadata.RouteEditable) ||
            bend <= 0 || bend >= Canvas2DConnectorPathMetadata.ResolveEditable(target).Length - 1 || labels is null)
            return null;

        // A real pointer normally hovers the bend handle before pressing it. Those
        // handles are recreated by the common overlay composer from this same family.
        if (state.HoveredObjectId is { } hover && family.All(item => item.Id != hover) &&
            !overlays.Any(item => item.Id == hover && item.Origin.VisualStateId == owner &&
                item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.HandleRole)))
            return null;

        var highlights = overlays.Where(static item => item.Layer != Canvas2DSceneLayer.Overlay).ToArray();
        if (highlights.Any(item => item.Layer != Canvas2DSceneLayer.Connector ||
                !item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState) ||
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None))
            return null;
        return new(labels.Where(pair => pair.Value.Label.OwnerId == connector).ToImmutableDictionary(),
            source.Items.Any(Canvas2DConnectorLineJumpMetadata.HasHitTarget),
            highlights.ToImmutableDictionary(static item => item.Id));
    }

    internal Canvas2DScene? TryReuseForRouteBend(
        Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { RouteBend: not null } prior ||
            _contributors.Descriptors.Any(static descriptor =>
                descriptor.RouteBendDependency != Canvas2DSceneTransientDependency.Invariant))
            return null;
        var provenance = prior.Source.Provenance;
        if (!ReferenceEquals(provenance.Builder, this) ||
            !ReferenceEquals(provenance.Document, document) || provenance.ScopeId != scopeId ||
            !ReferenceEquals(provenance.Graph, graph) || !ReferenceEquals(provenance.Layout, layout) ||
            !ReferenceEquals(provenance.Routing, routing) ||
            !ReferenceEquals(provenance.ProfileViewState, profileViewState) ||
            !ReferenceEquals(provenance.ProfileElementViewState, profileElementViewState) ||
            previous.DocumentId != document.DocumentId || previous.SourceRevision != document.Revision ||
            editorState.ActiveGesture is not { Kind: Canvas2DRouteGestureMetadata.Kind } ||
            !IsMoveTransition(prior.EditorState, editorState))
            return null;

        // Reuse the saved connector family, label measurements and jump presentation.
        // The common preview composer uses total displacement from the gesture origin.
        return TryComposeBoundedPresentation(previous, prior, document, scopeId, profileViewState,
            profileElementViewState, graph, layout, routing, editorState, cancellationToken);
    }
}
