using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private Canvas2DMeasuredNodeLabel? PrepareBoundedNodeLabelMove(
        Canvas2DBoundedPresentationSource source, EditorStateSnapshot state,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>? labels)
    {
        if (_contributors.Descriptors.Any(static descriptor =>
                descriptor.NodeLabelMoveDependency != Canvas2DSceneTransientDependency.Invariant) ||
            state.ActiveGesture is not { Kind: Canvas2DNodeLabelGestureMetadata.Kind } gesture ||
            !TryGetNodeLabelGestureTarget(gesture, out _, out _, out var labelId, out var owner,
                out var operation, out _) || operation != Canvas2DNodeLabelGestureOperation.Move ||
            state.Selection.Length != 1 || state.Selection[0] != owner ||
            state.SemanticSceneSelection is not null ||
            !state.TemporaryFeedback.IsEmpty ||
            !source.Families.TryGetValue(owner, out var family) || family.Length > 64 ||
            (state.HoveredObjectId is { } hover && family.All(item => item.Id != hover)) ||
            labels is null || !labels.TryGetValue(labelId, out var label) || label.NodeLabelPreview is null)
            return null;
        return label;
    }

    internal Canvas2DScene? TryReuseForNodeLabelMove(
        Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { NodeLabelMove: { } seed } prior ||
            _contributors.Descriptors.Any(static descriptor =>
                descriptor.NodeLabelMoveDependency != Canvas2DSceneTransientDependency.Invariant))
            return null;
        var source = prior.Source;
        var provenance = source.Provenance;
        if (!ReferenceEquals(provenance.Builder, this) ||
            !ReferenceEquals(provenance.Document, document) || provenance.ScopeId != scopeId ||
            !ReferenceEquals(provenance.Graph, graph) || !ReferenceEquals(provenance.Layout, layout) ||
            !ReferenceEquals(provenance.Routing, routing) ||
            !ReferenceEquals(provenance.ProfileViewState, profileViewState) ||
            !ReferenceEquals(provenance.ProfileElementViewState, profileElementViewState) ||
            previous.DocumentId != document.DocumentId || previous.SourceRevision != document.Revision ||
            editorState.ActiveGesture is not { } gesture ||
            !IsMoveTransition(prior.EditorState, editorState))
            return null;

        // Always translate the immutable initial preparation, never the previous sample.
        // Text shaping and wrapping are position invariant; resize uses the normal path.
        var bounds = seed.Current.PlacementBounds.Translate(gesture.Current - gesture.Origin);
        var original = seed.NodeLabelPreview!;
        var label = seed with
        {
            NodeLabelPreview = original with
            {
                PlacementBounds = bounds,
                ClipBounds = bounds,
            }
        };
        var labels = new Dictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel> { [seed.Label.Id] = label };
        var input = source.Families[editorState.Selection[0]].ToList();
        var start = input.Count;
        var diagnostics = new List<Diagnostic>();
        ComposeEditorOverlays(editorState, graph, document.VisualModel, input, diagnostics,
            labels, null, source.OverlayInputs);
        AssociateEditorOverlaysWithSpatialPresentation(input, start);
        var overlays = input.Skip(start).Where(static item => !IsDocumentBoundaryGuide(item)).ToList();
        if (diagnostics.Count != 0 || overlays.Count > Canvas2DBoundedPresentation.MaximumItems ||
            overlays.Any(static item => item.Layer != Canvas2DSceneLayer.Overlay))
            return null;
        foreach (var item in overlays)
        {
            ValidateItem(item, diagnostics);
            if (source.ItemsById.ContainsKey(item.Id)) return null;
        }
        if (diagnostics.Count != 0 || overlays.Select(static item => item.Id).Distinct().Count() != overlays.Count)
            return null;
        overlays.Sort(CompareItems);
        var presentation = CreateMovePresentation(source, editorState, overlays.ToImmutableArray()) with { NodeLabelMove = seed };
        cancellationToken.ThrowIfCancellationRequested();
        return previous.WithBoundedPresentation(presentation, MergeBoundedItems(source, presentation),
            CreatePanReuseSource(graph, layout, routing, document.VisualModel, editorState,
                new ScenePresentationInput(document, scopeId, profileViewState, profileElementViewState)));
    }

    private static void SuppressInstalledMovingLabel(EditorStateSnapshot state, IList<Canvas2DSceneItem> items)
    {
        if (state.ActiveGesture is not { Kind: Canvas2DNodeLabelGestureMetadata.Kind } gesture ||
            !TryGetNodeLabelGestureTarget(gesture, out _, out _, out var labelId, out var owner,
                out var operation, out _) || operation != Canvas2DNodeLabelGestureOperation.Move)
            return;
        // Keep installed identity/bounds for gesture currency checks, but render only the
        // complete current preview family. Activation uploads this immutable base once.
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Layer != Canvas2DSceneLayer.Label || item.Origin.ProjectedObjectId != labelId ||
                item.Origin.VisualStateId != owner) continue;
            items[index] = new Canvas2DSceneItem(item.Id, item.Layer, item.ZIndex, item.Geometry, item.Origin,
                item.Transform, item.Clip, item.Style, false, Canvas2DHitTestPolicy.None,
                item.PersistentAppearance, item.Metadata, item.Bounds, item.SpatialRegion, item.ConnectorPresentationMapping);
        }
    }
}
