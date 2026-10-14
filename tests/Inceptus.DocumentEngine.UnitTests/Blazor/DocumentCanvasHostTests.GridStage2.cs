using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
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
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(40)]
    public async Task GridStage2DemoMixedEventAndTaskPoolsAreIdempotentAfterSmartCreate(double distance)
    {
        var initial = await Inceptus.DocumentEngine.Blazor.Demo.BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var grid = new TestGridSource(new() { PointDistance = distance });
        await using var host = A23Host(initial.CaptureSnapshot(), grid);
        await host.InitializeAsync("grid-demo", "container");
        await A24Select(host, new VisualStateId("demo:pools:visual:prepare"));
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        result = await host.AutoArrangeAsync();
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var document = AttachedDocument(Session(host));
        var after = document.CaptureSnapshot();
        AssertGridStage2Centers(after, grid.Grid);
        var next = BpmnAutoArrangePlanner.Plan(after, [after.SemanticModel.RootScopeId], grid.Grid);
        Assert.True(next.IsNoChange, string.Join("; ", next.Positions.Select(pair => pair.Key.Value + ": " +
            after.VisualModel.VisualStates.Single(visual => visual.Id == pair.Key).Position + " -> " + pair.Value)));
        for (var repeat = 0; repeat < 3; repeat++)
        {
            await host.AutoArrangeAsync();
            Assert.Same(after, document.CaptureSnapshot());
        }
    }

    [Theory]
    [InlineData(25)]
    [InlineData(40)]
    public async Task GridStage2ActiveSubProcessUsesItsOwnCoordinatesAndPreservesParent(double distance)
    {
        var initial = A24Snapshot(BpmnSemanticTypes.SubProcess);
        var nested = new DocumentScopeId("grid:nested");
        var child = new SemanticElementSnapshot(new("child"), BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, Contracts.Properties.PropertyValue.FromText("Child"))]);
        var semantic = new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            initial.SemanticModel.Elements.Append(child), [], [new(nested, initial.SemanticModel.RootScopeId, new("source"))], [new(child.Id, nested)]);
        var visual = new VisualModelSnapshot(initial.DocumentId, initial.Revision, initial.VisualModel.VisualStates.Append(
            new(new("v-child"), child.Id, new(120, 250), new(120, 80), VisualPlacementMode.Pinned)));
        var grid = new TestGridSource(new() { PointDistance = distance });
        await using var host = A23Host(new(semantic, visual, initial.Metadata), grid);
        await host.InitializeAsync("grid-nested", "container");
        Assert.True((await host.NavigateToScopeAsync(nested))?.Succeeded);
        await A24Select(host, new VisualStateId("v-child"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var created = document.VisualModel.VisualStates.Single(visual => visual.Id == session.CaptureState().EditorState.Selection.Single());
        Assert.Equal(nested, document.SemanticModel.GetScope(created.SemanticElementId).Id);
        AssertGridStage2Centers(document.CaptureSnapshot(), grid.Grid, [created.Id]);
        result = await host.AutoArrangeAsync();
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        AssertGridStage2Centers(after, grid.Grid, [created.Id, new("v-child")]);
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == before.SemanticModel.RootScopeId),
            after.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == before.SemanticModel.RootScopeId));
        Assert.Equal(before.VisualModel.VisualStates.Single(visual => visual.Id.Value == "v-source"),
            after.VisualModel.VisualStates.Single(visual => visual.Id.Value == "v-source"));
        await host.UndoAsync();
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual)]
    public async Task GridStage2SmartCreateRetainsUnrelatedRoutesAndManualBends(ConnectorRoutingType mode)
    {
        await using var host = A23Host(A24Snapshot(), new TestGridSource(new() { PointDistance = 25 }));
        await host.InitializeAsync("grid-smart-routes", "container");
        await A24Select(host, new VisualStateId("v-source"));
        for (var index = 0; index < 2; index++) Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))?.IsCommitted);
        var session = Session(host);
        var document = AttachedDocument(session);
        var first = document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().First();
        if (mode != ConnectorRoutingType.Automatic)
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId, document.Revision, first.VisualStateId, mode))).IsCommitted);
        await session.WaitForIdleAsync();
        if (mode == ConnectorRoutingType.Manual)
        {
            var route = document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId == first.VisualStateId);
            Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId, document.Revision,
                first.VisualStateId, [route.Path[0], new(281, 357), new(317, 357), route.Path[^1]]))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var before = document.CaptureSnapshot();
        var retained = before.VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId == first.VisualStateId);
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        Assert.Equal(retained, after.VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId == first.VisualStateId));
        await host.UndoAsync(); A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync(); A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GridStage2SmartChecksCollisionAfterSnappingAndBoundsItsSearch(bool blocked)
    {
        var initial = A24Snapshot();
        var obstacle = new SemanticElementSnapshot(new("obstacle"), BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, Contracts.Properties.PropertyValue.FromText("Obstacle"))]);
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            initial.SemanticModel.Elements.Append(obstacle)), new VisualModelSnapshot(initial.DocumentId, initial.Revision,
            initial.VisualModel.VisualStates.Append(new(new("v-obstacle"), obstacle.Id,
                blocked ? new(320, 0) : new(450, 260), blocked ? new(500, 10000) : new(120, 80), VisualPlacementMode.Pinned))), initial.Metadata);
        var grid = new TestGridSource(new() { PointDistance = 25 });
        await using var host = A23Host(snapshot, grid);
        await host.InitializeAsync("grid-collision", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        if (blocked)
        {
            Assert.Null(result);
            Assert.Same(before, document.CaptureSnapshot());
            Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        }
        else
        {
            Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
            var created = document.VisualModel.VisualStates.Single(visual => visual.Id == session.CaptureState().EditorState.Selection.Single());
            Assert.Equal(new PointD(340, 110), created.Position);
            AssertGridStage2NoOverlaps(document.CaptureSnapshot());
        }
    }

    [Fact]
    public async Task GridStage2PlansRetainCapturedSettingsWhenPreferencesChangeBeforeCommit()
    {
        var initial = A24Snapshot();
        var composition = await new BpmnModelerCompositionFactory(initialDocument: initial).CreateAsync();
        var grid = new TestGridSource(new() { PointDistance = 25 });
        await using var host = A23Host(initial, grid);
        await host.InitializeAsync("grid-snapshot", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var captured = grid.Grid;
        var smart = await new BpmnSmartCreatePlanner(composition.Configuration.CommandHandlers, composition.Configuration.CommandValidators)
            .PlanAsync(before, new("v-source"), new("bpmn:toolbox:task"), GuidDocumentCreationIdentityProvider.Instance, captured);
        grid.Grid = new() { SnapToGrid = false, PointDistance = 40 };
        await host.RefreshGridAsync();
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        Assert.True((await session.ExecuteAsync(smart)).IsCommitted);
        await session.WaitForIdleAsync();
        AssertGridStage2Centers(document.CaptureSnapshot(), captured, [smart.CreatedVisualStateId]);
        var arrange = BpmnAutoArrangePlanner.Plan(document.CaptureSnapshot(), [before.SemanticModel.RootScopeId], captured);
        grid.Grid = new() { PointDistance = 10 };
        Assert.True((await session.ExecuteAsync(new BpmnAutoArrangeCommand(arrange))).IsCommitted);
        await session.WaitForIdleAsync();
        AssertGridStage2Centers(document.CaptureSnapshot(), captured, [.. document.VisualModel.VisualStates
            .Where(visual => document.SemanticModel.TryGetElement(visual.SemanticElementId, out _)).Select(visual => visual.Id)]);
    }

    [Theory]
    [InlineData("BPMN.ExclusiveGateway")]
    [InlineData("BPMN.ParallelGateway")]
    [InlineData("BPMN.InclusiveGateway")]
    public async Task GridStage2GatewayTypesPreserveBranchesAndMerge(string type)
    {
        var initial = A23Snapshot(A23Edges("branches"));
        var semantic = new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            initial.SemanticModel.Elements.Select(element => element.Id.Value is "split" or "merge"
                ? new SemanticElementSnapshot(element.Id, new(type), element.Properties) : element), initial.SemanticModel.Relationships);
        var grid = new TestGridSource(new() { PointDistance = 40 });
        await using var host = A23Host(new(semantic, initial.VisualModel, initial.Metadata), grid);
        await host.InitializeAsync("grid-gateway", "container");
        var result = await host.AutoArrangeAsync();
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = AttachedDocument(Session(host)).CaptureSnapshot();
        Assert.Equal(semantic.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(semantic.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        AssertGridStage2Centers(after, grid.Grid);
        AssertGridStage2NoOverlaps(after);
        var branches = new[] { A23Bounds(after, "a"), A23Bounds(after, "b"), A23Bounds(after, "c") }.OrderBy(bounds => bounds.Top).ToArray();
        Assert.Equal(branches[0].X + branches[0].Width / 2, branches[1].X + branches[1].Width / 2);
        for (var index = 1; index < branches.Length; index++) Assert.True(branches[index].Top - branches[index - 1].Bottom >= 60);
    }

    [Theory]
    [InlineData(10, true, .5)]
    [InlineData(20, false, 1)]
    [InlineData(25, true, 2)]
    [InlineData(40, false, .75)]
    [InlineData(500, false, 1)]
    [InlineData(1e-300, false, 1)]
    public async Task GridStage2SmartCreateUsesDisplayedCenterAndAtomicHistory(double distance, bool show, double zoom)
    {
        var grid = new TestGridSource(new() { PointDistance = distance, ShowGrid = show });
        await using var host = A23Host(A24Snapshot(), grid);
        await host.InitializeAsync("grid-smart", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new(43, -17)));
        await A24Select(host, new VisualStateId("v-source"));
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        var created = after.SemanticModel.Elements.Single(element => !before.SemanticModel.Elements.Contains(element));
        var body = after.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == created.Id);
        AssertGridStage2Centers(after, grid.Grid);
        Assert.True(body.Position.X >= 320);
        if (distance == 20) Assert.Equal(new PointD(320, 260), body.Position); // Y=290 midpoint -> 300.
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(before.Revision.Increment(), after.Revision);
        var flow = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(new SemanticElementId("source"), flow.SourceId);
        Assert.Equal(created.Id, flow.TargetId);
        Assert.Equal(body.Id, Assert.Single(session.CaptureState().EditorState.Selection));
        Assert.NotNull(host.CaptureState().ElementToolbox);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
        var reopened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(after).AsMemory());
        Assert.True(reopened.Succeeded);
        A23PresentationEqual(after, reopened.Document!.CaptureSnapshot());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(40)]
    [InlineData(500)]
    public async Task GridStage2SmartBranchesChooseDeterministicFreeTracks(double distance)
    {
        async Task<PointD[]> Create()
        {
            var grid = new TestGridSource(new() { PointDistance = distance });
            await using var host = A23Host(A24Snapshot(BpmnSemanticTypes.ExclusiveGateway), grid);
            await host.InitializeAsync("grid-branches", "container");
            var positions = new List<PointD>();
            for (var index = 0; index < 3; index++)
            {
                await A24Select(host, new VisualStateId("v-source"));
                var before = AttachedDocument(Session(host)).CaptureSnapshot();
                var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
                Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
                var after = AttachedDocument(Session(host)).CaptureSnapshot();
                foreach (var prior in before.VisualModel.VisualStates)
                    Assert.Equal(prior.Position, after.VisualModel.VisualStates.Single(item => item.Id == prior.Id).Position);
                AssertGridStage2Centers(after, grid.Grid);
                AssertGridStage2NoOverlaps(after);
                positions.Add(after.VisualModel.VisualStates.Single(item => item.Id ==
                    Session(host).CaptureState().EditorState.Selection.Single()).Position);
            }
            return [.. positions];
        }
        Assert.Equal(await Create(), await Create());
    }

    [Theory]
    [InlineData("sales-task-visual", 20)]
    [InlineData("warehouse-task-visual", 25)]
    [InlineData("supplier-task-visual", 40)]
    public async Task GridStage2SmartCreateInheritsPoolAndLaneWithNonzeroFrame(string source, double distance)
    {
        var grid = new TestGridSource(new() { PointDistance = distance, ShowGrid = false });
        await using var host = A23Host(A22PoolSnapshot(), grid);
        await host.InitializeAsync("grid-pool-smart", "container");
        await A24Select(host, new VisualStateId(source));
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        var created = after.SemanticModel.Elements.Single(element => !before.SemanticModel.Elements.Contains(element));
        var original = before.VisualModel.VisualStates.Single(visual => visual.Id.Value == source);
        Assert.Equal(OrganizationalSemantics.GetOwningPoolId(before.SemanticModel, original.SemanticElementId),
            OrganizationalSemantics.GetOwningPoolId(after.SemanticModel, created.Id));
        AssertGridStage2Centers(after, grid.Grid, [after.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == created.Id).Id]);
        await host.UndoAsync(); A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync(); A23PresentationEqual(after, document.CaptureSnapshot());
    }

    public static IEnumerable<object[]> GridStage2Arrangements()
    {
        string[] topologies = ["linear", "branches", "no-merge", "cycle", "components", "multiple-events"];
        double[] distances = [10, 20, 25, 40];
        return from topology in topologies from distance in distances select new object[] { topology, distance };
    }

    [Theory]
    [MemberData(nameof(GridStage2Arrangements))]
    [InlineData("linear", .1)]
    [InlineData("linear", 13.3)]
    [InlineData("linear", 1e-300)]
    [InlineData("linear", 500)]
    public async Task GridStage2AutoArrangePlansSpacingTopologyHistoryAndNoDrift(string topology, double distance)
    {
        var grid = new TestGridSource(new() { PointDistance = distance, ShowGrid = false });
        await using var host = A23Host(A23Snapshot(A23Edges(topology)), grid);
        await host.InitializeAsync("grid-arrange", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(.75, new(-27, 53)));
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var result = await host.AutoArrangeAsync();
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        AssertGridStage2Centers(after, grid.Grid);
        AssertGridStage2NoOverlaps(after);
        foreach (var (source, target) in A23Edges(topology).Where(edge => edge != ("c", "a")))
            Assert.True(A23Bounds(after, target).Left - A23Bounds(after, source).Right >= 80);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.All(after.VisualModel.EnumerateConnectorRoutingRecords(), record => Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome));
        Assert.True(BpmnAutoArrangePlanner.Plan(after, [after.SemanticModel.RootScopeId], grid.Grid).IsNoChange);
        await host.AutoArrangeAsync();
        Assert.Same(after, document.CaptureSnapshot());
        await host.UndoAsync(); A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync(); A23PresentationEqual(after, document.CaptureSnapshot());
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(after).AsMemory());
        Assert.True(opened.Succeeded);
        A23PresentationEqual(after, opened.Document!.CaptureSnapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GridStage2SnapDisabledPreservesExactPriorPlanning(bool show)
    {
        var grid = new TestGridSource(new() { SnapToGrid = false, ShowGrid = show, PointDistance = 500 });
        await using var host = A23Host(A24Snapshot(), grid);
        await host.InitializeAsync("grid-disabled", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))?.IsCommitted);
        var document = AttachedDocument(Session(host));
        Assert.Equal(new PointD(320, 250), document.VisualModel.VisualStates.Single(visual => visual.Id ==
            Session(host).CaptureState().EditorState.Selection.Single()).Position);
        var before = document.CaptureSnapshot();
        var legacy = BpmnAutoArrangePlanner.Plan(before, [before.SemanticModel.RootScopeId]);
        var disabled = BpmnAutoArrangePlanner.Plan(before, [before.SemanticModel.RootScopeId], grid.Grid);
        Assert.Equal(legacy.Positions.OrderBy(pair => pair.Key.Value), disabled.Positions.OrderBy(pair => pair.Key.Value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GridStage2UnrepresentableGridFailsWithoutPartialMutation(bool smart)
    {
        await using var host = A23Host(A24Snapshot(), new TestGridSource(new() { PointDistance = 1e100 }));
        await host.InitializeAsync("grid-impossible", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var result = smart ? await host.SmartCreateAsync(new("bpmn:toolbox:task")) : await host.AutoArrangeAsync();
        Assert.Null(result);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Contains(host.CaptureState().InteractionDiagnostics, item => item.Code ==
            (smart ? "BPMN_ELEMENT_TOOLBOX_FAILED" : "BPMN_AUTO_ARRANGE_FAILED"));
    }

    private static void AssertGridStage2Centers(DocumentSnapshot document, GridSettings grid, VisualStateId[]? selected = null)
    {
        foreach (var scope in document.VisualModel.RoutingScopes ?? [])
            foreach (var node in scope.Geometry.Nodes)
            {
                var visual = document.VisualModel.VisualStates.Single(visual => visual.Id == node.VisualStateId);
                if (!document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) ||
                    !BpmnGridPolicy.Supports(element!.TypeId) || (selected is not null && !selected.Contains(node.VisualStateId)) ||
                    (selected is null && node.VisualStateId.Value == "v-source")) continue;
                var frame = scope.Geometry.Regions.SingleOrDefault(region => region.Id == node.RegionId)?.LocalToScopeTransform ?? Matrix2D.Identity;
                var center = frame.TransformPoint(Center(node.LocalBounds));
                var snapped = GridGeometry.Snap(center, grid);
                Assert.Equal(snapped.X, center.X, 7);
                Assert.Equal(snapped.Y, center.Y, 7);
            }
    }

    private static void AssertGridStage2NoOverlaps(DocumentSnapshot document)
    {
        foreach (var scope in document.VisualModel.RoutingScopes ?? [])
            foreach (var node in scope.Geometry.Nodes)
                Assert.DoesNotContain(scope.Geometry.Nodes, other => other.VisualStateId != node.VisualStateId &&
                    other.RegionId == node.RegionId && node.LocalBounds.Intersects(other.LocalBounds));
    }
}
