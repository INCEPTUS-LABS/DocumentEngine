using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class ConnectorRoutingStatePreparer
{
    private ImmutableArray<DocumentRoutingSnapshot> PrepareDocumentRouting(
        ConnectorRoutingStatePreparationRequest request, IEnumerable<ScopeRoutingSnapshot> scopes,
        IStableConnectorRoutingPolicy policy, List<Diagnostic> diagnostics, bool strict,
        CancellationToken cancellationToken)
    {
        var proposed = request.ProposedDocument;
        var visual = proposed.VisualModel;
        var document = new DocumentSnapshot(proposed.SemanticModel,
            new VisualModelSnapshot(proposed.DocumentId, proposed.Revision, visual.VisualStates,
                visual.ProfileElementPresentations, scopes, visual.DocumentRoutingStates), proposed.Metadata, proposed.Publication);
        var processConnectorIds = document.VisualModel.RoutingScopes.GetValueOrDefault()
            .SelectMany(static scope => scope.Connectors).Select(static connector => connector.VisualStateId).ToHashSet();
        var expectedDocumentConnectorIds = visual.VisualStates.Where(state =>
                document.SemanticModel.TryGetRelationship(state.SemanticElementId, out _) && !processConnectorIds.Contains(state.Id))
            .Select(static state => state.Id).ToHashSet();
        if (visual.DocumentRoutingStates.IsEmpty && expectedDocumentConnectorIds.Count == 0)
            return [];
        var result = ImmutableArray.CreateBuilder<DocumentRoutingSnapshot>();
        var covered = new HashSet<VisualStateId>();
        var unresolvedDiagnostics = new List<Diagnostic>();
        foreach (var context in _configuration.ProjectionEngine.GetDocumentContexts(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = visual.DocumentRoutingStates.FirstOrDefault(state => state.OwnerId == context.OwnerId);
            var resolution = _configuration.ProjectionEngine.ResolvePresentation(document, context);
            if (!resolution.Succeeded || resolution.Plan is not { } plan)
            {
                if (existing is not null && existing.Connectors.Any(connector => expectedDocumentConnectorIds.Contains(connector.VisualStateId)))
                    throw new InvalidOperationException("A saved document routing owner could not resolve its authoritative members: " +
                        string.Join("; ", resolution.Diagnostics.Select(static diagnostic => diagnostic.Message)));
                unresolvedDiagnostics.AddRange(resolution.Diagnostics);
                continue;
            }
            var projection = _configuration.ProjectionEngine.Project(document, plan,
                _configuration.ProjectionContext, cancellationToken);
            if (projection.Graph is not { } graph)
                throw new InvalidOperationException("The document presentation could not project its authoritative members.");
            var edges = graph.Edges.Where(edge => !processConnectorIds.Contains(edge.Source.VisualStateId!)).ToArray();
            if (edges.Length == 0)
            {
                if (strict && existing is not null)
                    throw new InvalidOperationException("Saved document routing has no current connector owner.");
                continue;
            }
            diagnostics.AddRange(resolution.Diagnostics);
            diagnostics.AddRange(projection.Diagnostics);
            var layout = RestoreDocumentLayout(graph, document, plan);
            if (strict && existing is null)
                throw new InvalidOperationException("Saved document routing coverage is incomplete.");
            foreach (var edge in edges)
                if (edge.Source.VisualStateId is not { } id || !covered.Add(id))
                    throw new InvalidOperationException("A document connector requires one visual and one presentation owner.");
            var edgeIds = edges.Select(static edge => edge.Id).ToHashSet();
            var allEdgeIds = graph.Edges.Select(static edge => edge.Id).ToHashSet();
            var routingGraph = new ProjectedGraph(graph.DocumentId, graph.SourceRevision,
                graph.Nodes, edges, graph.Groups, graph.Ports,
                graph.Labels.Where(label => !allEdgeIds.Contains(label.OwnerId) || edgeIds.Contains(label.OwnerId)));
            var routingContext = new RoutingContext(_configuration.RoutingContext.Options,
                new PreparedRoutingInput(routingGraph, layout,
                    edges.Select(edge => KeyValuePair.Create(edge.Id,
                        new RoutingObstacleDomain(graph.Nodes.Where(node =>
                            node.Id == edge.SourceNodeId || node.Id == edge.TargetNodeId ||
                            !Canvas2DNodePresentationMetadata.IsBoundaryContainer(node.ProjectedProperties))
                            .Select(static node => node.Id))))));
            var previous = request.Before.VisualModel.DocumentRoutingStates.FirstOrDefault(state => state.OwnerId == context.OwnerId);
            var records = PrepareRecords(request, routingGraph, layout, routingContext,
                previous?.Connectors ?? [], existing?.Connectors ?? [],
                request.Before.VisualModel.EnumerateConnectorRoutingRecords(), policy, diagnostics, strict, cancellationToken,
                unchangedDocumentGeometry: previous is not null &&
                    HasUnchangedDocumentGeometry(request.Before, context, graph, layout, cancellationToken));
            if (strict && !records.AsSpan().SequenceEqual(existing!.Connectors.AsSpan()))
                throw new InvalidOperationException("Saved document connector paths or policy are incompatible.");
            result.Add(new DocumentRoutingSnapshot(context.OwnerId, strict ? existing!.Connectors : records));
        }
        if (!covered.SetEquals(expectedDocumentConnectorIds))
            throw new InvalidOperationException("Document connectors require exactly one resolved routing owner. " +
                string.Join("; ", unresolvedDiagnostics.Select(static diagnostic => diagnostic.Message)));
        if (strict && result.Count != visual.DocumentRoutingStates.Length)
            throw new InvalidOperationException("Saved document routing owners do not match registered presentation contexts.");
        return result.ToImmutable();
    }

    private bool HasUnchangedDocumentGeometry(DocumentSnapshot before, DocumentPresentationContext context,
        ProjectedGraph graph, LayoutResult layout, CancellationToken cancellationToken)
    {
        var resolution = _configuration.ProjectionEngine.ResolvePresentation(before, context);
        if (!resolution.Succeeded || resolution.Plan is not { } plan) return false;
        var previous = _configuration.ProjectionEngine.Project(before, plan, _configuration.ProjectionContext, cancellationToken);
        if (previous.Graph is not { } oldGraph || oldGraph.Nodes.Length != graph.Nodes.Length) return false;
        var oldLayout = RestoreDocumentLayout(oldGraph, before, plan);
        return oldLayout.AlgorithmId == layout.AlgorithmId && oldLayout.Nodes.AsSpan().SequenceEqual(layout.Nodes.AsSpan()) &&
            oldGraph.Nodes.Zip(graph.Nodes).All(pair => pair.First.Id == pair.Second.Id &&
                Canvas2DNodePresentationMetadata.IsBoundaryContainer(pair.First.ProjectedProperties) ==
                Canvas2DNodePresentationMetadata.IsBoundaryContainer(pair.Second.ProjectedProperties));
    }

    internal static LayoutResult RestoreDocumentLayout(ProjectedGraph graph, DocumentSnapshot document, PresentationViewPlan plan)
    {
        if (plan.DocumentId != document.DocumentId || plan.SourceRevision != document.Revision ||
            graph.DocumentId != document.DocumentId || graph.SourceRevision != document.Revision ||
            plan.Context is not DocumentPresentationContext)
            throw new InvalidOperationException("Document presentation geometry requires one matching authoritative revision.");
        var scopes = document.VisualModel.RoutingScopes ?? throw new InvalidOperationException("Saved process geometry is required.");
        var placements = plan.ScopePlacements.ToDictionary(static placement => placement.ScopeId);
        var scopeStates = scopes.ToDictionary(static scope => scope.ScopeId);
        var nodes = graph.Nodes.Select(node =>
        {
            if (node.Source.VisualStateId is not { } visualId ||
                !document.VisualModel.TryGetVisualState(visualId, out var visual) || visual is null ||
                visual.SemanticElementId != node.Source.SemanticElementId)
                throw new InvalidOperationException("Each document presentation node requires its real persistent Visual State.");
            if (document.SemanticModel.TryGetScope(visual.SemanticElementId, out var scope))
            {
                if (scope is null || !placements.TryGetValue(scope.Id, out var placement) ||
                    !scopeStates.TryGetValue(scope.Id, out var state) ||
                    state.Geometry.Nodes.SingleOrDefault(candidate => candidate.VisualStateId == visualId) is not { } saved)
                    throw new InvalidOperationException("Each process node requires saved geometry and an explicit view translation.");
                var regionTransform = saved.RegionId is { } regionId
                    ? state.Geometry.Regions.Single(region => region.Id == regionId).LocalToScopeTransform!.Value
                    : Matrix2D.Identity;
                var offset = new VectorD(regionTransform.OffsetX, regionTransform.OffsetY) + placement.Translation;
                return new LayoutNodeGeometry(node.Id, saved.LocalBounds.Translate(offset),
                    saved.Transform.Then(regionTransform).Then(Matrix2D.CreateTranslation(placement.Translation.X, placement.Translation.Y)));
            }
            if (visual.Size.Width <= 0d || visual.Size.Height <= 0d)
                throw new InvalidOperationException("Document node Visual States require positive authored geometry.");
            var bounds = new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height);
            if (!DocumentGeometryBoundary.Contains(bounds))
                throw new InvalidOperationException("Document node geometry lies outside the document origin.");
            return new LayoutNodeGeometry(node.Id, bounds, Matrix2D.CreateTranslation(bounds.X, bounds.Y));
        });
        var algorithm = scopes.FirstOrDefault()?.Geometry.LayoutAlgorithmId
            ?? throw new InvalidOperationException("Document geometry requires an existing saved layout policy.");
        return new LayoutResult(graph.DocumentId, graph.SourceRevision, algorithm, new LayoutComputation(nodes));
    }

    internal static RoutingResult RestoreDocumentRouting(ProjectedGraph graph, LayoutResult layout,
        DocumentSnapshot document, PresentationViewPlan plan, DocumentRoutingSnapshot? state, AlgorithmId routingAlgorithmId)
    {
        var placements = plan.ScopePlacements.ToDictionary(static placement => placement.ScopeId);
        var processRecords = document.VisualModel.RoutingScopes.GetValueOrDefault()
            .SelectMany(scope => scope.Connectors.Select(record => (scope.ScopeId, Record: record)))
            .ToDictionary(static item => item.Record.VisualStateId);
        var documentRecords = (state?.Connectors ?? []).ToDictionary(static record => record.VisualStateId);
        var routes = new List<RoutedConnectorGeometry>();
        var noRoutes = new List<ProjectedObjectId>();
        foreach (var edge in graph.Edges)
        {
            ConnectorRoutingRecord record;
            var offset = new VectorD(0d, 0d);
            if (processRecords.TryGetValue(edge.Source.VisualStateId!, out var process))
            {
                record = process.Record;
                if (!placements.TryGetValue(process.ScopeId, out var placement))
                    throw new InvalidOperationException("A visible process connector requires an explicit view translation.");
                offset = placement.Translation;
            }
            else if (!documentRecords.TryGetValue(edge.Source.VisualStateId!, out record!))
                throw new InvalidOperationException("A visible document connector requires saved routing authority.");
            if (record.Outcome == ConnectorRoutingOutcome.NoRoute) noRoutes.Add(edge.Id);
            else routes.Add(new RoutedConnectorGeometry(edge.Id, record.Path[0] + offset, record.Path[^1] + offset,
                record.Path.Skip(1).Take(record.Path.Length - 2).Select(point => point + offset), edge.SourcePortId, edge.TargetPortId));
        }
        return new RoutingResult(graph.DocumentId, graph.SourceRevision, layout.AlgorithmId,
            routingAlgorithmId, new RoutingComputation(routes, noRouteEdgeIds: noRoutes));
    }
}
