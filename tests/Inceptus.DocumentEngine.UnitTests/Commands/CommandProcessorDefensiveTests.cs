using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
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

public sealed class CommandProcessorDefensiveTests
{
    private static readonly DocumentId TestDocumentId = new("test:defensive-document");
    private static readonly SemanticElementId ElementId = new("test:defensive-element");
    private static readonly VisualStateId VisualId = new("test:defensive-visual");
    private static readonly CommandTypeId TestCommandTypeId = new("test:defensive-command");

    [Fact]
    public async Task HandlerProposalForDifferentDocumentFailsBeforeCommitAndEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = Processor(
            (_, _, _) => ValueTask.FromResult(CommandHandlerResult.Success(
                Snapshot(new DocumentId("test:other-document"), DocumentRevision.Zero))),
            subscriber);

        var result = await processor.ExecuteAsync(document, MetadataCommand(document));

        Assert.Equal(CommandExecutionStatus.ProposedStateValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ProposedStateInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task HandlerProposalAtWrongBaseRevisionFailsBeforeCommitAndEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = Processor(
            (_, _, _) => ValueTask.FromResult(CommandHandlerResult.Success(
                Snapshot(document.DocumentId, new DocumentRevision(1)))),
            subscriber);

        var result = await processor.ExecuteAsync(document, MetadataCommand(document));

        Assert.Equal(CommandExecutionStatus.ProposedStateValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ProposedStateInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task MalformedPluginEnvelopeCreatesNoTransactionOrDownstreamWork()
    {
        var transactionCreations = 0;
        var delegatedCalls = 0;
        var handlerCalls = 0;
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                new CommandHandlerRegistration(
                    TestCommandTypeId,
                    new RejectingEnvelopeValidator(),
                    new DelegateHandler((_, snapshot, _) =>
                    {
                        Interlocked.Increment(ref handlerCalls);
                        return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                    })),
            ],
            validators:
            [
                new CommandValidatorRegistration(
                    TestCommandTypeId,
                    new CommandValidatorId("test:malformed-envelope-validator"),
                    new DelegateValidator((_, _) =>
                    {
                        Interlocked.Increment(ref delegatedCalls);
                        return [];
                    })),
            ],
            subscribers: [subscriber],
            executionCheckpointObserver: checkpoint =>
            {
                if (checkpoint == CommandExecutionCheckpoint.TransactionCreated)
                {
                    Interlocked.Increment(ref transactionCreations);
                }
            });

        var result = await processor.ExecuteAsync(document, MetadataCommand(document));

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Equal(0, transactionCreations);
        Assert.Equal(0, delegatedCalls);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task IndependentProcessorsShareTheDocumentExecutionGate()
    {
        var firstHandlerEntered = NewSignal();
        var releaseFirstHandler = NewSignal();
        var secondHandlerCalls = 0;
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var firstProcessor = Processor(
            async (_, snapshot, _) =>
            {
                firstHandlerEntered.TrySetResult();
                await releaseFirstHandler.Task;
                return CommandHandlerResult.Success(ChangeMetadata(snapshot, "first"));
            },
            subscriber);
        var secondProcessor = Processor(
            (_, snapshot, _) =>
            {
                Interlocked.Increment(ref secondHandlerCalls);
                return ValueTask.FromResult(CommandHandlerResult.Success(
                    ChangeMetadata(snapshot, "second")));
            },
            subscriber);

        var firstTask = firstProcessor.ExecuteAsync(
            document,
            MetadataCommand(document)).AsTask();
        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = secondProcessor.ExecuteAsync(
            document,
            MetadataCommand(document)).AsTask();
        await Task.Yield();

        Assert.False(secondTask.IsCompleted);
        releaseFirstHandler.TrySetResult();
        var first = await firstTask.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(first.IsCommitted);
        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, second.Status);
        Assert.Contains(second.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(0, secondHandlerCalls);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Single(subscriber.Events);
    }

    [Fact]
    public async Task CancellationWhileWaitingForDocumentGateEntersNoCommandStage()
    {
        var firstHandlerEntered = NewSignal();
        var releaseFirstHandler = NewSignal();
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var firstProcessor = Processor(
            async (_, snapshot, _) =>
            {
                firstHandlerEntered.TrySetResult();
                await releaseFirstHandler.Task;
                return CommandHandlerResult.Success(ChangeMetadata(snapshot, "first"));
            },
            subscriber);
        var envelope = new CountingEnvelopeValidator();
        var transactionCreations = 0;
        var delegatedCalls = 0;
        var handlerCalls = 0;
        var secondProcessor = new CommandProcessor(
            handlers:
            [
                new CommandHandlerRegistration(
                    TestCommandTypeId,
                    envelope,
                    new DelegateHandler((_, snapshot, _) =>
                    {
                        Interlocked.Increment(ref handlerCalls);
                        return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                    })),
            ],
            validators:
            [
                new CommandValidatorRegistration(
                    TestCommandTypeId,
                    new CommandValidatorId("test:queued-validator"),
                    new DelegateValidator((_, _) =>
                    {
                        Interlocked.Increment(ref delegatedCalls);
                        return [];
                    })),
            ],
            subscribers: [subscriber],
            executionCheckpointObserver: checkpoint =>
            {
                if (checkpoint == CommandExecutionCheckpoint.TransactionCreated)
                {
                    Interlocked.Increment(ref transactionCreations);
                }
            });
        using var cancellation = new CancellationTokenSource();

        var firstTask = firstProcessor.ExecuteAsync(
            document,
            MetadataCommand(document)).AsTask();
        await firstHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelledTask = secondProcessor.ExecuteAsync(
            document,
            MetadataCommand(document),
            cancellation.Token).AsTask();
        Assert.False(cancelledTask.IsCompleted);

        await cancellation.CancelAsync();
        var cancelled = await cancelledTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(CommandExecutionStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, envelope.Calls);
        Assert.Equal(0, transactionCreations);
        Assert.Equal(0, delegatedCalls);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);

        releaseFirstHandler.TrySetResult();
        Assert.True((await firstTask.WaitAsync(TimeSpan.FromSeconds(5))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Single(subscriber.Events);
    }

    [Fact]
    public async Task CancellationAtFinalPreInstallCheckpointAbortsCleanly()
    {
        using var cancellation = new CancellationTokenSource();
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers: null,
            validators: null,
            subscribers: [subscriber],
            executionCheckpointObserver: checkpoint =>
            {
                if (checkpoint == CommandExecutionCheckpoint.BeforeFinalCancellationCheck)
                {
                    cancellation.Cancel();
                }
            });

        var result = await processor.ExecuteAsync(
            document,
            MoveCommand(document),
            cancellation.Token);

        Assert.Equal(CommandExecutionStatus.Cancelled, result.Status);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Null(result.CommittedEvent);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task CancellationAfterFinalCheckCannotSplitInstallRevisionAndEvent()
    {
        using var cancellation = new CancellationTokenSource();
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers: null,
            validators: null,
            subscribers: [subscriber],
            executionCheckpointObserver: checkpoint =>
            {
                if (checkpoint == CommandExecutionCheckpoint.AfterFinalCancellationCheck)
                {
                    cancellation.Cancel();
                }
            });

        var result = await processor.ExecuteAsync(
            document,
            MoveCommand(document),
            cancellation.Token);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        var change = Assert.Single(subscriber.Events);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(document.CaptureSnapshot(), result.CommittedSnapshot);
        Assert.Same(result.CommittedSnapshot, change.CommittedSnapshot);
    }

    [Fact]
    public async Task StructurallyEqualNewComponentsDoNotViolateVisualScope()
    {
        SemanticModelSnapshot? proposedSemantic = null;
        DocumentMetadataSnapshot? proposedMetadata = null;
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                new CommandHandlerRegistration(
                    TestCommandTypeId,
                    new PermissiveEnvelopeValidator(),
                    new DelegateHandler((_, snapshot, _) =>
                    {
                        proposedSemantic = new SemanticModelSnapshot(
                            snapshot.DocumentId,
                            snapshot.Revision,
                            snapshot.SemanticModel.Elements,
                            snapshot.SemanticModel.Relationships);
                        proposedMetadata = new DocumentMetadataSnapshot(
                            snapshot.DocumentId,
                            snapshot.Revision,
                            snapshot.Metadata.SystemManagedProperties,
                            snapshot.Metadata.ExtensionProperties);
                        var proposedVisual = new VisualModelSnapshot(
                            snapshot.DocumentId,
                            snapshot.Revision,
                            snapshot.VisualModel.VisualStates.Select(visual =>
                                visual.Id == VisualId
                                    ? new VisualStateSnapshot(
                                        visual.Id,
                                        visual.SemanticElementId,
                                        new PointD(90d, 110d),
                                        visual.Size,
                                        visual.PlacementMode,
                                        visual.Route,
                                        visual.Properties)
                                    : visual));

                        return ValueTask.FromResult(CommandHandlerResult.Success(
                            new DocumentSnapshot(
                                proposedSemantic,
                                proposedVisual,
                                proposedMetadata)));
                    })),
            ],
            subscribers: [subscriber]);
        var command = new TestCommand(
            TestCommandTypeId,
            document.DocumentId,
            document.Revision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        var result = await processor.ExecuteAsync(document, command);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        var committedSnapshot = document.CaptureSnapshot();
        Assert.True(committedSnapshot.VisualModel.TryGetVisualState(VisualId, out var movedVisual));
        Assert.NotNull(movedVisual);
        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), result.CommittedRevision);
        Assert.Equal(new PointD(90d, 110d), movedVisual.Position);
        Assert.NotSame(before.SemanticModel, proposedSemantic);
        Assert.NotSame(before.Metadata, proposedMetadata);
        Assert.Equal(before.SemanticModel, proposedSemantic);
        Assert.Equal(before.Metadata, proposedMetadata);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.AffectedComponentViolation);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, result.AffectedComponents);
        var change = Assert.Single(subscriber.Events);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, change.AffectedComponents);
        Assert.Same(result.CommittedSnapshot, change.CommittedSnapshot);
        Assert.Equal(committedSnapshot, change.CommittedSnapshot);
    }

    private static CommandProcessor Processor(
        Func<ICommand, DocumentSnapshot, CancellationToken, ValueTask<CommandHandlerResult>> handle,
        IDocumentChangedSubscriber subscriber) =>
        new(
            handlers:
            [
                new CommandHandlerRegistration(
                    TestCommandTypeId,
                    new PermissiveEnvelopeValidator(),
                    new DelegateHandler(handle)),
            ],
            subscribers: [subscriber]);

    private static TestCommand MetadataCommand(Document document) =>
        new(
            TestCommandTypeId,
            document.DocumentId,
            DocumentRevision.Zero,
            CommandCategory.Metadata,
            AuthoritativeDocumentComponent.Metadata);

    private static MoveVisualStateCommand MoveCommand(Document document) =>
        new(
            document.DocumentId,
            document.Revision,
            VisualId,
            new PointD(80d, 100d));

    private static Document CreateDocument()
    {
        var creation = DocumentFactory.Create(Snapshot(TestDocumentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(creation.Document);
    }

    private static DocumentSnapshot Snapshot(DocumentId documentId, DocumentRevision revision)
    {
        var semantic = new SemanticModelSnapshot(
            documentId,
            revision,
            [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]);
        var visual = new VisualModelSnapshot(
            documentId,
            revision,
            [
                new VisualStateSnapshot(
                    VisualId,
                    ElementId,
                    new PointD(10d, 20d),
                    new SizeD(30d, 40d),
                    VisualPlacementMode.Manual),
            ]);
        var metadata = new DocumentMetadataSnapshot(
            documentId,
            revision,
            [new("test:schema", PropertyValue.FromInteger(1))]);
        return new DocumentSnapshot(semantic, visual, metadata);
    }

    private static DocumentSnapshot ChangeMetadata(DocumentSnapshot snapshot, string value) =>
        new(
            snapshot.SemanticModel,
            snapshot.VisualModel,
            new DocumentMetadataSnapshot(
                snapshot.DocumentId,
                snapshot.Revision,
                snapshot.Metadata.SystemManagedProperties,
                [new("test:value", PropertyValue.FromText(value))]));

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestCommand(
        CommandTypeId typeId,
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        CommandCategory category,
        AuthoritativeDocumentComponent affectedComponents) : ICommand
    {
        public CommandTypeId TypeId { get; } = typeId;

        public DocumentId TargetDocumentId { get; } = targetDocumentId;

        public DocumentRevision ExpectedRevision { get; } = expectedRevision;

        public CommandCategory Category { get; } = category;

        public AuthoritativeDocumentComponent AffectedComponents { get; } = affectedComponents;
    }

    private sealed class DelegateHandler(
        Func<ICommand, DocumentSnapshot, CancellationToken, ValueTask<CommandHandlerResult>> handle) :
        ICommandHandler
    {
        public ValueTask<CommandHandlerResult> HandleAsync(
            ICommand command,
            DocumentSnapshot document,
            CancellationToken cancellationToken) =>
            handle(command, document, cancellationToken);
    }

    private sealed class DelegateValidator(
        Func<ICommand, DocumentSnapshot, ImmutableArray<Diagnostic>> validate) : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
            validate(command, document);
    }

    private sealed class PermissiveEnvelopeValidator : ICommandEnvelopeValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command) => [];
    }

    private sealed class RejectingEnvelopeValidator : ICommandEnvelopeValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command) =>
        [
            new Diagnostic(
                CommandExecutionDiagnosticCodes.InvalidCommandStructure,
                DiagnosticSeverity.Error,
                "The plugin-neutral request shape is malformed.",
                command.TypeId.Value),
        ];
    }

    private sealed class CountingEnvelopeValidator : ICommandEnvelopeValidator
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        public ImmutableArray<Diagnostic> Validate(ICommand command)
        {
            Interlocked.Increment(ref _calls);
            return [];
        }
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
