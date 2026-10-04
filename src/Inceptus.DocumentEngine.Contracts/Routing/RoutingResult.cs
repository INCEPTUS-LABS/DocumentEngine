using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable, transient connector geometry produced for one ProjectedGraph and LayoutResult.
/// </summary>
public sealed class RoutingResult : IEquatable<RoutingResult>
{
    public RoutingResult(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId layoutAlgorithmId,
        AlgorithmId routingAlgorithmId,
        RoutingComputation computation,
        IEnumerable<Diagnostic>? diagnostics = null)
        : this(documentId, sourceRevision, layoutAlgorithmId, routingAlgorithmId, computation, diagnostics, null)
    {
    }

    public RoutingResult(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId layoutAlgorithmId,
        AlgorithmId routingAlgorithmId,
        RoutingComputation computation,
        IEnumerable<Diagnostic>? diagnostics,
        RoutingLogicalGeometry? logicalGeometry)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(layoutAlgorithmId);
        ArgumentNullException.ThrowIfNull(routingAlgorithmId);
        ArgumentNullException.ThrowIfNull(computation);

        var copiedDiagnostics = RoutingDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
        if (copiedDiagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A RoutingResult cannot contain error diagnostics.",
                nameof(diagnostics));
        }

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        LayoutAlgorithmId = layoutAlgorithmId;
        RoutingAlgorithmId = routingAlgorithmId;
        Computation = computation;
        Diagnostics = copiedDiagnostics;
        LogicalGeometry = logicalGeometry;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public AlgorithmId LayoutAlgorithmId { get; }

    public AlgorithmId RoutingAlgorithmId { get; }

    public RoutingComputation Computation { get; }

    public RoutingLogicalGeometry? LogicalGeometry { get; }

    public ImmutableArray<RoutedConnectorGeometry> Routes => Computation.Routes;

    public ImmutableArray<ProjectedObjectId> NoRouteEdgeIds => Computation.NoRouteEdgeIds;

    public PropertyMap Metadata => Computation.Metadata;

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public int RouteCount => Routes.Length;

    public bool Equals(RoutingResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        LayoutAlgorithmId == other.LayoutAlgorithmId &&
        RoutingAlgorithmId == other.RoutingAlgorithmId &&
        Computation.Equals(other.Computation) &&
        Equals(LogicalGeometry, other.LogicalGeometry) &&
        RoutingDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as RoutingResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(LayoutAlgorithmId);
        hash.Add(RoutingAlgorithmId);
        hash.Add(Computation);
        hash.Add(LogicalGeometry);
        RoutingDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
