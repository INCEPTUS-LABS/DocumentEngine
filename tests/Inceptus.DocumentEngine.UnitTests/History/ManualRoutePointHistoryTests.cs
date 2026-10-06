using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class ManualRoutePointHistoryTests
{
    private static readonly VisualStateId ConnectorId = new("v:flow-a");

    [Fact]
    public async Task UndoMoveAfterModeAndAddRestoresPointInsteadOfSkippingToModeChange()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var document = fixture.Document;
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(fixture.Processor, new SetConnectorRoutingTypeCommand(
            document.DocumentId, document.Revision, ConnectorId, ConnectorRoutingType.Manual))).IsCommitted);
        var manual = Route(fixture);
        var pointA = new PointD(270, 150);
        var pointB = new PointD(310, 190);
        PointD[] added = [manual.Path[0], pointA, manual.Path[^1]];
        Assert.True((await history.ExecuteAsync(fixture.Processor, new UpdateConnectionRouteCommand(
            document.DocumentId, document.Revision, ConnectorId, added))).IsCommitted);
        var beforeMove = document.Revision;
        Assert.True((await history.ExecuteAsync(fixture.Processor, new UpdateConnectionRouteCommand(
            document.DocumentId, document.Revision, ConnectorId, [manual.Path[0], pointB, manual.Path[^1]]))).IsCommitted);
        Assert.Equal(beforeMove.Increment(), document.Revision);
        Assert.Equal(pointB, Assert.Single(Route(fixture).ManualDefinition!.Value));
        var entriesAfterMove = history.CaptureStatus().EntryCount;

        Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);

        var undone = Route(fixture);
        Assert.True(undone.RoutingType == ConnectorRoutingType.Manual &&
            undone.ManualDefinition!.Value.SequenceEqual([pointA]),
            $"Undo must restore P1 to A and keep Manual; actual mode={undone.RoutingType}, " +
            $"point={undone.ManualDefinition!.Value[0]}, History entries after mode/add/move={entriesAfterMove}.");
        Assert.Equal(added, undone.Path.AsEnumerable());
        Assert.Equal(3, entriesAfterMove);
    }

    [Fact]
    public async Task CanonicalChainHasFourDistinctOperationsAndExactUndoRedoStates()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var document = fixture.Document;
        var history = new HistoryManager(document);
        var identities = document.CaptureSnapshot().VisualModel.VisualStates;
        var initial = Route(fixture);
        await Commit(Type(fixture, ConnectorRoutingType.Manual), ConnectorRoutingHistoryOperation.ChangeRoutingMode);
        var manual = Route(fixture);
        await Commit(Edit(fixture, new PointD(270, 150)), ConnectorRoutingHistoryOperation.AddManualRoutePoint);
        var added = Route(fixture);
        await Commit(Edit(fixture, new PointD(310, 190)), ConnectorRoutingHistoryOperation.MoveManualRoutePoint);
        var moved = Route(fixture);
        // The real context action represents removal of the last point as an empty full route.
        await Commit(new UpdateConnectionRouteCommand(document.DocumentId, document.Revision, ConnectorId, []),
            ConnectorRoutingHistoryOperation.RemoveManualRoutePoint);
        var removed = Route(fixture);
        Assert.Empty(removed.ManualDefinition!.Value);

        foreach (var expected in new[] { moved, added, manual })
        {
            Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
            Assert.Equal(expected, Route(fixture));
        }
        Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(ConnectorRoutingType.Automatic, Route(fixture).RoutingType);
        Assert.Equal(initial.Path.AsEnumerable(), Route(fixture).Path.AsEnumerable());
        Assert.False(history.CaptureStatus().CanUndo);
        foreach (var expected in new[] { manual, added, moved, removed })
        {
            Assert.True((await history.RedoAsync(fixture.Processor)).IsCommitted);
            Assert.Equal(expected, Route(fixture));
        }
        Assert.Equal(new HistoryStatus(4, true, false), history.CaptureStatus());
        Assert.Equal(12UL, document.Revision.Value);
        Assert.Equal(identities.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());

        async Task Commit(ICommand command, ConnectorRoutingHistoryOperation expected)
        {
            var before = document.CaptureSnapshot();
            var count = history.CaptureStatus().EntryCount;
            Assert.True((await history.ExecuteAsync(fixture.Processor, command)).IsCommitted);
            Assert.Equal(before.Revision.Increment(), document.Revision);
            Assert.Equal(count + 1, history.CaptureStatus().EntryCount);
            var prepared = new HistoryCoordinator([SetConnectorRoutingTypeHistoryPolicy.Registration,
                UpdateConnectionRouteHistoryPolicy.Registration]).PrepareRecord(new HistoryStore(), command,
                    before, document.CaptureSnapshot());
            Assert.True(prepared.Succeeded);
            Assert.Equal(expected, Assert.Single(prepared.Mutation!.ProposedState.Entries).RoutingOperation);
        }
    }

    [Fact]
    public async Task TwoPointsKeepExactOrderThroughAllPointOperations()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var history = new HistoryManager(fixture.Document);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Type(fixture, ConnectorRoutingType.Manual))).IsCommitted);
        var states = new List<ConnectorRoutingRecord> { Route(fixture) };
        PointD p1 = new(270, 150), p2 = new(380, 160), moved = new(290, 195);
        foreach (var points in new PointD[][] { [p1], [p1, p2], [moved, p2], [moved] })
        {
            Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, points))).IsCommitted);
            states.Add(Route(fixture));
        }
        foreach (var expected in states.SkipLast(1).Reverse())
        {
            Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
            Assert.Equal(expected, Route(fixture));
        }
        foreach (var expected in states.Skip(1))
        {
            Assert.True((await history.RedoAsync(fixture.Processor)).IsCommitted);
            Assert.Equal(expected, Route(fixture));
        }
        Assert.Equal(5, history.CaptureStatus().EntryCount);
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    public async Task LeavingManualAndReturningRestoresExactAuthoredRoute(ConnectorRoutingType target)
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var history = new HistoryManager(fixture.Document);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Type(fixture, ConnectorRoutingType.Manual))).IsCommitted);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, new PointD(270, 150), new(380, 160)))).IsCommitted);
        var manual = Route(fixture);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Type(fixture, target))).IsCommitted);
        var other = Route(fixture);
        Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(manual, Route(fixture));
        Assert.True((await history.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(other, Route(fixture));
    }

    [Fact]
    public async Task NoOpRejectedAndStaleCommandsPreserveDocumentAndHistoryThenValidEditStillWorks()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var document = fixture.Document;
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Type(fixture, ConnectorRoutingType.Manual))).IsCommitted);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, new PointD(270, 150)))).IsCommitted);
        var stale = Edit(fixture, new PointD(310, 190));
        Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, new PointD(320, 200)))).IsCommitted);
        Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
        var before = document.CaptureSnapshot();
        var status = history.CaptureStatus();
        foreach (var command in new ICommand[] { Type(fixture, ConnectorRoutingType.Manual), Edit(fixture, new PointD(270, 150)) })
            Assert.Equal(HistoryOperationStatus.NoChange, (await history.ExecuteAsync(fixture.Processor, command)).Status);
        foreach (var command in new ICommand[] { stale, Edit(fixture, new PointD(-1, 150)),
            new UpdateConnectionRouteCommand(document.DocumentId, document.Revision, ConnectorId,
                [new(1, 1), new(310, 190), Route(fixture).Path[^1]]) })
        {
            var rejected = await history.ExecuteAsync(fixture.Processor, command);
            Assert.False(rejected.Succeeded);
            Assert.Null(rejected.CommittedRevision);
        }
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(status, history.CaptureStatus());
        Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, new PointD(350, 220)))).IsCommitted);
        Assert.False(history.CaptureStatus().CanRedo);
        Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(new PointD(270, 150), Assert.Single(Route(fixture).ManualDefinition!.Value));
        Assert.True((await history.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(new PointD(350, 220), Assert.Single(Route(fixture).ManualDefinition!.Value));
    }

    [Fact]
    public async Task AutomaticRerouteAddsOnlyNodeMoveAndUnrelatedPropertyUndoPreservesManualRoute()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var document = fixture.Document;
        var history = new HistoryManager(document);
        var automatic = Route(fixture);
        Assert.True((await history.ExecuteAsync(fixture.Processor, new MoveVisualStateCommand(document.DocumentId,
            document.Revision, new VisualStateId("v:a-source"), new(40, 110), VisualPlacementMode.Pinned))).IsCommitted);
        Assert.NotEqual(automatic.Path.AsEnumerable(), Route(fixture).Path.AsEnumerable());
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Type(fixture, ConnectorRoutingType.Manual))).IsCommitted);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, new PointD(270, 150)))).IsCommitted);
        var manual = Route(fixture);
        var task = document.CaptureSnapshot().SemanticModel.Elements.Single(element => element.Id.Value == "b-source");
        var property = task.Properties.First(pair => pair.Value.Kind == PropertyValueKind.Text);
        Assert.True((await history.ExecuteAsync(fixture.Processor, new UpdateSemanticElementPropertyCommand(document.DocumentId,
            document.Revision, task.Id, property.Key, PropertyValue.FromText("Changed property")))).IsCommitted);
        Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(manual, Route(fixture));
        Assert.Equal(property.Value, document.CaptureSnapshot().SemanticModel.Elements.Single(element => element.Id == task.Id)
            .Properties[property.Key]);
    }

    [Fact]
    public async Task NativeV2RoundtripKeepsRoutesAndFreshHistoryCanAddMoveAndRemove()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var history = new HistoryManager(fixture.Document);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Type(fixture, ConnectorRoutingType.Manual))).IsCommitted);
        Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, new PointD(270, 150), new(380, 160)))).IsCommitted);
        var saved = fixture.Document.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(saved);
        var json = System.Text.Json.Nodes.JsonNode.Parse(bytes.AsSpan())!;
        Assert.Equal(2, json["formatVersion"]!.GetValue<int>());
        Assert.DoesNotContain("history", System.Text.Encoding.UTF8.GetString(bytes.AsSpan()), StringComparison.OrdinalIgnoreCase);
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory(), fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(opened.Succeeded);
        Assert.Equal(saved, opened.Document!.CaptureSnapshot());
        fixture = fixture with { Document = opened.Document };
        history = new HistoryManager(opened.Document);
        Assert.Equal(new HistoryStatus(0, false, false), history.CaptureStatus());
        var states = new List<ConnectorRoutingRecord> { Route(fixture) };
        foreach (var points in new PointD[][] { [new(270, 150), new(380, 160), new(410, 180)],
            [new(290, 195), new(380, 160), new(410, 180)], [new(290, 195), new(410, 180)] })
        {
            Assert.True((await history.ExecuteAsync(fixture.Processor, Edit(fixture, points))).IsCommitted);
            states.Add(Route(fixture));
        }
        foreach (var expected in states.SkipLast(1).Reverse())
        {
            Assert.True((await history.UndoAsync(fixture.Processor)).IsCommitted);
            Assert.Equal(expected, Route(fixture));
        }
        foreach (var expected in states.Skip(1))
        {
            Assert.True((await history.RedoAsync(fixture.Processor)).IsCommitted);
            Assert.Equal(expected, Route(fixture));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompoundRouteEditsOwnOnlyTheirExplicitDefinitionInOneEntry(bool changesMode)
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var document = fixture.Document;
        if (!changesMode)
            Assert.True((await fixture.Processor.ExecuteAsync(document, Type(fixture, ConnectorRoutingType.Manual))).IsCommitted);
        var history = new HistoryManager(document);
        var original = Route(fixture);
        ICommand first = changesMode ? Type(fixture, ConnectorRoutingType.Manual) : Edit(fixture, new PointD(270, 150));
        var command = new CompoundDocumentCommand(document.DocumentId, document.Revision,
            [first, Edit(fixture, new PointD(310, 190))]);
        Assert.True((await history.ExecuteAsync(fixture.Processor, command)).IsCommitted);
        var edited = Route(fixture);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
        var undo = await history.UndoAsync(fixture.Processor);
        Assert.True(undo.IsCommitted, string.Join("; ", undo.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.Equal(original.RoutingType, Route(fixture).RoutingType);
        Assert.Equal(original.Path.AsEnumerable(), Route(fixture).Path.AsEnumerable());
        Assert.True((await history.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(edited, Route(fixture));
    }

    private static SetConnectorRoutingTypeCommand Type(StableRoutingPreparationTests.Fixture fixture, ConnectorRoutingType type) =>
        new(fixture.Document.DocumentId, fixture.Document.Revision, ConnectorId, type);

    private static UpdateConnectionRouteCommand Edit(StableRoutingPreparationTests.Fixture fixture, params PointD[] points) =>
        new(fixture.Document.DocumentId, fixture.Document.Revision, ConnectorId, [Route(fixture).Path[0], .. points, Route(fixture).Path[^1]]);

    private static ConnectorRoutingRecord Route(StableRoutingPreparationTests.Fixture fixture) =>
        fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value
            .SelectMany(scope => scope.Connectors).Single(route => route.VisualStateId == ConnectorId);
}
