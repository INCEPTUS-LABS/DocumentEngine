using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Measured local bodies/captions and authored capacity inputs for expanded geometry.</summary>
public sealed class Canvas2DScopeGeometryPresentationContext
{
    public Canvas2DScopeGeometryPresentationContext(
        Canvas2DScopeGeometryBaseContext baseContext,
        IEnumerable<Canvas2DScopeGeometryNodeBounds> nodes,
        IEnumerable<KeyValuePair<SceneObjectId, TextMetrics>> measurements,
        IEnumerable<SpatialRegionGeometrySnapshot> existingRegions,
        IEnumerable<KeyValuePair<Canvas2DSpatialRegionId, double>>? requestedRegionHeights = null)
        : this(baseContext, nodes, measurements, existingRegions, requestedRegionHeights, [], [])
    {
    }

    public Canvas2DScopeGeometryPresentationContext(
        Canvas2DScopeGeometryBaseContext baseContext,
        IEnumerable<Canvas2DScopeGeometryNodeBounds> nodes,
        IEnumerable<KeyValuePair<SceneObjectId, TextMetrics>> measurements,
        IEnumerable<SpatialRegionGeometrySnapshot> existingRegions,
        IEnumerable<KeyValuePair<Canvas2DSpatialRegionId, double>>? requestedRegionHeights,
        IEnumerable<SpatialScopeWidthSnapshot> existingWidths,
        IEnumerable<KeyValuePair<ModelProfileId, double>> requestedWidths)
    {
        ArgumentNullException.ThrowIfNull(baseContext);
        ArgumentNullException.ThrowIfNull(measurements);
        BaseContext = baseContext;
        Nodes = RoutingStateCollection.Unique(nodes, static node => node.VisualStateId.Value, nameof(nodes));
        ExistingRegions = RoutingStateCollection.Unique(existingRegions, static region => region.Id.Value, nameof(existingRegions));
        ExistingWidths = RoutingStateCollection.Unique(existingWidths, static width => width.ProfileId.Value, nameof(existingWidths));
        var widths = ImmutableDictionary.CreateBuilder<ModelProfileId, double>();
        foreach (var entry in requestedWidths)
        {
            ArgumentNullException.ThrowIfNull(entry.Key);
            if (!double.IsFinite(entry.Value) || entry.Value <= 0d)
                throw new ArgumentOutOfRangeException(nameof(requestedWidths));
            widths.Add(entry.Key, entry.Value);
        }
        RequestedWidths = widths.ToImmutable();
        var measured = ImmutableDictionary.CreateBuilder<SceneObjectId, TextMetrics>();
        foreach (var entry in measurements)
        {
            ArgumentNullException.ThrowIfNull(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            measured.Add(entry.Key, entry.Value);
        }
        Measurements = measured.ToImmutable();
        var heights = ImmutableDictionary.CreateBuilder<Canvas2DSpatialRegionId, double>();
        foreach (var entry in requestedRegionHeights ?? [])
        {
            ArgumentNullException.ThrowIfNull(entry.Key);
            if (!double.IsFinite(entry.Value) || entry.Value <= 0d)
                throw new ArgumentOutOfRangeException(nameof(requestedRegionHeights));
            heights.Add(entry.Key, entry.Value);
        }
        RequestedRegionHeights = heights.ToImmutable();
    }

    public Canvas2DScopeGeometryBaseContext BaseContext { get; }
    public ImmutableArray<Canvas2DScopeGeometryNodeBounds> Nodes { get; }
    public ImmutableDictionary<SceneObjectId, TextMetrics> Measurements { get; }
    public ImmutableArray<SpatialRegionGeometrySnapshot> ExistingRegions { get; }
    public ImmutableArray<SpatialScopeWidthSnapshot> ExistingWidths { get; }
    public ImmutableDictionary<ModelProfileId, double> RequestedWidths { get; }
    public ImmutableDictionary<Canvas2DSpatialRegionId, double> RequestedRegionHeights { get; }
}
