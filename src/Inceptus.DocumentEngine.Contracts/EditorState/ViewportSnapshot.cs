using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.EditorState;

/// <summary>
/// Describes the transient logical viewport without browser or backing-store state.
/// </summary>
public sealed class ViewportSnapshot : IEquatable<ViewportSnapshot>
{
    public ViewportSnapshot(
        double zoom,
        VectorD pan,
        RectD? visibleDocumentRegion = null)
    {
        if (!double.IsFinite(zoom) || zoom <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoom),
                zoom,
                "Viewport zoom must be finite and greater than zero.");
        }

        Zoom = zoom;
        Pan = pan;
        VisibleDocumentRegion = visibleDocumentRegion;
    }

    public static ViewportSnapshot Default { get; } = new(1d, default);

    public double Zoom { get; }

    public VectorD Pan { get; }

    public RectD? VisibleDocumentRegion { get; }

    public bool Equals(ViewportSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Zoom.Equals(other.Zoom) &&
        Pan == other.Pan &&
        VisibleDocumentRegion == other.VisibleDocumentRegion;

    public override bool Equals(object? obj) => Equals(obj as ViewportSnapshot);

    public override int GetHashCode() => HashCode.Combine(Zoom, Pan, VisibleDocumentRegion);
}
