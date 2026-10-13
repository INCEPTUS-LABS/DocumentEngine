using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private string? _nativeDocumentCanvasElementId;
    private string? _nativeDocumentStandbyCanvasElementId;
    private EditingSessionConfiguration? _nativeDocumentSessionConfiguration;

    private static Diagnostic Error(string code, string message, string? sourceIdentity = null) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}

internal sealed record NativeDocumentHostImportResult(
    BpmnModelerOperationStatus Status,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal DocumentSnapshot? Snapshot { get; init; }

    internal bool Succeeded => Status == BpmnModelerOperationStatus.Succeeded;

    internal static NativeDocumentHostImportResult Success { get; } =
        new(BpmnModelerOperationStatus.Succeeded, []);

    internal static NativeDocumentHostImportResult Rejected(
        ImmutableArray<Diagnostic> diagnostics) =>
        new(BpmnModelerOperationStatus.Rejected, diagnostics);

    internal static NativeDocumentHostImportResult Failure(
        BpmnModelerOperationStatus status,
        Diagnostic diagnostic) =>
        new(status, [diagnostic]);

    internal static NativeDocumentHostImportResult Cancelled { get; } =
        new(BpmnModelerOperationStatus.Cancelled, []);
}
