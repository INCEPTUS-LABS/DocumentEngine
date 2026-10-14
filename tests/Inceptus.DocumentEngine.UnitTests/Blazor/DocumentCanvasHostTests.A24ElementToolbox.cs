using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("task")]
    [InlineData("user-task")]
    [InlineData("manual-task")]
    [InlineData("service-task")]
    [InlineData("send-task")]
    [InlineData("receive-task")]
    [InlineData("sub-process")]
    [InlineData("exclusive-gateway")]
    [InlineData("parallel-gateway")]
    [InlineData("inclusive-gateway")]
    [InlineData("event-based-gateway")]
    [InlineData("message-catch-event")]
    [InlineData("message-throw-event")]
    [InlineData("timer-catch-event")]
    [InlineData("signal-catch-event")]
    [InlineData("signal-throw-event")]
    [InlineData("end-event")]
    public async Task A24ConcreteCreationOneCommitExactUndoRedoAndPersistence(string item)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-create", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var overlay = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        var candidate = Assert.Single(overlay.Candidates, candidate => candidate.ItemId.Value == "bpmn:toolbox:" + item);
        var result = await host.SmartCreateAsync(candidate.ItemId);
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        var selected = Assert.Single(session.CaptureState().EditorState.Selection);
        var visual = Assert.Single(after.VisualModel.VisualStates, visual => visual.Id == selected);
        var target = Assert.Single(after.SemanticModel.Elements, element => element.Id == visual.SemanticElementId);
        Assert.Equal(candidate.ElementTypeId, target.TypeId);
        Assert.False(target.Properties.ContainsKey(BpmnSemanticProperties.Description));
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(),
            after.SemanticModel.Elements.Where(element => element.Id != target.Id));
        Assert.Equal(before.SemanticModel.RootScopeId, after.SemanticModel.GetScope(target.Id).Id);
        Assert.True(visual.Position.X > before.VisualModel.VisualStates.Single().Position.X);
        Assert.Null(OrganizationalSemantics.GetOwningPoolId(after.SemanticModel, target.Id));
        var flow = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, flow.TypeId);
        Assert.Equal(new SemanticElementId("source"), flow.SourceId);
        Assert.Equal(target.Id, flow.TargetId);
        Assert.Equal(ConnectorRoutingOutcome.Path, Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords()).Outcome);
        Assert.Equal(selected, host.CaptureState().ElementToolbox!.VisualId);
        var exported = NativeDocumentSerializer.Export(after);
        var imported = NativeDocumentSerializer.Import(exported.AsMemory());
        Assert.True(imported.Succeeded);
        Assert.Equal(exported.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());
        await host.UndoAsync();
        var undone = document.CaptureSnapshot();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), undone.SemanticModel.Elements);
        Assert.Empty(undone.SemanticModel.Relationships);
        A23PresentationEqual(before, undone);
        Assert.DoesNotContain(selected, session.CaptureState().EditorState.Selection);
        await host.RedoAsync();
        var redone = document.CaptureSnapshot();
        Assert.Equal(after.SemanticModel.Elements.AsEnumerable(), redone.SemanticModel.Elements);
        Assert.Equal(after.SemanticModel.Relationships.AsEnumerable(), redone.SemanticModel.Relationships);
        A23PresentationEqual(after, redone);
    }

    [Fact]
    public async Task A24ContinuousCreationAndContextFiltering()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-continuous", "container");
        await A24Select(host, new VisualStateId("v-source"));
        foreach (var item in new[] { "task", "exclusive-gateway", "task", "end-event" })
        {
            var result = await host.SmartCreateAsync(new("bpmn:toolbox:" + item));
            Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
            Assert.Equal(Assert.Single(Session(host).CaptureState().EditorState.Selection), host.CaptureState().ElementToolbox!.VisualId);
        }
        Assert.Empty(host.CaptureState().ElementToolbox!.Candidates);
        Assert.Equal(4, AttachedDocument(Session(host)).CaptureSnapshot().SemanticModel.RelationshipCount);
        Assert.Equal(4, Session(host).CaptureState().HistoryStatus.EntryCount);
        var saved = AttachedDocument(Session(host)).CaptureSnapshot();
        var imported = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(saved).AsMemory());
        Assert.True(imported.Succeeded);
        await using var reopened = A23Host(imported.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("a24-reopened", "container");
        Assert.Null(reopened.CaptureState().ElementToolbox);
        Assert.Equal(saved.SemanticModel.Elements.AsEnumerable(), AttachedDocument(Session(reopened)).SemanticModel.Elements);
        A23PresentationEqual(saved, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Fact]
    public async Task A24EventGatewayUsesSemanticRulesAndRejectsInvalidDirectRequest()
    {
        await using var host = A23Host(A24Snapshot(BpmnSemanticTypes.EventBasedGateway));
        await host.InitializeAsync("a24-event-based", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var candidates = host.CaptureState().ElementToolbox!.Candidates;
        Assert.Equal(4, candidates.Length);
        Assert.All(candidates, item => Assert.Contains(item.ElementTypeId, new[] { BpmnSemanticTypes.MessageCatchEvent,
            BpmnSemanticTypes.TimerCatchEvent, BpmnSemanticTypes.SignalCatchEvent, BpmnSemanticTypes.ReceiveTask }));
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        Assert.Null(await host.SmartCreateAsync(new("bpmn:toolbox:task")));
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(new VisualStateId("v-source"), Assert.Single(Session(host).CaptureState().EditorState.Selection));
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:receive-task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        await A24Select(host, new VisualStateId("v-source"));
        Assert.DoesNotContain(host.CaptureState().ElementToolbox!.Candidates, item => item.ElementTypeId == BpmnSemanticTypes.MessageCatchEvent);
    }

    [Theory]
    [InlineData("sales-task-visual")]
    [InlineData("warehouse-task-visual")]
    [InlineData("supplier-task-visual")]
    public async Task A24PoolAndLaneInheritanceExpansionAndUndo(string id)
    {
        await using var host = A23Host(A22PoolSnapshot());
        await host.InitializeAsync("a24-pool", "container");
        await A24Select(host, new VisualStateId(id));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var original = before.VisualModel.VisualStates.Single(visual => visual.Id.Value == id);
        var pool = OrganizationalSemantics.GetOwningPoolId(before.SemanticModel, original.SemanticElementId);
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        var target = after.SemanticModel.Elements.Single(element => !before.SemanticModel.Elements.Any(prior => prior.Id == element.Id));
        Assert.Equal(pool, OrganizationalSemantics.GetOwningPoolId(after.SemanticModel, target.Id));
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements.Where(element => element.Id != target.Id));
        foreach (var prior in before.VisualModel.VisualStates)
            Assert.Equal(prior.Position, after.VisualModel.VisualStates.Single(visual => visual.Id == prior.Id).Position);
        var regions = after.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        Assert.Equal(regions.Single(region => region.ContainerSemanticElementId?.Value == "sales").ContentBounds!.Value.Bottom,
            regions.Single(region => region.ContainerSemanticElementId?.Value == "warehouse").ContentBounds!.Value.Top);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Fact]
    public async Task A24BranchesDoNotMoveExistingNodesAndUseFreeTracks()
    {
        await using var host = A23Host(A24Snapshot(BpmnSemanticTypes.ExclusiveGateway));
        await host.InitializeAsync("a24-branches", "container");
        for (var index = 0; index < 3; index++)
        {
            await A24Select(host, new VisualStateId("v-source"));
            var before = AttachedDocument(Session(host)).CaptureSnapshot();
            var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
            Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
            var after = AttachedDocument(Session(host)).CaptureSnapshot();
            foreach (var prior in before.VisualModel.VisualStates)
                Assert.Equal(prior.Position, after.VisualModel.VisualStates.Single(visual => visual.Id == prior.Id).Position);
            var nodes = after.VisualModel.RoutingScopes!.Value.Single().Geometry.Nodes;
            foreach (var first in nodes)
                Assert.DoesNotContain(nodes, second => first.VisualStateId != second.VisualStateId && first.LocalBounds.Intersects(second.LocalBounds));
            var source = after.VisualModel.VisualStates.Single(visual => visual.Id.Value == "v-source");
            foreach (var side in source.ConnectorAnchors.GroupBy(anchor => anchor.Side))
            {
                var remotePositions = side.OrderBy(anchor => anchor.Order).Select(anchor =>
                {
                    var connector = after.VisualModel.VisualStates.Single(visual => visual.SourceAnchorId == anchor.Id);
                    var flow = after.SemanticModel.Relationships.Single(flow => flow.Id == connector.SemanticElementId);
                    var target = after.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == flow.TargetId);
                    return side.Key is ConnectorAnchorSide.Left or ConnectorAnchorSide.Right ? target.Position.Y : target.Position.X;
                }).ToArray();
                Assert.Equal(remotePositions.Order(), remotePositions);
            }
        }
    }

    [Fact]
    public async Task A24OverlayTracksViewportAndHasNoDocumentSideEffectsOrRepeatedCandidateWork()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-overlay", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var overlay = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        Assert.False(overlay.CanAutoPoints);
        Assert.DoesNotContain(overlay.Candidates, item => item.ElementTypeId == BpmnSemanticTypes.StartEvent || BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(item.ElementTypeId));
        var evaluations = host.ElementCandidateEvaluationCount;
        for (var index = 0; index < 10; index++) host.CaptureState();
        await session.PanViewportAsync(new(-40, -20));
        await session.WaitForIdleAsync();
        var moved = host.CaptureState().ElementToolbox!;
        Assert.Equal(overlay.Anchor.X - 40, moved.Anchor.X);
        Assert.Equal(overlay.CreationWidth, moved.CreationWidth);
        Assert.Equal(evaluations, host.ElementCandidateEvaluationCount);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        await A24Select(host);
        Assert.Null(host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task A24AutoPointsRetainsNodeGeometryIdentitiesAndIsIdempotent()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("branches")));
        await host.InitializeAsync("a24-points", "container");
        await A24Select(host, new VisualStateId("v-split"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        Assert.True(host.CaptureState().ElementToolbox!.CanAutoPoints);
        var result = await host.AutoPointsAsync();
        Assert.NotNull(result);
        Assert.True(result.IsCommitted || !result.Diagnostics.Any(diagnostic => diagnostic.Severity == Contracts.Diagnostics.DiagnosticSeverity.Error), A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        foreach (var visual in before.VisualModel.VisualStates)
        {
            var current = after.VisualModel.VisualStates.Single(item => item.Id == visual.Id);
            Assert.Equal(visual.Position, current.Position); Assert.Equal(visual.Size, current.Size);
            Assert.Equal(visual.ConnectorAnchors.Select(anchor => anchor.Id).OrderBy(id => id.Value), current.ConnectorAnchors.Select(anchor => anchor.Id).OrderBy(id => id.Value));
            if (visual.Id.Value != "v-split") Assert.Equal(visual, current);
        }
        Assert.True(BpmnAutoPointsPlanner.Plan(after, new("v-split")).IsNoChange);
        await host.AutoPointsAsync();
        Assert.Same(after, document.CaptureSnapshot());
        if (result.IsCommitted)
        {
            await host.UndoAsync(); A23PresentationEqual(before, document.CaptureSnapshot());
            await host.RedoAsync(); A23PresentationEqual(after, document.CaptureSnapshot());
        }
    }

    private static async Task A24Select(DocumentCanvasHost host, params VisualStateId[] ids)
    {
        Assert.True(host.CaptureState().IsInitialized, string.Join("; ", host.CaptureState().HostDiagnostics.Select(diagnostic => diagnostic.Message)));
        var session = Session(host);
        var editor = session.CaptureState().EditorState;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(ids, null, null, null, editor.Viewport))).Succeeded);
        await session.WaitForIdleAsync();
    }

    private static string A24Diagnostics(DocumentCanvasHost host, Contracts.History.HistoryOperationResult? result) =>
        string.Join("; ", (result?.Diagnostics ?? []).Concat(host.CaptureState().InteractionDiagnostics).Select(item => item.Code + ": " + item.Message));

    private static DocumentSnapshot A24Snapshot(SemanticTypeId? type = null, PointD? position = null)
    {
        var id = new DocumentId("a24:document");
        var revision = DocumentRevision.Zero;
        var element = new SemanticElementSnapshot(new("source"), type ?? BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, Contracts.Properties.PropertyValue.FromText("Source"))]);
        return new(new SemanticModelSnapshot(id, revision, [element]),
            new VisualModelSnapshot(id, revision, [new(new("v-source"), element.Id, position ?? new(120, 250), new(120, 80), VisualPlacementMode.Pinned)]),
            new DocumentMetadataSnapshot(id, revision));
    }
}
