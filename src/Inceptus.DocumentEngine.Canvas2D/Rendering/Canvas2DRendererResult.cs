using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

/// <summary>
/// Immutable outcome of one Canvas2DRenderer lifecycle or graphics operation.
/// </summary>
public sealed class Canvas2DRendererResult : IEquatable<Canvas2DRendererResult>
{
    private Canvas2DRendererResult(
        Canvas2DRendererOperationStatus status,
        IEnumerable<Diagnostic>? diagnostics)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The operation status must be defined.");
        }

        Status = status;
        Diagnostics = Canvas2DRendererDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (status == Canvas2DRendererOperationStatus.Succeeded && hasError)
        {
            throw new ArgumentException(
                "A successful renderer operation cannot contain error diagnostics.",
                nameof(diagnostics));
        }

        if (status != Canvas2DRendererOperationStatus.Succeeded && !hasError)
        {
            throw new ArgumentException(
                "A failed or cancelled renderer operation requires an error diagnostic.",
                nameof(diagnostics));
        }
    }

    public Canvas2DRendererOperationStatus Status { get; }

    public bool Succeeded => Status == Canvas2DRendererOperationStatus.Succeeded;

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static Canvas2DRendererResult Success() =>
        new(Canvas2DRendererOperationStatus.Succeeded, null);

    internal static Canvas2DRendererResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(Canvas2DRendererOperationStatus.Failed, diagnostics);

    internal static Canvas2DRendererResult Cancelled(Diagnostic diagnostic) =>
        new(Canvas2DRendererOperationStatus.Cancelled, [diagnostic]);

    public bool Equals(Canvas2DRendererResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Status == other.Status &&
        Canvas2DRendererDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DRendererResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        Canvas2DRendererDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
