using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public enum Canvas2DSpatialResizeEdge { Bottom, Right }

/// <summary>A transient, notation-owned capability on an actual painted spatial boundary.</summary>
public sealed class Canvas2DSpatialResizeTarget : IEquatable<Canvas2DSpatialResizeTarget>
{
    public Canvas2DSpatialResizeTarget(ModelProfileId profileId, Canvas2DSpatialRegionId acquiredRegionId,
        Canvas2DSpatialRegionId? authorityRegionId, Canvas2DSpatialResizeEdge edge,
        SceneObjectId borderSceneObjectId, RectD paintedBounds, double authoredExtent,
        Canvas2DSpatialDimensionConstraints constraints, IEnumerable<Canvas2DSpatialRegionId> affectedRegionIds,
        RectD guideSpan)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(acquiredRegionId);
        ArgumentNullException.ThrowIfNull(borderSceneObjectId);
        ArgumentNullException.ThrowIfNull(constraints);
        if (!Enum.IsDefined(edge)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (!double.IsFinite(authoredExtent) || authoredExtent <= 0d) throw new ArgumentOutOfRangeException(nameof(authoredExtent));
        if (paintedBounds.Width <= 0d || paintedBounds.Height <= 0d) throw new ArgumentException("A resize target requires a painted body.", nameof(paintedBounds));
        if (edge == Canvas2DSpatialResizeEdge.Bottom ? authorityRegionId != acquiredRegionId : authorityRegionId is not null)
            throw new ArgumentException("Bottom owns the acquired region height; Right owns the shared profile width.", nameof(authorityRegionId));
        ProfileId = profileId;
        AcquiredRegionId = acquiredRegionId;
        AuthorityRegionId = authorityRegionId;
        Edge = edge;
        BorderSceneObjectId = borderSceneObjectId;
        PaintedBounds = paintedBounds;
        AuthoredExtent = authoredExtent;
        Constraints = constraints;
        AffectedRegionIds = RoutingStateCollection.Unique(affectedRegionIds, static id => id.Value, nameof(affectedRegionIds));
        if (!AffectedRegionIds.Contains(acquiredRegionId)) throw new ArgumentException("The acquired region must be affected.", nameof(affectedRegionIds));
        GuideSpan = guideSpan;
    }

    public ModelProfileId ProfileId { get; }
    public Canvas2DSpatialRegionId AcquiredRegionId { get; }
    public Canvas2DSpatialRegionId? AuthorityRegionId { get; }
    public Canvas2DSpatialResizeEdge Edge { get; }
    public SceneObjectId BorderSceneObjectId { get; }
    public RectD PaintedBounds { get; }
    public double AuthoredExtent { get; }
    public Canvas2DSpatialDimensionConstraints Constraints { get; }
    public ImmutableArray<Canvas2DSpatialRegionId> AffectedRegionIds { get; }
    public RectD GuideSpan { get; }

    public bool Equals(Canvas2DSpatialResizeTarget? other) => ReferenceEquals(this, other) || other is not null &&
        ProfileId == other.ProfileId && AcquiredRegionId == other.AcquiredRegionId && AuthorityRegionId == other.AuthorityRegionId &&
        Edge == other.Edge && BorderSceneObjectId == other.BorderSceneObjectId && PaintedBounds == other.PaintedBounds &&
        AuthoredExtent == other.AuthoredExtent && Constraints.Equals(other.Constraints) && GuideSpan == other.GuideSpan &&
        AffectedRegionIds.AsSpan().SequenceEqual(other.AffectedRegionIds.AsSpan());
    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialResizeTarget);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProfileId); hash.Add(AcquiredRegionId); hash.Add(AuthorityRegionId); hash.Add(Edge);
        hash.Add(BorderSceneObjectId); hash.Add(PaintedBounds); hash.Add(AuthoredExtent); hash.Add(Constraints); hash.Add(GuideSpan);
        foreach (var region in AffectedRegionIds) hash.Add(region);
        return hash.ToHashCode();
    }
}
