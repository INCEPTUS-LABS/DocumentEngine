using System.Xml.Linq;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class BpmnStandardConverterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainDiagramKeepsCollaborationAndOneUnassignedProcessTogether(bool asynchronous)
    {
        var input = Xml(CollaborationXml + "<process id='free'><subProcess id='sub'><task id='child'/></subProcess><task id='loose'/></process>");
        var result = asynchronous ? await BpmnStandardConverter.ImportAsync(input, Preparer()) : BpmnStandardConverter.Import(input);
        Success(result);
        var document = result.Document!;
        foreach (var id in new[] { "send", "receive", "loose", "sub", "sender", "receiver" })
            Assert.Equal(document.SemanticModel.RootScopeId, document.SemanticModel.GetScope(new(id)).Id);
        var nested = Assert.Single(document.SemanticModel.NestedScopes);
        Assert.Equal(document.SemanticModel.RootScopeId, nested.ParentScopeId);
        Assert.Equal("sub", nested.OwnerSemanticElementId!.Value);
        Assert.Equal(nested.Id, document.SemanticModel.GetScope(new("child")).Id);
        Assert.Null(OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, new("loose")));
        Assert.Single(ModelerPresentationViews.GetChoices(document));
        Assert.Single(ModelerPresentationViews.GetChoices(document, new ProcessScopePresentationContext(nested.Id)));
        var exported = BpmnStandardConverter.Export(document);
        Success(exported);
        Assert.Equal(3, XDocument.Parse(exported.Xml!).Root!.Elements(Bpmn + "process").Count());
        var again = BpmnStandardConverter.Import(exported.Xml!);
        Success(again);
        Assert.Single(again.Document!.SemanticModel.NestedScopes);
        Assert.Single(again.Document.SemanticModel.Relationships);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainDiagramRejectsSeveralUnassignedProcessesWithNamesOutsideTheLossLog(bool asynchronous)
    {
        var input = Xml("<process id='review' name='Review'/><process id='approval' name='Approval'/>");
        var result = asynchronous ? await BpmnStandardConverter.ImportAsync(input, Preparer()) : BpmnStandardConverter.Import(input);
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Empty(result.ConversionLog);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("BPMN_IMPORT_MULTIPLE_UNASSIGNED_PROCESSES", error.Code);
        Assert.Contains("Review [review]", error.Message, StringComparison.Ordinal);
        Assert.Contains("Approval [approval]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MainDiagramRejectsSeveralCollaborationsInsteadOfChoosingOne()
    {
        var result = BpmnStandardConverter.Import(Xml("<collaboration id='a'><participant id='x'/></collaboration><collaboration id='b'><participant id='y'/></collaboration>"));
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Empty(result.ConversionLog);
        Assert.Equal("BPMN_IMPORT_MULTIPLE_COLLABORATIONS", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void MainDiagramCountsParticipantReferencesByQNameAndNotLaneMembership()
    {
        Success(BpmnStandardConverter.Import(Xml("<process id='a'/><process id='b'/><collaboration id='c'><participant id='owner' processRef='b'/></collaboration>")));
        var result = BpmnStandardConverter.Import(Xml("<process id='a'><laneSet><lane id='lane'/></laneSet></process><process id='b'/>").Replace("<process", "<b:process", StringComparison.Ordinal).Replace("</process>", "</b:process>", StringComparison.Ordinal)
            .Replace("<definitions ", "<definitions xmlns:b='http://www.omg.org/spec/BPMN/20100524/MODEL' ", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BPMN_IMPORT_MULTIPLE_UNASSIGNED_PROCESSES");
    }

    [Fact]
    public void MainDiagramCompositionDoesNotLegalizeCrossProcessSequenceFlows()
    {
        var result = BpmnStandardConverter.Import(Xml("<process id='p'><task id='a'/><sequenceFlow id='f' sourceRef='a' targetRef='b'/></process><process id='q'><task id='b'/></process><collaboration id='c'><participant id='owner' processRef='q'/></collaboration>"));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BPMN_SEQUENCE_FLOW_SCOPE");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MainDiagramIndependentDiUsesExistingPreparationOrFailsExplicitly(bool overlap, bool asynchronous)
    {
        var input = Xml(MainDiagramDi(overlap));
        var result = asynchronous ? await BpmnStandardConverter.ImportAsync(input, Preparer()) : BpmnStandardConverter.Import(input);
        if (overlap && !asynchronous)
        {
            Assert.False(result.Succeeded);
            Assert.Null(result.Document);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BPMN_IMPORT_DI_COMPOSITION_UNSUPPORTED");
            Assert.DoesNotContain(result.ConversionLog, entry => entry.Code == "BPMN_IMPORT_DI_COMPOSITION_UNSUPPORTED");
        }
        else
        {
            Success(result);
            Assert.Empty(result.Document!.SemanticModel.NestedScopes);
            Assert.Equal(overlap ? 100 : 1000, result.Document.VisualModel.VisualStates.Single(visual => visual.SemanticElementId.Value == "loose").Position.Y);
            if (asynchronous)
            {
                Assert.Single(result.Document.VisualModel.RoutingScopes!.Value);
                var exported = BpmnStandardConverter.Export(result.Document);
                Success(exported);
                var shapes = XDocument.Parse(exported.Xml!).Descendants(BpmnDi + "BPMNShape").ToArray();
                var pool = shapes.Single(shape => (string?)shape.Attribute("bpmnElement") == "pool").Element(Dc + "Bounds")!;
                var loose = shapes.Single(shape => (string?)shape.Attribute("bpmnElement") == "loose").Element(Dc + "Bounds")!;
                Assert.True((double)loose.Attribute("y")! >= (double)pool.Attribute("y")! + (double)pool.Attribute("height")!);
            }
        }
    }

    [Fact]
    public void MainDiagramLegacyNativePeerRootsRemainSelectableWithoutCollaborationViews()
    {
        var source = Import(CollaborationXml);
        var semantic = source.SemanticModel;
        var legacy = new DocumentSnapshot(new SemanticModelSnapshot(source.DocumentId, source.Revision, semantic.Elements, semantic.Relationships,
            [new(new("legacy-peer"))], semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments), source.VisualModel, source.Metadata);
        var choices = ModelerPresentationViews.GetChoices(legacy);
        Assert.Equal(2, choices.Length);
        Assert.Contains(choices, choice => choice.Context == new ProcessScopePresentationContext(new("legacy-peer")));
        Assert.DoesNotContain(choices, choice => choice.Context is DocumentPresentationContext);
    }

    internal static string MainDiagramDi(bool overlap) => $$"""
        <process id="p"><task id="pooled" name="Pooled"/></process>
        <process id="free"><task id="loose" name="Unassigned"/></process>
        <collaboration id="c"><participant id="pool" name="Company" processRef="p"/></collaboration>
        <bpmndi:BPMNDiagram id="d1"><bpmndi:BPMNPlane id="plane1" bpmnElement="c">
          <bpmndi:BPMNShape id="s1" bpmnElement="pool" isHorizontal="true"><dc:Bounds x="0" y="0" width="800" height="500"/></bpmndi:BPMNShape>
          <bpmndi:BPMNShape id="s2" bpmnElement="pooled"><dc:Bounds x="300" y="100" width="140" height="80"/></bpmndi:BPMNShape>
        </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        <bpmndi:BPMNDiagram id="d2"><bpmndi:BPMNPlane id="plane2" bpmnElement="free">
          <bpmndi:BPMNShape id="s3" bpmnElement="loose"><dc:Bounds x="100" y="{{(overlap ? 100 : 1000)}}" width="140" height="80"/></bpmndi:BPMNShape>
        </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
        """;
}
