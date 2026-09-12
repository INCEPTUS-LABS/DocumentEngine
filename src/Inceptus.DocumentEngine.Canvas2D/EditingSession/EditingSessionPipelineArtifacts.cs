using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal sealed record EditingSessionPipelineArtifacts(
    DocumentScopeId ScopeId,
    ProjectedGraph ProjectedGraph,
    LayoutResult LayoutResult,
    RoutingResult RoutingResult)
{
    internal EditingSessionPipelineArtifacts(
        ProjectedGraph projectedGraph,
        LayoutResult layoutResult,
        RoutingResult routingResult)
        : this(
            new DocumentScopeId(projectedGraph.DocumentId.Value),
            projectedGraph,
            layoutResult,
            routingResult)
    {
    }

    internal bool IsInternallyConsistent => IsCompatibleWith(
        ProjectedGraph.DocumentId,
        ProjectedGraph.SourceRevision,
        ScopeId);

    internal bool IsCompatibleWith(
        DocumentId documentId,
        DocumentRevision revision) =>
        IsCompatibleWith(
            documentId,
            revision,
            new DocumentScopeId(documentId.Value));

    internal bool IsCompatibleWith(
        DocumentId documentId,
        DocumentRevision revision,
        DocumentScopeId scopeId) =>
        ScopeId == scopeId &&
        ProjectedGraph.DocumentId == documentId &&
        ProjectedGraph.SourceRevision == revision &&
        LayoutResult.DocumentId == documentId &&
        LayoutResult.SourceRevision == revision &&
        RoutingResult.DocumentId == documentId &&
        RoutingResult.SourceRevision == revision &&
        RoutingResult.LayoutAlgorithmId == LayoutResult.AlgorithmId;

    internal EditingSessionPipelineArtifacts RebindToCommittedRevision(
        DocumentId documentId,
        DocumentRevision previousRevision,
        DocumentRevision committedRevision)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        if (!IsCompatibleWith(documentId, previousRevision, ScopeId))
        {
            throw new ArgumentException(
                "Only internally consistent artifacts from the previous Document revision can be rebound.",
                nameof(previousRevision));
        }

        if (committedRevision != previousRevision.Increment())
        {
            throw new ArgumentException(
                "The rebound artifact revision must be exactly one revision after the previous revision.",
                nameof(committedRevision));
        }

        return new EditingSessionPipelineArtifacts(
            ScopeId,
            new ProjectedGraph(
                documentId,
                committedRevision,
                ProjectedGraph.Nodes,
                ProjectedGraph.Edges,
                ProjectedGraph.Groups,
                ProjectedGraph.Ports,
                ProjectedGraph.Labels),
            new LayoutResult(
                documentId,
                committedRevision,
                LayoutResult.AlgorithmId,
                LayoutResult.Computation,
                LayoutResult.Diagnostics),
            new RoutingResult(
                documentId,
                committedRevision,
                RoutingResult.LayoutAlgorithmId,
                RoutingResult.RoutingAlgorithmId,
                RoutingResult.Computation,
                RoutingResult.Diagnostics));
    }
}
