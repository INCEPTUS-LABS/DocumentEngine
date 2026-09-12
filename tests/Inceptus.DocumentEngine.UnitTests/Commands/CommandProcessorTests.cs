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

public sealed class CommandProcessorTests
{
    private static readonly DocumentId TestDocumentId = new("test:processor-document");
    private static readonly SemanticElementId ElementId = new("test:processor-element");
    private static readonly VisualStateId VisualId = new("test:processor-visual");
    private static readonly CommandTypeId TestCommandTypeId = new("test:command/custom");

    [Fact]
    public async Task EnvelopeFailuresInvokeNeitherValidatorNorHandlerAndInstallNothing()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var envelopeCalls = 0;
        var validatorCalls = 0;
        var handlerCalls = 0;
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration(
                    (_, snapshot, _) =>
                    {
                        handlerCalls++;
                        return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                    },
                    _ =>
                    {
                        envelopeCalls++;
                        return [];
                    }),
            ],
            validators:
            [
                ValidatorRegistration((_, _) =>
                {
                    validatorCalls++;
                    return [];
                }),
            ],
            subscribers: [subscriber]);
        var command = new TestCommand(
            TestCommandTypeId,
            new DocumentId("test:wrong-document"),
            DocumentRevision.Zero,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        var result = await processor.ExecuteAsync(document, command);

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Equal(document.DocumentId, result.DocumentId);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.TargetDocumentMismatch);
        Assert.Equal(0, envelopeCalls);
        Assert.Equal(0, validatorCalls);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task UnsupportedAndMissingHandlerAreDistinguished()
    {
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var unsupportedProcessor = new CommandProcessor(subscribers: [subscriber]);
        var command = MetadataCommand(document, DocumentRevision.Zero);

        var unsupported = await unsupportedProcessor.ExecuteAsync(document, command);
        var missingHandlerProcessor = new CommandProcessor(
            validators: [ValidatorRegistration((_, _) => [])],
            subscribers: [subscriber]);
        var missing = await missingHandlerProcessor.ExecuteAsync(document, command);

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, unsupported.Status);
        Assert.Contains(unsupported.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.UnsupportedCommandType);
        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, missing.Status);
        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.MissingHandler);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task KnownCommandTypeWithWrongImmutableShapeFailsEnvelopeValidation()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var command = new TestCommand(
            MoveVisualStateCommand.KnownTypeId,
            document.DocumentId,
            document.Revision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        var result = await new CommandProcessor(subscribers: [subscriber])
            .ExecuteAsync(document, command);

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task PluginNeutralEnvelopePolicyAllowsValidRequestBeforeHandlerExecution()
    {
        var document = CreateDocument();
        var envelopeCalls = 0;
        var handlerCalls = 0;
        var processor = new CommandProcessor(handlers:
        [
            Registration(
                (_, snapshot, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult(CommandHandlerResult.Success(
                        ChangeMetadata(snapshot, "envelope-approved")));
                },
                command =>
                {
                    envelopeCalls++;
                    Assert.IsType<TestCommand>(command);
                    return [];
                }),
        ]);

        var result = await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero));

        Assert.True(result.IsCommitted);
        Assert.Equal(1, envelopeCalls);
        Assert.Equal(1, handlerCalls);
        Assert.Equal(new DocumentRevision(1), document.Revision);
    }

    [Fact]
    public async Task PluginNeutralMalformedEnvelopeStopsBeforeDelegationHandlerEventAndRevision()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var delegatedCalls = 0;
        var handlerCalls = 0;
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration(
                    (_, snapshot, _) =>
                    {
                        handlerCalls++;
                        return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                    },
                    _ => [Error(
                        CommandExecutionDiagnosticCodes.InvalidCommandStructure,
                        "The plugin-neutral request shape is malformed.")]),
            ],
            validators:
            [
                ValidatorRegistration((_, _) =>
                {
                    delegatedCalls++;
                    return [];
                }),
            ],
            subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero));

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
        Assert.Equal(0, delegatedCalls);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public void MoveEnvelopePolicyUsesTheGenericEnvelopeContract()
    {
        var policy = new MoveVisualStateCommandEnvelopeValidator();
        var document = CreateDocument();
        var valid = new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            VisualId,
            new PointD(100d, 120d));
        var malformed = new TestCommand(
            MoveVisualStateCommand.KnownTypeId,
            document.DocumentId,
            document.Revision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        Assert.IsAssignableFrom<ICommandEnvelopeValidator>(policy);
        Assert.Empty(policy.Validate(valid));
        Assert.Contains(policy.Validate(malformed), diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);
    }

    [Fact]
    public async Task MissingVisualStateFailsThroughProcessorWithoutCommitOrEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                new VisualStateId("test:missing-visual"),
                new PointD(100d, 120d)));

        Assert.Equal(CommandExecutionStatus.HandlerFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateNotFound);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task InvalidAffectedDeclarationFailsEnvelopeWithoutCommitOrEvent()
    {
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var handlerCalls = 0;
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, snapshot, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                }),
            ],
            subscribers: [subscriber]);
        var invalid = new TestCommand(
            TestCommandTypeId,
            document.DocumentId,
            document.Revision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.None);

        var result = await processor.ExecuteAsync(document, invalid);

        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.InvalidAffectedComponentDeclaration);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public void DuplicateHandlerRegistrationFailsAtomically()
    {
        var handler = new DelegateHandler((_, snapshot, _) =>
            ValueTask.FromResult(CommandHandlerResult.Success(snapshot)));
        var firstPolicy = new DelegateEnvelopeValidator(_ => []);
        var secondPolicy = new DelegateEnvelopeValidator(_ => []);

        var exception = Assert.Throws<ArgumentException>(() => new CommandProcessor(
            handlers:
            [
                new CommandHandlerRegistration(TestCommandTypeId, firstPolicy, handler),
                new CommandHandlerRegistration(TestCommandTypeId, secondPolicy, handler),
            ]));

        Assert.Contains(
            CommandExecutionDiagnosticCodes.DuplicateHandlerRegistration,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerRegistrationRejectsMissingEnvelopePolicy()
    {
        var handler = new DelegateHandler((_, snapshot, _) =>
            ValueTask.FromResult(CommandHandlerResult.Success(snapshot)));

        Assert.Throws<ArgumentNullException>(() => new CommandHandlerRegistration(
            TestCommandTypeId,
            null!,
            handler));
    }

    [Fact]
    public async Task DelegatedValidationFailureAbortsBeforeHandlerAndEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var handlerCalls = 0;
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, snapshot, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                }),
            ],
            validators:
            [
                ValidatorRegistration((_, _) =>
                [
                    Error("TEST_DOMAIN_REJECTED", "The test validator rejected the request."),
                ]),
            ],
            subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero));

        Assert.Equal(CommandExecutionStatus.DelegatedValidationFailed, result.Status);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task HandlerExceptionAbortsWithoutRevisionOrEvent()
    {
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, _, _) => throw new InvalidOperationException("test failure")),
            ],
            subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero));

        Assert.Equal(CommandExecutionStatus.HandlerFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.HandlerFailure);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task ProposedStateInvariantFailureInstallsNothing()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, snapshot, _) =>
                {
                    var invalidVisual = new VisualModelSnapshot(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        [
                            new VisualStateSnapshot(
                                VisualId,
                                new SemanticElementId("test:missing-semantic"),
                                new PointD(10d, 20d),
                                new SizeD(30d, 40d),
                                VisualPlacementMode.Manual),
                        ]);
                    return ValueTask.FromResult(CommandHandlerResult.Success(
                        new DocumentSnapshot(
                            snapshot.SemanticModel,
                            invalidVisual,
                            snapshot.Metadata)));
                }),
            ],
            subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new TestCommand(
                TestCommandTypeId,
                document.DocumentId,
                document.Revision,
                CommandCategory.Visual,
                AuthoritativeDocumentComponent.VisualModel));

        Assert.Equal(CommandExecutionStatus.ProposedStateValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ProposedStateInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task UndeclaredAuthoritativeComponentChangeIsRejected()
    {
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, snapshot, _) =>
                {
                    var changedMetadata = new DocumentMetadataSnapshot(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        snapshot.Metadata.SystemManagedProperties,
                        [new("test:changed", PropertyValue.FromBoolean(true))]);
                    return ValueTask.FromResult(CommandHandlerResult.Success(
                        new DocumentSnapshot(
                            snapshot.SemanticModel,
                            snapshot.VisualModel,
                            changedMetadata)));
                }),
            ],
            subscribers: [subscriber]);
        var command = new TestCommand(
            TestCommandTypeId,
            document.DocumentId,
            document.Revision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        var result = await processor.ExecuteAsync(document, command);

        Assert.Equal(CommandExecutionStatus.ProposedStateValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.AffectedComponentViolation);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task ConcurrentSnapshotObserverSeesOnlyCompleteRevisionStates()
    {
        var handlerEntered = NewSignal();
        var releaseHandler = NewSignal();
        var observerStarted = NewSignal();
        var committedObserved = NewSignal();
        var stopObserver = NewSignal();
        var document = CreateDocument();
        var baseline = document.CaptureSnapshot();
        var processor = new CommandProcessor(handlers:
        [
            Registration(async (_, snapshot, _) =>
            {
                handlerEntered.TrySetResult();
                await releaseHandler.Task;
                return CommandHandlerResult.Success(ChangeMetadata(snapshot, "coherent"));
            }),
        ]);

        var executionTask = processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero)).AsTask();
        await handlerEntered.Task;
        var observerTask = Task.Run(async () =>
        {
            while (!stopObserver.Task.IsCompleted)
            {
                var observed = document.CaptureSnapshot();
                AssertCoherentObservedState(baseline, observed);
                observerStarted.TrySetResult();
                if (observed.Revision == new DocumentRevision(1))
                {
                    committedObserved.TrySetResult();
                }

                await Task.Yield();
            }
        });

        await observerStarted.Task;
        releaseHandler.TrySetResult();
        var result = await executionTask;
        var observation = await Task.WhenAny(committedObserved.Task, observerTask);
        if (observation == observerTask)
        {
            await observerTask;
        }

        await committedObserved.Task;
        stopObserver.TrySetResult();
        await observerTask;

        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
    }

    [Fact]
    public async Task SameDocumentCommandsSerializeAndSecondSameRevisionBecomesStale()
    {
        var handlerEntered = NewSignal();
        var releaseHandler = NewSignal();
        var handlerCalls = 0;
        var document = CreateDocument();
        var processor = new CommandProcessor(handlers:
        [
            Registration(async (_, snapshot, _) =>
            {
                handlerCalls++;
                handlerEntered.TrySetResult();
                await releaseHandler.Task;
                return CommandHandlerResult.Success(ChangeMetadata(snapshot, "first"));
            }),
        ]);
        var firstCommand = MetadataCommand(document, DocumentRevision.Zero);
        var secondCommand = MetadataCommand(document, DocumentRevision.Zero);

        var firstTask = processor.ExecuteAsync(document, firstCommand).AsTask();
        await handlerEntered.Task;
        var secondTask = processor.ExecuteAsync(document, secondCommand).AsTask();
        Assert.False(secondTask.IsCompleted);

        releaseHandler.TrySetResult();
        var first = await firstTask;
        var second = await secondTask;

        Assert.True(first.IsCommitted);
        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, second.Status);
        Assert.Contains(second.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(1, handlerCalls);
        Assert.Equal(new DocumentRevision(1), document.Revision);
    }

    [Fact]
    public async Task IndependentDocumentsDoNotShareAnExecutionGate()
    {
        var firstDocument = CreateDocument(new DocumentId("test:first-document"));
        var secondDocument = CreateDocument(new DocumentId("test:second-document"));
        var firstHandlerEntered = NewSignal();
        var releaseFirstHandler = NewSignal();
        var processor = new CommandProcessor(handlers:
        [
            Registration(async (_, snapshot, _) =>
            {
                if (snapshot.DocumentId == firstDocument.DocumentId)
                {
                    firstHandlerEntered.TrySetResult();
                    await releaseFirstHandler.Task;
                }

                return CommandHandlerResult.Success(ChangeMetadata(snapshot, snapshot.DocumentId.Value));
            }),
        ]);

        var firstTask = processor.ExecuteAsync(
            firstDocument,
            MetadataCommand(firstDocument, DocumentRevision.Zero)).AsTask();
        await firstHandlerEntered.Task;
        var secondTask = processor.ExecuteAsync(
            secondDocument,
            MetadataCommand(secondDocument, DocumentRevision.Zero)).AsTask();

        var secondResult = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secondResult.IsCommitted);
        Assert.False(firstTask.IsCompleted);

        releaseFirstHandler.TrySetResult();
        Assert.True((await firstTask).IsCommitted);
        Assert.Equal(new DocumentRevision(1), firstDocument.Revision);
        Assert.Equal(new DocumentRevision(1), secondDocument.Revision);
    }

    [Fact]
    public async Task PreCancelledExecutionCreatesNoCommitOrEvent()
    {
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                new PointD(100d, 120d)),
            cancellation.Token);

        Assert.Equal(CommandExecutionStatus.Cancelled, result.Status);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task CancellationDuringDelegatedValidationAbortsBeforeHandler()
    {
        using var cancellation = new CancellationTokenSource();
        using var releaseValidator = new ManualResetEventSlim();
        var validatorEntered = NewSignal();
        var handlerCalls = 0;
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, snapshot, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult(CommandHandlerResult.Success(snapshot));
                }),
            ],
            validators:
            [
                ValidatorRegistration((_, _) =>
                {
                    validatorEntered.TrySetResult();
                    releaseValidator.Wait();
                    return [];
                }),
            ],
            subscribers: [subscriber]);

        var executionTask = Task.Run(async () => await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero),
            cancellation.Token));
        await validatorEntered.Task;
        await cancellation.CancelAsync();
        releaseValidator.Set();
        var result = await executionTask;

        Assert.Equal(CommandExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task CancellationBeforeHandlerCompletionAbortsWithoutHalfCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var handlerEntered = NewSignal();
        var neverReleased = NewSignal();
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration(async (_, snapshot, cancellationToken) =>
                {
                    handlerEntered.TrySetResult();
                    await neverReleased.Task.WaitAsync(cancellationToken);
                    return CommandHandlerResult.Success(ChangeMetadata(snapshot, "unreachable"));
                }),
            ],
            subscribers: [subscriber]);

        var executionTask = processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero),
            cancellation.Token).AsTask();
        await handlerEntered.Task;
        await cancellation.CancelAsync();
        var result = await executionTask;

        Assert.Equal(CommandExecutionStatus.Cancelled, result.Status);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Null(result.CommittedSnapshot);
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task PostInstallCancellationCannotUndoTheCommittedState()
    {
        using var cancellation = new CancellationTokenSource();
        var subscriber = new CancellingSubscriber(cancellation);
        var document = CreateDocument();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                new PointD(100d, 120d)),
            cancellation.Token);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(document.CaptureSnapshot(), result.CommittedSnapshot);
    }

    [Fact]
    public async Task ExecutionGateReleasesAfterHandlerFailure()
    {
        var invocation = 0;
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            handlers:
            [
                Registration((_, snapshot, _) =>
                {
                    invocation++;
                    return ValueTask.FromResult(invocation == 1
                        ? CommandHandlerResult.Failure([Error("TEST_FIRST_FAILURE", "first failed")])
                        : CommandHandlerResult.Success(ChangeMetadata(snapshot, "second")));
                }),
            ],
            subscribers: [subscriber]);

        var failed = await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero));
        var committed = await processor.ExecuteAsync(
            document,
            MetadataCommand(document, DocumentRevision.Zero));

        Assert.Equal(CommandExecutionStatus.HandlerFailed, failed.Status);
        Assert.True(committed.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        Assert.Single(subscriber.Events);
    }

    [Fact]
    public async Task SubscriberFailureIsDiagnosticOnlyAndDoesNotBlockLaterSubscriber()
    {
        var document = CreateDocument();
        var recording = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers:
        [
            new ThrowingSubscriber(),
            recording,
        ]);

        var result = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                new PointD(100d, 120d)));
        var second = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                new DocumentRevision(1),
                VisualId,
                new PointD(140d, 160d)));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.True(second.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(2, recording.Events.Count);
        Assert.Equal(
            2,
            processor.CaptureDispatchDiagnostics().Count(diagnostic =>
                diagnostic.Code == CommandExecutionDiagnosticCodes.SubscriberFailure));
    }

    [Fact]
    public async Task RevisionOverflowFailsBeforeInstallationAndEventCreation()
    {
        var document = CreateDocument(revision: new DocumentRevision(ulong.MaxValue));
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                VisualId,
                new PointD(100d, 120d)));

        Assert.Equal(CommandExecutionStatus.InternalFailure, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.RevisionPreparationFailure);
        Assert.Equal(new DocumentRevision(ulong.MaxValue), document.Revision);
        Assert.Empty(subscriber.Events);
    }

    private static CommandHandlerRegistration Registration(
        Func<ICommand, DocumentSnapshot, CancellationToken, ValueTask<CommandHandlerResult>> handle,
        Func<ICommand, ImmutableArray<Diagnostic>>? validateEnvelope = null) =>
        new(
            TestCommandTypeId,
            new DelegateEnvelopeValidator(validateEnvelope ?? (static _ => [])),
            new DelegateHandler(handle));

    private static CommandValidatorRegistration ValidatorRegistration(
        Func<ICommand, DocumentSnapshot, ImmutableArray<Diagnostic>> validate) =>
        new(
            TestCommandTypeId,
            new CommandValidatorId("test:validator"),
            new DelegateValidator(validate));

    private static TestCommand MetadataCommand(
        Document document,
        DocumentRevision expectedRevision) =>
        new(
            TestCommandTypeId,
            document.DocumentId,
            expectedRevision,
            CommandCategory.Metadata,
            AuthoritativeDocumentComponent.Metadata);

    private static Document CreateDocument(
        DocumentId? documentId = null,
        DocumentRevision? revision = null)
    {
        var snapshot = Snapshot(
            documentId ?? TestDocumentId,
            revision ?? DocumentRevision.Zero);
        var result = snapshot.Revision == DocumentRevision.Zero
            ? DocumentFactory.Create(snapshot)
            : DocumentReconstructor.Reconstruct(snapshot);
        return Assert.IsType<Document>(result.Document);
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
                    VisualPlacementMode.Manual,
                    [new PointD(0d, 0d), new PointD(30d, 40d)]),
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

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);

    private static void AssertCoherentObservedState(
        DocumentSnapshot baseline,
        DocumentSnapshot observed)
    {
        Assert.Equal(observed.DocumentId, observed.SemanticModel.DocumentId);
        Assert.Equal(observed.DocumentId, observed.VisualModel.DocumentId);
        Assert.Equal(observed.DocumentId, observed.Metadata.DocumentId);
        Assert.Equal(observed.Revision, observed.SemanticModel.Revision);
        Assert.Equal(observed.Revision, observed.VisualModel.Revision);
        Assert.Equal(observed.Revision, observed.Metadata.Revision);

        if (observed.Revision == DocumentRevision.Zero)
        {
            Assert.Equal(baseline, observed);
            return;
        }

        Assert.Equal(new DocumentRevision(1), observed.Revision);
        Assert.Equal(
            baseline.SemanticModel.Elements.AsEnumerable(),
            observed.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            baseline.SemanticModel.Relationships.AsEnumerable(),
            observed.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            baseline.VisualModel.VisualStates.AsEnumerable(),
            observed.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(
            "coherent",
            observed.Metadata.ExtensionProperties["test:value"].TextValue);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestCommand : ICommand
    {
        internal TestCommand(
            CommandTypeId typeId,
            DocumentId targetDocumentId,
            DocumentRevision expectedRevision,
            CommandCategory category,
            AuthoritativeDocumentComponent affectedComponents)
        {
            TypeId = typeId;
            TargetDocumentId = targetDocumentId;
            ExpectedRevision = expectedRevision;
            Category = category;
            AffectedComponents = affectedComponents;
        }

        public CommandTypeId TypeId { get; }

        public DocumentId TargetDocumentId { get; }

        public DocumentRevision ExpectedRevision { get; }

        public CommandCategory Category { get; }

        public AuthoritativeDocumentComponent AffectedComponents { get; }
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

    private sealed class DelegateEnvelopeValidator(
        Func<ICommand, ImmutableArray<Diagnostic>> validate) : ICommandEnvelopeValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command) => validate(command);
    }

    private sealed class DelegateValidator(
        Func<ICommand, DocumentSnapshot, ImmutableArray<Diagnostic>> validate) : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
            validate(command, document);
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

    private sealed class ThrowingSubscriber : IDocumentChangedSubscriber
    {
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change) =>
            throw new InvalidOperationException("test subscriber failure");
    }

    private sealed class CancellingSubscriber(CancellationTokenSource cancellation) :
        IDocumentChangedSubscriber
    {
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
