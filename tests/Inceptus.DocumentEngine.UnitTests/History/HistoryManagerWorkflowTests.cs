using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class HistoryManagerWorkflowTests
{
    private static readonly DocumentId TestDocumentId = new("test:history-workflow");
    private static readonly SemanticElementId ElementId = new("test:history-element");
    private static readonly VisualStateId VisualId = new("test:history-visual");

    [Fact]
    public async Task BuiltInMoveRecordsOneEntryAndUndoRedoUseCurrentRevisions()
    {
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var processor = new CommandProcessor();

        var move = await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(30d, 40d)));
        var undo = await manager.UndoAsync(processor);
        var redo = await manager.RedoAsync(processor);

        Assert.True(move.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, move.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), move.CommittedRevision);
        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(1), undo.PreviousRevision);
        Assert.Equal(new DocumentRevision(2), undo.CommittedRevision);
        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), redo.PreviousRevision);
        Assert.Equal(new DocumentRevision(3), redo.CommittedRevision);
        Assert.Equal(new PointD(30d, 40d), Position(document));
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), manager.CaptureStatus());
    }

    [Fact]
    public async Task NewNormalCommitAfterUndoTruncatesRedoWithoutAddingNavigationEntries()
    {
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var processor = new CommandProcessor();

        Assert.True((await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(20d, 20d)))).IsCommitted);
        Assert.True((await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(30d, 30d)))).IsCommitted);
        Assert.True((await manager.UndoAsync(processor)).IsCommitted);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: true), manager.CaptureStatus());

        Assert.True((await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(40d, 40d)))).IsCommitted);

        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false), manager.CaptureStatus());
        var unavailable = await manager.RedoAsync(processor);
        Assert.Equal(HistoryOperationStatus.NothingToRedo, unavailable.Status);
        Assert.Contains(unavailable.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.RedoUnavailable);
        Assert.Equal(new DocumentRevision(4), document.Revision);
        Assert.Equal(new PointD(40d, 40d), Position(document));

        Assert.True((await manager.UndoAsync(processor)).IsCommitted);
        Assert.Equal(new PointD(20d, 20d), Position(document));
        Assert.Equal(2, manager.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task UnavailableAndFailedOperationsLeaveHistoryAndDocumentStable()
    {
        var emptyDocument = CreateDocument(new DocumentId("test:empty-history"));
        var emptyManager = new HistoryManager(emptyDocument);
        var processor = new CommandProcessor();

        var unavailableUndo = await emptyManager.UndoAsync(processor);
        var unavailableRedo = await emptyManager.RedoAsync(processor);

        Assert.Equal(HistoryOperationStatus.NothingToUndo, unavailableUndo.Status);
        Assert.Contains(unavailableUndo.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.UndoUnavailable);
        Assert.Equal(HistoryOperationStatus.NothingToRedo, unavailableRedo.Status);
        Assert.Contains(unavailableRedo.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.RedoUnavailable);
        Assert.Equal(new HistoryStatus(0, false, false), emptyManager.CaptureStatus());
        Assert.Equal(DocumentRevision.Zero, emptyDocument.Revision);

        var stale = await emptyManager.ExecuteAsync(
            processor,
            new MoveVisualStateCommand(
                emptyDocument.DocumentId,
                new DocumentRevision(1),
                VisualId,
                new PointD(50d, 50d)));

        Assert.Equal(HistoryOperationStatus.CommandFailed, stale.Status);
        Assert.Equal(new HistoryStatus(0, false, false), emptyManager.CaptureStatus());
        Assert.Equal(DocumentRevision.Zero, emptyDocument.Revision);

        var undoFailure = await CreateFailedNavigationScenarioAsync(failUndo: true);
        Assert.Equal(HistoryOperationStatus.CommandFailed, undoFailure.Result.Status);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), undoFailure.Status);
        Assert.Equal(new DocumentRevision(1), undoFailure.Document.Revision);

        var redoFailure = await CreateFailedNavigationScenarioAsync(failUndo: false);
        Assert.Equal(HistoryOperationStatus.CommandFailed, redoFailure.Result.Status);
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true), redoFailure.Status);
        Assert.Equal(new DocumentRevision(2), redoFailure.Document.Revision);
    }

    [Fact]
    public async Task CancellationDoesNotAdvanceDocumentOrHistoryCursor()
    {
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var processor = new CommandProcessor();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var normal = await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(20d, 20d)),
            cancelled.Token);

        Assert.Equal(HistoryOperationStatus.Cancelled, normal.Status);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(new HistoryStatus(0, false, false), manager.CaptureStatus());

        Assert.True((await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(30d, 30d)))).IsCommitted);
        var beforeUndo = manager.CaptureStatus();
        var undo = await manager.UndoAsync(processor, cancelled.Token);

        Assert.Equal(HistoryOperationStatus.Cancelled, undo.Status);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(beforeUndo, manager.CaptureStatus());
        Assert.Equal(new PointD(30d, 30d), Position(document));

        Assert.True((await manager.UndoAsync(processor)).IsCommitted);
        var beforeRedo = manager.CaptureStatus();
        var redo = await manager.RedoAsync(processor, cancelled.Token);

        Assert.Equal(HistoryOperationStatus.Cancelled, redo.Status);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(beforeRedo, manager.CaptureStatus());
        Assert.Equal(new PointD(10d, 10d), Position(document));
    }

    [Fact]
    public async Task CancellationWhileQueuedBehindDocumentGateRecordsNoHistory()
    {
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var sourceType = new CommandTypeId("test:queued-history-command");
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new CommandProcessor(
            handlers:
            [
                new CommandHandlerRegistration(
                    sourceType,
                    new TestEnvelopeValidator(),
                    new BlockingVisualHandler(entered, release)),
            ],
            historyPolicies:
            [
                new CommandHistoryPolicyRegistration(
                    sourceType,
                    new TestHistoryPolicy(
                        new CommandTypeId("test:queued-history-undo"),
                        new CommandTypeId("test:queued-history-redo"))),
            ]);
        var first = new TestVisualCommand(
            sourceType,
            document.DocumentId,
            DocumentRevision.Zero,
            new PointD(20d, 20d));
        var queued = new TestVisualCommand(
            sourceType,
            document.DocumentId,
            DocumentRevision.Zero,
            new PointD(30d, 30d));
        using var cancellation = new CancellationTokenSource();

        var firstTask = manager.ExecuteAsync(processor, first).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedTask = manager.ExecuteAsync(
            processor,
            queued,
            cancellation.Token).AsTask();
        await cancellation.CancelAsync();
        var cancelled = await queuedTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HistoryOperationStatus.Cancelled, cancelled.Status);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(new HistoryStatus(0, false, false), manager.CaptureStatus());

        release.TrySetResult();
        Assert.True((await firstTask.WaitAsync(TimeSpan.FromSeconds(5))).IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(new HistoryStatus(1, true, false), manager.CaptureStatus());
    }

    [Fact]
    public async Task HistoryOperationsNeverObserveOrModifyEditorState()
    {
        var editorState = new EditorStateSnapshot(
            selection: [new VisualStateId("visual:selected")],
            activeToolId: "tool:move",
            viewport: new ViewportSnapshot(2d, new VectorD(15d, -5d)));
        var editorStore = new EditorStateStore(editorState);
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var processor = new CommandProcessor();

        Assert.True((await manager.ExecuteAsync(
            processor,
            Move(document.Revision, new PointD(70d, 80d)))).IsCommitted);
        Assert.True((await manager.UndoAsync(processor)).IsCommitted);
        Assert.True((await manager.RedoAsync(processor)).IsCommitted);

        Assert.Same(editorState, editorStore.CaptureSnapshot());
        Assert.Equal("tool:move", editorStore.CaptureSnapshot().ActiveToolId);
        Assert.Equal(new VisualStateId("visual:selected"),
            Assert.Single(editorStore.CaptureSnapshot().Selection));
    }

    [Fact]
    public async Task TwoProcessorsAgainstOneManagerProduceOneCommitAndOneStaleResult()
    {
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var firstProcessor = new CommandProcessor();
        var secondProcessor = new CommandProcessor();
        var first = Move(DocumentRevision.Zero, new PointD(90d, 90d));
        var second = Move(DocumentRevision.Zero, new PointD(100d, 100d));
        using var start = new ManualResetEventSlim();

        var firstTask = Task.Run(async () =>
        {
            start.Wait();
            return await manager.ExecuteAsync(firstProcessor, first);
        });
        var secondTask = Task.Run(async () =>
        {
            start.Wait();
            return await manager.ExecuteAsync(secondProcessor, second);
        });
        start.Set();

        var results = await Task.WhenAll(firstTask, secondTask)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(results, result => result.IsCommitted);
        Assert.Single(results, result =>
            result.Status == HistoryOperationStatus.CommandFailed);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), manager.CaptureStatus());
    }

    private static async Task<FailedNavigationScenario> CreateFailedNavigationScenarioAsync(
        bool failUndo)
    {
        var document = CreateDocument(new DocumentId(
            failUndo ? "test:failed-undo" : "test:failed-redo"));
        var manager = new HistoryManager(document);
        var sourceType = new CommandTypeId(
            failUndo ? "test:source-failed-undo" : "test:source-failed-redo");
        var undoType = new CommandTypeId(
            failUndo ? "test:undo-fails" : "test:undo-succeeds");
        var redoType = new CommandTypeId("test:redo-fails");
        var processor = new CommandProcessor(
            handlers:
            [
                Registration(sourceType, succeeds: true),
                Registration(undoType, succeeds: !failUndo),
                Registration(redoType, succeeds: false),
            ],
            historyPolicies:
            [
                new CommandHistoryPolicyRegistration(
                    sourceType,
                    new TestHistoryPolicy(undoType, redoType)),
            ]);

        var normal = await manager.ExecuteAsync(
            processor,
            new TestVisualCommand(
                sourceType,
                document.DocumentId,
                document.Revision,
                new PointD(50d, 60d)));
        Assert.True(normal.IsCommitted);

        if (failUndo)
        {
            var failed = await manager.UndoAsync(processor);
            return new FailedNavigationScenario(
                document,
                failed,
                manager.CaptureStatus());
        }

        Assert.True((await manager.UndoAsync(processor)).IsCommitted);
        var redo = await manager.RedoAsync(processor);
        return new FailedNavigationScenario(document, redo, manager.CaptureStatus());
    }

    private static CommandHandlerRegistration Registration(
        CommandTypeId typeId,
        bool succeeds) =>
        new(typeId, new TestEnvelopeValidator(), new TestVisualHandler(succeeds));

    [Fact]
    public async Task ScopeNavigationReplayAppliesWithoutRevisionAndFailureLeavesCursorAtomic()
    {
        var document = CreateDocument();
        var manager = new HistoryManager(document);
        var processor = new CommandProcessor();
        var rootScopeId = new DocumentScopeId("test:history-workflow:root");
        var childScopeId = new DocumentScopeId("test:history-workflow:child");
        var preparation = manager.PrepareScopeNavigation(rootScopeId, childScopeId);
        Assert.True(preparation.Succeeded);
        manager.InstallPrepared(preparation.Mutation!);
        var original = document.CaptureSnapshot();
        DocumentScopeId? replayedTarget = null;

        var undo = await manager.UndoAsync(
            processor,
            (targetScopeId, _) =>
            {
                replayedTarget = targetScopeId;
                return ValueTask.FromResult(true);
            });

        Assert.True(undo.IsApplied);
        Assert.False(undo.IsCommitted);
        Assert.Equal(rootScopeId, replayedTarget);
        Assert.Equal(original, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true),
            manager.CaptureStatus());

        var failedRedo = await manager.RedoAsync(
            processor,
            (targetScopeId, _) =>
            {
                replayedTarget = targetScopeId;
                return ValueTask.FromResult(false);
            });

        Assert.Equal(HistoryOperationStatus.InternalFailure, failedRedo.Status);
        Assert.Contains(failedRedo.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.RuntimeReplayFailed);
        Assert.Equal(childScopeId, replayedTarget);
        Assert.Equal(original, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true),
            manager.CaptureStatus());

        var redo = await manager.RedoAsync(
            processor,
            (targetScopeId, _) =>
            {
                replayedTarget = targetScopeId;
                return ValueTask.FromResult(true);
            });

        Assert.True(redo.IsApplied);
        Assert.Equal(childScopeId, replayedTarget);
        Assert.Equal(original, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false),
            manager.CaptureStatus());
    }

    private static Document CreateDocument(DocumentId? documentId = null)
    {
        var id = documentId ?? TestDocumentId;
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                id,
                DocumentRevision.Zero,
                [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]),
            new VisualModelSnapshot(
                id,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        VisualId,
                        ElementId,
                        new PointD(10d, 10d),
                        new SizeD(30d, 20d),
                        VisualPlacementMode.Manual),
                ]),
            new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private static MoveVisualStateCommand Move(
        DocumentRevision revision,
        PointD position) =>
        new(TestDocumentId, revision, VisualId, position);

    private static PointD Position(Document document) =>
        document.VisualModel.VisualStates.Single(state => state.Id == VisualId).Position;

    private sealed record FailedNavigationScenario(
        Document Document,
        HistoryOperationResult Result,
        HistoryStatus Status);

    private sealed class TestVisualCommand(
        CommandTypeId typeId,
        DocumentId documentId,
        DocumentRevision revision,
        PointD position) : ICommand
    {
        public CommandTypeId TypeId { get; } = typeId;

        public DocumentId TargetDocumentId { get; } = documentId;

        public DocumentRevision ExpectedRevision { get; } = revision;

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
                        "test:invalid-command",
                        DiagnosticSeverity.Error,
                        "Expected a test visual Command."),
                ];
    }

    private sealed class TestVisualHandler(bool succeeds) : ICommandHandler
    {
        public ValueTask<CommandHandlerResult> HandleAsync(
            ICommand command,
            DocumentSnapshot document,
            CancellationToken cancellationToken)
        {
            if (!succeeds)
            {
                return ValueTask.FromResult(CommandHandlerResult.Failure(
                [
                    new Diagnostic(
                        "test:handler-failed",
                        DiagnosticSeverity.Error,
                        "The test handler rejected restoration."),
                ]));
            }

            var request = Assert.IsType<TestVisualCommand>(command);
            var proposedVisual = new VisualModelSnapshot(
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
                new DocumentSnapshot(
                    document.SemanticModel,
                    proposedVisual,
                    document.Metadata)));
        }
    }

    private sealed class BlockingVisualHandler(
        TaskCompletionSource entered,
        TaskCompletionSource release) : ICommandHandler
    {
        public async ValueTask<CommandHandlerResult> HandleAsync(
            ICommand command,
            DocumentSnapshot document,
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return await new TestVisualHandler(succeeds: true)
                .HandleAsync(command, document, cancellationToken);
        }
    }

    private sealed class TestHistoryPolicy(
        CommandTypeId undoType,
        CommandTypeId redoType) : ICommandHistoryPolicy
    {
        public CommandHistoryPreparationResult Prepare(
            ICommand command,
            DocumentSnapshot before,
            DocumentSnapshot committed)
        {
            var oldPosition = before.VisualModel.VisualStates
                .Single(state => state.Id == VisualId).Position;
            var newPosition = committed.VisualModel.VisualStates
                .Single(state => state.Id == VisualId).Position;
            return CommandHistoryPreparationResult.Undoable(
                new TestFactory(undoType, oldPosition),
                new TestFactory(redoType, newPosition));
        }
    }

    private sealed class TestFactory(
        CommandTypeId typeId,
        PointD position) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new TestVisualCommand(typeId, documentId, expectedRevision, position);
    }
}
