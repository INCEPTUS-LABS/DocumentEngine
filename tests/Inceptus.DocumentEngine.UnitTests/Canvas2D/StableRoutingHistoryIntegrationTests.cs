using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class StableRoutingHistoryIntegrationTests
{
    private static readonly VisualStateId FlowA = new("v:flow-a");
    private static readonly VisualStateId FlowB = new("v:flow-b");

    [Theory]
    [InlineData(VisualPlacementMode.Automatic, false)]
    [InlineData(VisualPlacementMode.Automatic, true)]
    [InlineData(VisualPlacementMode.Manual, false)]
    [InlineData(VisualPlacementMode.Manual, true)]
    public async Task UnpinnedNodeMoveAndResizeReplayOwnExactSavedGeometry(
        VisualPlacementMode originalMode, bool resize)
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync(originalMode);
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Manual));
        var id = new VisualStateId("v:a-source");
        var originalVisual = fixture.Document.CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == id);
        var original = Scope(fixture).Geometry.Nodes.Single(node => node.VisualStateId == id);
        var history = new HistoryManager(fixture.Document);
        var target = original.LocalBounds.Translate(new VectorD(40, 400));
        ICommand command = resize
            ? new ResizeVisualStateCommand(fixture.Document.DocumentId, fixture.Document.Revision, id,
                new RectD(target.X, target.Y, target.Width + 35, target.Height + 25), VisualPlacementMode.Pinned)
            : new MoveVisualStateCommand(fixture.Document.DocumentId, fixture.Document.Revision, id,
                target.TopLeft, VisualPlacementMode.Pinned);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, command));
        var edited = Scope(fixture).Geometry.Nodes.Single(node => node.VisualStateId == id);
        Assert.NotEqual(original, edited);
        await Execute(fixture, Manual(fixture, FlowA, new PointD(380, 550)));
        var definition = Record(fixture, FlowA).ManualDefinition!.Value;

        AssertCommitted(await history.UndoAsync(fixture.Processor));
        Assert.Equal(originalVisual, fixture.Document.CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == id));
        Assert.Equal(original, Scope(fixture).Geometry.Nodes.Single(node => node.VisualStateId == id));
        Assert.Equal(definition.AsEnumerable(), Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(edited, Scope(fixture).Geometry.Nodes.Single(node => node.VisualStateId == id));
        Assert.Equal(definition.AsEnumerable(), Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
    }

    [Fact]
    public async Task SnapshotUndoPreservesCurrentSurvivorManualDefinitionAndHeight()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await EnableSpatialRegions(fixture);
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Manual));
        await Execute(fixture, Height(fixture, 600));
        var history = new HistoryManager(fixture.Document);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, CreateNode(fixture, "snapshot-node")));

        // A direct core Command has no HistoryManager; the replay must still merge its current owned state.
        await Execute(fixture, Height(fixture, 700));
        await Execute(fixture, Manual(fixture, FlowA, new PointD(280, 155)));
        var current = Record(fixture, FlowA);
        AssertCommitted(await history.UndoAsync(fixture.Processor));
        Assert.Equal(current.ManualDefinition!.Value.AsEnumerable(), Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
        Assert.Equal(700, Unassigned(fixture).ExpandedHeight);
        Assert.DoesNotContain(fixture.Document.CaptureSnapshot().SemanticModel.Elements,
            element => element.Id == new SemanticElementId("snapshot-node"));
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(700, Unassigned(fixture).ExpandedHeight);
        Assert.Equal(current.ManualDefinition.Value.AsEnumerable(), Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
    }

    [Fact]
    public async Task DeleteUndoRestoresAbsentSeedAtTailAndPreservesEditedSurvivor()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Manual));
        await Execute(fixture, Type(fixture, FlowB, ConnectorRoutingType.Manual));
        await Execute(fixture, Manual(fixture, FlowA, new PointD(270, 145)));
        var absentSeed = Record(fixture, FlowA);
        var history = new HistoryManager(fixture.Document);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, new DeleteBpmnSequenceFlowCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, new SemanticElementId("flow-a"), FlowA,
            new SemanticElementId("a-source"), new SemanticElementId("a-target"),
            new ConnectorAnchorId("a-source:source"), new ConnectorAnchorId("a-target:target"))));
        await Execute(fixture, Manual(fixture, FlowB, new PointD(285, 370)));
        var survivor = Record(fixture, FlowB);

        AssertCommitted(await history.UndoAsync(fixture.Processor));
        Assert.Equal([FlowB, FlowA], Scope(fixture).Connectors.Select(static record => record.VisualStateId));
        Assert.Equal(absentSeed, Record(fixture, FlowA));
        Assert.Equal(survivor, Record(fixture, FlowB));
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(survivor, Assert.Single(Scope(fixture).Connectors));
    }

    [Fact]
    public async Task HeightUndoRedoOwnsHeightWhileManualEditsPreserveBothBranches()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await EnableSpatialRegions(fixture);
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Manual));
        var history = new HistoryManager(fixture.Document);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, Height(fixture, 600)));
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, Height(fixture, 700)));
        AssertCommitted(await history.UndoAsync(fixture.Processor));
        var branches = history.CaptureStatus();
        Assert.True(branches.CanUndo && branches.CanRedo);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, Manual(fixture, FlowA, new PointD(290, 160))));
        Assert.Equal(branches, history.CaptureStatus());
        var definition = Record(fixture, FlowA).ManualDefinition;
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(700, Unassigned(fixture).ExpandedHeight);
        Assert.Equal(definition!.Value.AsEnumerable(), Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
    }

    [Fact]
    public async Task FinalEqualModeCompoundReturnsNoChangeAndPreservesBothBranches()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Manual));
        await Execute(fixture, Manual(fixture, FlowA, new PointD(280, 155)));
        var history = new HistoryManager(fixture.Document);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, CreateNode(fixture, "first")));
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, CreateNode(fixture, "second")));
        AssertCommitted(await history.UndoAsync(fixture.Processor));
        var before = fixture.Document.CaptureSnapshot();
        var branches = history.CaptureStatus();
        fixture.Policy.Searches.Clear();
        var result = await history.ExecuteAsync(fixture.Processor, new CompoundDocumentCommand(
            before.DocumentId, before.Revision,
            [Type(fixture, FlowA, ConnectorRoutingType.Straight), Type(fixture, FlowA, ConnectorRoutingType.Manual)]));
        Assert.Equal(HistoryOperationStatus.NoChange, result.Status);
        Assert.Same(before, fixture.Document.CaptureSnapshot());
        Assert.Equal(branches, history.CaptureStatus());
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(HistoryOperationStatus.NoChange,
            (await history.ExecuteAsync(fixture.Processor, Type(fixture, FlowA, ConnectorRoutingType.Manual))).Status);
        Assert.Equal(branches, history.CaptureStatus());
    }

    [Fact]
    public async Task SceneOnlyAssignmentPromotesArtifactInvalidationWhenSavedGeometryChanges()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await EnableSpatialRegions(fixture);
        await Execute(fixture, Height(fixture, 600));
        var before = fixture.Document.CaptureSnapshot();
        var command = new UnassignOrganizationalElementCommand(before.DocumentId, before.Revision,
            new SemanticElementId("a-source"));
        Assert.Equal(PipelineInvalidation.Scene, CommandPipelineInvalidation.Resolve(command));

        var result = await fixture.Processor.ExecuteAsync(fixture.Document, command);

        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(CommandPipelineInvalidation.WithoutNodeLayout, result.CommittedEvent!.PipelineInvalidation);
        Assert.NotEqual(before.VisualModel.RoutingScopes!.Value.AsEnumerable(),
            fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.AsEnumerable());
        Assert.Equal(before.Revision.Value + 1, fixture.Document.Revision.Value);
    }

    [Fact]
    public async Task MixedNodeDeletionMoveAndTypeCommitOnceAndReplayExactStructure()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var history = new HistoryManager(fixture.Document);
        var before = fixture.Document.CaptureSnapshot();
        var removedId = new SemanticElementId("b-source");
        var nodeVisual = before.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == removedId);
        var movedVisual = before.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == new SemanticElementId("a-source"));
        var deletion = new DeleteBpmnFlowNodeCommand(before.DocumentId, before.Revision, removedId, nodeVisual.Id);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, new CompoundDocumentCommand(
            before.DocumentId, before.Revision,
            [deletion, new MoveVisualStateCommand(before.DocumentId, before.Revision, movedVisual.Id,
                new PointD(35, 30), VisualPlacementMode.Manual), Type(fixture, FlowA, ConnectorRoutingType.Manual)])));
        var committed = fixture.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Value + 1, committed.Revision.Value);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.DoesNotContain(committed.SemanticModel.Elements, element => element.Id == removedId);
        Assert.Equal(new PointD(35, 30), committed.VisualModel.VisualStates.Single(visual => visual.Id == movedVisual.Id).Position);
        Assert.Equal(ConnectorRoutingType.Manual, Record(fixture, FlowA).RoutingType);
        await Execute(fixture, Manual(fixture, FlowA, new PointD(280, 155)));

        AssertCommitted(await history.UndoAsync(fixture.Processor));
        var undone = fixture.Document.CaptureSnapshot();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), undone.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), undone.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), undone.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(ConnectorRoutingType.Automatic, Record(fixture, FlowA).RoutingType);
        Assert.Equal([new PointD(280, 155)], Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(), fixture.Document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(ConnectorRoutingType.Manual, Record(fixture, FlowA).RoutingType);
        Assert.Equal([new PointD(280, 155)], Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
    }

    [Fact]
    public async Task FirstManualCaptureUsesIntermediateAutomaticRouteWithinOneCompoundCommit()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Straight));
        await Execute(fixture, new CreateBpmnTaskCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            new SemanticElementId("obstacle"), new VisualStateId("v:obstacle"), new PointD(260, 30),
            new SizeD(120, 80), "OBSTACLE", "Obstacle", 100, VisualPlacementMode.Pinned));
        Assert.Null(Record(fixture, FlowA).ManualDefinition);
        Assert.Equal(2, Record(fixture, FlowA).Path.Length);
        fixture.Policy.Searches.Clear();
        var revision = fixture.Document.Revision;
        var history = new HistoryManager(fixture.Document);

        AssertCommitted(await history.ExecuteAsync(fixture.Processor, new CompoundDocumentCommand(
            fixture.Document.DocumentId, revision,
            [Type(fixture, FlowA, ConnectorRoutingType.Automatic), Type(fixture, FlowA, ConnectorRoutingType.Manual)])));

        var manual = Record(fixture, FlowA);
        Assert.Equal(ConnectorRoutingType.Manual, manual.RoutingType);
        Assert.NotEmpty(manual.ManualDefinition!.Value);
        Assert.Equal(manual.Path.Skip(1).Take(manual.Path.Length - 2), manual.ManualDefinition.Value.AsEnumerable());
        Assert.Equal([FlowA], fixture.Policy.Searches);
        Assert.Equal(revision.Value + 1, fixture.Document.Revision.Value);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task AlreadySatisfiedTypeReplayConsumesOnlyItsHistoryCursor()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var history = new HistoryManager(fixture.Document);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, Type(fixture, FlowA, ConnectorRoutingType.Manual)));
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Automatic));
        var before = fixture.Document.CaptureSnapshot();

        var replay = await history.UndoAsync(fixture.Processor);

        Assert.Equal(HistoryOperationStatus.NoChange, replay.Status);
        Assert.Same(before, fixture.Document.CaptureSnapshot());
        Assert.False(history.CaptureStatus().CanUndo);
        Assert.True(history.CaptureStatus().CanRedo);
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(ConnectorRoutingType.Manual, Record(fixture, FlowA).RoutingType);
    }

    [Fact]
    public async Task AtomicTargetAnchorCreationForwardsExplicitRoutingMode()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var id = new VisualStateId("v:anchor-flow");
        var command = new CreateBpmnSequenceFlowWithTargetAnchorCommand(fixture.Document.DocumentId,
            fixture.Document.Revision, new SemanticElementId("anchor-flow"), id,
            new SemanticElementId("a-target"), new SemanticElementId("b-source"),
            new ConnectorAnchorId("a-target:source"), new VisualStateId("v:b-source"),
            new ConnectorAnchorId("new:target"), ConnectorAnchorSide.Left, 1, ConnectorRoutingType.Straight);
        var history = new HistoryManager(fixture.Document);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, command));
        Assert.Equal(ConnectorRoutingType.Straight, Record(fixture, id).RoutingType);
        Assert.Equal(2, Record(fixture, id).Path.Length);
        AssertCommitted(await history.UndoAsync(fixture.Processor));
        Assert.DoesNotContain(fixture.Document.CaptureSnapshot().VisualModel.VisualStates
            .Single(visual => visual.Id == new VisualStateId("v:b-source")).ConnectorAnchors,
            anchor => anchor.Id == new ConnectorAnchorId("new:target"));
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(ConnectorRoutingType.Straight, Record(fixture, id).RoutingType);
    }

    [Fact]
    public async Task CompoundStructuralChildPreservesPreparedCapabilityForLaterManualChild()
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        await Execute(fixture, Type(fixture, FlowA, ConnectorRoutingType.Manual));
        var history = new HistoryManager(fixture.Document);
        var result = await history.ExecuteAsync(fixture.Processor, new CompoundDocumentCommand(
            fixture.Document.DocumentId, fixture.Document.Revision,
            [CreateNode(fixture, "compound-node"), Manual(fixture, FlowA, new PointD(285, 150))]));
        AssertCommitted(result);
        AssertCommitted(await history.UndoAsync(fixture.Processor));
        Assert.Equal([new PointD(285, 150)], Record(fixture, FlowA).ManualDefinition!.Value.AsEnumerable());
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Manual)]
    [InlineData(ConnectorRoutingType.Straight)]
    public async Task ExplicitCreationTypeAndRestoredIdentityUseSamePersistentTransaction(ConnectorRoutingType mode)
    {
        var fixture = await StableRoutingPreparationTests.Fixture.CreateAsync();
        var id = new VisualStateId("v:new-flow");
        var history = new HistoryManager(fixture.Document);
        var command = new CreateBpmnSequenceFlowCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            new SemanticElementId("new-flow"), id, new SemanticElementId("a-target"), new SemanticElementId("b-source"),
            new ConnectorAnchorId("a-target:source"), new ConnectorAnchorId("b-source:target"), mode);
        AssertCommitted(await history.ExecuteAsync(fixture.Processor, command));
        Assert.Equal(mode, Record(fixture, id).RoutingType);
        Assert.Empty(fixture.Document.CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == id).Route);
        var created = Record(fixture, id);
        AssertCommitted(await history.UndoAsync(fixture.Processor));
        Assert.DoesNotContain(Scope(fixture).Connectors, record => record.VisualStateId == id);
        AssertCommitted(await history.RedoAsync(fixture.Processor));
        Assert.Equal(created, Record(fixture, id));
        Assert.Equal(id, Scope(fixture).Connectors[^1].VisualStateId);
    }

    private static ScopeRoutingSnapshot Scope(StableRoutingPreparationTests.Fixture fixture) =>
        Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);

    private static ConnectorRoutingRecord Record(StableRoutingPreparationTests.Fixture fixture, VisualStateId id) =>
        Scope(fixture).Connectors.Single(record => record.VisualStateId == id);

    private static SetConnectorRoutingTypeCommand Type(StableRoutingPreparationTests.Fixture fixture,
        VisualStateId id, ConnectorRoutingType mode) => new(fixture.Document.DocumentId, fixture.Document.Revision, id, mode);

    private static UpdateConnectionRouteCommand Manual(StableRoutingPreparationTests.Fixture fixture,
        VisualStateId id, PointD bend)
    {
        var record = Record(fixture, id);
        return new(fixture.Document.DocumentId, fixture.Document.Revision, id, [record.Path[0], bend, record.Path[^1]]);
    }

    private static CreateBpmnTaskCommand CreateNode(StableRoutingPreparationTests.Fixture fixture, string name) =>
        new(fixture.Document.DocumentId, fixture.Document.Revision, new SemanticElementId(name), new VisualStateId($"v:{name}"),
            new PointD(180, 390), new SizeD(120, 80), name, name, 100, VisualPlacementMode.Pinned);

    private static SetOrganizationalRegionExpandedHeightCommand Height(StableRoutingPreparationTests.Fixture fixture, double height)
    {
        var scope = Scope(fixture);
        return new(fixture.Document.DocumentId, fixture.Document.Revision, scope.ScopeId,
            Unassigned(fixture).Id, height);
    }

    private static SpatialRegionGeometrySnapshot Unassigned(StableRoutingPreparationTests.Fixture fixture) =>
        Scope(fixture).Geometry.Regions.Single(region => region.ContainerSemanticElementId is null);

    private static async Task EnableSpatialRegions(StableRoutingPreparationTests.Fixture fixture)
    {
        await Execute(fixture, new SetModelProfileAvailabilityCommand(fixture.Document.DocumentId,
            fixture.Document.Revision, [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
        await Execute(fixture, new CreateOrganizationalPoolCommand(fixture.Document.DocumentId,
            fixture.Document.Revision, new SemanticElementId("height-pool"), Scope(fixture).ScopeId,
            OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Height Pool"));
    }

    private static async Task Execute(StableRoutingPreparationTests.Fixture fixture, ICommand command)
    {
        var result = await fixture.Processor.ExecuteAsync(fixture.Document, command);
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    private static void AssertCommitted(HistoryOperationResult result) =>
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
}
