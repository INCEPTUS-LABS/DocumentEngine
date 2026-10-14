using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Layout;

/// <summary>Logical spacing shared by explicit arrangement; independent of the viewport.</summary>
public static class BpmnAutoArrangeOptions
{
    public const double HorizontalGap = 80d;
    public const double VerticalGap = 60d;
    public const double ComponentGap = 80d;
    public const double Origin = 32d;
    internal const int OrderingSweeps = 8;
}

internal sealed record ArrangeNode(SemanticElementId Id, VisualStateId VisualId, RectD Bounds,
    string Region, int RegionOrder, int TypeOrder, bool GridEligible);
internal sealed record ArrangeEdge(SemanticElementId Id, SemanticElementId Source, SemanticElementId Target);

/// <summary>Pure Sequence Flow analysis. No model replacement, routing or hidden-scope traversal.</summary>
internal static class BpmnAutoArrangeGraph
{
    internal static List<Dictionary<int, List<SemanticElementId>>> Prepare(ArrangeNode[] nodes, ArrangeEdge[] edges,
        CancellationToken cancellationToken)
    {
        var byId = nodes.ToDictionary(node => node.Id);
        var outgoing = nodes.ToDictionary(node => node.Id, _ => new List<SemanticElementId>());
        var incoming = nodes.ToDictionary(node => node.Id, _ => new List<SemanticElementId>());
        foreach (var edge in edges.OrderBy(edge => edge.Id.Value, StringComparer.Ordinal))
        {
            outgoing[edge.Source].Add(edge.Target);
            incoming[edge.Target].Add(edge.Source);
        }
        foreach (var list in outgoing.Values.Concat(incoming.Values))
            list.Sort((a, b) => StringComparer.Ordinal.Compare(a.Value, b.Value));
        var components = new List<SemanticElementId[]>();
        var visited = new HashSet<SemanticElementId>();
        foreach (var node in nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal))
        {
            if (!visited.Add(node.Id)) continue;
            var pending = new Queue<SemanticElementId>();
            var component = new List<SemanticElementId>();
            pending.Enqueue(node.Id);
            while (pending.TryDequeue(out var id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                component.Add(id);
                foreach (var neighbor in outgoing[id].Concat(incoming[id]))
                    if (visited.Add(neighbor)) pending.Enqueue(neighbor);
            }
            components.Add([.. component]);
        }
        var result = new List<Dictionary<int, List<SemanticElementId>>>();
        foreach (var component in components.OrderBy(component => component.Min(id => byId[id].RegionOrder))
                     .ThenBy(component => component.Min(id => byId[id].Bounds.Top))
                     .ThenBy(component => component.Min(id => id.Value), StringComparer.Ordinal))
        {
            var ids = component.ToHashSet();
            var componentEdges = edges.Where(edge => ids.Contains(edge.Source)).ToArray();
            var ranks = Rank(component, outgoing, incoming, byId, cancellationToken);
            var lastRank = ranks.Values.Max();
            foreach (var id in component.Where(id => byId[id].TypeOrder == 2 && outgoing[id].Count == 0)) ranks[id] = lastRank;
            var layers = component.GroupBy(id => ranks[id]).OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.OrderBy(id => byId[id].RegionOrder)
                    .ThenBy(id => byId[id].Bounds.Top).ThenBy(id => id.Value, StringComparer.Ordinal).ToList());
            var authored = layers.Values.SelectMany(layer => layer.Select((id, index) => (id, index)))
                .ToDictionary(item => item.id, item => item.index);
            Order(layers, ranks, byId, componentEdges, outgoing, incoming, authored, cancellationToken);
            result.Add(layers);
        }
        return result;
    }

    internal static Dictionary<SemanticElementId, RectD> Place(ArrangeNode[] nodes,
        List<Dictionary<int, List<SemanticElementId>>> components, GridSettings? grid,
        IReadOnlyDictionary<string, Matrix2D> frames, CancellationToken cancellationToken)
    {
        var byId = nodes.ToDictionary(node => node.Id);
        var result = new Dictionary<SemanticElementId, RectD>();
        var cursors = nodes.Select(node => node.Region).Distinct().ToDictionary(region => region, _ => BpmnAutoArrangeOptions.Origin);
        foreach (var layers in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (grid is { SnapToGrid: true })
            {
                PlaceOnGrid(layers, byId, cursors, frames, grid, result);
                continue;
            }
            var ranks = layers.SelectMany(layer => layer.Value.Select(id => (id, layer.Key)))
                .ToDictionary(item => item.id, item => item.Key);
            var regionHeights = layers.Values.SelectMany(layer => layer).GroupBy(id => byId[id].Region).ToDictionary(group => group.Key,
                group => group.GroupBy(id => ranks[id]).Max(layer => layer.Sum(id => byId[id].Bounds.Height) +
                    (layer.Count() - 1) * BpmnAutoArrangeOptions.VerticalGap));
            var x = BpmnAutoArrangeOptions.Origin;
            foreach (var layer in layers.OrderBy(pair => pair.Key))
            {
                var width = layer.Value.Max(id => byId[id].Bounds.Width);
                foreach (var group in layer.Value.GroupBy(id => byId[id].Region))
                {
                    var height = group.Sum(id => byId[id].Bounds.Height) + (group.Count() - 1) * BpmnAutoArrangeOptions.VerticalGap;
                    var y = cursors[group.Key] + (regionHeights[group.Key] - height) / 2;
                    foreach (var id in group)
                    {
                        var size = byId[id].Bounds.Size;
                        result.Add(id, new RectD(x + (width - size.Width) / 2, y, size.Width, size.Height));
                        y += size.Height + BpmnAutoArrangeOptions.VerticalGap;
                    }
                }
                x += width + BpmnAutoArrangeOptions.HorizontalGap;
            }
            foreach (var (region, height) in regionHeights)
                cursors[region] += height + BpmnAutoArrangeOptions.ComponentGap;
        }
        return result;
    }

    private static void PlaceOnGrid(Dictionary<int, List<SemanticElementId>> layers,
        Dictionary<SemanticElementId, ArrangeNode> nodes, Dictionary<string, double> cursors,
        IReadOnlyDictionary<string, Matrix2D> frames, GridSettings grid, Dictionary<SemanticElementId, RectD> result)
    {
        // Plan track pitch and column centers before placing nodes. Every dependency consumes the
        // allocated size, including grid slack; no completed layout is independently rounded.
        var members = layers.Values.SelectMany(layer => layer).Select(id => nodes[id]).ToArray();
        var tracks = members.GroupBy(node => node.Region).ToDictionary(group => group.Key, group =>
        {
            var height = group.Max(node => node.Bounds.Height);
            var pitch = BpmnGridPlacement.AtOrAfter(height + BpmnAutoArrangeOptions.VerticalGap,
                height + BpmnAutoArrangeOptions.VerticalGap, grid);
            var count = layers.Values.Max(layer => layer.Count(id => nodes[id].Region == group.Key));
            var offset = frames.GetValueOrDefault(group.Key, Matrix2D.Identity).OffsetY;
            var first = BpmnGridPlacement.AtOrAfter(cursors[group.Key] + height / 2 + offset,
                cursors[group.Key] + height / 2 + offset, grid);
            return (Pitch: pitch, Count: count, First: first);
        });
        var left = BpmnAutoArrangeOptions.Origin + members.Max(node => frames.GetValueOrDefault(node.Region, Matrix2D.Identity).OffsetX);
        foreach (var layer in layers.OrderBy(pair => pair.Key))
        {
            var width = layer.Value.Max(id => nodes[id].Bounds.Width);
            var centerX = BpmnGridPlacement.AtOrAfter(left + width / 2, left + width / 2, grid);
            foreach (var group in layer.Value.GroupBy(id => nodes[id].Region))
            {
                var track = tracks[group.Key];
                var firstCenterY = GridGeometry.Snap(new(0, track.First + (track.Count - group.Count()) * track.Pitch / 2), grid).Y;
                var index = 0;
                foreach (var id in group)
                {
                    var node = nodes[id];
                    if (!node.GridEligible) throw new InvalidOperationException("This node does not support grid arrangement.");
                    var frame = frames.GetValueOrDefault(group.Key, Matrix2D.Identity);
                    if (!frame.TryInvert(out var inverse)) throw new InvalidOperationException("The arrangement frame is unavailable.");
                    var centerY = GridGeometry.Snap(new(0, firstCenterY + index++ * track.Pitch), grid).Y;
                    var center = inverse.TransformPoint(new(centerX, centerY));
                    var bounds = new RectD(center.X - node.Bounds.Width / 2, center.Y - node.Bounds.Height / 2,
                        node.Bounds.Width, node.Bounds.Height);
                    bounds = BpmnGridPlacement.RetainEquivalentCoordinates(bounds, node.Bounds, frame);
                    BpmnGridPlacement.Validate(bounds);
                    if (!DocumentGeometryBoundary.Contains(bounds)) throw new InvalidOperationException("The grid crosses the document boundary.");
                    result.Add(id, bounds);
                }
            }
            left = centerX + width / 2 + BpmnAutoArrangeOptions.HorizontalGap;
        }
        foreach (var region in tracks.Keys)
            cursors[region] = members.Where(node => node.Region == region).Max(node => result[node.Id].Bottom) + BpmnAutoArrangeOptions.ComponentGap;
    }

    private static Dictionary<SemanticElementId, int> Rank(SemanticElementId[] component,
        Dictionary<SemanticElementId, List<SemanticElementId>> outgoing,
        Dictionary<SemanticElementId, List<SemanticElementId>> incoming,
        Dictionary<SemanticElementId, ArrangeNode> nodes, CancellationToken cancellationToken)
    {
        // Iterative Kosaraju keeps deep user graphs off the CLR call stack.
        var visited = new HashSet<SemanticElementId>();
        var finish = new List<SemanticElementId>();
        foreach (var root in component.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            var stack = new Stack<(SemanticElementId Id, bool Exit)>();
            stack.Push((root, false));
            while (stack.TryPop(out var entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Exit) { finish.Add(entry.Id); continue; }
                if (!visited.Add(entry.Id)) continue;
                stack.Push((entry.Id, true));
                foreach (var next in outgoing[entry.Id].AsEnumerable().Reverse()) stack.Push((next, false));
            }
        }
        var owner = new Dictionary<SemanticElementId, int>();
        var sccs = new List<List<SemanticElementId>>();
        foreach (var root in finish.AsEnumerable().Reverse())
        {
            if (owner.ContainsKey(root)) continue;
            var index = sccs.Count;
            var members = new List<SemanticElementId>();
            var stack = new Stack<SemanticElementId>();
            stack.Push(root);
            while (stack.TryPop(out var id))
            {
                if (!owner.TryAdd(id, index)) continue;
                members.Add(id);
                foreach (var next in incoming[id]) stack.Push(next);
            }
            sccs.Add(members);
        }
        var result = new Dictionary<SemanticElementId, int>();
        var starts = new int[sccs.Count];
        // Kosaraju's second pass lists the condensation DAG in topological order.
        for (var index = 0; index < sccs.Count; index++)
        {
            var members = sccs[index];
            var seen = new HashSet<SemanticElementId>();
            var order = new List<SemanticElementId>();
            foreach (var root in members.OrderBy(id => incoming[id].Any(other => owner[other] != index) ? 0 : 1)
                         .ThenBy(id => nodes[id].TypeOrder).ThenBy(id => id.Value, StringComparer.Ordinal))
            {
                var stack = new Stack<SemanticElementId>();
                stack.Push(root);
                while (stack.TryPop(out var id))
                {
                    if (!seen.Add(id)) continue;
                    order.Add(id);
                    foreach (var next in outgoing[id].Where(next => owner[next] == index).AsEnumerable().Reverse()) stack.Push(next);
                }
            }
            for (var offset = 0; offset < order.Count; offset++) result[order[offset]] = starts[index] + offset;
            foreach (var id in members)
                foreach (var next in outgoing[id].Where(next => owner[next] != index))
                    starts[owner[next]] = Math.Max(starts[owner[next]], starts[index] + members.Count);
        }
        return result;
    }

    private static void Order(Dictionary<int, List<SemanticElementId>> layers, Dictionary<SemanticElementId, int> ranks,
        Dictionary<SemanticElementId, ArrangeNode> nodes, ArrangeEdge[] edges,
        Dictionary<SemanticElementId, List<SemanticElementId>> outgoing,
        Dictionary<SemanticElementId, List<SemanticElementId>> incoming,
        Dictionary<SemanticElementId, int> authored, CancellationToken cancellationToken)
    {
        Dictionary<SemanticElementId, double> Positions() => layers.Values.SelectMany(layer => layer
            .GroupBy(id => nodes[id].RegionOrder).SelectMany(group => group.Select((id, index) =>
                (id, position: group.Key * (double)(nodes.Count + 1) + index))))
            .ToDictionary(item => item.id, item => item.position);
        (int Crossings, int Deviation) Score()
        {
            var positions = Positions();
            var forward = edges.Where(edge => ranks[edge.Source] < ranks[edge.Target]).ToArray();
            var count = 0;
            for (var a = 0; a < forward.Length; a++)
                for (var b = a + 1; b < forward.Length; b++)
                {
                    var first = forward[a]; var second = forward[b];
                    var left = Math.Max(ranks[first.Source], ranks[second.Source]);
                    var right = Math.Min(ranks[first.Target], ranks[second.Target]);
                    if (left >= right) continue;
                    double At(ArrangeEdge edge, int rank) => positions[edge.Source] +
                        (positions[edge.Target] - positions[edge.Source]) * (rank - ranks[edge.Source]) /
                        (ranks[edge.Target] - ranks[edge.Source]);
                    if ((At(first, left) - At(second, left)) * (At(first, right) - At(second, right)) < 0) count++;
                }
            return (count, layers.Values.Sum(layer => layer.Select((id, index) => Math.Abs(authored[id] - index)).Sum()));
        }
        var best = layers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        var bestScore = Score();
        for (var sweep = 0; sweep < BpmnAutoArrangeOptions.OrderingSweeps; sweep++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forward = sweep % 2 == 0;
            foreach (var rank in (forward ? layers.Keys.Order() : layers.Keys.OrderDescending()).ToArray())
            {
                var positions = Positions();
                var neighbors = forward ? incoming : outgoing;
                double Center(SemanticElementId id)
                {
                    var relevant = neighbors[id].Where(other => forward ? ranks[other] < rank : ranks[other] > rank).ToArray();
                    return relevant.Length == 0 ? positions[id] : relevant.Average(other => positions[other]);
                }
                layers[rank] = [.. layers[rank].OrderBy(id => nodes[id].RegionOrder).ThenBy(Center)
                    .ThenBy(id => authored[id]).ThenBy(id => id.Value, StringComparer.Ordinal)];
            }
            var score = Score();
            if (score.CompareTo(bestScore) < 0)
            {
                bestScore = score;
                best = layers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
            }
        }
        foreach (var pair in best) layers[pair.Key] = pair.Value;
    }
}
