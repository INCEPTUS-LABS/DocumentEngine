using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

/// <summary>Prepares the complete immutable saved basis before the existing single installation.</summary>
public sealed partial class ConnectorRoutingStatePreparer : IConnectorRoutingStatePreparer
{
    private readonly EditingSessionConfiguration _configuration;
    private readonly ITextMetricsService _textMetrics;
    private readonly Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> _requestFactory;

    public ConnectorRoutingStatePreparer(EditingSessionConfiguration configuration, Canvas2DRenderer renderer)
        : this(configuration, renderer, (renderer ?? throw new ArgumentNullException(nameof(renderer))).CreateTextMeasurementRequest)
    {
    }

    public ConnectorRoutingStatePreparer(EditingSessionConfiguration configuration,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(textMetrics);
        ArgumentNullException.ThrowIfNull(requestFactory);
        _configuration = configuration;
        _textMetrics = textMetrics;
        _requestFactory = requestFactory;
    }

    public async ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(
        ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_configuration.RoutingEngine.TryGetStablePolicy(_configuration.RoutingAlgorithmId, out var policy))
            return Fail("INCEPTUS.ROUTING.PROVIDER.UNSUPPORTED", "The registered routing algorithm has no saved-path preparation capability.");
        var document = request.ProposedDocument;
        var strict = request.Purpose == ConnectorRoutingPreparationPurpose.ValidateSavedState;
        if (strict && document.VisualModel.RoutingScopes is null)
            return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE", "Saved routing state is missing.");
        if (document.VisualModel.VisualStates.Any(static visual => !visual.Route.IsEmpty))
            return Fail("INCEPTUS.ROUTING.LEGACY_GUIDANCE.UNSUPPORTED", "Untyped connector guidance cannot coexist with saved typed paths.");
        try
        {
            if (TryPreserveConnectorLabelRouting(request) is { } connectorLabelOnly)
                return ConnectorRoutingStatePreparationResult.Success(connectorLabelOnly);
            if (await TryPrepareLabelUpdateAsync(request, cancellationToken).ConfigureAwait(false) is { } labelOnly)
                return ConnectorRoutingStatePreparationResult.Success(labelOnly);
            var output = new List<ScopeRoutingSnapshot>();
            var diagnostics = new List<Diagnostic>();
            var beforeScopes = request.Before.VisualModel.RoutingScopes ?? [];
            var proposedScopes = document.VisualModel.RoutingScopes ?? [];
            foreach (var scope in document.SemanticModel.EnumerateScopeForest())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projection = _configuration.ProjectionEngine.Project(document, scope.Id,
                    _configuration.ProjectionContext, cancellationToken);
                if (projection.Graph is null)
                    return ConnectorRoutingStatePreparationResult.Failure(projection.Diagnostics);
                diagnostics.AddRange(projection.Diagnostics);
                var graph = projection.Graph;
                var current = beforeScopes.FirstOrDefault(item => item.ScopeId == scope.Id);
                var proposed = proposedScopes.FirstOrDefault(item => item.ScopeId == scope.Id);
                var basis = MergeGeometrySeeds(current?.Geometry, proposed?.Geometry, request);
                var layout = PrepareLocalLayout(graph, document, basis, request, strict, cancellationToken);
                var heights = request.SpatialHeightIntents.Where(intent => intent.ScopeId == scope.Id)
                    .GroupBy(static intent => intent.RegionId)
                    .ToDictionary(static group => group.Key, static group => group.Last().ExpandedHeight);
                var widths = request.SpatialWidthIntents.Where(intent => intent.ScopeId == scope.Id)
                    .GroupBy(static intent => intent.ProfileId)
                    .ToDictionary(static group => group.Key, static group => group.Last().OuterWidth);
                var geometry = await _configuration.SceneBuilder.PrepareScopeGeometryAsync(
                    CreateInputs(document, scope.Id), graph, layout, document.VisualModel, basis, heights, widths,
                    _textMetrics, _requestFactory, cancellationToken).ConfigureAwait(false);
                if (strict && (proposed is null || !geometry.Equals(proposed.Geometry)))
                    return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE",
                        $"Saved geometry, contributor policy or text metrics for scope '{scope.Id}' are incompatible.");
                if (strict) geometry = proposed!.Geometry;
                var logicalLayout = CreateLogicalLayout(graph, layout, geometry);
                var domain = new RoutingObstacleDomain(graph.Nodes.Select(static node => node.Id));
                var context = new RoutingContext(_configuration.RoutingContext.Options,
                    new PreparedRoutingInput(graph, logicalLayout,
                        graph.Edges.Select(edge => KeyValuePair.Create(edge.Id, domain)),
                        new RoutingLogicalGeometry(scope.Id, geometry, logicalLayout)));
                var records = PrepareRecords(request, scope.Id, graph, logicalLayout, context,
                    current, proposed, beforeScopes, policy, diagnostics, strict, cancellationToken);
                if (strict && !records.AsSpan().SequenceEqual(proposed!.Connectors.AsSpan()))
                    return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE",
                        $"Saved connector paths or policy for scope '{scope.Id}' are incompatible.");
                output.Add(new ScopeRoutingSnapshot(scope.Id, geometry, strict ? proposed!.Connectors : records));
            }
            if (strict && output.Count != proposedScopes.Length)
                return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE", "Saved scope coverage does not match the document.");
            return ConnectorRoutingStatePreparationResult.Success(output, diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ScopeGeometryPreparationException exception)
        {
            return ConnectorRoutingStatePreparationResult.Failure(exception.Diagnostics);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Fail(strict ? "INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE" : "INCEPTUS.ROUTING.PREPARATION.INVALID",
                exception.Message);
        }
    }

    private static ConnectorRoutingStatePreparationResult Fail(string code, string message) =>
        ConnectorRoutingStatePreparationResult.Failure([new Diagnostic(code, DiagnosticSeverity.Error, message)]);

    private static ScopeGeometryInputs CreateInputs(DocumentSnapshot document, DocumentScopeId scopeId)
    {
        var elements = document.SemanticModel.Elements.Where(element =>
            document.SemanticModel.TryGetScope(element.Id, out var scope) && scope!.Id == scopeId).ToArray();
        var ids = elements.Select(static element => element.Id).ToHashSet();
        var relationships = document.SemanticModel.Relationships.Where(relationship => ids.Contains(relationship.SourceId) && ids.Contains(relationship.TargetId)).ToArray();
        var sourceIds = ids.Concat(relationships.Select(static relationship => relationship.Id)).ToHashSet();
        return new ScopeGeometryInputs(scopeId, elements, relationships,
            document.SemanticModel.ScopeMemberships.Where(membership => ids.Contains(membership.SemanticElementId)),
            document.VisualModel.VisualStates.Where(visual => sourceIds.Contains(visual.SemanticElementId)),
            document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments.Where(assignment => ids.Contains(assignment.SemanticElementId)),
            document.VisualModel.ProfileElementPresentations.Where(presentation => ids.Contains(presentation.SemanticElementId)));
    }

    private static ScopeGeometrySnapshot? MergeGeometrySeeds(ScopeGeometrySnapshot? current,
        ScopeGeometrySnapshot? proposed, ConnectorRoutingStatePreparationRequest request)
    {
        if (current is null) return proposed;
        if (proposed is null || !request.IsHistoryReplay) return current;
        var currentRegions = current.Regions.Select(static region => region.Id).ToHashSet();
        var currentNodes = current.Nodes.ToDictionary(static node => node.VisualStateId);
        var restoredIds = ChangedNodeGeometry(request);
        foreach (var node in proposed.Nodes)
            if (!currentNodes.ContainsKey(node.VisualStateId) || restoredIds.Contains(node.VisualStateId))
                currentNodes[node.VisualStateId] = node;
        return new ScopeGeometrySnapshot(current.PolicyId, current.PolicyVersion, current.LayoutAlgorithmId,
            current.Configuration, current.TextConfiguration, current.Contributors, currentNodes.Values,
            current.Regions.Concat(proposed.Regions.Where(region => !currentRegions.Contains(region.Id))),
            [], current.TextMeasurements, current.SpatialWidths.Concat(proposed.SpatialWidths.Where(width =>
                current.SpatialWidths.All(existing => existing.ProfileId != width.ProfileId))));
    }

    private static HashSet<VisualStateId> ChangedNodeGeometry(ConnectorRoutingStatePreparationRequest request)
    {
        var changed = request.NodeGeometryImpact?.ChangedVisualStateIds.ToHashSet() ?? [];
        foreach (var visual in request.ProposedDocument.VisualModel.VisualStates)
        {
            if (!request.ProposedDocument.SemanticModel.TryGetElement(visual.SemanticElementId, out _) ||
                !request.Before.VisualModel.TryGetVisualState(visual.Id, out var before)) continue;
            if (before!.Position != visual.Position || before.Size != visual.Size ||
                before.PlacementMode != visual.PlacementMode || !Equals(before.BoundaryAttachment, visual.BoundaryAttachment))
                changed.Add(visual.Id);
        }
        return changed;
    }

    private LayoutResult PrepareLocalLayout(ProjectedGraph graph, DocumentSnapshot document,
        ScopeGeometrySnapshot? saved, ConnectorRoutingStatePreparationRequest request,
        bool strict, CancellationToken cancellationToken)
    {
        if (saved is null)
        {
            if (strict) throw new InvalidOperationException("Saved geometry is required.");
            var execution = _configuration.LayoutEngine.Layout(graph, _configuration.LayoutAlgorithmId,
                _configuration.LayoutContext, cancellationToken);
            return execution.Result ?? throw new InvalidOperationException("Initial scope layout failed.");
        }
        if (!graph.Groups.IsEmpty)
            throw new InvalidOperationException("The saved geometry provider does not support projected groups.");
        var savedNodes = saved.Nodes.ToDictionary(static node => node.VisualStateId);
        var explicitGeometryEdits = request.IsHistoryReplay
            ? [] : ChangedNodeGeometry(request);
        var projectedBySemantic = graph.Nodes.GroupBy(static node => node.Source.SemanticElementId)
            .ToDictionary(static group => group.Key!, static group => group.ToArray());
        var resolved = new Dictionary<ProjectedObjectId, LayoutNodeGeometry>();
        var resolving = new HashSet<ProjectedObjectId>();
        LayoutResult? fresh = null;
        LayoutNodeGeometry Resolve(ProjectedNode node)
        {
            if (resolved.TryGetValue(node.Id, out var existing)) return existing;
            if (!resolving.Add(node.Id)) throw new InvalidOperationException("Cyclic node attachment geometry.");
            var hint = node.PlacementHint;
            savedNodes.TryGetValue(node.Source.VisualStateId!, out var prior);
            RectD bounds;
            Matrix2D transform;
            if (hint?.BoundaryAttachment is { } attachment)
            {
                if (!projectedBySemantic.TryGetValue(attachment.AttachedToElementId, out var hosts) || hosts.Length != 1)
                    throw new InvalidOperationException("An attached node requires exactly one current host.");
                bounds = attachment.Placement.ResolveBounds(Resolve(hosts[0]).Bounds, hint.Size);
                transform = Matrix2D.CreateTranslation(bounds.X, bounds.Y);
            }
            else if (hint is not null && (hint.PlacementMode == VisualPlacementMode.Pinned ||
                prior is not null && !strict && explicitGeometryEdits.Contains(node.Source.VisualStateId!)))
            {
                bounds = new RectD(hint.Position.X, hint.Position.Y, hint.Size.Width, hint.Size.Height);
                transform = prior is not null && prior.LocalBounds == bounds ? prior.Transform : Matrix2D.CreateTranslation(bounds.X, bounds.Y);
            }
            else if (prior is not null)
            {
                bounds = prior.LocalBounds;
                transform = prior.Transform;
            }
            else
            {
                if (strict) throw new InvalidOperationException("Saved node coverage is incomplete.");
                fresh ??= _configuration.LayoutEngine.Layout(graph, _configuration.LayoutAlgorithmId,
                    _configuration.LayoutContext, cancellationToken).Result
                    ?? throw new InvalidOperationException("New node layout failed.");
                var value = fresh.Nodes.Single(geometry => geometry.ProjectedObjectId == node.Id);
                bounds = value.Bounds;
                transform = value.Transform;
            }
            if (!DocumentGeometryBoundary.Contains(bounds))
                throw new InvalidOperationException("A complete node body lies outside the document origin.");
            var result = new LayoutNodeGeometry(node.Id, bounds, transform);
            if (strict && (prior is null || prior.LocalBounds != bounds || prior.Transform != transform))
                throw new InvalidOperationException("Saved node geometry conflicts with its authored placement or attachment.");
            resolved.Add(node.Id, result);
            resolving.Remove(node.Id);
            return result;
        }
        return new LayoutResult(document.DocumentId, document.Revision, _configuration.LayoutAlgorithmId,
            new LayoutComputation(graph.Nodes.Select(Resolve).ToArray()));
    }

    internal static LayoutResult RestoreLocalLayout(ProjectedGraph graph, ScopeGeometrySnapshot saved)
    {
        var nodes = saved.Nodes.ToDictionary(static node => node.VisualStateId);
        return new LayoutResult(graph.DocumentId, graph.SourceRevision, saved.LayoutAlgorithmId,
            new LayoutComputation(graph.Nodes.Select(node =>
            {
                var value = nodes[node.Source.VisualStateId!];
                return new LayoutNodeGeometry(node.Id, value.LocalBounds, value.Transform);
            })));
    }

    internal static LayoutResult CreateLogicalLayout(ProjectedGraph graph, LayoutResult local, ScopeGeometrySnapshot geometry)
    {
        var memberships = geometry.Nodes.ToDictionary(static node => node.VisualStateId);
        var regions = geometry.Regions.ToDictionary(static region => region.Id);
        var locals = local.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        return new LayoutResult(graph.DocumentId, graph.SourceRevision, local.AlgorithmId,
            new LayoutComputation(graph.Nodes.Select(node =>
            {
                var value = locals[node.Id];
                var saved = memberships[node.Source.VisualStateId!];
                if (saved.RegionId is null) return value;
                var transform = regions[saved.RegionId].LocalToScopeTransform!.Value;
                return new LayoutNodeGeometry(node.Id,
                    value.Bounds.Translate(new VectorD(transform.OffsetX, transform.OffsetY)), value.Transform.Then(transform));
            })));
    }

    internal static RoutingResult RestoreRouting(ProjectedGraph graph, LayoutResult local,
        ScopeRoutingSnapshot scope, AlgorithmId routingAlgorithmId,
        IStableConnectorRoutingPolicy? policy = null)
    {
        var byVisual = scope.Connectors.ToDictionary(static record => record.VisualStateId);
        var routes = new List<RoutedConnectorGeometry>();
        var noRoutes = new List<ProjectedObjectId>();
        var diagnostics = new List<Diagnostic>();
        var logicalLayout = CreateLogicalLayout(graph, local, scope.Geometry);
        RoutingContext? diagnosticContext = null;
        foreach (var edge in graph.Edges)
        {
            var record = byVisual[edge.Source.VisualStateId!];
            if (record.Outcome == ConnectorRoutingOutcome.NoRoute)
            {
                noRoutes.Add(edge.Id);
                if (policy is not null)
                {
                    diagnosticContext ??= new RoutingContext(preparedInput: new PreparedRoutingInput(graph, logicalLayout,
                        graph.Edges.Select(item => KeyValuePair.Create(item.Id,
                            new RoutingObstacleDomain(graph.Nodes.Select(static node => node.Id)))),
                        new RoutingLogicalGeometry(scope.ScopeId, scope.Geometry, logicalLayout)));
                    diagnostics.AddRange(policy.Assess(graph, logicalLayout, diagnosticContext, edge.Id, record).Diagnostics);
                }
            }
            else routes.Add(new RoutedConnectorGeometry(edge.Id, record.Path[0], record.Path[^1],
                record.Path.Skip(1).Take(record.Path.Length - 2), edge.SourcePortId, edge.TargetPortId));
        }
        return new RoutingResult(graph.DocumentId, graph.SourceRevision, local.AlgorithmId,
            routingAlgorithmId, new RoutingComputation(routes, noRouteEdgeIds: noRoutes), diagnostics,
            new RoutingLogicalGeometry(scope.ScopeId, scope.Geometry, logicalLayout));
    }
}
