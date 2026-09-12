using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectedGroup : IProjectedObject, IEquatable<ProjectedGroup>
{
    private readonly ProjectedObjectPropertySet _propertySet;

    public ProjectedGroup(
        ProjectionSourceTrace source,
        IEnumerable<ProjectedObjectId>? memberNodeIds = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? layoutHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? routingHints = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? algorithmMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;
        Id = ProjectedObjectIdentity.Create(source, ProjectedObjectKind.Group);
        MemberNodeIds = CopyAndOrderMemberIds(memberNodeIds);
        _propertySet = new ProjectedObjectPropertySet(
            semanticProperties,
            projectedProperties,
            layoutHints,
            routingHints,
            algorithmMetadata);
    }

    public ProjectedObjectId Id { get; }

    public ProjectedObjectKind Kind => ProjectedObjectKind.Group;

    public ProjectionSourceTrace Source { get; }

    public ImmutableArray<ProjectedObjectId> MemberNodeIds { get; }

    public PropertyMap SemanticProperties => _propertySet.SemanticProperties;

    public PropertyMap ProjectedProperties => _propertySet.ProjectedProperties;

    public PropertyMap LayoutHints => _propertySet.LayoutHints;

    public PropertyMap RoutingHints => _propertySet.RoutingHints;

    public PropertyMap AlgorithmMetadata => _propertySet.AlgorithmMetadata;

    public bool Equals(ProjectedGroup? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        Source.Equals(other.Source) &&
        MemberNodeIds.AsSpan().SequenceEqual(other.MemberNodeIds.AsSpan()) &&
        _propertySet.Equals(other._propertySet);

    public override bool Equals(object? obj) => Equals(obj as ProjectedGroup);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Source);

        foreach (var memberId in MemberNodeIds)
        {
            hash.Add(memberId);
        }

        hash.Add(_propertySet);
        return hash.ToHashCode();
    }

    private static ImmutableArray<ProjectedObjectId> CopyAndOrderMemberIds(
        IEnumerable<ProjectedObjectId>? memberNodeIds)
    {
        if (memberNodeIds is null)
        {
            return [];
        }

        var copy = memberNodeIds.ToArray();

        if (Array.Exists(copy, static id => id is null))
        {
            throw new ArgumentException(
                "Projected group member identifiers cannot contain null values.",
                nameof(memberNodeIds));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));

        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1] == copy[index])
            {
                throw new ArgumentException(
                    $"Duplicate projected group member identifier '{copy[index]}'.",
                    nameof(memberNodeIds));
            }
        }

        return [.. copy];
    }
}
