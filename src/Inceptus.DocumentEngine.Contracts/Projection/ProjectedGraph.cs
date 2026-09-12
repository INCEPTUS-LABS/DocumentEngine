using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Immutable visual-processing graph derived from one authoritative Document snapshot.
/// </summary>
public sealed class ProjectedGraph : IEquatable<ProjectedGraph>
{
    public ProjectedGraph(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        IEnumerable<ProjectedNode>? nodes = null,
        IEnumerable<ProjectedEdge>? edges = null,
        IEnumerable<ProjectedGroup>? groups = null,
        IEnumerable<ProjectedPort>? ports = null,
        IEnumerable<ProjectedLabel>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        Nodes = CopyAndOrder(nodes, nameof(nodes));
        Edges = CopyAndOrder(edges, nameof(edges));
        Groups = CopyAndOrder(groups, nameof(groups));
        Ports = CopyAndOrder(ports, nameof(ports));
        Labels = CopyAndOrder(labels, nameof(labels));
        ValidateGraph();
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public ImmutableArray<ProjectedNode> Nodes { get; }

    public ImmutableArray<ProjectedEdge> Edges { get; }

    public ImmutableArray<ProjectedGroup> Groups { get; }

    public ImmutableArray<ProjectedPort> Ports { get; }

    public ImmutableArray<ProjectedLabel> Labels { get; }

    public int NodeCount => Nodes.Length;

    public int EdgeCount => Edges.Length;

    public int GroupCount => Groups.Length;

    public int PortCount => Ports.Length;

    public int LabelCount => Labels.Length;

    public bool Equals(ProjectedGraph? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        Nodes.AsSpan().SequenceEqual(other.Nodes.AsSpan()) &&
        Edges.AsSpan().SequenceEqual(other.Edges.AsSpan()) &&
        Groups.AsSpan().SequenceEqual(other.Groups.AsSpan()) &&
        Ports.AsSpan().SequenceEqual(other.Ports.AsSpan()) &&
        Labels.AsSpan().SequenceEqual(other.Labels.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ProjectedGraph);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        AddHashCodes(ref hash, Nodes);
        AddHashCodes(ref hash, Edges);
        AddHashCodes(ref hash, Groups);
        AddHashCodes(ref hash, Ports);
        AddHashCodes(ref hash, Labels);
        return hash.ToHashCode();
    }

    private void ValidateGraph()
    {
        var objects = new Dictionary<ProjectedObjectId, ProjectedObjectKind>();
        AddObjects(objects, Nodes);
        AddObjects(objects, Edges);
        AddObjects(objects, Groups);
        AddObjects(objects, Ports);
        AddObjects(objects, Labels);

        foreach (var projectedObject in AllObjects())
        {
            if (projectedObject.Source.DocumentId != DocumentId)
            {
                throw new ArgumentException(
                    $"Projected object '{projectedObject.Id}' traces to a different Document.",
                    nameof(DocumentId));
            }
        }

        var nodeIds = Nodes.Select(static node => node.Id).ToHashSet();
        var portsById = Ports.ToDictionary(static port => port.Id);
        foreach (var port in Ports)
        {
            if (!nodeIds.Contains(port.OwnerNodeId))
            {
                throw InvalidReference(
                    port.Id,
                    port.OwnerNodeId,
                    "port owner node");
            }
        }

        foreach (var group in Groups)
        {
            foreach (var memberNodeId in group.MemberNodeIds)
            {
                if (!nodeIds.Contains(memberNodeId))
                {
                    throw InvalidReference(group.Id, memberNodeId, "group member node");
                }
            }
        }

        foreach (var edge in Edges)
        {
            if (!nodeIds.Contains(edge.SourceNodeId))
            {
                throw InvalidReference(edge.Id, edge.SourceNodeId, "edge source node");
            }

            if (!nodeIds.Contains(edge.TargetNodeId))
            {
                throw InvalidReference(edge.Id, edge.TargetNodeId, "edge target node");
            }

            ValidateEdgePort(edge, edge.SourcePortId, edge.SourceNodeId, portsById, "source");
            ValidateEdgePort(edge, edge.TargetPortId, edge.TargetNodeId, portsById, "target");
        }

        var labelOwnerIds = Nodes.Select(static node => node.Id)
            .Concat(Edges.Select(static edge => edge.Id))
            .Concat(Groups.Select(static group => group.Id))
            .Concat(Ports.Select(static port => port.Id))
            .ToHashSet();
        foreach (var label in Labels)
        {
            if (!labelOwnerIds.Contains(label.OwnerId))
            {
                throw InvalidReference(label.Id, label.OwnerId, "label owner");
            }

            if (label.NodePlacement is not null && !nodeIds.Contains(label.OwnerId))
            {
                throw new ArgumentException(
                    $"Projected label '{label.Id}' declares node-label placement but its " +
                    $"owner '{label.OwnerId}' is not a projected node.",
                    nameof(Labels));
            }

            if (label.NodeInteractionPolicy != NodeLabelInteractionPolicy.Fixed &&
                !nodeIds.Contains(label.OwnerId))
            {
                throw new ArgumentException(
                    $"Projected label '{label.Id}' declares node-label interaction but its " +
                    $"owner '{label.OwnerId}' is not a projected node.",
                    nameof(Labels));
            }
        }
    }

    private IEnumerable<IProjectedObject> AllObjects() =>
        Nodes.Cast<IProjectedObject>()
            .Concat(Edges)
            .Concat(Groups)
            .Concat(Ports)
            .Concat(Labels);

    private static void AddObjects<T>(
        Dictionary<ProjectedObjectId, ProjectedObjectKind> objects,
        ImmutableArray<T> projectedObjects)
        where T : IProjectedObject
    {
        foreach (var projectedObject in projectedObjects)
        {
            if (!objects.TryAdd(projectedObject.Id, projectedObject.Kind))
            {
                throw new ArgumentException(
                    $"Projected object ID '{projectedObject.Id}' occurs more than once.",
                    nameof(projectedObjects));
            }
        }
    }

    private static void ValidateEdgePort(
        ProjectedEdge edge,
        ProjectedObjectId? portId,
        ProjectedObjectId expectedOwnerNodeId,
        Dictionary<ProjectedObjectId, ProjectedPort> portsById,
        string endpointName)
    {
        if (portId is null)
        {
            return;
        }

        if (!portsById.TryGetValue(portId, out var port))
        {
            throw InvalidReference(edge.Id, portId, $"edge {endpointName} port");
        }

        if (port.OwnerNodeId != expectedOwnerNodeId)
        {
            throw new ArgumentException(
                $"Projected edge '{edge.Id}' uses {endpointName} port '{portId}' from another node.",
                nameof(edge));
        }
    }

    private static ArgumentException InvalidReference(
        ProjectedObjectId ownerId,
        ProjectedObjectId referenceId,
        string role) =>
        new(
            $"Projected object '{ownerId}' references missing {role} '{referenceId}'.",
            nameof(referenceId));

    private static ImmutableArray<T> CopyAndOrder<T>(
        IEnumerable<T>? values,
        string parameterName)
        where T : class, IProjectedObject
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException(
                "ProjectedGraph collections cannot contain null values.",
                parameterName);
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].Id == copy[index].Id)
            {
                throw new ArgumentException(
                    $"Duplicate projected object ID '{copy[index].Id}'.",
                    parameterName);
            }
        }

        return [.. copy];
    }

    private static void AddHashCodes<T>(ref HashCode hash, ImmutableArray<T> values)
    {
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
