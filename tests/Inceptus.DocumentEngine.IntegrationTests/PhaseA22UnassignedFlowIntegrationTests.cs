using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA22UnassignedFlowIntegrationTests
{
    [Theory]
    [InlineData("review", false)]
    [InlineData("review", true)]
    [InlineData("pack", false)]
    [InlineData("pack", true)]
    public async Task PoolToUnassignedFlowPreservesUnrelatedManualRouteAndNativeState(string pooled, bool reverse)
    {
        var sample = await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var composition = await new BpmnModelerCompositionFactory(initialDocument: sample.CaptureSnapshot()).CreateAsync();
        await using var host = await HostHarness.CreateAsync(composition);
        var document = host.Composition.Document;
        async Task Commit(ICommand command)
        {
            var result = await host.Session.ExecuteAsync(command);
            Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
            await host.Session.WaitForIdleAsync();
        }
        await Commit(new UnassignOrganizationalElementCommand(document.DocumentId, document.Revision, Id("receive")));
        var source = reverse ? "receive" : pooled;
        var target = reverse ? pooled : "receive";
        foreach (var (name, role) in new[] { ("prepare", ConnectorAnchorRole.Source), ("send", ConnectorAnchorRole.Target),
                     (source, ConnectorAnchorRole.Source), (target, ConnectorAnchorRole.Target) })
            await Commit(new AddConnectorAnchorCommand(document.DocumentId, document.Revision, Visual(name), Anchor(name),
                role == ConnectorAnchorRole.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left, role, 0));
        await Commit(new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision, Id("existing"), Visual("existing"),
            Id("prepare"), Id("send"), Anchor("prepare"), Anchor("send"), name: "Existing route"));
        await Commit(new SetConnectorRoutingTypeCommand(document.DocumentId, document.Revision, Visual("existing"), ConnectorRoutingType.Manual));
        var before = document.CaptureSnapshot();
        var unaffected = Assert.Single(before.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.Equal(ConnectorRoutingType.Manual, unaffected.RoutingType);
        Assert.NotNull(unaffected.ManualDefinition);
        var history = host.State.HistoryStatus.EntryCount;
        await Commit(new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision, Id("unassigned-flow"), Visual("unassigned-flow"),
            Id(source), Id(target), Anchor(source), Anchor(target), name: "Pool to unassigned"));
        var after = document.CaptureSnapshot();
        Assert.Equal(history + 1, host.State.HistoryStatus.EntryCount);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.Where(visual => visual.Id != Visual("unassigned-flow")));
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.Single().Geometry, after.VisualModel.RoutingScopes!.Value.Single().Geometry);
        Assert.Equal(unaffected, after.VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId == Visual("existing")));
        Assert.All(after.VisualModel.EnumerateConnectorRoutingRecords(), record => Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome));
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, after.SemanticModel.Relationships.Single(flow => flow.Id == Id("unassigned-flow")).TypeId);

        await host.Host.UndoAsync();
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(unaffected, Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords()));
        await host.Host.RedoAsync();
        Assert.Equal(after.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(after.VisualModel.EnumerateConnectorRoutingRecords(), document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords());
        await Commit(new DeleteBpmnSequenceFlowCommand(document.DocumentId, document.Revision, Id("unassigned-flow"), Visual("unassigned-flow"),
            Id(source), Id(target), Anchor(source), Anchor(target)));
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        Assert.Equal(unaffected, Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords()));
        await host.Host.UndoAsync();

        var expected = document.CaptureSnapshot();
        var exported = await host.Host.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded);
        var imported = NativeDocumentSerializer.Import(exported.Payload.AsMemory(), composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded);
        await using var reopened = await HostHarness.CreateAsync(
            await new BpmnModelerCompositionFactory(initialDocument: imported.Document!.CaptureSnapshot()).CreateAsync());
        Assert.Equal(expected, reopened.Composition.Document.CaptureSnapshot());
        Assert.Equal(exported.Payload.ToArray(), (await reopened.Host.ExportNativeDocumentAsync()).Payload.ToArray());
    }

    private static SemanticElementId Id(string name) => new("demo:pools:" + name);
    private static VisualStateId Visual(string name) => new("demo:pools:visual:" + name);
    private static ConnectorAnchorId Anchor(string name) => new("unassigned-flow:anchor:" + name);
}
