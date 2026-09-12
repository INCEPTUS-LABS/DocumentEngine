using Inceptus.DocumentEngine.Bpmn.Projection;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Layout;

/// <summary>
/// Provides the deterministic logical layout policy for the supported BPMN flow slice.
/// </summary>
public sealed class BpmnLayoutAlgorithm : ILayoutAlgorithm
{
    private const double HorizontalGap = 80d;
    private const double VerticalGap = 60d;
    private const double OriginX = 0d;
    private const double OriginY = 0d;

    private static readonly IComparer<ProjectedObjectId> ProjectedIdComparer =
        Comparer<ProjectedObjectId>.Create(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value));

    public LayoutAlgorithmResult Compute(
        ProjectedGraph graph,
        LayoutContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (graph.Groups.Length != 0)
        {
            return Unsupported(
                "The BPMN layout does not support projected groups.",
                graph.Groups[0].Id.Value);
        }

        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedNode(node))
            {
                return Unsupported(
                    $"Projected node '{node.Id}' is not a supported BPMN flow node.",
                    node.Id.Value);
            }
        }

        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSequenceFlow(edge))
            {
                return Unsupported(
                    $"Projected edge '{edge.Id}' is not a BPMN Sequence Flow.",
                    edge.Id.Value);
            }
        }

        var nodesById = graph.Nodes.ToDictionary(static node => node.Id);
        var components = BuildComponents(graph, cancellationToken)
            .OrderBy(component => ComponentPriority(component, nodesById))
            .ThenBy(static component => component[0].Value, StringComparer.Ordinal)
            .ToArray();
        var geometries = new List<LayoutNodeGeometry>(graph.Nodes.Length);
        var nextRowY = OriginY;

        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var orderedIds = TopologicalOrder(component, graph.Edges, cancellationToken);
            var sizes = orderedIds.ToDictionary(
                static id => id,
                id => ResolveSize(nodesById[id]));
            var depths = ResolveTopologicalDepths(
                orderedIds,
                graph.Edges,
                cancellationToken);
            var columns = orderedIds
                .GroupBy(id => depths[id])
                .OrderBy(static column => column.Key)
                .Select(static column => column.ToArray())
                .ToArray();
            var columnWidths = columns.ToDictionary(
                static column => column[0],
                column => column.Max(id => sizes[id].Width));
            var columnHeights = columns.ToDictionary(
                static column => column[0],
                column => ColumnHeight(column, sizes));
            var componentHeight = columnHeights.Values.Max();
            var nominalBounds = new Dictionary<ProjectedObjectId, RectD>();
            var cursorX = OriginX;
            foreach (var column in columns)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var columnWidth = columnWidths[column[0]];
                var cursorY = nextRowY +
                    ((componentHeight - columnHeights[column[0]]) / 2d);
                foreach (var nodeId in column)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var size = sizes[nodeId];
                    nominalBounds.Add(
                        nodeId,
                        new RectD(
                            cursorX + ((columnWidth - size.Width) / 2d),
                            cursorY,
                            size.Width,
                            size.Height));
                    cursorY += size.Height + VerticalGap;
                }

                cursorX += columnWidth + HorizontalGap;
            }

            var pinnedId = orderedIds.FirstOrDefault(id =>
                nodesById[id].PlacementHint?.PlacementMode == VisualPlacementMode.Pinned);
            var hasPinnedAnchor = pinnedId is not null;
            var xOffset = hasPinnedAnchor
                ? nodesById[pinnedId!].PlacementHint!.Position.X -
                    nominalBounds[pinnedId!].X
                : 0d;
            var yOffset = hasPinnedAnchor
                ? nodesById[pinnedId!].PlacementHint!.Position.Y -
                    nominalBounds[pinnedId!].Y
                : 0d;
            var automaticNominalBounds = orderedIds
                .Where(id => nodesById[id].PlacementHint?.PlacementMode !=
                    VisualPlacementMode.Pinned)
                .Select(id => nominalBounds[id])
                .ToArray();
            var automaticXOffset = automaticNominalBounds.Length == 0
                ? xOffset
                : Math.Max(
                    xOffset,
                    DocumentGeometryBoundary.MinimumX -
                        automaticNominalBounds.Min(static bounds => bounds.Left));
            var automaticYOffset = automaticNominalBounds.Length == 0
                ? yOffset
                : Math.Max(
                    yOffset,
                    DocumentGeometryBoundary.MinimumY -
                        automaticNominalBounds.Min(static bounds => bounds.Top));
            var componentBottom = double.MinValue;

            foreach (var nodeId in orderedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = nodesById[nodeId];
                var size = sizes[nodeId];
                RectD bounds;
                if (node.PlacementHint?.PlacementMode == VisualPlacementMode.Pinned)
                {
                    bounds = Bounds(node.PlacementHint);
                }
                else
                {
                    var nominal = nominalBounds[nodeId];
                    bounds = new RectD(
                        nominal.X + automaticXOffset,
                        nominal.Y + automaticYOffset,
                        size.Width,
                        size.Height);
                }

                componentBottom = Math.Max(componentBottom, bounds.Bottom);
                geometries.Add(new LayoutNodeGeometry(
                    nodeId,
                    bounds,
                    Matrix2D.CreateTranslation(bounds.X, bounds.Y)));
            }

            nextRowY = Math.Max(nextRowY, componentBottom + VerticalGap);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return LayoutAlgorithmResult.Success(new LayoutComputation(geometries));
    }

    private static List<ProjectedObjectId[]> BuildComponents(
        ProjectedGraph graph,
        CancellationToken cancellationToken)
    {
        var neighbors = graph.Nodes.ToDictionary(
            static node => node.Id,
            static _ => new SortedSet<ProjectedObjectId>(ProjectedIdComparer));
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            neighbors[edge.SourceNodeId].Add(edge.TargetNodeId);
            neighbors[edge.TargetNodeId].Add(edge.SourceNodeId);
        }

        var visited = new HashSet<ProjectedObjectId>();
        var components = new List<ProjectedObjectId[]>();
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(node.Id))
            {
                continue;
            }

            var pending = new SortedSet<ProjectedObjectId>(ProjectedIdComparer) { node.Id };
            var component = new List<ProjectedObjectId>();
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Min!;
                pending.Remove(current);
                component.Add(current);
                foreach (var neighbor in neighbors[current])
                {
                    if (visited.Add(neighbor))
                    {
                        pending.Add(neighbor);
                    }
                }
            }

            component.Sort(ProjectedIdComparer);
            components.Add([.. component]);
        }

        return components;
    }

    private static ProjectedObjectId[] TopologicalOrder(
        ProjectedObjectId[] component,
        IEnumerable<ProjectedEdge> graphEdges,
        CancellationToken cancellationToken)
    {
        var componentIds = component.ToHashSet();
        var indegrees = component.ToDictionary(static id => id, static _ => 0);
        var outgoing = component.ToDictionary(
            static id => id,
            static _ => new List<ProjectedEdge>());
        foreach (var edge in graphEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!componentIds.Contains(edge.SourceNodeId))
            {
                continue;
            }

            indegrees[edge.TargetNodeId]++;
            outgoing[edge.SourceNodeId].Add(edge);
        }

        foreach (var edges in outgoing.Values)
        {
            edges.Sort(static (left, right) =>
            {
                var targetComparison = StringComparer.Ordinal.Compare(
                    left.TargetNodeId.Value,
                    right.TargetNodeId.Value);
                return targetComparison != 0
                    ? targetComparison
                    : StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value);
            });
        }

        var ready = new SortedSet<ProjectedObjectId>(
            indegrees.Where(static pair => pair.Value == 0).Select(static pair => pair.Key),
            ProjectedIdComparer);
        var ordered = new List<ProjectedObjectId>(component.Length);
        while (ready.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ready.Min!;
            ready.Remove(current);
            ordered.Add(current);
            foreach (var edge in outgoing[current])
            {
                indegrees[edge.TargetNodeId]--;
                if (indegrees[edge.TargetNodeId] == 0)
                {
                    ready.Add(edge.TargetNodeId);
                }
            }
        }

        var orderedIds = ordered.ToHashSet();
        foreach (var remaining in component
                     .Where(id => !orderedIds.Contains(id))
                     .OrderBy(static id => id.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ordered.Add(remaining);
        }

        return [.. ordered];
    }

    private static Dictionary<ProjectedObjectId, int> ResolveTopologicalDepths(
        ProjectedObjectId[] orderedIds,
        IEnumerable<ProjectedEdge> graphEdges,
        CancellationToken cancellationToken)
    {
        var orderIndexes = orderedIds
            .Select(static (id, index) => new KeyValuePair<ProjectedObjectId, int>(id, index))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        var depths = orderedIds.ToDictionary(static id => id, static _ => 0);
        var outgoing = orderedIds.ToDictionary(
            static id => id,
            static _ => new List<ProjectedEdge>());
        foreach (var edge in graphEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outgoing.TryGetValue(edge.SourceNodeId, out var edges) &&
                orderIndexes.ContainsKey(edge.TargetNodeId))
            {
                edges.Add(edge);
            }
        }

        foreach (var edges in outgoing.Values)
        {
            edges.Sort(static (left, right) =>
            {
                var targetComparison = StringComparer.Ordinal.Compare(
                    left.TargetNodeId.Value,
                    right.TargetNodeId.Value);
                return targetComparison != 0
                    ? targetComparison
                    : StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value);
            });
        }

        foreach (var nodeId in orderedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var edge in outgoing[nodeId])
            {
                if (orderIndexes[edge.TargetNodeId] <= orderIndexes[nodeId])
                {
                    continue;
                }

                depths[edge.TargetNodeId] = Math.Max(
                    depths[edge.TargetNodeId],
                    depths[nodeId] + 1);
            }
        }

        return depths;
    }

    private static double ColumnHeight(
        ProjectedObjectId[] column,
        Dictionary<ProjectedObjectId, SizeD> sizes) =>
        column.Sum(id => sizes[id].Height) + ((column.Length - 1) * VerticalGap);

    private static int ComponentPriority(
        IEnumerable<ProjectedObjectId> component,
        Dictionary<ProjectedObjectId, ProjectedNode> nodesById) =>
        component.Min(id => TypePriority(nodesById[id].Source.SemanticTypeId));

    private static int TypePriority(SemanticTypeId typeId)
    {
        if (typeId == BpmnSemanticTypes.StartEvent)
        {
            return 0;
        }

        if (typeId == BpmnSemanticTypes.Task)
        {
            return 1;
        }

        return BpmnSemanticTypes.IsGateway(typeId) ? 2 : 3;
    }

    private static SizeD ResolveSize(ProjectedNode node)
    {
        if (node.PlacementHint?.Size is { } hintedSize)
        {
            return hintedSize;
        }

        return BpmnNodeLogicalSizePolicy.Resolve(node.Source.SemanticTypeId);
    }

    private static RectD Bounds(ProjectedPlacementHint hint) =>
        new(hint.Position.X, hint.Position.Y, hint.Size.Width, hint.Size.Height);

    private static bool IsSupportedNode(ProjectedNode node) =>
        TryReadProjectedType(node.ProjectedProperties, out var projectedType) &&
        string.Equals(
            projectedType,
            node.Source.SemanticTypeId.Value,
            StringComparison.Ordinal) &&
        BpmnSemanticTypes.IsFlowNode(node.Source.SemanticTypeId);

    private static bool IsSequenceFlow(ProjectedEdge edge) =>
        TryReadProjectedType(edge.ProjectedProperties, out var projectedType) &&
        string.Equals(
            projectedType,
            edge.Source.SemanticTypeId.Value,
            StringComparison.Ordinal) &&
        edge.Source.SemanticTypeId == BpmnSemanticTypes.SequenceFlow;

    private static bool TryReadProjectedType(PropertyMap properties, out string value)
    {
        if (properties.TryGetValue(
                BpmnProjectionIdentities.ProjectedSemanticTypeProperty,
                out var type) &&
            type.Kind == PropertyValueKind.Text)
        {
            value = type.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static LayoutAlgorithmResult Unsupported(string message, string sourceIdentity) =>
        LayoutAlgorithmResult.Failure(
        [
            new Diagnostic(
                BpmnAlgorithmDiagnosticCodes.UnsupportedLayoutContent,
                DiagnosticSeverity.Error,
                message,
                sourceIdentity),
        ]);
}
