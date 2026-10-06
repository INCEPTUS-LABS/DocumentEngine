using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

// Shared provenance for move previews and certified hover/selection presentation.
// The complete logical Scene remains the interaction authority.
internal sealed record Canvas2DBoundedPresentationSource(
    Canvas2DScenePanReuseSource Provenance,
    ImmutableArray<Canvas2DSceneItem> Items,
    ImmutableDictionary<VisualStateId, ImmutableArray<Canvas2DSceneItem>> Families,
    ImmutableDictionary<SceneObjectId, Canvas2DSceneItem> ItemsById,
    ImmutableHashSet<VisualStateId> MovableNodes,
    Canvas2DEditorOverlayInputs OverlayInputs);

internal sealed record Canvas2DBoundedPresentation(
    Canvas2DBoundedPresentationSource Source,
    EditorStateSnapshot EditorState,
    ImmutableArray<Canvas2DSceneItem> Items,
    ImmutableArray<int> BeforeContentIndices,
    ImmutableArray<Canvas2DPlacementLabelLayout> PlacementLabelLayouts = default,
    ImmutableArray<Canvas2DSceneItem> PlacementItems = default,
    Canvas2DMeasuredNodeLabel? NodeLabelMove = null,
    Canvas2DBoundedRouteBend? RouteBend = null,
    bool NodeLabelResize = false,
    Canvas2DBoundedConnectorLabelMove? ConnectorLabelMove = null)
{
    internal const int MaximumItems = 128;
}

internal sealed record Canvas2DPlacementLabelLayout(
    string FeedbackId,
    string Text,
    SizeD BodySize,
    NodeLabelPlacement Placement,
    Canvas2DTextLayout TextLayout);

internal sealed record Canvas2DEditorOverlayInputs(
    IReadOnlyDictionary<VisualStateId, VisualStateSnapshot> Visuals,
    IReadOnlyDictionary<ProjectedObjectId, ProjectedConnectorAnchor[]> Anchors,
    HashSet<ConnectorAnchorId> ReferencedAnchors);
