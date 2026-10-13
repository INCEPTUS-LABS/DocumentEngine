using System.Xml.Linq;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class BpmnStandardConverterTests
{
    [Theory]
    [InlineData("scriptTask")]
    [InlineData("businessRuleTask")]
    [InlineData("callActivity")]
    public void SpecializedActivityKeepsTheBaseTaskAndItsConnectedFlows(string kind)
    {
        var result = BpmnStandardConverter.Import(Xml($"<process id='p'><startEvent id='s'/><{kind} id='a'/><endEvent id='e'/><sequenceFlow id='f' sourceRef='s' targetRef='a'/><sequenceFlow id='g' sourceRef='a' targetRef='e'/></process>"));
        Success(result);
        Assert.Equal(BpmnSemanticTypes.Task, result.Document!.SemanticModel.Elements.Single(element => element.Id.Value == "a").TypeId);
        Assert.Equal(2, result.Document.SemanticModel.RelationshipCount);
        Assert.Contains(result.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Ignored && entry.Property == "activity kind");
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("adHocSubProcess")]
    public void SpecializedSubprocessRetainsItsContainedGraph(string kind)
    {
        var result = BpmnStandardConverter.Import(Xml($"<process id='p'><{kind} id='sub'><task id='t'/></{kind}></process>"));
        Success(result);
        Assert.Equal(BpmnSemanticTypes.SubProcess, result.Document!.SemanticModel.Elements.Single(element => element.Id.Value == "sub").TypeId);
        Assert.Equal(new SemanticElementId("sub"), Assert.Single(result.Document.SemanticModel.NestedScopes).OwnerSemanticElementId);
        Assert.Contains(result.ConversionLog, entry => entry.Property == "subprocess kind");
    }

    [Fact]
    public void LaneMembershipNamesAndCrossLaneFlowSurvive()
    {
        var result = BpmnStandardConverter.Import(Xml("""
            <process id="p"><laneSet id="ls"><lane id="sales" name="Sales"><flowNodeRef>a</flowNodeRef></lane><lane id="warehouse" name="Warehouse"><flowNodeRef>b</flowNodeRef></lane></laneSet>
              <task id="a"/><task id="b"/><sequenceFlow id="f" sourceRef="a" targetRef="b"/></process>
            <collaboration id="c" name="Orders"><participant id="company" name="Company A" processRef="p"/></collaboration>
            """));
        Success(result);
        var source = result.Document!;
        Assert.Equal(new SemanticElementId("sales"), OrganizationalSemantics.GetOwningPoolId(source.SemanticModel, new("a")));
        Assert.Equal(new SemanticElementId("warehouse"), OrganizationalSemantics.GetOwningPoolId(source.SemanticModel, new("b")));
        Assert.Empty(BpmnEffectiveParticipant.ValidateSequenceEndpoints(source, new("a"), new("b")));
        var export = BpmnStandardConverter.Export(source);
        Success(export);
        var xml = XDocument.Parse(export.Xml!);
        Assert.Equal("Orders", (string?)Assert.Single(xml.Descendants(Bpmn + "collaboration")).Attribute("name"));
        Assert.Equal(2, xml.Descendants(Bpmn + "lane").Count());
        Assert.Single(xml.Descendants(Bpmn + "sequenceFlow"));
    }

    [Fact]
    public void StandaloneProcessLanesUseOneSharedA2ParticipantWithoutCrossLaneOwnershipErrors()
    {
        var source = Import("""
            <process id="p"><laneSet><lane id="l1"><flowNodeRef>a</flowNodeRef></lane><lane id="l2"><flowNodeRef>b</flowNodeRef></lane></laneSet>
              <task id="a"/><task id="b"/><sequenceFlow id="f" sourceRef="a" targetRef="b"/></process>
            """);
        Assert.Empty(BpmnEffectiveParticipant.ValidateSequenceEndpoints(source, new("a"), new("b")));
        Success(BpmnStandardConverter.Export(source));
    }

    [Fact]
    public void RepeatedCollaborationRoundTripsDoNotAccumulateEmptyScopesOrCollaborations()
    {
        var current = Import(CollaborationXml);
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var exported = BpmnStandardConverter.Export(current);
            Success(exported);
            Assert.Single(XDocument.Parse(exported.Xml!).Descendants(Bpmn + "collaboration"));
            var imported = BpmnStandardConverter.Import(exported.Xml!);
            Success(imported);
            current = imported.Document!;
            Assert.Empty(current.SemanticModel.NestedScopes);
            Assert.Single(current.SemanticModel.Relationships);
        }
    }

    [Theory]
    [InlineData("<process id='p'><task id='a'/></process><collaboration id='c'><participant id='x' processRef='p'/><participant id='y' processRef='p'/></collaboration>", "BPMN_SHARED_PROCESS")]
    [InlineData("<process id='p'><laneSet><lane id='x'><flowNodeRef>a</flowNodeRef></lane><lane id='y'><flowNodeRef>a</flowNodeRef></lane></laneSet><task id='a'/></process>", "BPMN_LANE_MEMBERSHIP")]
    [InlineData("<process id='p'><laneSet><lane id='x'><flowNodeRef>missing</flowNodeRef></lane></laneSet><task id='a'/></process>", "BPMN_XML_SCHEMA")]
    public void AmbiguousRequiredMembershipFailsInsteadOfAssigningArbitrarily(string input, string code)
    {
        var result = BpmnStandardConverter.Import(Xml(input));
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public async Task CollaborationCanBePreparedWithoutAnEditorInstance()
    {
        var result = await BpmnStandardConverter.ImportAsync(Xml(CollaborationXml), Preparer());
        Success(result);
        var source = result.Document!;
        Assert.Single(source.VisualModel.RoutingScopes!.Value);
        Assert.Single(source.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.True(new BpmnDocumentValidator().Validate(source).IsValid);
        Success(BpmnStandardConverter.Export(source));
    }

    [Fact]
    public async Task DirectParticipantMessageEndpointsUseExistingDerivedVisualOwnership()
    {
        var input = CollaborationXml.Replace("sourceRef=\"send\" targetRef=\"receive\"", "sourceRef=\"sender\" targetRef=\"receiver\"", StringComparison.Ordinal);
        var result = await BpmnStandardConverter.ImportAsync(Xml(input), Preparer());
        Success(result);
        var source = result.Document!;
        Assert.Equal(VisualPlacementMode.Derived, source.VisualModel.VisualStates.Single(visual => visual.SemanticElementId.Value == "sender").PlacementMode);
        Assert.Empty(BpmnMessageFlowSemantics.ValidateEndpoints(source, new("sender"), new("receiver")));
        Assert.Single(source.VisualModel.EnumerateConnectorRoutingRecords());
        Success(BpmnStandardConverter.Export(source));
    }

    [Fact]
    public async Task BlackBoxParticipantEndpointsRemainEditableAndExportable()
    {
        var result = await BpmnStandardConverter.ImportAsync(Xml("<collaboration id='c'><participant id='a'/><participant id='b'/><messageFlow id='f' sourceRef='a' targetRef='b'/></collaboration>"), Preparer());
        Success(result);
        Assert.Single(result.Document!.SemanticModel.Relationships);
        Assert.Contains(result.ConversionLog, entry => entry.Property == "black-box" && entry.Disposition == BpmnConversionDisposition.Ignored);
        Success(BpmnStandardConverter.Export(result.Document));
    }

    [Fact]
    public void EmbeddedLanesKeepTheirSubprocessAndMembershipOnRoundTrip()
    {
        var result = BpmnStandardConverter.Import(Xml("<process id='p'><subProcess id='sub'><laneSet><lane id='l'><flowNodeRef>a</flowNodeRef></lane></laneSet><task id='a'/></subProcess></process>"));
        Success(result);
        var export = BpmnStandardConverter.Export(result.Document!);
        Success(export);
        var xml = XDocument.Parse(export.Xml!);
        Assert.Single(Assert.Single(xml.Descendants(Bpmn + "subProcess")).Descendants(Bpmn + "lane"));
        Assert.Empty(xml.Descendants(Bpmn + "collaboration"));
        var again = BpmnStandardConverter.Import(export.Xml!);
        Success(again);
        Assert.Equal(new SemanticElementId("l"), OrganizationalSemantics.GetOwningPoolId(again.Document!.SemanticModel, new("a")));
    }

    [Fact]
    public async Task PoolLaneDiDimensionsUseExistingSpatialPreparationAndLogNormalization()
    {
        var input = """
            <process id="p"><laneSet><lane id="lane"><flowNodeRef>a</flowNodeRef></lane></laneSet><task id="a"/></process>
            <collaboration id="c"><participant id="company" processRef="p"/></collaboration>
            <bpmndi:BPMNDiagram id="diagram"><bpmndi:BPMNPlane id="plane" bpmnElement="c">
              <bpmndi:BPMNShape id="poolShape" bpmnElement="company" isHorizontal="true"><dc:Bounds x="0" y="0" width="800" height="350"/></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="laneShape" bpmnElement="lane" isHorizontal="true"><dc:Bounds x="38" y="0" width="762" height="350"/></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="taskShape" bpmnElement="a"><dc:Bounds x="120" y="80" width="140" height="80"/></bpmndi:BPMNShape>
            </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
            """;
        var result = await BpmnStandardConverter.ImportAsync(Xml(input), Preparer());
        Success(result);
        Assert.Contains(result.ConversionLog, entry => entry.Code == "BPMN_POOL_GEOMETRY_NORMALIZED");
        var scope = Assert.Single(result.Document!.VisualModel.RoutingScopes!.Value);
        Assert.Contains(scope.Geometry.Regions, region => region.ContainerSemanticElementId?.Value == "lane" && region.ExpandedHeight == 350);
        var export = BpmnStandardConverter.Export(result.Document);
        Success(export);
        var shapes = XDocument.Parse(export.Xml!).Descendants(BpmnDi + "BPMNShape").ToDictionary(shape => (string)shape.Attribute("bpmnElement")!, shape => shape.Element(Dc + "Bounds")!);
        Assert.True((double)shapes["company"].Attribute("x")! < (double)shapes["lane"].Attribute("x")!);
        Assert.True((double)shapes["company"].Attribute("width")! > (double)shapes["lane"].Attribute("width")!);
        Assert.NotEmpty(XDocument.Parse(export.Xml!).Descendants(BpmnDi + "BPMNLabel"));
    }

    [Fact]
    public void NonstandardTimerExpressionIsOmittedWithoutLosingTheTimerEvent()
    {
        var document = Import("<process id='p'><intermediateCatchEvent id='timer'><timerEventDefinition><timeDuration>${timeout}</timeDuration></timerEventDefinition></intermediateCatchEvent></process>");
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.Single(XDocument.Parse(exported.Xml!).Descendants(Bpmn + "timerEventDefinition"));
        Assert.Empty(XDocument.Parse(exported.Xml!).Descendants(Bpmn + "timeDuration"));
        Assert.Contains(exported.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Omitted && entry.Property == BpmnSemanticProperties.TimerDefinition);
    }

    [Fact]
    public void NamespacePrefixesAreResolvedByUriAndQualifiedProcessReferencesAreSupported()
    {
        var xml = """
            <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:t="urn:test" targetNamespace="urn:test">
              <b:process id="p"><b:task id="a"/></b:process><b:collaboration id="c"><b:participant id="company" processRef="t:p"/></b:collaboration>
            </b:definitions>
            """;
        var imported = BpmnStandardConverter.Import(xml);
        Success(imported);
        Assert.True(BpmnEffectiveParticipant.TryResolve(imported.Document!, new("a"), out var owner, out _));
        Assert.Equal(new SemanticElementId("company"), owner!.Id);
    }

    [Fact]
    public void NegativeDiCoordinatesAreTranslatedAsOneDiagram()
    {
        var input = DiExample.Replace("x=\"40\"", "x=\"-40\"", StringComparison.Ordinal);
        var imported = BpmnStandardConverter.Import(Xml(input));
        Success(imported);
        Assert.Equal(new PointD(0, 40), imported.Document!.VisualModel.VisualStates.Single(visual => visual.SemanticElementId.Value == "start").Position);
        Assert.Equal(new PointD(240, 20), imported.Document.VisualModel.VisualStates.Single(visual => visual.SemanticElementId.Value == "task").Position);
        Assert.Contains(imported.ConversionLog, entry => entry.Code == "BPMN_DI_TRANSLATED");
    }

    [Fact]
    public async Task InitialPreparationWithoutDiCanBeSavedInTheExistingNativeFormat()
    {
        var result = await BpmnStandardConverter.ImportAsync(Xml("<process id='p'><startEvent id='s'/><task id='a'/><endEvent id='e'/><sequenceFlow id='f' sourceRef='s' targetRef='a'/><sequenceFlow id='g' sourceRef='a' targetRef='e'/></process>"), Preparer());
        Success(result);
        Assert.All(result.Document!.VisualModel.EnumerateConnectorRoutingRecords(), record => Assert.Equal(ConnectorRoutingType.Automatic, record.RoutingType));
        var bytes = BpmnDocumentSerializer.Serialize(result.Document);
        var native = BpmnDocumentSerializer.Deserialize(bytes.AsMemory());
        Assert.True(native.Succeeded);
        Assert.Equal(result.Document, native.Document);
    }

    [Fact]
    public async Task CancelledPreparationDoesNotReturnAnInstalledDocument()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await BpmnStandardConverter.ImportAsync(Xml(DiExample), Preparer(), cancellation.Token));
    }

    [Fact]
    public async Task NonOrthogonalDiRemainsARepresentableManualPath()
    {
        var result = await BpmnStandardConverter.ImportAsync(Xml(DiExample.Replace("x=\"140\" y=\"60\"", "x=\"143\" y=\"67\"", StringComparison.Ordinal)), Preparer());
        Success(result);
        Assert.Single(result.Document!.SemanticModel.Relationships);
        Assert.True(new BpmnDocumentValidator().Validate(result.Document).IsStructurallyValid);
        Assert.Contains(new PointD(143, 67), Assert.Single(result.Document.VisualModel.EnumerateConnectorRoutingRecords()).Path);
    }

    [Fact]
    public async Task RejectedOptionalRoutingIntentIsLoggedAndFallsBackToExistingAutomaticRouting()
    {
        var result = await BpmnStandardConverter.ImportAsync(Xml(DiExample), new RejectManualIntents(Preparer()));
        Success(result);
        Assert.Equal(ConnectorRoutingType.Automatic, Assert.Single(result.Document!.VisualModel.EnumerateConnectorRoutingRecords()).RoutingType);
        Assert.Contains(result.ConversionLog, entry => entry.Property == "waypoints" && entry.Disposition == BpmnConversionDisposition.Ignored && entry.Message.Contains("TEST_MANUAL_UNAVAILABLE", StringComparison.Ordinal));
    }

    private sealed class RejectManualIntents(IConnectorRoutingStatePreparer inner) : IConnectorRoutingStatePreparer
    {
        public ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken) =>
            request.RoutingIntents.IsEmpty ? inner.PrepareAsync(request, cancellationToken) : ValueTask.FromResult(ConnectorRoutingStatePreparationResult.Failure(
                [new("TEST_MANUAL_UNAVAILABLE", Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity.Error, "The configured preparer rejects this imported route intent.")]));
    }
}
