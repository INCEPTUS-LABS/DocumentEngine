using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private BpmnModelerDocumentNotificationSource? _documentNotifications;
    private bool _initialNotificationSent;
    private bool _replacementNotificationPending;

    // Set by the owning root before initialization; never a service-provider singleton.
    internal BpmnModelerNotifications? ModelerNotifications { get; set; }

    internal BpmnModelerDocumentResult CaptureDocumentSnapshot()
    {
        lock (_sync)
        {
            if (!_disposed && _initialized && _session is { } session &&
                session.CaptureState().Status == EditingSessionStatus.Ready &&
                session.TryCaptureDocumentSnapshot(out var snapshot))
            {
                return new(BpmnModelerOperationStatus.Succeeded, snapshot, []);
            }
        }

        return new(BpmnModelerOperationStatus.Unavailable, null,
            [Error("BPMN_MODELER_CAPTURE_UNAVAILABLE", "The modeler is not ready to capture a Document.")]);
    }

    private async ValueTask EnterModelerOperationAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ModelerNotifications is { } notifications)
            {
                // A preceding snapshot must become visible before a later operation can
                // change the authoritative Document or retire its session generation.
                await notifications.Pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    private async ValueTask CompleteModelerOperationAsync()
    {
        Task delivery = Task.CompletedTask;
        try
        {
            await CollectModelerNotificationsUnderGateAsync().ConfigureAwait(false);
            delivery = ModelerNotifications?.Pending ?? Task.CompletedTask;
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            // Disposal suppresses all further callbacks, including committed work in flight.
        }
        finally
        {
            _gate.Release();
        }

        // Never wait for callback invocation while holding the host gate or Document dispatcher.
        try
        {
            await delivery.WaitAsync(_lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            // A disposed renderer need not start callbacks which have already been suppressed.
        }
    }

    private async ValueTask CollectModelerNotificationsUnderGateAsync()
    {
        EditingSession? session;
        BpmnModelerDocumentNotificationSource? source;
        lock (_sync)
        {
            if (_disposed || ModelerNotifications is null)
            {
                return;
            }

            session = _session;
            source = _documentNotifications;
        }

        if (session is not null && source is not null)
        {
            // The command result precedes asynchronous subscriber dispatch. In particular,
            // metadata-only changes need this receipt barrier as well as pipeline idleness.
            await session.WaitForIdleAsync(_lifetimeToken).ConfigureAwait(false);
            if (session.TryCaptureDocumentSnapshot(out var snapshot))
            {
                await source.WaitForReceiptAsync(snapshot.Revision, _lifetimeToken)
                    .ConfigureAwait(false);
                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(session, _session) ||
                        !ReferenceEquals(source, _documentNotifications))
                    {
                        return;
                    }

                    if (!_initialNotificationSent && _initialized &&
                        _latestPresentationSucceeded &&
                        session.CaptureState().Status == EditingSessionStatus.Ready)
                    {
                        _initialNotificationSent = true;
                        _ = ModelerNotifications!.Enqueue(new BpmnModelerReadyEventArgs(snapshot));
                    }

                    if (_replacementNotificationPending)
                    {
                        _replacementNotificationPending = false;
                        _ = ModelerNotifications!.Enqueue(new BpmnModelerDocumentChangedEventArgs(
                            source.InitialSnapshot, BpmnModelerDocumentChangeKind.DocumentReplacement));
                    }

                    foreach (var committed in source.TakePendingThrough(snapshot.Revision))
                    {
                        // A renderer failure cannot roll back an already committed revision.
                        // Preserve that accepted snapshot rather than silently losing the edit.
                        _ = ModelerNotifications!.Enqueue(new BpmnModelerDocumentChangedEventArgs(
                            committed, BpmnModelerDocumentChangeKind.PersistentMutation));
                    }
                }
            }
        }

        lock (_sync)
        {
            if (!_disposed && _initializationAttempted && !_initialNotificationSent)
            {
                _initialNotificationSent = true;
                _ = ModelerNotifications!.Enqueue(new BpmnModelerOperationFailedEventArgs(
                    BpmnModelerOperation.Startup, BpmnModelerOperationStatus.Failed,
                    [new Diagnostic("BPMN_MODELER_STARTUP_FAILED", DiagnosticSeverity.Error,
                        "The modeler could not initialize its Document and canvas presentation.")]));
            }
        }
    }
}
