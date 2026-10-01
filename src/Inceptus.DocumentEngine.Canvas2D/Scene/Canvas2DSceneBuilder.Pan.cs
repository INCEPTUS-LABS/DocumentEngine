using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private Canvas2DScenePanReuseSource? CreatePanReuseSource(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        ScenePresentationInput? presentation) =>
        presentation is not null &&
        ReferenceEquals(visualModel, presentation.Document.VisualModel) &&
        editorState.ActiveGesture is null && editorState.TemporaryFeedback.IsEmpty &&
        _contributors.Descriptors.All(static descriptor =>
            descriptor.PanDependency == Canvas2DScenePanDependency.Invariant)
            ? new(this, presentation.Document, presentation.ActiveScopeId,
                presentation.ModelProfileViewState, presentation.ModelProfileElementViewState,
                graph, layout, routing, editorState)
            : null;

    // Called only for an explicit Pan operation, after the session's surface/currency gate.
    internal Canvas2DScene? TryReuseForPan(
        Canvas2DScene previous,
        DocumentSnapshot document,
        DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        EditorStateSnapshot editorState)
    {
        if (!CanReuseForPan(previous, document, scopeId, profileViewState,
                profileElementViewState, graph, layout, routing, editorState))
        {
            return null;
        }

        var source = previous.PanReuseSource!;
        var guides = new List<Canvas2DSceneItem>(2);
        ComposeDocumentBoundaryGuides(editorState, guides);
        guides.Sort(CompareItems);
        return previous.WithPan(
            source with { EditorState = editorState },
            previous.BoundaryGuides with { Items = guides.ToImmutableArray() });
    }

    internal bool CanDeferPanPresentation(
        Canvas2DScene previous,
        DocumentSnapshot document,
        DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        EditorStateSnapshot editorState) =>
        CanReuseForPan(previous, document, scopeId, profileViewState,
            profileElementViewState, graph, layout, routing, editorState);

    private bool CanReuseForPan(
        Canvas2DScene previous,
        DocumentSnapshot document,
        DocumentScopeId scopeId,
        ModelProfileViewStateSnapshot profileViewState,
        ModelProfileElementViewStateSnapshot profileElementViewState,
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        EditorStateSnapshot editorState) =>
        previous.PanReuseSource is { } source && ReferenceEquals(source.Builder, this) &&
        ReferenceEquals(source.Document, document) && source.ScopeId == scopeId &&
        ReferenceEquals(source.Graph, graph) && ReferenceEquals(source.Layout, layout) &&
        ReferenceEquals(source.Routing, routing) &&
        ReferenceEquals(source.ProfileViewState, profileViewState) &&
        ReferenceEquals(source.ProfileElementViewState, profileElementViewState) &&
        previous.DocumentId == document.DocumentId && previous.SourceRevision == document.Revision &&
        IsPurePan(source.EditorState, editorState);

    private static bool IsPurePan(EditorStateSnapshot previous, EditorStateSnapshot current)
    {
        if (previous.ActiveGesture is not null || current.ActiveGesture is not null ||
            !previous.TemporaryFeedback.IsEmpty || !current.TemporaryFeedback.IsEmpty ||
            !previous.Selection.AsSpan().SequenceEqual(current.Selection.AsSpan()) ||
            previous.SemanticSceneSelection != current.SemanticSceneSelection ||
            previous.HoveredObjectId != current.HoveredObjectId ||
            !StringComparer.Ordinal.Equals(previous.ActiveToolId, current.ActiveToolId) ||
            !StringComparer.Ordinal.Equals(previous.FocusTargetId, current.FocusTargetId) ||
            !previous.ToolState.Equals(current.ToolState) ||
            previous.Viewport.Zoom != current.Viewport.Zoom ||
            previous.Viewport.Pan == current.Viewport.Pan)
        {
            return false;
        }

        // Verify the exact visible-region conversion used by the Pan entry point,
        // including a previously unobserved surface. A resize is never a Pan.
        return previous.Viewport.VisibleDocumentRegion is null
            ? current.Viewport.VisibleDocumentRegion is null
            : current.Viewport.VisibleDocumentRegion == CalculateVisibleDocumentRegion(
                current.Viewport, CalculateCanvasCssSurface(previous.Viewport));
    }

    private static bool IsDocumentBoundaryGuide(Canvas2DSceneItem item) =>
        item.Origin.Categories == Canvas2DSceneOriginCategory.EditorState &&
        item.Origin.StableSourceKey is "document-boundary:x-axis" or "document-boundary:y-axis";
}
