using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private void ValidateCompatibility(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        List<Diagnostic> diagnostics)
    {
        if (_configuration.ConfigurationId is null ||
            _configuration.Version is null ||
            _configuration.Options is null)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidInput,
                "Canvas2D scene configuration is not initialized.",
                "Canvas2DSceneConfiguration"));
        }

        if (graph.DocumentId is null ||
            graph.Nodes.IsDefault ||
            graph.Edges.IsDefault ||
            graph.Groups.IsDefault ||
            graph.Ports.IsDefault ||
            graph.Labels.IsDefault)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.IncompatibleProjectedGraph,
                "ProjectedGraph is not a complete immutable projection input.",
                "ProjectedGraph"));
            return;
        }

        ValidateLayoutCompatibility(graph, layout, diagnostics);
        ValidateRoutingCompatibility(graph, layout, routing, diagnostics);
        ValidateVisualCompatibility(graph, visualModel, diagnostics);
        ValidateEditorState(editorState, diagnostics);
    }

    private static void ValidateLayoutCompatibility(
        ProjectedGraph graph,
        LayoutResult layout,
        List<Diagnostic> diagnostics)
    {
        if (layout.DocumentId is null ||
            layout.AlgorithmId is null ||
            layout.Computation is null ||
            layout.Nodes.IsDefault ||
            layout.Groups.IsDefault ||
            layout.Metadata is null ||
            layout.Diagnostics.IsDefault ||
            layout.DocumentId != graph.DocumentId ||
            layout.SourceRevision != graph.SourceRevision)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.IncompatibleLayoutResult,
                "LayoutResult does not match the ProjectedGraph document and revision.",
                "LayoutResult"));
            return;
        }

        var expectedNodeIds = CopyProjectedIds(graph.Nodes, diagnostics);
        var expectedGroupIds = CopyProjectedIds(graph.Groups, diagnostics);
        var actualNodeIds = new HashSet<ProjectedObjectId>();
        foreach (var geometry in layout.Nodes)
        {
            if (geometry is null || geometry.ProjectedObjectId is null ||
                !IsValid(geometry.Bounds) || !IsValid(geometry.Transform) ||
                !actualNodeIds.Add(geometry.ProjectedObjectId) ||
                !expectedNodeIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleLayoutResult,
                    "LayoutResult contains invalid or foreign node geometry.",
                    geometry?.ProjectedObjectId?.Value ?? "LayoutNodeGeometry"));
            }
        }

        var actualGroupIds = new HashSet<ProjectedObjectId>();
        foreach (var geometry in layout.Groups)
        {
            if (geometry is null || geometry.ProjectedObjectId is null ||
                !IsValid(geometry.Bounds) ||
                !actualGroupIds.Add(geometry.ProjectedObjectId) ||
                !expectedGroupIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleLayoutResult,
                    "LayoutResult contains invalid or foreign group geometry.",
                    geometry?.ProjectedObjectId?.Value ?? "LayoutGroupGeometry"));
            }
        }

        if (!expectedNodeIds.SetEquals(actualNodeIds) ||
            !expectedGroupIds.SetEquals(actualGroupIds))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.IncompatibleLayoutResult,
                "LayoutResult does not completely cover projected nodes and groups.",
                "LayoutResult"));
        }
    }

    private static void ValidateRoutingCompatibility(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        List<Diagnostic> diagnostics)
    {
        if (routing.DocumentId is null ||
            routing.LayoutAlgorithmId is null ||
            routing.RoutingAlgorithmId is null ||
            routing.Computation is null ||
            routing.Routes.IsDefault ||
            routing.NoRouteEdgeIds.IsDefault ||
            routing.Metadata is null ||
            routing.Diagnostics.IsDefault ||
            routing.DocumentId != graph.DocumentId ||
            routing.SourceRevision != graph.SourceRevision ||
            routing.LayoutAlgorithmId != layout.AlgorithmId)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult,
                "RoutingResult does not match the ProjectedGraph and LayoutResult provenance.",
                "RoutingResult"));
            return;
        }

        var expectedEdgeIds = CopyProjectedIds(graph.Edges, diagnostics);
        var edgesById = graph.Edges.ToDictionary(static edge => edge.Id);
        var portsById = graph.Ports.ToDictionary(static port => port.Id);
        var layoutNodesById = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        var actualEdgeIds = new HashSet<ProjectedObjectId>();
        foreach (var route in routing.Routes)
        {
            if (route is null || route.ProjectedEdgeId is null ||
                route.Path.IsDefault || route.BendPoints.IsDefault || route.Metadata is null ||
                route.Path.Length < 2 ||
                !actualEdgeIds.Add(route.ProjectedEdgeId) ||
                !expectedEdgeIds.Contains(route.ProjectedEdgeId) ||
                !IsValid(route.SourceAnchor) || !IsValid(route.DestinationAnchor) ||
                route.Path.Any(static point => !IsValid(point)) ||
                route.BendPoints.Any(static point => !IsValid(point)) ||
                route.Path.Length != route.BendPoints.Length + 2 ||
                route.Path[0] != route.SourceAnchor ||
                route.Path[^1] != route.DestinationAnchor ||
                !route.Path.AsSpan()[1..^1].SequenceEqual(route.BendPoints.AsSpan()))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult,
                    "RoutingResult contains invalid or foreign connector geometry.",
                    route?.ProjectedEdgeId?.Value ?? "RoutedConnectorGeometry"));
                continue;
            }

            var edge = edgesById[route.ProjectedEdgeId];
            if (route.SourcePortId != edge.SourcePortId ||
                route.TargetPortId != edge.TargetPortId ||
                !IsValidPort(route.SourcePortId, edge.SourceNodeId, portsById) ||
                !IsValidPort(route.TargetPortId, edge.TargetNodeId, portsById) ||
                !layoutNodesById.TryGetValue(edge.SourceNodeId, out var sourceNode) ||
                !layoutNodesById.TryGetValue(edge.TargetNodeId, out var targetNode) ||
                !sourceNode.Bounds.Contains(route.SourceAnchor) ||
                !targetNode.Bounds.Contains(route.DestinationAnchor))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult,
                    $"Routing geometry '{route.ProjectedEdgeId}' is not compatible with its projected endpoints and Layout geometry.",
                    route.ProjectedEdgeId.Value));
            }
        }

        var noRouteEdgeIds = new HashSet<ProjectedObjectId>();
        foreach (var noRouteEdgeId in routing.NoRouteEdgeIds)
        {
            if (noRouteEdgeId is null ||
                !noRouteEdgeIds.Add(noRouteEdgeId) ||
                !expectedEdgeIds.Contains(noRouteEdgeId) ||
                actualEdgeIds.Contains(noRouteEdgeId))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult,
                    "RoutingResult contains an invalid, foreign, duplicate, or conflicting no-route outcome.",
                    noRouteEdgeId?.Value ?? "RoutingComputation.NoRouteEdgeIds"));
            }
        }

        var completedEdgeIds = new HashSet<ProjectedObjectId>(actualEdgeIds);
        completedEdgeIds.UnionWith(noRouteEdgeIds);
        if (!expectedEdgeIds.SetEquals(completedEdgeIds))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult,
                "RoutingResult does not completely cover projected edges with route outcomes.",
                "RoutingResult"));
        }
    }

    private static void ValidateVisualCompatibility(
        ProjectedGraph graph,
        VisualModelSnapshot visualModel,
        List<Diagnostic> diagnostics)
    {
        if (visualModel.DocumentId is null ||
            visualModel.VisualStates.IsDefault ||
            visualModel.DocumentId != graph.DocumentId ||
            visualModel.Revision != graph.SourceRevision)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.IncompatibleVisualModel,
                "Visual Model does not match the ProjectedGraph document and revision.",
                "VisualModelSnapshot"));
            return;
        }

        var visualsById = new Dictionary<VisualStateId, VisualStateSnapshot>();
        foreach (var visual in visualModel.VisualStates)
        {
            if (visual is null || visual.Id is null || visual.SemanticElementId is null ||
                visual.Properties is null || visual.Route.IsDefault ||
                !IsValid(visual.Position) || !IsValid(visual.Size) ||
                visual.Route.Any(static point => !IsValid(point)) ||
                !visualsById.TryAdd(visual.Id, visual))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleVisualModel,
                    "Visual Model contains invalid or duplicate visual state.",
                    visual?.Id?.Value ?? "VisualStateSnapshot"));
            }
        }

        foreach (var projectedObject in EnumerateProjectedObjects(graph))
        {
            if (projectedObject is null || projectedObject.Id is null ||
                projectedObject.Source is null ||
                projectedObject.Source.DocumentId != graph.DocumentId ||
                projectedObject.Source.SemanticElementId is null)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleProjectedGraph,
                    "ProjectedGraph contains an invalid source trace.",
                    projectedObject?.Id?.Value ?? "ProjectedObject"));
                continue;
            }

            var visualId = projectedObject.Source.VisualStateId;
            if (visualId is null)
            {
                continue;
            }

            if (!visualsById.TryGetValue(visualId, out var visual) ||
                visual.SemanticElementId != projectedObject.Source.SemanticElementId)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleVisualModel,
                    $"Projected object '{projectedObject.Id}' references an incompatible Visual state '{visualId}'.",
                    projectedObject.Id.Value));
            }
        }
    }

    private static void ValidateEditorState(
        EditorStateSnapshot editorState,
        List<Diagnostic> diagnostics)
    {
        if (editorState.Selection.IsDefault ||
            editorState.TemporaryFeedback.IsDefault ||
            editorState.Viewport is null ||
            editorState.ToolState is null ||
            !double.IsFinite(editorState.Viewport.Zoom) ||
            editorState.Viewport.Zoom <= 0d ||
            !IsValid(editorState.Viewport.Pan))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidInput,
                "Editor State is not a complete immutable scene input.",
                "EditorStateSnapshot"));
        }
    }

    private static HashSet<ProjectedObjectId> CopyProjectedIds<T>(
        IEnumerable<T> projectedObjects,
        List<Diagnostic> diagnostics)
        where T : IProjectedObject
    {
        var ids = new HashSet<ProjectedObjectId>();
        foreach (var projectedObject in projectedObjects)
        {
            if (projectedObject is null || projectedObject.Id is null || !ids.Add(projectedObject.Id))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.IncompatibleProjectedGraph,
                    "ProjectedGraph contains a missing or duplicate projected identity.",
                    projectedObject?.Id?.Value ?? "ProjectedObject"));
            }
        }

        return ids;
    }

    private static IEnumerable<IProjectedObject?> EnumerateProjectedObjects(ProjectedGraph graph) =>
        graph.Nodes.Cast<IProjectedObject?>()
            .Concat(graph.Edges)
            .Concat(graph.Groups)
            .Concat(graph.Ports)
            .Concat(graph.Labels);

    private static bool IsValid(PointD point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool IsValid(VectorD vector) =>
        double.IsFinite(vector.X) && double.IsFinite(vector.Y);

    private static bool IsValid(SizeD size) =>
        double.IsFinite(size.Width) && double.IsFinite(size.Height) &&
        size.Width >= 0d && size.Height >= 0d;

    private static bool IsValid(RectD bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
        bounds.Width >= 0d && bounds.Height >= 0d &&
        double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);

    private static bool IsValid(Matrix2D transform) =>
        double.IsFinite(transform.M11) && double.IsFinite(transform.M12) &&
        double.IsFinite(transform.M21) && double.IsFinite(transform.M22) &&
        double.IsFinite(transform.OffsetX) && double.IsFinite(transform.OffsetY);

    private static bool IsValidPort(
        ProjectedObjectId? portId,
        ProjectedObjectId ownerNodeId,
        Dictionary<ProjectedObjectId, ProjectedPort> portsById) =>
        portId is null ||
        portsById.TryGetValue(portId, out var port) && port.OwnerNodeId == ownerNodeId;
}
