using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.UnitTests.Bpmn;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task BpmnStandardImportCanReplaceTheEditorDocumentThenEditUndoRedoAndExport()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400, 900, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("bpmn-active", "bpmn-standby", "bpmn-container");
        var imported = BpmnStandardConverter.Import(BpmnStandardConverterTests.Xml(BpmnStandardConverterTests.DiExample));
        BpmnStandardConverterTests.Success(imported);
        var installed = await host.SetDocumentAsync(imported.Document!);
        Assert.True(installed.Succeeded, string.Join(Environment.NewLine, installed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var session = Session(host);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        var document = host.GetDocument().Snapshot!;
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new SemanticElementId("task"), BpmnSemanticProperties.Name, "Imported and edited"))).IsCommitted);
        await session.WaitForIdleAsync();
        await host.UndoAsync();
        Assert.Equal("Work", Name());
        await host.RedoAsync();
        Assert.Equal("Imported and edited", Name());
        var before = session.CaptureState().HistoryStatus;
        var exported = BpmnStandardConverter.Export(host.GetDocument().Snapshot!);
        BpmnStandardConverterTests.Success(exported);
        var roundTrip = BpmnStandardConverter.Import(exported.Xml!);
        BpmnStandardConverterTests.Success(roundTrip);
        Assert.Single(roundTrip.Document!.SemanticModel.Relationships);
        Assert.Equal(before, session.CaptureState().HistoryStatus);

        string Name() => host.GetDocument().Snapshot!.SemanticModel.Elements.Single(element => element.Id.Value == "task").Properties[BpmnSemanticProperties.Name].TextValue;
    }
}
