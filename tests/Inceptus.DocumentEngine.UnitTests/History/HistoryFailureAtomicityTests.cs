using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class HistoryFailureAtomicityTests
{
    private static readonly DocumentId TestDocumentId = new("test:history-failure-document");
    private static readonly SemanticElementId ElementId = new("test:history-failure-element");
    private static readonly VisualStateId VisualId = new("test:history-failure-visual");
    private static readonly CommandTypeId SourceType = new("test:history-source-command");
    private static readonly CommandTypeId UndoType = new("test:history-undo-command");
    private static readonly CommandTypeId RedoType = new("test:history-redo-command");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidHistoryPolicyPreparationAbortsBeforeDocumentHistoryAndEvent(
        bool throwFromPolicy)
    {
        var document = CreateDocument(new DocumentId(
            $"test:policy-failure-{throwFromPolicy}"));
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = Processor(
            sourcePolicy: throwFromPolicy
                ? new ThrowingPolicy()
                : new NullResultPolicy(),
            subscriber: subscriber);

        var result = await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)));

        Assert.Equal(HistoryOperationStatus.InternalFailure, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.PolicyFailure);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(new HistoryStatus(0, false, false), history.CaptureStatus());
        Assert.Empty(subscriber.Events);
    }

    [Theory]
    [InlineData(FactoryFailure.Throw)]
    [InlineData(FactoryFailure.ReturnNull)]
    public async Task UndoFactoryConstructionFailureLeavesCursorDocumentAndEventsUnchanged(
        FactoryFailure failure)
    {
        var document = CreateDocument(new DocumentId($"test:undo-factory-{failure}"));
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var policy = new FactoryPolicy(
            undoFailure: failure,
            redoFailure: FactoryFailure.None);
        var processor = Processor(policy, subscriber);

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var before = document.CaptureSnapshot();
        var status = history.CaptureStatus();
        var eventCount = subscriber.Events.Count;

        var undo = await history.UndoAsync(processor);

        Assert.Equal(HistoryOperationStatus.InternalFailure, undo.Status);
        var expectedCode = failure == FactoryFailure.Throw
            ? HistoryDiagnosticCodes.CommandFactoryFailure
            : HistoryDiagnosticCodes.InvalidRestorationCommand;
        Assert.Contains(undo.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(status, history.CaptureStatus());
        Assert.Equal(eventCount, subscriber.Events.Count);
    }

    [Theory]
    [InlineData(FactoryFailure.Throw)]
    [InlineData(FactoryFailure.ReturnNull)]
    public async Task RedoFactoryConstructionFailureLeavesCursorDocumentAndEventsUnchanged(
        FactoryFailure failure)
    {
        var document = CreateDocument(new DocumentId($"test:redo-factory-{failure}"));
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var policy = new FactoryPolicy(
            undoFailure: FactoryFailure.None,
            redoFailure: failure);
        var processor = Processor(policy, subscriber);

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)))).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var before = document.CaptureSnapshot();
        var status = history.CaptureStatus();
        var eventCount = subscriber.Events.Count;

        var redo = await history.RedoAsync(processor);

        Assert.Equal(HistoryOperationStatus.InternalFailure, redo.Status);
        var expectedCode = failure == FactoryFailure.Throw
            ? HistoryDiagnosticCodes.CommandFactoryFailure
            : HistoryDiagnosticCodes.InvalidRestorationCommand;
        Assert.Contains(redo.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(status, history.CaptureStatus());
        Assert.Equal(eventCount, subscriber.Events.Count);
    }

    [Fact]
    public void StructurallyInvalidHistoryCollaboratorsAreRejected()
    {
        var validFactory = new TestFactory(
            UndoType,
            new PointD(10d, 20d),
            FactoryFailure.None);
        var validPolicy = new FactoryPolicy(
            FactoryFailure.None,
            FactoryFailure.None);

        Assert.Throws<ArgumentNullException>(() =>
            new HistoryEntry(null!, validFactory, validFactory));
        Assert.Throws<ArgumentNullException>(() =>
            new HistoryEntry(SourceType, null!, validFactory));
        Assert.Throws<ArgumentNullException>(() =>
            new HistoryEntry(SourceType, validFactory, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CommandHistoryPolicyRegistration(null!, validPolicy));
        Assert.Throws<ArgumentNullException>(() =>
            new CommandHistoryPolicyRegistration(SourceType, null!));
        Assert.Throws<ArgumentNullException>(() =>
            CommandHistoryPreparationResult.Undoable(null!, validFactory));
        Assert.Throws<ArgumentNullException>(() =>
            CommandHistoryPreparationResult.Undoable(validFactory, null!));
        Assert.Throws<ArgumentException>(() =>
            CommandHistoryPreparationResult.Failure([null!]));
        Assert.Throws<ArgumentException>(() =>
            new HistoryPolicyRegistry([null!]));
    }

    [Fact]
    public async Task DelegatedValidationFailureDuringUndoLeavesPreparedNavigationUninstalled()
    {
        var document = CreateDocument(new DocumentId("test:undo-delegated-failure"));
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var undoHandlerCalls = 0;
        var processor = new CommandProcessor(
            handlers:
            [
                Registration(SourceType, new TestVisualHandler()),
                Registration(
                    UndoType,
                    new CountingVisualHandler(() =>
                        Interlocked.Increment(ref undoHandlerCalls))),
                Registration(RedoType, new TestVisualHandler()),
            ],
            validators:
            [
                new CommandValidatorRegistration(
                    UndoType,
                    new CommandValidatorId("test:reject-undo"),
                    new RejectingValidator()),
            ],
            subscribers: [subscriber],
            historyPolicies:
            [
                new CommandHistoryPolicyRegistration(
                    SourceType,
                    new FactoryPolicy(FactoryFailure.None, FactoryFailure.None)),
            ]);

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var before = document.CaptureSnapshot();
        var status = history.CaptureStatus();
        var eventCount = subscriber.Events.Count;

        var undo = await history.UndoAsync(processor);

        Assert.Equal(HistoryOperationStatus.CommandFailed, undo.Status);
        Assert.Contains(undo.Diagnostics, diagnostic =>
            diagnostic.Code == "test:undo-delegated-rejection");
        Assert.Equal(0, undoHandlerCalls);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(status, history.CaptureStatus());
        Assert.Equal(eventCount, subscriber.Events.Count);
    }

    [Theory]
    [InlineData(FactoryFailure.StaleRevision)]
    [InlineData(FactoryFailure.WrongDocument)]
    public async Task InvalidRestorationEnvelopeFromFactoryLeavesUndoAvailable(
        FactoryFailure failure)
    {
        var document = CreateDocument(new DocumentId($"test:invalid-factory-{failure}"));
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = Processor(
            new FactoryPolicy(failure, FactoryFailure.None),
            subscriber);

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var before = document.CaptureSnapshot();
        var eventCount = subscriber.Events.Count;

        var undo = await history.UndoAsync(processor);

        Assert.Equal(HistoryOperationStatus.InternalFailure, undo.Status);
        Assert.Contains(undo.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidRestorationCommand);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), history.CaptureStatus());
        Assert.Equal(eventCount, subscriber.Events.Count);
    }

    [Fact]
    public async Task FinalPreinstallCancellationDuringUndoInstallsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelUndo = false;
        var document = CreateDocument(new DocumentId("test:undo-final-cancellation"));
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = Processor(
            new FactoryPolicy(FactoryFailure.None, FactoryFailure.None),
            subscriber,
            checkpoint =>
            {
                if (cancelUndo &&
                    checkpoint == CommandExecutionCheckpoint.BeforeFinalCancellationCheck)
                {
                    cancellation.Cancel();
                }
            });

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var before = document.CaptureSnapshot();
        var status = history.CaptureStatus();
        var eventCount = subscriber.Events.Count;
        cancelUndo = true;

        var undo = await history.UndoAsync(processor, cancellation.Token);

        Assert.Equal(HistoryOperationStatus.Cancelled, undo.Status);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(status, history.CaptureStatus());
        Assert.Equal(eventCount, subscriber.Events.Count);
    }

    [Fact]
    public async Task CancellationAfterFinalUndoCheckCannotSplitDocumentHistoryAndEvent()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelUndo = false;
        var document = CreateDocument(new DocumentId("test:undo-post-check-cancellation"));
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = Processor(
            new FactoryPolicy(FactoryFailure.None, FactoryFailure.None),
            subscriber,
            checkpoint =>
            {
                if (cancelUndo &&
                    checkpoint == CommandExecutionCheckpoint.AfterFinalCancellationCheck)
                {
                    cancellation.Cancel();
                }
            });

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(SourceType, document, new PointD(30d, 40d)))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        cancelUndo = true;

        var undo = await history.UndoAsync(processor, cancellation.Token);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(new PointD(10d, 20d), Position(document));
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true), history.CaptureStatus());
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2)],
            subscriber.Events.Select(change => change.CommittedRevision));
    }

    private static CommandProcessor Processor(
        ICommandHistoryPolicy sourcePolicy,
        IDocumentChangedSubscriber subscriber,
        Action<CommandExecutionCheckpoint>? checkpoint = null) =>
        new(
            handlers:
            [
                Registration(SourceType, new TestVisualHandler()),
                Registration(UndoType, new TestVisualHandler()),
                Registration(RedoType, new TestVisualHandler()),
            ],
            validators: null,
            subscribers: [subscriber],
            historyPolicies:
            [
                new CommandHistoryPolicyRegistration(SourceType, sourcePolicy),
            ],
            executionCheckpointObserver: checkpoint);

    private static CommandHandlerRegistration Registration(
        CommandTypeId typeId,
        ICommandHandler handler) =>
        new(typeId, new TestEnvelopeValidator(), handler);

    private static TestVisualCommand Command(
        CommandTypeId typeId,
        Document document,
        PointD position) =>
        new(typeId, document.DocumentId, document.Revision, position);

    private static Document CreateDocument(DocumentId documentId)
    {
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                DocumentRevision.Zero,
                [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]),
            new VisualModelSnapshot(
                documentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        VisualId,
                        ElementId,
                        new PointD(10d, 20d),
                        new SizeD(30d, 20d),
                        VisualPlacementMode.Manual),
                ]),
            new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private static PointD Position(Document document) =>
        document.VisualModel.VisualStates.Single(state => state.Id == VisualId).Position;

    public enum FactoryFailure
    {
        None,
        Throw,
        ReturnNull,
        StaleRevision,
        WrongDocument,
    }

    private sealed class TestVisualCommand(
        CommandTypeId typeId,
        DocumentId documentId,
        DocumentRevision expectedRevision,
        PointD position) : ICommand
    {
        public CommandTypeId TypeId { get; } = typeId;

        public DocumentId TargetDocumentId { get; } = documentId;

        public DocumentRevision ExpectedRevision { get; } = expectedRevision;

        public CommandCategory Category => CommandCategory.Visual;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.VisualModel;

        internal PointD Position { get; } = position;
    }

    private sealed class TestEnvelopeValidator : ICommandEnvelopeValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command) =>
            command is TestVisualCommand
                ? []
                :
                [
                    new Diagnostic(
                        "test:invalid-history-command",
                        DiagnosticSeverity.Error,
                        "Expected an immutable test visual Command."),
                ];
    }

    private sealed class TestVisualHandler : ICommandHandler
    {
        public ValueTask<CommandHandlerResult> HandleAsync(
            ICommand command,
            DocumentSnapshot document,
            CancellationToken cancellationToken)
        {
            var request = Assert.IsType<TestVisualCommand>(command);
            var visual = new VisualModelSnapshot(
                document.DocumentId,
                document.Revision,
                document.VisualModel.VisualStates.Select(state =>
                    state.Id == VisualId
                        ? new VisualStateSnapshot(
                            state.Id,
                            state.SemanticElementId,
                            request.Position,
                            state.Size,
                            state.PlacementMode,
                            state.Route,
                            state.Properties)
                        : state));
            return ValueTask.FromResult(CommandHandlerResult.Success(
                new DocumentSnapshot(document.SemanticModel, visual, document.Metadata)));
        }
    }

    private sealed class CountingVisualHandler(Action invoked) : ICommandHandler
    {
        public ValueTask<CommandHandlerResult> HandleAsync(
            ICommand command,
            DocumentSnapshot document,
            CancellationToken cancellationToken)
        {
            invoked();
            return new TestVisualHandler().HandleAsync(command, document, cancellationToken);
        }
    }

    private sealed class RejectingValidator : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(
            ICommand command,
            DocumentSnapshot document) =>
        [
            new Diagnostic(
                "test:undo-delegated-rejection",
                DiagnosticSeverity.Error,
                "The delegated validator rejected Undo."),
        ];
    }

    private sealed class FactoryPolicy(
        FactoryFailure undoFailure,
        FactoryFailure redoFailure) : ICommandHistoryPolicy
    {
        public CommandHistoryPreparationResult Prepare(
            ICommand command,
            DocumentSnapshot before,
            DocumentSnapshot committed) =>
            CommandHistoryPreparationResult.Undoable(
                new TestFactory(
                    UndoType,
                    before.VisualModel.VisualStates
                        .Single(state => state.Id == VisualId).Position,
                    undoFailure),
                new TestFactory(
                    RedoType,
                    committed.VisualModel.VisualStates
                        .Single(state => state.Id == VisualId).Position,
                    redoFailure));
    }

    private sealed class ThrowingPolicy : ICommandHistoryPolicy
    {
        public CommandHistoryPreparationResult Prepare(
            ICommand command,
            DocumentSnapshot before,
            DocumentSnapshot committed) =>
            throw new InvalidOperationException("Expected History policy failure.");
    }

    private sealed class NullResultPolicy : ICommandHistoryPolicy
    {
        public CommandHistoryPreparationResult Prepare(
            ICommand command,
            DocumentSnapshot before,
            DocumentSnapshot committed) => null!;
    }

    private sealed class TestFactory(
        CommandTypeId typeId,
        PointD position,
        FactoryFailure failure) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            failure switch
            {
                FactoryFailure.Throw =>
                    throw new InvalidOperationException("Expected History factory failure."),
                FactoryFailure.ReturnNull => null!,
                FactoryFailure.StaleRevision =>
                    new TestVisualCommand(typeId, documentId, DocumentRevision.Zero, position),
                FactoryFailure.WrongDocument =>
                    new TestVisualCommand(
                        typeId,
                        new DocumentId("test:wrong-history-document"),
                        expectedRevision,
                        position),
                _ => new TestVisualCommand(typeId, documentId, expectedRevision, position),
            };
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
