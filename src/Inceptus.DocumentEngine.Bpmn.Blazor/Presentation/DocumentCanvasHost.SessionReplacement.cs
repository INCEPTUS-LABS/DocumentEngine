using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    /// <summary>
    /// Opens a candidate Document on the inactive canvas and performs the single atomic
    /// active-session switch shared by native Import, snapshot Load, and New diagram. The caller owns the host
    /// gate for the complete operation.
    /// </summary>
    private async ValueTask<DocumentSessionReplacementResult> ReplaceDocumentUnderGateAsync(
        Document candidateDocument,
        DocumentSessionReplacementMessages messages,
        CancellationToken cancellationToken,
        BpmnModelerNativeImportOptions? nativeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(candidateDocument);
        ArgumentNullException.ThrowIfNull(messages);

        Canvas2DRenderer? candidateRenderer = null;
        EditingSession? candidateSession = null;
        Canvas2DInteractionController? candidateController = null;
        ICanvasPresentationPointerObserver? candidatePointerObserver = null;
        BpmnModelerDocumentNotificationSource? candidateNotifications = null;
        var recovered = false;
        ImmutableArray<Diagnostic> preparationDiagnostics = [];
        try
        {
            EditingSession? oldSession;
            Canvas2DSurfaceSize? surfaceSize;
            EditingSessionConfiguration? sourceConfiguration;
            string? activeCanvasElementId;
            string? standbyCanvasElementId;
            Func<Canvas2DRenderer>? rendererFactory;
            lock (_sync)
            {
                oldSession = _session;
                surfaceSize = _surfaceSize;
                sourceConfiguration = _nativeDocumentSessionConfiguration;
                activeCanvasElementId = _nativeDocumentCanvasElementId;
                standbyCanvasElementId = _nativeDocumentStandbyCanvasElementId;
                rendererFactory = _replacementRendererFactory;
                if (_disposed || !_initialized || oldSession is null ||
                    surfaceSize is null || sourceConfiguration is null ||
                    string.IsNullOrWhiteSpace(activeCanvasElementId) ||
                    string.IsNullOrWhiteSpace(standbyCanvasElementId) ||
                    rendererFactory is null ||
                    _propertiesFormOpen || _modelViewPropertiesFormOpen)
                {
                    return DocumentSessionReplacementResult.Unavailable(
                        Error(messages.UnavailableCode, messages.UnavailableMessage));
                }
            }

            var oldState = oldSession.CaptureState();
            if (oldState.Status != EditingSessionStatus.Ready ||
                oldState.EditorState.ActiveGesture is not null)
            {
                return DocumentSessionReplacementResult.Unavailable(
                    Error(messages.UnavailableCode, messages.UnavailableMessage));
            }

            var oldController = _interactionController;
            var oldPointerObserver = _pointerObserver;

#pragma warning disable CA1031 // Replacement construction failures become bounded diagnostics.
            try
            {
                candidateRenderer = rendererFactory();
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return FailDocumentReplacement(
                    messages,
                    messages.RendererCreationMessage,
                    exception);
            }
#pragma warning restore CA1031

            var initialization = await candidateRenderer.InitializeAsync(
                standbyCanvasElementId,
                surfaceSize.Value,
                cancellationToken).ConfigureAwait(false);
            if (!initialization.Succeeded)
            {
                await DisposeDocumentSessionResourcesAsync(
                    pointerObserver: null,
                    controller: null,
                    session: null,
                    renderer: candidateRenderer).ConfigureAwait(false);
                candidateRenderer = null;
                return FailDocumentReplacement(messages, messages.CanvasInitializationMessage);
            }

            if (nativeOptions is not null)
            {
                var preparer = new ConnectorRoutingStatePreparer(sourceConfiguration, candidateRenderer);
                var candidateSnapshot = candidateDocument.CaptureSnapshot();
                var prepared = await preparer.PrepareAsync(new ConnectorRoutingStatePreparationRequest(
                    candidateSnapshot, candidateSnapshot, [], [], null, false,
                    ConnectorRoutingPreparationPurpose.ValidateSavedState), cancellationToken).ConfigureAwait(false);
                if (!prepared.Succeeded && nativeOptions.ReprepareIncompatibleV2 &&
                    prepared.Diagnostics.Any(static diagnostic => diagnostic.Code == "INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE"))
                {
                    prepared = await preparer.PrepareAsync(new ConnectorRoutingStatePreparationRequest(
                        candidateSnapshot, candidateSnapshot, [], [], null, false,
                        ConnectorRoutingPreparationPurpose.RecoverSavedState), cancellationToken).ConfigureAwait(false);
                    recovered = prepared.Succeeded;
                }
                if (!prepared.Succeeded)
                {
                    await candidateRenderer.DisposeAsync().ConfigureAwait(false);
                    candidateRenderer = null;
                    if (prepared.Diagnostics.Any(static diagnostic => diagnostic.Code == "INCEPTUS.ROUTING.PREPARATION.UNAVAILABLE"))
                        return FailDocumentReplacement(messages, messages.SessionReadyMessage) with { Diagnostics = prepared.Diagnostics };
                    return DocumentSessionReplacementResult.Rejected(prepared.Diagnostics);
                }
                preparationDiagnostics = prepared.Diagnostics;
                if (recovered)
                {
                    var visual = candidateSnapshot.VisualModel;
                    var reconstructed = DocumentReconstructor.Reconstruct(new DocumentSnapshot(candidateSnapshot.SemanticModel,
                        new VisualModelSnapshot(visual.DocumentId, visual.Revision, visual.VisualStates,
                            visual.ProfileElementPresentations, prepared.RoutingScopes), candidateSnapshot.Metadata, candidateSnapshot.Publication),
                        sourceConfiguration.ConnectorAnchorPolicyProvider);
                    if (!reconstructed.Succeeded)
                    {
                        await candidateRenderer.DisposeAsync().ConfigureAwait(false);
                        candidateRenderer = null;
                        return DocumentSessionReplacementResult.Rejected(reconstructed.Diagnostics);
                    }
                    candidateDocument = reconstructed.Document!;
                }
            }

            candidateNotifications = ModelerNotifications is null ? null :
                new BpmnModelerDocumentNotificationSource(candidateDocument.CaptureSnapshot());
            var configuration = WithVisibleDocumentRegion(
                sourceConfiguration,
                surfaceSize.Value,
                candidateNotifications);
            var attachment = await EditingSession.AttachAsync(
                candidateDocument,
                candidateRenderer,
                configuration,
                cancellationToken).ConfigureAwait(false);
            // AttachAsync accepts ownership of the initialized renderer for every outcome.
            candidateRenderer = null;
            if (attachment.Status != EditingSessionAttachStatus.Ready)
            {
                if (attachment.Session is not null)
                {
                    await attachment.Session.DisposeAsync().ConfigureAwait(false);
                }

                return FailDocumentReplacement(messages, messages.SessionReadyMessage);
            }

            candidateSession = attachment.Session!;
            var candidateState = candidateSession.CaptureState();
            var attachedSnapshot = await candidateSession.CaptureDocumentSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (attachedSnapshot is not null)
                candidateNotifications?.AcceptPreparedInitialSnapshot(attachedSnapshot);
            if (HasError(candidateState.PresentationDiagnostics))
            {
                await candidateSession.DisposeAsync().ConfigureAwait(false);
                candidateSession = null;
                return FailDocumentReplacement(messages, messages.SessionReadyMessage);
            }

            candidateController = new Canvas2DInteractionController(
                candidateSession,
                connectionCreationCatalog: _anchorConnectionCreationCatalog,
                creationIdentityProvider: _documentCreationIdentityProvider,
                endpointReconnectionCatalog: _endpointReconnectionCatalog,
                spatialEditPlanners: _spatialEditPlanners);
            var candidateToolboxController = new ToolboxPlacementController(
                _toolboxPlacementCatalog,
                _toolboxSelection,
                _documentCreationIdentityProvider,
                _spatialEditPlanners);
            if (_pointerObserverFactory is not null)
            {
                candidatePointerObserver = await _pointerObserverFactory.CreateAsync(
                    standbyCanvasElementId,
                    OnPointerInputAsync,
                    OnWheelInputAsync,
                    cancellationToken).ConfigureAwait(false);
                await candidatePointerObserver.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || !ReferenceEquals(_session, oldSession))
                {
                    throw new InvalidOperationException(
                        "The editor changed before the candidate Document could be attached.");
                }

                long nextPointerInputVersion;
                int nextSuccessfulRenderCount;
                checked
                {
                    nextPointerInputVersion = _pointerInputVersion + 1L;
                    nextSuccessfulRenderCount = _successfulRenderCount + 1;
                    _documentSessionVersion++;
                }

                candidateSession.StateChanged += HandleSessionStateChanged;
                _documentNotifications?.Dispose();
                _documentNotifications = candidateNotifications;
                candidateNotifications = null;
                _replacementNotificationPending = true;
                _session = candidateSession;
                ResetNativeSaveAttachmentUnderLock();
                _interactionController = candidateController;
                _toolboxPlacementController = candidateToolboxController;
                _pointerObserver = candidatePointerObserver;
                _nativeDocumentCanvasElementId = standbyCanvasElementId;
                _nativeDocumentStandbyCanvasElementId = activeCanvasElementId;
                _toolboxSelection.Clear();
                _observedActiveGestureId = null;
                _observedActiveGestureCaptureGeneration = 0;
                _observedDocumentRevision = candidateState.DocumentRevision;
                _cssCursor = "default";
                _validationSnapshot = null;
                _validationInFlight = false;
                _hostDiagnostics = [];
                _ = ReplaceInteractionDiagnosticsUnderLock([]);
                _activePointerInputKind = null;
                _activePointerId = null;
                _activePointerCaptureGeneration = 0;
                _consumedPlacementPointerId = null;
                _viewportPanGesture = null;
                _successfulPresentationGeneration = candidateState.Generation;
                _surfaceRenderPending = false;
                _contextMenu = null;
                _propertiesTargetVisualStateId = null;
                _propertiesTargetSemanticElementId = null;
                _propertiesTargetPresentationId = null;
                _propertiesFormOpen = false;
                _propertiesFormDirty = false;
                _propertiesApplyInFlight = false;
                ClearModelViewPropertiesFormUnderLock();
                _initialized = true;
                _latestPresentationSucceeded =
                    !HasError(candidateState.PresentationDiagnostics);
                _pointerInputVersion = nextPointerInputVersion;
                _successfulRenderCount = nextSuccessfulRenderCount;
            }
            oldSession.StateChanged -= HandleSessionStateChanged;

            candidateSession = null;
            candidateController = null;
            candidatePointerObserver = null;
            _ = await DisposeDocumentSessionResourcesAsync(
                oldPointerObserver,
                oldController,
                oldSession,
                renderer: null).ConfigureAwait(false);
            return DocumentSessionReplacementResult.Success with
            {
                Snapshot = attachedSnapshot,
                Recovered = recovered,
                Diagnostics = preparationDiagnostics,
            };
        }
#pragma warning disable CA1031 // Unexpected replacement failures become bounded diagnostics.
        catch (Exception exception) when (
            IsNonFatal(exception) && !cancellationToken.IsCancellationRequested)
        {
            await DisposeDocumentSessionResourcesAsync(
                candidatePointerObserver,
                candidateController,
                candidateSession,
                candidateRenderer).ConfigureAwait(false);
            return FailDocumentReplacement(
                messages,
                messages.UnexpectedFailureMessage,
                exception);
        }
#pragma warning restore CA1031
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeDocumentSessionResourcesAsync(
                candidatePointerObserver,
                candidateController,
                candidateSession,
                candidateRenderer).ConfigureAwait(false);
            throw;
        }
        finally
        {
            candidateNotifications?.Dispose();
        }
    }

    private DocumentSessionReplacementResult FailDocumentReplacement(
        DocumentSessionReplacementMessages messages,
        string message,
        Exception? exception = null)
    {
        var diagnostic = Error(
            messages.FailedCode,
            message,
            exception?.GetType().FullName ?? exception?.GetType().Name);
        lock (_sync)
        {
            _hostDiagnostics = [diagnostic];
        }

        return DocumentSessionReplacementResult.Failure(diagnostic);
    }

    private async ValueTask<Exception?> DisposeDocumentSessionResourcesAsync(
        ICanvasPresentationPointerObserver? pointerObserver,
        Canvas2DInteractionController? controller,
        EditingSession? session,
        Canvas2DRenderer? renderer)
    {
        Exception? firstFailure = null;
#pragma warning disable CA1031 // Replacement cleanup is best effort and reports its first failure.
        if (pointerObserver is not null)
        {
            try
            {
                await pointerObserver.SetCursorAsync("default", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                firstFailure ??= exception;
            }

            try
            {
                await pointerObserver.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                firstFailure ??= exception;
            }
        }

        if (controller is not null)
        {
            try
            {
                await controller.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                firstFailure ??= exception;
            }
        }

        if (session is not null)
        {
            session.StateChanged -= HandleSessionStateChanged;
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                firstFailure ??= exception;
            }
        }
        else if (renderer is not null)
        {
            try
            {
                await renderer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                firstFailure ??= exception;
            }
        }
#pragma warning restore CA1031
        return firstFailure;
    }
}

internal sealed record DocumentSessionReplacementMessages(
    string UnavailableCode,
    string UnavailableMessage,
    string FailedCode,
    string RendererCreationMessage,
    string CanvasInitializationMessage,
    string SessionReadyMessage,
    string UnexpectedFailureMessage);

internal enum DocumentSessionReplacementStatus
{
    Succeeded = 0,
    Unavailable = 1,
    Failed = 2,
    Rejected = 3,
}

internal sealed record DocumentSessionReplacementResult(
    DocumentSessionReplacementStatus Status,
    Diagnostic? Diagnostic)
{
    internal DocumentSnapshot? Snapshot { get; init; }
    internal bool Recovered { get; init; }
    internal ImmutableArray<Diagnostic> Diagnostics { get; init; } = [];

    internal bool Succeeded => Status == DocumentSessionReplacementStatus.Succeeded;

    internal bool ShouldNotify => Status != DocumentSessionReplacementStatus.Unavailable;

    internal static DocumentSessionReplacementResult Success { get; } =
        new(DocumentSessionReplacementStatus.Succeeded, null);

    internal static DocumentSessionReplacementResult Unavailable(Diagnostic diagnostic) =>
        new(DocumentSessionReplacementStatus.Unavailable, diagnostic);

    internal static DocumentSessionReplacementResult Failure(Diagnostic diagnostic) =>
        new(DocumentSessionReplacementStatus.Failed, diagnostic);

    internal static DocumentSessionReplacementResult Rejected(ImmutableArray<Diagnostic> diagnostics) =>
        new(DocumentSessionReplacementStatus.Rejected, diagnostics.FirstOrDefault()) { Diagnostics = diagnostics };
}
