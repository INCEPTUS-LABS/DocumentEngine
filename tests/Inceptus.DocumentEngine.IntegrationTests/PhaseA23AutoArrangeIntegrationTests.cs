using System.Diagnostics;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA23AutoArrangeIntegrationTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(.3d)]
    public async Task A23ProductionArrangeLanesMessagesBoundaryCycleHistoryAndNativeReopen(double zoom)
    {
        var (initial, flows) = await Fixture();
        var composition = await new BpmnModelerCompositionFactory(initialDocument: initial).CreateAsync();
        await using var host = await HostHarness.CreateAsync(composition);
        var document = host.Composition.Document;
        foreach (var flow in flows)
        {
            ICommand command = flow.TypeId == BpmnSemanticTypes.MessageFlow
                ? new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision, flow.Id, Visual(flow.Id), flow.SourceId, flow.TargetId,
                    new(flow.Id.Value + ":source"), new(flow.Id.Value + ":target"))
                : new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision, flow.Id, Visual(flow.Id), flow.SourceId, flow.TargetId,
                    new(flow.Id.Value + ":source"), new(flow.Id.Value + ":target"));
            var created = await host.Session.ExecuteAsync(command);
            Assert.True(created.IsCommitted, string.Join("; ", created.Diagnostics.Select(diagnostic => diagnostic.Message)));
            await host.Session.WaitForIdleAsync();
        }
        await host.Session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var fixturePath = Environment.GetEnvironmentVariable("A23_BROWSER_FIXTURE");
        if (fixturePath is not null && zoom == 1) File.WriteAllBytes(fixturePath, NativeDocumentSerializer.Export(before).ToArray());
        await host.Session.UpdateViewportAsync(new Inceptus.DocumentEngine.Contracts.EditorState.ViewportSnapshot(zoom, default));
        await host.Session.WaitForIdleAsync();
        var timer = Stopwatch.StartNew();
        var result = await host.Host.AutoArrangeAsync();
        timer.Stop();
        Assert.NotNull(result);
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Console.WriteLine($"A23 representative arrangement: {timer.Elapsed.TotalMilliseconds:F1} ms, 1 command, revision {document.Revision}.");
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.VisualModel.ProfileElementPresentations.AsEnumerable(), after.VisualModel.ProfileElementPresentations);
        Assert.All(after.VisualModel.EnumerateConnectorRoutingRecords(), record => Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome));
        Assert.Single(host.Scene.Items, item => item.Geometry.Content == "Company A");
        Assert.True((await host.Host.AutoArrangeAsync())!.Succeeded);
        Assert.Same(after, document.CaptureSnapshot());
        await host.Host.UndoAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        await host.Host.RedoAsync();
        Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        var exported = await host.Host.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded);
        var imported = NativeDocumentSerializer.Import(exported.Payload.AsMemory(), composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded);
        await using var reopened = await HostHarness.CreateAsync(await new BpmnModelerCompositionFactory(initialDocument: imported.Document!.CaptureSnapshot()).CreateAsync());
        Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), reopened.Composition.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
    }

    private static async Task<(DocumentSnapshot Snapshot, SemanticRelationshipSnapshot[] Flows)> Fixture()
    {
        var original = (await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync()).CaptureSnapshot();
        var extra = new[] { ("start", BpmnSemanticTypes.StartEvent), ("split", BpmnSemanticTypes.ExclusiveGateway),
            ("branch", BpmnSemanticTypes.Task), ("merge", BpmnSemanticTypes.ExclusiveGateway), ("end", BpmnSemanticTypes.EndEvent),
            ("boundary", BpmnSemanticTypes.MessageBoundaryEvent) }.Select(item => new SemanticElementSnapshot(Id(item.Item1), item.Item2,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText(item.Item1))], item.Item1 == "boundary" ? Id("send") : null)).ToArray();
        (string Source, string Target, bool Message)[] links = [("start", "prepare", false), ("prepare", "split", false),
            ("split", "send", false), ("split", "throw", false), ("split", "branch", false), ("send", "merge", false),
            ("throw", "merge", false), ("branch", "merge", false), ("merge", "pack", false), ("pack", "prepare", false),
            ("pack", "end", false), ("receive", "catch", false), ("send", "receive", true), ("review", "boundary", true)];
        var flows = links.Select((link, index) => new SemanticRelationshipSnapshot(Id("flow-" + index),
            link.Message ? BpmnSemanticTypes.MessageFlow : BpmnSemanticTypes.SequenceFlow, Id(link.Source), Id(link.Target),
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(link.Message ? "Notification" : ""))])).ToArray();
        var elements = original.SemanticModel.Elements.Concat(extra).ToArray();
        var semantic = new SemanticModelSnapshot(original.DocumentId, original.Revision, elements,
            modelProfiles: original.SemanticModel.ModelProfiles,
            profileAssignments: original.SemanticModel.ProfileAssignments.Concat(extra.Where(element => element.AttachedToElementId is null)
                .Select(element => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, element.Id,
                    Id(element.Id.Value.EndsWith("merge", StringComparison.Ordinal) || element.Id.Value.EndsWith("end", StringComparison.Ordinal) ? "warehouse" : "sales")))));
        var visuals = original.VisualModel.VisualStates.Concat(extra.Select((element, index) => new VisualStateSnapshot(Visual(element.Id), element.Id,
            new(100 + index * 120, 160 + index * 90), BpmnSemanticTypes.IsEvent(element.TypeId) ? new(40, 40) :
                element.TypeId == BpmnSemanticTypes.ExclusiveGateway ? new(50, 50) : new(140, 80), VisualPlacementMode.Pinned,
            boundaryAttachment: element.AttachedToElementId is null ? null : new(BoundaryAttachmentSide.Bottom, .6)))).Select(visual =>
        {
            var anchors = flows.Where(flow => flow.SourceId == visual.SemanticElementId).Select((flow, index) =>
                new ConnectorAnchor(new(flow.Id.Value + ":source"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, index))
                .Concat(flows.Where(flow => flow.TargetId == visual.SemanticElementId).Select((flow, index) =>
                    new ConnectorAnchor(new(flow.Id.Value + ":target"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, index)));
            var position = visual.BoundaryAttachment is not null ? new PointD(334, 85) : visual.Position;
            return new VisualStateSnapshot(visual.Id, visual.SemanticElementId, position, visual.Size, visual.PlacementMode,
                properties: visual.Properties, connectorAnchors: anchors, boundaryAttachment: visual.BoundaryAttachment);
        });
        return (new(semantic, new VisualModelSnapshot(original.DocumentId, original.Revision, visuals, original.VisualModel.ProfileElementPresentations), original.Metadata), flows);
    }

    private static SemanticElementId Id(string name) => new("demo:pools:" + name);
    private static VisualStateId Visual(SemanticElementId id) => new(id.Value.Replace("demo:pools:", "demo:pools:visual:", StringComparison.Ordinal));
}
