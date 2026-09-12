using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Layout;

/// <summary>
/// Framework-owned orchestration for immutable Layout Algorithm execution.
/// </summary>
public sealed class LayoutEngine
{
    private readonly LayoutAlgorithmRegistry _registry;

    public LayoutEngine(IEnumerable<LayoutAlgorithmRegistration>? registrations = null) =>
        _registry = new LayoutAlgorithmRegistry(registrations ?? []);

    /// <summary>
    /// Computes one complete immutable layout for an immutable ProjectedGraph.
    /// </summary>
    public LayoutExecutionResult Layout(
        ProjectedGraph graph,
        AlgorithmId algorithmId,
        LayoutContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(algorithmId);
        context ??= LayoutContext.Empty;

        try
        {
            return ComputeCore(graph, algorithmId, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(graph, algorithmId);
        }
    }

    private LayoutExecutionResult ComputeCore(
        ProjectedGraph graph,
        AlgorithmId algorithmId,
        LayoutContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Options is null)
        {
            return Failure(
                graph,
                algorithmId,
                Error(
                    LayoutDiagnosticCodes.InvalidInput,
                    "The Layout context contains no immutable options map.",
                    algorithmId.Value));
        }

        if (!_registry.TryGet(algorithmId, out var registration))
        {
            return Failure(
                graph,
                algorithmId,
                Error(
                    LayoutDiagnosticCodes.MissingAlgorithm,
                    $"Layout Algorithm '{algorithmId}' is not registered.",
                    algorithmId.Value));
        }

        cancellationToken.ThrowIfCancellationRequested();

        LayoutAlgorithmResult? algorithmResult;
#pragma warning disable CA1031 // Plugin algorithm faults are isolated as deterministic Layout diagnostics.
        try
        {
            algorithmResult = registration!.Algorithm.Compute(graph, context, cancellationToken);
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
                algorithmId,
                Error(
                    LayoutDiagnosticCodes.AlgorithmFailure,
                    $"Layout Algorithm '{algorithmId}' failed.",
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
                algorithmId,
                Error(
                    LayoutDiagnosticCodes.InvalidAlgorithmResult,
                    $"Layout Algorithm '{algorithmId}' returned no computation result.",
                algorithmId.Value));
        }

        if (algorithmResult.Diagnostics.IsDefault ||
            algorithmResult.Diagnostics.Any(static diagnostic => diagnostic is null))
        {
            return Failure(
                graph,
                algorithmId,
                Error(
                    LayoutDiagnosticCodes.InvalidAlgorithmResult,
                    $"Layout Algorithm '{algorithmId}' returned an invalid diagnostic collection.",
                    algorithmId.Value));
        }

        var diagnostics = new List<Diagnostic>(algorithmResult.Diagnostics);
        if (!algorithmResult.Succeeded)
        {
            if (!HasErrors(diagnostics))
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.InvalidAlgorithmResult,
                    $"Layout Algorithm '{algorithmId}' reported failure without an error diagnostic.",
                    algorithmId.Value));
            }

            return LayoutExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                diagnostics);
        }

        if (algorithmResult.Computation is null)
        {
            diagnostics.Add(Error(
                LayoutDiagnosticCodes.InvalidAlgorithmResult,
                $"Layout Algorithm '{algorithmId}' returned no computation data.",
                algorithmId.Value));
            return LayoutExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                diagnostics);
        }

        if (HasErrors(diagnostics))
        {
            return LayoutExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                diagnostics);
        }

        if (algorithmResult.Computation.Nodes.IsDefault ||
            algorithmResult.Computation.Groups.IsDefault ||
            algorithmResult.Computation.Metadata is null)
        {
            diagnostics.Add(Error(
                LayoutDiagnosticCodes.InvalidAlgorithmResult,
                $"Layout Algorithm '{algorithmId}' returned uninitialized geometry collections.",
                algorithmId.Value));
            return LayoutExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                diagnostics);
        }

        var computation = ApplyBoundaryAttachments(
            graph,
            algorithmResult.Computation,
            diagnostics,
            cancellationToken);

        ValidateComputation(
            graph,
            computation,
            diagnostics,
            cancellationToken);

        if (HasErrors(diagnostics))
        {
            return LayoutExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();

        LayoutResult result;
        try
        {
            result = new LayoutResult(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                computation,
                diagnostics);
        }
        catch (ArgumentException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(Error(
                LayoutDiagnosticCodes.InvalidAlgorithmResult,
                $"Layout Algorithm '{algorithmId}' produced an invalid final result.",
                algorithmId.Value,
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name)));
            return LayoutExecutionResult.Failure(
                graph.DocumentId,
                graph.SourceRevision,
                algorithmId,
                diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return LayoutExecutionResult.Success(result);
    }

    private static void ValidateComputation(
        ProjectedGraph graph,
        LayoutComputation computation,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var projectedNodeIds = new HashSet<ProjectedObjectId>();
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectedNodeIds.Add(node.Id);
        }

        var projectedGroupIds = new HashSet<ProjectedObjectId>();
        foreach (var group in graph.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projectedGroupIds.Add(group.Id);
        }
        var layoutNodeIds = new HashSet<ProjectedObjectId>();
        var layoutGroupIds = new HashSet<ProjectedObjectId>();

        foreach (var geometry in computation.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (geometry is null || geometry.ProjectedObjectId is null)
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.InvalidGeometry,
                    "Layout node geometry and its projected identity must be present.",
                    "LayoutNodeGeometry"));
                continue;
            }

            if (!layoutNodeIds.Add(geometry.ProjectedObjectId))
            {
                diagnostics.Add(DuplicateGeometry(geometry.ProjectedObjectId));
            }

            if (!projectedNodeIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(UnexpectedGeometry(geometry.ProjectedObjectId, "Node"));
            }

            if (!IsValidNodeBounds(geometry.Bounds) || !IsValid(geometry.Transform))
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.InvalidGeometry,
                    $"Layout node geometry '{geometry.ProjectedObjectId}' contains invalid document-coordinate values.",
                    geometry.ProjectedObjectId.Value));
            }
        }

        foreach (var geometry in computation.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (geometry is null || geometry.ProjectedObjectId is null)
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.InvalidGeometry,
                    "Layout group geometry and its projected identity must be present.",
                    "LayoutGroupGeometry"));
                continue;
            }

            if (!layoutGroupIds.Add(geometry.ProjectedObjectId) ||
                layoutNodeIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(DuplicateGeometry(geometry.ProjectedObjectId));
            }

            if (!projectedGroupIds.Contains(geometry.ProjectedObjectId))
            {
                diagnostics.Add(UnexpectedGeometry(geometry.ProjectedObjectId, "Group"));
            }
            if (!IsValid(geometry.Bounds))
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.InvalidGeometry,
                    $"Layout group geometry '{geometry.ProjectedObjectId}' contains invalid document-coordinate values.",
                    geometry.ProjectedObjectId.Value));
            }
        }

        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!layoutNodeIds.Contains(node.Id))
            {
                diagnostics.Add(MissingGeometry(node.Id, "Node"));
                continue;
            }

            if (node.PlacementHint?.PlacementMode != VisualPlacementMode.Pinned ||
                node.PlacementHint.BoundaryAttachment is not null)
            {
                continue;
            }

            var geometry = computation.Nodes.First(candidate =>
                candidate is not null && candidate.ProjectedObjectId == node.Id);
            var expectedBounds = new RectD(
                node.PlacementHint.Position.X,
                node.PlacementHint.Position.Y,
                node.PlacementHint.Size.Width,
                node.PlacementHint.Size.Height);
            if (geometry.Bounds != expectedBounds)
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.PinnedPlacementViolation,
                    $"Layout geometry for pinned projected node '{node.Id}' changed its persistent placement hint.",
                    node.Id.Value));
            }
        }

        foreach (var group in graph.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!layoutGroupIds.Contains(group.Id))
            {
                diagnostics.Add(MissingGeometry(group.Id, "Group"));
            }
        }
    }

    private static LayoutComputation ApplyBoundaryAttachments(
        ProjectedGraph graph,
        LayoutComputation computation,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var attachedNodes = graph.Nodes
            .Where(static node => node.PlacementHint?.BoundaryAttachment is not null)
            .ToArray();
        if (attachedNodes.Length == 0)
        {
            return computation;
        }

        var nodesById = graph.Nodes.ToDictionary(static node => node.Id);
        var nodesBySemanticElementId = graph.Nodes
            .GroupBy(static node => node.Source.SemanticElementId)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());
        var geometryById = new Dictionary<ProjectedObjectId, LayoutNodeGeometry>();
        var canApplyAttachments = true;
        foreach (var geometry in computation.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (geometry is null ||
                geometry.ProjectedObjectId is null ||
                !geometryById.TryAdd(geometry.ProjectedObjectId, geometry))
            {
                // ValidateComputation owns the public diagnostics for malformed or
                // duplicate geometry. Avoid pre-empting that validation with an
                // exception while preparing the attachment post-pass.
                canApplyAttachments = false;
            }
        }

        if (!canApplyAttachments)
        {
            return computation;
        }
        var resolvedById = new Dictionary<ProjectedObjectId, LayoutNodeGeometry>();
        var resolving = new HashSet<ProjectedObjectId>();

        LayoutNodeGeometry? Resolve(ProjectedNode node)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolvedById.TryGetValue(node.Id, out var resolved))
            {
                return resolved;
            }

            if (!geometryById.TryGetValue(node.Id, out var original))
            {
                return null;
            }

            var attachment = node.PlacementHint?.BoundaryAttachment;
            if (attachment is null)
            {
                resolvedById[node.Id] = original;
                return original;
            }

            if (!resolving.Add(node.Id))
            {
                diagnostics.Add(Error(
                    LayoutDiagnosticCodes.InvalidGeometry,
                    $"Projected node '{node.Id}' participates in a boundary-attachment cycle.",
                    node.Id.Value));
                return null;
            }

            try
            {
                if (!nodesBySemanticElementId.TryGetValue(
                        attachment.AttachedToElementId,
                        out var ownerNodes) ||
                    ownerNodes.Length != 1)
                {
                    diagnostics.Add(Error(
                        LayoutDiagnosticCodes.InvalidGeometry,
                        $"Projected boundary-attached node '{node.Id}' requires exactly one projected owner for semantic element '{attachment.AttachedToElementId}'.",
                        node.Id.Value,
                        new KeyValuePair<string, string>(
                            "AttachedToElementId",
                            attachment.AttachedToElementId.Value)));
                    return null;
                }

                var ownerGeometry = Resolve(ownerNodes[0]);
                if (ownerGeometry is null)
                {
                    return null;
                }

                RectD bounds;
                try
                {
                    bounds = attachment.Placement.ResolveBounds(
                        ownerGeometry.Bounds,
                        node.PlacementHint!.Size);
                }
                catch (ArgumentOutOfRangeException)
                {
                    diagnostics.Add(Error(
                        LayoutDiagnosticCodes.InvalidGeometry,
                        $"Projected boundary-attached node '{node.Id}' produced invalid derived bounds.",
                        node.Id.Value));
                    return null;
                }

                if (original.Bounds == bounds)
                {
                    resolved = original;
                }
                else
                {
                    var transform = new Matrix2D(
                        original.Transform.M11,
                        original.Transform.M12,
                        original.Transform.M21,
                        original.Transform.M22,
                        bounds.X + (original.Transform.OffsetX - original.Bounds.X),
                        bounds.Y + (original.Transform.OffsetY - original.Bounds.Y));
                    resolved = new LayoutNodeGeometry(node.Id, bounds, transform);
                }

                resolvedById[node.Id] = resolved;
                return resolved;
            }
            finally
            {
                _ = resolving.Remove(node.Id);
            }
        }

        foreach (var node in attachedNodes)
        {
            _ = Resolve(node);
        }

        if (diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return computation;
        }

        return new LayoutComputation(
            computation.Nodes.Select(geometry =>
                nodesById.TryGetValue(geometry.ProjectedObjectId, out var node) &&
                Resolve(node) is { } resolved
                    ? resolved
                    : geometry),
            computation.Groups,
            computation.Metadata);
    }

    private static LayoutExecutionResult Failure(
        ProjectedGraph graph,
        AlgorithmId algorithmId,
        Diagnostic diagnostic) =>
        LayoutExecutionResult.Failure(
            graph.DocumentId,
            graph.SourceRevision,
            algorithmId,
            [diagnostic]);

    private static LayoutExecutionResult Cancelled(
        ProjectedGraph graph,
        AlgorithmId algorithmId) =>
        LayoutExecutionResult.Cancelled(
            graph.DocumentId,
            graph.SourceRevision,
            algorithmId,
            [
                new Diagnostic(
                    LayoutDiagnosticCodes.Cancelled,
                    DiagnosticSeverity.Information,
                    "Layout computation was cancelled.",
                    algorithmId.Value,
                    [
                        new KeyValuePair<string, string>(
                            "DocumentId",
                            graph.DocumentId.Value),
                        new KeyValuePair<string, string>(
                            "SourceRevision",
                            graph.SourceRevision.ToString()),
                    ]),
            ]);

    private static Diagnostic DuplicateGeometry(ProjectedObjectId projectedObjectId) =>
        Error(
            LayoutDiagnosticCodes.DuplicateGeometry,
            $"Layout geometry ID '{projectedObjectId}' occurs more than once.",
            projectedObjectId.Value);

    private static Diagnostic UnexpectedGeometry(
        ProjectedObjectId projectedObjectId,
        string category) =>
        Error(
            LayoutDiagnosticCodes.UnexpectedGeometry,
            $"Layout {category.ToLowerInvariant()} geometry '{projectedObjectId}' does not match a projected {category.ToLowerInvariant()}.",
            projectedObjectId.Value,
            new KeyValuePair<string, string>("GeometryCategory", category));

    private static Diagnostic MissingGeometry(
        ProjectedObjectId projectedObjectId,
        string category) =>
        Error(
            LayoutDiagnosticCodes.MissingGeometry,
            $"Projected {category.ToLowerInvariant()} '{projectedObjectId}' has no Layout geometry.",
            projectedObjectId.Value,
            new KeyValuePair<string, string>("GeometryCategory", category));

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static bool IsValid(RectD bounds) =>
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height) &&
        bounds.Width >= 0d &&
        bounds.Height >= 0d &&
        DocumentGeometryBoundary.Contains(bounds) &&
        double.IsFinite(bounds.X + bounds.Width) &&
        double.IsFinite(bounds.Y + bounds.Height);

    private static bool IsValidNodeBounds(RectD bounds) =>
        IsValid(bounds) && bounds.Width > 0d && bounds.Height > 0d;

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
