using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable document-coordinate path calculated for one projected edge.
/// </summary>
public sealed class RoutedConnectorGeometry : IEquatable<RoutedConnectorGeometry>
{
    public RoutedConnectorGeometry(
        ProjectedObjectId projectedEdgeId,
        PointD sourceAnchor,
        PointD destinationAnchor,
        IEnumerable<PointD>? bendPoints = null,
        ProjectedObjectId? sourcePortId = null,
        ProjectedObjectId? targetPortId = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(projectedEdgeId);

        ProjectedEdgeId = projectedEdgeId;
        SourceAnchor = sourceAnchor;
        DestinationAnchor = destinationAnchor;
        BendPoints = bendPoints?.ToImmutableArray() ?? [];
        SourcePortId = sourcePortId;
        TargetPortId = targetPortId;
        Metadata = new PropertyMap(metadata);

        var path = ImmutableArray.CreateBuilder<PointD>(BendPoints.Length + 2);
        path.Add(SourceAnchor);
        path.AddRange(BendPoints);
        path.Add(DestinationAnchor);
        Path = path.MoveToImmutable();
    }

    public ProjectedObjectId ProjectedEdgeId { get; }

    public PointD SourceAnchor { get; }

    public PointD DestinationAnchor { get; }

    public ImmutableArray<PointD> BendPoints { get; }

    public ProjectedObjectId? SourcePortId { get; }

    public ProjectedObjectId? TargetPortId { get; }

    public PropertyMap Metadata { get; }

    /// <summary>
    /// Gets the complete ordered path from source anchor through bend points to destination anchor.
    /// </summary>
    public ImmutableArray<PointD> Path { get; }

    public bool Equals(RoutedConnectorGeometry? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ProjectedEdgeId == other.ProjectedEdgeId &&
        SourceAnchor == other.SourceAnchor &&
        DestinationAnchor == other.DestinationAnchor &&
        BendPoints.AsSpan().SequenceEqual(other.BendPoints.AsSpan()) &&
        SourcePortId == other.SourcePortId &&
        TargetPortId == other.TargetPortId &&
        Metadata.Equals(other.Metadata);

    public override bool Equals(object? obj) => Equals(obj as RoutedConnectorGeometry);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectedEdgeId);
        hash.Add(SourceAnchor);
        hash.Add(DestinationAnchor);
        foreach (var bendPoint in BendPoints)
        {
            hash.Add(bendPoint);
        }

        hash.Add(SourcePortId);
        hash.Add(TargetPortId);
        hash.Add(Metadata);
        return hash.ToHashCode();
    }
}
