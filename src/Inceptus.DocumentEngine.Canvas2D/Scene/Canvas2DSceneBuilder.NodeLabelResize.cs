namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    internal static Canvas2DScene ReuseEqualNodeLabelResizeContent(Canvas2DScene previous, Canvas2DScene current)
    {
        if (previous.BoundedPresentation is not { NodeLabelResize: true } prior ||
            current.BoundedPresentation is not { NodeLabelResize: true } next ||
            !IsMoveTransition(prior.EditorState, next.EditorState))
            return current;

        var before = prior.Source.Provenance;
        var after = next.Source.Provenance;
        if (!ReferenceEquals(before.Builder, after.Builder) ||
            !ReferenceEquals(before.Document, after.Document) || before.ScopeId != after.ScopeId ||
            !ReferenceEquals(before.Graph, after.Graph) || !ReferenceEquals(before.Layout, after.Layout) ||
            !ReferenceEquals(before.Routing, after.Routing) ||
            !ReferenceEquals(before.ProfileViewState, after.ProfileViewState) ||
            !ReferenceEquals(before.ProfileElementViewState, after.ProfileElementViewState) ||
            !prior.Source.Items.AsSpan().SequenceEqual(next.Source.Items.AsSpan()))
            return current;

        // Every contributor and all validation have already run for this frame.
        // Exact drawing-input equality authorizes retaining the renderer's existing
        // immutable base. Keep all current Scene metadata and fresh gesture overlays.
        var presentation = next with { Source = prior.Source };
        return current.WithBoundedPresentation(presentation, MergeBoundedItems(prior.Source, presentation), current.PanReuseSource);
    }
}
