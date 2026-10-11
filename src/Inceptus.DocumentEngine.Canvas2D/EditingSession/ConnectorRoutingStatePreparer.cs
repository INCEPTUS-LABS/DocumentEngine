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
                return ConnectorRoutingStatePreparationResult.Success(connectorLabelOnly, null,
                    documentRoutingStates: document.VisualModel.DocumentRoutingStates);
            if (await TryPrepareLabelUpdateAsync(request, cancellationToken).ConfigureAwait(false) is { } labelOnly)
                return ConnectorRoutingStatePreparationResult.Success(labelOnly, null,
                    documentRoutingStates: document.VisualModel.DocumentRoutingStates);
            var output = new List<ScopeRoutingSnapshot>();
            var diagnostics = new List<Diagnostic>();
            var beforeScopes = request.Before.VisualModel.RoutingScopes ?? [];
            var proposedScopes = document.VisualModel.RoutingScopes ?? [];
            var canPreserveScopes = request.Purpose == ConnectorRoutingPreparationPurpose.Mutation &&
                (request.NodeGeometryImpact is { HasExplicitChanges: true } || !request.SpatialHeightIntents.IsEmpty || !request.SpatialWidthIntents.IsEmpty) &&
                request.RoutingIntents.IsEmpty &&
                SemanticContentUnchanged(request.Before, document);
            foreach (var scope in document.SemanticModel.EnumerateScopeForest())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unchanged = beforeScopes.FirstOrDefault(item => item.ScopeId == scope.Id);
                if (canPreserveScopes && unchanged is not null &&
                    unchanged.Equals(proposedScopes.FirstOrDefault(item => item.ScopeId == scope.Id)) &&
                    !request.SpatialHeightIntents.Any(intent => intent.ScopeId == scope.Id) &&
                    !request.SpatialWidthIntents.Any(intent => intent.ScopeId == scope.Id))
                {
                    var priorInputs = CreateInputs(request.Before, scope.Id);
                    var nextInputs = CreateInputs(document, scope.Id);
                    if (priorInputs.VisualStates.AsSpan().SequenceEqual(nextInputs.VisualStates.AsSpan()) &&
                        priorInputs.ProfileElementPresentations.AsSpan().SequenceEqual(nextInputs.ProfileElementPresentations.AsSpan()) &&
                        !request.RoutingIntents.Any(intent => unchanged.Connectors.Any(record => record.VisualStateId == intent.VisualStateId)))
                    { output.Add(unchanged); continue; }
                }
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
                    _textMetrics, _requestFactory, cancellationToken, CreateInputs(request.Before, scope.Id)).ConfigureAwait(false);
                if (strict && (proposed is null || !geometry.Equals(proposed.Geometry)))
                    return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE",
                        $"Saved geometry, contributor policy or text metrics for scope '{scope.Id}' are incompatible.");
                if (strict) geometry = proposed!.Geometry;
                var logicalLayout = CreateLogicalLayout(graph, layout, geometry);
                var context = new RoutingContext(_configuration.RoutingContext.Options,
                    new PreparedRoutingInput(graph, logicalLayout,
                        graph.Edges.Select(edge => KeyValuePair.Create(edge.Id, new RoutingObstacleDomain(
                            graph.Nodes.Where(node => node.Id == edge.SourceNodeId || node.Id == edge.TargetNodeId ||
                                !Canvas2DNodePresentationMetadata.IsBoundaryContainer(node.ProjectedProperties)).Select(node => node.Id)))),
                        new RoutingLogicalGeometry(scope.Id, geometry, logicalLayout)));
                var records = PrepareRecords(request, graph, logicalLayout, context,
                    current?.Connectors ?? [], proposed?.Connectors ?? [],
                    request.Before.VisualModel.EnumerateConnectorRoutingRecords(), policy, diagnostics,
                    strict, cancellationToken, current?.Geometry);
                if (strict && !records.AsSpan().SequenceEqual(proposed!.Connectors.AsSpan()))
                    return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE",
                        $"Saved connector paths or policy for scope '{scope.Id}' are incompatible.");
                output.Add(new ScopeRoutingSnapshot(scope.Id, geometry, strict ? proposed!.Connectors : records));
            }
            if (strict && output.Count != proposedScopes.Length)
                return Fail("INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE", "Saved scope coverage does not match the document.");
            var documentStates = PrepareDocumentRouting(request, output, policy, diagnostics, strict, cancellationToken);
            return ConnectorRoutingStatePreparationResult.Success(output, diagnostics, documentStates);
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

    private static bool SemanticContentUnchanged(DocumentSnapshot before, DocumentSnapshot after) =>
        SemanticDefinitionsUnchanged(before, after) &&
        before.VisualModel.VisualStates.Where(visual => before.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) &&
            element!.ContainmentKind == Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementContainmentKind.Document)
            .SequenceEqual(after.VisualModel.VisualStates.Where(visual => after.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) &&
                element!.ContainmentKind == Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementContainmentKind.Document));

    private static bool SemanticDefinitionsUnchanged(DocumentSnapshot before, DocumentSnapshot after) =>
        before.SemanticModel.Elements.AsSpan().SequenceEqual(after.SemanticModel.Elements.AsSpan()) &&
        before.SemanticModel.Relationships.AsSpan().SequenceEqual(after.SemanticModel.Relationships.AsSpan()) &&
        before.SemanticModel.NestedScopes.AsSpan().SequenceEqual(after.SemanticModel.NestedScopes.AsSpan()) &&
        before.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(after.SemanticModel.ScopeMemberships.AsSpan()) &&
        before.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(after.SemanticModel.ProfileAssignments.AsSpan()) &&
        before.SemanticModel.ModelProfiles.Equals(after.SemanticModel.ModelProfiles);

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
        if (proposed is null) return current;
        var currentRegions = current.Regions.Select(static region => region.Id).ToHashSet();
        var currentNodes = current.Nodes.ToDictionary(static node => node.VisualStateId);
        var restoredIds = ChangedNodeGeometry(request);
        foreach (var node in proposed.Nodes)
            if (!currentNodes.ContainsKey(node.VisualStateId) || restoredIds.Contains(node.VisualStateId))
                currentNodes[node.VisualStateId] = node;
        return new ScopeGeometrySnapshot(current.PolicyId, current.PolicyVersion, current.LayoutAlgorithmId,
            current.Configuration, current.TextConfiguration, current.Contributors, currentNodes.Values,
            current.Regions.Select(region => proposed.Regions.FirstOrDefault(candidate => candidate.Id == region.Id) is { } seed
                ? new SpatialRegionGeometrySnapshot(seed.Id, seed.ProfileId, seed.ContainerSemanticElementId,
                    region.ExpandedHeight, seed.LocalToScopeTransform,
                    seed.ContentBounds is { } bounds ? new RectD(bounds.X, bounds.Y, bounds.Width, region.ExpandedHeight) : null)
                : region)
                .Concat(proposed.Regions.Where(region => !currentRegions.Contains(region.Id))),
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
        LayoutResult InitialLayout()
        {
            var nodes = graph.Nodes.Where(node => node.RegionEnvelope is null).ToArray();
            var ids = nodes.Select(node => node.Id).ToHashSet();
            var edges = graph.Edges.Where(edge => ids.Contains(edge.SourceNodeId) && ids.Contains(edge.TargetNodeId)).ToArray();
            var owners = ids.Concat(edges.Select(edge => edge.Id)).Concat(graph.Groups.Select(group => group.Id)).ToHashSet();
            var ports = graph.Ports.Where(port => ids.Contains(port.OwnerNodeId)).ToArray();
            owners.UnionWith(ports.Select(port => port.Id));
            var layoutGraph = new ProjectedGraph(graph.DocumentId, graph.SourceRevision, nodes, edges, graph.Groups, ports,
                graph.Labels.Where(label => owners.Contains(label.OwnerId)));
            var result = _configuration.LayoutEngine.Layout(layoutGraph, _configuration.LayoutAlgorithmId,
                _configuration.LayoutContext, cancellationToken).Result ?? throw new InvalidOperationException("Initial scope layout failed.");
            return new LayoutResult(graph.DocumentId, graph.SourceRevision, result.AlgorithmId,
                new LayoutComputation(result.Nodes.Concat(graph.Nodes.Where(node => node.RegionEnvelope is not null)
                    .Select(node => new LayoutNodeGeometry(node.Id, new RectD(0, 0, 1, 1), Matrix2D.Identity))), result.Groups));
        }
        if (saved is null)
        {
            if (strict) throw new InvalidOperationException("Saved geometry is required.");
            return InitialLayout();
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
            if (node.RegionEnvelope is { } envelope)
            {
                // This pre-measurement seed is discarded after the region provider runs.
                // Only the region-derived result reaches routing and the visible Scene.
                var boundary = saved is not null && envelope.RegionIds.All(id => saved.Regions.Any(region => region.Id == id && region.IsActive))
                    ? envelope.Resolve(saved.Regions) : new RectD(0, 0, 1, 1);
                return new(node.Id, boundary, Matrix2D.CreateTranslation(boundary.X, boundary.Y));
            }
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
                fresh ??= InitialLayout();
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
                if (node.RegionEnvelope is { } envelope)
                {
                    var bounds = envelope.Resolve(saved.Regions);
                    return new LayoutNodeGeometry(node.Id, bounds, Matrix2D.CreateTranslation(bounds.X, bounds.Y));
                }
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
                if (node.RegionEnvelope is { } envelope)
                {
                    var bounds = envelope.Resolve(geometry.Regions);
                    return new LayoutNodeGeometry(node.Id, bounds, Matrix2D.CreateTranslation(bounds.X, bounds.Y));
                }
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
                            new RoutingObstacleDomain(graph.Nodes.Where(node => node.RegionEnvelope is null ||
                                node.Id == item.SourceNodeId || node.Id == item.TargetNodeId).Select(static node => node.Id)))),
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
