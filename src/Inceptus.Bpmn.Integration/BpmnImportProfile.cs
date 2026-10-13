using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;

namespace Inceptus.Bpmn.Integration;

public enum BpmnImportAction { Map, AcceptDefault, AcceptMetadata, AcceptDerived, Normalize, Degrade, Ignore }
public enum BpmnImportDisposition { Ignored }
public enum BpmnImportReportScope { Element, Owner, Diagram, Document }
public enum BpmnImportValueKind { Text, Boolean, WholeNumber }
public enum BpmnImportContext { Any, PoolOrLane }
public enum BpmnImportStrategy { None, DerivedFromTopology }
public enum BpmnImportTransformation { PoolLocalCoordinates, DiagramOrigin, BoundaryPosition, ConnectorEndpoint, ConnectorPath }

public sealed record BpmnImportReportRule(BpmnImportDisposition Disposition, string Code,
    string Message, BpmnImportReportScope Scope);

public sealed record BpmnImportValueRule(BpmnImportAction Action, string? TargetValue,
    BpmnImportStrategy Strategy, BpmnImportReportRule? Report);

public sealed record BpmnImportAttributeRule(string Name, BpmnImportValueKind ValueKind,
    BpmnImportContext Context, BpmnImportValueRule? Missing,
    ImmutableDictionary<string, BpmnImportValueRule> Values, BpmnImportValueRule? Any)
{
    public BpmnImportValueRule? Resolve(string? value) => value is null ? Missing :
        Values.GetValueOrDefault(Normalize(value, ValueKind)) ?? Any;

    internal static string Normalize(string value, BpmnImportValueKind kind) => kind switch
    {
        BpmnImportValueKind.Boolean => XmlConvert.ToBoolean(value.Trim()) ? "true" : "false",
        BpmnImportValueKind.WholeNumber => BigInteger.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => value,
    };
}

public sealed record BpmnImportElementRule(XName Name, BpmnImportValueRule? Behavior,
    ImmutableDictionary<string, BpmnImportAttributeRule> Attributes);

public sealed record BpmnImportRules(
    ImmutableDictionary<XName, BpmnImportElementRule> Elements,
    ImmutableDictionary<BpmnImportTransformation, BpmnImportValueRule> Transformations,
    BpmnImportValueRule UnmappedElement, BpmnImportValueRule UnmappedAttribute);

/// <summary>
/// Validated immutable import policy, not model validation or executable mapping logic.
/// Load fails explicitly on invalid configuration; a converter never silently substitutes a profile.
/// </summary>
public sealed partial class BpmnImportProfile
{
    public const string Namespace = "urn:inceptus:bpmn:import-profile:1";
    private static readonly Lazy<BpmnImportProfile> Bundled = new(() =>
    {
        using var stream = Resource("Bpmn202ImportProfile.xml");
        return Load(stream);
    });

    public static BpmnImportProfile Default => Bundled.Value;
    public string ProfileVersion { get; } = "1";
    public string Standard { get; } = "BPMN";
    public string StandardVersion { get; } = "2.0.2";
    public BpmnImportRules ImportRules { get; }

    private BpmnImportProfile(BpmnImportRules importRules) => ImportRules = importRules;

    public static BpmnImportProfile Load(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, ReaderSettings());
        return Read(reader);
    }

    /// <summary>Reads without closing the caller's stream.</summary>
    public static BpmnImportProfile Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream, ReaderSettings());
        return Read(reader);
    }

    private static XmlReaderSettings ReaderSettings() => new()
    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };

    private static Stream Resource(string name) => typeof(BpmnImportProfile).Assembly
        .GetManifestResourceStream("Inceptus.Bpmn.Integration." + name)
        ?? throw new InvalidOperationException("Bundled import-profile resource is missing: " + name);
}
