using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.Routing;

/// <summary>
/// Framework-owned orchestration for immutable Routing Algorithm execution.
/// </summary>
public sealed class RoutingEngine
{
    private readonly RoutingAlgorithmRegistry _registry;

    public RoutingEngine(IEnumerable<RoutingAlgorithmRegistration>? registrations = null) =>
        _registry = new RoutingAlgorithmRegistry(registrations ?? []);

    /// <summary>
    /// Computes one complete immutable routing solution for a compatible ProjectedGraph and
    /// LayoutResult.
    /// </summary>
    public RoutingExecutionResult Route(
        ProjectedGraph graph,
        LayoutResult layout,
        AlgorithmId algorithmId,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(algorithmId);
        context ??= RoutingContext.Empty;

        try
        {
            return ComputeCore(graph, layout, algorithmId, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(graph, layout, algorithmId);
        }
    }

    private RoutingExecutionResult ComputeCore(
        ProjectedGraph graph,
        LayoutResult layout,
        AlgorithmId algorithmId,
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Options is null)
        {
            return Failure(
                graph,
                layout,
                algorithmId,
                Error(
                    RoutingDiagnosticCodes.InvalidInput,
                    "The Routing context contains no immutable options map.",
                    algorithmId.Value));
        }

        var diagnostics = new List<Diagnostic>();
        ValidateCompatibility(graph, layout, diagnostics, cancellationToken);
        if (HasErrors(diagnostics))
        {
            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        if (!_registry.TryGet(algorithmId, out var registration))
        {
            return Failure(
                graph,
                layout,
                algorithmId,
                Error(
                    RoutingDiagnosticCodes.MissingAlgorithm,
                    $"Routing Algorithm '{algorithmId}' is not registered.",
                    algorithmId.Value));
        }

        cancellationToken.ThrowIfCancellationRequested();

        RoutingAlgorithmResult? algorithmResult;
#pragma warning disable CA1031 // Plugin algorithm faults are isolated as deterministic Routing diagnostics.
        try
        {
            algorithmResult = registration!.Algorithm.Route(
                graph,
                layout,
                context,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(
                graph,
                layout,
                algorithmId,
                Error(
                    RoutingDiagnosticCodes.AlgorithmFailure,
                    $"Routing Algorithm '{algorithmId}' failed.",
                    algorithmId.Value,
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)));
        }
#pragma warning restore CA1031

        cancellationToken.ThrowIfCancellationRequested();

        if (algorithmResult is null)
        {
            return Failure(
                graph,
                layout,
                algorithmId,
                Error(
                    RoutingDiagnosticCodes.InvalidAlgorithmResult,
                    $"Routing Algorithm '{algorithmId}' returned no computation result.",
                    algorithmId.Value));
        }

        if (algorithmResult.Diagnostics.IsDefault ||
            algorithmResult.Diagnostics.Any(static diagnostic => diagnostic is null))
        {
            return Failure(
                graph,
                layout,
                algorithmId,
                Error(
                    RoutingDiagnosticCodes.InvalidAlgorithmResult,
                    $"Routing Algorithm '{algorithmId}' returned an invalid diagnostic collection.",
                    algorithmId.Value));
        }

        diagnostics.AddRange(algorithmResult.Diagnostics);
        if (!algorithmResult.Succeeded)
        {
            if (!HasErrors(diagnostics))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.InvalidAlgorithmResult,
                    $"Routing Algorithm '{algorithmId}' reported failure without an error diagnostic.",
                    algorithmId.Value));
            }

            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        if (algorithmResult.Computation is null)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidAlgorithmResult,
                $"Routing Algorithm '{algorithmId}' returned no computation data.",
                algorithmId.Value));
            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        if (HasErrors(diagnostics))
        {
            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        if (algorithmResult.Computation.Routes.IsDefault ||
            algorithmResult.Computation.NoRouteEdgeIds.IsDefault ||
            algorithmResult.Computation.Metadata is null)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidAlgorithmResult,
                $"Routing Algorithm '{algorithmId}' returned uninitialized routing data.",
                algorithmId.Value));
            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        ValidateComputation(
            graph,
            layout,
            algorithmResult.Computation,
            diagnostics,
            cancellationToken);

        if (HasErrors(diagnostics))
        {
            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        RoutingResult result;
        try
        {
            result = new RoutingResult(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                algorithmResult.Computation,
                diagnostics);
        }
        catch (ArgumentException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidAlgorithmResult,
                $"Routing Algorithm '{algorithmId}' produced an invalid final result.",
                algorithmId.Value,
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name)));
            return RoutingExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                layout.AlgorithmId,
                algorithmId,
                diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return RoutingExecutionResult.Success(result);
    }

    private static void ValidateCompatibility(
        ProjectedGraph graph,
        LayoutResult layout,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (graph.DocumentId is null ||
            graph.Nodes.IsDefault ||
            graph.Edges.IsDefault ||
            graph.Groups.IsDefault ||
            graph.Ports.IsDefault ||
            graph.Labels.IsDefault)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidInput,
                "The ProjectedGraph contains uninitialized routing input data.",
                "ProjectedGraph"));
            return;
        }

        if (layout.DocumentId is null ||
            layout.AlgorithmId is null ||
            layout.Computation is null ||
            layout.Nodes.IsDefault ||
            layout.Groups.IsDefault ||
            layout.Metadata is null ||
            layout.Diagnostics.IsDefault)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.IncompatibleLayoutResult,
                "The LayoutResult contains uninitialized compatibility data.",
                "LayoutResult"));
            return;
        }

        if (layout.DocumentId != graph.DocumentId)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.IncompatibleLayoutResult,
                "The LayoutResult belongs to a different Document than the ProjectedGraph.",
                layout.DocumentId.Value,
                new KeyValuePair<string, string>("ExpectedDocumentId", graph.DocumentId.Value)));
        }

        if (layout.SourceRevision != graph.SourceRevision)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.IncompatibleLayoutResult,
                "The LayoutResult belongs to a different source revision than the ProjectedGraph.",
                layout.SourceRevision.ToString(),
                new KeyValuePair<string, string>(
                    "ExpectedSourceRevision",
                    graph.SourceRevision.ToString())));
        }

        var projectedNodeIds = new HashSet<ProjectedObjectId>();
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is null || node.Id is null || !projectedNodeIds.Add(node.Id))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.InvalidInput,
                    "The ProjectedGraph contains invalid or duplicate projected node identities.",
                    "ProjectedGraph.Nodes"));
            }
        }

        var projectedGroupIds = new HashSet<ProjectedObjectId>();
        foreach (var group in graph.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group is null || group.Id is null || !projectedGroupIds.Add(group.Id))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.InvalidInput,
                    "The ProjectedGraph contains invalid or duplicate projected group identities.",
                    "ProjectedGraph.Groups"));
            }
        }

        var layoutNodeIds = new HashSet<ProjectedObjectId>();
        foreach (var geometry in layout.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (geometry is null || geometry.ProjectedObjectId is null)
            {
                diagnostics.Add(IncompatibleGeometry("Layout node geometry has no projected identity."));
                continue;
            }

            if (!layoutNodeIds.Add(geometry.ProjectedObjectId))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Layout node geometry ID '{geometry.ProjectedObjectId}' occurs more than once.",
                    geometry.ProjectedObjectId.Value));
            }

            if (!projectedNodeIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Layout node geometry '{geometry.ProjectedObjectId}' does not match a projected node.",
                    geometry.ProjectedObjectId.Value));
            }

            if (!IsValid(geometry.Bounds) || !IsValid(geometry.Transform))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Layout node geometry '{geometry.ProjectedObjectId}' is not valid document-coordinate geometry.",
                    geometry.ProjectedObjectId.Value));
            }
        }

        var layoutGroupIds = new HashSet<ProjectedObjectId>();
        foreach (var geometry in layout.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (geometry is null || geometry.ProjectedObjectId is null)
            {
                diagnostics.Add(IncompatibleGeometry("Layout group geometry has no projected identity."));
                continue;
            }

            if (!layoutGroupIds.Add(geometry.ProjectedObjectId) ||
                layoutNodeIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Layout geometry ID '{geometry.ProjectedObjectId}' occurs more than once.",
                    geometry.ProjectedObjectId.Value));
            }

            if (!projectedGroupIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Layout group geometry '{geometry.ProjectedObjectId}' does not match a projected group.",
                    geometry.ProjectedObjectId.Value));
            }

            if (!IsValid(geometry.Bounds))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Layout group geometry '{geometry.ProjectedObjectId}' is not valid document-coordinate geometry.",
                    geometry.ProjectedObjectId.Value));
            }
        }

        foreach (var projectedNodeId in projectedNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!layoutNodeIds.Contains(projectedNodeId))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Projected node '{projectedNodeId}' has no compatible Layout geometry.",
                    projectedNodeId.Value));
            }
        }

        foreach (var projectedGroupId in projectedGroupIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!layoutGroupIds.Contains(projectedGroupId))
            {
                diagnostics.Add(IncompatibleGeometry(
                    $"Projected group '{projectedGroupId}' has no compatible Layout geometry.",
                    projectedGroupId.Value));
            }
        }
    }

    private static void ValidateComputation(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingComputation computation,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var layoutNodesById = new Dictionary<ProjectedObjectId, LayoutNodeGeometry>();
        foreach (var geometry in layout.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (geometry is not null && geometry.ProjectedObjectId is not null)
            {
                layoutNodesById.TryAdd(geometry.ProjectedObjectId, geometry);
            }
        }

        var edgesById = new Dictionary<ProjectedObjectId, ProjectedEdge>();
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge is not null && edge.Id is not null)
            {
                edgesById.TryAdd(edge.Id, edge);
            }
        }

        var portsById = new Dictionary<ProjectedObjectId, ProjectedPort>();
        foreach (var port in graph.Ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port is not null && port.Id is not null)
            {
                portsById.TryAdd(port.Id, port);
            }
        }

        var routeIds = new HashSet<ProjectedObjectId>();
        foreach (var route in computation.Routes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (route is null || route.ProjectedEdgeId is null)
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.InvalidProjectedIdentity,
                    "A routed connector and its projected edge identity must be present.",
                    "RoutedConnectorGeometry"));
                continue;
            }

            if (!routeIds.Add(route.ProjectedEdgeId))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.DuplicateRoute,
                    $"Routing geometry ID '{route.ProjectedEdgeId}' occurs more than once.",
                    route.ProjectedEdgeId.Value));
            }

            if (!edgesById.TryGetValue(route.ProjectedEdgeId, out var edge))
            {
                if (TryGetProjectedKind(
                        graph,
                        route.ProjectedEdgeId,
                        cancellationToken,
                        out var actualKind))
                {
                    diagnostics.Add(Error(
                        RoutingDiagnosticCodes.InvalidProjectedIdentity,
                        $"Routing geometry '{route.ProjectedEdgeId}' references a projected {actualKind} instead of an edge.",
                        route.ProjectedEdgeId.Value,
                        new KeyValuePair<string, string>("ExpectedKind", ProjectedObjectKind.Edge.ToString()),
                        new KeyValuePair<string, string>("ActualKind", actualKind.ToString())));
                }
                else
                {
                    diagnostics.Add(Error(
                        RoutingDiagnosticCodes.UnexpectedRoute,
                        $"Routing geometry '{route.ProjectedEdgeId}' does not match a projected edge.",
                        route.ProjectedEdgeId.Value));
                }

                ValidatePath(route, diagnostics, cancellationToken);
                continue;
            }

            ValidatePath(route, diagnostics, cancellationToken);
            ValidateAttachment(
                route.ProjectedEdgeId,
                "source",
                route.SourceAnchor,
                edge.SourceNodeId,
                layoutNodesById,
                diagnostics);
            ValidateAttachment(
                route.ProjectedEdgeId,
                "destination",
                route.DestinationAnchor,
                edge.TargetNodeId,
                layoutNodesById,
                diagnostics);
            ValidatePort(
                route.ProjectedEdgeId,
                "source",
                route.SourcePortId,
                edge.SourcePortId,
                edge.SourceNodeId,
                portsById,
                diagnostics);
            ValidatePort(
                route.ProjectedEdgeId,
                "target",
                route.TargetPortId,
                edge.TargetPortId,
                edge.TargetNodeId,
                portsById,
                diagnostics);
        }

        var noRouteIds = new HashSet<ProjectedObjectId>();
        foreach (var noRouteEdgeId in computation.NoRouteEdgeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (noRouteEdgeId is null)
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.InvalidProjectedIdentity,
                    "A no-route outcome must identify a projected edge.",
                    "RoutingComputation.NoRouteEdgeIds"));
                continue;
            }

            if (!noRouteIds.Add(noRouteEdgeId))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.DuplicateRoute,
                    $"No-route outcome ID '{noRouteEdgeId}' occurs more than once.",
                    noRouteEdgeId.Value));
            }

            if (routeIds.Contains(noRouteEdgeId))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.ConflictingRouteOutcome,
                    $"Projected edge '{noRouteEdgeId}' has both Routing geometry and a no-route outcome.",
                    noRouteEdgeId.Value));
            }

            if (edgesById.ContainsKey(noRouteEdgeId))
            {
                continue;
            }

            if (TryGetProjectedKind(
                    graph,
                    noRouteEdgeId,
                    cancellationToken,
                    out var actualKind))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.InvalidProjectedIdentity,
                    $"No-route outcome '{noRouteEdgeId}' references a projected {actualKind} instead of an edge.",
                    noRouteEdgeId.Value,
                    new KeyValuePair<string, string>(
                        "ExpectedKind",
                        ProjectedObjectKind.Edge.ToString()),
                    new KeyValuePair<string, string>(
                        "ActualKind",
                        actualKind.ToString())));
            }
            else
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.UnexpectedRoute,
                    $"No-route outcome '{noRouteEdgeId}' does not match a projected edge.",
                    noRouteEdgeId.Value));
            }
        }

        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge is not null && edge.Id is not null &&
                !routeIds.Contains(edge.Id) &&
                !noRouteIds.Contains(edge.Id))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.MissingRoute,
                    $"Projected edge '{edge.Id}' has neither Routing geometry nor a no-route outcome.",
                    edge.Id.Value));
            }
        }
    }

    private static void ValidateAttachment(
        ProjectedObjectId routeId,
        string endpointName,
        PointD anchor,
        ProjectedObjectId nodeId,
        Dictionary<ProjectedObjectId, LayoutNodeGeometry> layoutNodesById,
        List<Diagnostic> diagnostics)
    {
        if (!IsValid(anchor) ||
            !layoutNodesById.TryGetValue(nodeId, out var geometry) ||
            !geometry.Bounds.Contains(anchor))
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidAttachmentPoint,
                $"Routing geometry '{routeId}' has a {endpointName} anchor that is not attached to projected node '{nodeId}'.",
                routeId.Value,
                new KeyValuePair<string, string>("Endpoint", endpointName),
                new KeyValuePair<string, string>("ProjectedNodeId", nodeId.Value)));
        }
    }

    private static void ValidatePath(
        RoutedConnectorGeometry route,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (route.BendPoints.IsDefault ||
            route.Path.IsDefault ||
            route.Metadata is null)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidPath,
                $"Routing geometry '{route.ProjectedEdgeId}' contains uninitialized path data.",
                route.ProjectedEdgeId.Value));
            return;
        }

        if (!IsValid(route.SourceAnchor) || !IsValid(route.DestinationAnchor))
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.NonFiniteGeometry,
                $"Routing geometry '{route.ProjectedEdgeId}' contains a non-finite anchor.",
                route.ProjectedEdgeId.Value));
        }

        foreach (var bendPoint in route.BendPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValid(bendPoint))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.NonFiniteGeometry,
                    $"Routing geometry '{route.ProjectedEdgeId}' contains a non-finite bend point.",
                    route.ProjectedEdgeId.Value));
            }
        }

        foreach (var point in route.Path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValid(point))
            {
                diagnostics.Add(Error(
                    RoutingDiagnosticCodes.NonFiniteGeometry,
                    $"Routing geometry '{route.ProjectedEdgeId}' contains a non-finite path point.",
                    route.ProjectedEdgeId.Value));
            }
        }

        if (route.Path.Any(static point => !DocumentGeometryBoundary.Contains(point)))
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.RoutingConstraintViolation,
                $"Routing geometry '{route.ProjectedEdgeId}' crosses the Document boundary.",
                route.ProjectedEdgeId.Value));
        }

        var expectedPathLength = route.BendPoints.Length + 2;
        if (route.Path.Length != expectedPathLength ||
            route.Path.Length < 2 ||
            route.Path[0] != route.SourceAnchor ||
            route.Path[^1] != route.DestinationAnchor ||
            !PathContainsBendPoints(route.Path, route.BendPoints, cancellationToken))
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidPath,
                $"Routing geometry '{route.ProjectedEdgeId}' does not define one complete anchor-to-anchor path.",
                route.ProjectedEdgeId.Value));
        }
    }

    private static bool PathContainsBendPoints(
        System.Collections.Immutable.ImmutableArray<PointD> path,
        System.Collections.Immutable.ImmutableArray<PointD> bendPoints,
        CancellationToken cancellationToken)
    {
        if (path.Length != bendPoints.Length + 2)
        {
            return false;
        }

        for (var index = 0; index < bendPoints.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path[index + 1] != bendPoints[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidatePort(
        ProjectedObjectId routeId,
        string endpointName,
        ProjectedObjectId? actualPortId,
        ProjectedObjectId? expectedPortId,
        ProjectedObjectId expectedOwnerNodeId,
        Dictionary<ProjectedObjectId, ProjectedPort> portsById,
        List<Diagnostic> diagnostics)
    {
        if (actualPortId != expectedPortId)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidPortReference,
                $"Routing geometry '{routeId}' does not preserve the projected edge's {endpointName} port.",
                routeId.Value,
                new KeyValuePair<string, string>(
                    "ExpectedPortId",
                    expectedPortId?.Value ?? string.Empty),
                new KeyValuePair<string, string>(
                    "ActualPortId",
                    actualPortId?.Value ?? string.Empty)));
            return;
        }

        if (actualPortId is null)
        {
            return;
        }

        if (!portsById.TryGetValue(actualPortId, out var port) ||
            port.OwnerNodeId != expectedOwnerNodeId)
        {
            diagnostics.Add(Error(
                RoutingDiagnosticCodes.InvalidPortReference,
                $"Routing geometry '{routeId}' references an invalid {endpointName} port '{actualPortId}'.",
                routeId.Value,
                new KeyValuePair<string, string>("PortId", actualPortId.Value),
                new KeyValuePair<string, string>("Endpoint", endpointName)));
        }
    }

    private static bool TryGetProjectedKind(
        ProjectedGraph graph,
        ProjectedObjectId projectedObjectId,
        CancellationToken cancellationToken,
        out ProjectedObjectKind kind)
    {
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is not null && node.Id == projectedObjectId)
            {
                kind = ProjectedObjectKind.Node;
                return true;
            }
        }

        foreach (var group in graph.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group is not null && group.Id == projectedObjectId)
            {
                kind = ProjectedObjectKind.Group;
                return true;
            }
        }

        foreach (var port in graph.Ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port is not null && port.Id == projectedObjectId)
            {
                kind = ProjectedObjectKind.Port;
                return true;
            }
        }

        foreach (var label in graph.Labels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (label is not null && label.Id == projectedObjectId)
            {
                kind = ProjectedObjectKind.Label;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static RoutingExecutionResult Failure(
        ProjectedGraph graph,
        LayoutResult layout,
        AlgorithmId algorithmId,
        Diagnostic diagnostic) =>
        RoutingExecutionResult.Failure(
            graph.DocumentId,
            graph.SourceRevision,
            layout.AlgorithmId,
            algorithmId,
            [diagnostic]);

    private static RoutingExecutionResult Cancelled(
        ProjectedGraph graph,
        LayoutResult layout,
        AlgorithmId algorithmId) =>
        RoutingExecutionResult.Cancelled(
            graph.DocumentId,
            graph.SourceRevision,
            layout.AlgorithmId,
            algorithmId,
            [
                new Diagnostic(
                    RoutingDiagnosticCodes.Cancelled,
                    DiagnosticSeverity.Information,
                    "Routing computation was cancelled.",
                    algorithmId.Value,
                    [
                        new KeyValuePair<string, string>(
                            "DocumentId",
                            graph.DocumentId.Value),
                        new KeyValuePair<string, string>(
                            "SourceRevision",
                            graph.SourceRevision.ToString()),
                        new KeyValuePair<string, string>(
                            "LayoutAlgorithmId",
                            layout.AlgorithmId.Value),
                    ]),
            ]);

    private static Diagnostic IncompatibleGeometry(
        string message,
        string sourceIdentity = "LayoutResult") =>
        Error(
            RoutingDiagnosticCodes.IncompatibleLayoutResult,
            message,
            sourceIdentity);

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static bool IsValid(PointD point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool IsValid(RectD bounds) =>
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height) &&
        bounds.Width >= 0d &&
        bounds.Height >= 0d &&
        double.IsFinite(bounds.X + bounds.Width) &&
        double.IsFinite(bounds.Y + bounds.Height);

    private static bool IsValid(Matrix2D transform) =>
        double.IsFinite(transform.M11) &&
        double.IsFinite(transform.M12) &&
        double.IsFinite(transform.M21) &&
        double.IsFinite(transform.M22) &&
        double.IsFinite(transform.OffsetX) &&
        double.IsFinite(transform.OffsetY);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}
