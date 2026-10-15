using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(true, true, .5)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 1)]
    public async Task AlignmentUsesFirstClickedCenterAndOneHistoryOperation(bool horizontal, bool snap, double zoom)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")),
            new TestGridSource(new() { SnapToGrid = snap, ShowGrid = false, PointDistance = 37 }));
        await host.InitializeAsync("alignment", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new(-83, 27)));
        var ids = new[] { new VisualStateId("v-b"), new("v-a"), new("v-start") };
        for (var index = 0; index < ids.Length; index++)
        {
            var scene = session.CaptureState().CurrentScene!;
            await PointerObserver(host).ClickDocumentPointAsync(scene, Center(A24PanelBody(scene, ids[index]).Bounds), controlKey: index > 0);
        }
        Assert.Equal(ids, session.CaptureState().SelectionOrder);
        Assert.NotEqual(ids[0], session.CaptureState().EditorState.Selection[0]);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var reference = before.VisualModel.VisualStates.Single(v => v.Id == ids[0]);
        Assert.True(host.CanAlign(horizontal));
        Assert.True((await host.AlignAsync(horizontal))!.IsCommitted);
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(reference, after.VisualModel.VisualStates.Single(v => v.Id == reference.Id));
        foreach (var id in ids.Skip(1))
        {
            var old = before.VisualModel.VisualStates.Single(v => v.Id == id);
            var moved = after.VisualModel.VisualStates.Single(v => v.Id == id);
            if (horizontal)
            {
                Assert.Equal(reference.Position.Y + reference.Size.Height / 2, moved.Position.Y + moved.Size.Height / 2);
                Assert.Equal(old.Position.X, moved.Position.X);
            }
            else
            {
                Assert.Equal(reference.Position.X + reference.Size.Width / 2, moved.Position.X + moved.Size.Width / 2);
                Assert.Equal(old.Position.Y, moved.Position.Y);
            }
        }
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(ids, session.CaptureState().SelectionOrder);
        Assert.False(host.CanAlign(horizontal));
        Assert.Null(await host.AlignAsync(horizontal));
        Assert.Same(after, document.CaptureSnapshot());
        await using var movement = A23Host(before);
        await movement.InitializeAsync("alignment-reference", "container");
        var movementSession = Session(movement);
        var moves = ids.Skip(1).Select(id => new VisualStateMove(id,
            after.VisualModel.VisualStates.Single(v => v.Id == id).Position, VisualPlacementMode.Pinned));
        Assert.True((await movementSession.ExecuteAsync(new MoveVisualStatesCommand(before.DocumentId, before.Revision, moves))).IsCommitted);
        await movementSession.WaitForIdleAsync();
        A23PresentationEqual(after, AttachedDocument(movementSession).CaptureSnapshot());
        await host.UndoAsync();
        await movement.UndoAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        // Ordinary movement restores geometry through its existing routing authority;
        // affected routing records follow that authority's survivor ordering on Undo.
        A23PresentationEqual(AttachedDocument(movementSession).CaptureSnapshot(), document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
        var bytes = NativeDocumentSerializer.Export(after);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(imported.Succeeded);
        await using var reopened = A23Host(imported.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("alignment-reopen", "container");
        A23PresentationEqual(after, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlignmentUsesCurrentFirstSelectionAfterRemovalAndReselection(bool horizontal)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("alignment-order", "container");
        var b = new VisualStateId("v-b");
        var a = new VisualStateId("v-a");
        var start = new VisualStateId("v-start");
        await A24Select(host, b);
        await A24Select(host, b, start);
        await A24Select(host, a, b, start);
        await A24Select(host, a, start);
        await A24Select(host, a, b, start);
        var session = Session(host);
        Assert.Equal(new[] { start, a, b }, session.CaptureState().SelectionOrder);
        var reference = AttachedDocument(session).VisualModel.VisualStates.Single(v => v.Id == start);
        Assert.True((await host.AlignAsync(horizontal))!.IsCommitted);
        Assert.Equal(reference, AttachedDocument(session).VisualModel.VisualStates.Single(v => v.Id == start));
        await A24Select(host);
        Assert.Empty(session.CaptureState().SelectionOrder);
        await A24Select(host, b);
        await A24Select(host, a, b);
        Assert.Equal(new[] { b, a }, session.CaptureState().SelectionOrder);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("connector")]
    [InlineData("boundary")]
    [InlineData("horizontal-outside-lane")]
    public async Task AlignmentRejectsUnavailableSelectionsWithoutMutation(string reason)
    {
        var snapshot = reason == "horizontal-outside-lane" ? A22PoolSnapshot() : A23Snapshot(A23Edges("linear"));
        if (reason == "boundary")
            snapshot = new DocumentSnapshot(snapshot.SemanticModel,
                new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates.Select(v =>
                    v.Id.Value == "v-start" ? new VisualStateSnapshot(v.Id, v.SemanticElementId, new(0, 0), v.Size,
                        v.PlacementMode, v.Route, v.Properties, v.ConnectorAnchors, v.SourceAnchorId, v.TargetAnchorId) : v)), snapshot.Metadata);
        await using var host = A23Host(snapshot);
        await host.InitializeAsync("alignment-reject", "container");
        var ids = reason switch
        {
            "single" => new[] { new VisualStateId("v-a") },
            "connector" => [new("v-a"), new("v-flow-0")],
            "horizontal-outside-lane" => [new("sales-task-visual"), new("warehouse-task-visual")],
            _ => [new("v-start"), new("v-a")]
        };
        await A24Select(host, ids[0]);
        await A24Select(host, ids);
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.False(host.CanAlign(true));
        Assert.Null(await host.AlignAsync(true));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData("sales-task-visual", true, .75)]
    [InlineData("supplier-task-visual", true, 2)]
    [InlineData("sales-task-visual", false, 2)]
    [InlineData("supplier-task-visual", false, .75)]
    public async Task AlignmentAcrossPoolsAndLanesUsesEachLocalMappingAndKeepsReferenceFixed(string referenceId, bool snap, double zoom)
    {
        var initial = AlignmentPoolSnapshot();
        await using var host = A23Host(initial, new TestGridSource(new() { PointDistance = 37, SnapToGrid = snap, ShowGrid = false }));
        await host.InitializeAsync("alignment-cross-pools", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new(-33, 57)));
        var ids = new[] { new VisualStateId(referenceId) }.Concat(new[]
            { new VisualStateId("sales-task-visual"), new("warehouse-task-visual"), new("supplier-task-visual") }
            .Where(id => id.Value != referenceId)).ToArray();
        await A24Select(host, ids[0]);
        await A24Select(host, ids);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var state = session.CaptureState();
        var bounds = ids.ToDictionary(id => id, id => A24PanelBody(state.CurrentScene!, id).Bounds);
        var regions = ids.ToDictionary(id => id, id => state.CurrentScene!.GetVisualRegion(id)!);
        Assert.Equal(3, regions.Values.Select(region => region.Id).Distinct().Count());
        Assert.NotEqual(regions[new("sales-task-visual")].LocalToSceneTransform.OffsetY,
            regions[new("supplier-task-visual")].LocalToSceneTransform.OffsetY);
        Assert.True(host.CanAlign(false));
        Assert.True((await host.AlignAsync(false))!.IsCommitted);
        var after = document.CaptureSnapshot();
        var scene = session.CaptureState().CurrentScene!;
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(state.HistoryStatus.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(bounds[ids[0]], A24PanelBody(scene, ids[0]).Bounds);
        foreach (var id in ids)
        {
            var current = A24PanelBody(scene, id).Bounds;
            var oldVisual = before.VisualModel.VisualStates.Single(v => v.Id == id);
            var newVisual = after.VisualModel.VisualStates.Single(v => v.Id == id);
            Assert.Equal(Center(bounds[ids[0]]).X, Center(current).X);
            Assert.Equal(bounds[id].Y, current.Y);
            Assert.Equal(oldVisual.Position.Y, newVisual.Position.Y);
            Assert.Equal(oldVisual.Size, newVisual.Size);
            Assert.Equal(regions[id], scene.GetVisualRegion(id));
            Assert.True(regions[id].Bounds.Contains(current));
            Assert.Equal(regions[id].MapSceneToLocal(current).TopLeft, newVisual.Position);
        }
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.VisualModel.ProfileElementPresentations.AsEnumerable(), after.VisualModel.ProfileElementPresentations);
        Assert.False(host.CanAlign(false));
        Assert.Null(await host.AlignAsync(false));
        Assert.Same(after, document.CaptureSnapshot());
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
        var imported = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(after).AsMemory());
        Assert.True(imported.Succeeded);
        await using var reopened = A23Host(imported.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("alignment-cross-pools-reopen", "container");
        A23PresentationEqual(after, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Fact]
    public async Task AlignmentAcrossPoolsRejectsOutOfBoundsTargetAtomically()
    {
        var initial = AlignmentPoolSnapshot();
        await using var host = A23Host(initial);
        await host.InitializeAsync("alignment-cross-pools-boundary", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var source = new VisualStateId("supplier-task-visual");
        // A center at the standalone Pool's left edge is left of the legal center
        // of the wider task in Warehouse. The other standalone Pool can
        // accept the position, but no part of the mixed selection may commit.
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            source, new(0, 15), VisualPlacementMode.Pinned))).IsCommitted);
        await session.WaitForIdleAsync();
        await A24Select(host, source);
        await A24Select(host, source, new("supplier2-task-visual"));
        await A24Select(host, source, new("supplier2-task-visual"), new("warehouse-task-visual"));
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.False(host.CanAlign(false));
        Assert.Null(await host.AlignAsync(false));
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    private static DocumentSnapshot AlignmentPoolSnapshot()
    {
        var initial = A22PoolSnapshot();
        var visuals = initial.VisualModel.VisualStates.Select(v => new VisualStateSnapshot(v.Id, v.SemanticElementId,
            v.Id.Value switch
            {
                "sales-task-visual" => new(80, 35),
                "warehouse-task-visual" => new(231, 65),
                "supplier-task-visual" => new(177, 15),
                _ => v.Position
            }, v.Id.Value == "warehouse-task-visual" ? new(140, 90) : v.Size, v.PlacementMode));
        return new(initial.SemanticModel, new VisualModelSnapshot(initial.DocumentId, initial.Revision, visuals,
            initial.VisualModel.ProfileElementPresentations), initial.Metadata);
    }

    [Theory]
    [InlineData(false, ConnectorRoutingType.Automatic)]
    [InlineData(false, ConnectorRoutingType.Straight)]
    [InlineData(false, ConnectorRoutingType.Manual)]
    [InlineData(true, ConnectorRoutingType.Automatic)]
    [InlineData(true, ConnectorRoutingType.Straight)]
    [InlineData(true, ConnectorRoutingType.Manual)]
    public async Task AlignmentAcrossPoolsAndLanesUsesExistingFlowRouting(bool message, ConnectorRoutingType mode)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22PoolHost(selection);
        await host.InitializeAsync("alignment-cross-flow", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var target = new VisualStateId(message ? "supplier-task-visual" : "warehouse-task-visual");
        var source = new VisualStateId("sales-task-visual");
        var visual = document.VisualModel.VisualStates.Single(v => v.Id == target);
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            target, visual.Position + new VectorD(137, 0)))).IsCommitted);
        await session.WaitForIdleAsync();
        var tool = A22FlowTool(message ? "Message Flow" : "Sequence Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        foreach (var id in new[] { source, target })
        {
            var scene = session.CaptureState().CurrentScene!;
            var point = Center(A24PanelBody(scene, id).Bounds);
            await PointerObserver(host).DownDocumentPointAsync(scene, point, pointerId: 912);
            await PointerObserver(host).UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 912);
        }
        var connector = Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords()).VisualStateId;
        selection.Clear();
        await host.RefreshToolboxPlacementAsync();
        if (mode != ConnectorRoutingType.Automatic)
        {
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
                document.Revision, connector, mode))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        await A24Select(host, source);
        await A24Select(host, source, target);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.True((await host.AlignAsync(false))!.IsCommitted);
        var after = document.CaptureSnapshot();
        Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        var record = Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.Equal(mode, record.RoutingType);
        Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome);
        Assert.Equal(Assert.Single(before.VisualModel.EnumerateConnectorRoutingRecords()).ManualDefinition, record.ManualDefinition);
        await using var movement = A23Host(before);
        await movement.InitializeAsync("alignment-cross-flow-reference", "container");
        var movementSession = Session(movement);
        Assert.True((await movementSession.ExecuteAsync(new MoveVisualStatesCommand(before.DocumentId, before.Revision,
            [new(target, after.VisualModel.VisualStates.Single(v => v.Id == target).Position, VisualPlacementMode.Pinned)]))).IsCommitted);
        await movementSession.WaitForIdleAsync();
        A23PresentationEqual(after, AttachedDocument(movementSession).CaptureSnapshot());
        await host.UndoAsync();
        await movement.UndoAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        A23PresentationEqual(AttachedDocument(movementSession).CaptureSnapshot(), document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Fact]
    public async Task AlignmentAcrossLanesEnablesVerticalToolbarAndRetainsItOnHover()
    {
        await using var test = await PanComponentFixture.CreateAsync(document: AlignmentPoolSnapshot());
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            await A24Select(test.Host, new VisualStateId("sales-task-visual"));
            await A24Select(test.Host, new VisualStateId("sales-task-visual"), new("warehouse-task-visual"));
        });
        await test.DrainAsync();
        var presentation = ActionBarSnapshot(test.Activator.Canvas);
        Assert.True(ActionBarEnabled(presentation, "CanAlignVertical"));
        Assert.False(ActionBarEnabled(presentation, "CanAlignHorizontal"));
        var session = Session(test.Host);
        var point = Center(A24PanelBody(session.CaptureState().CurrentScene!, new("warehouse-task-visual")).Bounds);
        await A24DuringPausedSceneAsync(test.Host,
            () => test.Renderer.Dispatcher.InvokeAsync(() => InteractionController(test.Host).PointerMovedAsync(
                session.CaptureState().CurrentScene!.ViewportTransform.TransformPoint(point)).AsTask()),
            async () =>
            {
                await test.DrainAsync();
                Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas));
            });
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.AlignAsync(false).AsTask());
        await test.DrainAsync();
        Assert.False(ActionBarEnabled(ActionBarSnapshot(test.Activator.Canvas), "CanAlignVertical"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlignmentUsesDisplayedCoordinatesInsideLane(bool horizontal)
    {
        await using var host = A23Host(A22PoolSnapshot());
        await host.InitializeAsync("alignment-lane", "container");
        var source = new VisualStateId("sales-task-visual");
        await A24Select(host, source);
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))!.IsCommitted);
        var session = Session(host);
        var added = session.CaptureState().EditorState.Selection.Single();
        var document = AttachedDocument(session);
        var visual = document.VisualModel.VisualStates.Single(v => v.Id == added);
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            added, visual.Position + new VectorD(0, 30)))).IsCommitted);
        await session.WaitForIdleAsync();
        await A24Select(host, source);
        await A24Select(host, source, added);
        var before = document.CaptureSnapshot();
        var referenceBounds = A24PanelBody(session.CaptureState().CurrentScene!, source).Bounds;
        Assert.NotEqual(default, session.CaptureState().CurrentScene!.GetVisualRegion(source)!.LocalToSceneTransform.OffsetY);
        Assert.True((await host.AlignAsync(horizontal))!.IsCommitted);
        var scene = session.CaptureState().CurrentScene!;
        Assert.Equal(referenceBounds, A24PanelBody(scene, source).Bounds);
        var moved = A24PanelBody(scene, added).Bounds;
        Assert.Equal(horizontal ? Center(referenceBounds).Y : Center(referenceBounds).X, horizontal ? Center(moved).Y : Center(moved).X);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), document.SemanticModel.ProfileAssignments);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual)]
    public async Task AlignmentRetainsConnectorModesAndUnrelatedRoutes(ConnectorRoutingType mode)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("components")));
        await host.InitializeAsync("alignment-routing", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var connector = new VisualStateId("v-flow-0");
        if (mode != ConnectorRoutingType.Automatic)
        {
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId, document.Revision, connector, mode))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        await A24Select(host, new VisualStateId("v-end1"));
        await A24Select(host, new VisualStateId("v-end1"), new("v-a"));
        var before = document.CaptureSnapshot();
        Assert.True((await host.AlignAsync(true))!.IsCommitted);
        var after = document.CaptureSnapshot();
        var oldRecord = before.VisualModel.EnumerateConnectorRoutingRecords().Single(r => r.VisualStateId == connector);
        var current = after.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
        Assert.Equal(mode, current.Single(r => r.VisualStateId == connector).RoutingType);
        Assert.Equal(oldRecord.ManualDefinition, current.Single(r => r.VisualStateId == connector).ManualDefinition);
        foreach (var record in before.VisualModel.EnumerateConnectorRoutingRecords().Where(r => r.VisualStateId.Value is not ("v-flow-0" or "v-flow-1")))
            Assert.Equal(record, current.Single(r => r.VisualStateId == record.VisualStateId));
        await using var movement = A23Host(before);
        await movement.InitializeAsync("alignment-route-reference", "container");
        var movementSession = Session(movement);
        Assert.True((await movementSession.ExecuteAsync(new MoveVisualStatesCommand(before.DocumentId, before.Revision,
            [new(new("v-a"), after.VisualModel.VisualStates.Single(v => v.Id.Value == "v-a").Position, VisualPlacementMode.Pinned)]))).IsCommitted);
        await movementSession.WaitForIdleAsync();
        A23PresentationEqual(after, AttachedDocument(movementSession).CaptureSnapshot());
        await host.UndoAsync();
        await movement.UndoAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        A23PresentationEqual(AttachedDocument(movementSession).CaptureSnapshot(), document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Fact]
    public async Task AlignmentIconsHaveAccessibleLabelsAndRemainStableDuringHover()
    {
        using var culture = new ModelerCultureScope("en");
        await using var test = await PanComponentFixture.CreateAsync(document: A23Snapshot(A23Edges("linear")));
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            await A24Select(test.Host, new VisualStateId("v-b"));
            await A24Select(test.Host, new VisualStateId("v-b"), new("v-a"));
        });
        await test.DrainAsync();
        var markup = ActionBarMarkup(await test.MarkupAsync());
        foreach (var name in new[] { "Align horizontally to first selected element", "Align vertically to first selected element", "Auto Arrange" })
        {
            Assert.Contains("aria-label=\"" + name + "\"", markup);
            Assert.Contains("title=\"" + name + "\"", markup);
            Assert.DoesNotContain(">" + name + "</button>", markup);
        }
        var layoutStart = markup.IndexOf("toolbar-group layout-controls", StringComparison.Ordinal);
        var layoutControls = markup[layoutStart..markup.IndexOf("</div>", layoutStart, StringComparison.Ordinal)];
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Count(layoutControls, "<svg "));
        var presentation = ActionBarSnapshot(test.Activator.Canvas);
        Assert.True(ActionBarEnabled(presentation, "CanAlignHorizontal"));
        Assert.True(ActionBarEnabled(presentation, "CanAlignVertical"));
        var session = Session(test.Host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        await A24DuringPausedSceneAsync(test.Host,
            () => test.Renderer.Dispatcher.InvokeAsync(() => InteractionController(test.Host).PointerMovedAsync(
                session.CaptureState().CurrentScene!.ViewportTransform.TransformPoint(new(651, 230))).AsTask()),
            async () =>
            {
                await test.DrainAsync();
                Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas));
                Assert.Equal(markup, ActionBarMarkup(await test.MarkupAsync()));
                Assert.Null(await test.Host.AlignAsync(true));
            });
        await test.DrainAsync();
        Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.AlignAsync(true).AsTask());
        await test.DrainAsync();
        Assert.False(ActionBarEnabled(ActionBarSnapshot(test.Activator.Canvas), "CanAlignHorizontal"));
    }
}
