using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Bpmn.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("company", "supplier")]
    [InlineData("supplier", "supplier2")]
    [InlineData("sales-task", "supplier-task")]
    public async Task A23AutoArrangeMessageEndpointsRetainIdentityAndDoNotDriveRanks(string source, string target)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22PoolHost(selection);
        await host.InitializeAsync("arrange-message-endpoints", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var initial = document.CaptureSnapshot();
        var expected = BpmnAutoArrangePlanner.Plan(initial, [initial.SemanticModel.RootScopeId]).Positions;
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        foreach (var endpoint in new[] { source, target })
        {
            var scene = session.CaptureState().CurrentScene!;
            var body = A22ViewBody(scene, new(endpoint));
            var point = endpoint is "supplier" or "supplier2" ? new PointD(body.Bounds.Right - 10, body.Bounds.Bottom - 10) : Center(body.Bounds);
            await PointerObserver(host).DownDocumentPointAsync(scene, point, pointerId: 502);
            await PointerObserver(host).UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 502);
        }
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var flow = Assert.Single(before.SemanticModel.Relationships);
        Assert.Equal(BpmnSemanticTypes.MessageFlow, flow.TypeId);
        var connector = before.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == flow.Id);
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var after = document.CaptureSnapshot();
        foreach (var (id, position) in expected)
            Assert.Equal(position, after.VisualModel.VisualStates.Single(visual => visual.Id == id).Position);
        Assert.Equal(connector, after.VisualModel.VisualStates.Single(visual => visual.Id == connector.Id));
        Assert.Equal(ConnectorRoutingOutcome.Path, Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords()).Outcome);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.True(BpmnAutoArrangePlanner.Plan(after, [after.SemanticModel.RootScopeId]).IsNoChange);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
    }

    [Fact]
    public async Task A23AutoArrangeUnassignedNodeRemainsInItsRegion()
    {
        var initial = A22PoolSnapshot();
        var element = new SemanticElementSnapshot(new("unassigned"), BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Unassigned task"))]);
        var semantic = new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            initial.SemanticModel.Elements.Append(element), modelProfiles: initial.SemanticModel.ModelProfiles,
            profileAssignments: initial.SemanticModel.ProfileAssignments);
        var visual = new VisualModelSnapshot(initial.DocumentId, initial.Revision,
            initial.VisualModel.VisualStates.Append(new(new("unassigned-visual"), element.Id, new(600, 400), new(140, 80), VisualPlacementMode.Pinned)),
            initial.VisualModel.ProfileElementPresentations);
        await using var host = A23Host(new(semantic, visual, initial.Metadata));
        await host.InitializeAsync("arrange-unassigned", "container");
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        var old = before.VisualModel.RoutingScopes!.Value.Single().Geometry.Nodes.Single(node => node.VisualStateId.Value == "unassigned-visual");
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var after = document.CaptureSnapshot();
        Assert.Equal(old.RegionId, after.VisualModel.RoutingScopes!.Value.Single().Geometry.Nodes.Single(node => node.VisualStateId == old.VisualStateId).RegionId);
        Assert.DoesNotContain(after.SemanticModel.ProfileAssignments, assignment => assignment.SemanticElementId == element.Id);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A23AutoArrangeInvalidPlanningInputsDoNotChangeAuthority(bool anchors)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("arrange-planning-failure", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var broken = new VisualModelSnapshot(before.DocumentId, before.Revision,
            before.VisualModel.VisualStates.Select(visual => anchors ? new VisualStateSnapshot(visual.Id, visual.SemanticElementId,
                visual.Position, visual.Size, visual.PlacementMode, visual.Route, visual.Properties, [],
                visual.SourceAnchorId, visual.TargetAnchorId, visual.BoundaryAttachment) : visual),
            before.VisualModel.ProfileElementPresentations, anchors ? before.VisualModel.RoutingScopes : null);
        Assert.Throws<InvalidOperationException>(() => BpmnAutoArrangePlanner.Plan(
            new(before.SemanticModel, broken, before.Metadata), [before.SemanticModel.RootScopeId]));
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task A23AutoArrangeRejectsInvalidMembershipPlanAtTheSameRevision()
    {
        await using var host = A23Host(A22PoolSnapshot());
        await host.InitializeAsync("arrange-invalid-membership", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var semantic = before.SemanticModel;
        var invalid = new SemanticModelSnapshot(before.DocumentId, before.Revision, semantic.Elements,
            semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles,
            semantic.ProfileAssignments.Select(assignment => new ModelProfileElementAssignmentSnapshot(
                assignment.ProfileId, assignment.SemanticElementId, new("missing-pool"))));
        var plan = BpmnAutoArrangePlanner.Plan(new(invalid, before.VisualModel, before.Metadata), [semantic.RootScopeId]);
        var result = await session.ExecuteAsync(new BpmnAutoArrangeCommand(plan));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BPMN_AUTO_ARRANGE_STALE");
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }
}
