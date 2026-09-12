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

public sealed class UpdateSemanticElementPropertyCommandProcessorTests
{
    internal static readonly DocumentId DocumentId = new("test:property-processor");
    internal static readonly SemanticElementId ElementId = new("test:element");
    internal const string DescriptionKey = "test:description";
    internal const string NumberKey = "test:element-number";

    [Fact]
    public void EnvelopeValidatorAcceptsOnlyTheRegisteredImmutableShape()
    {
        var validator = new UpdateSemanticElementPropertyCommandEnvelopeValidator();

        Assert.Empty(validator.Validate(Update(
            DocumentRevision.Zero,
            DescriptionKey,
            PropertyValue.FromText("Changed"))));
        Assert.Contains(validator.Validate(new MalformedPropertyCommand()), diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
    }

    [Fact]
    public async Task BuiltInRegistrationCommitsOneTypedSemanticChangeAndOneEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        const string description = "First line\nSecond line";

        var result = await processor.ExecuteAsync(
            document,
            Update(
                document.Revision,
                DescriptionKey,
                PropertyValue.FromText(description)));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, result.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), result.CommittedRevision);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel,
            result.AffectedComponents);
        var committed = document.CaptureSnapshot();
        Assert.Equal(description, Value(committed, DescriptionKey).TextValue);
        Assert.Equal(10, Value(committed, NumberKey).IntegerValue);
        Assert.Equal(
            before.VisualModel.VisualStates.AsEnumerable(),
            committed.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(
            before.Metadata.ExtensionProperties,
            committed.Metadata.ExtensionProperties);
        var changed = Assert.Single(subscriber.Events);
        Assert.Equal(UpdateSemanticElementPropertyCommand.KnownTypeId, changed.CommandTypeId);
        Assert.Equal(AuthoritativeDocumentComponent.SemanticModel, changed.AffectedComponents);
        Assert.Equal(DocumentRevision.Zero, changed.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), changed.CommittedRevision);
        Assert.Equal(CommandPipelineInvalidation.Full, changed.PipelineInvalidation);
    }

    [Fact]
    public async Task BuiltInRegistrationPreservesIntegerTypeAtLongBoundary()
    {
        var document = CreateDocument();

        var result = await new CommandProcessor().ExecuteAsync(
            document,
            Update(
                document.Revision,
                NumberKey,
                PropertyValue.FromInteger(long.MaxValue)));

        Assert.True(result.IsCommitted);
        var value = Value(document.CaptureSnapshot(), NumberKey);
        Assert.Equal(PropertyValueKind.Integer, value.Kind);
        Assert.Equal(long.MaxValue, value.IntegerValue);
    }

    [Fact]
    public async Task BuiltInRegistrationUpdatesRelationshipPropertyWithoutChangingEndpoints()
    {
        var relationshipId = new SemanticElementId("test:relationship");
        var targetId = new SemanticElementId("test:target");
        var baseline = Snapshot(DocumentRevision.Zero, "Initial description", 10);
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                baseline.SemanticModel.Elements.Append(
                    new SemanticElementSnapshot(
                        targetId,
                        new SemanticTypeId("test:node"))),
                [
                    new SemanticRelationshipSnapshot(
                        relationshipId,
                        new SemanticTypeId("test:connector"),
                        ElementId,
                        targetId,
                        [new(DescriptionKey, PropertyValue.FromText("Initial relationship"))]),
                ]),
            baseline.VisualModel,
            baseline.Metadata);
        var document = Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                document.Revision,
                relationshipId,
                DescriptionKey,
                PropertyValue.FromText("Changed relationship")));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        var committed = document.CaptureSnapshot();
        Assert.True(committed.SemanticModel.TryGetRelationship(
            relationshipId,
            out var relationship));
        Assert.Equal(
            "Changed relationship",
            relationship!.Properties[DescriptionKey].TextValue);
        Assert.Equal(ElementId, relationship.SourceId);
        Assert.Equal(targetId, relationship.TargetId);
        Assert.Equal(
            snapshot.VisualModel.VisualStates.AsEnumerable(),
            committed.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(
            CommandPipelineInvalidation.ConnectorOnly,
            Assert.Single(subscriber.Events).PipelineInvalidation);
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
            Update(
                new DocumentRevision(1),
                NumberKey,
                PropertyValue.FromInteger(25)));
        var malformed = await processor.ExecuteAsync(document, new MalformedPropertyCommand());
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

    [Fact]
    public async Task ExactNoOpOrKindMismatchDoesNotCommitOrPublish()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var noOp = await processor.ExecuteAsync(
            document,
            Update(
                document.Revision,
                DescriptionKey,
                PropertyValue.FromText("Initial description")));
        var wrongKind = await processor.ExecuteAsync(
            document,
            Update(
                document.Revision,
                NumberKey,
                PropertyValue.FromText("25")));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.Equal(CommandExecutionStatus.HandlerFailed, noOp.Status);
        Assert.Contains(noOp.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged);
        Assert.Equal(CommandExecutionStatus.HandlerFailed, wrongKind.Status);
        Assert.Contains(wrongKind.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.SemanticPropertyUnsupported);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    internal static UpdateSemanticElementPropertyCommand Update(
        DocumentRevision revision,
        string key,
        PropertyValue value) =>
        new(DocumentId, revision, ElementId, key, value);

    internal static Document CreateDocument() =>
        Assert.IsType<Document>(DocumentFactory.Create(
            Snapshot(DocumentRevision.Zero, "Initial description", 10)).Document);

    internal static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        string description,
        long number) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    new SemanticElementSnapshot(
                        ElementId,
                        new SemanticTypeId("test:node"),
                        [
                            new(DescriptionKey, PropertyValue.FromText(description)),
                            new(NumberKey, PropertyValue.FromInteger(number)),
                            new("test:retained", PropertyValue.FromBoolean(true)),
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
                        VisualPlacementMode.Manual),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));

    internal static PropertyValue Value(DocumentSnapshot snapshot, string key)
    {
        Assert.True(snapshot.SemanticModel.TryGetElement(ElementId, out var element));
        return element!.Properties[key];
    }

    private sealed class MalformedPropertyCommand : ICommand
    {
        public CommandTypeId TypeId => UpdateSemanticElementPropertyCommand.KnownTypeId;

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
