using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal sealed partial class EditingSessionPipeline
{
    private static LayoutComputation? TryAppendPinnedNodeGeometry(
        EditingSessionPipelineArtifacts previousArtifacts,
        ProjectedGraph currentGraph,
        NodeGeometryPipelineImpact impact)
    {
        // The caller already validates Document, scope, revision, algorithm and artifact
        // consistency. This path proves an insertion, not arbitrary topology compatibility.
        // Historical replay must use its exact retained revision, never synthesized geometry.
        var previousGraph = previousArtifacts.ProjectedGraph;
        if (!impact.HasExplicitChanges || impact.HistoricalSourceRevision is not null ||
            impact.HasRemovedVisualStates ||
            !GeometryMatchesGraph(previousArtifacts.LayoutResult, previousGraph) ||
            currentGraph.NodeCount != previousGraph.NodeCount + impact.ChangedVisualStateIds.Length ||
            !previousGraph.Edges.AsSpan().SequenceEqual(currentGraph.Edges.AsSpan()) ||
            !previousGraph.Groups.AsSpan().SequenceEqual(currentGraph.Groups.AsSpan()))
        {
            return null;
        }

        var requestedIds = impact.ChangedVisualStateIds.ToHashSet();
        if (previousGraph.Nodes.Any(node =>
                node.Source.VisualStateId is { } id && requestedIds.Contains(id)))
        {
            return null;
        }

        var insertedNodes = currentGraph.Nodes.Where(node =>
            node.Source.VisualStateId is { } id && requestedIds.Contains(id)).ToArray();
        if (insertedNodes.Length != requestedIds.Count ||
            insertedNodes.Select(static node => node.Source.VisualStateId).Distinct().Count() != requestedIds.Count)
        {
            return null;
        }

        var insertedIds = insertedNodes.Select(static node => node.Id).ToHashSet();
        if (!currentGraph.Nodes.Where(node => !insertedIds.Contains(node.Id)).SequenceEqual(previousGraph.Nodes) ||
            !currentGraph.Ports.Where(port => !insertedIds.Contains(port.OwnerNodeId)).SequenceEqual(previousGraph.Ports) ||
            !currentGraph.Labels.Where(label => !insertedIds.Contains(label.OwnerId)).SequenceEqual(previousGraph.Labels))
        {
            return null;
        }

        var insertedGeometry = new List<LayoutNodeGeometry>(insertedNodes.Length);
        foreach (var node in insertedNodes)
        {
            // Layout-dependent/attached placement is deliberately not inferred here.
            // Unknown requests retain the existing conservative full-layout fallback.
            if (node.PlacementHint is not { PlacementMode: VisualPlacementMode.Pinned, BoundaryAttachment: null } placement ||
                node.LayoutHints.Count != 0 || node.AlgorithmMetadata.Count != 0)
            {
                return null;
            }

            RectD bounds;
            try
            {
                bounds = new RectD(placement.Position.X, placement.Position.Y,
                    placement.Size.Width, placement.Size.Height);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }

            if (bounds.IsEmpty || !DocumentGeometryBoundary.Contains(bounds))
            {
                return null;
            }

            insertedGeometry.Add(new LayoutNodeGeometry(node.Id, bounds,
                Matrix2D.CreateTranslation(bounds.X, bounds.Y)));
        }

        // Reuse the exact existing geometry objects, including their transforms. Only
        // newly declared pinned nodes acquire geometry; no sibling is pinned or rewritten.
        return new LayoutComputation(
            previousArtifacts.LayoutResult.Nodes.Concat(insertedGeometry),
            previousArtifacts.LayoutResult.Groups,
            previousArtifacts.LayoutResult.Metadata);
    }
}
