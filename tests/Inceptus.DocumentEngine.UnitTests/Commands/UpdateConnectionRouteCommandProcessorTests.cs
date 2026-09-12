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
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class UpdateConnectionRouteCommandProcessorTests
{
    private static readonly DocumentId DocumentId = new("test:route-processor");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId VisualId = new("test:relationship-visual");
    private static readonly PointD[] OriginalRoute =
        [new(10d, 20d), new(30d, 40d), new(80d, 90d)];
    private static readonly PointD[] UpdatedRoute =
        [new(10d, 20d), new(45d, 55d), new(80d, 90d)];

    [Fact]
    public void EnvelopeValidatorAcceptsOnlyTheRegisteredImmutableShape()
    {
        var validator = new UpdateConnectionRouteCommandEnvelopeValidator();
        var valid = new UpdateConnectionRouteCommand(
            DocumentId,
            DocumentRevision.Zero,
            VisualId,
            UpdatedRoute);
        var malformed = new MalformedRouteCommand();

        Assert.IsAssignableFrom<ICommandEnvelopeValidator>(validator);
        Assert.Empty(validator.Validate(valid));
        Assert.Contains(validator.Validate(malformed), diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
    }

    [Fact]
    public async Task BuiltInRegistrationCommitsOneVisualRouteChangeAndOneEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new UpdateConnectionRouteCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                UpdatedRoute));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, result.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), result.CommittedRevision);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            result.AffectedComponents);
        var committed = document.CaptureSnapshot();
        var updated = Assert.Single(committed.VisualModel.VisualStates);
        Assert.Equal(UpdatedRoute, updated.Route.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            committed.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Relationships.AsEnumerable(),
            committed.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(before.Metadata.ExtensionProperties, committed.Metadata.ExtensionProperties);
        var changed = Assert.Single(subscriber.Events);
        Assert.Equal(UpdateConnectionRouteCommand.KnownTypeId, changed.CommandTypeId);
        Assert.Equal(DocumentRevision.Zero, changed.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), changed.CommittedRevision);
        Assert.Equal(CommandPipelineInvalidation.ConnectorOnly, changed.PipelineInvalidation);
    }

    [Fact]
    public async Task BuiltInRegistrationEstablishesFirstPersistentRouteAsOneVisualCommit()
    {
        var document = CreateDocument(route: []);
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new UpdateConnectionRouteCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                UpdatedRoute));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(
            UpdatedRoute,
            Assert.Single(document.VisualModel.VisualStates).Route.AsEnumerable());
        var committed = document.CaptureSnapshot();
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            committed.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Relationships.AsEnumerable(),
            committed.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            before.Metadata.ExtensionProperties,
            committed.Metadata.ExtensionProperties);
        Assert.Single(subscriber.Events);
    }

    [Fact]
    public async Task MalformedBuiltInShapeDoesNotCommitOrPublish()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();

        var result = await new CommandProcessor(subscribers: [subscriber])
            .ExecuteAsync(document, new MalformedRouteCommand());
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
            new UpdateConnectionRouteCommand(
                document.DocumentId,
                new DocumentRevision(1),
                VisualId,
                UpdatedRoute));

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Theory]
    [MemberData(nameof(IllegalRoutes))]
    public async Task IllegalRouteGeometryDoesNotCommitRecordHistoryOrPublish(
        PointD[] illegalRoute)
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await history.ExecuteAsync(
            processor,
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                VisualId,
                illegalRoute));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.Empty(subscriber.Events);
    }

    public static TheoryData<PointD[]> IllegalRoutes => new()
    {
        new PointD[]
        {
            OriginalRoute[0],
            new(-double.MaxValue, 45d),
            OriginalRoute[^1],
        },
        new PointD[]
        {
            new(11d, 20d),
            UpdatedRoute[1],
            OriginalRoute[^1],
        },
        new PointD[]
        {
            OriginalRoute[0],
            UpdatedRoute[1],
            new(79d, 90d),
        },
    };

    private static Document CreateDocument(IEnumerable<PointD>? route = null)
    {
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(SourceId, new SemanticTypeId("test:type")),
                    new SemanticElementSnapshot(TargetId, new SemanticTypeId("test:type")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        SourceId,
                        TargetId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        VisualId,
                        RelationshipId,
                        new PointD(5d, 6d),
                        new SizeD(7d, 8d),
                        VisualPlacementMode.Manual,
                        route ?? OriginalRoute),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private sealed class MalformedRouteCommand : ICommand
    {
        public CommandTypeId TypeId => UpdateConnectionRouteCommand.KnownTypeId;

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
