using System.Xml.Linq;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class BpmnStandardConverterTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace BpmnDi = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace Di = "http://www.omg.org/spec/DD/20100524/DI";
    private static readonly XNamespace Dc = "http://www.omg.org/spec/DD/20100524/DC";

    [Theory]
    [InlineData("startEvent", "BPMN.StartEvent")]
    [InlineData("endEvent", "BPMN.EndEvent")]
    [InlineData("task", "BPMN.Task")]
    [InlineData("userTask", "BPMN.UserTask")]
    [InlineData("manualTask", "BPMN.ManualTask")]
    [InlineData("serviceTask", "BPMN.ServiceTask")]
    [InlineData("sendTask", "BPMN.SendTask")]
    [InlineData("receiveTask", "BPMN.ReceiveTask")]
    [InlineData("exclusiveGateway", "BPMN.ExclusiveGateway")]
    [InlineData("parallelGateway", "BPMN.ParallelGateway")]
    [InlineData("inclusiveGateway", "BPMN.InclusiveGateway")]
    [InlineData("eventBasedGateway", "BPMN.EventBasedGateway")]
    [InlineData("subProcess", "BPMN.SubProcess")]
    public void SupportedKindsNamesAndDocumentationSurviveCommonSubsetRoundTrip(string xmlType, string nativeType)
    {
        var imported = Import($"<process id='p'><{xmlType} id='node' name='A &amp; B'><documentation>Details</documentation></{xmlType}></process>");
        var node = Assert.Single(imported.SemanticModel.Elements);
        Assert.Equal(new SemanticTypeId(nativeType), node.TypeId);
        Assert.Equal("A & B", node.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal("Details", node.Properties[BpmnSemanticProperties.Description].TextValue);
        var exported = BpmnStandardConverter.Export(imported);
        Success(exported);
        Assert.Single(XDocument.Parse(exported.Xml!).Descendants(Bpmn + xmlType));
        var roundTrip = BpmnStandardConverter.Import(exported.Xml!);
        Success(roundTrip);
        var final = Assert.Single(roundTrip.Document!.SemanticModel.Elements);
        Assert.Equal(node.TypeId, final.TypeId);
        Assert.Equal(node.Properties[BpmnSemanticProperties.Name], final.Properties[BpmnSemanticProperties.Name]);
        Assert.Equal(node.Properties[BpmnSemanticProperties.Description], final.Properties[BpmnSemanticProperties.Description]);
    }

    [Theory]
    [InlineData("intermediateCatchEvent", "messageEventDefinition", "BPMN.MessageCatchEvent")]
    [InlineData("intermediateThrowEvent", "messageEventDefinition", "BPMN.MessageThrowEvent")]
    [InlineData("intermediateCatchEvent", "signalEventDefinition", "BPMN.SignalCatchEvent")]
    [InlineData("intermediateThrowEvent", "signalEventDefinition", "BPMN.SignalThrowEvent")]
    [InlineData("intermediateCatchEvent", "timerEventDefinition", "BPMN.TimerCatchEvent")]
    public void IntermediateEventsKeepTheirSupportedDefinition(string eventKind, string definition, string native)
    {
        var document = Import($"<process id='p'><{eventKind} id='event'><{definition}/></{eventKind}></process>");
        Assert.Equal(native, Assert.Single(document.SemanticModel.Elements).TypeId.Value);
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.Single(XDocument.Parse(exported.Xml!).Descendants(Bpmn + eventKind).Elements(Bpmn + definition));
    }

    [Theory]
    [InlineData("timerEventDefinition", "BPMN.TimerBoundaryEvent")]
    [InlineData("messageEventDefinition", "BPMN.MessageBoundaryEvent")]
    [InlineData("signalEventDefinition", "BPMN.SignalBoundaryEvent")]
    public void BoundaryEventsRetainOwnerInterruptingFlagAndSafeGeometry(string definition, string native)
    {
        var document = Import($"<process id='p'><task id='owner'/><boundaryEvent id='boundary' attachedToRef='owner' cancelActivity='false'><{definition}/></boundaryEvent></process>");
        var boundary = document.SemanticModel.Elements.Single(element => element.Id.Value == "boundary");
        Assert.Equal(native, boundary.TypeId.Value);
        Assert.Equal(new SemanticElementId("owner"), boundary.AttachedToElementId);
        Assert.False(boundary.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.NotNull(document.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == boundary.Id).BoundaryAttachment);
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.Equal("false", (string?)XDocument.Parse(exported.Xml!).Descendants(Bpmn + "boundaryEvent").Single().Attribute("cancelActivity"));
    }

    [Theory]
    [InlineData("timeDuration", "PT15M")]
    [InlineData("timeDate", "2026-10-11T12:00:00Z")]
    [InlineData("timeCycle", "R3/PT5M")]
    public void TimerLiteralsConvertWithoutAddingNativeProperties(string kind, string value)
    {
        var document = Import($"<process id='p'><intermediateCatchEvent id='timer'><timerEventDefinition><{kind}>{value}</{kind}></timerEventDefinition></intermediateCatchEvent></process>");
        Assert.Equal(value, Assert.Single(document.SemanticModel.Elements).Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.Equal(value, Assert.Single(XDocument.Parse(exported.Xml!).Descendants(Bpmn + kind)).Value);
    }

    [Fact]
    public void UnknownNativePropertiesAreOmittedWithStructuredDiagnosticsWithoutExtensions()
    {
        var source = Import("<process id='p'><task id='task' name='Work'/></process>");
        var original = Assert.Single(source.SemanticModel.Elements);
        var node = new SemanticElementSnapshot(original.Id, original.TypeId, original.Properties.Append(new("native:extra", PropertyValue.FromText("secret metadata"))));
        var document = new DocumentSnapshot(new(source.DocumentId, source.Revision, [node]), source.VisualModel, source.Metadata);
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.True(exported.HasLosses);
        Assert.Contains(exported.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Omitted && entry.SourceIdentity == "task" && entry.Property == "native:extra");
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Context.GetValueOrDefault("disposition") == "Omitted");
        Assert.Empty(XDocument.Parse(exported.Xml!).Descendants(Bpmn + "extensionElements"));
        Assert.DoesNotContain("secret metadata", exported.Xml!);
        Assert.Equal("secret metadata", node.Properties["native:extra"].TextValue);
    }

    [Fact]
    public void ExtraStandardAndForeignInformationIsIgnoredAndLogged()
    {
        var result = BpmnStandardConverter.Import(Xml("""
            <process id="p" isExecutable="true">
              <extensionElements><vendor:options xmlns:vendor="urn:vendor" mode="fast"/></extensionElements>
              <task id="a" name="A"><documentation>One</documentation><documentation>Two</documentation></task>
              <task id="b"/>
              <sequenceFlow id="f" sourceRef="a" targetRef="b"><conditionExpression>approved</conditionExpression></sequenceFlow>
              <dataObject id="data" name="Order"/>
            </process>
            """));
        Success(result);
        Assert.Equal(2, result.Document!.SemanticModel.ElementCount);
        Assert.Single(result.Document.SemanticModel.Relationships);
        Assert.Contains(result.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Ignored && entry.Property == "isExecutable");
        Assert.Contains(result.ConversionLog, entry => entry.Property?.Contains("dataObject", StringComparison.Ordinal) == true);
        Assert.Contains(result.ConversionLog, entry => entry.Property?.Contains("conditionExpression", StringComparison.Ordinal) == true);
        Assert.Contains(result.ConversionLog, entry => entry.Property?.Contains("extensionElements", StringComparison.Ordinal) == true);
        Assert.Contains(Environment.NewLine, result.Document.SemanticModel.Elements.Single(element => element.Id.Value == "a").Properties[BpmnSemanticProperties.Description].TextValue);
    }

    [Fact]
    public void UnsupportedIsolatedNodeCanBeIgnoredButARetainedFlowCannotDangle()
    {
        var ignored = BpmnStandardConverter.Import(Xml("<process id='p'><task id='a'/><intermediateCatchEvent id='unsupported'><conditionalEventDefinition><condition>x</condition></conditionalEventDefinition></intermediateCatchEvent></process>"));
        Success(ignored);
        Assert.Single(ignored.Document!.SemanticModel.Elements);
        Assert.Contains(ignored.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Ignored && entry.SourceIdentity == "unsupported");
        var blocked = BpmnStandardConverter.Import(Xml("<process id='p'><task id='a'/><intermediateCatchEvent id='unsupported'><conditionalEventDefinition><condition>x</condition></conditionalEventDefinition></intermediateCatchEvent><sequenceFlow id='f' sourceRef='a' targetRef='unsupported'/></process>"));
        Assert.False(blocked.Succeeded);
        Assert.Null(blocked.Document);
        Assert.Contains(blocked.Diagnostics, entry => entry.Severity == Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity.Error && entry.Code == "BPMN_FLOW_ENDPOINT_UNRESOLVED");
    }

    [Theory]
    [InlineData("<process id='p'><task id='a'/><task id='a'/></process>")]
    [InlineData("<process id='p'><task id='a'/><sequenceFlow id='f' sourceRef='a' targetRef='missing'/></process>")]
    [InlineData("<collaboration id='c'><participant id='p' processRef='missing'/></collaboration>")]
    [InlineData("<process id='p'><boundaryEvent id='b' attachedToRef='missing'><timerEventDefinition/></boundaryEvent></process>")]
    public void InvalidRequiredStructureFailsWithoutAPartialDocument(string content)
    {
        var result = BpmnStandardConverter.Import(Xml(content));
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, entry => entry.Severity == Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity.Error);
        Assert.All(result.ConversionLog, entry => Assert.True(entry.Disposition is BpmnConversionDisposition.Omitted or BpmnConversionDisposition.Ignored));
    }

    [Fact]
    public void DtdAndExternalEntitiesAreRejected()
    {
        var result = BpmnStandardConverter.Import("<!DOCTYPE definitions [<!ENTITY remote SYSTEM 'file:///private'>]>" + Xml("<process id='p' name='&remote;'/>"));
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BPMN_IMPORT_INVALID");
    }

    [Fact]
    public void NestedSubprocessScopesAndFlowsRoundTripWithoutFlattening()
    {
        var document = Import("""
            <process id="p"><subProcess id="sub" name="Nested"><startEvent id="s"/><task id="t"/><endEvent id="e"/>
            <sequenceFlow id="f1" sourceRef="s" targetRef="t"/><sequenceFlow id="f2" sourceRef="t" targetRef="e"/></subProcess></process>
            """);
        var scope = Assert.Single(document.SemanticModel.NestedScopes);
        Assert.Equal(new SemanticElementId("sub"), scope.OwnerSemanticElementId);
        Assert.Equal(scope.Id, document.SemanticModel.GetScope(new SemanticElementId("t")).Id);
        var export = BpmnStandardConverter.Export(document);
        Success(export);
        var nested = Assert.Single(XDocument.Parse(export.Xml!).Descendants(Bpmn + "subProcess"));
        Assert.Single(nested.Elements(Bpmn + "task"));
        Assert.Equal(2, nested.Elements(Bpmn + "sequenceFlow").Count());
        var second = BpmnStandardConverter.Import(export.Xml!);
        Success(second);
        Assert.Single(second.Document!.SemanticModel.NestedScopes);
    }

    [Fact]
    public void A2PoolMembershipIsTheAuthorityForExportAndReimport()
    {
        var source = PhaseA22PoolParticipantTests.Snapshot();
        var export = BpmnStandardConverter.Export(source);
        Success(export);
        var xml = XDocument.Parse(export.Xml!);
        Assert.Equal(3, xml.Descendants(Bpmn + "participant").Count());
        Assert.Equal(2, xml.Descendants(Bpmn + "lane").Count());
        var import = BpmnStandardConverter.Import(export.Xml!);
        Success(import);
        foreach (var endpoint in new[] { "sales-task", "warehouse-task", "supplier-task", "supplier2-task" })
        {
            Assert.True(BpmnEffectiveParticipant.TryResolve(source, new(endpoint), out var before, out _));
            Assert.True(BpmnEffectiveParticipant.TryResolve(import.Document!, new(endpoint), out var after, out _));
            Assert.Equal(before, after);
        }
        Assert.False(BpmnEffectiveParticipant.TryResolve(import.Document!, new("unpooled"), out _, out _));
    }

    [Fact]
    public void CrossProcessBpmnCollaborationUsesTheExistingSharedA2Diagram()
    {
        var document = Import(CollaborationXml);
        Assert.Empty(document.SemanticModel.NestedScopes);
        Assert.Empty(BpmnMessageFlowSemantics.ValidateEndpoints(document, new("send"), new("receive")));
        Assert.True(BpmnEffectiveParticipant.TryResolve(document, new("send"), out var sender, out _));
        Assert.True(BpmnEffectiveParticipant.TryResolve(document, new("receive"), out var receiver, out _));
        Assert.NotEqual(sender, receiver);
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.Single(XDocument.Parse(exported.Xml!).Descendants(Bpmn + "messageFlow"));
        var roundTrip = BpmnStandardConverter.Import(exported.Xml!);
        Success(roundTrip);
        Assert.Equal(2, roundTrip.Document!.SemanticModel.Elements.Count(element => BpmnTaskSemanticTypes.IsTask(element.TypeId)));
        Assert.Single(roundTrip.Document.SemanticModel.Relationships);
    }

    [Fact]
    public void NativeUnpooledToPoolSequenceFlowIsNotGivenAnInventedParticipant()
    {
        var source = PhaseA22PoolParticipantTests.Snapshot(BpmnSemanticFactory.CreateSequenceFlow(new("f"), new("unpooled"), new("sales-task")));
        var result = BpmnStandardConverter.Export(source);
        Assert.False(result.Succeeded);
        Assert.Null(result.Xml);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BPMN_SEQUENCE_FLOW_OWNERSHIP");
        Assert.False(OrganizationalSemantics.TryGetAssignedPoolId(source.SemanticModel, new("unpooled"), out _));
    }

    [Fact]
    public async Task DiBoundsWaypointsAndManualIntentUseExistingPreparation()
    {
        var imported = await BpmnStandardConverter.ImportAsync(Xml(DiExample), Preparer());
        Success(imported);
        var document = imported.Document!;
        var task = document.VisualModel.VisualStates.Single(visual => visual.SemanticElementId.Value == "task");
        Assert.Equal(new PointD(200, 20), task.Position);
        Assert.Equal(new SizeD(140, 80), task.Size);
        var route = document.VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId.Value == "bpmn:visual:f");
        Assert.Equal(ConnectorRoutingType.Manual, route.RoutingType);
        Assert.Equal<PointD>([new(80, 60), new(140, 60), new(140, 80), new(200, 80)], route.Path);
        Assert.NotNull(document.VisualModel.RoutingScopes);
        Assert.True(new BpmnDocumentValidator().Validate(document).IsStructurallyValid);
        var export = BpmnStandardConverter.Export(document);
        Success(export);
        var path = XDocument.Parse(export.Xml!).Descendants(BpmnDi + "BPMNEdge").Single().Elements(Di + "waypoint").ToArray();
        Assert.Equal(4, path.Length);
        var second = await BpmnStandardConverter.ImportAsync(export.Xml!, Preparer());
        Success(second);
        Assert.Equal<PointD>(route.Path, Assert.Single(second.Document!.VisualModel.EnumerateConnectorRoutingRecords()).Path);
    }

    [Fact]
    public void SynchronousImportReportsPathsNeedingHostPreparation()
    {
        var result = BpmnStandardConverter.Import(Xml(DiExample));
        Success(result);
        Assert.Null(result.Document!.VisualModel.RoutingScopes);
        Assert.All(result.Document.VisualModel.VisualStates, visual => Assert.Empty(visual.Route));
        Assert.Contains(result.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Ignored && entry.Property == "waypoints");
    }

    [Fact]
    public void XmlIdEncodingIsCollisionFreeAndAllReferencesRoundTrip()
    {
        var id = new DocumentId("ids");
        var a = BpmnSemanticFactory.CreateTask(new("a:b"), "A", "A", 1);
        var b = BpmnSemanticFactory.CreateTask(new("a_x003A_b"), "B", "B", 2);
        var source = new DocumentSnapshot(new(id, DocumentRevision.Zero, [a, b], [BpmnSemanticFactory.CreateSequenceFlow(new("a:b:flow"), a.Id, b.Id)]),
            new(id, DocumentRevision.Zero), new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
        var export = BpmnStandardConverter.Export(source);
        Success(export);
        var xml = XDocument.Parse(export.Xml!);
        var ids = xml.Descendants().Attributes("id").Select(attribute => attribute.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        var import = BpmnStandardConverter.Import(export.Xml!);
        Success(import);
        Assert.Equal(2, import.Document!.SemanticModel.ElementCount);
        Assert.Single(import.Document.SemanticModel.Relationships);
    }

    private static DocumentSnapshot Import(string content)
    { var result = BpmnStandardConverter.Import(Xml(content)); Success(result); return result.Document!; }
    internal static void Success(BpmnStandardConversionResult result) =>
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Where(diagnostic => diagnostic.Severity == Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
    internal static string Xml(string content) => """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
          xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" id="definitions" targetNamespace="urn:test">
        """ + content + "</definitions>";
    internal static IConnectorRoutingStatePreparer Preparer()
    {
        var configuration = BpmnModelerComposition.Create(DocumentFactory.CreateEmpty(new("converter:preparation")).Document!).Configuration;
        return new ConnectorRoutingStatePreparer(configuration, new Canvas2DTextLayoutServiceTests.FixedAdvanceTextMetricsService(), StableRoutingPreparationTests.Fixture.CreateRequest);
    }
    internal const string CollaborationXml = """
        <process id="p1"><sendTask id="send" name="Send"/></process><process id="p2"><receiveTask id="receive" name="Receive"/></process>
        <collaboration id="collaboration"><participant id="sender" name="Sender" processRef="p1"/><participant id="receiver" name="Receiver" processRef="p2"/>
          <messageFlow id="message" sourceRef="send" targetRef="receive" name="Order"/></collaboration>
        """;
    internal const string DiExample = """
        <process id="p"><startEvent id="start"/><task id="task" name="Work"/><sequenceFlow id="f" sourceRef="start" targetRef="task"/></process>
        <bpmndi:BPMNDiagram id="diagram"><bpmndi:BPMNPlane id="plane" bpmnElement="p">
          <bpmndi:BPMNShape id="s1" bpmnElement="start"><dc:Bounds x="40" y="40" width="40" height="40"/></bpmndi:BPMNShape>
          <bpmndi:BPMNShape id="s2" bpmnElement="task"><dc:Bounds x="200" y="20" width="140" height="80"/></bpmndi:BPMNShape>
          <bpmndi:BPMNEdge id="e1" bpmnElement="f"><di:waypoint x="80" y="60"/><di:waypoint x="140" y="60"/><di:waypoint x="140" y="80"/><di:waypoint x="200" y="80"/></bpmndi:BPMNEdge>
        </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        """;
}
