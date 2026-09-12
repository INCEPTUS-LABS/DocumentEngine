using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectedEdge : IProjectedObject, IEquatable<ProjectedEdge>
{
    private readonly ProjectedObjectPropertySet _propertySet;

    public ProjectedEdge(
        ProjectionSourceTrace source,
        ProjectedObjectId sourceNodeId,
        ProjectedObjectId targetNodeId,
        ProjectedObjectId? sourcePortId = null,
        ProjectedObjectId? targetPortId = null,
        IEnumerable<PointD>? persistentRoute = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? layoutHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? routingHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? algorithmMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceNodeId);
        ArgumentNullException.ThrowIfNull(targetNodeId);

        Source = source;
        Id = ProjectedObjectIdentity.Create(source, ProjectedObjectKind.Edge);
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
        SourcePortId = sourcePortId;
        TargetPortId = targetPortId;
        PersistentRoute = persistentRoute?.ToImmutableArray() ?? [];
        _propertySet = new ProjectedObjectPropertySet(
            semanticProperties,
            projectedProperties,
            layoutHints,
            routingHints,
            algorithmMetadata);
    }

    public ProjectedObjectId Id { get; }

    public ProjectedObjectKind Kind => ProjectedObjectKind.Edge;

    public ProjectionSourceTrace Source { get; }

    public ProjectedObjectId SourceNodeId { get; }

    public ProjectedObjectId TargetNodeId { get; }

    public ProjectedObjectId? SourcePortId { get; }

    public ProjectedObjectId? TargetPortId { get; }

    /// <summary>
    /// Gets explicitly persisted route information exposed as a read-only projection hint.
    /// </summary>
    public ImmutableArray<PointD> PersistentRoute { get; }

    public PropertyMap SemanticProperties => _propertySet.SemanticProperties;

    public PropertyMap ProjectedProperties => _propertySet.ProjectedProperties;

    public PropertyMap LayoutHints => _propertySet.LayoutHints;

    public PropertyMap RoutingHints => _propertySet.RoutingHints;

    public PropertyMap AlgorithmMetadata => _propertySet.AlgorithmMetadata;

    public bool Equals(ProjectedEdge? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Source.Equals(other.Source) &&
        SourceNodeId == other.SourceNodeId &&
        TargetNodeId == other.TargetNodeId &&
        SourcePortId == other.SourcePortId &&
        TargetPortId == other.TargetPortId &&
        PersistentRoute.AsSpan().SequenceEqual(other.PersistentRoute.AsSpan()) &&
        _propertySet.Equals(other._propertySet);

    public override bool Equals(object? obj) => Equals(obj as ProjectedEdge);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Source);
        hash.Add(SourceNodeId);
        hash.Add(TargetNodeId);
        hash.Add(SourcePortId);
        hash.Add(TargetPortId);

        foreach (var point in PersistentRoute)
        {
            hash.Add(point);
        }

        hash.Add(_propertySet);
        return hash.ToHashCode();
    }
}
