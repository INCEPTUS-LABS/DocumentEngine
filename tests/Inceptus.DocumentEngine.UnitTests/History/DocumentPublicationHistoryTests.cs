using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class DocumentPublicationHistoryTests
{
    [Fact]
    public void EmptyDocumentHasNoPublication()
    {
        var document = CreateDocument("empty");

        Assert.Null(document.Publication);
        Assert.Null(document.CaptureSnapshot().Publication);
    }

    [Fact]
    public void PublicationConstructionNormalizesOnlyOuterWhitespace()
    {
        var publication = new DocumentPublicationSnapshot(
            "  kompletacja-zamowienia  ",
            "  Proces kompletacji zamówienia  ",
            "  Przebieg procesu kompletacji zamówienia.  ");

        Assert.Equal("kompletacja-zamowienia", publication.Code);
        Assert.Equal("Proces kompletacji zamówienia", publication.Title);
        Assert.Equal("Przebieg procesu kompletacji zamówienia.", publication.Description);
        Assert.Equal(
            string.Empty,
            new DocumentPublicationSnapshot("process-a", "Process A", " \t ").Description);
    }

    [Theory]
    [InlineData("Kompletacja", "Title")]
    [InlineData("kompletacja zamowienia", "Title")]
    [InlineData("-kompletacja", "Title")]
    [InlineData("kompletacja-", "Title")]
    [InlineData("kompletacja--zamowienia", "Title")]
    [InlineData("process-a", "")]
    [InlineData("process-a", "   ")]
    public async Task InvalidPublicationCommandIsAtomic(string code, string title)
    {
        var document = CreateDocument("invalid");
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);

        var result = await history.ExecuteAsync(
            new CommandProcessor(),
            new UpdateDocumentPublicationCommand(
                document.DocumentId,
                document.Revision,
                code,
                title,
                "Description"));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.DocumentPublicationInvalid);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), history.CaptureStatus());
    }

    [Fact]
    public async Task ChangedPublicationIsOneRevisionAndHistoryEntryWhileNoOpIsRejected()
    {
        var document = CreateDocument("changed");
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        Assert.Equal(
            PipelineInvalidation.None,
            CommandPipelineInvalidation.Resolve(
                Command(document, "process-a", "Process A", "Description A")));

        var changed = await history.ExecuteAsync(
            processor,
            new UpdateDocumentPublicationCommand(
                document.DocumentId,
                document.Revision,
                " process-a ",
                " Process A ",
                " Description A "));
        var afterChanged = document.CaptureSnapshot();
        var noOp = await history.ExecuteAsync(
            processor,
            new UpdateDocumentPublicationCommand(
                document.DocumentId,
                document.Revision,
                "process-a",
                "Process A",
                "Description A"));

        Assert.True(changed.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(
            new DocumentPublicationSnapshot("process-a", "Process A", "Description A"),
            document.Publication);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
        Assert.False(noOp.IsCommitted);
        Assert.Contains(noOp.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.DocumentPublicationUnchanged);
        Assert.Same(afterChanged, document.CaptureSnapshot());
    }

    [Fact]
    public async Task UndoRedoRestoresExactPublicationWithoutChangingDocumentIdentityOrModels()
    {
        var document = CreateDocument("undo-redo");
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var initialDocumentId = document.DocumentId;
        var initial = document.CaptureSnapshot();
        Assert.True((await history.ExecuteAsync(
            processor,
            Command(document, "process-a", "Process A", "Description A"))).IsCommitted);
        var first = document.CaptureSnapshot();
        Assert.True((await history.ExecuteAsync(
            processor,
            Command(document, "process-b", "Process B", "Description B"))).IsCommitted);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(first.Publication, document.Publication);
        Assert.Equal(initialDocumentId, document.DocumentId);
        AssertUnrelatedStateEqual(initial, document.CaptureSnapshot());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(
            new DocumentPublicationSnapshot("process-b", "Process B", "Description B"),
            document.Publication);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Null(document.Publication);
        Assert.Equal(initialDocumentId, document.DocumentId);
    }

    private static UpdateDocumentPublicationCommand Command(
        Document document,
        string code,
        string title,
        string description) =>
        new(document.DocumentId, document.Revision, code, title, description);

    private static Document CreateDocument(string suffix) =>
        Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            new DocumentId($"test:n10.6:publication:{suffix}")).Document);

    private static void AssertUnrelatedStateEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.Elements, actual.SemanticModel.Elements);
        Assert.Equal(expected.SemanticModel.Relationships, actual.SemanticModel.Relationships);
        Assert.Equal(expected.SemanticModel.NestedScopes, actual.SemanticModel.NestedScopes);
        Assert.Equal(expected.SemanticModel.ScopeMemberships,
            actual.SemanticModel.ScopeMemberships);
        Assert.Equal(expected.SemanticModel.ModelProfiles, actual.SemanticModel.ModelProfiles);
        Assert.Equal(expected.SemanticModel.ProfileAssignments,
            actual.SemanticModel.ProfileAssignments);
        Assert.Equal(expected.VisualModel.VisualStates, actual.VisualModel.VisualStates);
        Assert.Equal(expected.VisualModel.ProfileElementPresentations,
            actual.VisualModel.ProfileElementPresentations);
        Assert.Equal(expected.Metadata.SystemManagedProperties,
            actual.Metadata.SystemManagedProperties);
        Assert.Equal(expected.Metadata.ExtensionProperties,
            actual.Metadata.ExtensionProperties);
    }
}
