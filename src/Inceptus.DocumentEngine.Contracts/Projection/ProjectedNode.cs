using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectedNode : IProjectedObject, IEquatable<ProjectedNode>
{
    private readonly ProjectedObjectPropertySet _propertySet;

    public ProjectedNode(
        ProjectionSourceTrace source,
        ProjectedPlacementHint? placementHint = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? layoutHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? routingHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? algorithmMetadata = null,
        NodeGeometryInteractionPolicy geometryInteractionPolicy =
            NodeGeometryInteractionPolicy.FreeMoveAndResize)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(geometryInteractionPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometryInteractionPolicy),
                geometryInteractionPolicy,
                "The node geometry interaction policy must be defined.");
        }

        Source = source;
        Id = ProjectedObjectIdentity.Create(source, ProjectedObjectKind.Node);
        PlacementHint = placementHint;
        GeometryInteractionPolicy = geometryInteractionPolicy;
        _propertySet = new ProjectedObjectPropertySet(
            semanticProperties,
            projectedProperties,
            layoutHints,
            routingHints,
            algorithmMetadata);
    }

    public ProjectedObjectId Id { get; }

    public ProjectedObjectKind Kind => ProjectedObjectKind.Node;

    public ProjectionSourceTrace Source { get; }

    public ProjectedPlacementHint? PlacementHint { get; }

    public NodeGeometryInteractionPolicy GeometryInteractionPolicy { get; }

    public PropertyMap SemanticProperties => _propertySet.SemanticProperties;

    public PropertyMap ProjectedProperties => _propertySet.ProjectedProperties;

    public PropertyMap LayoutHints => _propertySet.LayoutHints;

    public PropertyMap RoutingHints => _propertySet.RoutingHints;

    public PropertyMap AlgorithmMetadata => _propertySet.AlgorithmMetadata;

    public bool Equals(ProjectedNode? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Source.Equals(other.Source) &&
        Equals(PlacementHint, other.PlacementHint) &&
        GeometryInteractionPolicy == other.GeometryInteractionPolicy &&
        _propertySet.Equals(other._propertySet);

    public override bool Equals(object? obj) => Equals(obj as ProjectedNode);

    public override int GetHashCode() =>
        HashCode.Combine(
            Id,
            Source,
            PlacementHint,
            GeometryInteractionPolicy,
            _propertySet);
}
