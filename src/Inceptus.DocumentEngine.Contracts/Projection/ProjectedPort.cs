using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectedPort : IProjectedObject, IEquatable<ProjectedPort>
{
    private readonly ProjectedObjectPropertySet _propertySet;

    public ProjectedPort(
        ProjectionSourceTrace source,
        ProjectedObjectId ownerNodeId,
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? layoutHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? routingHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? algorithmMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ownerNodeId);

        Source = source;
        Id = ProjectedObjectIdentity.Create(source, ProjectedObjectKind.Port);
        OwnerNodeId = ownerNodeId;
        _propertySet = new ProjectedObjectPropertySet(
            semanticProperties,
            projectedProperties,
            layoutHints,
            routingHints,
            algorithmMetadata);
    }

    public ProjectedObjectId Id { get; }

    public ProjectedObjectKind Kind => ProjectedObjectKind.Port;

    public ProjectionSourceTrace Source { get; }

    public ProjectedObjectId OwnerNodeId { get; }

    public PropertyMap SemanticProperties => _propertySet.SemanticProperties;

    public PropertyMap ProjectedProperties => _propertySet.ProjectedProperties;

    public PropertyMap LayoutHints => _propertySet.LayoutHints;

    public PropertyMap RoutingHints => _propertySet.RoutingHints;

    public PropertyMap AlgorithmMetadata => _propertySet.AlgorithmMetadata;

    public bool Equals(ProjectedPort? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Source.Equals(other.Source) &&
        OwnerNodeId == other.OwnerNodeId &&
        _propertySet.Equals(other._propertySet);

    public override bool Equals(object? obj) => Equals(obj as ProjectedPort);

    public override int GetHashCode() => HashCode.Combine(Id, Source, OwnerNodeId, _propertySet);
}
