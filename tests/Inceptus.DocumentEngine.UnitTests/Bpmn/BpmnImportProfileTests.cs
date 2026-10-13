using System.Text;
using System.Xml.Linq;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using static Inceptus.DocumentEngine.UnitTests.Bpmn.BpmnStandardConverterTests;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnImportProfileTests
{
    private static readonly XNamespace Profile = BpmnImportProfile.Namespace;

    [Theory]
    [InlineData("Export")]
    [InlineData("Import")]
    [InlineData("ExportFallback")]
    public void ImportOnlyRootRejectsDirectionWrappersAndExportConfiguration(string child)
    {
        var xml = XDocument.Parse(ProfileText());
        Assert.Equal(Profile + "BpmnImportProfile", xml.Root!.Name);
        Assert.Equal("urn:inceptus:bpmn:import-profile:1", Profile.NamespaceName);
        Assert.NotNull(xml.Root.Element(Profile + "Fallback"));
        Assert.Null(xml.Root.Element(Profile + "Import"));
        Assert.Null(xml.Root.Element(Profile + "Export"));
        xml.Root.Add(new XElement(Profile + child));
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(xml.ToString()));
    }

    [Fact]
    public void ProfileSizeIsBoundedForBothLoadOverloads()
    {
        var xml = ProfileText().Replace("</BpmnImportProfile>", new string(' ', 1024 * 1024) + "</BpmnImportProfile>", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(xml));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(stream));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IgnoreTreatmentReportsLossOnlyWhenExplicitlyConfigured(bool attribute, bool report)
    {
        var policy = XDocument.Parse(ProfileText());
        var code = attribute ? "BPMN_IMPORT_EXECUTABLE_SEMANTICS_IGNORED" : "BPMN_IMPORT_DATA_ARTIFACT_IGNORED";
        var reporting = policy.Descendants(Profile + "Report").Single(item => (string?)item.Attribute("code") == code);
        Assert.Equal("Ignore", (string?)reporting.Parent!.Attribute("action"));
        if (!report) reporting.Remove();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(policy.ToString()));
        var profile = BpmnImportProfile.Load(stream);
        var source = Xml(attribute ? "<process id='p' isExecutable='true'><task id='a' name='A'/></process>" :
            "<process id='p'><task id='a' name='A'/><dataObject id='data'/></process>");
        var result = BpmnStandardConverter.Import(source, profile);
        Success(result);
        Assert.Equal("a", Assert.Single(result.Document!.SemanticModel.Elements).Id.Value);
        Assert.Equal(report ? 1 : 0, result.ConversionLog.Length);
        if (report) Assert.Equal(BpmnConversionDisposition.Ignored, Assert.Single(result.ConversionLog).Disposition);
        Assert.Equal(BpmnStandardConverter.Import(source).Document, result.Document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalizeTreatmentAndGeometryAreIndependentOfOptionalReport(bool report)
    {
        var policy = XDocument.Parse(ProfileText());
        var rule = policy.Descendants(Profile + "Report").Single(item => (string?)item.Attribute("code") == "BPMN_IMPORT_VERTICAL_ORIENTATION_NORMALIZED");
        Assert.Equal("Normalize", (string?)rule.Parent!.Attribute("action"));
        if (!report) rule.Remove();
        var result = await BpmnStandardConverter.ImportAsync(PoolXml("false"), Preparer(), BpmnImportProfile.Load(policy.ToString()));
        Success(result);
        Assert.Equal(report ? 1 : 0, result.ConversionLog.Count(loss => loss.Code == "BPMN_IMPORT_VERTICAL_ORIENTATION_NORMALIZED"));
        var baseline = await BpmnStandardConverter.ImportAsync(PoolXml("false"), Preparer());
        Success(baseline);
        Assert.Equal(BpmnStandardConverter.Export(baseline.Document!).Xml, BpmnStandardConverter.Export(result.Document!).Xml);
        Assert.Single(result.ConversionLog, loss => loss.Code == "BPMN_POOL_GEOMETRY_NORMALIZED");
    }

    [Fact]
    public void PoolTransformationUsesExplicitReportWithoutChangingItsNormalization()
    {
        var policy = XDocument.Parse(ProfileText());
        policy.Descendants(Profile + "Transformation").Single(item => (string?)item.Attribute("id") == "PoolLocalCoordinates")
            .Element(Profile + "Report")!.Remove();
        var imported = BpmnStandardConverter.Import(PoolXml("true"), BpmnImportProfile.Load(policy.ToString()));
        Success(imported);
        Assert.Empty(imported.ConversionLog);
        Assert.Equal(new PointD(80, 40), imported.Document!.VisualModel.VisualStates.Single(item => item.SemanticElementId.Value == "a").Position);
    }

    [Fact]
    public void AlternateImportReportsCannotChangeExportXmlOrCodeDefinedOmissions()
    {
        var source = Xml("<process id='p'><task id='a' name='A'/><dataObject id='data'/></process>");
        var first = BpmnStandardConverter.Import(source);
        Success(first);
        var before = BpmnStandardConverter.Export(first.Document!);
        Success(before);
        var profile = BpmnImportProfile.Load(ProfileText().Replace("BPMN_IMPORT_DATA_ARTIFACT_IGNORED", "HOST_DATA_LOSS", StringComparison.Ordinal));
        var second = BpmnStandardConverter.Import(source, profile);
        Success(second);
        Assert.Contains(second.ConversionLog, loss => loss.Code == "HOST_DATA_LOSS");
        var after = BpmnStandardConverter.Export(second.Document!);
        Success(after);
        Assert.Equal(before.Xml, after.Xml);
        Assert.Equal(before.ConversionLog.ToArray(), after.ConversionLog.ToArray());
        Assert.Contains(after.ConversionLog, loss => loss.Code == "BPMN_EXPORT_OMITTED" && loss.Disposition == BpmnConversionDisposition.Omitted);
        var export = Assert.Single(typeof(BpmnStandardConverter).GetMethods(), method => method.Name == "Export");
        Assert.Equal(typeof(DocumentEngine.Contracts.Documents.DocumentSnapshot), Assert.Single(export.GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(BpmnImportProfile).GetProperties(), property => property.Name == "Export");
        Assert.Equal(["Ignored"], Enum.GetNames<BpmnImportDisposition>());
        Assert.DoesNotContain("Omit", Enum.GetNames<BpmnImportAction>());
    }

    [Fact]
    public void GeometryNormalizationReportsAreConfiguredWithoutChangingGeometry()
    {
        var policy = XDocument.Parse(ProfileText());
        policy.Descendants(Profile + "Transformation").Elements(Profile + "Report").Remove();
        var source = Xml(DiExample.Replace("x=\"40\"", "x=\"-40\"", StringComparison.Ordinal));
        var baseline = BpmnStandardConverter.Import(source);
        var silent = BpmnStandardConverter.Import(source, BpmnImportProfile.Load(policy.ToString()));
        Success(baseline); Success(silent);
        Assert.Contains(baseline.ConversionLog, loss => loss.Code == "BPMN_DI_TRANSLATED");
        Assert.DoesNotContain(silent.ConversionLog, loss => loss.Code is "BPMN_DI_TRANSLATED" or "BPMN_BOUNDARY_SNAPPED" or "BPMN_ANCHOR_NORMALIZED" or "BPMN_PATH_NORMALIZED");
        Assert.Equal(baseline.Document, silent.Document);
    }

    [Theory]
    [InlineData("process", "48EB290494ECA2AEB9FD414D286133D0")]
    [InlineData("subprocess", "A71D577765F7C578A459A1FA6CA79266")]
    [InlineData("pool", "2DBB4A6318E71679C347B2BE6216D0C2")]
    public async Task A32ExportXmlAndDiagnosticsRetainPreRefinementBaseline(string scenario, string expected)
    {
        var xml = scenario switch
        {
            "pool" => PoolXml("true"),
            "subprocess" => Xml("<process id='p'><subProcess id='sub' name='Nested'><startEvent id='start'/><task id='work' name='Work'/><sequenceFlow id='flow' sourceRef='start' targetRef='work'/></subProcess></process>"),
            _ => Xml("<process id='p'><startEvent id='start'/><userTask id='work' name='Work'><documentation>Instructions</documentation></userTask><endEvent id='end'/><sequenceFlow id='first' sourceRef='start' targetRef='work'/><sequenceFlow id='last' sourceRef='work' targetRef='end'/></process>"),
        };
        var imported = await BpmnStandardConverter.ImportAsync(xml, Preparer());
        Success(imported);
        var exported = BpmnStandardConverter.Export(imported.Document!);
        Success(exported);
        var baseline = exported.Xml!.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n" +
            System.Text.Json.JsonSerializer.Serialize(new { exported.ConversionLog, exported.Diagnostics });
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(baseline)));
        // First 128 bits of the XML + structured log/diagnostics baseline captured before the import-only refinement.
        Assert.Equal(expected, fingerprint[..32]);
    }

    [Fact]
    public void A32ShippedProfileValidatesAgainstEmbeddedSchemaAndLoadsOnce()
    {
        var profile = BpmnImportProfile.Load(ProfileText());
        Assert.Equal("1", profile.ProfileVersion);
        Assert.Equal("2.0.2", profile.StandardVersion);
        Assert.NotEmpty(profile.ImportRules.Elements);
        Assert.Equal("BPMN", profile.Standard);
        Assert.Same(BpmnImportProfile.Default, BpmnImportProfile.Default);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ProfileText()));
        Assert.Equal(profile.ImportRules.Elements.Count, BpmnImportProfile.Load(stream).ImportRules.Elements.Count);
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("action=\"AcceptMetadata\"", "action=\"ExecuteScript\"")]
    [InlineData("disposition=\"Ignored\"", "disposition=\"Warning\"")]
    [InlineData("bpmn:definitions", "bad:definitions")]
    [InlineData("bpmn:definitions", "bpmn:madeUpElement")]
    [InlineData("http://www.omg.org/spec/BPMN/20100524/MODEL", "urn:unknown:model")]
    [InlineData("version=\"1\"", "version=\"2\"")]
    [InlineData("standardVersion=\"2.0.2\"", "standardVersion=\"2.0\"")]
    [InlineData("targetValue=\"PoolLocal\"", "targetValue=\"RotateAndRepartition\"")]
    [InlineData("scope=\"Owner\"", "scope=\"Anything\"")]
    [InlineData("action=\"AcceptMetadata\"", "action=\"Omit\"")]
    [InlineData("disposition=\"Ignored\"", "disposition=\"Omitted\"")]
    [InlineData("disposition=\"Ignored\"", "disposition=\"None\"")]
    [InlineData("{element}", "{runCode()}")]
    [InlineData("name=\"exporter\"", "name=\"id\"")]
    [InlineData("valueKind=\"WholeNumber\"", "valueKind=\"Boolean\"")]
    public void A32InvalidProfileIsRejectedExplicitly(string before, string after)
    {
        var exception = Assert.Throws<FormatException>(() => BpmnImportProfile.Load(ProfileText().Replace(before, after, StringComparison.Ordinal)));
        Assert.Contains("Invalid BPMN import profile", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<broken")]
    [InlineData("<wrongRoot/>")]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///private'>]><x>&e;</x>")]
    public void A32MalformedAndExternalEntityProfilesAreRejected(string xml) =>
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(xml));

    [Fact]
    public void A32ConflictingDuplicateRulesAndBooleanAliasesAreRejected()
    {
        var xml = XDocument.Parse(ProfileText());
        var attribute = xml.Descendants(Profile + "Attribute").First();
        attribute.AddAfterSelf(new XElement(attribute));
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(xml.ToString()));
        xml = XDocument.Parse(ProfileText());
        attribute = xml.Descendants(Profile + "Attribute").Single(item => (string?)item.Attribute("name") == "isExecutable");
        attribute.Element(Profile + "Value")!.AddAfterSelf(new XElement(Profile + "Value", new XAttribute("value", "0"), new XAttribute("action", "AcceptDefault")));
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(xml.ToString()));
    }

    [Fact]
    public void A32FallbackCannotClaimAggregationWithoutAnOwningContext()
    {
        var xml = XDocument.Parse(ProfileText());
        xml.Descendants(Profile + "Fallback").First().Descendants(Profile + "Report").First().SetAttributeValue("scope", "Document");
        Assert.Throws<FormatException>(() => BpmnImportProfile.Load(xml.ToString()));
    }

    [Fact]
    public void A32AlternateProfileControlsStableLossCodesAndQNamePrefixesAreIrrelevant()
    {
        var profile = BpmnImportProfile.Load(ProfileText().Replace("BPMN_IMPORT_DATA_ARTIFACT_IGNORED", "HOST_DATA_LOSS", StringComparison.Ordinal));
        var xml = Xml("<process id='p'><task id='a' name='A'/><dataObject id='data'/></process>");
        var parsed = XDocument.Parse(xml);
        parsed.Root!.Add(new XAttribute(XNamespace.Xmlns + "different", parsed.Root.Name.NamespaceName));
        Assert.Contains("<different:", parsed.ToString(), StringComparison.Ordinal);
        var result = BpmnStandardConverter.Import(parsed.ToString(), profile);
        Success(result);
        Assert.Contains(result.ConversionLog, loss => loss.Code == "HOST_DATA_LOSS" && loss.SourceIdentity == "data");
        Assert.DoesNotContain(result.ConversionLog, loss => loss.Code == "BPMN_IMPORT_DATA_ARTIFACT_IGNORED");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    public void A32ExecutableDefaultsAreNotLossesButExecutionSemanticsAre(string? value, bool loss)
    {
        var result = BpmnStandardConverter.Import(Xml($"<process id='p' {Attribute("isExecutable", value)}><task id='a' name='A'/></process>"));
        Success(result);
        Assert.Single(result.Document!.SemanticModel.Elements);
        Assert.Equal(loss ? 1 : 0, result.ConversionLog.Count(entry => entry.Property == "isExecutable"));
        if (loss) Assert.Contains(result.ConversionLog, entry => entry.Code == "BPMN_IMPORT_EXECUTABLE_SEMANTICS_IGNORED");
    }

    [Theory]
    [InlineData("process", "isClosed", "false", false)]
    [InlineData("process", "isClosed", "true", true)]
    [InlineData("process", "processType", "None", false)]
    [InlineData("process", "processType", "Private", true)]
    [InlineData("task", "isForCompensation", "false", false)]
    [InlineData("task", "isForCompensation", "true", true)]
    [InlineData("task", "startQuantity", "1", false)]
    [InlineData("task", "startQuantity", "+01", false)]
    [InlineData("task", "startQuantity", "2", true)]
    [InlineData("task", "completionQuantity", "1", false)]
    [InlineData("task", "completionQuantity", "3", true)]
    [InlineData("subProcess", "triggeredByEvent", "false", false)]
    [InlineData("subProcess", "triggeredByEvent", "true", true)]
    [InlineData("eventBasedGateway", "instantiate", "false", false)]
    [InlineData("eventBasedGateway", "instantiate", "true", true)]
    [InlineData("eventBasedGateway", "eventGatewayType", "Exclusive", false)]
    [InlineData("eventBasedGateway", "eventGatewayType", "Parallel", true)]
    public void A32DefaultAndNonDefaultValuesHaveConfiguredClassification(string type, string attribute, string value, bool loss)
    {
        var node = $"<{type} id='a' name='A' {Attribute(attribute, value)}/>";
        var result = BpmnStandardConverter.Import(Xml(type == "process" ? node : "<process id='p'>" + node + "</process>"));
        Success(result);
        Assert.Equal(loss ? 1 : 0, result.ConversionLog.Count(entry => entry.Property == attribute));
        if (type != "process") Assert.Single(result.Document!.SemanticModel.Elements);
    }

    [Fact]
    public void A32ExporterMetadataXmlMechanicsAndNativeGeneratedDefaultsHaveNoLossOrRoutineWarning()
    {
        var xml = Xml("<process id='p'><task id='task' name='Work'/></process>")
            .Replace("targetNamespace=\"urn:test\"", "targetNamespace=\"urn:test\" exporter=\"Example\" exporterVersion=\"1\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"urn:test unreachable.xsd\"", StringComparison.Ordinal);
        var first = BpmnStandardConverter.Import(xml);
        var second = BpmnStandardConverter.Import(xml);
        Success(first); Success(second);
        Assert.Empty(first.ConversionLog);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Code == "BPMN_NATIVE_DEFAULTS");
        var before = Assert.Single(first.Document!.SemanticModel.Elements);
        var after = Assert.Single(second.Document!.SemanticModel.Elements);
        Assert.Equal("task", before.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(before.Properties[BpmnSemanticProperties.Code], after.Properties[BpmnSemanticProperties.Code]);
        Assert.Equal(before.Properties[BpmnSemanticProperties.ElementNumber], after.Properties[BpmnSemanticProperties.ElementNumber]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("true", false)]
    [InlineData("1", false)]
    [InlineData("false", true)]
    [InlineData("0", true)]
    public async Task A32OrientationUsesExistingHorizontalPresentationAndReportsOnlyVerticalLoss(string? horizontal, bool loss)
    {
        var result = await BpmnStandardConverter.ImportAsync(PoolXml(horizontal), Preparer());
        Success(result);
        Assert.Equal(loss ? 1 : 0, result.ConversionLog.Count(entry => entry.Code == "BPMN_IMPORT_VERTICAL_ORIENTATION_NORMALIZED"));
        var export = BpmnStandardConverter.Export(result.Document!);
        Success(export);
        var poolShape = XDocument.Parse(export.Xml!).Descendants().Single(item => item.Name.LocalName == "BPMNShape" && (string?)item.Attribute("bpmnElement") == "pool");
        Assert.Equal("true", (string?)poolShape.Attribute("isHorizontal"));
        Assert.All(result.ConversionLog, entry => Assert.Equal(BpmnConversionDisposition.Ignored, entry.Disposition));
    }

    [Fact]
    public void A32PoolNormalizationIsReportedOncePerOwnerWithUnchangedLocalGeometry()
    {
        var result = BpmnStandardConverter.Import(PoolXml("true"));
        Success(result);
        var report = Assert.Single(result.ConversionLog, entry => entry.Code == "BPMN_POOL_GEOMETRY_NORMALIZED");
        Assert.Equal("pool", report.SourceIdentity);
        Assert.Equal("Pool placement", report.Property);
        Assert.Contains("Warehouse", report.Message, StringComparison.Ordinal);
        Assert.Equal(new PointD(80, 40), result.Document!.VisualModel.VisualStates.Single(item => item.SemanticElementId.Value == "a").Position);
        Assert.Equal(new PointD(280, 40), result.Document.VisualModel.VisualStates.Single(item => item.SemanticElementId.Value == "b").Position);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("text/plain", false)]
    [InlineData("text/html", true)]
    public void A32DocumentationPreservesTextAndReportsOnlyFormattingLoss(string? format, bool loss)
    {
        var result = BpmnStandardConverter.Import(Xml($"<process id='p'><task id='a' name='A'><documentation {Attribute("textFormat", format)}>Description</documentation></task></process>"));
        Success(result);
        Assert.Equal("Description", Assert.Single(result.Document!.SemanticModel.Elements).Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.Equal(loss ? 1 : 0, result.ConversionLog.Count(entry => entry.Code == "BPMN_IMPORT_DOCUMENTATION_FORMAT_IGNORED"));
    }

    [Theory]
    [InlineData("scriptTask", "BPMN.Task")]
    [InlineData("businessRuleTask", "BPMN.Task")]
    [InlineData("callActivity", "BPMN.Task")]
    [InlineData("transaction", "BPMN.SubProcess")]
    [InlineData("adHocSubProcess", "BPMN.SubProcess")]
    public void A32ExistingDegradationsAreProfileDriven(string xmlType, string nativeType)
    {
        var result = BpmnStandardConverter.Import(Xml($"<process id='p'><{xmlType} id='a' name='A'/></process>"));
        Success(result);
        Assert.Equal(nativeType, Assert.Single(result.Document!.SemanticModel.Elements).TypeId.Value);
        Assert.Single(result.ConversionLog, entry => entry.Code is "BPMN_IMPORT_ACTIVITY_DEGRADED" or "BPMN_IMPORT_SUBPROCESS_DEGRADED");
    }

    [Theory]
    [InlineData(null, 0, 0, false)]
    [InlineData("Unspecified", 0, 0, false)]
    [InlineData("Diverging", 1, 2, false)]
    [InlineData("Converging", 2, 1, false)]
    [InlineData("Mixed", 2, 2, false)]
    [InlineData("Diverging", 2, 1, true)]
    [InlineData("Mixed", 1, 1, true)]
    public void A32GatewayDirectionIsAcceptedOnlyWhenDerivedFromRetainedTopology(string? direction, int incoming, int outgoing, bool loss)
    {
        var nodes = "<task id='a' name='A'/><task id='b' name='B'/>";
        var flows = string.Concat(Enumerable.Range(0, incoming).Select(i => $"<sequenceFlow id='in{i}' sourceRef='{(i == 0 ? "a" : "b")}' targetRef='g'/>")) +
            string.Concat(Enumerable.Range(0, outgoing).Select(i => $"<sequenceFlow id='out{i}' sourceRef='g' targetRef='{(i == 0 ? "a" : "b")}'/>"));
        var result = BpmnStandardConverter.Import(Xml($"<process id='p'>{nodes}<exclusiveGateway id='g' name='G' {Attribute("gatewayDirection", direction)}/>{flows}</process>"));
        Success(result);
        Assert.Equal(loss ? 1 : 0, result.ConversionLog.Count(entry => entry.Code == "BPMN_IMPORT_GATEWAY_DIRECTION_IGNORED"));
    }

    private static string Attribute(string name, string? value) => value is null ? string.Empty : $"{name}='{value}'";

    [Theory]
    [InlineData("task")]
    [InlineData("subProcess")]
    [InlineData("eventBasedGateway")]
    public void A32MissingActivityAndGatewayDefaultsAreNotLosses(string type)
    {
        var result = BpmnStandardConverter.Import(Xml($"<process id='p'><{type} id='node' name='Node'/></process>"));
        Success(result);
        Assert.Empty(result.ConversionLog);
    }

    [Fact]
    public async Task A32LaneOrientationAndAsyncAlternateProfileUseTheSamePolicy()
    {
        var xml = PoolXml("false").Replace("<task id=\"a\"", "<laneSet id=\"ls\"><lane id=\"lane\" name=\"Warehouse\"><flowNodeRef>a</flowNodeRef><flowNodeRef>b</flowNodeRef></lane></laneSet><task id=\"a\"", StringComparison.Ordinal)
            .Replace("bpmnElement=\"pool\"", "bpmnElement=\"lane\"", StringComparison.Ordinal);
        var profile = BpmnImportProfile.Load(ProfileText().Replace("BPMN_IMPORT_VERTICAL_ORIENTATION_NORMALIZED", "HOST_VERTICAL_LOSS", StringComparison.Ordinal));
        var result = await BpmnStandardConverter.ImportAsync(xml, Preparer(), profile);
        Success(result);
        Assert.Single(result.ConversionLog, loss => loss.Code == "HOST_VERTICAL_LOSS");
        var export = BpmnStandardConverter.Export(result.Document!);
        Success(export);
        Assert.Contains(XDocument.Parse(export.Xml!).Descendants(), item => (string?)item.Attribute("bpmnElement") == "lane" && (string?)item.Attribute("isHorizontal") == "true");
    }

    [Fact]
    public void A32AnonymousLossesHaveDistinctLocationsAndDoNotInterpretSourceTextAsTemplates()
    {
        var result = BpmnStandardConverter.Import(Xml("<process id='p'><task id='a' name='A'><documentation textFormat='text/html'>One</documentation><documentation textFormat='text/html'>Two</documentation></task><dataObject name='{message}'/><dataObject/></process>"));
        Success(result);
        var descriptions = result.ConversionLog.Where(loss => loss.Code == "BPMN_IMPORT_DOCUMENTATION_FORMAT_IGNORED").ToArray();
        Assert.Equal(2, descriptions.Length);
        Assert.Equal(2, descriptions.Select(loss => loss.SourceIdentity).Distinct().Count());
        Assert.Equal(2, result.ConversionLog.Count(loss => loss.Code == "BPMN_IMPORT_DATA_ARTIFACT_IGNORED"));
        var literal = BpmnStandardConverter.Import(PoolXml("true").Replace("Warehouse", "{message}", StringComparison.Ordinal));
        Success(literal);
        Assert.Contains(literal.ConversionLog, entry => entry.Code == "BPMN_POOL_GEOMETRY_NORMALIZED" && entry.Message.Contains("'{message}'", StringComparison.Ordinal));
    }
    internal static string ProfileText()
    {
        using var stream = typeof(BpmnImportProfile).Assembly.GetManifestResourceStream("Inceptus.Bpmn.Integration.Bpmn202ImportProfile.xml")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    internal static string PoolXml(string? horizontal) => Xml($"""
        <process id="p" isExecutable="false"><task id="a" name="A"/><task id="b" name="B"/></process>
        <collaboration id="c"><participant id="pool" name="Warehouse" processRef="p"/></collaboration>
        <bpmndi:BPMNDiagram id="diagram"><bpmndi:BPMNPlane id="plane" bpmnElement="c">
          <bpmndi:BPMNShape id="poolShape" bpmnElement="pool" {Attribute("isHorizontal", horizontal)}><dc:Bounds x="100" y="100" width="600" height="200"/></bpmndi:BPMNShape>
          <bpmndi:BPMNShape id="aShape" bpmnElement="a"><dc:Bounds x="180" y="140" width="140" height="80"/></bpmndi:BPMNShape>
          <bpmndi:BPMNShape id="bShape" bpmnElement="b"><dc:Bounds x="380" y="140" width="140" height="80"/></bpmndi:BPMNShape>
        </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        """);
}
