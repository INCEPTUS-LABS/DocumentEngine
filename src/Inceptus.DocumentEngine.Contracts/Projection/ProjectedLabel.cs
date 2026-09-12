using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectedLabel : IProjectedObject, IEquatable<ProjectedLabel>
{
    private readonly ProjectedObjectPropertySet _propertySet;

    public ProjectedLabel(
        ProjectionSourceTrace source,
        ProjectedObjectId ownerId,
        string text,
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? layoutHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? routingHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? algorithmMetadata = null,
        NodeLabelPlacement? nodePlacement = null,
        NodeLabelInteractionPolicy nodeInteractionPolicy =
            NodeLabelInteractionPolicy.Fixed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(nodeInteractionPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeInteractionPolicy),
                nodeInteractionPolicy,
                "The node-label interaction policy must be defined.");
        }

        Source = source;
        Id = ProjectedObjectIdentity.Create(source, ProjectedObjectKind.Label);
        OwnerId = ownerId;
        Text = text;
        NodePlacement = nodePlacement;
        NodeInteractionPolicy = nodeInteractionPolicy;
        _propertySet = new ProjectedObjectPropertySet(
            semanticProperties,
            projectedProperties,
            layoutHints,
            routingHints,
            algorithmMetadata);
    }

    public ProjectedObjectId Id { get; }

    public ProjectedObjectKind Kind => ProjectedObjectKind.Label;

    public ProjectionSourceTrace Source { get; }

    public ProjectedObjectId OwnerId { get; }

    public string Text { get; }

    /// <summary>
    /// Gets optional transient placement intent when this label is owned by a projected node.
    /// A null value resolves to <see cref="NodeLabelPlacement.InsideCentered"/> downstream.
    /// </summary>
    public NodeLabelPlacement? NodePlacement { get; }

    /// <summary>
    /// Gets transient interaction intent when this label is owned by a projected node.
    /// Persistent instance placement remains an owning Visual State concern.
    /// </summary>
    public NodeLabelInteractionPolicy NodeInteractionPolicy { get; }

    public PropertyMap SemanticProperties => _propertySet.SemanticProperties;

    public PropertyMap ProjectedProperties => _propertySet.ProjectedProperties;

    public PropertyMap LayoutHints => _propertySet.LayoutHints;

    public PropertyMap RoutingHints => _propertySet.RoutingHints;

    public PropertyMap AlgorithmMetadata => _propertySet.AlgorithmMetadata;

    public bool Equals(ProjectedLabel? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Source.Equals(other.Source) &&
        OwnerId == other.OwnerId &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        Equals(NodePlacement, other.NodePlacement) &&
        NodeInteractionPolicy == other.NodeInteractionPolicy &&
        _propertySet.Equals(other._propertySet);

    public override bool Equals(object? obj) => Equals(obj as ProjectedLabel);

    public override int GetHashCode() => HashCode.Combine(
        Id,
        Source,
        OwnerId,
        StringComparer.Ordinal.GetHashCode(Text),
        NodePlacement,
        NodeInteractionPolicy,
        _propertySet);
}
