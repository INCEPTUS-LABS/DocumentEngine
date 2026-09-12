using System.Collections.Immutable;
using System.Text.Json;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Exports and imports the versioned, notation-neutral native Document format.
/// </summary>
public static class NativeDocumentSerializer
{
    internal const string MalformedJsonCode = "NATIVE_DOCUMENT_JSON_MALFORMED";
    internal const string InvalidFormatCode = "NATIVE_DOCUMENT_FORMAT_INVALID";
    internal const string UnsupportedVersionCode = "NATIVE_DOCUMENT_VERSION_UNSUPPORTED";
    internal const string InvalidStructureCode = "NATIVE_DOCUMENT_STRUCTURE_INVALID";

    /// <summary>
    /// Gets the native envelope format identifier.
    /// </summary>
    public static string FormatIdentifier => "Inceptus.Document";

    /// <summary>
    /// Gets the only native format version supported by this implementation.
    /// </summary>
    public static int FormatVersion => 1;

    /// <summary>
    /// Exports one coherent authoritative Document snapshot as deterministic UTF-8 JSON.
    /// </summary>
    public static ImmutableArray<byte> Export(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Export(document.CaptureSnapshot());
    }

    /// <summary>
    /// Exports one already-captured coherent authoritative Document snapshot as deterministic
    /// UTF-8 JSON.
    /// </summary>
    public static ImmutableArray<byte> Export(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return NativeDocumentJsonCodec.Write(snapshot);
    }

    /// <summary>
    /// Reconstructs a new Document from UTF-8 native JSON without Commands or History.
    /// </summary>
    public static DocumentConstructionResult Import(
        ReadOnlyMemory<byte> utf8Json,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        try
        {
            var snapshot = NativeDocumentJsonCodec.Read(utf8Json);
            return DocumentReconstructor.Reconstruct(
                snapshot,
                connectorAnchorPolicyProvider);
        }
        catch (JsonException exception)
        {
            var path = string.IsNullOrWhiteSpace(exception.Path) ? "$" : exception.Path;
            return Failure(
                MalformedJsonCode,
                "The native Document is not valid UTF-8 JSON.",
                path,
                new KeyValuePair<string, string>("Path", path));
        }
        catch (NativeDocumentReadException exception)
        {
            return exception.Kind switch
            {
                NativeDocumentReadFailureKind.InvalidFormat => Failure(
                    InvalidFormatCode,
                    $"The native format identifier '{exception.ActualValue}' is not supported.",
                    exception.Path,
                    new KeyValuePair<string, string>(
                        "ExpectedFormat",
                        FormatIdentifier),
                    new KeyValuePair<string, string>(
                        "ActualFormat",
                        exception.ActualValue ?? "null")),
                NativeDocumentReadFailureKind.UnsupportedVersion => Failure(
                    UnsupportedVersionCode,
                    $"Native format version '{exception.ActualValue}' is not supported.",
                    exception.Path,
                    new KeyValuePair<string, string>(
                        "SupportedVersion",
                        FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>(
                        "ActualVersion",
                        exception.ActualValue ?? "null")),
                _ => Failure(
                    InvalidStructureCode,
                    exception.Message,
                    exception.Path,
                    new KeyValuePair<string, string>("Path", exception.Path)),
            };
        }
        catch (ArgumentException)
        {
            return Failure(
                InvalidStructureCode,
                "The native Document contains a persistent value that violates a structural invariant.",
                "$.document",
                new KeyValuePair<string, string>("Path", "$.document"));
        }
        catch (InvalidOperationException)
        {
            return Failure(
                InvalidStructureCode,
                "The native Document contains a persistent value that cannot be reconstructed.",
                "$.document",
                new KeyValuePair<string, string>("Path", "$.document"));
        }
        catch (OverflowException)
        {
            return Failure(
                InvalidStructureCode,
                "The native Document contains a numeric value outside the supported range.",
                "$.document",
                new KeyValuePair<string, string>("Path", "$.document"));
        }
    }

    private static DocumentConstructionResult Failure(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        DocumentConstructionResult.Failure(
        [
            new Diagnostic(
                code,
                DiagnosticSeverity.Error,
                message,
                sourceIdentity,
                context),
        ]);
}
