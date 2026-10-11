using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal sealed record EditingSessionPipelineArtifacts(
    PresentationViewContext PresentationContext,
    ProjectedGraph ProjectedGraph,
    LayoutResult LayoutResult,
    RoutingResult RoutingResult)
{
    internal PresentationViewPlan? PresentationPlan { get; init; }

    internal EditingSessionPipelineArtifacts(DocumentScopeId scopeId, ProjectedGraph graph,
        LayoutResult layout, RoutingResult routing)
        : this(new ProcessScopePresentationContext(scopeId), graph, layout, routing) { }

    internal DocumentScopeId ScopeId => PresentationContext is ProcessScopePresentationContext process
        ? process.ScopeId : throw new InvalidOperationException("A document presentation has no active process scope.");

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
        PresentationContext);

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
        DocumentScopeId scopeId) => IsCompatibleWith(documentId, revision, new ProcessScopePresentationContext(scopeId));

    internal bool IsCompatibleWith(DocumentId documentId, DocumentRevision revision,
        PresentationViewContext context) =>
        PresentationContext == context &&
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
        DocumentRevision committedRevision,
        ScopeGeometrySnapshot? committedGeometry = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        if (!IsCompatibleWith(documentId, previousRevision, PresentationContext))
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
            PresentationContext,
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
                RoutingResult.Diagnostics,
                RoutingResult.LogicalGeometry is { } logical
                    ? new RoutingLogicalGeometry(logical.ScopeId, committedGeometry ?? logical.Geometry,
                        new LayoutResult(documentId, committedRevision, logical.Layout.AlgorithmId,
                            logical.Layout.Computation, logical.Layout.Diagnostics))
                    : null))
        {
            PresentationPlan = PresentationPlan is { } plan ? new PresentationViewPlan(documentId, committedRevision,
                plan.Context, plan.ElementIds, plan.RelationshipIds, plan.ScopePlacements) : null,
        };
    }
}
