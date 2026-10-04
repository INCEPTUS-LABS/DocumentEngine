using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Routing;

public sealed record ScopeTextMeasurementSnapshot
{
    public ScopeTextMeasurementSnapshot(TextMeasurementRequest request, TextMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metrics);
        Request = request;
        Metrics = metrics;
    }

    public TextMeasurementRequest Request { get; }
    public TextMetrics Metrics { get; }
}

/// <summary>Accepted caption measurements needed to reproduce the saved node/frame basis.</summary>
public sealed class ScopeNodeCaptionSnapshot : IEquatable<ScopeNodeCaptionSnapshot>
{
    public ScopeNodeCaptionSnapshot(
        VisualStateId ownerVisualStateId,
        ProjectedObjectId labelId,
        NodeLabelPlacement placement,
        NodeLabelVisualOverride? visualOverride,
        RectD placementBounds,
        RectD contentBounds,
        Matrix2D transform,
        IEnumerable<ScopeTextMeasurementSnapshot> lines)
    {
        ArgumentNullException.ThrowIfNull(ownerVisualStateId);
        ArgumentNullException.ThrowIfNull(labelId);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(lines);
        OwnerVisualStateId = ownerVisualStateId;
        LabelId = labelId;
        Placement = placement;
        VisualOverride = visualOverride;
        PlacementBounds = placementBounds;
        ContentBounds = contentBounds;
        Transform = transform;
        Lines = RoutingStateCollection.Copy(lines, nameof(lines));
    }

    public VisualStateId OwnerVisualStateId { get; }
    public ProjectedObjectId LabelId { get; }
    public NodeLabelPlacement Placement { get; }
    public NodeLabelVisualOverride? VisualOverride { get; }
    public RectD PlacementBounds { get; }
    public RectD ContentBounds { get; }
    public Matrix2D Transform { get; }
    public ImmutableArray<ScopeTextMeasurementSnapshot> Lines { get; }

    public bool Equals(ScopeNodeCaptionSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null && OwnerVisualStateId == other.OwnerVisualStateId &&
        LabelId == other.LabelId && Placement.Equals(other.Placement) &&
        Equals(VisualOverride, other.VisualOverride) && PlacementBounds == other.PlacementBounds &&
        ContentBounds == other.ContentBounds && Transform == other.Transform &&
        Lines.AsSpan().SequenceEqual(other.Lines.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ScopeNodeCaptionSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OwnerVisualStateId);
        hash.Add(LabelId);
        hash.Add(Placement);
        hash.Add(VisualOverride);
        hash.Add(PlacementBounds);
        hash.Add(ContentBounds);
        hash.Add(Transform);
        foreach (var line in Lines) hash.Add(line);
        return hash.ToHashCode();
    }
}
