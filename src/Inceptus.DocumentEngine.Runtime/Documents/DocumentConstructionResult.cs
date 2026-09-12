using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Represents the atomic outcome of creating or reconstructing a Document.
/// </summary>
public sealed class DocumentConstructionResult
{
    private DocumentConstructionResult(
        bool succeeded,
        Document? document,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Succeeded = succeeded;
        Document = document;
        Diagnostics = diagnostics;
    }

    public bool Succeeded { get; }

    public Document? Document { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static DocumentConstructionResult Success(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentConstructionResult(true, document, []);
    }

    internal static DocumentConstructionResult Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var copiedDiagnostics = diagnostics.ToImmutableArray();

        if (copiedDiagnostics.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A failed Document construction must include at least one diagnostic.",
                nameof(diagnostics));
        }

        if (copiedDiagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Document construction diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        if (!copiedDiagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A failed Document construction must include an error diagnostic.",
                nameof(diagnostics));
        }

        return new DocumentConstructionResult(false, null, copiedDiagnostics);
    }
}
