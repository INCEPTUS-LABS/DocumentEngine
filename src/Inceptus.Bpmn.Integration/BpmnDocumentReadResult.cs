using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.Bpmn.Integration;

/// <summary>The native envelope version is distinct from document revision and the BPMN standard version.</summary>
public sealed record BpmnDocumentReadResult(
    DocumentSnapshot? Document,
    int? NativeVersion,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public bool Succeeded => Document is not null && NativeVersion is not null &&
        !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
