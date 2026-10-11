using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("x1")]
    [InlineData("x2")]
    [InlineData("x3")]
    public async Task A22UnassignmentSeparatesStandalonePoolAndKeepsRemainingLanesTogether(string source)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("a22-unassignment-gap", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var historyCount = session.CaptureState().HistoryStatus.EntryCount;
        var poolId = new SemanticElementId(source);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(semanticSceneSelection: poolId));
        await session.WaitForIdleAsync();
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(poolId));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        draft.DataFields.Single(field => field.FieldId.Value == "participant").EditorValue = string.Empty;
        var result = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        Assert.Equal(string.Empty, result.Authoritative!.DataFields.Single(field => field.FieldId.Value == "participant").EditorValue);
        Assert.Null(OrganizationalSemantics.GetParticipantId(after.SemanticModel.Elements.Single(element => element.Id == poolId)));
        string[] expectedOrder = source == "x2"
            ? ["a", "x1", "x3", "x2", "b", "y1", "y2", "c"]
            : ["a", "x1", "x2", "x3", "b", "y1", "y2", "c"];
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
        Assert.Equal(AssignmentRegion(before, "a"), AssignmentRegion(after, "a"));
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.SemanticModel.Elements.Where(element => element.Id != poolId), after.SemanticModel.Elements.Where(element => element.Id != poolId));
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(historyCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        for (var index = 0; index < 2; index++)
        {
            await host.UndoAsync();
            Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
            Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
            await host.RedoAsync();
            Assert.Equal(after.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
            Assert.Equal(after.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        }
        var restored = document.CaptureSnapshot();
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(restored).AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = CreatePropertiesUxHost(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("a22-unassignment-reopened", "container");
        Assert.Equal(restored, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }
}
