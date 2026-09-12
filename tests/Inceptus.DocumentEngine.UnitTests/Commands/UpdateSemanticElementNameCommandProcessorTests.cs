using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class UpdateSemanticElementNameCommandProcessorTests
{
    private static readonly DocumentId DocumentId = new("test:name-processor");
    private static readonly SemanticElementId ElementId = new("test:element");
    private const string NameKey = "test:name";

    [Fact]
    public void EnvelopeValidatorAcceptsOnlyTheRegisteredImmutableShape()
    {
        var validator = new UpdateSemanticElementNameCommandEnvelopeValidator();

        Assert.Empty(validator.Validate(Update(DocumentRevision.Zero, "Renamed")));
        Assert.Contains(validator.Validate(new MalformedNameCommand()), diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
    }

    [Fact]
    public async Task BuiltInRegistrationCommitsOneSemanticChangeAndOneEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            Update(document.Revision, "Renamed"));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, result.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), result.CommittedRevision);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel,
            result.AffectedComponents);
        var committed = document.CaptureSnapshot();
        Assert.Equal("Renamed", Name(committed));
        Assert.Equal(
            before.VisualModel.VisualStates.AsEnumerable(),
            committed.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(before.Metadata.ExtensionProperties, committed.Metadata.ExtensionProperties);
        var changed = Assert.Single(subscriber.Events);
        Assert.Equal(UpdateSemanticElementNameCommand.KnownTypeId, changed.CommandTypeId);
        Assert.Equal(AuthoritativeDocumentComponent.SemanticModel, changed.AffectedComponents);
        Assert.Equal(DocumentRevision.Zero, changed.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), changed.CommittedRevision);
    }

    [Fact]
    public async Task StaleOrMalformedRequestDoesNotCommitOrPublish()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var stale = await processor.ExecuteAsync(
            document,
            Update(new DocumentRevision(1), "Renamed"));
        var malformed = await processor.ExecuteAsync(document, new MalformedNameCommand());
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, stale.Status);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, malformed.Status);
        Assert.Contains(malformed.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Original")]
    public async Task InvalidOrUnchangedNameDoesNotCommitOrPublish(string name)
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();

        var result = await new CommandProcessor(subscribers: [subscriber])
            .ExecuteAsync(document, Update(document.Revision, name));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.Equal(CommandExecutionStatus.HandlerFailed, result.Status);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    internal static UpdateSemanticElementNameCommand Update(
        DocumentRevision revision,
        string name) =>
        new(DocumentId, revision, ElementId, NameKey, name);

    internal static Document CreateDocument() =>
        Assert.IsType<Document>(DocumentFactory.Create(Snapshot(DocumentRevision.Zero, "Original")).Document);

    internal static DocumentSnapshot Snapshot(DocumentRevision revision, string name) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    new SemanticElementSnapshot(
                        ElementId,
                        new SemanticTypeId("test:node"),
                        [
                            new(NameKey, PropertyValue.FromText(name)),
                            new("test:retained", PropertyValue.FromInteger(7)),
                        ]),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("test:visual"),
                        ElementId,
                        new PointD(10d, 20d),
                        new SizeD(100d, 50d),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));

    internal static string Name(DocumentSnapshot snapshot)
    {
        Assert.True(snapshot.SemanticModel.TryGetElement(ElementId, out var element));
        return element!.Properties[NameKey].TextValue;
    }

    private sealed class MalformedNameCommand : ICommand
    {
        public CommandTypeId TypeId => UpdateSemanticElementNameCommand.KnownTypeId;

        public DocumentId TargetDocumentId => DocumentId;

        public DocumentRevision ExpectedRevision => DocumentRevision.Zero;

        public CommandCategory Category => CommandCategory.Semantic;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.SemanticModel;
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
