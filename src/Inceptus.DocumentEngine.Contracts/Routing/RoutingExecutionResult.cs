using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable outcome of one complete framework-owned Routing Engine run.
/// </summary>
public sealed class RoutingExecutionResult : IEquatable<RoutingExecutionResult>
{
    private RoutingExecutionResult(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId layoutAlgorithmId,
        AlgorithmId routingAlgorithmId,
        RoutingExecutionStatus status,
        RoutingResult? result,
        IEnumerable<Diagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(layoutAlgorithmId);
        ArgumentNullException.ThrowIfNull(routingAlgorithmId);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The Routing execution status must be defined.");
        }

        Diagnostics = RoutingDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (status == RoutingExecutionStatus.Succeeded)
        {
            if (result is null ||
                result.DocumentId != documentId ||
                result.SourceRevision != sourceRevision ||
                result.LayoutAlgorithmId != layoutAlgorithmId ||
                result.RoutingAlgorithmId != routingAlgorithmId ||
                hasError)
            {
                throw new ArgumentException(
                    "A successful Routing execution requires a matching RoutingResult and no error diagnostics.",
                    nameof(result));
            }
        }
        else if (result is not null)
        {
            throw new ArgumentException(
                "A non-successful Routing execution cannot expose a partial RoutingResult.",
                nameof(result));
        }

        if (status == RoutingExecutionStatus.Failed && !hasError)
        {
            throw new ArgumentException(
                "A failed Routing execution requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        LayoutAlgorithmId = layoutAlgorithmId;
        RoutingAlgorithmId = routingAlgorithmId;
        Status = status;
        Result = result;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public AlgorithmId LayoutAlgorithmId { get; }

    public AlgorithmId RoutingAlgorithmId { get; }

    public RoutingExecutionStatus Status { get; }

    public RoutingResult? Result { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool IsSuccessful => Status == RoutingExecutionStatus.Succeeded;

    public static RoutingExecutionResult Success(RoutingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.DocumentId,
            result.SourceRevision,
            result.LayoutAlgorithmId,
            result.RoutingAlgorithmId,
            RoutingExecutionStatus.Succeeded,
            result,
            result.Diagnostics);
    }

    public static RoutingExecutionResult Failure(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId layoutAlgorithmId,
        AlgorithmId routingAlgorithmId,
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(
            documentId,
            sourceRevision,
            layoutAlgorithmId,
            routingAlgorithmId,
            RoutingExecutionStatus.Failed,
            null,
            diagnostics);
    }

    public static RoutingExecutionResult Cancelled(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId layoutAlgorithmId,
        AlgorithmId routingAlgorithmId,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            documentId,
            sourceRevision,
            layoutAlgorithmId,
            routingAlgorithmId,
            RoutingExecutionStatus.Cancelled,
            null,
            diagnostics);

    public bool Equals(RoutingExecutionResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        LayoutAlgorithmId == other.LayoutAlgorithmId &&
        RoutingAlgorithmId == other.RoutingAlgorithmId &&
        Status == other.Status &&
        Equals(Result, other.Result) &&
        RoutingDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as RoutingExecutionResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(LayoutAlgorithmId);
        hash.Add(RoutingAlgorithmId);
        hash.Add(Status);
        hash.Add(Result);
        RoutingDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
