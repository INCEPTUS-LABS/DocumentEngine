using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class BpmnStandardConversionIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedProcessUsesNormalHostSceneEditingHistoryNativeSaveAndStandardExport(bool lanes)
    {
        var laneXml = lanes ? "<laneSet><lane id='l1' name='Sales'><flowNodeRef>a</flowNodeRef></lane><lane id='l2' name='Warehouse'><flowNodeRef>b</flowNodeRef></lane></laneSet>" : string.Empty;
        var xml = "<definitions xmlns='http://www.omg.org/spec/BPMN/20100524/MODEL' targetNamespace='urn:test'><process id='p'>" + laneXml +
            "<userTask id='a' name='Review'/><serviceTask id='b' name='Complete'/><sequenceFlow id='f' sourceRef='a' targetRef='b'/></process></definitions>";
        var imported = BpmnStandardConverter.Import(xml);
        Assert.True(imported.Succeeded, string.Join(Environment.NewLine, imported.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var composition = await new BpmnModelerCompositionFactory(initialDocument: imported.Document).CreateAsync();
        await using var harness = await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync(composition);
        Assert.Contains(harness.Scene.Items, item => item.Origin.VisualStateId?.Value == "bpmn:visual:a");
        Assert.Contains(harness.Scene.Items, item => item.Origin.VisualStateId?.Value == "bpmn:visual:f");
        var document = harness.Host.GetDocument().Snapshot!;
        Assert.True((await harness.Session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new SemanticElementId("a"), BpmnSemanticProperties.Name, "Reviewed"))).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        await harness.Host.UndoAsync();
        Assert.Equal("Review", Name());
        await harness.Host.RedoAsync();
        Assert.Equal("Reviewed", Name());
        document = harness.Host.GetDocument().Snapshot!;
        var native = BpmnDocumentSerializer.Deserialize(BpmnDocumentSerializer.Serialize(document).AsMemory());
        Assert.True(native.Succeeded);
        Assert.Equal(document, native.Document);
        var exported = BpmnStandardConverter.Export(document);
        Assert.True(exported.Succeeded, string.Join(Environment.NewLine, exported.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var again = BpmnStandardConverter.Import(exported.Xml!);
        Assert.True(again.Succeeded);
        var flow = Assert.Single(again.Document!.SemanticModel.Relationships);
        Assert.Equal(new SemanticElementId("a"), flow.SourceId);
        Assert.Equal(new SemanticElementId("b"), flow.TargetId);
        Assert.Equal(BpmnSemanticTypes.UserTask, again.Document.SemanticModel.Elements.Single(element => element.Id == flow.SourceId).TypeId);
        Assert.Equal("Reviewed", again.Document.SemanticModel.Elements.Single(element => element.Id == flow.SourceId).Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(1, harness.State.HistoryStatus.EntryCount);

        string Name() => harness.Host.GetDocument().Snapshot!.SemanticModel.Elements.Single(element => element.Id.Value == "a").Properties[BpmnSemanticProperties.Name].TextValue;
    }
}
