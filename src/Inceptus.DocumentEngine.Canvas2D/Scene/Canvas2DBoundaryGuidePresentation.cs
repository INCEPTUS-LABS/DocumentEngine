using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

// At most two framework-owned, non-hittable lines. Insertion positions refer to
// stable content and are computed once, even when a guide is currently absent.
internal sealed record Canvas2DBoundaryGuidePresentation(
    ImmutableArray<Canvas2DSceneItem> Items,
    int HorizontalContentIndex,
    int VerticalContentIndex)
{
    internal static Canvas2DBoundaryGuidePresentation Empty { get; } = new([], 0, 0);

    internal bool HasSamePresentation(Canvas2DBoundaryGuidePresentation other) =>
        HorizontalContentIndex == other.HorizontalContentIndex &&
        VerticalContentIndex == other.VerticalContentIndex &&
        Items.AsSpan().SequenceEqual(other.Items.AsSpan());
}
