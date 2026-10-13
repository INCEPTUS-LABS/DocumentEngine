using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.Bpmn.Integration;

/// <summary>Native Inceptus JSON, using the canonical notation-neutral codec. No file or UI operations.</summary>
public static class BpmnDocumentSerializer
{
    public static string FormatIdentifier => NativeDocumentSerializer.FormatIdentifier;

    public static int CurrentVersion => NativeDocumentSerializer.FormatVersion;

    public static ImmutableArray<byte> Serialize(DocumentSnapshot document) => NativeDocumentSerializer.Export(document);

    public static BpmnDocumentReadResult Deserialize(ReadOnlyMemory<byte> utf8Json)
    {
        var result = NativeDocumentSerializer.Import(utf8Json, BpmnIntegrationPolicy.ConnectorAnchors);
        return new(result.Document?.CaptureSnapshot(), result.Succeeded ? CurrentVersion : null, result.Diagnostics);
    }
}
