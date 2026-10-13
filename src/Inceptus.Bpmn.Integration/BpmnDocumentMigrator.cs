using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.Bpmn.Integration;

/// <summary>
/// Native compatibility boundary. Only v2 is supported today; there are no supported older
/// schemas or migration steps. Saved-routing recovery within v2 is not a schema migration.
/// </summary>
public static class BpmnDocumentMigrator
{
    public static BpmnDocumentReadResult Migrate(DocumentSnapshot document, int nativeVersion)
    {
        ArgumentNullException.ThrowIfNull(document);
        return nativeVersion == BpmnDocumentSerializer.CurrentVersion
            ? new(document, nativeVersion, [])
            : new(null, nativeVersion,
                [new Diagnostic("NATIVE_DOCUMENT_VERSION_UNSUPPORTED", DiagnosticSeverity.Error,
                    $"Native format version '{nativeVersion}' is not supported. Only version {BpmnDocumentSerializer.CurrentVersion} is supported.")]);
    }
}
