using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Executes immutable Commands through the single transactional modification path.
/// </summary>
public sealed class CommandProcessor
{
    private const int DispatchDiagnosticRetentionLimit = 256;

    private readonly Queue<Diagnostic> _dispatchDiagnostics =
        new(DispatchDiagnosticRetentionLimit);
    private readonly object _dispatchDiagnosticsSync = new();
    private readonly Action<CommandExecutionCheckpoint>? _executionCheckpointObserver;
    private readonly CommandHandlerRegistry _handlers;
    private readonly HistoryCoordinator _history;
    private readonly ImmutableArray<IDocumentChangedSubscriber> _subscribers;
    private readonly CommandValidationService _validation;
    private readonly IElementConnectorAnchorPolicyProvider _connectorAnchorPolicyProvider;

    public CommandProcessor(
        IEnumerable<CommandHandlerRegistration>? handlers = null,
        IEnumerable<CommandValidatorRegistration>? validators = null,
        IEnumerable<IDocumentChangedSubscriber>? subscribers = null,
        IEnumerable<CommandHistoryPolicyRegistration>? historyPolicies = null,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
        : this(
            handlers,
            validators,
            subscribers,
            historyPolicies,
            connectorAnchorPolicyProvider,
            executionCheckpointObserver: null)
    {
    }

    internal CommandProcessor(
        IEnumerable<CommandHandlerRegistration>? handlers,
        IEnumerable<CommandValidatorRegistration>? validators,
        IEnumerable<IDocumentChangedSubscriber>? subscribers,
        Action<CommandExecutionCheckpoint>? executionCheckpointObserver)
        : this(
            handlers,
            validators,
            subscribers,
            historyPolicies: null,
            connectorAnchorPolicyProvider: null,
            executionCheckpointObserver: executionCheckpointObserver)
    {
    }

    internal CommandProcessor(
        IEnumerable<CommandHandlerRegistration>? handlers,
        IEnumerable<CommandValidatorRegistration>? validators,
        IEnumerable<IDocumentChangedSubscriber>? subscribers,
        IEnumerable<CommandHistoryPolicyRegistration>? historyPolicies,
        Action<CommandExecutionCheckpoint>? executionCheckpointObserver)
        : this(
            handlers,
            validators,
            subscribers,
            historyPolicies,
            connectorAnchorPolicyProvider: null,
            executionCheckpointObserver)
    {
    }

    internal CommandProcessor(
        IEnumerable<CommandHandlerRegistration>? handlers,
        IEnumerable<CommandValidatorRegistration>? validators,
        IEnumerable<IDocumentChangedSubscriber>? subscribers,
        IEnumerable<CommandHistoryPolicyRegistration>? historyPolicies,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider,
        Action<CommandExecutionCheckpoint>? executionCheckpointObserver)
    {
        _executionCheckpointObserver = executionCheckpointObserver;
        _connectorAnchorPolicyProvider = connectorAnchorPolicyProvider ??
            ElementConnectorAnchorPolicyRegistry.Default;
        _validation = CreateValidationService(CreateValidatorRegistrations(validators));
        var ordinaryHandlers = CreateHandlerRegistrations(handlers, _connectorAnchorPolicyProvider).ToArray();
        var compoundHandler = new CompoundDocumentCommandHandler(
            new CommandHandlerRegistry(ordinaryHandlers), _validation, _connectorAnchorPolicyProvider);
        var restorationHandler = new RestoreCompoundDocumentCommandHandler();
        _handlers = new CommandHandlerRegistry([
            .. ordinaryHandlers,
            new CommandHandlerRegistration(CompoundDocumentCommand.KnownTypeId, compoundHandler, compoundHandler),
            new CommandHandlerRegistration(RestoreCompoundDocumentCommand.KnownTypeId, restorationHandler, restorationHandler),
        ]);
        _history = new HistoryCoordinator(CreateHistoryPolicyRegistrations(historyPolicies));
        _subscribers = CopySubscribers(subscribers);
    }

    /// <summary>
    /// Executes one Command and completes after a committed event is enqueued, without
    /// waiting for external subscriber delivery.
    /// </summary>
    public async ValueTask<CommandExecutionResult> ExecuteAsync(
        Document document,
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteCoreAsync(
            document,
            command,
            historyStore: null,
            recordNormalHistory: false,
            cancellationToken).ConfigureAwait(false);
        return execution.Result;
    }

    internal async ValueTask<HistoryOperationResult> ExecuteWithHistoryAsync(
        Document document,
        ICommand command,
        HistoryStore historyStore,
        CancellationToken cancellationToken)
    {
        var execution = await ExecuteCoreAsync(
            document,
            command,
            historyStore,
            recordNormalHistory: true,
            cancellationToken).ConfigureAwait(false);
        return CreateHistoryOperationResult(
            execution.Result,
            execution.HistoryStatus!);
    }

    internal async ValueTask<HistoryOperationResult> ExecuteHistoryOperationAsync(
        Document document,
        HistoryStore historyStore,
        bool isUndo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(historyStore);

        var coordinator = document.ExecutionCoordinator;
        var gateHeld = false;
        var startDispatch = false;
        DocumentDispatchWorkItem? enqueuedWorkItem = null;
        ICommand? restorationCommand = null;
        CommandExecutionResult? execution = null;
        HistoryStatus? completedHistoryStatus = null;
        HistoryOperationResult? earlyResult = null;

        try
        {
            await coordinator.WaitForExecutionAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;

            var baseSnapshot = document.CaptureSnapshot();
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = isUndo
                ? historyStore.PrepareUndo(baseSnapshot.DocumentId, baseSnapshot.Revision)
                : historyStore.PrepareRedo(baseSnapshot.DocumentId, baseSnapshot.Revision);
            if (!preparation.Succeeded || preparation.Mutation?.RestorationCommand is null)
            {
                var unavailableCode = isUndo
                    ? HistoryDiagnosticCodes.UndoUnavailable
                    : HistoryDiagnosticCodes.RedoUnavailable;
                var status = preparation.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == unavailableCode)
                    ? isUndo
                        ? HistoryOperationStatus.NothingToUndo
                        : HistoryOperationStatus.NothingToRedo
                    : HistoryOperationStatus.InternalFailure;
                earlyResult = HistoryOperationResult.CreateNotCommitted(
                    baseSnapshot.DocumentId,
                    commandTypeId: null,
                    status,
                    baseSnapshot.Revision,
                    historyStore.CaptureStatus(),
                    preparation.Diagnostics);
            }
            else
            {
                restorationCommand = preparation.Mutation.RestorationCommand;
                (execution, startDispatch, enqueuedWorkItem) = await ExecuteUnderGateAsync(
                    document,
                    restorationCommand,
                    historyStore,
                    recordNormalHistory: false,
                    preparation.Mutation,
                    cancellationToken).ConfigureAwait(false);
                completedHistoryStatus = historyStore.CaptureStatus();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var snapshot = document.CaptureSnapshot();
            earlyResult = HistoryOperationResult.CreateNotCommitted(
                snapshot.DocumentId,
                restorationCommand?.TypeId,
                HistoryOperationStatus.Cancelled,
                snapshot.Revision,
                historyStore.CaptureStatus(),
                [new Diagnostic(
                    CommandExecutionDiagnosticCodes.Cancelled,
                    DiagnosticSeverity.Information,
                    isUndo
                        ? "The Undo request was cancelled before commit."
                        : "The Redo request was cancelled before commit.",
                    restorationCommand?.TypeId.Value ?? snapshot.DocumentId.Value)]);
        }
#pragma warning disable CA1031 // Unexpected non-fatal faults become immutable History results.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            var snapshot = document.CaptureSnapshot();
            earlyResult = HistoryOperationResult.CreateNotCommitted(
                snapshot.DocumentId,
                restorationCommand?.TypeId,
                HistoryOperationStatus.InternalFailure,
                snapshot.Revision,
                historyStore.CaptureStatus(),
                [new Diagnostic(
                    CommandExecutionDiagnosticCodes.InternalFailure,
                    DiagnosticSeverity.Error,
                    isUndo
                        ? "The Undo request failed unexpectedly before commit."
                        : "The Redo request failed unexpectedly before commit.",
                    restorationCommand?.TypeId.Value ?? snapshot.DocumentId.Value,
                    [
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            ExceptionType(exception)),
                    ])]);
        }
#pragma warning restore CA1031
        finally
        {
            if (gateHeld)
            {
                coordinator.ReleaseExecution();
                enqueuedWorkItem?.ReleaseForDispatch();
            }
        }

        if (startDispatch)
        {
            StartDispatch(
                coordinator,
                restorationCommand?.TypeId.Value ?? document.DocumentId.Value);
        }

        if (earlyResult is not null)
        {
            return earlyResult;
        }

        return CreateHistoryOperationResult(execution!, completedHistoryStatus!);
    }

    private async ValueTask<(
        CommandExecutionResult Result,
        HistoryStatus? HistoryStatus)> ExecuteCoreAsync(
        Document document,
        ICommand command,
        HistoryStore? historyStore,
        bool recordNormalHistory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.TypeId);
        ArgumentNullException.ThrowIfNull(command.TargetDocumentId);

        var coordinator = document.ExecutionCoordinator;
        var gateHeld = false;
        var startDispatch = false;
        DocumentDispatchWorkItem? enqueuedWorkItem = null;
        CommandExecutionResult result;
        HistoryStatus? historyStatus = null;

        try
        {
            await coordinator.WaitForExecutionAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            (result, startDispatch, enqueuedWorkItem) = await ExecuteUnderGateAsync(
                document,
                command,
                historyStore,
                recordNormalHistory,
                preparedHistoryMutation: null,
                cancellationToken).ConfigureAwait(false);
            historyStatus = historyStore?.CaptureStatus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var snapshot = document.CaptureSnapshot();
            result = Failure(
                snapshot.DocumentId,
                command,
                CommandExecutionStatus.Cancelled,
                snapshot.Revision,
                CancelledDiagnostic(command));
            historyStatus = historyStore?.CaptureStatus();
        }
#pragma warning disable CA1031 // Unexpected non-fatal faults become sanitized execution results.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            var snapshot = document.CaptureSnapshot();
            result = Failure(
                snapshot.DocumentId,
                command,
                CommandExecutionStatus.InternalFailure,
                snapshot.Revision,
                UnexpectedFailureDiagnostic(command, exception));
            historyStatus = historyStore?.CaptureStatus();
        }
#pragma warning restore CA1031
        finally
        {
            if (gateHeld)
            {
                coordinator.ReleaseExecution();
                enqueuedWorkItem?.ReleaseForDispatch();
            }
        }

        if (startDispatch)
        {
            StartDispatch(coordinator, command.TypeId.Value);
        }

        return (result, historyStatus);
    }

    private void StartDispatch(
        DocumentExecutionCoordinator coordinator,
        string sourceIdentity)
    {
        try
        {
            var scheduling = coordinator.TryStartDispatch();
            if (!scheduling.IsScheduled)
            {
                ReportDispatchSchedulingFailure(sourceIdentity, scheduling.Failure);
            }
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            // TryStartDispatch contains its own isolation. This last-resort guard
            // preserves the committed outcome if an impossible internal fault occurs.
            ReportDispatchSchedulingFailure(sourceIdentity, exception);
        }
    }

    private static HistoryOperationResult CreateHistoryOperationResult(
        CommandExecutionResult execution,
        HistoryStatus historyStatus)
    {
        if (execution.IsCommitted)
        {
            return HistoryOperationResult.CreateCommitted(
                execution.DocumentId,
                execution.CommandTypeId,
                execution.PreviousRevision,
                execution.CommittedRevision!.Value,
                historyStatus,
                execution.Diagnostics);
        }

        var status = execution.Status switch
        {
            CommandExecutionStatus.Cancelled => HistoryOperationStatus.Cancelled,
            CommandExecutionStatus.InternalFailure => HistoryOperationStatus.InternalFailure,
            _ => HistoryOperationStatus.CommandFailed,
        };
        return HistoryOperationResult.CreateNotCommitted(
            execution.DocumentId,
            execution.CommandTypeId,
            status,
            execution.PreviousRevision,
            historyStatus,
            execution.Diagnostics);
    }

    internal static Task WaitForEventDispatchIdleAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ExecutionCoordinator.WaitForDispatchIdleAsync();
    }

    /// <summary>
    /// Captures the retained post-commit delivery diagnostics in occurrence order.
    /// At most the newest 256 diagnostics are retained; when full, the oldest entry
    /// is discarded. Diagnostic retention failure never changes a committed outcome.
    /// </summary>
    public ImmutableArray<Diagnostic> CaptureDispatchDiagnostics()
    {
        lock (_dispatchDiagnosticsSync)
        {
            return [.. _dispatchDiagnostics];
        }
    }

    private async ValueTask<(
        CommandExecutionResult Result,
        bool StartDispatch,
        DocumentDispatchWorkItem? EnqueuedWorkItem)>
        ExecuteUnderGateAsync(
            Document document,
            ICommand command,
            HistoryStore? historyStore,
            bool recordNormalHistory,
            PreparedHistoryMutation? preparedHistoryMutation,
            CancellationToken cancellationToken)
    {
        var baseState = document.CaptureState();
        var baseSnapshot = baseState.Snapshot;
        cancellationToken.ThrowIfCancellationRequested();

        _handlers.TryGet(command.TypeId, out var handlerRegistration);
        var envelope = _validation.ValidateEnvelope(
            command,
            baseSnapshot,
            handlerRegistration);
        if (!envelope.IsValid)
        {
            return (
                Failure(
                    baseSnapshot.DocumentId,
                    command,
                    CommandExecutionStatus.EnvelopeValidationFailed,
                    baseSnapshot.Revision,
                    envelope.Diagnostics),
                false,
                null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        CommandTransaction transaction;
        try
        {
            transaction = new CommandTransaction(baseState, command.AffectedComponents);
            _executionCheckpointObserver?.Invoke(CommandExecutionCheckpoint.TransactionCreated);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return (
                Failure(
                    baseSnapshot.DocumentId,
                    command,
                    CommandExecutionStatus.InternalFailure,
                    baseSnapshot.Revision,
                    Error(
                        CommandExecutionDiagnosticCodes.TransactionCreationFailure,
                        $"The transaction for Command type '{command.TypeId}' could not be created.",
                        command.TypeId.Value,
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            ExceptionType(exception)))),
                false,
                null);
        }

        CommandValidationResult delegatedValidation;
        try
        {
            delegatedValidation = _validation.ValidateDelegated(
                command,
                transaction.BaseSnapshot,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.Cancelled,
                    transaction.BaseRevision,
                    CancelledDiagnostic(command)),
                false,
                null);
        }

        transaction.AddDiagnostics(delegatedValidation.Diagnostics);
        if (!delegatedValidation.IsValid)
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.DelegatedValidationFailed,
                    transaction.BaseRevision,
                    delegatedValidation.Diagnostics),
                false,
                null);
        }

        transaction.MarkDelegatedValidationComplete();
        cancellationToken.ThrowIfCancellationRequested();

        CommandHandlerResult? handlerResult;
        try
        {
            handlerResult = await handlerRegistration!.Handler.HandleAsync(
                command,
                transaction.BaseSnapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.Cancelled,
                    transaction.BaseRevision,
                    CancelledDiagnostic(command)),
                false,
                null);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.HandlerFailed,
                    transaction.BaseRevision,
                    HandlerExceptionDiagnostic(command, exception)),
                false,
                null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (handlerResult is not null)
        {
            transaction.AddDiagnostics(handlerResult.Diagnostics);
        }

        if (handlerResult is null ||
            !handlerResult.Succeeded ||
            handlerResult.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.HandlerFailed,
                    transaction.BaseRevision,
                    WithStageDiagnostic(
                        handlerResult?.Diagnostics ?? [],
                        Error(
                            CommandExecutionDiagnosticCodes.HandlerFailure,
                            $"The handler for Command type '{command.TypeId}' did not produce a valid proposed state.",
                            command.TypeId.Value))),
                false,
                null);
        }

        transaction.SetProposedSnapshot(handlerResult.ProposedDocument!);
        var proposedStateDiagnostics = ValidateProposedState(transaction);
        transaction.AddDiagnostics(proposedStateDiagnostics);
        if (proposedStateDiagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.ProposedStateValidationFailed,
                    transaction.BaseRevision,
                    proposedStateDiagnostics),
                false,
                null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        DocumentSnapshot committedSnapshot;
        try
        {
            var nextRevision = transaction.BaseRevision.Increment();
            committedSnapshot = DocumentSnapshotCloner.CloneAtRevision(
                transaction.ProposedSnapshot!,
                nextRevision);
        }
        catch (OverflowException exception)
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.InternalFailure,
                    transaction.BaseRevision,
                    Error(
                        CommandExecutionDiagnosticCodes.RevisionPreparationFailure,
                        $"The next revision for Command type '{command.TypeId}' could not be prepared.",
                        command.TypeId.Value,
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            ExceptionType(exception)))),
                false,
                null);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.InternalFailure,
                    transaction.BaseRevision,
                    Error(
                        CommandExecutionDiagnosticCodes.InternalFailure,
                        $"The proposed state for Command type '{command.TypeId}' could not be prepared for commit.",
                        command.TypeId.Value,
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            ExceptionType(exception)))),
                false,
                null);
        }

        var preparedStateDiagnostics = DocumentInvariantValidator.Validate(
            committedSnapshot,
            _connectorAnchorPolicyProvider);
        transaction.AddDiagnostics(preparedStateDiagnostics);
        if (preparedStateDiagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.ProposedStateValidationFailed,
                    transaction.BaseRevision,
                    WithStageDiagnostic(
                        preparedStateDiagnostics,
                        Error(
                            CommandExecutionDiagnosticCodes.ProposedStateInvalid,
                            "The revision-stamped proposed Document state violates a framework invariant.",
                            command.TypeId.Value))),
                false,
                null);
        }

        if (recordNormalHistory && historyStore is not null)
        {
            var historyPreparation = _history.PrepareRecord(
                historyStore,
                command,
                transaction.BaseSnapshot,
                committedSnapshot);
            transaction.AddDiagnostics(historyPreparation.Diagnostics);
            if (!historyPreparation.Succeeded)
            {
                transaction.MarkAborted();
                return (
                    Failure(
                        transaction.DocumentId,
                        command,
                        CommandExecutionStatus.InternalFailure,
                        transaction.BaseRevision,
                        historyPreparation.Diagnostics),
                    false,
                    null);
            }

            preparedHistoryMutation = historyPreparation.Mutation;
        }

        if (preparedHistoryMutation is not null &&
            (historyStore is null || !historyStore.IsCurrent(preparedHistoryMutation)))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.InternalFailure,
                    transaction.BaseRevision,
                    new Diagnostic(
                        HistoryDiagnosticCodes.InvalidPreparation,
                        DiagnosticSeverity.Error,
                        "The prepared History transition no longer matches the current History state.",
                        command.TypeId.Value)),
                false,
                null);
        }

        DocumentChangedEvent committedEvent;
        CommandExecutionResult committedResult;
        DocumentDispatchWorkItem dispatchWorkItem;
        try
        {
            var pipelineInvalidation =
                handlerResult.PipelineInvalidation ??
                CommandPipelineInvalidation.Resolve(command);
            committedEvent = new DocumentChangedEvent(
                transaction.DocumentId,
                transaction.BaseRevision,
                committedSnapshot.Revision,
                transaction.DeclaredAffectedComponents,
                command.TypeId,
                committedSnapshot,
                pipelineInvalidation,
                handlerResult.NodeGeometryImpact);
            committedResult = CommandExecutionResult.CreateCommitted(
                committedEvent,
                transaction.Diagnostics);
            dispatchWorkItem = new DocumentDispatchWorkItem(
                () => DispatchEventAsync(committedEvent),
                exception => ReportSubscriberFailure(command.TypeId.Value, exception));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.InternalFailure,
                    transaction.BaseRevision,
                    Error(
                        CommandExecutionDiagnosticCodes.EventPayloadPreparationFailure,
                        $"The committed event for Command type '{command.TypeId}' could not be prepared.",
                        command.TypeId.Value,
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            ExceptionType(exception)))),
                false,
                null);
        }

        transaction.MarkPrepared();
        _executionCheckpointObserver?.Invoke(CommandExecutionCheckpoint.BeforeFinalCancellationCheck);
        cancellationToken.ThrowIfCancellationRequested();
        _executionCheckpointObserver?.Invoke(CommandExecutionCheckpoint.AfterFinalCancellationCheck);

        var committedState = new DocumentState(committedSnapshot);
        if (!document.TryInstallState(transaction.BaseState, committedState))
        {
            transaction.MarkAborted();
            return (
                Failure(
                    transaction.DocumentId,
                    command,
                    CommandExecutionStatus.InternalFailure,
                    transaction.BaseRevision,
                    Error(
                        CommandExecutionDiagnosticCodes.InternalFailure,
                        "The authoritative Document state changed unexpectedly during commit.",
                        command.TypeId.Value)),
                false,
                null);
        }

        // Cancellation is deliberately ignored from this point through enqueue.
        // The immutable state is already installed and the commit must remain whole.
        if (preparedHistoryMutation is not null)
        {
            historyStore!.InstallPrepared(preparedHistoryMutation);
        }

        var startDispatch = document.ExecutionCoordinator.Enqueue(dispatchWorkItem);
        transaction.MarkCommitted();
        return (committedResult, startDispatch, dispatchWorkItem);
    }

    private ImmutableArray<Diagnostic> ValidateProposedState(
        CommandTransaction transaction)
    {
        var proposedSnapshot = transaction.ProposedSnapshot!;
        var diagnostics = new List<Diagnostic>();

        if (proposedSnapshot.DocumentId != transaction.DocumentId ||
            proposedSnapshot.Revision != transaction.BaseRevision)
        {
            diagnostics.Add(Error(
                CommandExecutionDiagnosticCodes.ProposedStateInvalid,
                "A handler proposal must describe the transaction's Document and base revision.",
                transaction.DocumentId.Value));
        }

        diagnostics.AddRange(DocumentInvariantValidator.Validate(
            proposedSnapshot,
            _connectorAnchorPolicyProvider));

        var actualChanges = GetChangedComponents(
            transaction.BaseSnapshot,
            proposedSnapshot);
        var undeclaredChanges = actualChanges & ~transaction.DeclaredAffectedComponents;
        if (undeclaredChanges != AuthoritativeDocumentComponent.None)
        {
            diagnostics.Add(Error(
                CommandExecutionDiagnosticCodes.AffectedComponentViolation,
                "The handler modified authoritative components outside the Command's declared scope.",
                transaction.DocumentId.Value,
                new KeyValuePair<string, string>("ActualChanges", actualChanges.ToString()),
                new KeyValuePair<string, string>(
                    "DeclaredComponents",
                    transaction.DeclaredAffectedComponents.ToString()),
                new KeyValuePair<string, string>(
                    "UndeclaredChanges",
                    undeclaredChanges.ToString())));
        }

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) &&
            !diagnostics.Any(static diagnostic =>
                diagnostic.Code == CommandExecutionDiagnosticCodes.ProposedStateInvalid ||
                diagnostic.Code == CommandExecutionDiagnosticCodes.AffectedComponentViolation))
        {
            diagnostics.Add(Error(
                CommandExecutionDiagnosticCodes.ProposedStateInvalid,
                "The complete proposed Document state violates a framework invariant.",
                transaction.DocumentId.Value));
        }

        return [.. diagnostics];
    }

    private static AuthoritativeDocumentComponent GetChangedComponents(
        DocumentSnapshot baseline,
        DocumentSnapshot proposed)
    {
        var changed = AuthoritativeDocumentComponent.None;
        if (!baseline.SemanticModel.Equals(proposed.SemanticModel))
        {
            changed |= AuthoritativeDocumentComponent.SemanticModel;
        }

        if (!baseline.VisualModel.Equals(proposed.VisualModel))
        {
            changed |= AuthoritativeDocumentComponent.VisualModel;
        }

        if (!baseline.Metadata.Equals(proposed.Metadata))
        {
            changed |= AuthoritativeDocumentComponent.Metadata;
        }

        if (!Equals(baseline.Publication, proposed.Publication))
        {
            changed |= AuthoritativeDocumentComponent.Publication;
        }

        return changed;
    }

    private async ValueTask DispatchEventAsync(DocumentChangedEvent change)
    {
        foreach (var subscriber in _subscribers)
        {
            try
            {
                await subscriber.OnDocumentChangedAsync(change).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // One external subscriber must not stop the FIFO queue.
            catch (Exception exception) when (IsNonFatal(exception))
            {
                ReportSubscriberFailure(change.CommandTypeId.Value, exception);
            }
#pragma warning restore CA1031
        }
    }

    private void ReportSubscriberFailure(string sourceIdentity, Exception exception)
    {
#pragma warning disable CA1031 // Diagnostic construction and retention cannot affect delivery.
        try
        {
            RetainDispatchDiagnostic(Error(
                CommandExecutionDiagnosticCodes.SubscriberFailure,
                "A Document Changed subscriber failed after the Document commit.",
                sourceIdentity,
                [
                    new KeyValuePair<string, string>(
                        "ExceptionMessage",
                        exception.Message),
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        ExceptionType(exception)),
                ]));
        }
        catch (Exception)
        {
            // A lost operational diagnostic must not skip another subscriber or event.
        }
#pragma warning restore CA1031
    }

    private void ReportDispatchSchedulingFailure(
        string sourceIdentity,
        Exception? exception)
    {
#pragma warning disable CA1031 // Diagnostic construction and retention cannot affect a commit.
        try
        {
            var context = exception is null
                ? Array.Empty<KeyValuePair<string, string>>()
                :
                [
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        ExceptionType(exception)),
                ];

            RetainDispatchDiagnostic(Error(
                CommandExecutionDiagnosticCodes.EventDispatchSchedulingFailure,
                "Document Changed event dispatch could not be scheduled; the FIFO remains queued for retry.",
                sourceIdentity,
                context));
        }
        catch (Exception)
        {
            // Scheduling remains post-commit even if its diagnostic cannot be retained.
        }
#pragma warning restore CA1031
    }

    private void RetainDispatchDiagnostic(Diagnostic diagnostic)
    {
#pragma warning disable CA1031 // Diagnostics are non-transactional and must never affect a commit.
        try
        {
            lock (_dispatchDiagnosticsSync)
            {
                if (_dispatchDiagnostics.Count == DispatchDiagnosticRetentionLimit)
                {
                    _ = _dispatchDiagnostics.Dequeue();
                }

                _dispatchDiagnostics.Enqueue(diagnostic);
            }
        }
        catch (Exception)
        {
            // A diagnostic may be lost under resource failure, but committed state and
            // the queued event remain final and available for a later dispatch retry.
        }
#pragma warning restore CA1031
    }

    private static IEnumerable<CommandHandlerRegistration> CreateHandlerRegistrations(
        IEnumerable<CommandHandlerRegistration>? handlers,
        IElementConnectorAnchorPolicyProvider connectorAnchorPolicyProvider)
    {
        yield return new CommandHandlerRegistration(
            MoveVisualStateCommand.KnownTypeId,
            new MoveVisualStateCommandEnvelopeValidator(),
            new MoveVisualStateCommandHandler());

        yield return new CommandHandlerRegistration(
            MoveVisualStatesCommand.KnownTypeId,
            new MoveVisualStatesCommandEnvelopeValidator(),
            new MoveVisualStatesCommandHandler());

        yield return new CommandHandlerRegistration(
            ResizeVisualStateCommand.KnownTypeId,
            new ResizeVisualStateCommandEnvelopeValidator(),
            new ResizeVisualStateCommandHandler());

        yield return new CommandHandlerRegistration(
            UpdateBoundaryAttachmentCommand.KnownTypeId,
            new UpdateBoundaryAttachmentCommandEnvelopeValidator(),
            new UpdateBoundaryAttachmentCommandHandler());

        yield return new CommandHandlerRegistration(
            UpdateConnectionRouteCommand.KnownTypeId,
            new UpdateConnectionRouteCommandEnvelopeValidator(),
            new UpdateConnectionRouteCommandHandler());

        yield return new CommandHandlerRegistration(
            MoveLabelCommand.KnownTypeId,
            new MoveLabelCommandEnvelopeValidator(),
            new MoveLabelCommandHandler());

        yield return new CommandHandlerRegistration(
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            new UpdateNodeLabelVisualOverrideCommandEnvelopeValidator(),
            new UpdateNodeLabelVisualOverrideCommandHandler());

        yield return new CommandHandlerRegistration(
            AddConnectorAnchorCommand.KnownTypeId,
            new AddConnectorAnchorCommandEnvelopeValidator(),
            new AddConnectorAnchorCommandHandler(connectorAnchorPolicyProvider));

        yield return new CommandHandlerRegistration(
            RemoveConnectorAnchorCommand.KnownTypeId,
            new RemoveConnectorAnchorCommandEnvelopeValidator(),
            new RemoveConnectorAnchorCommandHandler(connectorAnchorPolicyProvider));

        yield return new CommandHandlerRegistration(
            UpdateSemanticElementNameCommand.KnownTypeId,
            new UpdateSemanticElementNameCommandEnvelopeValidator(),
            new UpdateSemanticElementNameCommandHandler());

        yield return new CommandHandlerRegistration(
            UpdateSemanticElementPropertyCommand.KnownTypeId,
            new UpdateSemanticElementPropertyCommandEnvelopeValidator(),
            new UpdateSemanticElementPropertyCommandHandler());

        yield return new CommandHandlerRegistration(
            SetModelProfileAvailabilityCommand.KnownTypeId,
            new SetModelProfileAvailabilityCommandEnvelopeValidator(),
            new SetModelProfileAvailabilityCommandHandler());

        yield return new CommandHandlerRegistration(
            UpdateDocumentPublicationCommand.KnownTypeId,
            new UpdateDocumentPublicationCommandEnvelopeValidator(),
            new UpdateDocumentPublicationCommandHandler());

        yield return new CommandHandlerRegistration(
            CreateTopLevelDocumentScopeCommand.KnownTypeId,
            new TopLevelDocumentScopeCommandEnvelopeValidator(),
            new CreateTopLevelDocumentScopeCommandHandler());

        yield return new CommandHandlerRegistration(
            RestoreTopLevelDocumentScopeCommand.KnownTypeId,
            new TopLevelDocumentScopeCommandEnvelopeValidator(),
            new RestoreTopLevelDocumentScopeCommandHandler());

        if (handlers is null)
        {
            yield break;
        }

        foreach (var registration in handlers)
        {
            yield return registration;
        }
    }

    private static IEnumerable<CommandHistoryPolicyRegistration>
        CreateHistoryPolicyRegistrations(
            IEnumerable<CommandHistoryPolicyRegistration>? historyPolicies)
    {
        yield return MoveVisualStateHistoryPolicy.Registration;
        yield return MoveVisualStatesHistoryPolicy.Registration;
        yield return ResizeVisualStateHistoryPolicy.Registration;
        yield return UpdateBoundaryAttachmentHistoryPolicy.Registration;
        yield return UpdateConnectionRouteHistoryPolicy.Registration;
        yield return MoveLabelHistoryPolicy.Registration;
        yield return UpdateNodeLabelVisualOverrideHistoryPolicy.Registration;
        yield return AddConnectorAnchorHistoryPolicy.Registration;
        yield return RemoveConnectorAnchorHistoryPolicy.Registration;
        yield return UpdateSemanticElementNameHistoryPolicy.Registration;
        yield return UpdateSemanticElementPropertyHistoryPolicy.Registration;
        yield return SetModelProfileAvailabilityHistoryPolicy.Registration;
        yield return UpdateDocumentPublicationHistoryPolicy.Registration;
        yield return CreateTopLevelDocumentScopeHistoryPolicy.Registration;
        yield return CompoundDocumentHistoryPolicy.Registration;

        if (historyPolicies is null)
        {
            yield break;
        }

        foreach (var registration in historyPolicies)
        {
            yield return registration;
        }
    }

    private static CommandValidationService CreateValidationService(
        IEnumerable<CommandValidatorRegistration>? validators)
    {
        var creation = CommandValidationService.Create(validators ?? []);
        if (creation.Succeeded)
        {
            return creation.Service!;
        }

        var codes = string.Join(
            ", ",
            creation.Diagnostics.Select(static diagnostic => diagnostic.Code));
        throw new ArgumentException(
            $"Command validator registration failed: {codes}.",
            nameof(validators));
    }

    private static IEnumerable<CommandValidatorRegistration>
        CreateValidatorRegistrations(
            IEnumerable<CommandValidatorRegistration>? validators)
    {
        yield return UpdateBoundaryAttachmentCommandValidator.Registration;
        yield return UpdateDocumentPublicationCommandValidator.Registration;

        if (validators is null)
        {
            yield break;
        }

        foreach (var registration in validators)
        {
            yield return registration;
        }
    }

    private static ImmutableArray<IDocumentChangedSubscriber> CopySubscribers(
        IEnumerable<IDocumentChangedSubscriber>? subscribers)
    {
        if (subscribers is null)
        {
            return [];
        }

        var copy = subscribers.ToArray();
        if (Array.Exists(copy, static subscriber => subscriber is null))
        {
            throw new ArgumentException(
                "Document Changed subscribers cannot contain null values.",
                nameof(subscribers));
        }

        return [.. copy];
    }

    private static CommandExecutionResult Failure(
        Inceptus.DocumentEngine.Contracts.Primitives.DocumentId documentId,
        ICommand command,
        CommandExecutionStatus status,
        Inceptus.DocumentEngine.Contracts.Primitives.DocumentRevision previousRevision,
        Diagnostic diagnostic) =>
        Failure(documentId, command, status, previousRevision, [diagnostic]);

    private static CommandExecutionResult Failure(
        Inceptus.DocumentEngine.Contracts.Primitives.DocumentId documentId,
        ICommand command,
        CommandExecutionStatus status,
        Inceptus.DocumentEngine.Contracts.Primitives.DocumentRevision previousRevision,
        IEnumerable<Diagnostic> diagnostics) =>
        CommandExecutionResult.CreateFailure(
            documentId,
            command.TypeId,
            status,
            previousRevision,
            command.AffectedComponents,
            diagnostics);

    private static Diagnostic CancelledDiagnostic(ICommand command) =>
        Error(
            CommandExecutionDiagnosticCodes.Cancelled,
            $"Command type '{command.TypeId}' was cancelled before atomic installation.",
            command.TypeId.Value);

    private static Diagnostic HandlerExceptionDiagnostic(
        ICommand command,
        Exception exception) =>
        Error(
            CommandExecutionDiagnosticCodes.HandlerFailure,
            $"The handler for Command type '{command.TypeId}' failed.",
            command.TypeId.Value,
            new KeyValuePair<string, string>("ExceptionType", ExceptionType(exception)));

    private static Diagnostic UnexpectedFailureDiagnostic(
        ICommand command,
        Exception exception) =>
        Error(
            CommandExecutionDiagnosticCodes.InternalFailure,
            $"Command type '{command.TypeId}' failed before commit.",
            command.TypeId.Value,
            new KeyValuePair<string, string>("ExceptionType", ExceptionType(exception)));

    private static IEnumerable<Diagnostic> WithStageDiagnostic(
        IEnumerable<Diagnostic> diagnostics,
        Diagnostic stageDiagnostic) =>
        diagnostics.Append(stageDiagnostic);

    private static string ExceptionType(Exception exception) =>
        exception.GetType().FullName ?? exception.GetType().Name;

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);
}
