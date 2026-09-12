using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal sealed partial class EditingSessionPipeline : ISessionPipelineProcessing
{
    private readonly EditingSessionConfiguration _configuration;
    private readonly Canvas2DRenderer? _renderer;

    internal EditingSessionPipeline(EditingSessionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    internal EditingSessionPipeline(
        EditingSessionConfiguration configuration,
        Canvas2DRenderer renderer)
        : this(configuration)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunFullAsync(
            document,
            document.SemanticModel.RootScopeId,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunFullAsync(
            document,
            activeScopeId,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunFullAsync(
            document,
            activeScopeId,
            editorState,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken) =>
        RunDocumentPipelineAsync(
            document,
            activeScopeId,
            previousArtifacts: null,
            nodeLayoutHistory: [],
            nodeGeometryImpact: null,
            preserveScopeLayoutIfCompatible: false,
            editorState,
            modelProfileViewState,
            modelProfileElementViewState,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        EditingSessionPipelineArtifacts previousArtifacts,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            document.SemanticModel.RootScopeId,
            previousArtifacts,
            nodeGeometryImpact,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeGeometryImpact,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            activeScopeId,
            previousArtifacts,
            [],
            nodeGeometryImpact,
            editorState,
            modelProfileViewState,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            document.SemanticModel.RootScopeId,
            previousArtifacts,
            nodeLayoutHistory,
            nodeGeometryImpact,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeLayoutHistory,
            nodeGeometryImpact,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeLayoutHistory,
            nodeGeometryImpact,
            editorState,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previousArtifacts);
        ArgumentNullException.ThrowIfNull(nodeGeometryImpact);
        return RunDocumentPipelineAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeLayoutHistory,
            nodeGeometryImpact,
            preserveScopeLayoutIfCompatible: false,
            editorState,
            modelProfileViewState,
            modelProfileElementViewState,
            cancellationToken);
    }

    public ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunPreservingScopeLayoutIfCompatibleAsync(
            document,
            activeScopeId,
            previousArtifacts,
            editorState,
            ModelProfileViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunPreservingScopeLayoutIfCompatibleAsync(
            document,
            activeScopeId,
            previousArtifacts,
            editorState,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            cancellationToken);

    public ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previousArtifacts);
        return RunDocumentPipelineAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeLayoutHistory: [],
            nodeGeometryImpact: null,
            preserveScopeLayoutIfCompatible: true,
            editorState,
            modelProfileViewState,
            modelProfileElementViewState,
            cancellationToken);
    }

    private async ValueTask<EditingSessionPipelineResult> RunDocumentPipelineAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts? previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        bool preserveScopeLayoutIfCompatible,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(editorState);
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        cancellationToken.ThrowIfCancellationRequested();

        var projection = _configuration.ProjectionEngine.Project(
            document,
            activeScopeId,
            _configuration.ProjectionContext,
            cancellationToken);
        if (!projection.IsSuccessful || projection.Graph is null)
        {
            return FromStageFailure(
                projection.Status == ProjectionStatus.Cancelled,
                projection.Diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scopedNodeGeometryImpact = ResolveScopedNodeGeometryImpact(
            document,
            activeScopeId,
            previousArtifacts,
            nodeGeometryImpact);
        var layout = preserveScopeLayoutIfCompatible
            ? TryCarryForwardCompatibleScopeLayout(
                previousArtifacts,
                projection.Graph,
                activeScopeId)
            : TryCarryForwardLayout(
                previousArtifacts,
                nodeLayoutHistory,
                projection.Graph,
                activeScopeId,
                scopedNodeGeometryImpact);
        if (layout is null && nodeGeometryImpact?.RequiresExactCarryForward == true)
        {
            return FromStageFailure(
                cancelled: false,
                [new Diagnostic(
                    EditingSessionDiagnosticCodes.NodeGeometryPreservationUnavailable,
                    DiagnosticSeverity.Error,
                    "The exact compatible node geometry required by this committed edit is unavailable; automatic layout was not invoked.",
                    document.DocumentId.Value)]);
        }

        if (layout is null)
        {
            var execution = _configuration.LayoutEngine.Layout(
                projection.Graph,
                _configuration.LayoutAlgorithmId,
                _configuration.LayoutContext,
                cancellationToken);
            if (!execution.IsSuccessful || execution.Result is null)
            {
                return FromStageFailure(
                    execution.Status == LayoutExecutionStatus.Cancelled,
                    execution.Diagnostics);
            }

            layout = execution.Result;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var routing = _configuration.RoutingEngine.Route(
            projection.Graph,
            layout,
            _configuration.RoutingAlgorithmId,
            _configuration.RoutingContext,
            cancellationToken);
        if (!routing.IsSuccessful || routing.Result is null)
        {
            return FromStageFailure(
                routing.Status == RoutingExecutionStatus.Cancelled,
                routing.Diagnostics);
        }

        var artifacts = new EditingSessionPipelineArtifacts(
            activeScopeId,
            projection.Graph,
            layout,
            routing.Result);
        cancellationToken.ThrowIfCancellationRequested();
        var scene = await BuildSceneAsync(
            artifacts.ProjectedGraph,
            artifacts.LayoutResult,
            artifacts.RoutingResult,
            document.VisualModel,
            editorState,
            document,
            activeScopeId,
            modelProfileViewState,
            modelProfileElementViewState,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = projection.Diagnostics
            .Concat(layout.Diagnostics)
            .Concat(routing.Diagnostics)
            .Concat(scene.Diagnostics);
        return scene.Succeeded && scene.Scene is not null
            ? EditingSessionPipelineResult.Success(
                artifacts,
                scene.Scene,
                diagnostics)
            : EditingSessionPipelineResult.Failure(diagnostics);
    }

    private static NodeGeometryPipelineImpact? ResolveScopedNodeGeometryImpact(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts? previousArtifacts,
        NodeGeometryPipelineImpact? nodeGeometryImpact)
    {
        if (nodeGeometryImpact is null || previousArtifacts is null)
        {
            return nodeGeometryImpact;
        }

        if (nodeGeometryImpact.HasRemovedVisualStates)
        {
            var previousScopeVisualStateIds = previousArtifacts.ProjectedGraph.Nodes
                .Where(static node => node.Source.VisualStateId is not null)
                .Select(static node => node.Source.VisualStateId!)
                .ToHashSet();
            var scopedRemovedIds = nodeGeometryImpact.RemovedVisualStateIds
                .Where(previousScopeVisualStateIds.Contains)
                .ToImmutableArray();
            return scopedRemovedIds.IsEmpty
                ? NodeGeometryPipelineImpact.PreserveAll
                : NodeGeometryPipelineImpact.ForRemovedVisualStates(scopedRemovedIds);
        }

        if (!nodeGeometryImpact.HasExplicitChanges)
        {
            return nodeGeometryImpact;
        }

        var scopedChangedIds = nodeGeometryImpact.ChangedVisualStateIds
            .Where(id => IsInActiveScopeOrUnresolved(document, activeScopeId, id))
            .ToImmutableArray();
        if (scopedChangedIds.IsEmpty)
        {
            return NodeGeometryPipelineImpact.PreserveAll;
        }

        return nodeGeometryImpact.HistoricalSourceRevision is { } sourceRevision
            ? NodeGeometryPipelineImpact.ForHistoricalRestoration(
                scopedChangedIds,
                sourceRevision)
            : NodeGeometryPipelineImpact.ForChangedVisualStates(scopedChangedIds);
    }

    private static bool IsInActiveScopeOrUnresolved(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        VisualStateId visualStateId)
    {
        if (!document.VisualModel.TryGetVisualState(visualStateId, out var visualState) ||
            visualState is null ||
            !document.SemanticModel.TryGetElement(
                visualState.SemanticElementId,
                out var semanticElement) ||
            semanticElement is null)
        {
            return true;
        }

        return document.SemanticModel.GetScope(semanticElement.Id).Id == activeScopeId;
    }

    private LayoutResult? TryCarryForwardLayout(
        EditingSessionPipelineArtifacts? previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        ProjectedGraph currentGraph,
        DocumentScopeId activeScopeId,
        NodeGeometryPipelineImpact? nodeGeometryImpact)
    {
        if (nodeGeometryImpact is null ||
            previousArtifacts is null ||
            !previousArtifacts.IsInternallyConsistent ||
            previousArtifacts.ScopeId != activeScopeId ||
            previousArtifacts.ProjectedGraph.DocumentId != currentGraph.DocumentId ||
            previousArtifacts.ProjectedGraph.SourceRevision >= currentGraph.SourceRevision ||
            previousArtifacts.LayoutResult.AlgorithmId != _configuration.LayoutAlgorithmId)
        {
            return null;
        }

        LayoutComputation? computation;
        if (nodeGeometryImpact.HasRemovedVisualStates)
        {
            computation = TryShrinkRemovedNodeGeometry(
                previousArtifacts,
                currentGraph,
                nodeGeometryImpact);
        }
        else
        {
            computation = TryAppendPinnedNodeGeometry(
                previousArtifacts,
                currentGraph,
                nodeGeometryImpact);
            if (computation is null && nodeGeometryImpact.HasExplicitChanges &&
                (nodeGeometryImpact.HistoricalSourceRevision is not null ||
                 NodeIdentitiesMatch(previousArtifacts.ProjectedGraph, currentGraph)))
            {
                computation = TryRestoreHistoricalNodeGeometry(
                    nodeLayoutHistory,
                    currentGraph,
                    activeScopeId,
                    nodeGeometryImpact);
            }

            if (computation is null &&
                NodeIdentitiesMatch(previousArtifacts.ProjectedGraph, currentGraph) &&
                previousArtifacts.ProjectedGraph.Groups.AsSpan().SequenceEqual(
                    currentGraph.Groups.AsSpan()) &&
                GeometryMatchesGraph(previousArtifacts.LayoutResult, currentGraph))
            {
                computation = nodeGeometryImpact.HasExplicitChanges
                    ? TryPatchExplicitNodeGeometry(
                        previousArtifacts,
                        currentGraph,
                        nodeGeometryImpact)
                    : previousArtifacts.ProjectedGraph.Nodes.AsSpan().SequenceEqual(
                        currentGraph.Nodes.AsSpan())
                        ? previousArtifacts.LayoutResult.Computation
                        : null;
            }
        }

        return computation is null
            ? null
            : new LayoutResult(
            currentGraph.DocumentId,
            currentGraph.SourceRevision,
            previousArtifacts.LayoutResult.AlgorithmId,
            computation,
            previousArtifacts.LayoutResult.Diagnostics);
    }

    private LayoutResult? TryCarryForwardCompatibleScopeLayout(
        EditingSessionPipelineArtifacts? previousArtifacts,
        ProjectedGraph currentGraph,
        DocumentScopeId activeScopeId)
    {
        if (previousArtifacts is null ||
            !previousArtifacts.IsInternallyConsistent ||
            previousArtifacts.ScopeId != activeScopeId ||
            previousArtifacts.ProjectedGraph.DocumentId != currentGraph.DocumentId ||
            previousArtifacts.ProjectedGraph.SourceRevision >= currentGraph.SourceRevision ||
            previousArtifacts.LayoutResult.AlgorithmId != _configuration.LayoutAlgorithmId ||
            !ProjectedGraphContentMatches(previousArtifacts.ProjectedGraph, currentGraph) ||
            !GeometryMatchesGraph(previousArtifacts.LayoutResult, currentGraph))
        {
            return null;
        }

        return new LayoutResult(
            currentGraph.DocumentId,
            currentGraph.SourceRevision,
            previousArtifacts.LayoutResult.AlgorithmId,
            previousArtifacts.LayoutResult.Computation,
            previousArtifacts.LayoutResult.Diagnostics);
    }

    private static LayoutComputation? TryShrinkRemovedNodeGeometry(
        EditingSessionPipelineArtifacts previousArtifacts,
        ProjectedGraph currentGraph,
        NodeGeometryPipelineImpact nodeGeometryImpact)
    {
        var requestedIds = nodeGeometryImpact.RemovedVisualStateIds.ToHashSet();
        var previousNodesByVisualState = previousArtifacts.ProjectedGraph.Nodes
            .Where(static node => node.Source.VisualStateId is not null)
            .GroupBy(static node => node.Source.VisualStateId!)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        if (requestedIds.Any(id =>
                !previousNodesByVisualState.TryGetValue(id, out var nodes) ||
                nodes.Length != 1) ||
            requestedIds.Any(id => currentGraph.Nodes.Any(node =>
                node.Source.VisualStateId == id)))
        {
            return null;
        }

        var removedProjectedIds = requestedIds
            .Select(id => previousNodesByVisualState[id][0].Id)
            .ToHashSet();
        if (removedProjectedIds.Count != requestedIds.Count ||
            !previousArtifacts.ProjectedGraph.Nodes
                .Where(node => !removedProjectedIds.Contains(node.Id))
                .SequenceEqual(currentGraph.Nodes) ||
            !previousArtifacts.ProjectedGraph.Groups.AsSpan().SequenceEqual(
                currentGraph.Groups.AsSpan()) ||
            !GeometryMatchesGraph(
                previousArtifacts.LayoutResult,
                previousArtifacts.ProjectedGraph))
        {
            return null;
        }

        var currentProjectedIds = currentGraph.Nodes
            .Select(static node => node.Id)
            .ToHashSet();
        var survivingGeometry = previousArtifacts.LayoutResult.Nodes
            .Where(geometry => currentProjectedIds.Contains(geometry.ProjectedObjectId));
        return new LayoutComputation(
            survivingGeometry,
            previousArtifacts.LayoutResult.Groups,
            previousArtifacts.LayoutResult.Metadata);
    }

    private LayoutComputation? TryRestoreHistoricalNodeGeometry(
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        ProjectedGraph currentGraph,
        DocumentScopeId activeScopeId,
        NodeGeometryPipelineImpact nodeGeometryImpact)
    {
        if (!ChangedNodeIdsResolveExactly(currentGraph, nodeGeometryImpact))
        {
            return null;
        }

        for (var index = nodeLayoutHistory.Length - 1; index >= 0; index--)
        {
            var candidate = nodeLayoutHistory[index];
            if (candidate.IsInternallyConsistent &&
                candidate.ScopeId == activeScopeId &&
                candidate.ProjectedGraph.DocumentId == currentGraph.DocumentId &&
                candidate.ProjectedGraph.SourceRevision < currentGraph.SourceRevision &&
                (nodeGeometryImpact.HistoricalSourceRevision is null ||
                 candidate.ProjectedGraph.SourceRevision ==
                    nodeGeometryImpact.HistoricalSourceRevision.Value) &&
                candidate.LayoutResult.AlgorithmId == _configuration.LayoutAlgorithmId &&
                ProjectedGraphContentMatches(candidate.ProjectedGraph, currentGraph) &&
                GeometryMatchesGraph(candidate.LayoutResult, currentGraph))
            {
                return candidate.LayoutResult.Computation;
            }
        }

        return null;
    }

    private static LayoutComputation? TryPatchExplicitNodeGeometry(
        EditingSessionPipelineArtifacts previousArtifacts,
        ProjectedGraph currentGraph,
        NodeGeometryPipelineImpact nodeGeometryImpact)
    {
        var requestedIds = nodeGeometryImpact.ChangedVisualStateIds.ToHashSet();
        var currentNodesByVisualState = currentGraph.Nodes
            .Where(static node => node.Source.VisualStateId is not null)
            .GroupBy(static node => node.Source.VisualStateId!)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        if (requestedIds.Any(id =>
                !currentNodesByVisualState.TryGetValue(id, out var nodes) ||
                nodes.Length != 1))
        {
            return null;
        }

        var changedProjectedIds = requestedIds
            .Select(id => currentNodesByVisualState[id][0].Id)
            .ToHashSet();
        if (changedProjectedIds.Count != requestedIds.Count)
        {
            return null;
        }

        var previousNodesById = previousArtifacts.ProjectedGraph.Nodes
            .ToDictionary(static node => node.Id);
        foreach (var currentNode in currentGraph.Nodes)
        {
            var previousNode = previousNodesById[currentNode.Id];
            if (changedProjectedIds.Contains(currentNode.Id))
            {
                if (currentNode.PlacementHint is null ||
                    !MatchesExceptPlacement(previousNode, currentNode))
                {
                    return null;
                }
            }
            else if (!previousNode.Equals(currentNode))
            {
                return null;
            }
        }

        var currentNodesById = currentGraph.Nodes.ToDictionary(static node => node.Id);
        var currentNodesBySemanticElementId = currentGraph.Nodes
            .Where(static node => node.Source.SemanticElementId is not null)
            .GroupBy(static node => node.Source.SemanticElementId!)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var previousGeometryById = previousArtifacts.LayoutResult.Nodes
            .ToDictionary(static geometry => geometry.ProjectedObjectId);
        var resolvedBoundsById = new Dictionary<ProjectedObjectId, RectD>();
        var resolving = new HashSet<ProjectedObjectId>();

        RectD? ResolvePatchedBounds(ProjectedNode node)
        {
            if (resolvedBoundsById.TryGetValue(node.Id, out var resolvedBounds))
            {
                return resolvedBounds;
            }

            if (!previousGeometryById.TryGetValue(node.Id, out var previousGeometry))
            {
                return null;
            }

            if (!changedProjectedIds.Contains(node.Id))
            {
                resolvedBoundsById[node.Id] = previousGeometry.Bounds;
                return previousGeometry.Bounds;
            }

            if (!resolving.Add(node.Id))
            {
                return null;
            }

            try
            {
                var placement = node.PlacementHint!;
                var attachment = placement.BoundaryAttachment;
                RectD bounds;
                if (attachment is null)
                {
                    bounds = new RectD(
                        placement.Position.X,
                        placement.Position.Y,
                        placement.Size.Width,
                        placement.Size.Height);
                }
                else
                {
                    if (!currentNodesBySemanticElementId.TryGetValue(
                            attachment.AttachedToElementId,
                            out var ownerNodes) ||
                        ownerNodes.Length != 1 ||
                        ResolvePatchedBounds(ownerNodes[0]) is not { } ownerBounds)
                    {
                        return null;
                    }

                    bounds = attachment.Placement.ResolveBounds(
                        ownerBounds,
                        placement.Size);
                }

                if (bounds.IsEmpty || !DocumentGeometryBoundary.Contains(bounds))
                {
                    return null;
                }

                resolvedBoundsById[node.Id] = bounds;
                return bounds;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
            finally
            {
                _ = resolving.Remove(node.Id);
            }
        }

        foreach (var changedId in changedProjectedIds)
        {
            if (ResolvePatchedBounds(currentNodesById[changedId]) is null)
            {
                return null;
            }
        }

        var patchedNodes = previousArtifacts.LayoutResult.Nodes.Select(previousGeometry =>
        {
            if (!changedProjectedIds.Contains(previousGeometry.ProjectedObjectId))
            {
                return previousGeometry;
            }

            var bounds = resolvedBoundsById[previousGeometry.ProjectedObjectId];
            if (bounds == previousGeometry.Bounds)
            {
                return previousGeometry;
            }

            var transform = new Matrix2D(
                previousGeometry.Transform.M11,
                previousGeometry.Transform.M12,
                previousGeometry.Transform.M21,
                previousGeometry.Transform.M22,
                bounds.X +
                    (previousGeometry.Transform.OffsetX - previousGeometry.Bounds.X),
                bounds.Y +
                    (previousGeometry.Transform.OffsetY - previousGeometry.Bounds.Y));
            return new LayoutNodeGeometry(
                previousGeometry.ProjectedObjectId,
                bounds,
                transform);
        });
        return new LayoutComputation(
            patchedNodes,
            previousArtifacts.LayoutResult.Groups,
            previousArtifacts.LayoutResult.Metadata);
    }

    private static bool NodeIdentitiesMatch(ProjectedGraph previous, ProjectedGraph current) =>
        previous.Nodes.Select(static node => node.Id)
            .SequenceEqual(current.Nodes.Select(static node => node.Id));

    private static bool ChangedNodeIdsResolveExactly(
        ProjectedGraph graph,
        NodeGeometryPipelineImpact nodeGeometryImpact)
    {
        var requestedIds = nodeGeometryImpact.ChangedVisualStateIds.ToHashSet();
        return requestedIds.Count == nodeGeometryImpact.ChangedVisualStateIds.Length &&
            requestedIds.All(id => graph.Nodes.Count(node =>
                node.Source.VisualStateId == id) == 1);
    }

    private static bool ProjectedGraphContentMatches(
        ProjectedGraph previous,
        ProjectedGraph current) =>
        previous.Nodes.AsSpan().SequenceEqual(current.Nodes.AsSpan()) &&
        previous.Edges.AsSpan().SequenceEqual(current.Edges.AsSpan()) &&
        previous.Groups.AsSpan().SequenceEqual(current.Groups.AsSpan()) &&
        previous.Ports.AsSpan().SequenceEqual(current.Ports.AsSpan()) &&
        previous.Labels.AsSpan().SequenceEqual(current.Labels.AsSpan());

    private static bool MatchesExceptPlacement(ProjectedNode previous, ProjectedNode current) =>
        previous.Id == current.Id &&
        previous.Source.Equals(current.Source) &&
        previous.GeometryInteractionPolicy == current.GeometryInteractionPolicy &&
        previous.SemanticProperties.Equals(current.SemanticProperties) &&
        previous.ProjectedProperties.Equals(current.ProjectedProperties) &&
        previous.LayoutHints.Equals(current.LayoutHints) &&
        previous.RoutingHints.Equals(current.RoutingHints) &&
        previous.AlgorithmMetadata.Equals(current.AlgorithmMetadata);

    private static bool GeometryMatchesGraph(LayoutResult layout, ProjectedGraph graph) =>
        layout.Nodes.Select(static geometry => geometry.ProjectedObjectId)
            .SequenceEqual(graph.Nodes.Select(static node => node.Id)) &&
        layout.Groups.Select(static geometry => geometry.ProjectedObjectId)
            .SequenceEqual(graph.Groups.Select(static group => group.Id));

    public async ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        EditingSessionPipelineArtifacts artifacts,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(editorState);
        cancellationToken.ThrowIfCancellationRequested();
        var scene = await BuildCompatibilitySceneAsync(
            artifacts.ProjectedGraph,
            artifacts.LayoutResult,
            artifacts.RoutingResult,
            visualModel,
            editorState,
            cancellationToken).ConfigureAwait(false);
        var diagnostics = artifacts.LayoutResult.Diagnostics
            .Concat(artifacts.RoutingResult.Diagnostics)
            .Concat(scene.Diagnostics);
        return scene.Succeeded && scene.Scene is not null
            ? EditingSessionPipelineResult.Success(artifacts, scene.Scene, diagnostics)
            : EditingSessionPipelineResult.Failure(diagnostics);
    }

    public async ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        DocumentSnapshot document,
        EditingSessionPipelineArtifacts artifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        await RebuildSceneAsync(
            document,
            artifacts,
            editorState,
            modelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty,
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        DocumentSnapshot document,
        EditingSessionPipelineArtifacts artifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(editorState);
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        cancellationToken.ThrowIfCancellationRequested();

        var scene = await BuildSceneAsync(
            artifacts.ProjectedGraph,
            artifacts.LayoutResult,
            artifacts.RoutingResult,
            document.VisualModel,
            editorState,
            document,
            artifacts.ScopeId,
            modelProfileViewState,
            modelProfileElementViewState,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = artifacts.LayoutResult.Diagnostics
            .Concat(artifacts.RoutingResult.Diagnostics)
            .Concat(scene.Diagnostics);
        return scene.Succeeded && scene.Scene is not null
            ? EditingSessionPipelineResult.Success(
                artifacts,
                scene.Scene,
                diagnostics)
            : EditingSessionPipelineResult.Failure(diagnostics);
    }

    private ValueTask<Canvas2DSceneBuildResult> BuildSceneAsync(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken) =>
        _renderer is null
            ? ValueTask.FromResult(_configuration.SceneBuilder.Build(
                document,
                activeScopeId,
                modelProfileViewState,
                modelProfileElementViewState,
                graph,
                layout,
                routing,
                visualModel,
                editorState))
            : _configuration.SceneBuilder.BuildMeasuredAsync(
                document,
                activeScopeId,
                modelProfileViewState,
                modelProfileElementViewState,
                graph,
                layout,
                routing,
                visualModel,
                editorState,
                _renderer,
                _renderer.CreateTextMeasurementRequest,
                cancellationToken);

    private ValueTask<Canvas2DSceneBuildResult> BuildCompatibilitySceneAsync(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        _renderer is null
            ? ValueTask.FromResult(_configuration.SceneBuilder.Build(
                graph,
                layout,
                routing,
                visualModel,
                editorState))
            : _configuration.SceneBuilder.BuildMeasuredAsync(
                graph,
                layout,
                routing,
                visualModel,
                editorState,
                _renderer,
                _renderer.CreateTextMeasurementRequest,
                cancellationToken);

    private static EditingSessionPipelineResult FromStageFailure(
        bool cancelled,
        IEnumerable<Diagnostic> diagnostics) =>
        cancelled
            ? EditingSessionPipelineResult.Cancelled(diagnostics)
            : EditingSessionPipelineResult.Failure(diagnostics);
}
