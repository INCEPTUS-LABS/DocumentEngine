using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Inceptus.Bpmn.Integration;

/// <summary>Unmodified normative OMG schemas, embedded for deterministic offline validation.</summary>
internal static class BpmnXmlSchema
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(Create);
    internal static bool IsKnownElement(XName name) => Schemas.Value.GlobalElements.Contains(new XmlQualifiedName(name.LocalName, name.NamespaceName));
    internal static bool HasAttribute(XName name, string attribute) =>
        Schemas.Value.GlobalElements[new XmlQualifiedName(name.LocalName, name.NamespaceName)] is XmlSchemaElement element &&
        element.ElementSchemaType is XmlSchemaComplexType type && type.AttributeUses.Contains(new XmlQualifiedName(attribute));
    internal static XmlTypeCode AttributeTypeCode(XName name, string attribute)
    {
        var element = (XmlSchemaElement)Schemas.Value.GlobalElements[new XmlQualifiedName(name.LocalName, name.NamespaceName)]!;
        var type = (XmlSchemaComplexType)element.ElementSchemaType!;
        var property = (XmlSchemaAttribute)type.AttributeUses[new XmlQualifiedName(attribute)]!;
        return property.AttributeSchemaType!.TypeCode;
    }
    internal static bool Validate(XDocument document, BpmnConversionLog log)
    {
        var valid = true;
        document.Validate(Schemas.Value, (_, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
            {
                valid = false;
                log.Error("BPMN_XML_SCHEMA", args.Message);
            }
            else log.Add(BpmnConversionDisposition.Warning, "BPMN_XML_SCHEMA", args.Message);
        });
        return valid;
    }

    private static XmlSchemaSet Create()
    {
        var schemas = new Dictionary<string, XmlSchema>(StringComparer.Ordinal);
        foreach (var name in new[] { "BPMN20.xsd", "Semantic.xsd", "BPMNDI.xsd", "DI.xsd", "DC.xsd" })
        {
            using var stream = typeof(BpmnXmlSchema).Assembly.GetManifestResourceStream("Inceptus.Bpmn.Integration.Schemas." + name)
                ?? throw new InvalidOperationException("Bundled BPMN schema is missing: " + name);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            schemas.Add(name, XmlSchema.Read(reader, null) ?? throw new InvalidOperationException("Bundled BPMN schema is invalid."));
        }
        foreach (var schema in schemas.Values)
            foreach (XmlSchemaExternal reference in schema.Includes)
                reference.Schema = schemas[reference.SchemaLocation!];
        var set = new XmlSchemaSet { XmlResolver = null };
        set.Add(schemas["BPMN20.xsd"]);
        set.Compile();
        return set;
    }
}
