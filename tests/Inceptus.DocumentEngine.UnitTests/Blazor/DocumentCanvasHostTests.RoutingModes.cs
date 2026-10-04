using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task SwitchingSavedNoRouteToManualClearsTheObsoleteRoutingWarning()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("routing-no-route-properties", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId,
            document.Revision, BpmnDemoPipeline.TaskVisualId, new PointD(212, 121),
            VisualPlacementMode.Pinned))).IsCommitted);
        await session.WaitForIdleAsync();
        var visualId = BpmnDemoPipeline.SecondSequenceFlowVisualId;
        var unresolved = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value
            .SelectMany(static scope => scope.Connectors).Single(record => record.VisualStateId == visualId);
        Assert.Equal(ConnectorRoutingOutcome.NoRoute, unresolved.Outcome);
        Assert.Contains(session.CaptureState().RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute);
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visualId]))).Succeeded);
        await session.WaitForIdleAsync();
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(visualId));
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = "manual" };
        Assert.True(host.UpdatePropertiesFormState(true, visualId, true));

        var result = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute);
        Assert.DoesNotContain(session.CaptureState().RuntimeDiagnostics,
            diagnostic => diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute);
        var manual = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value
            .SelectMany(static scope => scope.Connectors).Single(record => record.VisualStateId == visualId);
        Assert.Equal(ConnectorRoutingType.Manual, manual.RoutingType);
        Assert.Equal(ConnectorRoutingOutcome.Path, manual.Outcome);
        Assert.Empty(manual.ManualDefinition!.Value);
        Assert.Equal(new[] { unresolved.AutomaticProof!.Source.Point, unresolved.AutomaticProof.Target.Point },
            manual.Path.AsEnumerable());
    }

    [Fact]
    public async Task RoutingTypePropertiesApplyUsesOneTransactionAndUnchangedApplyIsNoChange()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("routing-properties", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var visualId = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visualId]))).Succeeded);
        await session.WaitForIdleAsync();
        var initial = document.CaptureSnapshot();
        var initialRecord = initial.VisualModel.RoutingScopes!.Value
            .SelectMany(static scope => scope.Connectors).Single(record => record.VisualStateId == visualId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(visualId));
        Assert.Equal(ConnectorRoutingType.Automatic, properties.RoutingType);
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = "manual" };
        Assert.True(host.UpdatePropertiesFormState(true, visualId, draft.IsDirty));

        var applied = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, applied.Status);
        Assert.Equal(initial.Revision.Increment(), document.Revision);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        var manual = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value
            .SelectMany(static scope => scope.Connectors).Single(record => record.VisualStateId == visualId);
        Assert.Equal(ConnectorRoutingType.Manual, manual.RoutingType);
        Assert.Equal(initialRecord.Path.AsEnumerable(), manual.Path);
        Assert.Equal(initialRecord.Path.Skip(1).Take(initialRecord.Path.Length - 2), manual.ManualDefinition!.Value);
        Assert.Equal(initial.SemanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements);
        Assert.Equal(initial.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        var unchanged = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(applied.Authoritative));
        var beforeNoChange = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.NoChange, (await host.ApplyPropertiesAsync(unchanged)).Status);
        Assert.Same(beforeNoChange, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        host.UpdatePropertiesFormState(false, null, false);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        var undone = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value
            .SelectMany(static scope => scope.Connectors).Single(record => record.VisualStateId == visualId);
        Assert.Equal(ConnectorRoutingType.Automatic, undone.RoutingType);
        Assert.Equal(manual.ManualDefinition!.Value.AsEnumerable(), undone.ManualDefinition!.Value);
    }
}
