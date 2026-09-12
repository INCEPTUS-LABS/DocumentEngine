using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public interface IProjectedObject
{
    ProjectedObjectId Id { get; }

    ProjectedObjectKind Kind { get; }

    ProjectionSourceTrace Source { get; }

    PropertyMap SemanticProperties { get; }

    PropertyMap ProjectedProperties { get; }

    PropertyMap LayoutHints { get; }

    PropertyMap RoutingHints { get; }

    PropertyMap AlgorithmMetadata { get; }
}

internal sealed class ProjectedObjectPropertySet : IEquatable<ProjectedObjectPropertySet>
{
    public ProjectedObjectPropertySet(
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties,
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties,
        IEnumerable<KeyValuePair<string, PropertyValue>>? layoutHints,
        IEnumerable<KeyValuePair<string, PropertyValue>>? routingHints,
        IEnumerable<KeyValuePair<string, PropertyValue>>? algorithmMetadata)
    {
        SemanticProperties = new PropertyMap(semanticProperties);
        ProjectedProperties = new PropertyMap(projectedProperties);
        LayoutHints = new PropertyMap(layoutHints);
        RoutingHints = new PropertyMap(routingHints);
        AlgorithmMetadata = new PropertyMap(algorithmMetadata);
    }

    public PropertyMap SemanticProperties { get; }

    public PropertyMap ProjectedProperties { get; }

    public PropertyMap LayoutHints { get; }

    public PropertyMap RoutingHints { get; }

    public PropertyMap AlgorithmMetadata { get; }

    public bool Equals(ProjectedObjectPropertySet? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        SemanticProperties.Equals(other.SemanticProperties) &&
        ProjectedProperties.Equals(other.ProjectedProperties) &&
        LayoutHints.Equals(other.LayoutHints) &&
        RoutingHints.Equals(other.RoutingHints) &&
        AlgorithmMetadata.Equals(other.AlgorithmMetadata);

    public override bool Equals(object? obj) => Equals(obj as ProjectedObjectPropertySet);

    public override int GetHashCode() => HashCode.Combine(
        SemanticProperties,
        ProjectedProperties,
        LayoutHints,
        RoutingHints,
        AlgorithmMetadata);
}
