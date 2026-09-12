using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Immutable result of one complete Projection run.
/// </summary>
public sealed class ProjectionResult : IEquatable<ProjectionResult>
{
    private ProjectionResult(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        ProjectionStatus status,
        ProjectedGraph? graph,
        IEnumerable<Diagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        Diagnostics = ProjectionDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (status == ProjectionStatus.Succeeded)
        {
            if (graph is null ||
                graph.DocumentId != documentId ||
                graph.SourceRevision != sourceRevision ||
                hasError)
            {
                throw new ArgumentException(
                    "A successful Projection result requires a matching graph and no error diagnostics.",
                    nameof(graph));
            }
        }
        else if (graph is not null)
        {
            throw new ArgumentException(
                "A non-successful Projection result cannot expose a partially usable graph.",
                nameof(graph));
        }

        if (status == ProjectionStatus.Failed && !hasError)
        {
            throw new ArgumentException(
                "A failed Projection result requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        Status = status;
        Graph = graph;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public ProjectionStatus Status { get; }

    public ProjectedGraph? Graph { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool IsSuccessful => Status == ProjectionStatus.Succeeded;

    public static ProjectionResult Success(
        ProjectedGraph graph,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new(
            graph.DocumentId,
            graph.SourceRevision,
            ProjectionStatus.Succeeded,
            graph,
            diagnostics);
    }

    public static ProjectionResult Failure(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(
            documentId,
            sourceRevision,
            ProjectionStatus.Failed,
            null,
            diagnostics);
    }

    public static ProjectionResult Cancelled(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            documentId,
            sourceRevision,
            ProjectionStatus.Cancelled,
            null,
            diagnostics);

    public bool Equals(ProjectionResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        Status == other.Status &&
        Equals(Graph, other.Graph) &&
        ProjectionDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as ProjectionResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(Status);
        hash.Add(Graph);
        ProjectionDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
