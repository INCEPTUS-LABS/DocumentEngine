using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Inceptus.Bpmn.Integration;

public sealed partial class BpmnImportProfile
{
    private static readonly XNamespace Profile = Namespace;
    private static readonly Lazy<XmlSchemaSet> ProfileSchema = new(() =>
    {
        using var stream = Resource("Bpmn202ImportProfile.xsd");
        using var reader = XmlReader.Create(stream, ReaderSettings());
        var schemas = new XmlSchemaSet { XmlResolver = null };
        schemas.Add(Namespace, reader);
        schemas.Compile();
        return schemas;
    });

    private static BpmnImportProfile Read(XmlReader reader)
    {
        try
        {
            var document = XDocument.Load(reader);
            if (document.Root?.Name != Profile + "BpmnImportProfile") throw Invalid("A BpmnImportProfile root in the versioned import-profile namespace is required.");
            document.Validate(ProfileSchema.Value, null);
            return new(ReadRules(document.Root!));
        }
        catch (Exception exception) when (exception is XmlException or XmlSchemaException or ArgumentException or FormatException or OverflowException)
        { throw new FormatException("Invalid BPMN import profile: " + exception.Message, exception); }
    }

    private static BpmnImportRules ReadRules(XElement root)
    {
        var elements = new Dictionary<XName, BpmnImportElementRule>();
        foreach (var entry in root.Elements(Profile + "Element"))
        {
            foreach (var token in Required(entry, "qnames").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = QName(entry, token);
                if (!BpmnXmlSchema.IsKnownElement(name)) throw Invalid("Unknown BPMN element QName: " + name);
                elements.TryGetValue(name, out var previous);
                var behavior = entry.Element(Profile + "Behavior") is { } behaviorXml ? Rule(behaviorXml) : null;
                if (behavior is not null)
                {
                    if (previous?.Behavior is not null) throw Invalid("Duplicate element behavior: " + name);
                    ValidateBehavior(name, behavior);
                }
                var attributes = previous?.Attributes.ToBuilder() ?? ImmutableDictionary.CreateBuilder<string, BpmnImportAttributeRule>(StringComparer.Ordinal);
                foreach (var attribute in entry.Elements(Profile + "Attribute"))
                {
                    var attributeName = Required(attribute, "name");
                    if (!BpmnXmlSchema.HasAttribute(name, attributeName)) throw Invalid($"Unknown attribute {name}/@{attributeName}.");
                    if (attributeName is "id" or "name" or "targetNamespace" or "processRef" or "bpmnElement" or "sourceRef" or "targetRef" or
                        "attachedToRef" or "cancelActivity" or "x" or "y" or "width" or "height")
                        throw Invalid("The converter directly maps this structural/geometry attribute; a profile cannot override its binding: " + attributeName);
                    if (attributes.ContainsKey(attributeName)) throw Invalid($"Duplicate attribute rule {name}/@{attributeName}.");
                    var kind = EnumValue<BpmnImportValueKind>(attribute, "valueKind");
                    if (kind != AttributeValueKind(name, attributeName)) throw Invalid("valueKind disagrees with the BPMN schema for " + attributeName);
                    var context = EnumValue<BpmnImportContext>(attribute, "context");
                    if (context == BpmnImportContext.PoolOrLane && (name != BpmnXmlVocabulary.BpmnDi + "BPMNShape" || attributeName != "isHorizontal"))
                        throw Invalid("PoolOrLane context is only valid for BPMNShape.isHorizontal.");
                    var missing = attribute.Element(Profile + "Missing") is { } missingXml ? Rule(missingXml) : null;
                    if (missing is not null && (missing.Action != BpmnImportAction.AcceptDefault || missing.TargetValue is null || missing.Report is not null))
                        throw Invalid("A missing value requires AcceptDefault, a targetValue and no loss report.");
                    if (missing?.TargetValue is { } defaultValue) _ = BpmnImportAttributeRule.Normalize(defaultValue, kind);
                    var values = ImmutableDictionary.CreateBuilder<string, BpmnImportValueRule>(StringComparer.Ordinal);
                    foreach (var value in attribute.Elements(Profile + "Value"))
                    {
                        var key = BpmnImportAttributeRule.Normalize(Required(value, "value"), kind);
                        if (values.ContainsKey(key)) throw Invalid("Duplicate normalized attribute value: " + key);
                        var valueRule = Rule(value);
                        if (valueRule.Action == BpmnImportAction.Map && key != "text/plain") throw Invalid("Only plain documentation text has a configurable Map binding.");
                        values.Add(key, valueRule);
                    }
                    var any = attribute.Element(Profile + "Any") is { } anyXml ? Rule(anyXml) : null;
                    if (any?.Action == BpmnImportAction.Map) throw Invalid("Map requires an explicitly supported source value.");
                    if (missing is null && values.Count == 0 && any is null) throw Invalid("Empty attribute rule: " + attributeName);
                    foreach (var rule in values.Values.Concat(new[] { missing, any }.OfType<BpmnImportValueRule>()))
                    {
                        if (rule.TargetValue is { } targetValue) _ = BpmnImportAttributeRule.Normalize(targetValue, kind);
                        if (rule.Strategy == BpmnImportStrategy.DerivedFromTopology &&
                            (attributeName != "gatewayDirection" || !BpmnXmlVocabulary.Nodes.Any(mapping => name == BpmnXmlVocabulary.Model + mapping.Element &&
                                DocumentEngine.Bpmn.Semantics.BpmnGatewaySemanticTypes.IsGateway(mapping.Type))))
                            throw Invalid("DerivedFromTopology is only supported for mapped gateways' gatewayDirection.");
                        if (rule.Action == BpmnImportAction.Map && (name != BpmnXmlVocabulary.Model + "documentation" || attributeName != "textFormat"))
                            throw Invalid("This attribute has no configurable Map binding: " + attributeName);
                        if (rule.Action == BpmnImportAction.Degrade && (name != BpmnXmlVocabulary.Model + "eventBasedGateway" || attributeName != "eventGatewayType"))
                            throw Invalid("No existing attribute degradation binding for " + attributeName);
                        if (rule.Action == BpmnImportAction.Normalize &&
                            !(name == BpmnXmlVocabulary.Model + "documentation" && attributeName == "textFormat" && rule.TargetValue == "text/plain") &&
                            !(name == BpmnXmlVocabulary.BpmnDi + "BPMNShape" && attributeName == "isHorizontal" && rule.TargetValue == "true"))
                            throw Invalid("No existing normalization binding for " + attributeName);
                    }
                    attributes.Add(attributeName, new(attributeName, kind, context, missing, values.ToImmutable(), any));
                }
                elements[name] = new(name, behavior ?? previous?.Behavior, attributes.ToImmutable());
            }
        }
        var transformations = ImmutableDictionary.CreateBuilder<BpmnImportTransformation, BpmnImportValueRule>();
        foreach (var node in root.Elements(Profile + "Transformation"))
        {
            var id = EnumValue<BpmnImportTransformation>(node, "id");
            var rule = Rule(node);
            var target = id switch
            {
                BpmnImportTransformation.PoolLocalCoordinates => "PoolLocal",
                BpmnImportTransformation.DiagramOrigin => "Nonnegative",
                BpmnImportTransformation.BoundaryPosition => "Attachment",
                BpmnImportTransformation.ConnectorEndpoint => "Anchor",
                _ => "PreparedRoute",
            };
            if (rule.Action != BpmnImportAction.Normalize || rule.TargetValue != target) throw Invalid($"{id} requires Normalize to {target}.");
            if (transformations.ContainsKey(id)) throw Invalid("Duplicate transformation: " + id);
            transformations.Add(id, rule);
        }
        var fallback = root.Element(Profile + "Fallback")!;
        var fallbackElement = Rule(fallback.Element(Profile + "Element")!);
        var fallbackAttribute = Rule(fallback.Element(Profile + "Attribute")!);
        if (fallbackElement.Action != BpmnImportAction.Ignore || fallbackAttribute.Action != BpmnImportAction.Ignore) throw Invalid("Fallback must report unmapped source information as a loss.");
        if (fallbackElement.Report?.Scope != BpmnImportReportScope.Element || fallbackAttribute.Report?.Scope != BpmnImportReportScope.Element)
            throw Invalid("Fallback reports require Element scope; owner/diagram/document aggregation requires a contextual rule.");
        foreach (var transformation in Enum.GetValues<BpmnImportTransformation>())
            if (!transformations.ContainsKey(transformation)) throw Invalid($"The existing {transformation} transformation requires a treatment rule.");
        return new(elements.ToImmutableDictionary(), transformations.ToImmutable(), fallbackElement, fallbackAttribute);
    }

    private static BpmnImportValueRule Rule(XElement node)
    {
        var action = EnumValue<BpmnImportAction>(node, "action");
        var strategy = EnumValue<BpmnImportStrategy>(node, "strategy");
        if ((action == BpmnImportAction.AcceptDerived) != (strategy == BpmnImportStrategy.DerivedFromTopology))
            throw Invalid("AcceptDerived requires the DerivedFromTopology strategy.");
        BpmnImportReportRule? report = null;
        if (node.Element(Profile + "Report") is { } reportXml)
        {
            var disposition = EnumValue<BpmnImportDisposition>(reportXml, "disposition");
            var code = Required(reportXml, "code");
            var message = reportXml.Value.Trim();
            if (string.IsNullOrWhiteSpace(message)) throw Invalid("Loss reports require a message.");
            var remainder = message;
            foreach (var token in new[] { "element", "property", "value", "source", "name", "message" }) remainder = remainder.Replace("{" + token + "}", string.Empty, StringComparison.Ordinal);
            if (remainder.Contains('{', StringComparison.Ordinal) || remainder.Contains('}', StringComparison.Ordinal)) throw Invalid("Unknown report placeholder.");
            report = new(disposition, code, message, EnumValue<BpmnImportReportScope>(reportXml, "scope"));
        }
        // Treatment never implies loss: only an explicit Report opts into loss reporting.
        return new(action, (string?)node.Attribute("targetValue"), strategy, report);
    }

    private static void ValidateBehavior(XName name, BpmnImportValueRule rule)
    {
        if (rule.Action == BpmnImportAction.Ignore && name.Namespace == BpmnXmlVocabulary.Model && name.LocalName is
            "dataObject" or "dataObjectReference" or "dataStore" or "dataStoreReference" or "dataInput" or "dataOutput" or
            "dataInputAssociation" or "dataOutputAssociation" or "itemDefinition" or "group" or "textAnnotation" or "association" or
            "scriptTask" or "businessRuleTask" or "callActivity" or "transaction" or "adHocSubProcess") return;
        if (rule.Action == BpmnImportAction.Degrade && name.Namespace == BpmnXmlVocabulary.Model &&
            (rule.TargetValue == "task" && name.LocalName is "scriptTask" or "businessRuleTask" or "callActivity" ||
             rule.TargetValue == "subProcess" && name.LocalName is "transaction" or "adHocSubProcess")) return;
        throw Invalid("No existing element construction binding for " + name + ": " + rule.Action);
    }

    private static BpmnImportValueKind AttributeValueKind(XName name, string attribute) => BpmnXmlSchema.AttributeTypeCode(name, attribute) switch
    {
        XmlTypeCode.Boolean => BpmnImportValueKind.Boolean,
        XmlTypeCode.Integer or XmlTypeCode.NonPositiveInteger or XmlTypeCode.NegativeInteger or XmlTypeCode.Long or
        XmlTypeCode.Int or XmlTypeCode.Short or XmlTypeCode.Byte or XmlTypeCode.NonNegativeInteger or XmlTypeCode.UnsignedLong or
        XmlTypeCode.UnsignedInt or XmlTypeCode.UnsignedShort or XmlTypeCode.UnsignedByte or XmlTypeCode.PositiveInteger => BpmnImportValueKind.WholeNumber,
        _ => BpmnImportValueKind.Text,
    };

    private static XName QName(XElement element, string value)
    {
        var parts = value.Split(':');
        var ns = parts.Length == 2 ? element.GetNamespaceOfPrefix(parts[0]) : element.GetDefaultNamespace();
        if (ns is null) throw Invalid("Unresolved QName prefix: " + value);
        return ns + parts[^1];
    }
    private static T EnumValue<T>(XElement node, string name) where T : struct, Enum =>
        node.Attribute(name) is { } attribute ? Enum.Parse<T>(attribute.Value) : default;
    private static string Required(XElement node, string name) => (string?)node.Attribute(name) ?? throw Invalid("Missing field: " + name);
    private static FormatException Invalid(string message) => new(message);
}
