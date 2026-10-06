using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

internal sealed record Canvas2DBoundedConnectorLabelMove(
    bool HasLineJumps,
    ImmutableDictionary<SceneObjectId, Canvas2DSceneItem> FixedHighlights);

public sealed partial class Canvas2DSceneBuilder
{
    internal static bool IsBoundedConnectorLabelMoveTransition(Canvas2DScene scene, EditorStateSnapshot state) =>
        scene.BoundedPresentation is { ConnectorLabelMove: not null } prior &&
        state.ActiveGesture is { Kind: Canvas2DLabelGestureMetadata.Kind } &&
        IsMoveTransition(prior.EditorState, state);

    private static Canvas2DSceneItem ResolveConnectorLabelHoverPreview(Canvas2DSceneItem target, EditorGestureSnapshot? gesture)
    {
        if (!TryGetConnectorLabelMoveTarget(gesture, out _, out var label, out var owner) ||
            target.Layer != Canvas2DSceneLayer.Label || target.Origin.ProjectedObjectId != label ||
            target.Origin.VisualStateId != owner) return target;
        var delta = gesture!.Current - gesture.Origin;
        return new Canvas2DSceneItem(target.Id, target.Layer, target.ZIndex, target.Geometry, target.Origin,
            target.Transform.Then(Matrix2D.CreateTranslation(delta)), target.Clip?.Translate(delta), target.Style,
            target.IsVisible, target.HitTestPolicy, target.PersistentAppearance, target.Metadata,
            target.Bounds.Translate(delta), target.SpatialRegion, target.ConnectorPresentationMapping);
    }

    private Canvas2DBoundedConnectorLabelMove? PrepareBoundedConnectorLabelMove(
        Canvas2DBoundedPresentationSource source, EditorStateSnapshot state, Canvas2DSceneItem[] overlays)
    {
        if (_contributors.Descriptors.Any(static descriptor =>
                descriptor.ConnectorLabelMoveDependency != Canvas2DSceneTransientDependency.Invariant) ||
            !TryGetConnectorLabelMoveTarget(state.ActiveGesture, out var connectorId, out var labelId, out var owner) ||
            state.Selection.Length != 1 || state.Selection[0] != owner ||
            state.SemanticSceneSelection is not null || !state.TemporaryFeedback.IsEmpty ||
            !source.Families.TryGetValue(owner!, out var family) || family.Length > 64 ||
            !source.ItemsById.TryGetValue(connectorId!, out var connector) ||
            connector.Layer != Canvas2DSceneLayer.Connector || connector.Origin.VisualStateId != owner ||
            !family.Any(item => item.Layer == Canvas2DSceneLayer.Label && item.Origin.ProjectedObjectId == labelId &&
                HasBooleanMetadata(item, Canvas2DLabelGestureMetadata.LabelMoveCapable)) ||
            (state.HoveredObjectId is { } hover && family.All(item => item.Id != hover)))
            return null;

        var highlights = overlays.Where(static item => item.Layer != Canvas2DSceneLayer.Overlay).ToArray();
        if (highlights.Any(item => item.Layer != Canvas2DSceneLayer.Connector ||
                !item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState) ||
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None))
            return null;
        return new(source.Items.Any(Canvas2DConnectorLineJumpMetadata.HasHitTarget),
            highlights.ToImmutableDictionary(static item => item.Id));
    }

    internal Canvas2DScene? TryReuseForConnectorLabelMove(
        Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { ConnectorLabelMove: not null } prior ||
            _contributors.Descriptors.Any(static descriptor =>
                descriptor.ConnectorLabelMoveDependency != Canvas2DSceneTransientDependency.Invariant))
            return null;
        var provenance = prior.Source.Provenance;
        if (!ReferenceEquals(provenance.Builder, this) ||
            !ReferenceEquals(provenance.Document, document) || provenance.ScopeId != scopeId ||
            !ReferenceEquals(provenance.Graph, graph) || !ReferenceEquals(provenance.Layout, layout) ||
            !ReferenceEquals(provenance.Routing, routing) ||
            !ReferenceEquals(provenance.ProfileViewState, profileViewState) ||
            !ReferenceEquals(provenance.ProfileElementViewState, profileElementViewState) ||
            previous.DocumentId != document.DocumentId || previous.SourceRevision != document.Revision ||
            editorState.ActiveGesture is not { Kind: Canvas2DLabelGestureMetadata.Kind } ||
            !IsMoveTransition(prior.EditorState, editorState))
            return null;

        // The immutable family contains the measured label and the captured route.
        // Only the label's total translation changes; shaping and routes stay untouched.
        return TryComposeBoundedPresentation(previous, prior, document, scopeId, profileViewState,
            profileElementViewState, graph, layout, routing, editorState, cancellationToken);
    }

    private static bool TryGetConnectorLabelMoveTarget(EditorGestureSnapshot? gesture,
        out SceneObjectId? connector, out ProjectedObjectId? label, out VisualStateId? owner)
    {
        connector = null;
        label = null;
        owner = null;
        if (gesture is not { Kind: Canvas2DLabelGestureMetadata.Kind } ||
            !gesture.Properties.TryGetValue(Canvas2DLabelGestureMetadata.TargetConnectorSceneObjectId, out var connectorValue) ||
            connectorValue.Kind != PropertyValueKind.Text || string.IsNullOrWhiteSpace(connectorValue.TextValue) ||
            !gesture.Properties.TryGetValue(Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId, out var labelValue) ||
            labelValue.Kind != PropertyValueKind.Text || string.IsNullOrWhiteSpace(labelValue.TextValue) ||
            !gesture.Properties.TryGetValue(Canvas2DLabelGestureMetadata.TargetVisualStateId, out var ownerValue) ||
            ownerValue.Kind != PropertyValueKind.Text || string.IsNullOrWhiteSpace(ownerValue.TextValue))
            return false;
        connector = new(connectorValue.TextValue);
        label = new(labelValue.TextValue);
        owner = new(ownerValue.TextValue);
        return true;
    }

    private static void SuppressInstalledConnectorLabelGesture(EditorStateSnapshot state, IList<Canvas2DSceneItem> items)
    {
        if (!TryGetConnectorLabelMoveTarget(state.ActiveGesture, out _, out var label, out var owner)) return;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Layer != Canvas2DSceneLayer.Label || item.Origin.ProjectedObjectId != label ||
                item.Origin.VisualStateId != owner) continue;
            // Keep source identity and geometry for currency checks, but draw only the preview.
            items[index] = new Canvas2DSceneItem(item.Id, item.Layer, item.ZIndex, item.Geometry, item.Origin,
                item.Transform, item.Clip, item.Style, false, Canvas2DHitTestPolicy.None,
                item.PersistentAppearance, item.Metadata, item.Bounds, item.SpatialRegion, item.ConnectorPresentationMapping);
        }
    }
}
