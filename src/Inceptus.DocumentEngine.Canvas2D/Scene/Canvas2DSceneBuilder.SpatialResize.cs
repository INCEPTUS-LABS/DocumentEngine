using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private bool SupportsSpatialResizeContributors() => _contributors.Descriptors.All(static descriptor =>
        descriptor.RegionResizeDependency == Canvas2DSceneTransientDependency.Invariant);

    private static bool IsSupportedSpatialResizeState(EditorStateSnapshot state) =>
        state.TemporaryFeedback.Count(static feedback => feedback.SpatialResize is not null) <= 1 &&
        (state.ActiveGesture is null || state.ActiveGesture.Kind == Canvas2DSpatialResizeFeedback.FeedbackKind);

    private static bool IsSpatialResizeItem(Canvas2DSceneItem item) =>
        item.Id == Canvas2DSceneObjectIdentity.ForEditorState("spatial-resize:acquired") ||
        item.Id == Canvas2DSceneObjectIdentity.ForEditorState("spatial-resize:candidate") ||
        item.Id == Canvas2DSceneObjectIdentity.ForEditorState("spatial-resize:stack");

    private static void ComposeSpatialResizeFeedback(EditorStateSnapshot state, List<Canvas2DSceneItem> items)
    {
        var feedbacks = state.TemporaryFeedback.Where(static value => value.SpatialResize is not null).Take(2).ToArray();
        if (feedbacks.Length != 1) return;
        var feedback = feedbacks[0].SpatialResize;
        if (feedback is null || !double.IsFinite(feedback.CandidateEdge)) return;
        var target = feedback.Target;
        var bounds = target.PaintedBounds;
        var bottom = target.Edge == Canvas2DSpatialResizeEdge.Bottom;
        var acquiredStart = bottom ? new PointD(bounds.Left, bounds.Bottom) : new PointD(bounds.Right, bounds.Top);
        var acquiredEnd = new PointD(bounds.Right, bounds.Bottom);
        Add("acquired", acquiredStart, acquiredEnd, "#2563eb", 2.5d);
        if (feedback.Phase == Canvas2DSpatialResizePhase.Hover) return;
        var candidate = feedback.CandidateEdge;
        var color = feedback.IsAllowed ? "#16a34a" : "#dc2626";
        Add("candidate", bottom ? new PointD(bounds.Left, candidate) : new PointD(candidate, bounds.Top),
            bottom ? new PointD(bounds.Right, candidate) : new PointD(candidate, bounds.Bottom), color, 2.5d);
        if (!bottom)
            Add("stack", new PointD(candidate, target.GuideSpan.Top), new PointD(candidate, target.GuideSpan.Bottom), color, 1d);

        void Add(string role, PointD start, PointD end, string color, double width)
        {
            var key = $"spatial-resize:{role}";
            items.Add(new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForEditorState(key), Canvas2DSceneLayer.Overlay,
                role == "stack" ? 4200 : role == "acquired" ? 4201 : 4202, Canvas2DSceneGeometry.Path([start, end]),
                new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.EditorState, stableSourceKey: key),
                style: new Canvas2DSceneStyle(stroke: color, strokeWidth: width / state.Viewport.Zoom,
                    dashPattern: role == "stack" ? [4d / state.Viewport.Zoom, 3d / state.Viewport.Zoom] : null),
                hitTestPolicy: Canvas2DHitTestPolicy.None));
        }
    }

    internal Canvas2DScene? TryReuseForSpatialResize(Canvas2DScene previous, DocumentSnapshot document, DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState, ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing, EditorStateSnapshot state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (previous.BoundedPresentation is not { } prior || !SupportsSpatialResizeContributors() ||
            !IsSupportedSpatialResizeState(prior.EditorState) || !IsSupportedSpatialResizeState(state)) return null;
        var old = prior.EditorState;
        if (!old.Viewport.Equals(state.Viewport) || !old.Selection.AsSpan().SequenceEqual(state.Selection.AsSpan()) ||
            old.HoveredObjectId != state.HoveredObjectId || old.SemanticSceneSelection != state.SemanticSceneSelection ||
            old.ActiveToolId != state.ActiveToolId || old.FocusTargetId != state.FocusTargetId || !old.ToolState.Equals(state.ToolState) ||
            !old.TemporaryFeedback.Where(static feedback => feedback.SpatialResize is null)
                .SequenceEqual(state.TemporaryFeedback.Where(static feedback => feedback.SpatialResize is null)) ||
            (!old.TemporaryFeedback.Any(static feedback => feedback.SpatialResize is not null) &&
             !state.TemporaryFeedback.Any(static feedback => feedback.SpatialResize is not null))) return null;
        var source = prior.Source;
        var provenance = source.Provenance;
        if (!ReferenceEquals(provenance.Builder, this) || !ReferenceEquals(provenance.Document, document) || provenance.ScopeId != scopeId ||
            !ReferenceEquals(provenance.Graph, graph) || !ReferenceEquals(provenance.Layout, layout) || !ReferenceEquals(provenance.Routing, routing) ||
            !ReferenceEquals(provenance.ProfileViewState, profileViewState) || !ReferenceEquals(provenance.ProfileElementViewState, profileElementViewState) ||
            previous.DocumentId != document.DocumentId || previous.SourceRevision != document.Revision) return null;
        var resize = new List<Canvas2DSceneItem>();
        ComposeSpatialResizeFeedback(state, resize);
        var diagnostics = new List<Contracts.Diagnostics.Diagnostic>();
        foreach (var item in resize)
        {
            ValidateItem(item, diagnostics);
            if (item.Origin.Categories != Canvas2DSceneOriginCategory.EditorState || item.Origin.VisualStateId is not null ||
                item.Origin.SemanticElementId is not null || item.Origin.ProjectedObjectId is not null ||
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None || item.Metadata.Count != 0 || item.SpatialRegion is not null ||
                item.PersistentAppearance.Count != 0 || item.Layer != Canvas2DSceneLayer.Overlay) return null;
        }
        if (resize.Count > 3 || diagnostics.Count != 0) return null;
        var overlays = prior.Items.Where(static item => !IsSpatialResizeItem(item)).Concat(resize).ToList();
        if (overlays.Count > Canvas2DBoundedPresentation.MaximumItems || overlays.Any(item => source.ItemsById.ContainsKey(item.Id)) ||
            overlays.Select(static item => item.Id).Distinct().Count() != overlays.Count) return null;
        overlays.Sort(CompareItems);
        var bounded = CreateMovePresentation(source, state, overlays.ToImmutableArray()) with
        {
            PlacementItems = prior.PlacementItems,
            PlacementLabelLayouts = prior.PlacementLabelLayouts,
        };
        return previous.WithBoundedPresentation(bounded, MergeBoundedItems(source, bounded),
            CreatePanReuseSource(graph, layout, routing, document.VisualModel, state,
                new ScenePresentationInput(document, scopeId, profileViewState, profileElementViewState)));
    }
}
