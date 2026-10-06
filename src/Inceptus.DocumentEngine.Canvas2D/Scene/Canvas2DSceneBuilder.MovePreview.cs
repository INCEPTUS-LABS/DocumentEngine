using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private static Canvas2DEditorOverlayInputs CreateEditorOverlayInputs(
        ProjectedGraph graph, VisualModelSnapshot visualModel) => new(
        visualModel.VisualStates.ToDictionary(static visual => visual.Id),
        graph.Ports.Select(static port => new
        {
            Port = port,
            Anchor = ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) ? anchor : null,
        }).Where(static entry => entry.Anchor is not null)
            .GroupBy(static entry => entry.Port.OwnerNodeId)
            .ToDictionary(static group => group.Key, static group => group
                .Select(static entry => entry.Anchor!).OrderBy(static anchor => anchor.Side)
                .ThenBy(static anchor => anchor.Order)
                .ThenBy(static anchor => anchor.Id.Value, StringComparer.Ordinal).ToArray()),
        ConnectorAnchorOccupancy.EnumerateEndpointReferences(visualModel).ToHashSet());

    private Canvas2DBoundedPresentation? CreateBoundedPresentation(
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        VisualModelSnapshot visualModel, EditorStateSnapshot editorState,
        ScenePresentationInput? presentation, Canvas2DSceneItem[] baseItems,
        Canvas2DSceneItem[] overlays, Canvas2DEditorOverlayInputs overlayInputs,
        List<Diagnostic> diagnostics,
        ImmutableArray<Canvas2DPlacementLabelLayout> placementLabelLayouts,
        ImmutableArray<Canvas2DSceneItem> placementItems,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>? measuredLabels,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>? measuredConnectorLabels)
    {
        if (presentation is null || !ReferenceEquals(visualModel, presentation.Document.VisualModel) ||
            diagnostics.Count != 0 || overlays.Length > Canvas2DBoundedPresentation.MaximumItems)
        {
            return null;
        }

        // Preserve the pre-overlay composition order for ghost ordinals. The final stable
        // drawing array is separately ordered by the same canonical comparator.
        var families = baseItems.Where(static item => item.Origin.VisualStateId is not null)
            .GroupBy(static item => item.Origin.VisualStateId!)
            .ToImmutableDictionary(static group => group.Key, static group => group.ToImmutableArray());
        var attachedOwners = graph.Nodes.Where(static node => node.PlacementHint?.BoundaryAttachment is not null)
            .Select(static node => node.PlacementHint!.BoundaryAttachment!.AttachedToElementId).ToHashSet();
        var movable = graph.Nodes.Where(node => node.Source.VisualStateId is not null &&
                node.PlacementHint?.BoundaryAttachment is null &&
                !attachedOwners.Contains(node.Source.SemanticElementId))
            .Select(static node => node.Source.VisualStateId!).ToImmutableHashSet();
        var orderedBase = baseItems.ToArray();
        SuppressInstalledNodeLabelGesture(editorState, orderedBase);
        SuppressInstalledConnectorLabelGesture(editorState, orderedBase);
        Array.Sort(orderedBase, CompareItems);
        var source = new Canvas2DBoundedPresentationSource(
            new(this, presentation.Document, presentation.ActiveScopeId,
                presentation.ModelProfileViewState, presentation.ModelProfileElementViewState,
                graph, layout, routing, editorState),
            orderedBase.ToImmutableArray(), families,
            baseItems.ToImmutableDictionary(static item => item.Id), movable, overlayInputs);
        var supportsMove = _contributors.Descriptors.All(static descriptor =>
            descriptor.MoveGestureDependency == Canvas2DSceneMoveGestureDependency.Invariant) &&
            IsSupportedMoveEditorState(source, editorState);
        var supportsSelection = _contributors.Descriptors.All(static descriptor =>
            descriptor.HoverDependency == Canvas2DSceneTransientDependency.Invariant ||
            descriptor.VisualSelectionDependency == Canvas2DSceneTransientDependency.Invariant) &&
            IsSupportedSelectionEditorState(source, editorState);
        var supportsPlacement = SupportsPlacementContributors() && IsSupportedPlacementEditorState(editorState);
        var supportsResize = SupportsSpatialResizeContributors() && IsSupportedSpatialResizeState(editorState);
        var labelMove = PrepareBoundedNodeLabelMove(source, editorState, measuredLabels);
        // Partitioning a freshly composed resize frame makes no assumption about
        // contributor invariance. Reuse requires exact output equality below.
        var labelResize = editorState.ActiveGesture is { Kind: Canvas2DNodeLabelGestureMetadata.Kind } labelGesture &&
            TryGetNodeLabelGestureTarget(labelGesture, out _, out _, out var labelId, out _, out var operation, out _) &&
            operation == Canvas2DNodeLabelGestureOperation.Resize &&
            measuredLabels?.GetValueOrDefault(labelId)?.NodeLabelPreview is not null;
        var routeBend = PrepareBoundedRouteBend(source, editorState, overlays, measuredConnectorLabels);
        var connectorLabelMove = PrepareBoundedConnectorLabelMove(source, editorState, overlays);
        var fixedHighlights = routeBend?.FixedHighlights ?? connectorLabelMove?.FixedHighlights;
        if (fixedHighlights is { IsEmpty: false })
        {
            // Selected connector/jump highlights keep their canonical Connector layer.
            // They are fixed during a bend drag; the renderer's bounded family stays Overlay-only.
            var stableItems = source.Items.Concat(fixedHighlights.Values).ToArray();
            Array.Sort(stableItems, CompareItems);
            source = source with
            {
                Items = stableItems.ToImmutableArray(),
                ItemsById = source.ItemsById.AddRange(fixedHighlights),
            };
            overlays = overlays.Where(item => !fixedHighlights.ContainsKey(item.Id)).ToArray();
        }
        if ((!supportsMove && !supportsSelection && !supportsPlacement && !supportsResize && labelMove is null && !labelResize && routeBend is null && connectorLabelMove is null) ||
            overlays.Any(static item => item.Layer != Canvas2DSceneLayer.Overlay))
        {
            return null;
        }

        Array.Sort(overlays, CompareItems);
        return CreateMovePresentation(source, editorState, overlays.ToImmutableArray()) with
        {
            PlacementLabelLayouts = placementLabelLayouts,
            PlacementItems = placementItems,
            NodeLabelMove = labelMove,
            RouteBend = routeBend,
            NodeLabelResize = labelResize,
            ConnectorLabelMove = connectorLabelMove,
        };
    }

    internal Canvas2DScene? TryReuseForMovePreview(
        Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { } prior ||
            _contributors.Descriptors.Any(static descriptor =>
                descriptor.MoveGestureDependency != Canvas2DSceneMoveGestureDependency.Invariant))
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
            !IsMoveTransition(prior.EditorState, editorState) ||
            !IsSupportedMoveEditorState(source, editorState))
        {
            return null;
        }

        return TryComposeBoundedPresentation(previous, prior, document, scopeId, profileViewState,
            profileElementViewState, graph, layout, routing, editorState, cancellationToken);
    }

    private Canvas2DScene? TryComposeBoundedPresentation(
        Canvas2DScene previous, Canvas2DBoundedPresentation prior,
        DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing,
        EditorStateSnapshot editorState, CancellationToken cancellationToken)
    {
        var source = prior.Source;
        var input = new List<Canvas2DSceneItem>();
        if (editorState.Selection.Length == 1)
        {
            input.AddRange(source.Families[editorState.Selection[0]]);
        }
        if (prior.RouteBend is null && prior.ConnectorLabelMove is null && editorState.HoveredObjectId is { } hover && input.All(item => item.Id != hover))
        {
            input.AddRange(source.Families[source.ItemsById[hover].Origin.VisualStateId!]);
        }
        var start = input.Count;
        var diagnostics = new List<Diagnostic>();
        ComposeEditorOverlays(editorState, graph, document.VisualModel, input, diagnostics,
            measuredLabels: null, prior.RouteBend?.Labels, source.OverlayInputs,
            prior.RouteBend?.HasLineJumps ?? prior.ConnectorLabelMove?.HasLineJumps);
        AssociateEditorOverlaysWithSpatialPresentation(input, start);
        var overlays = input.Skip(start).Where(static item => !IsDocumentBoundaryGuide(item)).ToList();
        if ((prior.RouteBend?.FixedHighlights ?? prior.ConnectorLabelMove?.FixedHighlights) is { } fixedHighlights)
        {
            foreach (var highlight in fixedHighlights.Values)
            {
                // Validate the fixed contribution instead of assuming its invariance.
                var index = overlays.FindIndex(item => item.Id == highlight.Id);
                if (index < 0 || !overlays[index].Equals(highlight)) return null;
                overlays.RemoveAt(index);
            }
        }
        if (diagnostics.Count != 0 || overlays.Count > Canvas2DBoundedPresentation.MaximumItems ||
            overlays.Any(static item => item.Layer != Canvas2DSceneLayer.Overlay))
        {
            return null;
        }

        foreach (var overlay in overlays)
        {
            ValidateItem(overlay, diagnostics);
            if (source.ItemsById.ContainsKey(overlay.Id))
            {
                return null;
            }
        }
        if (diagnostics.Count != 0 || overlays.Select(static item => item.Id).Distinct().Count() != overlays.Count)
        {
            return null;
        }
        overlays.Sort(CompareItems);
        // Even invariant handles retain their exact object references across move samples.
        for (var index = 0; index < overlays.Count; index++)
        {
            var candidate = overlays[index];
            var existing = prior.Items.FirstOrDefault(item => item.Id == candidate.Id);
            if (candidate.Equals(existing))
            {
                overlays[index] = existing!;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var presentation = CreateMovePresentation(source, editorState, overlays.ToImmutableArray()) with
        { RouteBend = prior.RouteBend, ConnectorLabelMove = prior.ConnectorLabelMove };
        var complete = MergeBoundedItems(source, presentation);
        cancellationToken.ThrowIfCancellationRequested();
        return previous.WithBoundedPresentation(presentation, complete,
            CreatePanReuseSource(graph, layout, routing, document.VisualModel, editorState,
                new ScenePresentationInput(document, scopeId, profileViewState, profileElementViewState)));
    }

    private static ImmutableArray<Canvas2DSceneItem> MergeBoundedItems(
        Canvas2DBoundedPresentationSource source, Canvas2DBoundedPresentation presentation)
    {
        var overlays = presentation.Items;
        var complete = ImmutableArray.CreateBuilder<Canvas2DSceneItem>(source.Items.Length + overlays.Length);
        var boundedIndex = 0;
        for (var index = 0; index <= source.Items.Length; index++)
        {
            while (boundedIndex < overlays.Length && presentation.BeforeContentIndices[boundedIndex] == index)
            {
                complete.Add(overlays[boundedIndex++]);
            }
            if (index < source.Items.Length)
            {
                complete.Add(source.Items[index]);
            }
        }
        return complete.MoveToImmutable();
    }

    private static Canvas2DBoundedPresentation CreateMovePresentation(
        Canvas2DBoundedPresentationSource source, EditorStateSnapshot editorState,
        ImmutableArray<Canvas2DSceneItem> overlays)
    {
        var indices = ImmutableArray.CreateBuilder<int>(overlays.Length);
        foreach (var overlay in overlays)
        {
            var low = 0;
            var high = source.Items.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (CompareItems(source.Items[middle], overlay) < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            indices.Add(low);
        }
        return new(source, editorState, overlays, indices.MoveToImmutable());
    }

    private static bool IsSupportedMoveEditorState(Canvas2DBoundedPresentationSource source, EditorStateSnapshot state)
    {
        if (state.Selection.Length > 1 || !state.TemporaryFeedback.IsEmpty || state.SemanticSceneSelection is not null)
        {
            return false;
        }
        if (state.HoveredObjectId is { } hover &&
            (!source.ItemsById.TryGetValue(hover, out var hovered) ||
             hovered.Layer is not (Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Label) ||
             hovered.Origin.VisualStateId is not { } hoveredVisual ||
             !source.MovableNodes.Contains(hoveredVisual) ||
             !source.Families.TryGetValue(hoveredVisual, out var hoveredFamily) || hoveredFamily.Length > 64))
        {
            return false;
        }
        if (state.Selection.Length == 1)
        {
            var id = state.Selection[0];
            if (!source.MovableNodes.Contains(id) || !source.Families.TryGetValue(id, out var family) ||
                family.Length > 64 || ResolveLogicalSelectionTarget(family, id) is not { } target ||
                target.Layer != Canvas2DSceneLayer.Content ||
                !HasBooleanMetadata(target, Canvas2DMoveGestureMetadata.MoveCapable) ||
                target.Origin.ProjectedObjectId is not { } nodeId ||
                (source.OverlayInputs.Anchors.TryGetValue(nodeId, out var anchors) && anchors.Length > 32))
            {
                return false;
            }
        }
        return state.ActiveGesture is not { } gesture ||
            (StringComparer.Ordinal.Equals(gesture.Kind, Canvas2DMoveGestureMetadata.Kind) &&
             TryGetMoveTargets(gesture, out var targets) && targets.Count == 1 &&
             state.Selection.Length == 1 && targets.Contains(state.Selection[0]));
    }

    private static bool IsMoveTransition(EditorStateSnapshot previous, EditorStateSnapshot current)
    {
        if (!previous.Viewport.Equals(current.Viewport) || previous.HoveredObjectId != current.HoveredObjectId ||
            previous.SemanticSceneSelection != current.SemanticSceneSelection ||
            !StringComparer.Ordinal.Equals(previous.ActiveToolId, current.ActiveToolId) ||
            !StringComparer.Ordinal.Equals(previous.FocusTargetId, current.FocusTargetId) ||
            !previous.ToolState.Equals(current.ToolState) ||
            !previous.TemporaryFeedback.IsEmpty || !current.TemporaryFeedback.IsEmpty)
        {
            return false;
        }
        if (previous.ActiveGesture is not { } oldGesture)
        {
            return current.ActiveGesture is not null;
        }
        if (!previous.Selection.AsSpan().SequenceEqual(current.Selection.AsSpan()))
        {
            return false;
        }
        return current.ActiveGesture is not { } gesture ||
            (StringComparer.Ordinal.Equals(oldGesture.Id, gesture.Id) &&
             StringComparer.Ordinal.Equals(oldGesture.Kind, gesture.Kind) &&
             oldGesture.Origin == gesture.Origin &&
             (oldGesture.Properties.Equals(gesture.Properties) ||
              (gesture.Kind == Canvas2DRouteGestureMetadata.Kind &&
               new PropertyMap(oldGesture.Properties.Where(pair => !Canvas2DRouteGestureMetadata.IsCandidateProperty(pair.Key)))
                   .Equals(new PropertyMap(gesture.Properties.Where(pair => !Canvas2DRouteGestureMetadata.IsCandidateProperty(pair.Key)))))));
    }
}
