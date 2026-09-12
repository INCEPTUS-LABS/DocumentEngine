using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable, transient geometry produced for one ProjectedGraph by one Layout Algorithm.
/// </summary>
public sealed class LayoutResult : IEquatable<LayoutResult>
{
    public LayoutResult(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId algorithmId,
        LayoutComputation computation,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(algorithmId);
        ArgumentNullException.ThrowIfNull(computation);

        var copiedDiagnostics = LayoutDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
        if (copiedDiagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A LayoutResult cannot contain error diagnostics.",
                nameof(diagnostics));
        }

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        AlgorithmId = algorithmId;
        Computation = computation;
        Diagnostics = copiedDiagnostics;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public AlgorithmId AlgorithmId { get; }

    public LayoutComputation Computation { get; }

    public ImmutableArray<LayoutNodeGeometry> Nodes => Computation.Nodes;

    public ImmutableArray<LayoutGroupGeometry> Groups => Computation.Groups;

    public PropertyMap Metadata => Computation.Metadata;

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public int NodeCount => Nodes.Length;

    public int GroupCount => Groups.Length;

    public bool Equals(LayoutResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        AlgorithmId == other.AlgorithmId &&
        Computation.Equals(other.Computation) &&
        LayoutDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as LayoutResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(AlgorithmId);
        hash.Add(Computation);
        LayoutDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
