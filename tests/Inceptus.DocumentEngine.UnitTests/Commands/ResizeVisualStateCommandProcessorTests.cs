using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class ResizeVisualStateCommandProcessorTests
{
    private static readonly DocumentId DocumentId = new("test:resize-processor");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly VisualStateId VisualId = new("test:visual");

    [Fact]
    public void EnvelopeValidatorAcceptsOnlyTheRegisteredImmutableShape()
    {
        var validator = new ResizeVisualStateCommandEnvelopeValidator();
        var valid = new ResizeVisualStateCommand(
            DocumentId,
            DocumentRevision.Zero,
            VisualId,
            new RectD(20d, 30d, 80d, 60d));
        var malformed = new MalformedResizeCommand();

        Assert.IsAssignableFrom<ICommandEnvelopeValidator>(validator);
        Assert.Empty(validator.Validate(valid));
        Assert.Contains(validator.Validate(malformed), diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
    }

    [Fact]
    public async Task BuiltInRegistrationCommitsOneVisualChangeAndOneEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var targetBounds = new RectD(70d, 80d, 110d, 65d);

        var result = await processor.ExecuteAsync(
            document,
            new ResizeVisualStateCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                targetBounds,
                VisualPlacementMode.Pinned));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, result.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), result.CommittedRevision);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            result.AffectedComponents);
        var committed = document.CaptureSnapshot();
        var resized = Assert.Single(committed.VisualModel.VisualStates);
        Assert.Equal(targetBounds.TopLeft, resized.Position);
        Assert.Equal(targetBounds.Size, resized.Size);
        Assert.Equal(VisualPlacementMode.Pinned, resized.PlacementMode);
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            committed.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Relationships.AsEnumerable(),
            committed.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            before.Metadata.SystemManagedProperties,
            committed.Metadata.SystemManagedProperties);
        Assert.Equal(
            before.Metadata.ExtensionProperties,
            committed.Metadata.ExtensionProperties);
        var changed = Assert.Single(subscriber.Events);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, changed.CommandTypeId);
        Assert.Equal(DocumentRevision.Zero, changed.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), changed.CommittedRevision);
        Assert.Equal(
            CommandPipelineInvalidation.WithoutNodeLayout,
            changed.PipelineInvalidation);
        Assert.Equal(
            [VisualId],
            Assert.IsType<NodeGeometryPipelineImpact>(changed.NodeGeometryImpact)
                .ChangedVisualStateIds
                .ToArray());
    }

    [Fact]
    public async Task MalformedBuiltInShapeDoesNotCommitOrPublish()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();

        var result = await new CommandProcessor(subscribers: [subscriber])
            .ExecuteAsync(document, new MalformedResizeCommand());
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task StaleRevisionDoesNotCommitOrPublish()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();

        var result = await new CommandProcessor(subscribers: [subscriber]).ExecuteAsync(
            document,
            new ResizeVisualStateCommand(
                document.DocumentId,
                new DocumentRevision(1),
                VisualId,
                new RectD(70d, 80d, 110d, 65d)));

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    private static Document CreateDocument()
    {
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        VisualId,
                        ElementId,
                        new PointD(10d, 20d),
                        new SizeD(30d, 40d),
                        VisualPlacementMode.Manual,
                        [new PointD(10d, 20d), new PointD(40d, 60d)]),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private sealed class MalformedResizeCommand : ICommand
    {
        public CommandTypeId TypeId => ResizeVisualStateCommand.KnownTypeId;

        public DocumentId TargetDocumentId => DocumentId;

        public DocumentRevision ExpectedRevision => DocumentRevision.Zero;

        public CommandCategory Category => CommandCategory.Visual;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.VisualModel;
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
