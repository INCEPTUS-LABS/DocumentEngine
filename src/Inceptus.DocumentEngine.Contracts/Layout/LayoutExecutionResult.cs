using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable outcome of one complete framework-owned Layout Engine run.
/// </summary>
public sealed class LayoutExecutionResult : IEquatable<LayoutExecutionResult>
{
    private LayoutExecutionResult(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId algorithmId,
        LayoutExecutionStatus status,
        LayoutResult? result,
        IEnumerable<Diagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(algorithmId);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The Layout execution status must be defined.");
        }

        Diagnostics = LayoutDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (status == LayoutExecutionStatus.Succeeded)
        {
            if (result is null ||
                result.DocumentId != documentId ||
                result.SourceRevision != sourceRevision ||
                result.AlgorithmId != algorithmId ||
                hasError)
            {
                throw new ArgumentException(
                    "A successful Layout execution requires a matching LayoutResult and no error diagnostics.",
                    nameof(result));
            }
        }
        else if (result is not null)
        {
            throw new ArgumentException(
                "A non-successful Layout execution cannot expose a partial LayoutResult.",
                nameof(result));
        }

        if (status == LayoutExecutionStatus.Failed && !hasError)
        {
            throw new ArgumentException(
                "A failed Layout execution requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        AlgorithmId = algorithmId;
        Status = status;
        Result = result;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public AlgorithmId AlgorithmId { get; }

    public LayoutExecutionStatus Status { get; }

    public LayoutResult? Result { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool IsSuccessful => Status == LayoutExecutionStatus.Succeeded;

    public static LayoutExecutionResult Success(LayoutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.DocumentId,
            result.SourceRevision,
            result.AlgorithmId,
            LayoutExecutionStatus.Succeeded,
            result,
            result.Diagnostics);
    }

    public static LayoutExecutionResult Failure(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId algorithmId,
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(
            documentId,
            sourceRevision,
            algorithmId,
            LayoutExecutionStatus.Failed,
            null,
            diagnostics);
    }

    public static LayoutExecutionResult Cancelled(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId algorithmId,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            documentId,
            sourceRevision,
            algorithmId,
            LayoutExecutionStatus.Cancelled,
            null,
            diagnostics);

    public bool Equals(LayoutExecutionResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        AlgorithmId == other.AlgorithmId &&
        Status == other.Status &&
        Equals(Result, other.Result) &&
        LayoutDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as LayoutExecutionResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(AlgorithmId);
        hash.Add(Status);
        hash.Add(Result);
        LayoutDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
