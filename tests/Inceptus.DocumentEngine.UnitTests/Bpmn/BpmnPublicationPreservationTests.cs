using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnPublicationPreservationTests
{
    private static readonly SemanticElementId TaskId = new("test:publication-preservation:task");
    private static readonly VisualStateId TaskVisualId = new("test:publication-preservation:task:visual");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationAndDeletionPreservePublicationThroughUndoRedo(bool delete)
    {
        var registration = BpmnPluginRegistration.N100;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            new DocumentId("test:publication-preservation")).Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var history = new HistoryManager(document);
        if (delete)
        {
            Assert.True((await history.ExecuteAsync(processor, CreateTask(document))).IsCommitted);
        }

        Assert.True((await history.ExecuteAsync(processor, new UpdateDocumentPublicationCommand(
            document.DocumentId,
            document.Revision,
            "preserved-process",
            "Preserved process",
            "Publication must survive ordinary BPMN edits."))).IsCommitted);
        var before = document.CaptureSnapshot();
        var publication = Assert.IsType<DocumentPublicationSnapshot>(before.Publication);
        var historyCount = history.CaptureStatus().EntryCount;
        ICommand command = delete
            ? new DeleteBpmnFlowNodeCommand(document.DocumentId, document.Revision, TaskId, TaskVisualId)
            : CreateTask(document);

        var edited = await history.ExecuteAsync(processor, command);

        Assert.True(edited.IsCommitted, string.Join(Environment.NewLine,
            edited.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(publication, after.Publication);
        Assert.Equal(!delete, after.SemanticModel.TryGetElement(TaskId, out _));
        Assert.Equal(!delete, after.VisualModel.TryGetVisualState(TaskVisualId, out _));
        Assert.Equal(historyCount + 1, history.CaptureStatus().EntryCount);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(publication, document.Publication);
        Assert.Equal(before.DocumentId, document.DocumentId);
        Assert.True(before.SemanticModel.Elements.SequenceEqual(
            document.CaptureSnapshot().SemanticModel.Elements));
        Assert.True(before.VisualModel.VisualStates.SequenceEqual(
            document.CaptureSnapshot().VisualModel.VisualStates));

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(publication, document.Publication);
        Assert.Equal(after.DocumentId, document.DocumentId);
        Assert.True(after.SemanticModel.Elements.SequenceEqual(
            document.CaptureSnapshot().SemanticModel.Elements));
        Assert.True(after.VisualModel.VisualStates.SequenceEqual(
            document.CaptureSnapshot().VisualModel.VisualStates));
        Assert.Empty(DocumentInvariantValidator.Validate(document.CaptureSnapshot(), anchorPolicies));
    }

    private static CreateBpmnTaskCommand CreateTask(Document document) =>
        new(document.DocumentId, document.Revision, TaskId, TaskVisualId,
            new PointD(200d, 100d), new SizeD(120d, 80d), "task-1", "Task 1", 1);
}
