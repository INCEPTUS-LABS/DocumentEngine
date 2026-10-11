using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly string[] AssignmentSourceOrder = ["a", "x1", "x2", "x3", "b", "y1", "y2"];

    [Theory]
    [InlineData("new")]
    [InlineData("below")]
    [InlineData("above")]
    public async Task A22AssignmentAppendsPoolAfterExistingLanesAndClosesVacatedSlotAbove(string scenario)
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-assignment-order", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var poolId = new SemanticElementId(scenario == "new" ? "new-pool" : "supplier");
        if (scenario == "new")
        {
            var initial = document.CaptureSnapshot();
            Assert.True((await session.ExecuteAsync(new CreateOrganizationalPoolCommand(document.DocumentId, document.Revision,
                poolId, initial.SemanticModel.RootScopeId, OrganizationalPoolCreationMode.Empty, "New Pool"))).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(AssignmentRegion(initial, "sales"), AssignmentRegion(document.CaptureSnapshot(), "sales"));
            Assert.Equal(AssignmentRegion(initial, "warehouse"), AssignmentRegion(document.CaptureSnapshot(), "warehouse"));
        }
        else if (scenario == "above")
        {
            Assert.True((await session.ExecuteAsync(new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision,
                poolId, OrganizationalPoolMoveDirection.Up))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var oldGroupBottom = AssignmentRegion(before, "warehouse").ContentBounds!.Value.Bottom;
        var height = AssignmentRegion(before, poolId.Value).ExpandedHeight;
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(semanticSceneSelection: poolId));
        await session.WaitForIdleAsync();
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(poolId));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        draft.DataFields.Single(field => field.FieldId.Value == "participant").EditorValue = "company";
        var result = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        Assert.Equal("company", result.Authoritative!.DataFields.Single(field => field.FieldId.Value == "participant").EditorValue);
        var offset = scenario == "above" ? -(height + 40d) : 0d;
        foreach (var id in new[] { "sales", "warehouse" })
        {
            var original = AssignmentRegion(before, id);
            var current = AssignmentRegion(after, id);
            Assert.Equal(original.ContentBounds!.Value.Top + offset, current.ContentBounds!.Value.Top);
            Assert.Equal(original.ExpandedHeight, current.ExpandedHeight);
            Assert.Equal(original.LocalToScopeTransform!.Value.OffsetY + offset, current.LocalToScopeTransform!.Value.OffsetY);
        }
        Assert.Equal(oldGroupBottom + offset, AssignmentRegion(after, poolId.Value).ContentBounds!.Value.Top);
        Assert.Equal(height, AssignmentRegion(after, poolId.Value).ExpandedHeight);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.SemanticModel.Elements.Where(element => element.Id != poolId), after.SemanticModel.Elements.Where(element => element.Id != poolId));
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company A");
        var cursor = oldGroupBottom + offset + height + 40d;
        foreach (var region in before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions
                     .Where(region => region.ContainerSemanticElementId is { } id && id != poolId &&
                         id.Value is not "sales" and not "warehouse").OrderBy(region => region.ContentBounds!.Value.Top))
        {
            var current = AssignmentRegion(after, region.ContainerSemanticElementId!.Value);
            Assert.Equal(scenario == "above" ? cursor : Math.Max(cursor, region.ContentBounds!.Value.Top), current.ContentBounds!.Value.Top);
            Assert.Equal(region.ExpandedHeight, current.ExpandedHeight);
            cursor = current.ContentBounds.Value.Bottom + 40d;
        }
        await host.UndoAsync();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        await host.RedoAsync();
        Assert.Equal(after.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
        Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        var restored = document.CaptureSnapshot();
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(restored).AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = CreatePropertiesUxHost(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("a22-assignment-reopened", "container");
        Assert.Equal(restored, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    private static SpatialRegionGeometrySnapshot AssignmentRegion(DocumentSnapshot document, string pool) =>
        document.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions.Single(region => region.ContainerSemanticElementId?.Value == pool);

    [Theory]
    [InlineData("a")]
    [InlineData("x1")]
    [InlineData("x2")]
    [InlineData("x3")]
    [InlineData("b")]
    public async Task A22AssignmentFromAboveClosesSourceSlotAcrossUnequalParticipantBlocks(string source)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("a22-assignment-source-gap", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
            new(source), new("participant-Y")))).IsCommitted);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var expectedOrder = AssignmentSourceOrder
            .Where(id => id != source).Append(source).Append("c").ToArray();
        Assert.Equal(expectedOrder, OrderingRegions(after).Select(region => region.ContainerSemanticElementId!.Value));
        var cursor = AssignmentRegion(before, "a").ContentBounds!.Value.Top;
        SemanticElementId? previousParticipant = null;
        foreach (var id in expectedOrder)
        {
            var original = AssignmentRegion(before, id);
            var current = AssignmentRegion(after, id);
            var participant = OrganizationalSemantics.GetParticipantId(after.SemanticModel.Elements.Single(element => element.Id.Value == id));
            if (id != expectedOrder[0] && (participant is null || participant != previousParticipant)) cursor += 40d;
            Assert.Equal(cursor, current.ContentBounds!.Value.Top);
            Assert.Equal(original.ExpandedHeight, current.ExpandedHeight);
            Assert.Equal(original.ContentBounds!.Value.X, current.ContentBounds.Value.X);
            Assert.Equal(original.ContentBounds.Value.Width, current.ContentBounds.Value.Width);
            Assert.Equal(original.LocalToScopeTransform!.Value.OffsetY + cursor - original.ContentBounds.Value.Top,
                current.LocalToScopeTransform!.Value.OffsetY);
            cursor = current.ContentBounds.Value.Bottom;
            previousParticipant = participant;
        }
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        await host.UndoAsync();
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        await host.RedoAsync();
        Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
    }

    [Fact]
    public async Task A22AssignmentRepeatedAppendPreservesUnequalLanesAndMovesFollowingParticipantTogether()
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("a22-assignment-repeat", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var initial = document.CaptureSnapshot();
        for (var index = 1; index <= 2; index++)
        {
            var pool = new SemanticElementId("added-" + index);
            Assert.True((await session.ExecuteAsync(new CreateOrganizationalPoolCommand(document.DocumentId, document.Revision,
                pool, initial.SemanticModel.RootScopeId, OrganizationalPoolCreationMode.Empty))).IsCommitted);
            await session.WaitForIdleAsync();
            var before = document.CaptureSnapshot();
            var lastLane = index == 1 ? "x3" : "added-1";
            Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
                pool, new("participant-X")))).IsCommitted);
            await session.WaitForIdleAsync();
            var after = document.CaptureSnapshot();
            foreach (var id in new[] { "a", "x1", "x2", "x3", lastLane })
                Assert.Equal(AssignmentRegion(before, id), AssignmentRegion(after, id));
            Assert.Equal(AssignmentRegion(before, lastLane).ContentBounds!.Value.Bottom,
                AssignmentRegion(after, pool.Value).ContentBounds!.Value.Top);
            var offset = AssignmentRegion(after, pool.Value).ExpandedHeight;
            foreach (var id in new[] { "b", "y1", "y2", "c" })
            {
                var oldRegion = AssignmentRegion(before, id);
                var changed = AssignmentRegion(after, id);
                Assert.Equal(oldRegion.ContentBounds!.Value.Top + offset, changed.ContentBounds!.Value.Top);
                Assert.Equal(oldRegion.ExpandedHeight, changed.ExpandedHeight);
            }
            Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
            await host.UndoAsync();
            Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
            await host.RedoAsync();
            Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        }
        Assert.Equal<string>(["a", "x1", "x2", "x3", "added-1", "added-2", "b", "y1", "y2", "c"],
            OrderingRegions(document.CaptureSnapshot()).Select(region => region.ContainerSemanticElementId!.Value));
    }

    [Fact]
    public async Task A22UnassignOnlyLaneKeepsGeometryAndHistoryWithNoRegionDelta()
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-assignment-empty-delta", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
            new("warehouse"), null))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
            new("sales"), null))).IsCommitted);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions.AsEnumerable(),
            after.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
        await host.UndoAsync();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        await host.RedoAsync();
        Assert.Equal(after.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
        Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
    }
}
