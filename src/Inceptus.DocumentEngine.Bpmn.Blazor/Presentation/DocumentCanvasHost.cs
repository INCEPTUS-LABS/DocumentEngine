using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Validation;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Component-scoped ownership of the physical canvas surface and its Editing Session attachment.
/// Runtime currency and renderer ownership belong to EditingSession after attachment. Native
/// Document replacement prepares against the inactive host canvas before atomically swapping the
/// active session.
/// </summary>
internal sealed partial class DocumentCanvasHost : IAsyncDisposable
{
    private const string ModelerFontFamily = "DejaVu Sans";
    private const string ModelerFontIdentity = "org.dejavu.DejaVuSans";
    private const string ModelerFontVersion = "2.37";
    private const string ModelerFontUri =
        "_content/Inceptus.DocumentEngine.Bpmn.Blazor/fonts/DejaVuSans-2.37.ttf";
    private const string HostInitializationFailed = "CANVAS_HOST_INITIALIZATION_FAILED";
    private const string HostResizeFailed = "CANVAS_HOST_RESIZE_FAILED";
    private const string HostPointerCaptureReleaseFailed =
        "CANVAS_HOST_POINTER_CAPTURE_RELEASE_FAILED";
    private const string HostPointerCursorFailed = "CANVAS_HOST_POINTER_CURSOR_FAILED";
    private const string HostPointerCoordinateInvalid =
        "CANVAS_HOST_POINTER_COORDINATE_INVALID";
    private const int MiddleMouseButton = 1;
    private const int MiddleMouseButtonsMask = 4;
    private const string PropertiesApplyUnavailable =
        "CANVAS_PROPERTIES_APPLY_UNAVAILABLE";
    private const string PropertiesApplyStale = "CANVAS_PROPERTIES_APPLY_STALE";
    private const string ModelViewPropertiesApplyUnavailable =
        "CANVAS_MODEL_VIEW_PROPERTIES_APPLY_UNAVAILABLE";
    private const string ModelViewPropertiesApplyStale =
        "CANVAS_MODEL_VIEW_PROPERTIES_APPLY_STALE";
    private const string HostBackgroundActionFailed =
        "CANVAS_BACKGROUND_ACTION_FAILED";
    private const string HostSemanticSceneViewActionFailed =
        "CANVAS_SEMANTIC_SCENE_VIEW_ACTION_FAILED";
    private const string HostSemanticSceneCommandActionFailed =
        "CANVAS_SEMANTIC_SCENE_COMMAND_ACTION_FAILED";
    private const string HostModelValidationFailed = "CANVAS_MODEL_VALIDATION_FAILED";
    private const string HostDisposalFailed = "CANVAS_HOST_DISPOSAL_FAILED";

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly ICanvasPresentationSurfaceObserverFactory _observerFactory;
    private readonly ICanvasPresentationPointerObserverFactory? _pointerObserverFactory;
    private readonly IDocumentCanvasCompositionFactory _compositionFactory;
    private readonly ToolboxSelectionState _toolboxSelection;
    private readonly Func<Canvas2DRenderer>? _replacementRendererFactory;
    private Canvas2DRenderer? _unattachedRenderer;
    private ICanvasPresentationSurfaceObserver? _observer;
    private ICanvasPresentationPointerObserver? _pointerObserver;
    private Canvas2DInteractionController? _interactionController;
    private ToolboxPlacementController? _toolboxPlacementController;
    private string? _observedActiveGestureId;
    private long _observedActiveGestureCaptureGeneration;
    private DocumentRevision? _observedDocumentRevision;
    private string _cssCursor = "default";
    private EditingSession? _session;
    private long _documentSessionVersion;
    private ElementPropertiesSchemaCatalog? _propertiesSchemaCatalog;
    private ToolboxPlacementCatalog _toolboxPlacementCatalog = ToolboxPlacementCatalog.Empty;
    private AnchorConnectionCreationCatalog _anchorConnectionCreationCatalog =
        AnchorConnectionCreationCatalog.Empty;
    private ConnectorEndpointReconnectionCatalog _endpointReconnectionCatalog =
        ConnectorEndpointReconnectionCatalog.Empty;
    private Canvas2DSpatialEditPlannerCatalog _spatialEditPlanners =
        Canvas2DSpatialEditPlannerCatalog.Empty;
    private DiagramDeletionCatalog _deletionCatalog = DiagramDeletionCatalog.Empty;
    private ScopeNavigationCatalog _scopeNavigationCatalog = ScopeNavigationCatalog.Empty;
    private CanvasBackgroundActionCatalog _backgroundActionCatalog =
        CanvasBackgroundActionCatalog.Empty;
    private SemanticSceneViewActionCatalog _semanticSceneViewActionCatalog =
        SemanticSceneViewActionCatalog.Empty;
    private SemanticSceneCommandActionCatalog _semanticSceneCommandActionCatalog =
        SemanticSceneCommandActionCatalog.Empty;
    private IDocumentCreationIdentityProvider _documentCreationIdentityProvider =
        GuidDocumentCreationIdentityProvider.Instance;
    private ModelValidationEngine _modelValidationEngine = new();
    private ValidationSnapshot? _validationSnapshot;
    private bool _validationInFlight;
    private IDocumentCanvasPipelineCounters? _pipelineCounters;
    private Canvas2DSurfaceSize? _surfaceSize;
    private ImmutableArray<Diagnostic> _hostDiagnostics = [];
    private ImmutableArray<Diagnostic> _interactionDiagnostics = [];
    private (EditingSession Session, DocumentId DocumentId, DocumentRevision Revision,
        DocumentScopeId ScopeId)? _contextCommandDiagnosticOwner;
    private bool _initializationAttempted;
    private bool _initialized;
    private bool _disposed;
    private bool _latestPresentationSucceeded;
    private int _successfulRenderCount;
    private long _surfaceRequestVersion;
    private long _pointerInputVersion;
    private CanvasPointerEventKind? _activePointerInputKind;
    private long? _activePointerId;
    private long _activePointerCaptureGeneration;
    private long? _consumedPlacementPointerId;
    private ViewportPanGesture? _viewportPanGesture;
    private EditingSessionGeneration? _successfulPresentationGeneration;
    private bool _surfaceRenderPending;
    private DocumentCanvasContextMenuState? _contextMenu;
    private VisualStateId? _propertiesTargetVisualStateId;
    private SemanticElementId? _propertiesTargetSemanticElementId;
    private Canvas2DSpatialRegionId? _propertiesTargetPresentationId;
    private bool _propertiesFormOpen;
    private bool _propertiesFormDirty;
    private bool _propertiesApplyInFlight;
    private bool _modelViewPropertiesFormOpen;
    private bool _modelViewPropertiesFormDirty;
    private bool _modelViewPropertiesApplyInFlight;
    private DocumentScopeId? _modelViewPropertiesScopeId;
    private Task? _disposeTask;

    internal DocumentCanvasHost(
        IJSRuntime jsRuntime,
        IDocumentCanvasCompositionFactory compositionFactory,
        ToolboxSelectionState? toolboxSelection = null)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentNullException.ThrowIfNull(compositionFactory);
        _lifetimeToken = _lifetime.Token;
        _unattachedRenderer = new Canvas2DRenderer(jsRuntime, CreateRendererConfiguration());
        _observerFactory = new CanvasPresentationSurfaceObserverFactory(jsRuntime);
        _pointerObserverFactory = new CanvasPresentationPointerObserverFactory(jsRuntime);
        _compositionFactory = compositionFactory;
        _toolboxSelection = toolboxSelection ?? new ToolboxSelectionState();
        _replacementRendererFactory = () => new Canvas2DRenderer(
            jsRuntime,
            CreateRendererConfiguration());
    }

    internal DocumentCanvasHost(
        IDocumentCanvasCompositionFactory compositionFactory,
        Canvas2DRenderer renderer,
        ICanvasPresentationSurfaceObserverFactory observerFactory,
        ICanvasPresentationPointerObserverFactory? pointerObserverFactory = null,
        ToolboxSelectionState? toolboxSelection = null,
        Func<Canvas2DRenderer>? replacementRendererFactory = null,
        PublishedProcessPackageBuilder? publishPackageBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(compositionFactory);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(observerFactory);
        _lifetimeToken = _lifetime.Token;
        _unattachedRenderer = renderer;
        _observerFactory = observerFactory;
        _pointerObserverFactory = pointerObserverFactory;
        _compositionFactory = compositionFactory;
        _toolboxSelection = toolboxSelection ?? new ToolboxSelectionState();
        _replacementRendererFactory = replacementRendererFactory;
        _publishPackageBuilder = publishPackageBuilder ?? PublishPackageBuilder;
    }

    internal event Func<Task>? StateChanged;

    internal ValueTask UndoAsync(CancellationToken cancellationToken = default) =>
        ExecuteHistoryOperationAsync(isUndo: true, cancellationToken);

    internal ValueTask RedoAsync(CancellationToken cancellationToken = default) =>
        ExecuteHistoryOperationAsync(isUndo: false, cancellationToken);

    internal ValueTask ZoomInAsync(CancellationToken cancellationToken = default) =>
        ExecuteZoomOperationAsync(
            static zoom => DocumentCanvasZoomPolicy.ZoomIn(zoom),
            cancellationToken);

    internal ValueTask ZoomOutAsync(CancellationToken cancellationToken = default) =>
        ExecuteZoomOperationAsync(
            static zoom => DocumentCanvasZoomPolicy.ZoomOut(zoom),
            cancellationToken);

    internal ValueTask SetZoomToActualSizeAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteZoomOperationAsync(
            static _ => DocumentCanvasZoomPolicy.ActualSize,
            cancellationToken);

    internal async ValueTask<ValidationSnapshot?> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            EditingSession? session;
            ModelValidationEngine engine;
            lock (_sync)
            {
                if (_disposed || _validationInFlight)
                {
                    return null;
                }

                session = _session;
                engine = _modelValidationEngine;
                _validationInFlight = true;
            }

            if (session is null)
            {
                return null;
            }

            var observed = session.CaptureState();
            if (!IsValidationContextAvailable(observed) ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null)
            {
                return null;
            }

            var confirmed = session.CaptureState();
            if (!IsSameValidationContext(observed, confirmed) ||
                document.DocumentId != confirmed.DocumentId ||
                document.Revision != confirmed.DocumentRevision)
            {
                return null;
            }

            var routingIssues = RoutingModelValidationIssueProvider.CreateIssues(
                confirmed.ProjectedGraph!,
                confirmed.RoutingResult!);
            var validation = engine.Validate(
                new ModelValidationContext(document, confirmed.ActiveScopeId),
                routingIssues,
                confirmed.Generation.Value);

            var current = session.CaptureState();
            if (!IsValidationContextAvailable(current) ||
                current.DocumentId != validation.DocumentId ||
                current.DocumentRevision != validation.SourceRevision ||
                current.ActiveScopeId != validation.ScopeId)
            {
                return null;
            }

            lock (_sync)
            {
                if (!_disposed)
                {
                    _validationSnapshot = validation;
                    RemoveHostDiagnosticUnderLock(HostModelValidationFailed);
                }
            }

            return validation;
        }
#pragma warning disable CA1031 // Plugin faults are exposed as infrastructure diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            lock (_sync)
            {
                _validationSnapshot = null;
                RemoveHostDiagnosticUnderLock(HostModelValidationFailed);
                _hostDiagnostics = _hostDiagnostics.Add(new Diagnostic(
                    HostModelValidationFailed,
                    DiagnosticSeverity.Error,
                    "Model validation failed because a validation contribution threw unexpectedly.",
                    context:
                    [
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            exception.GetType().FullName ?? exception.GetType().Name),
                    ]));
            }

            return null;
        }
#pragma warning restore CA1031
        finally
        {
            lock (_sync)
            {
                _validationInFlight = false;
            }

            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal async ValueTask<Canvas2DInteractionResult?> SelectValidationIssueAsync(
        ModelValidationIssueId issueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issueId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            EditingSession? session;
            Canvas2DInteractionController? interactionController;
            ValidationSnapshot? validation;
            lock (_sync)
            {
                session = _session;
                interactionController = _interactionController;
                validation = _validationSnapshot;
            }

            if (session is null || interactionController is null || validation is null)
            {
                return null;
            }

            var issue = validation.Issues.FirstOrDefault(candidate => candidate.Id == issueId);
            if (issue?.Target.VisualStateId is not { } visualStateId)
            {
                return null;
            }

            var state = session.CaptureState();
            if (!IsValidationContextAvailable(state) ||
                state.DocumentId != validation.DocumentId ||
                state.DocumentRevision != validation.SourceRevision ||
                state.ActiveScopeId != validation.ScopeId ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.DocumentId != validation.DocumentId ||
                document.Revision != validation.SourceRevision ||
                !document.VisualModel.TryGetVisualState(visualStateId, out _) ||
                !TryResolveValidationSelectionCssPoint(
                    state,
                    visualStateId,
                    out var cssPoint))
            {
                return null;
            }

            var result = await interactionController.PointerActivatedAsync(
                cssPoint,
                linked.Token).ConfigureAwait(false);
            lock (_sync)
            {
                _ = ReplaceInteractionDiagnosticsUnderLock(result.Diagnostics);
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
        }
    }

    internal ValueTask RefreshToolboxPlacementAsync(
        CancellationToken cancellationToken = default) =>
        UpdateToolboxPlacementAsync(cancel: false, cancellationToken);

    internal ValueTask CancelToolboxPlacementAsync(
        CancellationToken cancellationToken = default) =>
        UpdateToolboxPlacementAsync(cancel: true, cancellationToken);

    internal ValueTask CancelActiveInteractionAsync(
        CancellationToken cancellationToken = default) =>
        CancelActiveInteractionCoreAsync(cancellationToken);

    internal void CloseContextMenu()
    {
        var changed = false;
        lock (_sync)
        {
            if (!_disposed && _contextMenu is not null)
            {
                _contextMenu = null;
                changed = true;
            }
        }

        if (changed)
        {
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal async ValueTask<EditingSessionOperationResult?> NavigateToScopeAsync(
        DocumentScopeId scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            lock (_sync)
            {
                session = _disposed || !_initialized ? null : _session;
            }

            if (session is null)
            {
                return null;
            }

            return await NavigateToScopeUnderGateAsync(
                session,
                scopeId,
                changed => notify |= changed,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    internal async ValueTask<EditingSessionOperationResult?>
        ExecuteScopeNavigationContextActionAsync(
            CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            ScopeNavigationCatalog catalog;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                    ? null
                    : _session;
                context = _contextMenu;
                catalog = _scopeNavigationCatalog;
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || context?.ScopeNavigationAction is not { } action)
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.ActiveScopeId != context.ScopeId ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.Generation.Value < context.SessionGeneration.Value ||
                !state.EditorState.Selection.Contains(action.VisualStateId) ||
                GetSpatialRegionId(state, action.VisualStateId) !=
                    context.TargetPresentation?.Id ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.Revision != state.DocumentRevision ||
                !TryResolveScopeNavigationAction(
                    catalog,
                    document,
                    context.InteractionScopeId,
                    action.VisualStateId,
                    out var currentAction) ||
                currentAction is null ||
                currentAction.OwnerSemanticElementId != action.OwnerSemanticElementId ||
                currentAction.SemanticTypeId != action.SemanticTypeId ||
                currentAction.TargetScopeId != action.TargetScopeId)
            {
                return null;
            }

            return await NavigateToScopeUnderGateAsync(
                session,
                currentAction.TargetScopeId,
                changed => notify |= changed,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    internal async ValueTask<HistoryOperationResult?> ExecuteDeletionContextActionAsync(
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            DiagramDeletionCatalog deletionCatalog;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                    ? null
                    : _session;
                context = _contextMenu;
                deletionCatalog = _deletionCatalog;
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || context?.DeletionAction is not { } action)
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.Generation.Value < context.SessionGeneration.Value ||
                state.CurrentScene is null ||
                state.CurrentScene.DocumentId != state.DocumentId ||
                !(action.VisualStateId is { } actionVisualStateId
                    ? state.EditorState.Selection.Contains(actionVisualStateId) &&
                      GetSpatialRegionId(state, actionVisualStateId) ==
                          context.TargetPresentation?.Id
                    : state.EditorState.Selection.IsEmpty &&
                      state.EditorState.SemanticSceneSelection == action.SemanticId &&
                      ContainsSemanticSceneTarget(state, action.SemanticId)) ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.Revision != state.DocumentRevision ||
                !(action.VisualStateId is { } visualStateId
                    ? TryCreateDeletionRequest(document, visualStateId, out var request)
                    : TryCreateDeletionRequest(document, action.SemanticId, out request)) ||
                request is null ||
                request.TargetKind != action.TargetKind ||
                request.SemanticId != action.SemanticId ||
                !deletionCatalog.TryGetRegistration(
                    action.DeletionId,
                    out var registration) ||
                !registration.CommandFactory.CanDelete(request))
            {
                return null;
            }

            var matching = deletionCatalog.GetMatchingRegistrations(request);
            if (matching.Length != 1 || matching[0].DeletionId != action.DeletionId)
            {
                return null;
            }

            var planned = registration.CommandFactory.CreatePlan(request);
            if (!planned.Succeeded ||
                planned.Plan is not { } plan ||
                plan.TargetKind != action.TargetKind ||
                plan.SemanticId != action.SemanticId ||
                plan.VisualStateId != action.VisualStateId ||
                plan.Command.TargetDocumentId != document.DocumentId ||
                plan.Command.ExpectedRevision != document.Revision)
            {
                return null;
            }

            SceneObjectId? targetSceneObjectId = context.TargetSceneObjectId;
            if (action.VisualStateId is { } selectedVisualStateId)
            {
                if (!TryResolveVisualPropertiesTarget(
                        state,
                        document,
                        selectedVisualStateId,
                        context.TargetSceneObjectId,
                        context.TargetPresentation?.Id,
                        out var persistentTarget,
                        out _) ||
                    persistentTarget is null)
                {
                    return null;
                }

                targetSceneObjectId = persistentTarget.Id;
            }

            if (targetSceneObjectId is null)
            {
                return null;
            }

            var result = await session.ExecuteForSceneTargetAsync(
                plan.Command,
                state.CurrentScene,
                state.Generation,
                targetSceneObjectId,
                context.TargetPresentation,
                linked.Token).ConfigureAwait(false);
            notify |= RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<HistoryOperationResult?>
        ExecuteConnectorRouteContextActionAsync(
            CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                    ? null
                    : _session;
                context = _contextMenu;
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || context?.ConnectorRouteAction is not { } action)
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.Generation.Value < context.SessionGeneration.Value ||
                state.CurrentScene is null ||
                state.CurrentScene.DocumentId != state.DocumentId ||
                !state.EditorState.Selection.Contains(action.TargetVisualStateId) ||
                GetSpatialRegionId(state, action.TargetVisualStateId) !=
                    context.TargetPresentation?.Id ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.Revision != state.DocumentRevision ||
                !document.VisualModel.TryGetVisualState(
                    action.TargetVisualStateId,
                    out var visualState) ||
                visualState is null ||
                !action.TryResolveTargetRoute(
                    state.CurrentScene,
                    visualState.Route,
                    out var targetRoute))
            {
                return null;
            }

            var command = new UpdateConnectionRouteCommand(
                document.DocumentId,
                document.Revision,
                action.TargetVisualStateId,
                targetRoute);
            var result = await session.ExecuteForSceneTargetAsync(
                command,
                state.CurrentScene,
                state.Generation,
                action.SourceSceneObjectId,
                context.TargetPresentation,
                linked.Token).ConfigureAwait(false);
            notify |= RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<HistoryOperationResult?>
        ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole? addRole = null,
            CancellationToken cancellationToken = default)
    {
        if (addRole is { } requestedRole && !Enum.IsDefined(requestedRole))
        {
            throw new ArgumentOutOfRangeException(
                nameof(addRole),
                addRole,
                "The connector-anchor role must be defined.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                    ? null
                    : _session;
                context = _contextMenu;
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || context?.ConnectorAnchorAction is not { } action)
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status == EditingSessionStatus.Rebuilding &&
                state.EditorState.ActiveGesture is null &&
                state.DocumentId == context.SourceScene.DocumentId &&
                state.DocumentRevision == context.DocumentRevision &&
                state.ActiveScopeId == context.ScopeId &&
                state.Generation.Value >= context.SessionGeneration.Value &&
                state.EditorState.Selection.Contains(action.TargetVisualStateId))
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
                state = session.CaptureState();
            }

            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.Generation.Value < context.SessionGeneration.Value ||
                state.CurrentScene is null ||
                state.CurrentScene.DocumentId != state.DocumentId ||
                !state.EditorState.Selection.Contains(action.TargetVisualStateId) ||
                GetSpatialRegionId(state, action.TargetVisualStateId) !=
                    context.TargetPresentation?.Id ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.Revision != state.DocumentRevision ||
                !document.VisualModel.TryGetVisualState(
                    action.TargetVisualStateId,
                    out var visualState) ||
                visualState is null ||
                !action.IsCurrent(state.CurrentScene, visualState))
            {
                return null;
            }

            if (!document.SemanticModel.TryGetElement(
                    visualState.SemanticElementId,
                    out var semanticElement) ||
                semanticElement is null)
            {
                return null;
            }

            var connectorAnchorPolicy = session.ConnectorAnchorPolicyProvider.Resolve(
                semanticElement.TypeId);

            ICommand? command = action.Kind switch
            {
                Canvas2DConnectorAnchorContextActionKind.AddAnchor when
                    addRole is { } role &&
                    action.CanAdd(role) &&
                    ElementConnectorAnchorPolicyEvaluator.CanAdd(
                        connectorAnchorPolicy,
                        visualState,
                        action.Side,
                        role) =>
                    new AddConnectorAnchorCommand(
                        document.DocumentId,
                        document.Revision,
                        action.TargetVisualStateId,
                        CreateConnectorAnchorId(
                            document.DocumentId,
                            document.Revision,
                            action.TargetVisualStateId,
                            action.Side,
                            role,
                            action.InsertionIndex),
                        action.Side,
                        role,
                        action.InsertionIndex),
                Canvas2DConnectorAnchorContextActionKind.DeleteAnchor when
                    addRole is null &&
                    action.CanDelete &&
                    action.AnchorId is { } anchorId &&
                    ElementConnectorAnchorPolicyEvaluator.CanRemove(
                        connectorAnchorPolicy,
                        visualState,
                        anchorId) &&
                    !ConnectorAnchorOccupancy.IsOccupied(
                        document.VisualModel,
                        anchorId) =>
                    new RemoveConnectorAnchorCommand(
                        document.DocumentId,
                        document.Revision,
                        action.TargetVisualStateId,
                        anchorId),
                _ => null,
            };
            if (command is null)
            {
                return null;
            }

            var result = await session.ExecuteForSceneTargetAsync(
                command,
                state.CurrentScene,
                state.Generation,
                action.TargetSceneObjectId,
                context.TargetPresentation,
                linked.Token).ConfigureAwait(false);
            notify |= RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<HistoryOperationResult?>
        ExecuteNodeLabelContextActionAsync(
            CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                    ? null
                    : _session;
                context = _contextMenu;
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || context?.NodeLabelAction is not { } action)
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.Generation.Value < context.SessionGeneration.Value ||
                state.CurrentScene is null ||
                state.CurrentScene.DocumentId != state.DocumentId ||
                !state.EditorState.Selection.Contains(action.TargetVisualStateId) ||
                GetSpatialRegionId(state, action.TargetVisualStateId) !=
                    context.TargetPresentation?.Id ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.Revision != state.DocumentRevision ||
                !document.VisualModel.TryGetVisualState(
                    action.TargetVisualStateId,
                    out var visualState) ||
                visualState is null ||
                !action.IsCurrent(state.CurrentScene, visualState))
            {
                return null;
            }

            var command = new UpdateNodeLabelVisualOverrideCommand(
                document.DocumentId,
                document.Revision,
                action.TargetVisualStateId,
                targetOverride: null);
            var result = await session.ExecuteForSceneTargetAsync(
                command,
                state.CurrentScene,
                state.Generation,
                action.SourceSceneObjectId,
                context.TargetPresentation,
                linked.Token).ConfigureAwait(false);
            notify |= RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<DocumentCanvasPropertySnapshot?> OpenPropertiesAsync(
        VisualStateId targetVisualStateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            EditingSession? session;
            ElementPropertiesSchemaCatalog? propertiesSchemaCatalog;
            DocumentCanvasContextMenuState? context;
            lock (_sync)
            {
                if (_disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen)
                {
                    return null;
                }

                session = _session;
                propertiesSchemaCatalog = _propertiesSchemaCatalog;
                context = _contextMenu;
            }

            var contextMatches = context?.TargetVisualStateId == targetVisualStateId;
            var targetSceneObjectId = contextMatches ? context!.TargetSceneObjectId : null;
            var targetPresentationId = contextMatches
                ? context!.TargetPresentation?.Id
                : GetSpatialRegionId(session?.CaptureState(), targetVisualStateId);

            if (session is null || propertiesSchemaCatalog is null ||
                !TryCaptureSelectedProperties(
                    session,
                    targetVisualStateId,
                    requireReady: true,
                    propertiesSchemaCatalog,
                    out var snapshot,
                    targetSceneObjectId,
                    targetPresentationId))
            {
                lock (_sync)
                {
                    _contextMenu = null;
                }

                return null;
            }

            lock (_sync)
            {
                var state = session.CaptureState();
                var selection = state.EditorState.Selection;
                if (_disposed || !ReferenceEquals(session, _session) ||
                    state.Status != EditingSessionStatus.Ready ||
                    state.EditorState.ActiveGesture is not null ||
                    state.DocumentRevision != snapshot!.Revision ||
                    !selection.Contains(targetVisualStateId) ||
                    !session.TryCaptureDocumentSnapshot(out var currentDocument) ||
                    currentDocument is null || currentDocument.Revision != state.DocumentRevision ||
                    !TryCaptureSelectedProperties(
                        session,
                        targetVisualStateId,
                        requireReady: true,
                        propertiesSchemaCatalog,
                        out snapshot,
                        snapshot!.TargetSceneObjectId,
                        snapshot.SpatialRegion?.Id) ||
                    snapshot is null)
                {
                    _contextMenu = null;
                    return null;
                }

                _contextMenu = null;
                _propertiesTargetVisualStateId = targetVisualStateId;
                _propertiesTargetSemanticElementId = null;
                _propertiesTargetPresentationId = snapshot.SpatialRegion?.Id;
                _propertiesFormOpen = true;
                _propertiesFormDirty = false;
                _propertiesApplyInFlight = false;
            }

            return snapshot;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal bool CanOpenContextProperties()
    {
        lock (_sync)
        {
            return !_disposed && _initialized &&
                _contextMenu is { Kind: DocumentCanvasContextMenuKind.Element } context &&
                _session is { } session && _propertiesSchemaCatalog is { } catalog &&
                TryCaptureSelectedProperties(session, context.TargetVisualStateId,
                    context.TargetSemanticElementId, requireReady: true, catalog, out _,
                    context.TargetSceneObjectId, context.TargetPresentation?.Id);
        }
    }

    internal bool TryCaptureSelectedProperties(
        VisualStateId targetVisualStateId,
        out DocumentCanvasPropertySnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        snapshot = null;
        EditingSession? session;
        ElementPropertiesSchemaCatalog? propertiesSchemaCatalog;
        lock (_sync)
        {
            session = _disposed ? null : _session;
            propertiesSchemaCatalog = _disposed ? null : _propertiesSchemaCatalog;
        }

        var presentationId = GetSpatialRegionId(session?.CaptureState(), targetVisualStateId);

        return session is not null && propertiesSchemaCatalog is not null &&
            TryCaptureSelectedProperties(
                session,
                targetVisualStateId,
                requireReady: false,
                propertiesSchemaCatalog,
                out snapshot,
                expectedPresentationId: presentationId);
    }

    internal bool TryCaptureCurrentPropertiesForm(
        out DocumentCanvasPropertySnapshot? snapshot)
    {
        snapshot = null;
        lock (_sync)
        {
            return !_disposed &&
                _propertiesFormOpen &&
                _session is { } session &&
                _propertiesSchemaCatalog is { } propertiesSchemaCatalog &&
                TryCaptureSelectedProperties(
                    session,
                    _propertiesTargetVisualStateId,
                    _propertiesTargetSemanticElementId,
                    requireReady: false,
                    propertiesSchemaCatalog,
                    out snapshot,
                    expectedPresentationId: _propertiesTargetPresentationId);
        }
    }

    internal bool UpdatePropertiesFormState(
        bool isOpen,
        VisualStateId? targetVisualStateId,
        bool isDirty,
        bool applyInFlight = false,
        SemanticElementId? targetSemanticElementId = null,
        Canvas2DSpatialRegionId? targetPresentationId = null)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            if (!isOpen)
            {
                _propertiesFormOpen = false;
                _propertiesTargetVisualStateId = null;
                _propertiesTargetSemanticElementId = null;
                _propertiesTargetPresentationId = null;
                _propertiesFormDirty = false;
                _propertiesApplyInFlight = false;
                return true;
            }

            var editorState = _session?.CaptureState().EditorState;
            var targetIsCurrent = _propertiesFormOpen &&
                _propertiesTargetVisualStateId == targetVisualStateId &&
                _propertiesTargetSemanticElementId == targetSemanticElementId &&
                _propertiesTargetPresentationId == targetPresentationId &&
                ((targetVisualStateId is not null &&
                  editorState?.Selection.Contains(targetVisualStateId) == true &&
                  GetSpatialRegionId(_session?.CaptureState(), targetVisualStateId) == targetPresentationId) ||
                 (targetSemanticElementId is not null &&
                  editorState?.SemanticSceneSelection == targetSemanticElementId));
            if (!targetIsCurrent)
            {
                return false;
            }

            _propertiesFormDirty = isDirty;
            _propertiesApplyInFlight = applyInFlight;
            _contextMenu = null;
            return true;
        }
    }

    internal async ValueTask<DocumentCanvasPropertiesApplyResult> ApplyPropertiesAsync(
        DocumentCanvasPropertiesDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return CreateApplyFailure(
                DocumentCanvasPropertiesApplyStatus.Unavailable,
                PropertiesApplyUnavailable,
                "Properties could not be applied because the editor is unavailable.");
        }

        try
        {
            if (!draft.TryValidate(out var targetBounds, out var validationMessages))
            {
                return new DocumentCanvasPropertiesApplyResult(
                    DocumentCanvasPropertiesApplyStatus.ValidationFailed,
                    null,
                    [],
                    string.Join(" ", validationMessages));
            }

            EditingSession? session;
            ElementPropertiesSchemaCatalog? propertiesSchemaCatalog;
            lock (_sync)
            {
                if (_disposed || !_initialized || !_propertiesFormOpen ||
                    _propertiesApplyInFlight ||
                    _propertiesTargetVisualStateId != draft.Authoritative.VisualStateId ||
                    _propertiesTargetSemanticElementId !=
                        (draft.Authoritative.VisualStateId is null
                            ? draft.Authoritative.SemanticId
                            : null) ||
                    _propertiesTargetPresentationId !=
                        draft.Authoritative.SpatialRegion?.Id)
                {
                    return CreateApplyFailure(
                        DocumentCanvasPropertiesApplyStatus.Unavailable,
                        PropertiesApplyUnavailable,
                        "Properties could not be applied because the form is no longer current.");
                }

                _propertiesApplyInFlight = true;
                session = _session;
                propertiesSchemaCatalog = _propertiesSchemaCatalog;
            }

            if (session is null || propertiesSchemaCatalog is null ||
                !TryCaptureSelectedProperties(
                    session,
                    draft.Authoritative.VisualStateId,
                    draft.Authoritative.VisualStateId is null
                        ? draft.Authoritative.SemanticId
                        : null,
                    requireReady: true,
                    propertiesSchemaCatalog,
                    out var current,
                    draft.Authoritative.TargetSceneObjectId,
                    draft.Authoritative.SpatialRegion?.Id) ||
                current is null)
            {
                return CreateApplyFailure(
                    DocumentCanvasPropertiesApplyStatus.Unavailable,
                    PropertiesApplyUnavailable,
                    "Properties could not be applied because the selected object is unavailable.");
            }

            if (draft.IsStale || current.Revision != draft.Authoritative.Revision)
            {
                return CreateApplyFailure(
                    DocumentCanvasPropertiesApplyStatus.Stale,
                    PropertiesApplyStale,
                    "The selected object changed after the form was opened. Cancel and reopen Properties.");
            }

            var dataChanged = draft.TryGetDirtyDataField(out var changedDataField);
            var boundsChanged = current.CanEditBounds && targetBounds != current.Bounds;
            if (dataChanged && boundsChanged)
            {
                return new DocumentCanvasPropertiesApplyResult(
                    DocumentCanvasPropertiesApplyStatus.ValidationFailed,
                    current,
                    [],
                    "Apply one Data field or visual bounds before editing another property group.");
            }

            if (!dataChanged && !boundsChanged)
            {
                lock (_sync)
                {
                    _propertiesFormDirty = false;
                }

                return new DocumentCanvasPropertiesApplyResult(
                    DocumentCanvasPropertiesApplyStatus.NoChange,
                    current,
                    [],
                    null);
            }

            ICommand command;
            HistoryOperationResult result;
            if (dataChanged)
            {
                if (changedDataField is null ||
                    !current.TryGetDataField(changedDataField.FieldId, out var currentField) ||
                    currentField is null ||
                    !currentField.CanEdit ||
                    !currentField.Definition.Equals(changedDataField.Definition) ||
                    !changedDataField.TryCreateTargetValue(out var targetValue) ||
                    targetValue is null)
                {
                    return CreateApplyFailure(
                        DocumentCanvasPropertiesApplyStatus.Unavailable,
                        PropertiesApplyUnavailable,
                        "The selected Data field is no longer available for editing.");
                }

                command = currentField.Definition.MutationKind switch
                {
                    SemanticPropertyMutationKind.Name when !current.IsConnector =>
                        new UpdateSemanticElementNameCommand(
                            current.DocumentId,
                            current.Revision,
                            current.SemanticId,
                            currentField.Definition.SemanticPropertyKey,
                            targetValue.TextValue),
                    SemanticPropertyMutationKind.Property =>
                        new UpdateSemanticElementPropertyCommand(
                            current.DocumentId,
                            current.Revision,
                            current.SemanticId,
                            currentField.Definition.SemanticPropertyKey,
                            targetValue),
                    _ => throw new InvalidOperationException(
                        "A relationship Data field cannot use the Semantic Name mutation path."),
                };
            }
            else
            {
                var visualStateId = current.VisualStateId ?? throw new InvalidOperationException(
                    "Semantic-only Properties cannot edit visual bounds.");
                command = targetBounds.Width.Equals(current.Bounds.Width) &&
                    targetBounds.Height.Equals(current.Bounds.Height)
                    ? (ICommand)new MoveVisualStateCommand(
                        current.DocumentId,
                        current.Revision,
                        visualStateId,
                        targetBounds.TopLeft,
                        VisualPlacementMode.Pinned)
                    : new ResizeVisualStateCommand(
                        current.DocumentId,
                        current.Revision,
                        visualStateId,
                        targetBounds,
                        VisualPlacementMode.Pinned);
            }
            if (current.SourceScene is null ||
                current.SessionGeneration is not { } currentGeneration ||
                current.TargetSceneObjectId is null)
            {
                return CreateApplyFailure(
                    DocumentCanvasPropertiesApplyStatus.Unavailable,
                    PropertiesApplyUnavailable,
                    "The selected presentation is no longer available for editing.");
            }

            result = await session.ExecuteForSceneTargetAsync(
                command,
                current.SourceScene,
                currentGeneration,
                current.TargetSceneObjectId,
                current.SpatialRegion,
                linked.Token).ConfigureAwait(false);
            if (!result.IsCommitted)
            {
                return new DocumentCanvasPropertiesApplyResult(
                    DocumentCanvasPropertiesApplyStatus.Failed,
                    current,
                    result.Diagnostics,
                    result.Diagnostics.FirstOrDefault()?.Message ??
                        "The property change was not committed.");
            }

            await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            // The exact Scene item was checked before the command. A committed Name
            // edit can replace its rendered label lines, so refresh using the same
            // authoritative element and spatial presentation rather than the old line.
            if (!TryCaptureSelectedProperties(
                    session,
                    current.VisualStateId,
                    current.VisualStateId is null ? current.SemanticId : null,
                    requireReady: false,
                    propertiesSchemaCatalog,
                    out var refreshed,
                    expectedPresentationId: current.SpatialRegion?.Id) ||
                refreshed is null)
            {
                return CreateApplyFailure(
                    DocumentCanvasPropertiesApplyStatus.Failed,
                    PropertiesApplyUnavailable,
                    "The property change committed, but its refreshed values are unavailable.");
            }

            lock (_sync)
            {
                _propertiesFormDirty = false;
            }

            return new DocumentCanvasPropertiesApplyResult(
                DocumentCanvasPropertiesApplyStatus.Committed,
                refreshed,
                result.Diagnostics,
                null);
        }
        finally
        {
            lock (_sync)
            {
                _propertiesApplyInFlight = false;
            }

            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal async ValueTask<DocumentCanvasPropertySnapshot?> OpenPropertiesAsync(
        SemanticElementId targetSemanticElementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            EditingSession? session;
            ElementPropertiesSchemaCatalog? propertiesSchemaCatalog;
            DocumentCanvasContextMenuState? context;
            lock (_sync)
            {
                if (_disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen)
                {
                    return null;
                }

                session = _session;
                propertiesSchemaCatalog = _propertiesSchemaCatalog;
                context = _contextMenu;
            }

            if (session is null || propertiesSchemaCatalog is null ||
                !TryCaptureSelectedProperties(
                    session,
                    targetVisualStateId: null,
                    targetSemanticElementId,
                    requireReady: true,
                    propertiesSchemaCatalog,
                    out var snapshot,
                    expectedSceneObjectId:
                        context?.TargetSemanticElementId == targetSemanticElementId
                            ? context.TargetSceneObjectId
                            : null) ||
                snapshot is null)
            {
                lock (_sync)
                {
                    _contextMenu = null;
                }

                return null;
            }

            lock (_sync)
            {
                var state = session.CaptureState();
                if (_disposed || !ReferenceEquals(session, _session) ||
                    state.Status != EditingSessionStatus.Ready ||
                    state.EditorState.ActiveGesture is not null ||
                    state.DocumentRevision != snapshot.Revision ||
                    state.EditorState.SemanticSceneSelection != targetSemanticElementId)
                {
                    _contextMenu = null;
                    return null;
                }

                _contextMenu = null;
                _propertiesTargetVisualStateId = null;
                _propertiesTargetSemanticElementId = targetSemanticElementId;
                _propertiesTargetPresentationId = snapshot.SpatialRegion?.Id;
                _propertiesFormOpen = true;
                _propertiesFormDirty = false;
                _propertiesApplyInFlight = false;
            }

            return snapshot;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal async ValueTask<HistoryOperationResult?> ExecuteBackgroundContextActionAsync(
        CanvasBackgroundActionId actionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            Canvas2DInteractionController? interactionController;
            DocumentCanvasContextMenuState? context;
            CanvasBackgroundActionDefinition? definition;
            IDocumentCreationIdentityProvider identityProvider;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                        ? null
                        : _session;
                interactionController = _interactionController;
                context = _contextMenu;
                identityProvider = _documentCreationIdentityProvider;
                _ = _backgroundActionCatalog.TryGetDefinition(actionId, out definition);
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || interactionController is null || definition is null ||
                context?.Kind != DocumentCanvasContextMenuKind.Background ||
                !context.BackgroundActions.Any(action => action.Id == actionId))
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.Generation != context.SessionGeneration ||
                state.ActiveScopeId != context.ScopeId ||
                !ReferenceEquals(state.CurrentScene, context.SourceScene) ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.DocumentId != state.DocumentId ||
                document.Revision != state.DocumentRevision)
            {
                return null;
            }

            CanvasBackgroundActionPlan plan;
            try
            {
                var backgroundRequest = new CanvasBackgroundActionRequest(
                    document,
                    state.ActiveScopeId,
                    identityProvider);
                plan = definition.CreatePlan(backgroundRequest);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                RecordHostFailure(
                    HostBackgroundActionFailed,
                    $"The '{definition.DisplayName}' model action could not be prepared.",
                    exception);
                notify = true;
                return null;
            }

            if (plan.Command.TargetDocumentId != state.DocumentId ||
                plan.Command.ExpectedRevision != state.DocumentRevision)
            {
                RecordHostFailure(
                    HostBackgroundActionFailed,
                    $"The '{definition.DisplayName}' model action returned a stale command.",
                    new InvalidOperationException(
                        "A Canvas background action must target its exact request revision."));
                notify = true;
                return null;
            }

            var result = await session.ExecuteAsync(plan.Command, linked.Token)
                .ConfigureAwait(false);
            notify |= RetainContextCommandDiagnostics(session, state, result);
            if (!result.IsCommitted)
            {
                return result;
            }

            await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            if (plan.NavigateToScopeId is { } targetScopeId)
            {
                _ = await NavigateToScopeUnderGateAsync(
                    session,
                    targetScopeId,
                    changed => notify |= changed,
                    linked.Token).ConfigureAwait(false);
            }

            if (plan.SelectSemanticElementId is { } selectSemanticElementId)
            {
                var selectionState = session.CaptureState();
                if (selectionState.Status == EditingSessionStatus.Ready &&
                    selectionState.EditorState.ActiveGesture is null &&
                    ContainsSemanticSceneTarget(selectionState, selectSemanticElementId))
                {
                    _ = await interactionController.SelectSemanticSceneTargetAsync(
                        selectSemanticElementId,
                        linked.Token).ConfigureAwait(false);
                }
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<EditingSessionOperationResult?>
        ExecuteSemanticSceneViewContextActionAsync(
            SemanticSceneViewActionId actionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            SemanticSceneViewActionDefinition? definition;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                        ? null
                        : _session;
                context = _contextMenu;
                _ = _semanticSceneViewActionCatalog.TryGetDefinition(
                    actionId,
                    out definition);
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || definition is null ||
                context?.Kind != DocumentCanvasContextMenuKind.Element ||
                context.TargetVisualStateId is not null ||
                context.TargetSemanticElementId is not { } targetSemanticElementId ||
                context.TargetSceneObjectId is not { } targetSceneObjectId ||
                !context.SemanticViewActions.Any(action => action.Id == actionId))
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.CurrentScene is null ||
                state.EditorState.SemanticSceneSelection != targetSemanticElementId ||
                !state.EditorState.Selection.IsEmpty ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.DocumentId != state.DocumentId ||
                document.Revision != state.DocumentRevision)
            {
                return null;
            }

            var targetItems = state.CurrentScene.Items
                .Where(item =>
                    item.Id == targetSceneObjectId &&
                    item.Origin.SemanticElementId == targetSemanticElementId &&
                    item.Origin.VisualStateId is null &&
                    Equals(item.SpatialRegion, context.TargetPresentation) &&
                    item.IsVisible &&
                    item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                    Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item))
                .Take(2)
                .ToArray();
            if (targetItems.Length != 1)
            {
                return null;
            }

            var targetItem = targetItems[0];

            SemanticSceneViewActionPlan plan;
            try
            {
                var request = new SemanticSceneViewActionRequest(
                    document,
                    state.ActiveScopeId,
                    state.ModelProfileViewState,
                    state.ModelProfileElementViewState,
                    targetItem);
                // Leaving the Canvas for its HTML context menu may clear hover and install an
                // equivalent transient Scene. Keep document/scope/selection currency strict,
                // then re-evaluate applicability against that current Scene instead of rejecting
                // a View action solely because the Scene reference or generation changed.
                if (!IsSemanticSceneViewActionApplicable(definition, request))
                {
                    return null;
                }

                plan = definition.CreatePlan(request);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                RecordHostFailure(
                    HostSemanticSceneViewActionFailed,
                    $"The '{definition.DisplayName}' View action could not be prepared.",
                    exception);
                notify = true;
                return null;
            }

            return await session.TrySetModelProfileElementCollapsedAsync(
                state.DocumentId,
                state.DocumentRevision,
                state.ActiveScopeId,
                state.ModelProfileViewState,
                state.ModelProfileElementViewState,
                targetSceneObjectId,
                plan.ProfileId,
                plan.SemanticElementId,
                plan.IsCollapsed,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<HistoryOperationResult?>
        ExecuteSemanticSceneCommandContextActionAsync(
            SemanticSceneCommandActionId actionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        var notify = false;
        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            SemanticSceneCommandActionDefinition? definition;
            lock (_sync)
            {
                session = _disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen
                        ? null
                        : _session;
                context = _contextMenu;
                _ = _semanticSceneCommandActionCatalog.TryGetDefinition(
                    actionId,
                    out definition);
                notify = _contextMenu is not null;
                _contextMenu = null;
            }

            if (session is null || definition is null ||
                context?.Kind != DocumentCanvasContextMenuKind.Element ||
                context.TargetVisualStateId is not null ||
                context.TargetSemanticElementId is not { } targetSemanticElementId ||
                context.TargetSceneObjectId is not { } targetSceneObjectId ||
                !context.SemanticCommandActions.Any(action => action.Id == actionId))
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentId != context.SourceScene.DocumentId ||
                state.DocumentRevision != context.DocumentRevision ||
                state.ActiveScopeId != context.ScopeId ||
                state.CurrentScene is null ||
                state.EditorState.SemanticSceneSelection != targetSemanticElementId ||
                !state.EditorState.Selection.IsEmpty ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null ||
                document.DocumentId != state.DocumentId ||
                document.Revision != state.DocumentRevision)
            {
                return null;
            }

            var targetItems = state.CurrentScene.Items
                .Where(item =>
                    item.Id == targetSceneObjectId &&
                    item.Origin.SemanticElementId == targetSemanticElementId &&
                    item.Origin.VisualStateId is null &&
                    Equals(item.SpatialRegion, context.TargetPresentation) &&
                    item.IsVisible &&
                    item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                    Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item))
                .Take(2)
                .ToArray();
            if (targetItems.Length != 1)
            {
                return null;
            }

            SemanticSceneCommandActionPlan plan;
            try
            {
                var request = new SemanticSceneCommandActionRequest(
                    document,
                    state.ActiveScopeId,
                    targetItems[0]);
                if (!IsSemanticSceneCommandActionApplicable(definition, request))
                {
                    return null;
                }

                plan = definition.CreatePlan(request);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                RecordHostFailure(
                    HostSemanticSceneCommandActionFailed,
                    $"The '{definition.DisplayName}' model action could not be prepared.",
                    exception);
                notify = true;
                return null;
            }

            if (plan.SemanticElementId != targetSemanticElementId ||
                plan.Command.TargetDocumentId != document.DocumentId ||
                plan.Command.ExpectedRevision != document.Revision)
            {
                RecordHostFailure(
                    HostSemanticSceneCommandActionFailed,
                    $"The '{definition.DisplayName}' model action returned a stale command.",
                    new InvalidOperationException(
                        "A semantic Scene command action must target its exact request."));
                notify = true;
                return null;
            }

            var result = await session.ExecuteForSceneTargetAsync(
                plan.Command,
                state.CurrentScene,
                state.Generation,
                targetSceneObjectId,
                context.TargetPresentation,
                linked.Token).ConfigureAwait(false);
            notify |= RetainContextCommandDiagnostics(session, state, result);
            if (result.IsCommitted)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                _ = NotifyStateChangedSafelyAsync();
            }
        }
    }

    internal async ValueTask<DocumentCanvasModelViewPropertiesSnapshot?>
        OpenModelViewPropertiesAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            EditingSession? session;
            DocumentCanvasContextMenuState? context;
            lock (_sync)
            {
                if (_disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen)
                {
                    return null;
                }

                session = _session;
                context = _contextMenu;
            }

            if (session is null ||
                context?.Kind != DocumentCanvasContextMenuKind.Background)
            {
                return null;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.EditorState.ActiveGesture is not null ||
                state.DocumentRevision != context.DocumentRevision ||
                state.Generation != context.SessionGeneration ||
                state.ActiveScopeId != context.ScopeId ||
                !ReferenceEquals(state.CurrentScene, context.SourceScene))
            {
                lock (_sync)
                {
                    _contextMenu = null;
                }

                return null;
            }

            var snapshot = DocumentCanvasModelViewPropertiesSnapshot.Create(state);
            lock (_sync)
            {
                if (_disposed || !ReferenceEquals(session, _session))
                {
                    _contextMenu = null;
                    return null;
                }

                _contextMenu = null;
                _modelViewPropertiesFormOpen = true;
                _modelViewPropertiesFormDirty = false;
                _modelViewPropertiesApplyInFlight = false;
                _modelViewPropertiesScopeId = state.ActiveScopeId;
            }

            return snapshot;
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal bool TryCaptureCurrentModelViewPropertiesForm(
        out DocumentCanvasModelViewPropertiesSnapshot? snapshot)
    {
        snapshot = null;
        lock (_sync)
        {
            if (_disposed || !_modelViewPropertiesFormOpen ||
                _session is not { } session)
            {
                return false;
            }

            var state = session.CaptureState();
            if (state.IsClosed || state.ActiveScopeId != _modelViewPropertiesScopeId)
            {
                return false;
            }

            snapshot = DocumentCanvasModelViewPropertiesSnapshot.Create(state);
            return true;
        }
    }

    internal bool UpdateModelViewPropertiesFormState(
        bool isOpen,
        bool isDirty,
        bool applyInFlight = false)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            if (!isOpen)
            {
                ClearModelViewPropertiesFormUnderLock();
                return true;
            }

            if (!_modelViewPropertiesFormOpen ||
                _session?.CaptureState().ActiveScopeId != _modelViewPropertiesScopeId)
            {
                return false;
            }

            _modelViewPropertiesFormDirty = isDirty;
            _modelViewPropertiesApplyInFlight = applyInFlight;
            _contextMenu = null;
            return true;
        }
    }

    internal async ValueTask<DocumentCanvasModelViewPropertiesApplyResult>
        ApplyModelViewPropertiesAsync(
            DocumentCanvasModelViewPropertiesDraft draft,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return CreateModelViewApplyFailure(
                DocumentCanvasModelViewPropertiesApplyStatus.Unavailable,
                ModelViewPropertiesApplyUnavailable,
                "View and model properties could not be applied because the editor is unavailable.");
        }

        try
        {
            EditingSession? session;
            lock (_sync)
            {
                if (_disposed || !_initialized || !_modelViewPropertiesFormOpen ||
                    _modelViewPropertiesApplyInFlight ||
                    _modelViewPropertiesScopeId != draft.Authoritative.ScopeId)
                {
                    return CreateModelViewApplyFailure(
                        DocumentCanvasModelViewPropertiesApplyStatus.Unavailable,
                        ModelViewPropertiesApplyUnavailable,
                        "View and model properties could not be applied because the form is no longer current.");
                }

                _modelViewPropertiesApplyInFlight = true;
                session = _session;
            }

            if (session is null)
            {
                return CreateModelViewApplyFailure(
                    DocumentCanvasModelViewPropertiesApplyStatus.Unavailable,
                    ModelViewPropertiesApplyUnavailable,
                    "View and model properties could not be applied because the session is unavailable.");
            }

            var current = session.CaptureState();
            var catalogMatches = current.ModelProfileCatalog.Definitions
                .Select(static definition => definition.Id)
                .SequenceEqual(draft.Profiles.Select(static profile => profile.Definition.Id));
            if (draft.IsStale || !catalogMatches ||
                current.Status != EditingSessionStatus.Ready ||
                current.EditorState.ActiveGesture is not null ||
                current.DocumentId != draft.Authoritative.DocumentId ||
                current.DocumentRevision != draft.Authoritative.Revision ||
                current.ActiveScopeId != draft.Authoritative.ScopeId)
            {
                return CreateModelViewApplyFailure(
                    DocumentCanvasModelViewPropertiesApplyStatus.Stale,
                    ModelViewPropertiesApplyStale,
                    "The model or active view changed after the form was opened. Cancel and reopen View and model properties.");
            }

            var availabilityChanges = draft.CreateAvailabilityChanges();
            var targetViewState = draft.CreateTargetViewState(current.ModelProfileViewState);
            if (availabilityChanges.IsEmpty &&
                targetViewState.Equals(current.ModelProfileViewState))
            {
                lock (_sync)
                {
                    ClearModelViewPropertiesFormUnderLock();
                }

                return new DocumentCanvasModelViewPropertiesApplyResult(
                    DocumentCanvasModelViewPropertiesApplyStatus.NoChange,
                    DocumentCanvasModelViewPropertiesSnapshot.Create(current),
                    [],
                    null);
            }

            var diagnostics = ImmutableArray<Diagnostic>.Empty;
            if (!availabilityChanges.IsEmpty)
            {
                var command = new SetModelProfileAvailabilityCommand(
                    current.DocumentId,
                    current.DocumentRevision,
                    availabilityChanges);
                var result = await session.ExecuteAsync(command, linked.Token)
                    .ConfigureAwait(false);
                diagnostics = result.Diagnostics;
                if (!result.IsCommitted)
                {
                    return new DocumentCanvasModelViewPropertiesApplyResult(
                        DocumentCanvasModelViewPropertiesApplyStatus.Failed,
                        DocumentCanvasModelViewPropertiesSnapshot.Create(current),
                        diagnostics,
                        diagnostics.FirstOrDefault()?.Message ??
                            "Model profile availability was not committed.");
                }

                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
                current = session.CaptureState();
                if (current.Status != EditingSessionStatus.Ready ||
                    current.ActiveScopeId != draft.Authoritative.ScopeId)
                {
                    return CreateModelViewApplyFailure(
                        DocumentCanvasModelViewPropertiesApplyStatus.Failed,
                        ModelViewPropertiesApplyUnavailable,
                        "Profile availability committed, but the active view is no longer available.");
                }
            }

            targetViewState = draft.CreateTargetViewState(current.ModelProfileViewState);
            if (!targetViewState.Equals(current.ModelProfileViewState))
            {
                var viewResult = await session.UpdateModelProfileViewStateAsync(
                    targetViewState,
                    linked.Token).ConfigureAwait(false);
                diagnostics = diagnostics.AddRange(viewResult.Diagnostics);
                if (!viewResult.Succeeded)
                {
                    return new DocumentCanvasModelViewPropertiesApplyResult(
                        DocumentCanvasModelViewPropertiesApplyStatus.Failed,
                        DocumentCanvasModelViewPropertiesSnapshot.Create(current),
                        diagnostics,
                        viewResult.Diagnostics.FirstOrDefault()?.Message ??
                            "Profile visibility could not be updated.");
                }
            }

            current = session.CaptureState();
            lock (_sync)
            {
                ClearModelViewPropertiesFormUnderLock();
            }

            return new DocumentCanvasModelViewPropertiesApplyResult(
                DocumentCanvasModelViewPropertiesApplyStatus.Committed,
                DocumentCanvasModelViewPropertiesSnapshot.Create(current),
                diagnostics,
                null);
        }
        finally
        {
            lock (_sync)
            {
                _modelViewPropertiesApplyInFlight = false;
            }

            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    internal DocumentCanvasHostState CaptureState()
    {
        lock (_sync)
        {
            var sessionState = _session?.CaptureState();
            DocumentPublicationSnapshot? publication = null;
            if (sessionState is not null &&
                _session!.TryCaptureDocumentSnapshot(out var document) &&
                document.DocumentId == sessionState.DocumentId &&
                document.Revision == sessionState.DocumentRevision)
            {
                publication = document.Publication;
            }

            return new DocumentCanvasHostState(
                sessionState,
                _surfaceSize,
                _hostDiagnostics,
                _interactionDiagnostics,
                _pipelineCounters,
                _initializationAttempted,
                _initialized,
                _disposed,
                _successfulRenderCount,
                _latestPresentationSucceeded,
                _cssCursor,
                _contextMenu,
                _propertiesTargetVisualStateId,
                _propertiesFormOpen,
                _propertiesFormDirty,
                _propertiesApplyInFlight,
                _validationSnapshot,
                _validationInFlight,
                CaptureScopeBreadcrumbUnderLock(sessionState),
                _modelViewPropertiesFormOpen,
                _modelViewPropertiesFormDirty,
                _modelViewPropertiesApplyInFlight,
                _propertiesTargetSemanticElementId,
                _nativeDocumentCanvasElementId,
                publication,
                _documentSessionVersion);
        }
    }

    internal async ValueTask InitializeAsync(
        string canvasElementId,
        string containerElementId,
        CancellationToken cancellationToken = default) =>
        await InitializeAsync(
            canvasElementId,
            canvasElementId,
            containerElementId,
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask InitializeAsync(
        string canvasElementId,
        string standbyCanvasElementId,
        string containerElementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(standbyCanvasElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerElementId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (_disposed || _initializationAttempted)
                {
                    return;
                }

                _initializationAttempted = true;
            }

            try
            {
                _observer = await _observerFactory.CreateAsync(
                    containerElementId,
                    OnSurfaceChangedAsync,
                    linked.Token).ConfigureAwait(false);
                var initialSurface = await _observer.StartAsync(linked.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    _surfaceSize = initialSurface;
                }

                var renderer = _unattachedRenderer ?? throw new InvalidOperationException(
                    "The host renderer is unavailable before Editing Session attachment.");
                var initialization = await renderer.InitializeAsync(
                    canvasElementId,
                    initialSurface,
                    linked.Token).ConfigureAwait(false);
                if (!initialization.Succeeded)
                {
                    lock (_sync)
                    {
                        _hostDiagnostics = initialization.Diagnostics;
                    }

                    await ReleaseFailedInitializationResourcesAsync().ConfigureAwait(false);
                    return;
                }

                var composition = await _compositionFactory.CreateAsync(
                    linked.Token).ConfigureAwait(false);
                var notificationSource = ModelerNotifications is null ? null :
                    new BpmnModelerDocumentNotificationSource(composition.Document.CaptureSnapshot());
                _documentNotifications = notificationSource;
                var configuration = WithVisibleDocumentRegion(
                    composition.Configuration,
                    initialSurface,
                    notificationSource);
                var attachment = await EditingSession.AttachAsync(
                    composition.Document,
                    renderer,
                    configuration,
                    linked.Token).ConfigureAwait(false);

                // AttachAsync accepts ownership of the initialized renderer for every outcome.
                _unattachedRenderer = null;
                if (!attachment.IsAttached)
                {
                    lock (_sync)
                    {
                        _hostDiagnostics = attachment.Diagnostics;
                    }

                    await ReleaseFailedInitializationResourcesAsync().ConfigureAwait(false);
                    return;
                }

                var session = attachment.Session!;
                lock (_sync)
                {
                    _session = session;
                    checked { _documentSessionVersion++; }
                    _nativeDocumentCanvasElementId = canvasElementId;
                    _nativeDocumentStandbyCanvasElementId = standbyCanvasElementId;
                    _nativeDocumentSessionConfiguration = composition.Configuration;
                    _propertiesSchemaCatalog = composition.PropertiesSchemaCatalog;
                    _toolboxPlacementCatalog = composition.ToolboxPlacementCatalog;
                    _anchorConnectionCreationCatalog = composition.AnchorConnectionCreationCatalog;
                    _endpointReconnectionCatalog = composition.EndpointReconnectionCatalog;
                    _spatialEditPlanners = composition.SpatialEditPlanners;
                    _deletionCatalog = composition.DeletionCatalog;
                    _scopeNavigationCatalog = composition.ScopeNavigationCatalog;
                    _backgroundActionCatalog = composition.BackgroundActionCatalog;
                    _semanticSceneViewActionCatalog =
                        composition.SemanticSceneViewActionCatalog;
                    _semanticSceneCommandActionCatalog =
                        composition.SemanticSceneCommandActionCatalog;
                    _documentCreationIdentityProvider =
                        composition.DocumentCreationIdentityProvider;
                    _modelValidationEngine = new ModelValidationEngine(
                        composition.ModelValidationCatalog);
                    _pipelineCounters = composition.Counters;
                    _interactionController = new Canvas2DInteractionController(
                        session,
                        connectionCreationCatalog:
                            composition.AnchorConnectionCreationCatalog,
                        creationIdentityProvider:
                            composition.DocumentCreationIdentityProvider,
                        endpointReconnectionCatalog:
                            composition.EndpointReconnectionCatalog,
                        spatialEditPlanners: composition.SpatialEditPlanners);
                    _toolboxPlacementController = new ToolboxPlacementController(
                        composition.ToolboxPlacementCatalog,
                        _toolboxSelection,
                        composition.DocumentCreationIdentityProvider,
                        composition.SpatialEditPlanners);
                }

                session.StateChanged += HandleSessionStateChanged;
                if (_pointerObserverFactory is not null)
                {
                    _pointerObserver = await _pointerObserverFactory.CreateAsync(
                        canvasElementId,
                        OnPointerInputAsync,
                        OnWheelInputAsync,
                        linked.Token).ConfigureAwait(false);
                    await _pointerObserver.StartAsync(linked.Token).ConfigureAwait(false);
                }
                var sessionState = session.CaptureState();
                var presentationSucceeded =
                    attachment.Status == EditingSessionAttachStatus.Ready &&
                    !HasError(sessionState.PresentationDiagnostics);
                lock (_sync)
                {
                    _initialized = true;
                    _hostDiagnostics = [];
                    _observedActiveGestureId = sessionState.EditorState.ActiveGesture?.Id;
                    _observedDocumentRevision = sessionState.DocumentRevision;
                    _successfulPresentationGeneration = presentationSucceeded
                        ? sessionState.Generation : null;
                    _latestPresentationSucceeded = presentationSucceeded;
                    if (presentationSucceeded)
                    {
                        checked { _successfulRenderCount++; }
                    }
                }

                if (_toolboxPlacementController.IsPlacementActive)
                {
                    _ = await SetPointerCursorUnderGateAsync("crosshair", linked.Token)
                        .ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // Browser host failures become bounded presentation diagnostics.
            catch (Exception exception) when (IsNonFatal(exception))
            {
                lock (_sync)
                {
                    _hostDiagnostics =
                    [
                        new Diagnostic(
                            HostInitializationFailed,
                            DiagnosticSeverity.Error,
                            "The browser canvas host could not initialize its Editing Session.",
                            exception.GetType().FullName ?? exception.GetType().Name),
                    ];
                    _latestPresentationSucceeded = false;
                }

                await ReleaseFailedInitializationResourcesAsync().ConfigureAwait(false);
            }
#pragma warning restore CA1031
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        Task task;
        lock (_sync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = true;
            ModelerNotifications?.Dispose();
            _documentNotifications?.Dispose();
            _viewportPanGesture = null;
            _lifetime.Cancel();
            _disposeTask = task = DisposeCoreAsync();
        }

        return new ValueTask(task);
    }

    private async ValueTask UpdateToolboxPlacementAsync(
        bool cancel,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        var notify = false;
        try
        {
            ToolboxPlacementController? controller;
            EditingSession? session;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                controller = _toolboxPlacementController;
                session = _session;
            }

            if (controller is not null && session is not null)
            {
                notify |= await ToolboxPlacementController.ClearPreviewAsync(
                        session,
                        linked.Token)
                    .ConfigureAwait(false);
            }

            if (cancel)
            {
                notify |= controller?.Cancel() ?? _toolboxSelection.Clear();
            }

            var cursor = controller?.IsPlacementActive == true ? "crosshair" : "default";
            notify |= await SetPointerCursorUnderGateAsync(cursor, linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask CancelActiveInteractionCoreAsync(
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        var notify = false;
        try
        {
            Canvas2DInteractionController? interactionController;
            ToolboxPlacementController? placementController;
            ICanvasPresentationPointerObserver? pointerObserver;
            ViewportPanGesture? viewportPanGesture;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                interactionController = _interactionController;
                placementController = _toolboxPlacementController;
                pointerObserver = _pointerObserver;
                viewportPanGesture = _viewportPanGesture;
                _viewportPanGesture = null;
            }

            if (viewportPanGesture is not null &&
                pointerObserver is not null &&
                viewportPanGesture.CaptureGeneration > 0)
            {
                await pointerObserver.ReleaseCaptureAsync(
                    viewportPanGesture.CaptureGeneration,
                    linked.Token).ConfigureAwait(false);
            }

            if (interactionController is not null)
            {
                var cancelled = await interactionController.CancelActiveGestureAsync(
                    linked.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    notify |= ReplaceInteractionDiagnosticsUnderLock(
                        cancelled.Diagnostics);
                    _observedActiveGestureId =
                        cancelled.SessionState.EditorState.ActiveGesture?.Id;
                    _observedActiveGestureCaptureGeneration = 0;
                }
            }

            notify |= placementController?.Cancel() ?? _toolboxSelection.Clear();
            if (placementController is not null && _session is { } session)
            {
                notify |= await ToolboxPlacementController.ClearPreviewAsync(
                        session,
                        linked.Token)
                    .ConfigureAwait(false);
            }
            notify |= await SetPointerCursorUnderGateAsync("default", linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<EditingSessionOperationResult?> NavigateToScopeUnderGateAsync(
        EditingSession session,
        DocumentScopeId scopeId,
        Action<bool> publishChanged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(publishChanged);

        var observed = session.CaptureState();
        if (observed.ActiveScopeId == scopeId)
        {
            return await session.NavigateToScopeAsync(scopeId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (observed.Status != EditingSessionStatus.Ready ||
            !session.TryCaptureDocumentSnapshot(out var document) ||
            document is null ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !ScopeExists(document, scopeId))
        {
            return await session.NavigateToScopeAsync(scopeId, cancellationToken)
                .ConfigureAwait(false);
        }

        publishChanged(await ClearScopeNavigationTransientStateUnderGateAsync(
            cancellationToken).ConfigureAwait(false));
        return await session.NavigateToScopeAsync(scopeId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<bool> ClearScopeNavigationTransientStateUnderGateAsync(
        CancellationToken cancellationToken)
    {
        Canvas2DInteractionController? interactionController;
        ToolboxPlacementController? placementController;
        ICanvasPresentationPointerObserver? pointerObserver;
        ViewportPanGesture? viewportPanGesture;
        long interactionCaptureGeneration;
        var changed = false;
        lock (_sync)
        {
            interactionController = _interactionController;
            placementController = _toolboxPlacementController;
            pointerObserver = _pointerObserver;
            viewportPanGesture = _viewportPanGesture;
            interactionCaptureGeneration = _observedActiveGestureCaptureGeneration;
            _viewportPanGesture = null;
            _observedActiveGestureCaptureGeneration = 0;
            _consumedPlacementPointerId = null;
            changed = _contextMenu is not null ||
                _propertiesFormOpen ||
                _propertiesTargetVisualStateId is not null ||
                _propertiesTargetSemanticElementId is not null ||
                _propertiesTargetPresentationId is not null ||
                _propertiesFormDirty ||
                _modelViewPropertiesFormOpen ||
                _modelViewPropertiesFormDirty ||
                _validationSnapshot is not null ||
                !_interactionDiagnostics.IsEmpty;
            _contextMenu = null;
            _propertiesFormOpen = false;
            _propertiesTargetVisualStateId = null;
            _propertiesTargetSemanticElementId = null;
            _propertiesTargetPresentationId = null;
            _propertiesFormDirty = false;
            _propertiesApplyInFlight = false;
            ClearModelViewPropertiesFormUnderLock();
            _validationSnapshot = null;
            _ = ReplaceInteractionDiagnosticsUnderLock([]);
            RemoveHostDiagnosticUnderLock(HostModelValidationFailed);
        }

        if (viewportPanGesture is not null &&
            pointerObserver is not null &&
            viewportPanGesture.CaptureGeneration > 0)
        {
            await pointerObserver.ReleaseCaptureAsync(
                viewportPanGesture.CaptureGeneration,
                cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (pointerObserver is not null &&
            interactionCaptureGeneration > 0 &&
            interactionCaptureGeneration != viewportPanGesture?.CaptureGeneration)
        {
            await pointerObserver.ReleaseCaptureAsync(
                interactionCaptureGeneration,
                cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (interactionController is not null)
        {
            var cancelled = await interactionController.CancelActiveGestureAsync(
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                changed |= ReplaceInteractionDiagnosticsUnderLock(cancelled.Diagnostics);
                _observedActiveGestureId =
                    cancelled.SessionState.EditorState.ActiveGesture?.Id;
                _observedActiveGestureCaptureGeneration = 0;
            }
        }

        changed |= placementController?.Cancel() ?? false;
        changed |= _toolboxSelection.Clear();
        changed |= await SetPointerCursorUnderGateAsync("default", cancellationToken)
            .ConfigureAwait(false);
        return changed;
    }

    private async Task OnSurfaceChangedAsync(Canvas2DSurfaceSize surfaceSize)
    {
        var requestVersion = Interlocked.Increment(ref _surfaceRequestVersion);
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        try
        {
            await EnterModelerOperationAsync(_lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            EditingSession? session;
            lock (_sync)
            {
                if (_disposed || !_initialized ||
                    requestVersion != Volatile.Read(ref _surfaceRequestVersion) ||
                    _surfaceSize == surfaceSize)
                {
                    return;
                }

                session = _session;
                _surfaceSize = surfaceSize;
                if (_contextMenu is { } contextMenu)
                {
                    _contextMenu = contextMenu.Kind ==
                        DocumentCanvasContextMenuKind.Background
                            ? DocumentCanvasContextMenuState.CreateBackgroundClamped(
                                contextMenu.CssPosition,
                                surfaceSize,
                                contextMenu.DocumentRevision,
                                contextMenu.SessionGeneration,
                                contextMenu.SourceScene,
                                contextMenu.ScopeId,
                                contextMenu.BackgroundActions)
                            : contextMenu.TargetVisualStateId is { } targetVisualStateId
                            ? DocumentCanvasContextMenuState.CreateClamped(
                                contextMenu.CssPosition,
                                surfaceSize,
                                targetVisualStateId,
                                contextMenu.TargetSceneObjectId!,
                                contextMenu.ConnectorRouteAction,
                                contextMenu.DocumentRevision,
                                contextMenu.SessionGeneration,
                                contextMenu.SourceScene,
                                contextMenu.ConnectorAnchorAction,
                                contextMenu.NodeLabelAction,
                                contextMenu.DeletionAction,
                                contextMenu.ScopeNavigationAction,
                                contextMenu.ScopeId,
                                contextMenu.InteractionScopeId,
                                contextMenu.TargetPresentation)
                            : DocumentCanvasContextMenuState.CreateSemanticClamped(
                                contextMenu.CssPosition,
                                surfaceSize,
                                contextMenu.TargetSemanticElementId!,
                                contextMenu.TargetSceneObjectId!,
                                contextMenu.DeletionAction,
                                contextMenu.DocumentRevision,
                                contextMenu.SessionGeneration,
                                contextMenu.SourceScene,
                                contextMenu.ScopeId,
                                contextMenu.SemanticViewActions,
                                contextMenu.SemanticCommandActions,
                                contextMenu.TargetPresentation);
                }

                _surfaceRenderPending = true;
                _latestPresentationSucceeded = false;
            }

            if (session is null)
            {
                return;
            }

            var beforeResize = session.CaptureState();
            var sceneWillRebuild = beforeResize.EditorState.Viewport.VisibleDocumentRegion !=
                Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                    beforeResize.EditorState.Viewport,
                    surfaceSize);
            var resized = await session.ResizeAsync(
                surfaceSize,
                _lifetimeToken).ConfigureAwait(false);
            if (!resized.Succeeded)
            {
                lock (_sync)
                {
                    _surfaceRenderPending = false;
                    _latestPresentationSucceeded = false;
                }

                return;
            }

            var presentationSucceeded = !HasError(
                session.CaptureState().PresentationDiagnostics);
            lock (_sync)
            {
                _surfaceRenderPending = false;
                _latestPresentationSucceeded = presentationSucceeded;
                if (presentationSucceeded && !sceneWillRebuild)
                {
                    _hostDiagnostics = [];
                    checked { _successfulRenderCount++; }
                }
            }
        }
#pragma warning disable CA1031 // Browser callback failures become bounded host diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            lock (_sync)
            {
                _surfaceRenderPending = false;
                _latestPresentationSucceeded = false;
                _hostDiagnostics =
                [
                    new Diagnostic(
                        HostResizeFailed,
                        DiagnosticSeverity.Error,
                        "The browser canvas could not be resized and rendered.",
                        exception.GetType().FullName ?? exception.GetType().Name),
                ];
            }
        }
#pragma warning restore CA1031
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Stop browser input before releasing its interpreter or any presentation state.
            if (_pointerObserver is not null)
            {
#pragma warning disable CA1031 // Cursor reset is best effort; observer disposal still runs.
                try
                {
                    await _pointerObserver.SetCursorAsync(
                        "default",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    RecordHostFailure(
                        HostDisposalFailed,
                        "The browser canvas cursor could not be reset during disposal.",
                        exception);
                }
#pragma warning restore CA1031
                lock (_sync)
                {
                    _cssCursor = "default";
                }

                try
                {
                    await _pointerObserver.DisposeAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Session cleanup must continue after observer cleanup failure.
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    RecordHostFailure(
                        HostDisposalFailed,
                        "The browser canvas pointer observer could not be disposed completely.",
                        exception);
                }
#pragma warning restore CA1031
                finally
                {
                    _pointerObserver = null;
                }
            }

            if (_interactionController is not null)
            {
                try
                {
                    await _interactionController.DisposeAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Session cleanup must continue after interaction cleanup failure.
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    RecordHostFailure(
                        HostDisposalFailed,
                        "The Canvas2D interaction controller could not be disposed completely.",
                        exception);
                }
#pragma warning restore CA1031
                finally
                {
                    _interactionController = null;
                }
            }

            // Stop browser surface callbacks before closing the session and its renderer.
            if (_observer is not null)
            {
                try
                {
                    await _observer.DisposeAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Session cleanup must continue after observer cleanup failure.
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    RecordHostFailure(
                        HostDisposalFailed,
                        "The browser canvas observer could not be disposed completely.",
                        exception);
                }
#pragma warning restore CA1031
                finally
                {
                    _observer = null;
                }
            }

            if (_session is not null)
            {
                _session.StateChanged -= HandleSessionStateChanged;
                try
                {
                    await _session.DisposeAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Host disposal records but does not reclassify session cleanup failure.
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    RecordHostFailure(
                        HostDisposalFailed,
                        "The Editing Session could not be disposed completely.",
                        exception);
                }
#pragma warning restore CA1031
            }
            else if (_unattachedRenderer is not null)
            {
                try
                {
                    await _unattachedRenderer.DisposeAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Host disposal records best-effort pre-attachment cleanup failure.
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    RecordHostFailure(
                        HostDisposalFailed,
                        "The unattached Canvas2DRenderer could not be disposed completely.",
                        exception);
                }
#pragma warning restore CA1031
                _unattachedRenderer = null;
            }
        }
        finally
        {
            lock (_sync)
            {
                _contextMenu = null;
                _propertiesTargetVisualStateId = null;
                _propertiesTargetSemanticElementId = null;
                _propertiesTargetPresentationId = null;
                _propertiesFormOpen = false;
                _propertiesFormDirty = false;
                _propertiesApplyInFlight = false;
                ClearModelViewPropertiesFormUnderLock();
                _ = ReplaceInteractionDiagnosticsUnderLock([]);
            }

            await CompleteModelerOperationAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }

    private async ValueTask ReleaseFailedInitializationResourcesAsync()
    {
#pragma warning disable CA1031 // Initialization cleanup is best effort and cannot replace its failure.
        if (_pointerObserver is not null)
        {
            try
            {
                await _pointerObserver.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                _ = exception;
            }

            _pointerObserver = null;
        }

        if (_interactionController is not null)
        {
            try
            {
                await _interactionController.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                _ = exception;
            }

            _interactionController = null;
        }

        if (_observer is not null)
        {
            try
            {
                await _observer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                _ = exception;
            }

            _observer = null;
        }

        EditingSession? session;
        lock (_sync)
        {
            session = _session;
            _session = null;
            _documentNotifications?.Dispose();
            _documentNotifications = null;
            _pipelineCounters = null;
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
                _ = exception;
            }
        }

        if (_unattachedRenderer is not null)
        {
            try
            {
                await _unattachedRenderer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                _ = exception;
            }

            _unattachedRenderer = null;
        }
#pragma warning restore CA1031
    }

    private async Task OnWheelInputAsync(CanvasWheelInput input)
    {
        if (!input.IsFinite || input.IsModified || !input.HasDelta ||
            Volatile.Read(ref _disposed) ||
            !await TryEnterInteractionGateAsync().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            EditingSession? session;
            Canvas2DSurfaceSize? surfaceSize;
            lock (_sync)
            {
                session = _session;
                surfaceSize = _surfaceSize;
                if (_disposed || !_initialized || session is null || surfaceSize is null ||
                    _propertiesFormOpen || _modelViewPropertiesFormOpen ||
                    _contextMenu is not null ||
                    _viewportPanGesture is not null)
                {
                    return;
                }
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.CurrentScene is null ||
                state.EditorState.ActiveGesture is not null)
            {
                return;
            }

            var canvasTranslation = input.ResolveCanvasTranslation(surfaceSize.Value);
            _ = await session.PanViewportAsync(
                canvasTranslation,
                _lifetimeToken).ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
        }
    }

    private async Task OnPointerInputAsync(CanvasPointerInput input)
    {
        if (!input.IsFinite || Volatile.Read(ref _disposed))
        {
            return;
        }

        ToolboxPlacementController? observedPlacementController;
        lock (_sync)
        {
            observedPlacementController = _toolboxPlacementController;
        }

        // Pointer callbacks normally arrive through the browser's ordered drain. This early
        // guard also makes direct/concurrent managed callbacks ignore a second placement press
        // instead of queueing another Command behind the host gate.
        if (input.Kind == CanvasPointerEventKind.Down &&
            observedPlacementController?.IsPlacementInFlight == true)
        {
            return;
        }

        if (!await TryEnterInteractionGateAsync().ConfigureAwait(false))
        {
            return;
        }

        var notify = false;
        try
        {
            Canvas2DInteractionController? controller;
            ToolboxPlacementController? placementController;
            EditingSession? session;
            lock (_sync)
            {
                controller = _interactionController;
                placementController = _toolboxPlacementController;
                session = _session;
                if (_disposed || !_initialized || controller is null || session is null ||
                    _propertiesFormOpen || _modelViewPropertiesFormOpen)
                {
                    return;
                }

                if (input.Kind == CanvasPointerEventKind.Down && _contextMenu is not null)
                {
                    _contextMenu = null;
                    notify = true;
                }

                checked { _pointerInputVersion++; }
                _activePointerInputKind = input.Kind;
                _activePointerId = input.PointerId;
                _activePointerCaptureGeneration = input.CaptureGeneration;
            }

            if (TryConsumePlacementPointerBoundary(input))
            {
                notify |= await SetPointerCursorUnderGateAsync(
                    placementController?.IsPlacementActive == true ? "crosshair" : "default",
                    _lifetimeToken).ConfigureAwait(false);
                return;
            }

            if (await TryHandleViewportPanPointerUnderGateAsync(
                    input,
                    cssPoint: null,
                    session,
                    placementController).ConfigureAwait(false))
            {
                return;
            }

            if (!session.CaptureState().IsGraphicalInteractionEnabled)
            {
                // Last Known Good is display-only. The managed controller keeps its strict
                // readiness guard for direct callers, while the host silently drops ordinary
                // browser input so it cannot mask the authoritative runtime fault.
                lock (_sync)
                {
                    notify |= ReplaceInteractionDiagnosticsUnderLock([]);
                }

                return;
            }

            if (placementController?.IsPlacementActive == true &&
                input.Kind is CanvasPointerEventKind.Cancel or CanvasPointerEventKind.Leave)
            {
                notify |= await ToolboxPlacementController.ClearPreviewAsync(
                    session,
                    _lifetimeToken).ConfigureAwait(false);
                notify |= await SetPointerCursorUnderGateAsync(
                    "crosshair",
                    _lifetimeToken).ConfigureAwait(false);
                return;
            }

            Canvas2DInteractionResult result;
            if (input.Kind == CanvasPointerEventKind.Cancel)
            {
                result = await controller.PointerCancelledAsync(
                    input.PointerId,
                    _lifetimeToken).ConfigureAwait(false);
            }
            else if (input.Kind == CanvasPointerEventKind.Leave)
            {
                result = await controller.PointerLeftAsync(_lifetimeToken).ConfigureAwait(false);
            }
            else
            {
                PointD cssPoint;
                try
                {
                    cssPoint = new PointD(
                        input.ClientX - input.CanvasLeft,
                        input.ClientY - input.CanvasTop);
                }
                catch (ArgumentOutOfRangeException) when (
                    input.Kind is CanvasPointerEventKind.Down or
                        CanvasPointerEventKind.ContextMenu or
                        CanvasPointerEventKind.DoubleClick)
                {
                    var placementActive = input.Kind == CanvasPointerEventKind.Down &&
                        input.IsPrimary &&
                        input.Button == 0 &&
                        placementController?.IsPlacementActive == true;
                    lock (_sync)
                    {
                        notify |= _contextMenu is not null;
                        _contextMenu = null;
                        if (placementActive)
                        {
                            _consumedPlacementPointerId = input.PointerId;
                            notify |= ReplaceInteractionDiagnosticsUnderLock(
                            [
                                new Diagnostic(
                                    HostPointerCoordinateInvalid,
                                    DiagnosticSeverity.Warning,
                                    "The browser pointer coordinates could not be normalized for Toolbox placement.",
                                    FormattableString.Invariant($"pointer:{input.PointerId}")),
                            ]);
                        }
                    }

                    if (input.Kind == CanvasPointerEventKind.Down)
                    {
                        notify |= await SetPointerCursorUnderGateAsync(
                            placementActive ? "crosshair" : "default",
                            _lifetimeToken).ConfigureAwait(false);
                    }

                    return;
                }
                catch (ArgumentOutOfRangeException)
                {
                    result = await controller.PointerCancelledAsync(
                        input.PointerId,
                        _lifetimeToken).ConfigureAwait(false);
                    var diagnostics = result.Diagnostics.Add(new Diagnostic(
                        HostPointerCoordinateInvalid,
                        DiagnosticSeverity.Warning,
                        "The browser pointer coordinates could not be normalized; the active gesture was cancelled.",
                        FormattableString.Invariant($"pointer:{input.PointerId}")));
                    lock (_sync)
                    {
                        notify = ReplaceInteractionDiagnosticsUnderLock(diagnostics);
                    }

                    notify |= await SetPointerCursorUnderGateAsync(
                        result.CssCursor,
                        _lifetimeToken).ConfigureAwait(false);

                    return;
                }

                if (await TryHandleViewportPanPointerUnderGateAsync(
                        input,
                        cssPoint,
                        session,
                        placementController).ConfigureAwait(false))
                {
                    return;
                }

                if (input.Kind == CanvasPointerEventKind.Move &&
                    placementController?.IsPlacementActive == true)
                {
                    var preview = await placementController.UpdatePreviewAtCssPointAsync(
                        session,
                        cssPoint,
                        _lifetimeToken).ConfigureAwait(false);
                    if (preview.Handled)
                    {
                        lock (_sync)
                        {
                            notify |= ReplaceInteractionDiagnosticsUnderLock(
                                preview.Diagnostics);
                        }
                    }

                    notify |= await SetPointerCursorUnderGateAsync(
                        "crosshair",
                        _lifetimeToken).ConfigureAwait(false);
                    return;
                }

                if (input.Kind == CanvasPointerEventKind.Down &&
                    input.IsPrimary &&
                    input.Button == 0 &&
                    placementController?.IsPlacementActive == true)
                {
                    lock (_sync)
                    {
                        _consumedPlacementPointerId = input.PointerId;
                    }

                    var preview = await placementController.UpdatePreviewAtCssPointAsync(
                        session,
                        cssPoint,
                        _lifetimeToken).ConfigureAwait(false);
                    if (preview.Handled && !preview.Diagnostics.IsEmpty)
                    {
                        lock (_sync)
                        {
                            notify |= ReplaceInteractionDiagnosticsUnderLock(
                                preview.Diagnostics);
                        }
                    }

                    var placement = await placementController.TryPlaceAtCssPointAsync(
                        session,
                        cssPoint,
                        _lifetimeToken).ConfigureAwait(false);
                    if (placement.Handled)
                    {
                        var placementState = session.CaptureState();
                        lock (_sync)
                        {
                            notify |= ReplaceInteractionDiagnosticsUnderLock(
                                placementState.Status == EditingSessionStatus.RuntimeFaulted
                                    ? []
                                    : placement.Diagnostics);
                        }
                    }

                    notify |= await SetPointerCursorUnderGateAsync(
                        placementController.IsPlacementActive ? "crosshair" : "default",
                        _lifetimeToken).ConfigureAwait(false);
                    return;
                }

                if (input.Kind == CanvasPointerEventKind.ContextMenu)
                {
                    if (placementController?.IsPlacementActive == true)
                    {
                        notify |= placementController.Cancel();
                        notify |= await ToolboxPlacementController.ClearPreviewAsync(
                            session,
                            _lifetimeToken).ConfigureAwait(false);
                        notify |= await SetPointerCursorUnderGateAsync(
                            "default",
                            _lifetimeToken).ConfigureAwait(false);
                    }

                    result = await controller.PointerContextMenuAsync(
                        cssPoint,
                        _lifetimeToken).ConfigureAwait(false);
                    var targetVisualStateId = result.TargetOrigin?.VisualStateId;
                    lock (_sync)
                    {
                        var contextMenu = ReferenceEquals(session, _session)
                            ? CreateContextMenuIfCurrent(
                                result,
                                targetVisualStateId,
                                cssPoint,
                                _surfaceSize,
                                session,
                                _deletionCatalog,
                                _scopeNavigationCatalog,
                                _backgroundActionCatalog,
                                _semanticSceneViewActionCatalog,
                                _semanticSceneCommandActionCatalog,
                                _documentCreationIdentityProvider)
                            : null;
                        notify |= !Equals(_contextMenu, contextMenu);
                        _contextMenu = contextMenu;
                    }
                }
                else if (input.Kind == CanvasPointerEventKind.DoubleClick)
                {
                    result = await controller.PointerNodeBodyActivatedAsync(
                        cssPoint,
                        _lifetimeToken).ConfigureAwait(false);
                    var targetVisualStateId = result.TargetOrigin?.VisualStateId;
                    var activationState = session.CaptureState();
                    if (targetVisualStateId is not null &&
                        activationState.Status == EditingSessionStatus.Ready &&
                        activationState.ActiveScopeId == result.SessionState.ActiveScopeId &&
                        activationState.DocumentRevision ==
                            result.SessionState.DocumentRevision &&
                        ReferenceEquals(
                            activationState.CurrentScene,
                            result.SessionState.CurrentScene) &&
                        session.TryCaptureDocumentSnapshot(out var document) &&
                        document is not null &&
                        document.DocumentId == activationState.DocumentId &&
                        document.Revision == activationState.DocumentRevision &&
                        TryResolveScopeNavigationAction(
                            _scopeNavigationCatalog,
                            document,
                            activationState.ActiveScopeId,
                            targetVisualStateId,
                            out var scopeNavigationAction) &&
                        scopeNavigationAction is not null)
                    {
                        _ = await NavigateToScopeUnderGateAsync(
                            session,
                            scopeNavigationAction.TargetScopeId,
                            changed => notify |= changed,
                            _lifetimeToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    var normalized = new Canvas2DPointerInput(
                        input.PointerId,
                        cssPoint,
                        input.IsPrimary,
                        input.Button,
                        input.Buttons,
                        input.ControlKey);
                    result = input.Kind switch
                    {
                        CanvasPointerEventKind.Down => await controller.PointerPressedAsync(
                            normalized,
                            _lifetimeToken).ConfigureAwait(false),
                        CanvasPointerEventKind.Move => await controller.PointerMovedAsync(
                            normalized,
                            _lifetimeToken).ConfigureAwait(false),
                        CanvasPointerEventKind.Up => await controller.PointerReleasedAsync(
                            normalized,
                            _lifetimeToken).ConfigureAwait(false),
                        _ => throw new InvalidOperationException(
                            "The normalized Canvas pointer event kind is unsupported."),
                    };
                }
            }

            var publishInteractionDiagnostics = session.CaptureState().Status !=
                EditingSessionStatus.RuntimeFaulted;
            lock (_sync)
            {
                notify |= ReplaceInteractionDiagnosticsUnderLock(
                    publishInteractionDiagnostics
                        ? result.Diagnostics
                        : []);
                if (input.Kind is CanvasPointerEventKind.Up or CanvasPointerEventKind.Cancel)
                {
                    _observedActiveGestureId =
                        result.SessionState.EditorState.ActiveGesture?.Id;
                    _observedActiveGestureCaptureGeneration = 0;
                    _observedDocumentRevision = result.SessionState.DocumentRevision;
                }
            }

            if (input.Kind != CanvasPointerEventKind.ContextMenu)
            {
                notify |= await SetPointerCursorUnderGateAsync(
                    placementController?.IsPlacementActive == true
                        ? "crosshair"
                        : result.CssCursor,
                    _lifetimeToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_sync)
            {
                _activePointerInputKind = null;
                _activePointerId = null;
                _activePointerCaptureGeneration = 0;
            }

            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<bool> TryHandleViewportPanPointerUnderGateAsync(
        CanvasPointerInput input,
        PointD? cssPoint,
        EditingSession session,
        ToolboxPlacementController? placementController)
    {
        ViewportPanGesture? gesture;
        lock (_sync)
        {
            gesture = _viewportPanGesture;
        }

        if (input.Kind == CanvasPointerEventKind.Cancel &&
            gesture is not null &&
            gesture.PointerId == input.PointerId)
        {
            lock (_sync)
            {
                if (_viewportPanGesture?.PointerId == input.PointerId)
                {
                    _viewportPanGesture = null;
                }
            }

            _ = await SetPointerCursorUnderGateAsync(
                placementController?.IsPlacementActive == true ? "crosshair" : "default",
                _lifetimeToken).ConfigureAwait(false);
            return true;
        }

        if (input.Kind == CanvasPointerEventKind.Down &&
            input.IsPrimary &&
            input.Button == MiddleMouseButton)
        {
            if (cssPoint is null)
            {
                return false;
            }

            var state = session.CaptureState();
            if (gesture is not null ||
                state.Status != EditingSessionStatus.Ready ||
                state.CurrentScene is null ||
                state.EditorState.ActiveGesture is not null)
            {
                ICanvasPresentationPointerObserver? observer;
                lock (_sync)
                {
                    observer = _pointerObserver;
                }

                if (observer is not null && input.CaptureGeneration > 0)
                {
                    await observer.ReleaseCaptureAsync(
                        input.CaptureGeneration,
                        _lifetimeToken).ConfigureAwait(false);
                }

                return true;
            }

            lock (_sync)
            {
                _viewportPanGesture = new ViewportPanGesture(
                    input.PointerId,
                    input.CaptureGeneration,
                    cssPoint.Value);
            }

            _ = await SetPointerCursorUnderGateAsync(
                "grabbing",
                _lifetimeToken).ConfigureAwait(false);
            return true;
        }

        if (gesture is null ||
            gesture.PointerId != input.PointerId ||
            cssPoint is null ||
            input.Kind is not (CanvasPointerEventKind.Move or CanvasPointerEventKind.Up))
        {
            return false;
        }

        var finalBoundary = input.Kind == CanvasPointerEventKind.Up;
        var middleButtonStillDown = (input.Buttons & MiddleMouseButtonsMask) != 0;
        if (!finalBoundary && !middleButtonStillDown)
        {
            ICanvasPresentationPointerObserver? observer;
            lock (_sync)
            {
                if (_viewportPanGesture?.PointerId == input.PointerId)
                {
                    _viewportPanGesture = null;
                }

                observer = _pointerObserver;
            }

            if (observer is not null && gesture.CaptureGeneration > 0)
            {
                await observer.ReleaseCaptureAsync(
                    gesture.CaptureGeneration,
                    _lifetimeToken).ConfigureAwait(false);
            }

            _ = await SetPointerCursorUnderGateAsync(
                placementController?.IsPlacementActive == true ? "crosshair" : "default",
                _lifetimeToken).ConfigureAwait(false);
            return true;
        }

        var canvasTranslation = cssPoint.Value - gesture.LastCssPoint;
        if (canvasTranslation != default)
        {
            var panned = await session.PanViewportAsync(
                canvasTranslation,
                _lifetimeToken).ConfigureAwait(false);
            if (panned.Succeeded && !finalBoundary)
            {
                lock (_sync)
                {
                    if (_viewportPanGesture == gesture)
                    {
                        _viewportPanGesture = gesture with { LastCssPoint = cssPoint.Value };
                    }
                }
            }
        }

        if (finalBoundary)
        {
            lock (_sync)
            {
                if (_viewportPanGesture?.PointerId == input.PointerId)
                {
                    _viewportPanGesture = null;
                }
            }

            _ = await SetPointerCursorUnderGateAsync(
                placementController?.IsPlacementActive == true ? "crosshair" : "default",
                _lifetimeToken).ConfigureAwait(false);
        }

        return true;
    }

    private bool TryConsumePlacementPointerBoundary(CanvasPointerInput input)
    {
        if (input.Kind is not (
                CanvasPointerEventKind.Move or
                CanvasPointerEventKind.Up or
                CanvasPointerEventKind.Cancel))
        {
            return false;
        }

        lock (_sync)
        {
            if (_consumedPlacementPointerId != input.PointerId)
            {
                return false;
            }

            if (input.Kind is CanvasPointerEventKind.Up or CanvasPointerEventKind.Cancel)
            {
                _consumedPlacementPointerId = null;
            }

            return true;
        }
    }

    private async ValueTask ExecuteHistoryOperationAsync(
        bool isUndo,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        try
        {
            EditingSession? session;
            lock (_sync)
            {
                if (_disposed || !_initialized || _propertiesFormDirty ||
                    _propertiesApplyInFlight || _modelViewPropertiesFormDirty ||
                    _modelViewPropertiesApplyInFlight)
                {
                    return;
                }

                session = _session;
                _contextMenu = null;
            }

            if (session is null)
            {
                return;
            }

            var state = session.CaptureState();
            var activeScopeId = state.ActiveScopeId;
            var historyAvailable = isUndo
                ? state.HistoryStatus.CanUndo
                : state.HistoryStatus.CanRedo;
            if (state.EditorState.ActiveGesture is not null ||
                state.Status is not (
                    EditingSessionStatus.Ready or EditingSessionStatus.RuntimeFaulted) ||
                !historyAvailable)
            {
                return;
            }

            var result = isUndo
                ? await session.UndoAsync(linked.Token).ConfigureAwait(false)
                : await session.RedoAsync(linked.Token).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
                if (result.IsApplied ||
                    session.CaptureState().ActiveScopeId != activeScopeId)
                {
                    _ = await ClearScopeNavigationTransientStateUnderGateAsync(
                        linked.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            // A Razor event handler may be awaiting this operation on the renderer
            // synchronization context. Session notifications already publish the
            // authoritative state; do not synchronously call back into that context.
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    private async ValueTask ExecuteZoomOperationAsync(
        Func<double, double> targetZoomFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetZoomFactory);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        try
        {
            await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        try
        {
            EditingSession? session;
            Canvas2DSurfaceSize? surfaceSize;
            lock (_sync)
            {
                if (_disposed || !_initialized || _propertiesFormOpen ||
                    _modelViewPropertiesFormOpen)
                {
                    return;
                }

                session = _session;
                surfaceSize = _surfaceSize;
                _contextMenu = null;
            }

            if (session is null || surfaceSize is null)
            {
                return;
            }

            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready ||
                state.CurrentScene is null ||
                state.EditorState.ActiveGesture is not null)
            {
                return;
            }

            var currentViewport = state.EditorState.Viewport;
            var targetZoom = targetZoomFactory(currentViewport.Zoom);
            if (targetZoom.Equals(currentViewport.Zoom))
            {
                return;
            }

            var cssCenter = new PointD(
                surfaceSize.Value.CssWidth / 2d,
                surfaceSize.Value.CssHeight / 2d);
            var documentAnchor = Canvas2DRenderer.ConvertCssToDocument(
                state.CurrentScene,
                cssCenter);
            var targetPan = new VectorD(
                cssCenter.X - (documentAnchor.X * targetZoom),
                cssCenter.Y - (documentAnchor.Y * targetZoom));
            var viewport = new ViewportSnapshot(
                targetZoom,
                targetPan,
                currentViewport.VisibleDocumentRegion);

            await session.UpdateViewportAsync(viewport, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            // Session notifications publish rebuild progress and completion. This final
            // notification also refreshes controls after a guarded or rejected operation.
            _ = NotifyStateChangedSafelyAsync();
        }
    }

    private async ValueTask<bool> TryEnterInteractionGateAsync()
    {
        try
        {
            await EnterModelerOperationAsync(_lifetimeToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task ResetPointerPresentationSafelyAsync(
        bool releasePointerCapture,
        bool resetWhileUnavailable,
        bool forceCursorReset,
        string? expectedActiveGestureId,
        long expectedPointerInputVersion,
        long expectedCaptureGeneration)
    {
        if (!await TryEnterInteractionGateAsync().ConfigureAwait(false))
        {
            return;
        }

        var notify = false;
        try
        {
            ICanvasPresentationPointerObserver? observer;
            EditingSession? session;
            lock (_sync)
            {
                observer = _disposed ? null : _pointerObserver;
                session = _disposed ? null : _session;
            }

            if (observer is null || session is null)
            {
                return;
            }

            var current = session.CaptureState();
            bool pointerInputVersionStillExpected;
            lock (_sync)
            {
                pointerInputVersionStillExpected =
                    _pointerInputVersion == expectedPointerInputVersion;
            }

            var activeGestureStillExpected = StringComparer.Ordinal.Equals(
                expectedActiveGestureId,
                current.EditorState.ActiveGesture?.Id);
            var releaseStillCurrent = releasePointerCapture && activeGestureStillExpected;
            var cursorResetStillCurrent = pointerInputVersionStillExpected &&
                (releaseStillCurrent ||
                    forceCursorReset ||
                    resetWhileUnavailable && current.Status != EditingSessionStatus.Ready);
            if (!releaseStillCurrent && !cursorResetStillCurrent)
            {
                return;
            }

            if (cursorResetStillCurrent)
            {
                notify = await SetPointerCursorUnderGateAsync(
                    "default",
                    _lifetimeToken).ConfigureAwait(false);
            }

            if (!releaseStillCurrent)
            {
                return;
            }

            try
            {
                await observer.ReleaseCaptureAsync(
                    expectedCaptureGeneration,
                    _lifetimeToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Capture cleanup is post-state best effort and diagnostically visible.
            catch (Exception exception) when (IsNonFatal(exception))
            {
                RecordHostFailure(
                    HostPointerCaptureReleaseFailed,
                    "The browser canvas pointer capture could not be released.",
                    exception);
                notify = true;
            }
#pragma warning restore CA1031
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            if (notify)
            {
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<bool> SetPointerCursorUnderGateAsync(
        string cssCursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cssCursor);
        ICanvasPresentationPointerObserver? observer;
        lock (_sync)
        {
            if (_disposed || StringComparer.Ordinal.Equals(_cssCursor, cssCursor))
            {
                return false;
            }

            observer = _pointerObserver;
        }

        try
        {
            if (observer is not null)
            {
                await observer.SetCursorAsync(cssCursor, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
#pragma warning disable CA1031 // Cursor transport failure is bounded presentation state.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            RecordHostFailure(
                HostPointerCursorFailed,
                "The browser canvas cursor could not be updated.",
                exception);
            return true;
        }
#pragma warning restore CA1031

        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(observer, _pointerObserver))
            {
                return false;
            }

            _cssCursor = cssCursor;
            return true;
        }
    }

    private bool ReplaceInteractionDiagnosticsUnderLock(ImmutableArray<Diagnostic> diagnostics)
    {
        _contextCommandDiagnosticOwner = null;
        if (DiagnosticsEqual(_interactionDiagnostics, diagnostics))
        {
            return false;
        }

        _interactionDiagnostics = diagnostics;
        return true;
    }

    private bool RetainContextCommandDiagnostics(
        EditingSession session,
        EditingSessionState sourceState,
        HistoryOperationResult result)
    {
        if (result.Succeeded || result.Status == HistoryOperationStatus.Cancelled ||
            result.Diagnostics.IsDefaultOrEmpty)
        {
            return false;
        }

        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(_session, session))
            {
                return false;
            }

            var current = session.CaptureState();
            if (current.Status != EditingSessionStatus.Ready ||
                current.DocumentId != sourceState.DocumentId ||
                current.DocumentRevision != sourceState.DocumentRevision ||
                current.ActiveScopeId != sourceState.ActiveScopeId ||
                current.Generation != sourceState.Generation ||
                !ReferenceEquals(current.CurrentScene, sourceState.CurrentScene) ||
                result.DocumentId != sourceState.DocumentId ||
                result.PreviousRevision != sourceState.DocumentRevision)
            {
                return false;
            }

            var changed = ReplaceInteractionDiagnosticsUnderLock(result.Diagnostics);
            _contextCommandDiagnosticOwner =
                (session, sourceState.DocumentId, sourceState.DocumentRevision, sourceState.ActiveScopeId);
            return changed;
        }
    }

    private static bool DiagnosticsEqual(
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right) =>
        left.AsSpan().SequenceEqual(right.AsSpan());

    private void HandleSessionStateChanged(
        object? sender,
        EditingSessionStateChangedEventArgs eventArgs)
    {
        var releasePointerCapture = false;
        var resetPointerCursor = false;
        var forceCursorReset = false;
        string? expectedActiveGestureId = null;
        long expectedPointerInputVersion = 0;
        long expectedCaptureGeneration = 0;
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _session))
            {
                return;
            }

            var activeGestureId = eventArgs.State.EditorState.ActiveGesture?.Id;
            var previousActiveGestureId = _observedActiveGestureId;
            var previousCaptureGeneration = _observedActiveGestureCaptureGeneration;
            var activeGestureDisappeared = _observedActiveGestureId is not null &&
                !StringComparer.Ordinal.Equals(_observedActiveGestureId, activeGestureId);
            var browserBoundaryAlreadyReleasedCapture = activeGestureDisappeared &&
                _activePointerId is not null &&
                _activePointerInputKind is
                    CanvasPointerEventKind.Up or CanvasPointerEventKind.Cancel;
            releasePointerCapture = activeGestureDisappeared &&
                !browserBoundaryAlreadyReleasedCapture &&
                previousCaptureGeneration > 0;
            _observedActiveGestureId = activeGestureId;
            if (activeGestureId is null)
            {
                _observedActiveGestureCaptureGeneration = 0;
            }
            else if (!StringComparer.Ordinal.Equals(previousActiveGestureId, activeGestureId))
            {
                _observedActiveGestureCaptureGeneration =
                    _activePointerInputKind is
                        CanvasPointerEventKind.Down or CanvasPointerEventKind.Move
                        ? _activePointerCaptureGeneration
                        : 0;
            }

            expectedActiveGestureId = activeGestureId;
            expectedPointerInputVersion = _pointerInputVersion;
            expectedCaptureGeneration = previousCaptureGeneration;
            var activeBrowserBoundaryOwnsFinalCursor =
                _activePointerInputKind is
                    CanvasPointerEventKind.Up or CanvasPointerEventKind.Cancel;
            var documentRevisionChanged = _observedDocumentRevision is not null &&
                _observedDocumentRevision != eventArgs.State.DocumentRevision;
            forceCursorReset = (documentRevisionChanged &&
                !activeBrowserBoundaryOwnsFinalCursor) ||
                eventArgs.State.Status == EditingSessionStatus.RuntimeFaulted;
            if (documentRevisionChanged)
            {
                _ = ReplaceInteractionDiagnosticsUnderLock([]);
                RemoveHostDiagnosticUnderLock(HostModelValidationFailed);
            }

            // A completed command's feedback belongs to its authoritative context,
            // not the Scene/generation recreated when the feedback itself resizes the canvas.
            var contextCommandDiagnosticIsCurrent = _contextCommandDiagnosticOwner is { } owner &&
                ReferenceEquals(owner.Session, _session) &&
                owner.DocumentId == eventArgs.State.DocumentId &&
                owner.Revision == eventArgs.State.DocumentRevision &&
                owner.ScopeId == eventArgs.State.ActiveScopeId &&
                !eventArgs.State.IsClosed;
            if ((_contextCommandDiagnosticOwner is not null ||
                 eventArgs.State.Status == EditingSessionStatus.Rebuilding) &&
                !contextCommandDiagnosticIsCurrent)
            {
                _ = ReplaceInteractionDiagnosticsUnderLock([]);
            }

            if (_validationSnapshot is { } validation &&
                (validation.DocumentId != eventArgs.State.DocumentId ||
                 validation.SourceRevision != eventArgs.State.DocumentRevision ||
                 validation.ScopeId != eventArgs.State.ActiveScopeId))
            {
                _validationSnapshot = null;
                RemoveHostDiagnosticUnderLock(HostModelValidationFailed);
            }

            if ((eventArgs.State.IsClosed ||
                 eventArgs.State.Status == EditingSessionStatus.RuntimeFaulted) &&
                _viewportPanGesture is { } viewportPanGesture)
            {
                _viewportPanGesture = null;
                releasePointerCapture = viewportPanGesture.CaptureGeneration > 0;
                resetPointerCursor = true;
                forceCursorReset = true;
                expectedCaptureGeneration = viewportPanGesture.CaptureGeneration;
            }

            _observedDocumentRevision = eventArgs.State.DocumentRevision;
            var selection = eventArgs.State.EditorState.Selection;
            if (_contextMenu is { } contextMenu &&
                (eventArgs.State.IsClosed ||
                 eventArgs.State.Status == EditingSessionStatus.RuntimeFaulted ||
                 documentRevisionChanged ||
                 contextMenu.ScopeId != eventArgs.State.ActiveScopeId ||
                 contextMenu.Kind == DocumentCanvasContextMenuKind.Background &&
                 (contextMenu.SessionGeneration != eventArgs.State.Generation ||
                  !ReferenceEquals(contextMenu.SourceScene, eventArgs.State.CurrentScene)) ||
                  contextMenu.Kind == DocumentCanvasContextMenuKind.Element &&
                  !((contextMenu.TargetVisualStateId is { } targetVisualStateId &&
                      selection.Contains(targetVisualStateId) &&
                      (eventArgs.State.Status != EditingSessionStatus.Ready ||
                       (GetSpatialRegionId(eventArgs.State, targetVisualStateId) ==
                            contextMenu.TargetPresentation?.Id &&
                        ContainsPersistentVisualTarget(
                            eventArgs.State,
                            targetVisualStateId,
                            contextMenu.TargetPresentation)))) ||
                     (contextMenu.TargetSemanticElementId is { } targetSemanticElementId &&
                      selection.IsEmpty &&
                      eventArgs.State.EditorState.SemanticSceneSelection ==
                          targetSemanticElementId &&
                      (eventArgs.State.Status != EditingSessionStatus.Ready ||
                       ContainsSemanticSceneTarget(
                           eventArgs.State,
                           targetSemanticElementId,
                           contextMenu.TargetPresentation))))))
            {
                _contextMenu = null;
            }

            if (_propertiesFormOpen)
            {
                var semanticSelection =
                    eventArgs.State.EditorState.SemanticSceneSelection;
                if (selection.IsEmpty && semanticSelection is null)
                {
                    _propertiesFormOpen = false;
                    _propertiesTargetVisualStateId = null;
                    _propertiesTargetSemanticElementId = null;
                    _propertiesTargetPresentationId = null;
                    _propertiesFormDirty = false;
                    _propertiesApplyInFlight = false;
                }
                else if (!((_propertiesTargetVisualStateId is { } currentVisualTarget &&
                            selection.Contains(currentVisualTarget) &&
                            GetSpatialRegionId(eventArgs.State, currentVisualTarget) ==
                                _propertiesTargetPresentationId) ||
                           (_propertiesTargetSemanticElementId is { } currentSemanticTarget &&
                            selection.IsEmpty &&
                            semanticSelection == currentSemanticTarget)))
                {
                    if (selection.Length == 1)
                    {
                        _propertiesTargetVisualStateId = selection[0];
                        _propertiesTargetSemanticElementId = null;
                        _propertiesTargetPresentationId =
                            GetSpatialRegionId(eventArgs.State, selection[0]);
                        _propertiesFormDirty = false;
                        _propertiesApplyInFlight = false;
                    }
                    else if (semanticSelection is not null)
                    {
                        _propertiesTargetVisualStateId = null;
                        _propertiesTargetSemanticElementId = semanticSelection;
                        _propertiesTargetPresentationId = null;
                        _propertiesFormDirty = false;
                        _propertiesApplyInFlight = false;
                    }
                    else
                    {
                        _propertiesFormOpen = false;
                        _propertiesTargetVisualStateId = null;
                        _propertiesTargetSemanticElementId = null;
                        _propertiesTargetPresentationId = null;
                        _propertiesFormDirty = false;
                        _propertiesApplyInFlight = false;
                    }
                }
            }

            // Selection can retarget an existing form without another Open call.
            // Only a captured, explicitly ineligible target closes it here;
            // transient capture failures retain the existing stale-form behavior.
            if (_propertiesFormOpen && !_propertiesApplyInFlight &&
                eventArgs.State.Status == EditingSessionStatus.Ready &&
                _session is { } propertiesSession && _propertiesSchemaCatalog is { } propertiesCatalog &&
                !TryCaptureSelectedProperties(propertiesSession, _propertiesTargetVisualStateId,
                    _propertiesTargetSemanticElementId, requireReady: false, propertiesCatalog,
                    out var propertiesTarget, expectedPresentationId: _propertiesTargetPresentationId) &&
                propertiesTarget is { IsPropertiesAvailable: false })
            {
                _ = UpdatePropertiesFormState(isOpen: false, targetVisualStateId: null, isDirty: false);
            }

            if (_modelViewPropertiesFormOpen &&
                (eventArgs.State.IsClosed ||
                 eventArgs.State.Status == EditingSessionStatus.RuntimeFaulted ||
                 eventArgs.State.ActiveScopeId != _modelViewPropertiesScopeId))
            {
                ClearModelViewPropertiesFormUnderLock();
            }

            resetPointerCursor = forceCursorReset ||
                !activeBrowserBoundaryOwnsFinalCursor &&
                activeGestureDisappeared;

            switch (eventArgs.State.Status)
            {
                case EditingSessionStatus.Rebuilding:
                    _latestPresentationSucceeded = false;
                    break;
                case EditingSessionStatus.RuntimeFaulted:
                    _latestPresentationSucceeded = false;
                    _ = ReplaceInteractionDiagnosticsUnderLock([]);
                    break;
                case EditingSessionStatus.Ready:
                    // Notifications capture current state and may omit or repeat intermediate
                    // states. Graphics completion belongs to the session's renderer result,
                    // never to an assumed Rebuilding -> Ready -> Ready notification sequence.
                    var scenePresented = eventArgs.State.IsCurrentScenePresented;
                    if (!_surfaceRenderPending)
                    {
                        _latestPresentationSucceeded = scenePresented;
                    }
                    if (scenePresented &&
                        _successfulPresentationGeneration != eventArgs.State.Generation)
                    {
                        _successfulPresentationGeneration = eventArgs.State.Generation;
                        checked { _successfulRenderCount++; }
                    }

                    break;
            }
        }

        if (releasePointerCapture || resetPointerCursor)
        {
            _ = ResetPointerPresentationSafelyAsync(
                releasePointerCapture,
                eventArgs.State.Status != EditingSessionStatus.Ready,
                forceCursorReset,
                expectedActiveGestureId,
                expectedPointerInputVersion,
                expectedCaptureGeneration);
        }

        _ = NotifyStateChangedSafelyAsync();
    }

    private async Task NotifyStateChangedSafelyAsync()
    {
        try
        {
            await NotifyStateChangedAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A detached UI notification cannot affect session correctness.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
        }
#pragma warning restore CA1031
    }

    private async Task NotifyStateChangedAsync()
    {
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            await handler().ConfigureAwait(false);
        }
    }

    private static bool TryCaptureSelectedProperties(
        EditingSession session,
        VisualStateId targetVisualStateId,
        bool requireReady,
        ElementPropertiesSchemaCatalog propertiesSchemaCatalog,
        out DocumentCanvasPropertySnapshot? snapshot,
        SceneObjectId? expectedSceneObjectId = null,
        Canvas2DSpatialRegionId? expectedPresentationId = null) =>
        TryCaptureSelectedProperties(
            session,
            targetVisualStateId,
            targetSemanticElementId: null,
            requireReady,
            propertiesSchemaCatalog,
            out snapshot,
            expectedSceneObjectId,
            expectedPresentationId);

    private static bool TryCaptureSelectedProperties(
        EditingSession session,
        VisualStateId? targetVisualStateId,
        SemanticElementId? targetSemanticElementId,
        bool requireReady,
        ElementPropertiesSchemaCatalog propertiesSchemaCatalog,
        out DocumentCanvasPropertySnapshot? snapshot,
        SceneObjectId? expectedSceneObjectId = null,
        Canvas2DSpatialRegionId? expectedPresentationId = null)
    {
        snapshot = null;
        if ((targetVisualStateId is null) == (targetSemanticElementId is null))
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var state = session.CaptureState();
            if (state.IsClosed || state.EditorState.ActiveGesture is not null ||
                (targetVisualStateId is not null
                    ? !state.EditorState.Selection.Contains(targetVisualStateId) ||
                      expectedPresentationId is not null &&
                      GetSpatialRegionId(state, targetVisualStateId) != expectedPresentationId
                    : !state.EditorState.Selection.IsEmpty ||
                      state.EditorState.SemanticSceneSelection != targetSemanticElementId) ||
                requireReady && state.Status != EditingSessionStatus.Ready ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document is null)
            {
                return false;
            }

            var confirmed = session.CaptureState();
            if (confirmed.DocumentRevision != state.DocumentRevision ||
                !ReferenceEquals(confirmed.EditorState, state.EditorState))
            {
                continue;
            }

            if (document.Revision == confirmed.DocumentRevision)
            {
                if (targetVisualStateId is not null)
                {
                    if (!TryResolveVisualPropertiesTarget(
                            confirmed,
                            document,
                            targetVisualStateId,
                            expectedSceneObjectId,
                            expectedPresentationId,
                            out var targetItem,
                            out var interactionScopeId) ||
                        targetItem is null || interactionScopeId is null)
                    {
                        return false;
                    }

                    return DocumentCanvasPropertySnapshot.TryCreate(
                        document,
                        targetVisualStateId,
                        propertiesSchemaCatalog,
                        out snapshot,
                        interactionScopeId,
                        targetItem.Id,
                        targetItem.SpatialRegion,
                        confirmed.Generation,
                        confirmed.CurrentScene) && snapshot is { IsPropertiesAvailable: true };
                }

                var presentationItem = confirmed.CurrentScene?.Items.FirstOrDefault(item =>
                    (expectedSceneObjectId is null || item.Id == expectedSceneObjectId) &&
                    item.Origin.SemanticElementId == targetSemanticElementId &&
                    Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item) &&
                    item.IsVisible &&
                    item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
                if (presentationItem is null ||
                    !DocumentCanvasPropertySnapshot.TryCreateSemantic(
                        document,
                        targetSemanticElementId!,
                        presentationItem.Bounds,
                        propertiesSchemaCatalog,
                        out snapshot) ||
                    snapshot is not { IsPropertiesAvailable: true })
                {
                    return false;
                }

                snapshot = snapshot with
                {
                    InteractionScopeId = confirmed.ActiveScopeId,
                    TargetSceneObjectId = presentationItem.Id,
                    SpatialRegion = presentationItem.SpatialRegion,
                    SessionGeneration = confirmed.Generation,
                    SourceScene = confirmed.CurrentScene,
                };
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveVisualPropertiesTarget(
        EditingSessionState state,
        DocumentSnapshot document,
        VisualStateId visualStateId,
        SceneObjectId? expectedSceneObjectId,
        Canvas2DSpatialRegionId? expectedPresentationId,
        out Canvas2DSceneItem? targetItem,
        out DocumentScopeId? interactionScopeId)
    {
        targetItem = null;
        interactionScopeId = null;
        if (state.CurrentScene is not { } scene ||
            GetSpatialRegionId(state, visualStateId) != expectedPresentationId ||
            !document.VisualModel.TryGetVisualState(visualStateId, out var visual) ||
            visual is null)
        {
            return false;
        }

        Canvas2DSpatialRegion? presentation = null;
        if (expectedPresentationId is not null)
        {
            presentation = scene.SpatialPresentationPlan?.Regions.SingleOrDefault(
                instance => instance.Id == expectedPresentationId);
            if (presentation is null)
            {
                return false;
            }
        }

        if (!state.TryGetCurrentProcessInteraction(out var scopeScene) ||
            scopeScene is null ||
            !document.SemanticModel.TryGetScope(visual.SemanticElementId, out var semanticScope) ||
            semanticScope?.Id != scopeScene.ActiveScopeId)
        {
            return false;
        }

        if (expectedSceneObjectId is not null &&
            !scene.Items.Any(item =>
                item.Id == expectedSceneObjectId &&
                item.Origin.VisualStateId == visualStateId &&
                Equals(item.SpatialRegion, presentation)))
        {
            return false;
        }

        targetItem = scene.Items
            .Where(item =>
                item.Origin.VisualStateId == visualStateId &&
                Equals(item.SpatialRegion, presentation) &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                item.Origin.SemanticElementId == visual.SemanticElementId &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            .OrderBy(item => item.Id == expectedSceneObjectId ? 0 : 1)
            .ThenBy(static item => item.Layer)
            .ThenBy(static item => item.ZIndex)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (targetItem is null)
        {
            return false;
        }

        interactionScopeId = scopeScene.ActiveScopeId;
        return true;
    }

    private static Canvas2DSpatialRegionId? GetSpatialRegionId(
        EditingSessionState? state, VisualStateId visualStateId) =>
        state?.CurrentScene?.GetVisualRegion(visualStateId)?.Id;

    private static bool ContainsPersistentVisualTarget(
        EditingSessionState state,
        VisualStateId targetVisualStateId,
        Canvas2DSpatialRegion? spatialRegion = null) =>
        state.CurrentScene?.Items.Any(item =>
            item.Origin.VisualStateId == targetVisualStateId &&
            Equals(item.SpatialRegion, spatialRegion) &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            item.IsVisible &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None) == true;

    private static bool ContainsSemanticSceneTarget(
        EditingSessionState state,
        SemanticElementId targetSemanticElementId,
        Canvas2DSpatialRegion? spatialRegion = null) =>
        state.CurrentScene?.Items.Any(item =>
            item.Origin.SemanticElementId == targetSemanticElementId &&
            Equals(item.SpatialRegion, spatialRegion) &&
            Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item) &&
            item.IsVisible &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None) == true;

    private static bool IsBackgroundActionApplicable(
        CanvasBackgroundActionDefinition definition,
        CanvasBackgroundActionRequest request)
    {
#pragma warning disable CA1031 // Faulty optional applicability hides only its contributed action.
        try
        {
            return definition.IsApplicable(request);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
            return false;
        }
#pragma warning restore CA1031
    }

    private static bool IsSemanticSceneViewActionApplicable(
        SemanticSceneViewActionDefinition definition,
        SemanticSceneViewActionRequest request)
    {
#pragma warning disable CA1031 // Faulty optional applicability hides only its contributed action.
        try
        {
            return definition.IsApplicable(request);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
            return false;
        }
#pragma warning restore CA1031
    }

    private static bool IsSemanticSceneCommandActionApplicable(
        SemanticSceneCommandActionDefinition definition,
        SemanticSceneCommandActionRequest request)
    {
#pragma warning disable CA1031 // Faulty optional applicability hides only its contributed action.
        try
        {
            return definition.IsApplicable(request);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
            return false;
        }
#pragma warning restore CA1031
    }

    private static DocumentCanvasContextMenuState? CreateContextMenuIfCurrent(
        Canvas2DInteractionResult result,
        VisualStateId? targetVisualStateId,
        PointD cssPoint,
        Canvas2DSurfaceSize? surfaceSize,
        EditingSession session,
        DiagramDeletionCatalog deletionCatalog,
        ScopeNavigationCatalog scopeNavigationCatalog,
        CanvasBackgroundActionCatalog backgroundActionCatalog,
        SemanticSceneViewActionCatalog semanticSceneViewActionCatalog,
        SemanticSceneCommandActionCatalog semanticSceneCommandActionCatalog,
        IDocumentCreationIdentityProvider identityProvider)
    {
        ArgumentNullException.ThrowIfNull(deletionCatalog);
        ArgumentNullException.ThrowIfNull(scopeNavigationCatalog);
        ArgumentNullException.ThrowIfNull(backgroundActionCatalog);
        ArgumentNullException.ThrowIfNull(semanticSceneViewActionCatalog);
        ArgumentNullException.ThrowIfNull(semanticSceneCommandActionCatalog);
        ArgumentNullException.ThrowIfNull(identityProvider);
        var current = session.CaptureState();
        if (!((result.Status is
                    Canvas2DInteractionStatus.Updated or
                    Canvas2DInteractionStatus.Unchanged) &&
            surfaceSize is not null &&
            current.Status == EditingSessionStatus.Ready &&
            current.DocumentRevision == result.SessionState.DocumentRevision &&
            current.Generation == result.SessionState.Generation &&
            ReferenceEquals(current.EditorState, result.SessionState.EditorState) &&
            ReferenceEquals(current.CurrentScene, result.SessionState.CurrentScene) &&
            current.ActiveScopeId == result.SessionState.ActiveScopeId &&
            current.CurrentScene is not null))
        {
            return null;
        }

        var targetSemanticElementId = targetVisualStateId is null
            ? result.TargetOrigin?.SemanticElementId
            : null;
        if (targetVisualStateId is null && targetSemanticElementId is null)
        {
            if (cssPoint.X < 0d || cssPoint.X > surfaceSize.Value.CssWidth ||
                cssPoint.Y < 0d || cssPoint.Y > surfaceSize.Value.CssHeight)
            {
                return null;
            }

            var backgroundActions = backgroundActionCatalog.Definitions;
            if (session.TryCaptureDocumentSnapshot(out var backgroundDocument) &&
                backgroundDocument is not null &&
                backgroundDocument.Revision == current.DocumentRevision)
            {
                var backgroundRequest = new CanvasBackgroundActionRequest(
                    backgroundDocument,
                    current.ActiveScopeId,
                    identityProvider);
                backgroundActions = [.. backgroundActions.Where(action =>
                    IsBackgroundActionApplicable(action, backgroundRequest))];
            }
            else
            {
                backgroundActions = [];
            }

            return DocumentCanvasContextMenuState.CreateBackgroundClamped(
                cssPoint,
                surfaceSize.Value,
                result.SessionState.DocumentRevision,
                result.SessionState.Generation,
                result.SessionState.CurrentScene!,
                current.ActiveScopeId,
                backgroundActions);
        }

        if (targetSemanticElementId is not null)
        {
            if (result.TargetId is not { } semanticTargetSceneObjectId)
            {
                return null;
            }

            if (current.EditorState.SemanticSceneSelection != targetSemanticElementId ||
                !current.EditorState.Selection.IsEmpty ||
                !ContainsSemanticSceneTarget(current, targetSemanticElementId))
            {
                return null;
            }

            var semanticDeletionAction = TryCreateDeletionRequest(
                    session,
                    current,
                    targetSemanticElementId,
                    out var semanticRequest) &&
                semanticRequest is not null
                    ? ResolveDeletionAction(deletionCatalog, semanticRequest)
                    : null;
            var semanticViewActions = ImmutableArray<SemanticSceneViewActionDefinition>.Empty;
            var semanticCommandActions =
                ImmutableArray<SemanticSceneCommandActionDefinition>.Empty;
            var targetItem = current.CurrentScene.Items.FirstOrDefault(
                item => item.Id == semanticTargetSceneObjectId &&
                    item.Origin.SemanticElementId == targetSemanticElementId &&
                    item.Origin.VisualStateId is null &&
                    item.IsVisible &&
                    item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                    Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item));
            if (targetItem is not null &&
                session.TryCaptureDocumentSnapshot(out var semanticDocument) &&
                semanticDocument is not null &&
                semanticDocument.DocumentId == current.DocumentId &&
                semanticDocument.Revision == current.DocumentRevision)
            {
                var semanticViewRequest = new SemanticSceneViewActionRequest(
                    semanticDocument,
                    current.ActiveScopeId,
                    current.ModelProfileViewState,
                    current.ModelProfileElementViewState,
                    targetItem);
                semanticViewActions =
                [
                    .. semanticSceneViewActionCatalog.Definitions.Where(action =>
                        IsSemanticSceneViewActionApplicable(action, semanticViewRequest)),
                ];
                var semanticCommandRequest = new SemanticSceneCommandActionRequest(
                    semanticDocument,
                    current.ActiveScopeId,
                    targetItem);
                semanticCommandActions =
                [
                    .. semanticSceneCommandActionCatalog.Definitions.Where(action =>
                        IsSemanticSceneCommandActionApplicable(
                            action,
                            semanticCommandRequest)),
                ];
            }

            return DocumentCanvasContextMenuState.CreateSemanticClamped(
                cssPoint,
                surfaceSize.Value,
                targetSemanticElementId,
                semanticTargetSceneObjectId,
                semanticDeletionAction,
                result.SessionState.DocumentRevision,
                result.SessionState.Generation,
                result.SessionState.CurrentScene!,
                current.ActiveScopeId,
                semanticViewActions,
                semanticCommandActions,
                result.HitResult?.SpatialRegion);
        }

        if (targetVisualStateId is not { } visualTarget ||
            result.TargetId is not { } visualTargetSceneObjectId ||
            !(current.EditorState.Selection.Contains(visualTarget) &&
              GetSpatialRegionId(current, visualTarget) ==
                  result.HitResult?.SpatialRegion?.Id &&
              ContainsPersistentVisualTarget(
                  current,
                  visualTarget,
                  result.HitResult?.SpatialRegion)))
        {
            return null;
        }

        var deletionAction = TryCreateDeletionRequest(
                session,
                current,
                visualTarget,
                out var request) &&
            request is not null
                ? ResolveDeletionAction(deletionCatalog, request)
                : null;
        var scopeNavigationAction = session.TryCaptureDocumentSnapshot(out var document) &&
            document is not null &&
            document.DocumentId == current.DocumentId &&
            document.Revision == current.DocumentRevision &&
            TryResolveScopeNavigationAction(
                scopeNavigationCatalog,
                document,
                current.ActiveScopeId,
                visualTarget,
                out var resolvedScopeNavigationAction)
                ? resolvedScopeNavigationAction
                : null;
        return DocumentCanvasContextMenuState.CreateClamped(
            cssPoint,
            surfaceSize.Value,
            visualTarget,
            visualTargetSceneObjectId,
            result.ConnectorRouteContextAction,
            result.SessionState.DocumentRevision,
            result.SessionState.Generation,
            result.SessionState.CurrentScene!,
            result.ConnectorAnchorContextAction,
            result.NodeLabelContextAction,
            deletionAction,
            scopeNavigationAction,
            current.ActiveScopeId,
            current.ActiveScopeId,
            result.HitResult?.SpatialRegion);
    }

    private static bool TryResolveScopeNavigationAction(
        ScopeNavigationCatalog catalog,
        DocumentSnapshot document,
        DocumentScopeId owningScopeId,
        VisualStateId visualStateId,
        out DocumentCanvasScopeNavigationContextAction? action)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(owningScopeId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        action = null;
        if (!document.VisualModel.TryGetVisualState(visualStateId, out var visual) ||
            visual is null ||
            !document.SemanticModel.TryGetElement(
                visual.SemanticElementId,
                out var owner) ||
            owner is null ||
            document.SemanticModel.GetScope(owner.Id).Id != owningScopeId ||
            !catalog.TryGetRegistration(owner.TypeId, out var registration) ||
            registration is null)
        {
            return false;
        }

        DocumentScopeId? targetScopeId;
#pragma warning disable CA1031 // A faulty optional contribution cannot fault generic UI input.
        try
        {
            if (!registration.Contribution.TryResolveTargetScope(
                    document,
                    owner,
                    out targetScopeId) ||
                targetScopeId is null)
            {
                return false;
            }
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
            return false;
        }
#pragma warning restore CA1031

        var targetScope = document.SemanticModel.NestedScopes.FirstOrDefault(
            scope => scope.Id == targetScopeId);
        if (targetScope is null ||
            targetScope.ParentScopeId != owningScopeId ||
            targetScope.OwnerSemanticElementId != owner.Id)
        {
            return false;
        }

        action = new DocumentCanvasScopeNavigationContextAction(
            owner.Id,
            owner.TypeId,
            visualStateId,
            targetScope.Id,
            registration.OpenActionLabel.Trim());
        return true;
    }

    private static DocumentCanvasDeletionContextAction? ResolveDeletionAction(
        DiagramDeletionCatalog deletionCatalog,
        DiagramDeletionRequest request)
    {
        var matching = deletionCatalog.GetMatchingRegistrations(request);
        return matching.Length == 1
            ? new DocumentCanvasDeletionContextAction(
                matching[0].DeletionId,
                request.TargetKind,
                request.SemanticId,
                request.VisualStateId)
            : null;
    }

    private static bool TryCreateDeletionRequest(
        EditingSession session,
        EditingSessionState state,
        VisualStateId targetVisualStateId,
        out DiagramDeletionRequest? request)
    {
        request = null;
        return session.TryCaptureDocumentSnapshot(out var document) &&
            document is not null &&
            document.Revision == state.DocumentRevision &&
            TryCreateDeletionRequest(document, targetVisualStateId, out request);
    }

    private static bool TryCreateDeletionRequest(
        EditingSession session,
        EditingSessionState state,
        SemanticElementId targetSemanticElementId,
        out DiagramDeletionRequest? request)
    {
        request = null;
        return state.EditorState.Selection.IsEmpty &&
            state.EditorState.SemanticSceneSelection == targetSemanticElementId &&
            ContainsSemanticSceneTarget(state, targetSemanticElementId) &&
            session.TryCaptureDocumentSnapshot(out var document) &&
            document is not null &&
            document.Revision == state.DocumentRevision &&
            document.SemanticModel.TryGetElement(targetSemanticElementId, out var element) &&
            element is not null &&
            (request = new DiagramDeletionRequest(
                document,
                document.Revision,
                DiagramDeletionTargetKind.Element,
                targetSemanticElementId,
                visualStateId: null)) is not null;
    }

    private static bool TryCreateDeletionRequest(
        DocumentSnapshot document,
        VisualStateId targetVisualStateId,
        out DiagramDeletionRequest? request)
    {
        request = null;
        if (!document.VisualModel.TryGetVisualState(targetVisualStateId, out var visual) ||
            visual is null)
        {
            return false;
        }

        DiagramDeletionTargetKind targetKind;
        if (document.SemanticModel.TryGetElement(visual.SemanticElementId, out _))
        {
            targetKind = DiagramDeletionTargetKind.Element;
        }
        else if (document.SemanticModel.TryGetRelationship(visual.SemanticElementId, out _))
        {
            targetKind = DiagramDeletionTargetKind.Connection;
        }
        else
        {
            return false;
        }

        request = new DiagramDeletionRequest(
            document,
            document.Revision,
            targetKind,
            visual.SemanticElementId,
            visual.Id);
        return true;
    }

    private static bool TryCreateDeletionRequest(
        DocumentSnapshot document,
        SemanticElementId targetSemanticElementId,
        out DiagramDeletionRequest? request)
    {
        request = null;
        if (!document.SemanticModel.TryGetElement(targetSemanticElementId, out var element) ||
            element is null)
        {
            return false;
        }

        request = new DiagramDeletionRequest(
            document,
            document.Revision,
            DiagramDeletionTargetKind.Element,
            targetSemanticElementId,
            visualStateId: null);
        return true;
    }

    private static ConnectorAnchorId CreateConnectorAnchorId(
        DocumentId documentId,
        DocumentRevision revision,
        VisualStateId visualStateId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex) =>
        new(FormattableString.Invariant(
            $"inceptus:connector-anchor:{documentId.Value.Length}:{documentId.Value}:{visualStateId.Value.Length}:{visualStateId.Value}:{revision.Value}:{side}:{role}:{insertionIndex}"));

    private static DocumentCanvasPropertiesApplyResult CreateApplyFailure(
        DocumentCanvasPropertiesApplyStatus status,
        string diagnosticCode,
        string message) =>
        new(
            status,
            null,
            [new Diagnostic(diagnosticCode, DiagnosticSeverity.Warning, message)],
            message);

    private static DocumentCanvasModelViewPropertiesApplyResult
        CreateModelViewApplyFailure(
            DocumentCanvasModelViewPropertiesApplyStatus status,
            string code,
            string message) =>
        new(
            status,
            null,
            [
                new Diagnostic(
                    code,
                    DiagnosticSeverity.Warning,
                    message,
                    "model-view-properties"),
            ],
            message);

    private void ClearModelViewPropertiesFormUnderLock()
    {
        _modelViewPropertiesFormOpen = false;
        _modelViewPropertiesFormDirty = false;
        _modelViewPropertiesApplyInFlight = false;
        _modelViewPropertiesScopeId = null;
    }

    private static Canvas2DRendererConfiguration CreateRendererConfiguration() =>
        new(
            fontResources:
            [
                new Canvas2DFontResource(
                    ModelerFontIdentity,
                    ModelerFontVersion,
                    ModelerFontFamily,
                    ModelerFontUri,
                    400,
                    TextFontStyle.Normal),
            ],
            defaultFontFamily: ModelerFontFamily);

    private static EditingSessionConfiguration WithVisibleDocumentRegion(
        EditingSessionConfiguration source,
        Canvas2DSurfaceSize surfaceSize,
        BpmnModelerDocumentNotificationSource? notificationSource = null)
    {
        var initialEditorState = source.InitialEditorState;
        var visibleRegion = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
            initialEditorState.Viewport,
            surfaceSize);
        var viewport = new ViewportSnapshot(
            initialEditorState.Viewport.Zoom,
            initialEditorState.Viewport.Pan,
            visibleRegion);
        return new EditingSessionConfiguration(
            source.ProjectionEngine,
            source.LayoutEngine,
            source.LayoutAlgorithmId,
            source.RoutingEngine,
            source.RoutingAlgorithmId,
            source.SceneBuilder,
            source.ProjectionContext,
            source.LayoutContext,
            source.RoutingContext,
            CopyEditorStateWithViewport(initialEditorState, viewport),
            source.CommandHandlers,
            source.CommandValidators,
            source.HistoryPolicies,
            notificationSource is null
                ? source.DocumentChangedSubscribers
                : source.DocumentChangedSubscribers.Append(notificationSource),
            source.ConnectorAnchorPolicyProvider,
            source.ModelProfileCatalog,
            source.InitialModelProfileViewState);
    }

    private static EditorStateSnapshot CopyEditorStateWithViewport(
        EditorStateSnapshot source,
        ViewportSnapshot viewport) =>
        new(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState,
            source.SemanticSceneSelection);

    private static bool HasError(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static bool IsValidationContextAvailable(EditingSessionState state) =>
        !state.IsClosed &&
        state.Status == EditingSessionStatus.Ready &&
        state.CurrentScene is not null &&
        state.ProjectedGraph is not null &&
        state.LayoutResult is not null &&
        state.RoutingResult is not null &&
        state.EditorState.ActiveGesture is null;

    private static bool IsSameValidationContext(
        EditingSessionState observed,
        EditingSessionState confirmed) =>
        IsValidationContextAvailable(confirmed) &&
        observed.DocumentId == confirmed.DocumentId &&
        observed.DocumentRevision == confirmed.DocumentRevision &&
        observed.ActiveScopeId == confirmed.ActiveScopeId &&
        observed.Generation == confirmed.Generation &&
        ReferenceEquals(observed.CurrentScene, confirmed.CurrentScene) &&
        ReferenceEquals(observed.ProjectedGraph, confirmed.ProjectedGraph) &&
        ReferenceEquals(observed.LayoutResult, confirmed.LayoutResult) &&
        ReferenceEquals(observed.RoutingResult, confirmed.RoutingResult);

    private static bool TryResolveValidationSelectionCssPoint(
        EditingSessionState state,
        VisualStateId visualStateId,
        out PointD cssPoint)
    {
        var item = state.CurrentScene?.Items
            .Where(candidate =>
                candidate.Origin.VisualStateId == visualStateId &&
                (candidate.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                candidate.IsVisible &&
                candidate.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            .OrderBy(static candidate => candidate.Layer switch
            {
                Canvas2DSceneLayer.Content => 0,
                Canvas2DSceneLayer.Connector => 1,
                Canvas2DSceneLayer.Label => 2,
                _ => 3,
            })
            .ThenByDescending(static candidate => candidate.ZIndex)
            .ThenBy(static candidate => candidate.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (item is null || state.CurrentScene is null)
        {
            cssPoint = default;
            return false;
        }

        var documentPoint = item.Geometry.Kind == Canvas2DSceneGeometryKind.Path
            ? MidpointOfLongestSegment(
                item.Geometry.Points
                    .Select(item.Transform.TransformPoint))
            : new PointD(
                item.Bounds.Left + (item.Bounds.Width / 2d),
                item.Bounds.Top + (item.Bounds.Height / 2d));
        cssPoint = state.CurrentScene.ViewportTransform.TransformPoint(documentPoint);
        return double.IsFinite(cssPoint.X) && double.IsFinite(cssPoint.Y);
    }

    private static PointD MidpointOfLongestSegment(IEnumerable<PointD> points)
    {
        var path = points.ToArray();
        if (path.Length < 2)
        {
            return default;
        }

        var start = path[0];
        var end = path[1];
        var maximumLengthSquared = -1d;
        for (var index = 1; index < path.Length; index++)
        {
            var candidateStart = path[index - 1];
            var candidateEnd = path[index];
            var delta = candidateEnd - candidateStart;
            var lengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
            if (lengthSquared > maximumLengthSquared)
            {
                maximumLengthSquared = lengthSquared;
                start = candidateStart;
                end = candidateEnd;
            }
        }

        return new PointD((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);
    }

    private void RemoveHostDiagnosticUnderLock(string code) =>
        _hostDiagnostics = _hostDiagnostics
            .Where(diagnostic => !StringComparer.Ordinal.Equals(diagnostic.Code, code))
            .ToImmutableArray();

    private void RecordHostFailure(string code, string message, Exception exception)
    {
        lock (_sync)
        {
            _hostDiagnostics =
            [
                new Diagnostic(
                    code,
                    DiagnosticSeverity.Error,
                    message,
                    exception.GetType().FullName ?? exception.GetType().Name),
            ];
        }
    }

    private ImmutableArray<DocumentCanvasScopeBreadcrumbSegment>
        CaptureScopeBreadcrumbUnderLock(EditingSessionState? state)
    {
        var session = _session;
        if (session is null || state is null || state.IsClosed ||
            !session.TryCaptureDocumentSnapshot(out var document) ||
            document is null ||
            document.DocumentId != state.DocumentId ||
            document.Revision != state.DocumentRevision ||
            !ScopeExists(document, state.ActiveScopeId))
        {
            return DocumentCanvasScopeBreadcrumb.Empty;
        }

        var scopes = document.SemanticModel.GetAncestors(state.ActiveScopeId)
            .Reverse()
            .Append(state.ActiveScopeId == document.SemanticModel.RootScopeId
                ? document.SemanticModel.GetRootScope()
                : document.SemanticModel.NestedScopes.Single(
                    scope => scope.Id == state.ActiveScopeId));
        var segments = ImmutableArray.CreateBuilder<DocumentCanvasScopeBreadcrumbSegment>();
        foreach (var scope in scopes)
        {
            var label = ResolveScopeBreadcrumbLabel(document, scope, out var labelResourceKey);
            segments.Add(new DocumentCanvasScopeBreadcrumbSegment(
                scope.Id,
                label,
                scope.Id == state.ActiveScopeId,
                labelResourceKey));
        }

        return segments.ToImmutable();
    }

    private string ResolveScopeBreadcrumbLabel(
        DocumentSnapshot document,
        DocumentScopeSnapshot scope,
        out string? labelResourceKey)
    {
        labelResourceKey = "Navigation_Scope";
        if (scope.Id == document.SemanticModel.RootScopeId)
        {
            labelResourceKey = "Navigation_MainProcess";
            return DocumentCanvasScopeBreadcrumb.RootLabel;
        }

        if (scope.OwnerSemanticElementId is not { } ownerId ||
            !document.SemanticModel.TryGetElement(ownerId, out var owner) ||
            owner is null ||
            !_scopeNavigationCatalog.TryGetRegistration(owner.TypeId, out var registration) ||
            registration is null)
        {
            return DocumentCanvasScopeBreadcrumb.FallbackNestedLabel;
        }

#pragma warning disable CA1031 // A presentation-label contribution cannot fault the host.
        try
        {
            var label = registration.Contribution.ResolveBreadcrumbLabel(document, owner);
            if (!string.IsNullOrWhiteSpace(label))
            {
                labelResourceKey = ModelerLabels.ScopeFallbackKey(owner);
            }
            return string.IsNullOrWhiteSpace(label)
                ? DocumentCanvasScopeBreadcrumb.FallbackNestedLabel
                : label.Trim();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
            return DocumentCanvasScopeBreadcrumb.FallbackNestedLabel;
        }
#pragma warning restore CA1031
    }

    private static bool ScopeExists(
        DocumentSnapshot document,
        DocumentScopeId scopeId) =>
        scopeId == document.SemanticModel.RootScopeId ||
        document.SemanticModel.NestedScopes.Any(scope => scope.Id == scopeId);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private sealed record ViewportPanGesture(
        long PointerId,
        long CaptureGeneration,
        PointD LastCssPoint);
}

internal sealed record DocumentCanvasHostState(
    EditingSessionState? Session,
    Canvas2DSurfaceSize? SurfaceSize,
    ImmutableArray<Diagnostic> HostDiagnostics,
    ImmutableArray<Diagnostic> InteractionDiagnostics,
    IDocumentCanvasPipelineCounters? PipelineCounters,
    bool InitializationAttempted,
    bool IsInitialized,
    bool IsDisposed,
    int SuccessfulRenderCount,
    bool LatestPresentationSucceeded,
    string CssCursor,
    DocumentCanvasContextMenuState? ContextMenu,
    VisualStateId? PropertiesTargetVisualStateId,
    bool PropertiesFormOpen,
    bool PropertiesFormDirty,
    bool PropertiesApplyInFlight,
    ValidationSnapshot? ValidationSnapshot,
    bool ValidationInFlight,
    ImmutableArray<DocumentCanvasScopeBreadcrumbSegment> ScopeBreadcrumb,
    bool ModelViewPropertiesFormOpen = false,
    bool ModelViewPropertiesFormDirty = false,
    bool ModelViewPropertiesApplyInFlight = false,
    SemanticElementId? PropertiesTargetSemanticElementId = null,
    string? ActiveCanvasElementId = null,
    DocumentPublicationSnapshot? Publication = null,
    long DocumentSessionVersion = 0);
