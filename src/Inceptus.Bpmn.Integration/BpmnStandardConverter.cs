using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.Bpmn.Integration;

/// <summary>
/// Converts the common BPMN 2.0.2 subset. Unrepresented information is reported, not hidden in extensions.
/// </summary>
public static class BpmnStandardConverter
{
    public const string StandardVersion = "2.0.2";
    public static bool IsSupported => true;

    public static BpmnStandardConversionResult Export(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new BpmnXmlExporter(document).Convert();
    }

    public static BpmnStandardConversionResult Import(string xml) => Import(xml, BpmnImportProfile.Default);

    public static BpmnStandardConversionResult Import(string xml, BpmnImportProfile profile)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(profile);
        return new BpmnXmlImporter(xml, profile).Convert();
    }

    /// <summary>
    /// Imports DI paths through the host's existing geometry/routing preparation service.
    /// The synchronous overload returns a fresh snapshot with shape geometry; it reports paths
    /// that require preparation. No renderer, editor instance, or browser is required here.
    /// </summary>
    public static ValueTask<BpmnStandardConversionResult> ImportAsync(string xml,
        IConnectorRoutingStatePreparer preparer, CancellationToken cancellationToken = default) =>
        ImportAsync(xml, preparer, BpmnImportProfile.Default, cancellationToken);

    public static ValueTask<BpmnStandardConversionResult> ImportAsync(string xml,
        IConnectorRoutingStatePreparer preparer, BpmnImportProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(preparer);
        ArgumentNullException.ThrowIfNull(profile);
        return new BpmnXmlImporter(xml, profile).ConvertAsync(preparer, cancellationToken);
    }
}

public enum BpmnConversionDisposition { Converted, Omitted, Ignored, Warning, Error }

/// <summary>Source information lost during conversion, including its source and optional destination identity.</summary>
public sealed record BpmnConversionLogEntry(BpmnConversionDisposition Disposition, string Code,
    string Message, string? SourceIdentity = null, string? Property = null, string? TargetIdentity = null);

public sealed record BpmnStandardConversionResult(
    DocumentSnapshot? Document,
    string? Xml,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>Only Omitted/Ignored source information; execution diagnostics are separate.</summary>
    public ImmutableArray<BpmnConversionLogEntry> ConversionLog { get; init; } = [];
    public bool HasLosses => ConversionLog.Any(static entry =>
        entry.Disposition is BpmnConversionDisposition.Omitted or BpmnConversionDisposition.Ignored);
    public bool Succeeded => (Document is not null || Xml is not null) &&
        !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
