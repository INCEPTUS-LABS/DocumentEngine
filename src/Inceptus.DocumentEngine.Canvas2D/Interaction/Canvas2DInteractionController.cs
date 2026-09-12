using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using EditingSessionRuntime = Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSession;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Interprets normalized Canvas2D pointer input as transient Editor State updates.
/// </summary>
public sealed partial class Canvas2DInteractionController : IAsyncDisposable
{
    /// <summary>
    /// Gets the minimum supported width or height, in logical document units, for an
    /// interactively resized visual.
    /// </summary>
    public const double MinimumVisualExtent = 1d;

    private static readonly double MaximumInteractiveMagnitude =
        Math.Sqrt(double.MaxValue) / 16d;
    private const double MoveActivationThreshold = 3d;
    private readonly EditingSessionRuntime _session;
    private readonly Canvas2DSceneHitTestService _hitTestService;
    private readonly AnchorConnectionCreationCatalog _connectionCreationCatalog;
    private readonly ConnectorEndpointReconnectionCatalog _endpointReconnectionCatalog;
    private readonly IDocumentCreationIdentityProvider? _creationIdentityProvider;
    private readonly Canvas2DSpatialEditPlannerCatalog _spatialEditPlanners;
    private readonly object _disposalSync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private PersistentGestureState? _persistentGesture;
    private PendingPointerState? _pendingPointer;
    private bool _suppressNextActivation;
    private Task? _disposeTask;
    private int _disposed;

    public Canvas2DInteractionController(
        EditingSessionRuntime session,
        Canvas2DSceneHitTestService? hitTestService = null,
        AnchorConnectionCreationCatalog? connectionCreationCatalog = null,
        IDocumentCreationIdentityProvider? creationIdentityProvider = null,
        ConnectorEndpointReconnectionCatalog? endpointReconnectionCatalog = null,
        Canvas2DSpatialEditPlannerCatalog? spatialEditPlanners = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _hitTestService = hitTestService ?? new Canvas2DSceneHitTestService();
        _connectionCreationCatalog = connectionCreationCatalog ??
            AnchorConnectionCreationCatalog.Empty;
        _endpointReconnectionCatalog = endpointReconnectionCatalog ??
            ConnectorEndpointReconnectionCatalog.Empty;
        if (!_connectionCreationCatalog.Registrations.IsEmpty)
        {
            ArgumentNullException.ThrowIfNull(creationIdentityProvider);
        }

        _creationIdentityProvider = creationIdentityProvider;
        _spatialEditPlanners = spatialEditPlanners ?? Canvas2DSpatialEditPlannerCatalog.Empty;
        _lifetimeToken = _lifetime.Token;
    }

    public ValueTask<Canvas2DInteractionResult> PointerMovedAsync(
        PointD cssPoint,
        CancellationToken cancellationToken = default) =>
        ExecutePointInteractionAsync(cssPoint, InteractionKind.Hover, cancellationToken);

    public ValueTask<Canvas2DInteractionResult> PointerPressedAsync(
        Canvas2DPointerInput input,
        CancellationToken cancellationToken = default) =>
        ExecutePointerPressedAsync(input, cancellationToken);

    public ValueTask<Canvas2DInteractionResult> PointerMovedAsync(
        Canvas2DPointerInput input,
        CancellationToken cancellationToken = default) =>
        ExecuteNormalizedPointerMoveAsync(input, cancellationToken);

    public ValueTask<Canvas2DInteractionResult> PointerReleasedAsync(
        Canvas2DPointerInput input,
        CancellationToken cancellationToken = default) =>
        ExecutePointerReleasedAsync(input, cancellationToken);

    public ValueTask<Canvas2DInteractionResult> PointerCancelledAsync(
        long pointerId,
        CancellationToken cancellationToken = default) =>
        ExecutePointerCancelledAsync(pointerId, cancellationToken);

    /// <summary>
    /// Cancels the transient pointer gesture currently owned by this controller. This is the
    /// managed Escape-key boundary; it never executes persistent work.
    /// </summary>
    public ValueTask<Canvas2DInteractionResult> CancelActiveGestureAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteCancelActiveGestureAsync(cancellationToken);

    public ValueTask<Canvas2DInteractionResult> PointerActivatedAsync(
        PointD cssPoint,
        CancellationToken cancellationToken = default) =>
        ExecutePointInteractionAsync(cssPoint, InteractionKind.Selection, cancellationToken);

    /// <summary>
    /// Resolves one canvas context-menu request through the canonical viewport conversion,
    /// Scene hit test, and logical Visual State selection path.
    /// </summary>
    public ValueTask<Canvas2DInteractionResult> PointerContextMenuAsync(
        PointD cssPoint,
        CancellationToken cancellationToken = default) =>
        ExecutePointInteractionAsync(cssPoint, InteractionKind.ContextMenu, cancellationToken);

    /// <summary>
    /// Selects an interactable semantic-only Scene target through the same transient
    /// interaction boundary used by pointer selection.
    /// </summary>
    public ValueTask<Canvas2DInteractionResult> SelectSemanticSceneTargetAsync(
        SemanticElementId semanticElementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        return ExecuteSemanticSceneSelectionAsync(semanticElementId, cancellationToken);
    }

    /// <summary>
    /// Resolves an explicit node-body activation through canonical viewport conversion and
    /// Scene hit testing. Decorations, labels, handles, anchors, and connectors are excluded.
    /// </summary>
    public ValueTask<Canvas2DInteractionResult> PointerNodeBodyActivatedAsync(
        PointD cssPoint,
        CancellationToken cancellationToken = default) =>
        ExecutePointInteractionAsync(
            cssPoint,
            InteractionKind.NodeBodyActivation,
            cancellationToken);

    public ValueTask<Canvas2DInteractionResult> PointerLeftAsync(
        CancellationToken cancellationToken = default) =>
        ExecutePointerLeaveAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (_disposalSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref _disposed, 1);
        _lifetime.Cancel();
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_persistentGesture is { } gesture)
            {
                _persistentGesture = null;
                _ = await _session.ClearPersistentGestureFromInteractionAsync(
                    gesture.GestureId,
                    CancellationToken.None).ConfigureAwait(false);
            }

            _pendingPointer = null;
        }
        finally
        {
            _operationGate.Release();
        }

        _lifetime.Dispose();
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointInteractionAsync(
        PointD cssPoint,
        InteractionKind kind,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        var entered = await TryEnterAsync(linked.Token).ConfigureAwait(false);
        if (!entered)
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            return await ExecutePointInteractionUnderGateAsync(
                cssPoint,
                kind,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult> ExecuteSemanticSceneSelectionAsync(
        SemanticElementId semanticElementId,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        if (!await TryEnterAsync(linked.Token).ConfigureAwait(false))
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            var observed = _session.CaptureState();
            if (!observed.IsGraphicalInteractionEnabled || observed.CurrentScene is null)
            {
                return UnavailableResult(observed);
            }

            var target = observed.CurrentScene.Items.FirstOrDefault(item =>
                item.Origin.SemanticElementId == semanticElementId &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item));
            if (target is null)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed);
            }

            var updated = WithPlainSelection(
                observed.EditorState,
                selectedVisualStateId: null,
                semanticElementId);
            return updated.Equals(observed.EditorState)
                ? new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed)
                : await ApplyEditorStateAsync(
                    observed,
                    updated,
                    hitResult: null,
                    linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointInteractionUnderGateAsync(
        PointD cssPoint,
        InteractionKind kind,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return DisposedResult();
        }

        if (kind == InteractionKind.Selection && _suppressNextActivation)
        {
            _suppressNextActivation = false;
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                _session.CaptureState());
        }

        var observed = _session.CaptureState();
        if (!observed.IsGraphicalInteractionEnabled || observed.CurrentScene is null)
        {
            return UnavailableResult(observed);
        }

        if ((kind is InteractionKind.ContextMenu or InteractionKind.NodeBodyActivation) &&
            (_pendingPointer is not null ||
             _persistentGesture is not null ||
             observed.EditorState.ActiveGesture is not null))
        {
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                "An object action cannot activate while a pointer interaction is active.",
                observed);
        }

        if (!TryConvertPoint(observed, cssPoint, out var documentPoint, out var failure))
        {
            return failure!;
        }

        Canvas2DSceneHitTestResult? hit;
        try
        {
            hit = _hitTestService.HitTest(observed.CurrentScene, documentPoint);
        }
#pragma warning disable CA1031 // Malformed transient geometry becomes a stable interaction diagnostic.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                "The current Canvas2DScene could not be hit tested.",
                exception);
        }
#pragma warning restore CA1031

        if (kind == InteractionKind.NodeBodyActivation)
        {
            var hitItem = hit is null
                ? null
                : observed.CurrentScene.Items.FirstOrDefault(
                    item => item.Id == hit.SceneObjectId);
            if (hitItem is null ||
                !IsEditableVisualPresentation(hitItem) ||
                !IsUnobscuredCanonicalNodeBodyActivation(
                    observed.CurrentScene,
                    hitItem,
                    documentPoint))
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed);
            }
        }

        var targetId = hit?.SceneObjectId;
        var resolvedHitItem = hit is null
            ? null
            : observed.CurrentScene.Items.FirstOrDefault(item => item.Id == hit.SceneObjectId);
        var permitsVisualActions = resolvedHitItem is null ||
            resolvedHitItem.Origin.VisualStateId is null ||
            IsEditableVisualPresentation(resolvedHitItem);
        var cssCursor = kind == InteractionKind.Hover
            ? CssCursor(observed.CurrentScene, hit)
            : "default";
        var resultHit = hit;
        var connectorRouteContextAction = kind == InteractionKind.ContextMenu && permitsVisualActions
            ? ResolveConnectorRouteContextAction(observed.CurrentScene, hit)
            : null;
        var connectorAnchorContextAction = kind == InteractionKind.ContextMenu && permitsVisualActions
            ? ResolveConnectorAnchorContextAction(observed, hit)
            : null;
        var nodeLabelContextAction = kind == InteractionKind.ContextMenu && permitsVisualActions
            ? ResolveNodeLabelContextAction(observed.CurrentScene, hit)
            : null;
        EditorStateSnapshot updated;
        if (kind == InteractionKind.Hover)
        {
            updated = WithHover(observed.EditorState, targetId);
        }
        else
        {
            var selectedVisualStateId = ResolveSelectableVisualStateId(
                observed.CurrentScene,
                hit);
            var selectedSemanticElementId = selectedVisualStateId is null
                ? ResolveSelectableSemanticElementId(observed.CurrentScene, hit)
                : null;
            if (kind == InteractionKind.ContextMenu)
            {
                resultHit = selectedVisualStateId is null && selectedSemanticElementId is null
                    ? null
                    : hit;
                updated = selectedVisualStateId is null && selectedSemanticElementId is null
                    ? observed.EditorState
                    : (selectedVisualStateId is not null &&
                           observed.EditorState.Selection.Contains(selectedVisualStateId)) ||
                    (selectedSemanticElementId is not null &&
                     observed.EditorState.SemanticSceneSelection == selectedSemanticElementId)
                        ? observed.EditorState
                        : WithPlainSelection(
                            observed.EditorState,
                            selectedVisualStateId,
                            selectedSemanticElementId);
            }
            else
            {
                updated = WithPlainSelection(
                    observed.EditorState,
                    selectedVisualStateId,
                    selectedSemanticElementId);
            }
        }

        if (updated.Equals(observed.EditorState))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                observed,
                resultHit,
                cssCursor: cssCursor,
                connectorRouteContextAction: connectorRouteContextAction,
                connectorAnchorContextAction: connectorAnchorContextAction,
                nodeLabelContextAction: nodeLabelContextAction);
        }

        return await ApplyEditorStateAsync(
            observed,
            updated,
            resultHit,
            cancellationToken,
            cssCursor,
            connectorRouteContextAction,
            connectorAnchorContextAction,
            nodeLabelContextAction).ConfigureAwait(false);
    }

    private static bool IsUnobscuredCanonicalNodeBodyActivation(
        Canvas2DScene scene,
        Canvas2DSceneItem hitItem,
        PointD documentPoint)
    {
        if (!Canvas2DNodeBodyMetadata.IsNodeBody(hitItem))
        {
            return false;
        }

        var bodyIndex = scene.Items.IndexOf(hitItem);
        if (bodyIndex < 0)
        {
            return false;
        }

        for (var index = bodyIndex + 1; index < scene.Items.Length; index++)
        {
            var overlay = scene.Items[index];
            if (overlay.IsVisible &&
                overlay.Layer == Canvas2DSceneLayer.Decoration &&
                (overlay.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                overlay.Origin.VisualStateId == hitItem.Origin.VisualStateId &&
                !Canvas2DNodeBodyMetadata.IsNodeBody(overlay) &&
                TransformedGeometryBounds(overlay).Contains(documentPoint) &&
                (overlay.Clip is not { } clip || clip.Contains(documentPoint)))
            {
                return false;
            }
        }

        return true;
    }

    private static RectD TransformedGeometryBounds(Canvas2DSceneItem item)
    {
        var local = item.Geometry.Bounds;
        var topLeft = item.Transform.TransformPoint(local.TopLeft);
        var topRight = item.Transform.TransformPoint(new PointD(local.Right, local.Top));
        var bottomLeft = item.Transform.TransformPoint(new PointD(local.Left, local.Bottom));
        var bottomRight = item.Transform.TransformPoint(new PointD(local.Right, local.Bottom));
        var minimumX = Math.Min(
            Math.Min(topLeft.X, topRight.X),
            Math.Min(bottomLeft.X, bottomRight.X));
        var minimumY = Math.Min(
            Math.Min(topLeft.Y, topRight.Y),
            Math.Min(bottomLeft.Y, bottomRight.Y));
        var maximumX = Math.Max(
            Math.Max(topLeft.X, topRight.X),
            Math.Max(bottomLeft.X, bottomRight.X));
        var maximumY = Math.Max(
            Math.Max(topLeft.Y, topRight.Y),
            Math.Max(bottomLeft.Y, bottomRight.Y));
        return new RectD(
            minimumX,
            minimumY,
            maximumX - minimumX,
            maximumY - minimumY);
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointerLeaveAsync(
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        var entered = await TryEnterAsync(linked.Token).ConfigureAwait(false);
        if (!entered)
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return DisposedResult();
            }

            if (_persistentGesture is { } activeGesture)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    _session.CaptureState(),
                    cssCursor: CssCursor(activeGesture));
            }

            _pendingPointer = null;

            var observed = _session.CaptureState();
            var updated = WithHover(observed.EditorState, hoveredObjectId: null);
            if (updated.Equals(observed.EditorState))
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed);
            }

            return await ClearHoverAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointerPressedAsync(
        Canvas2DPointerInput input,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        if (!await TryEnterAsync(linked.Token).ConfigureAwait(false))
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return DisposedResult();
            }

            if (!input.IsPrimary || input.Button != 0)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    _session.CaptureState());
            }

            var observed = _session.CaptureState();
            _suppressNextActivation = false;
            if (_persistentGesture is { } existingGesture)
            {
                if (IsCurrentGesture(observed, existingGesture))
                {
                    return GestureUnavailableResult(
                        Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                        "Only one persistent Canvas2D gesture may be active at a time.");
                }

                _persistentGesture = null;
                var reconciled = await _session.ClearPersistentGestureFromInteractionAsync(
                    existingGesture.GestureId,
                    CancellationToken.None).ConfigureAwait(false);
                observed = reconciled.State;
            }

            if (_pendingPointer is { } existingPending)
            {
                if (IsCurrentPendingPointer(observed, existingPending))
                {
                    return GestureUnavailableResult(
                        Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                        "Only one Canvas2D pointer activation may be pending at a time.");
                }

                _pendingPointer = null;
            }

            if (!observed.IsGraphicalInteractionEnabled || observed.CurrentScene is null)
            {
                return UnavailableResult(observed);
            }

            if (observed.EditorState.ActiveGesture is not null)
            {
                return GestureUnavailableResult(
                    Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                    "Editor State already contains an active gesture.");
            }

            if (!TryConvertPoint(
                    observed,
                    input.CssPoint,
                    out var documentPoint,
                    out var conversionFailure))
            {
                return conversionFailure!;
            }

            Canvas2DSceneHitTestResult? hit;
            try
            {
                hit = _hitTestService.HitTest(observed.CurrentScene, documentPoint);
            }
#pragma warning disable CA1031 // Malformed transient geometry becomes a stable interaction diagnostic.
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return FailureResult(
                    observed,
                    Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                    "The current Canvas2DScene could not be hit tested for a move gesture.",
                    exception);
            }
#pragma warning restore CA1031

            if (hit is null)
            {
                _pendingPointer = new PendingPointerState(
                    input.PointerId,
                    input.ControlKey,
                    documentPoint,
                    null,
                    null,
                    null,
                    observed.CurrentScene,
                    observed.Generation,
                    observed.EditorState);
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed);
            }

            var hitItem = observed.CurrentScene.Items.Single(item => item.Id == hit.SceneObjectId);
            if (hitItem.Origin.VisualStateId is not null &&
                !IsEditableVisualPresentation(hitItem))
            {
                _pendingPointer = new PendingPointerState(
                    input.PointerId,
                    input.ControlKey,
                    documentPoint,
                    SelectedVisualStateId: null,
                    SelectedSemanticElementId: null,
                    MoveTarget: null,
                    observed.CurrentScene,
                    observed.Generation,
                    observed.EditorState);
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed,
                    hit,
                    cssCursor: "default");
            }

            var reconnectionStart = await TryStartConnectorEndpointReconnectionUnderGateAsync(
                observed,
                input,
                hit,
                hitItem,
                linked.Token).ConfigureAwait(false);
            if (reconnectionStart is not null)
            {
                return reconnectionStart;
            }

            var connectionStart = await TryStartAnchorConnectionUnderGateAsync(
                observed,
                input,
                hit,
                hitItem,
                linked.Token).ConfigureAwait(false);
            if (connectionStart is not null)
            {
                return connectionStart;
            }

            PersistentGestureKind? specializedGestureKind = null;
            var resizeDirection = Canvas2DResizeDirection.SouthEast;
            var nodeLabelOperation = Canvas2DNodeLabelGestureOperation.Move;
            Canvas2DSceneItem? nodeLabelNodeTarget = null;
            var targetItem = hitItem;
            var bendIndex = -1;
            ProjectedObjectId? labelProjectedObjectId = null;
            if (TryResolveRouteTarget(
                    observed.CurrentScene,
                    observed.EditorState,
                    hitItem,
                    out var routeTarget,
                    out bendIndex))
            {
                specializedGestureKind = PersistentGestureKind.RouteBend;
                targetItem = routeTarget;
            }
            else if (TryResolveNodeLabelTarget(
                         observed.CurrentScene,
                         observed.EditorState,
                         hitItem,
                         out var nodeLabelTarget,
                         out var ownerNodeTarget,
                         out labelProjectedObjectId,
                         out nodeLabelOperation,
                         out resizeDirection))
            {
                specializedGestureKind = PersistentGestureKind.NodeLabelEdit;
                targetItem = nodeLabelTarget;
                nodeLabelNodeTarget = ownerNodeTarget;
            }
            else if (TryResolveResizeTarget(
                    observed.CurrentScene,
                    observed.EditorState,
                    hitItem,
                    out var resizeTarget,
                    out resizeDirection))
            {
                specializedGestureKind = PersistentGestureKind.Resize;
                targetItem = resizeTarget;
            }
            else if (TryResolveLabelTarget(
                         observed.CurrentScene,
                         observed.EditorState,
                         hitItem,
                         out var labelConnectorTarget,
                         out labelProjectedObjectId))
            {
                specializedGestureKind = PersistentGestureKind.LabelMove;
                targetItem = labelConnectorTarget;
            }

            if (specializedGestureKind is null)
            {
                var selectedVisualStateId = ResolveSelectableVisualStateId(
                    observed.CurrentScene,
                    hit);
                var selectedSemanticElementId = selectedVisualStateId is null
                    ? ResolveSelectableSemanticElementId(observed.CurrentScene, hit)
                    : null;
                var moveTarget = selectedVisualStateId is null ||
                    IsConnectorEndpointHandle(hitItem) ||
                    IsConnectorAnchorHandle(hitItem)
                    ? null
                    : ResolveCanonicalSceneTarget(
                        observed.CurrentScene,
                        selectedVisualStateId,
                        requireMoveCapability: true);
                _pendingPointer = new PendingPointerState(
                    input.PointerId,
                    input.ControlKey,
                    documentPoint,
                    selectedVisualStateId,
                    selectedSemanticElementId,
                    moveTarget,
                    observed.CurrentScene,
                    observed.Generation,
                    observed.EditorState);
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed,
                    hit,
                    cssCursor: CssCursor(observed.CurrentScene, hit));
            }

            var gestureKind = specializedGestureKind.Value;

            if (targetItem.Origin.VisualStateId is not { } visualStateId)
            {
                return GestureUnavailableResult(
                    Canvas2DInteractionDiagnosticCodes.InvalidGestureTarget,
                    "The persistent gesture target does not trace to a Visual State.",
                    observed,
                    hit);
            }

            var visualState = _session.CaptureVisualStateForInteraction(
                observed.CurrentScene,
                observed.Generation,
                observed.EditorState,
                visualStateId);
            if (visualState is null)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Stale,
                    _session.CaptureState(),
                    hit,
                    [Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        DiagnosticSeverity.Error,
                        "The persistent gesture target no longer belongs to the current Document revision.")]);
            }

            if (gestureKind == PersistentGestureKind.RouteBend)
            {
                var editableRoute = targetItem.ConnectorPresentationMapping?.CanonicalEditablePath ??
                    Canvas2DConnectorPathMetadata.ResolveEditable(targetItem);
                if (visualState.Route.Length < 3 ||
                    bendIndex <= 0 ||
                    bendIndex >= visualState.Route.Length - 1 ||
                    visualState.Route.Length != editableRoute.Length ||
                    !visualState.Route.AsSpan(1, visualState.Route.Length - 2)
                        .SequenceEqual(editableRoute.AsSpan(
                            1,
                            editableRoute.Length - 2)))
                {
                    return new Canvas2DInteractionResult(
                        Canvas2DInteractionStatus.Stale,
                        _session.CaptureState(),
                        hit,
                        [Diagnostic(
                            Canvas2DInteractionDiagnosticCodes.StaleGesture,
                            DiagnosticSeverity.Error,
                            "The route bend no longer matches the current persistent Visual State.")]);
                }
            }

            ImmutableArray<PointD> originalRoute = visualState.Route;
            var originalBounds = targetItem.Bounds;
            var originalOwnerBounds = nodeLabelNodeTarget?.Bounds ?? originalBounds;
            if (gestureKind == PersistentGestureKind.LabelMove)
            {
                originalRoute = Canvas2DConnectorPathMetadata.Resolve(targetItem)
                    .Select(targetItem.Transform.TransformPoint)
                    .ToImmutableArray();
                if (originalRoute.Length < 2)
                {
                    return GestureUnavailableResult(
                        Canvas2DInteractionDiagnosticCodes.InvalidGestureTarget,
                        "The connector label does not resolve to a usable connector path.",
                        observed,
                        hit);
                }

                var placement = ConnectorLabelPlacement.Resolve(visualState.Properties);
                var anchor = Canvas2DConnectorPathGeometry.ResolvePoint(
                    originalRoute,
                    placement.PathPosition) + placement.Offset;
                originalBounds = new RectD(anchor.X, anchor.Y, 0d, 0d);
            }

            var gesturePrefix = gestureKind switch
            {
                PersistentGestureKind.Move => "move",
                PersistentGestureKind.Resize => "resize",
                PersistentGestureKind.RouteBend => "route-bend",
                PersistentGestureKind.NodeLabelEdit => "node-label",
                _ => "label-move",
            };
            var gestureId = FormattableString.Invariant(
                $"{gesturePrefix}:{observed.DocumentRevision.Value}:{input.PointerId}:{visualStateId.Value}");
            var gestureSceneObjectId = targetItem.Id;
            var gestureSnapshot = new EditorGestureSnapshot(
                gestureId,
                gestureKind switch
                {
                    PersistentGestureKind.Move => Canvas2DMoveGestureMetadata.Kind,
                    PersistentGestureKind.Resize => Canvas2DResizeGestureMetadata.Kind,
                    PersistentGestureKind.RouteBend => Canvas2DRouteGestureMetadata.Kind,
                    PersistentGestureKind.NodeLabelEdit =>
                        Canvas2DNodeLabelGestureMetadata.Kind,
                    _ => Canvas2DLabelGestureMetadata.Kind,
                },
                documentPoint,
                documentPoint,
                gestureKind switch
                {
                    PersistentGestureKind.Resize => CreateResizeProperties(
                        gestureSceneObjectId,
                        visualStateId,
                        resizeDirection),
                    PersistentGestureKind.RouteBend => CreateRouteProperties(
                        gestureSceneObjectId,
                        visualStateId,
                        bendIndex),
                    PersistentGestureKind.NodeLabelEdit => CreateNodeLabelProperties(
                        nodeLabelNodeTarget!.Id,
                        gestureSceneObjectId,
                        visualStateId,
                        labelProjectedObjectId!,
                        nodeLabelOperation,
                        resizeDirection),
                    _ => CreateLabelProperties(
                        gestureSceneObjectId,
                        visualStateId,
                        labelProjectedObjectId!),
                });
            var updatedEditorState = Copy(
                observed.EditorState,
                    gestureKind == PersistentGestureKind.NodeLabelEdit &&
                    !observed.EditorState.Selection.Contains(visualStateId)
                        ? [visualStateId]
                        : observed.EditorState.Selection,
                observed.EditorState.HoveredObjectId,
                gestureSnapshot);
            var update = await ApplyEditorStateAsync(
                observed,
                updatedEditorState,
                hit,
                linked.Token,
                gestureKind == PersistentGestureKind.Resize
                    ? Canvas2DResizeGeometry.CssCursor(resizeDirection)
                    : gestureKind == PersistentGestureKind.NodeLabelEdit &&
                        nodeLabelOperation == Canvas2DNodeLabelGestureOperation.Resize
                        ? Canvas2DResizeGeometry.CssCursor(resizeDirection)
                    : gestureKind is PersistentGestureKind.LabelMove or
                        PersistentGestureKind.NodeLabelEdit
                        ? "grabbing"
                        : "pointer").ConfigureAwait(false);
            if (update.Status == Canvas2DInteractionStatus.Updated &&
                update.SessionState.CurrentScene is { } currentScene)
            {
                _persistentGesture = new PersistentGestureState(
                    gestureKind,
                    resizeDirection,
                    input.PointerId,
                    gestureId,
                    observed.DocumentId,
                    observed.DocumentRevision,
                    gestureSceneObjectId,
                    nodeLabelNodeTarget?.Id,
                    visualState.Id,
                    originalBounds,
                    originalOwnerBounds,
                    originalRoute,
                    bendIndex,
                    labelProjectedObjectId,
                    nodeLabelOperation,
                    [],
                    documentPoint,
                    documentPoint,
                    currentScene,
                    update.SessionState.Generation,
                    update.SessionState.EditorState,
                    SpatialRegion: targetItem.SpatialRegion);
            }
            else
            {
                return await CleanupFailedGestureUpdateUnderGateAsync(
                    gestureId,
                    update).ConfigureAwait(false);
            }

            return update;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult?>
        TryStartAnchorConnectionUnderGateAsync(
            EditingSessionState observed,
            Canvas2DPointerInput input,
            Canvas2DSceneHitTestResult hit,
            Canvas2DSceneItem hitItem,
            CancellationToken cancellationToken)
    {
        if (_connectionCreationCatalog.Registrations.IsEmpty ||
            !TryResolveConnectorAnchorHandle(
                observed.CurrentScene!,
                observed.EditorState,
                hitItem,
                out var sourceTarget,
                out var sourceAnchorId,
                out _,
                out var anchorKind,
                out _,
                out var role,
                out _) ||
            anchorKind != ResolvedConnectorAnchorKind.Dynamic ||
            role != ConnectorAnchorRole.Source ||
            sourceTarget.Origin.SemanticElementId is not { } sourceSemanticElementId ||
            sourceTarget.Origin.VisualStateId is not { } sourceVisualStateId)
        {
            return null;
        }

        if (!_session.TryCaptureDocumentSnapshot(out var document) ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !TryResolvePersistentAnchor(
                document,
                sourceSemanticElementId,
                sourceVisualStateId,
                sourceAnchorId,
                ConnectorAnchorRole.Source))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Stale,
                _session.CaptureState(),
                hit,
                [Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.StaleGesture,
                    DiagnosticSeverity.Error,
                    "The Source anchor no longer belongs to the current persistent Document.")]);
        }

        if (ConnectorAnchorOccupancy.IsOccupied(
                document.VisualModel,
                sourceAnchorId))
        {
            return null;
        }

        var sourceRequest = new AnchorConnectionCreationSourceRequest(
            document,
            observed.DocumentRevision,
            sourceSemanticElementId,
            sourceVisualStateId,
            sourceAnchorId);
        ImmutableArray<AnchorConnectionCreationRegistration> matches;
        try
        {
            matches = _connectionCreationCatalog.GetMatchingRegistrations(sourceRequest);
        }
#pragma warning disable CA1031 // Plugin matching failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.ConnectionCreationFactoryFailed,
                "A registered anchor-connection factory failed while matching the Source anchor.",
                exception);
        }
#pragma warning restore CA1031

        if (matches.IsEmpty)
        {
            return null;
        }

        if (matches.Length != 1)
        {
            var identities = string.Join(
                ", ",
                matches.Select(static match => match.CreationId.Value));
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.AmbiguousConnectionCreation,
                $"The Source anchor matches multiple connection capabilities: {identities}.",
                observed,
                hit);
        }

        var registration = matches[0];
        var sourcePoint = Center(hitItem.Bounds);
        var gestureId = FormattableString.Invariant(
            $"anchor-connection:{observed.DocumentRevision.Value}:{input.PointerId}:{sourceAnchorId.Value}:{registration.CreationId.Value}");
        var gestureSnapshot = new EditorGestureSnapshot(
            gestureId,
            Canvas2DAnchorConnectionGestureMetadata.Kind,
            sourcePoint,
            sourcePoint,
            Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
                sourceSemanticElementId,
                sourceVisualStateId,
                sourceAnchorId,
                registration.CreationId));
        var updatedEditorState = Copy(
            observed.EditorState,
            observed.EditorState.Selection,
            hoveredObjectId: null,
            gestureSnapshot);
        var update = await ApplyEditorStateAsync(
            observed,
            updatedEditorState,
            hit,
            cancellationToken,
            "crosshair").ConfigureAwait(false);
        if (update.Status == Canvas2DInteractionStatus.Updated &&
            update.SessionState.CurrentScene is { } currentScene)
        {
            _persistentGesture = new PersistentGestureState(
                PersistentGestureKind.AnchorConnectionCreation,
                Canvas2DResizeDirection.SouthEast,
                input.PointerId,
                gestureId,
                observed.DocumentId,
                observed.DocumentRevision,
                hitItem.Id,
                sourceTarget.Id,
                sourceVisualStateId,
                hitItem.Bounds,
                sourceTarget.Bounds,
                [],
                BendIndex: -1,
                LabelProjectedObjectId: null,
                NodeLabelOperation: Canvas2DNodeLabelGestureOperation.Move,
                MoveTargets: [],
                sourcePoint,
                sourcePoint,
                currentScene,
                update.SessionState.Generation,
                update.SessionState.EditorState,
                    new AnchorConnectionGestureState(
                    registration,
                    sourceSemanticElementId,
                    sourceVisualStateId,
                        sourceAnchorId,
                        TargetAcquisition: null,
                        ProposedAnchorId: null),
                    SpatialRegion: sourceTarget.SpatialRegion);
            return update;
        }

        return await CleanupFailedGestureUpdateUnderGateAsync(
            gestureId,
            update).ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(
        Canvas2DPointerInput input,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        if (!await TryEnterAsync(linked.Token).ConfigureAwait(false))
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            if (_pendingPointer is { } pending)
            {
                return await ExecutePendingPointerMoveUnderGateAsync(
                    input,
                    pending,
                    linked.Token).ConfigureAwait(false);
            }

            var gesture = _persistentGesture;
            if (gesture is null)
            {
                return await ExecutePointInteractionUnderGateAsync(
                    input.CssPoint,
                    InteractionKind.Hover,
                    linked.Token).ConfigureAwait(false);
            }

            if (input.PointerId != gesture.PointerId)
            {
                return GestureUnavailableResult(
                    Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                    "The pointer sample does not belong to the active persistent gesture.");
            }

            var observed = _session.CaptureState();
            if (!IsCurrentGesture(observed, gesture))
            {
                return await CancelStaleGestureUnderGateAsync(
                    gesture,
                    "The active persistent gesture was superseded by another runtime generation.")
                    .ConfigureAwait(false);
            }

            if (!TryConvertPoint(
                    observed,
                    input.CssPoint,
                    out var documentPoint,
                    out var conversionFailure))
            {
                _persistentGesture = null;
                return await CleanupFailedGestureUpdateUnderGateAsync(
                    gesture.GestureId,
                    conversionFailure!).ConfigureAwait(false);
            }

            if (gesture.Kind == PersistentGestureKind.ConnectorEndpointReconnection)
            {
                return await ExecuteConnectorEndpointReconnectionMoveUnderGateAsync(
                    observed,
                    gesture,
                    documentPoint,
                    linked.Token).ConfigureAwait(false);
            }

            if (gesture.Kind == PersistentGestureKind.AnchorConnectionCreation)
            {
                return await ExecuteAnchorConnectionMoveUnderGateAsync(
                    observed,
                    gesture,
                    documentPoint,
                    linked.Token).ConfigureAwait(false);
            }

            var effectiveDocumentPoint = ClampPersistentGesturePoint(gesture, documentPoint);
            if (effectiveDocumentPoint == gesture.CurrentDocumentPoint)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    observed,
                    cssCursor: CssCursor(gesture));
            }

            if (!TryCalculatePersistentGestureGeometry(
                    gesture,
                    observed.CurrentScene!,
                    effectiveDocumentPoint,
                    out var finalBounds,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                return await TerminateInvalidGeometryGestureUnderGateAsync(gesture)
                    .ConfigureAwait(false);
            }

            if (gesture.Kind == PersistentGestureKind.Resize ||
                gesture.Kind == PersistentGestureKind.NodeLabelEdit &&
                gesture.NodeLabelOperation == Canvas2DNodeLabelGestureOperation.Resize)
            {
                var previousBounds = gesture.Kind == PersistentGestureKind.Resize
                    ? Canvas2DResizeGeometry.CalculateBounds(
                        gesture.OriginalBounds,
                        gesture.CurrentDocumentPoint - gesture.StartDocumentPoint,
                        gesture.ResizeDirection)
                    : Canvas2DResizeGeometry.CalculateBounds(
                        gesture.OriginalBounds,
                        gesture.CurrentDocumentPoint - gesture.StartDocumentPoint,
                        gesture.ResizeDirection,
                        NodeLabelVisualOverride.MinimumWidth,
                        NodeLabelVisualOverride.MinimumHeight);
                if (finalBounds == previousBounds)
                {
                    return new Canvas2DInteractionResult(
                        Canvas2DInteractionStatus.Unchanged,
                        observed,
                        cssCursor: CssCursor(gesture));
                }
            }

            var active = observed.EditorState.ActiveGesture!;
            var updatedGesture = new EditorGestureSnapshot(
                active.Id,
                active.Kind,
                active.Origin,
                effectiveDocumentPoint,
                active.Properties);
            var updatedEditorState = Copy(
                observed.EditorState,
                observed.EditorState.Selection,
                observed.EditorState.HoveredObjectId,
                updatedGesture);
            var update = await ApplyEditorStateAsync(
                observed,
                updatedEditorState,
                hitResult: null,
                linked.Token,
                CssCursor(gesture)).ConfigureAwait(false);
            if (update.Status == Canvas2DInteractionStatus.Updated &&
                update.SessionState.CurrentScene is { } currentScene)
            {
                _persistentGesture = gesture with
                {
                    CurrentDocumentPoint = effectiveDocumentPoint,
                    CurrentScene = currentScene,
                    CurrentGeneration = update.SessionState.Generation,
                    CurrentEditorState = update.SessionState.EditorState,
                };
            }
            else if (update.Status == Canvas2DInteractionStatus.Stale)
            {
                return await CancelStaleGestureUnderGateAsync(
                    gesture,
                    "The move preview could not be installed because its source scene became stale.")
                    .ConfigureAwait(false);
            }
            else if (update.Status != Canvas2DInteractionStatus.Updated)
            {
                _persistentGesture = null;
                return await CleanupFailedGestureUpdateUnderGateAsync(
                    gesture.GestureId,
                    update).ConfigureAwait(false);
            }

            return update;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult>
        ExecuteAnchorConnectionMoveUnderGateAsync(
            EditingSessionState observed,
            PersistentGestureState gesture,
            PointD documentPoint,
            CancellationToken cancellationToken)
    {
        var connection = gesture.AnchorConnection!;
        Canvas2DSceneHitTestResult? hit;
        try
        {
            hit = _hitTestService.HitTest(observed.CurrentScene!, documentPoint);
        }
#pragma warning disable CA1031 // Malformed transient geometry becomes a stable interaction diagnostic.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _persistentGesture = null;
            var failed = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                "The current Canvas2DScene could not be hit tested for a connection target.",
                exception);
            return await CleanupFailedGestureUpdateUnderGateAsync(
                gesture.GestureId,
                failed).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        AnchorConnectionTarget? target = TryResolveConnectionTarget(
            observed,
            hit,
            connection,
            out var resolvedTarget,
            out var proposedAnchorId)
            ? resolvedTarget
            : null;
        var effectivePoint = DocumentGeometryBoundary.Clamp(target is null
            ? documentPoint
            : target.SpatialRegion?.MapLocalToScene(target.Acquisition.DocumentPoint!.Value) ??
                target.Acquisition.DocumentPoint!.Value);
        if (effectivePoint == gesture.CurrentDocumentPoint &&
            Equals(connection.TargetAcquisition, target?.Acquisition))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                observed,
                hit,
                cssCursor: "crosshair");
        }

        var active = observed.EditorState.ActiveGesture!;
        var properties = target?.IsSmart == true
            ? Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
                connection.SourceSemanticElementId,
                connection.SourceVisualStateId,
                connection.SourceAnchorId,
                connection.Registration.CreationId,
                target.Acquisition)
            : Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
                connection.SourceSemanticElementId,
                connection.SourceVisualStateId,
                connection.SourceAnchorId,
                connection.Registration.CreationId,
                target?.Acquisition.TargetSemanticElementId,
                target?.Acquisition.TargetVisualStateId,
                target?.Acquisition.AnchorId);
        var updatedGesture = new EditorGestureSnapshot(
            active.Id,
            active.Kind,
            active.Origin,
            effectivePoint,
            properties);
        var updatedEditorState = Copy(
            observed.EditorState,
            observed.EditorState.Selection,
            hoveredObjectId: null,
            updatedGesture);
        var update = await ApplyEditorStateAsync(
            observed,
            updatedEditorState,
            hit,
            cancellationToken,
            "crosshair").ConfigureAwait(false);
        if (update.Status == Canvas2DInteractionStatus.Updated &&
            update.SessionState.CurrentScene is { } currentScene)
        {
            _persistentGesture = gesture with
            {
                CurrentDocumentPoint = effectivePoint,
                CurrentScene = currentScene,
                CurrentGeneration = update.SessionState.Generation,
                CurrentEditorState = update.SessionState.EditorState,
                AnchorConnection = connection with
                {
                    TargetAcquisition = target?.Acquisition,
                    ProposedAnchorId = proposedAnchorId,
                },
            };
            return update;
        }

        if (update.Status == Canvas2DInteractionStatus.Stale)
        {
            return await CancelStaleGestureUnderGateAsync(
                gesture,
                "The connection preview could not be installed because its source Scene became stale.")
                .ConfigureAwait(false);
        }

        _persistentGesture = null;
        return await CleanupFailedGestureUpdateUnderGateAsync(
            gesture.GestureId,
            update).ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePendingPointerMoveUnderGateAsync(
        Canvas2DPointerInput input,
        PendingPointerState pending,
        CancellationToken cancellationToken)
    {
        if (input.PointerId != pending.PointerId)
        {
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                "The pointer sample does not belong to the pending Canvas2D activation.");
        }

        var observed = _session.CaptureState();
        if (!IsCurrentPendingPointer(observed, pending))
        {
            _pendingPointer = null;
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Stale,
                observed,
                diagnostics:
                [
                    Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        DiagnosticSeverity.Error,
                        "The pending Canvas2D activation was superseded by another runtime generation."),
                ]);
        }

        if (!TryConvertPoint(
                observed,
                input.CssPoint,
                out var documentPoint,
                out var conversionFailure))
        {
            _pendingPointer = null;
            return conversionFailure!;
        }

        if (!IsBounded(documentPoint))
        {
            _pendingPointer = null;
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Failed,
                observed,
                diagnostics:
                [
                    Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry,
                        DiagnosticSeverity.Error,
                        "The pointer sample is outside the finite Canvas2D document space."),
                ]);
        }

        var pendingCursor = pending.MoveTarget is not null
            ? "grab"
            : pending.SelectedVisualStateId is not null ||
              pending.SelectedSemanticElementId is not null
                ? "pointer"
                : "default";
        var delta = documentPoint - pending.StartDocumentPoint;
        if (pending.MoveTarget is null ||
            !HasReachedMoveActivationThreshold(delta))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                observed,
                cssCursor: pendingCursor);
        }

        var initiatorVisualStateId = pending.SelectedVisualStateId!;
        if (IsAttachedFixedSizeNode(observed, pending.MoveTarget))
        {
            if (TryResolveBoundaryAttachmentContext(
                    observed,
                    pending.MoveTarget,
                    out var projectedAttachment,
                    out var ownerTarget) &&
                projectedAttachment is not null &&
                ownerTarget is not null)
            {
                return await StartBoundaryAttachmentMoveUnderGateAsync(
                    input,
                    pending,
                    observed,
                    documentPoint,
                    projectedAttachment,
                    ownerTarget,
                    cancellationToken).ConfigureAwait(false);
            }

            _pendingPointer = null;
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.InvalidGestureTarget,
                "The attached move target has no visible structural owner boundary.",
                observed);
        }

        var selection = pending.EditorState.Selection.Contains(initiatorVisualStateId)
            ? pending.EditorState.Selection
            : [initiatorVisualStateId];
        var commandTargetItems = selection
            .Select(visualStateId => ResolveCanonicalSceneTarget(
                pending.Scene,
                visualStateId,
                requireMoveCapability: true))
            .Where(static item => item is not null)
            .Cast<Canvas2DSceneItem>()
            .Where(item => !IsAttachedFixedSizeNode(observed, item))
            .ToArray();
        var commandTargetIds = commandTargetItems
            .Select(static item => item.Origin.VisualStateId!)
            .ToHashSet();
        var dependentTargetItems = ResolveAttachedDependentTargets(
                observed,
                commandTargetItems)
            .Where(item => !commandTargetIds.Contains(item.Origin.VisualStateId!))
            .ToArray();
        var targetItems = commandTargetItems
            .Concat(dependentTargetItems)
            .OrderBy(static item => item.Origin.VisualStateId!.Value, StringComparer.Ordinal)
            .ToArray();
        var visualStates = _session.CaptureVisualStatesForInteraction(
            pending.Scene,
            pending.Generation,
            pending.EditorState,
            targetItems.Select(static item => item.Origin.VisualStateId!));
        if (visualStates is null || visualStates.Value.Length != targetItems.Length)
        {
            _pendingPointer = null;
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Stale,
                _session.CaptureState(),
                diagnostics:
                [
                    Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        DiagnosticSeverity.Error,
                        "The move targets no longer belong to the current Document revision."),
                ]);
        }

        var visualsById = visualStates.Value.ToDictionary(static item => item.Id);
        var moveTargets = targetItems
            .Select(item => new MoveGestureTarget(
                item.Id,
                item.Origin.VisualStateId!,
                item.Bounds,
                ResolveMoveBoundaryBounds(
                    item.Bounds,
                    visualsById[item.Origin.VisualStateId!]),
                visualsById[item.Origin.VisualStateId!].Route,
                IsPreviewOnly: !commandTargetIds.Contains(item.Origin.VisualStateId!),
                SpatialRegion: item.SpatialRegion))
            .OrderBy(static target => target.VisualStateId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (moveTargets.IsEmpty)
        {
            _pendingPointer = null;
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.InvalidGestureTarget,
                "The selected objects contain no persistently movable Visual States.",
                observed);
        }

        var initiator = moveTargets.Single(target =>
            target.VisualStateId == initiatorVisualStateId);
        // Use the same basis on activation as on subsequent spatial move samples. A drop
        // into another region is validated against its destination inverse, not the source
        // origin (which may be hundreds of Scene units below the destination).
        var effectiveDelta = pending.Scene.SpatialPresentationPlan is not null
            ? delta
            : DocumentGeometryBoundary.ClampTranslation(
                moveTargets.Select(static target =>
                    target.SpatialRegion?.MapSceneToLocal(target.BoundaryBounds) ??
                    target.BoundaryBounds),
                delta);
        var effectiveDocumentPoint = pending.StartDocumentPoint + effectiveDelta;
        var gestureId = FormattableString.Invariant(
            $"move:{observed.DocumentRevision.Value}:{input.PointerId}:{initiatorVisualStateId.Value}");
        var gestureSnapshot = new EditorGestureSnapshot(
            gestureId,
            Canvas2DMoveGestureMetadata.Kind,
            pending.StartDocumentPoint,
            effectiveDocumentPoint,
            CreateMoveProperties(moveTargets));
        var updatedEditorState = Copy(
            observed.EditorState,
            selection,
            observed.EditorState.HoveredObjectId,
            gestureSnapshot);
        _pendingPointer = null;
        var update = await ApplyEditorStateAsync(
            observed,
            updatedEditorState,
            hitResult: null,
            cancellationToken,
            "grabbing").ConfigureAwait(false);
        if (update.Status == Canvas2DInteractionStatus.Updated &&
            update.SessionState.CurrentScene is { } currentScene)
        {
            _persistentGesture = new PersistentGestureState(
                PersistentGestureKind.Move,
                Canvas2DResizeDirection.SouthEast,
                input.PointerId,
                gestureId,
                observed.DocumentId,
                observed.DocumentRevision,
                initiator.SceneObjectId,
                NodeLabelOwnerSceneObjectId: null,
                initiator.VisualStateId,
                initiator.OriginalBounds,
                OriginalOwnerBounds: initiator.OriginalBounds,
                initiator.OriginalRoute,
                BendIndex: -1,
                LabelProjectedObjectId: null,
                NodeLabelOperation: Canvas2DNodeLabelGestureOperation.Move,
                moveTargets,
                pending.StartDocumentPoint,
                effectiveDocumentPoint,
                currentScene,
                update.SessionState.Generation,
                update.SessionState.EditorState,
                SpatialRegion: initiator.SpatialRegion);
            return update;
        }

        return await CleanupFailedGestureUpdateUnderGateAsync(
            gestureId,
            update).ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DInteractionResult>
        StartBoundaryAttachmentMoveUnderGateAsync(
            Canvas2DPointerInput input,
            PendingPointerState pending,
            EditingSessionState observed,
            PointD documentPoint,
            ProjectedBoundaryAttachment projectedAttachment,
            Canvas2DSceneItem ownerTarget,
            CancellationToken cancellationToken)
    {
        var target = pending.MoveTarget!;
        var visualStateId = pending.SelectedVisualStateId!;
        var visualState = _session.CaptureVisualStateForInteraction(
            pending.Scene,
            pending.Generation,
            pending.EditorState,
            visualStateId);
        if (visualState is null ||
            visualState.BoundaryAttachment is null ||
            !visualState.BoundaryAttachment.Equals(projectedAttachment.Placement) ||
            visualState.Size.IsEmpty)
        {
            _pendingPointer = null;
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Stale,
                _session.CaptureState(),
                diagnostics:
                [
                    Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        DiagnosticSeverity.Error,
                        "The attached move target no longer matches its projected attachment."),
                ]);
        }

        if (!BoundaryAttachmentPlacement.TryProjectToBoundary(
                ownerTarget.Bounds,
                documentPoint,
                visualState.Size,
                out var placement) ||
            placement is null)
        {
            _pendingPointer = null;
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry,
                "The owner boundary has no legal fixed-size attachment position.",
                observed);
        }

        var previewBounds = placement.ResolveBounds(ownerTarget.Bounds, visualState.Size);
        var translation = previewBounds.TopLeft - target.Bounds.TopLeft;
        var effectiveDocumentPoint = pending.StartDocumentPoint + translation;
        var moveTarget = new MoveGestureTarget(
            target.Id,
            visualStateId,
            target.Bounds,
            ResolveMoveBoundaryBounds(target.Bounds, visualState),
            visualState.Route,
            SpatialRegion: target.SpatialRegion);
        var moveTargets = ImmutableArray.Create(moveTarget);
        var gestureId = FormattableString.Invariant(
            $"boundary-attachment-move:{observed.DocumentRevision.Value}:{input.PointerId}:{visualStateId.Value}");
        var gestureSnapshot = new EditorGestureSnapshot(
            gestureId,
            Canvas2DMoveGestureMetadata.Kind,
            pending.StartDocumentPoint,
            effectiveDocumentPoint,
            CreateMoveProperties(moveTargets));
        var updatedEditorState = Copy(
            observed.EditorState,
            [visualStateId],
            observed.EditorState.HoveredObjectId,
            gestureSnapshot);
        _pendingPointer = null;
        var update = await ApplyEditorStateAsync(
            observed,
            updatedEditorState,
            hitResult: null,
            cancellationToken,
            "grabbing").ConfigureAwait(false);
        if (update.Status == Canvas2DInteractionStatus.Updated &&
            update.SessionState.CurrentScene is { } currentScene)
        {
            _persistentGesture = new PersistentGestureState(
                PersistentGestureKind.BoundaryAttachmentMove,
                Canvas2DResizeDirection.SouthEast,
                input.PointerId,
                gestureId,
                observed.DocumentId,
                observed.DocumentRevision,
                target.Id,
                NodeLabelOwnerSceneObjectId: null,
                visualStateId,
                target.Bounds,
                ownerTarget.Bounds,
                visualState.Route,
                BendIndex: -1,
                LabelProjectedObjectId: null,
                NodeLabelOperation: Canvas2DNodeLabelGestureOperation.Move,
                moveTargets,
                pending.StartDocumentPoint,
                effectiveDocumentPoint,
                currentScene,
                update.SessionState.Generation,
                update.SessionState.EditorState,
                OriginalBoundaryAttachment: visualState.BoundaryAttachment,
                SpatialRegion: target.SpatialRegion);
            return update;
        }

        return await CleanupFailedGestureUpdateUnderGateAsync(gestureId, update)
            .ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DInteractionResult> CompletePendingPointerClickUnderGateAsync(
        Canvas2DPointerInput input,
        PendingPointerState pending,
        CancellationToken cancellationToken)
    {
        if (!input.IsPrimary || input.Button != 0 || input.PointerId != pending.PointerId)
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                _session.CaptureState());
        }

        var observed = _session.CaptureState();
        _pendingPointer = null;
        if (!IsCurrentPendingPointer(observed, pending))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Stale,
                observed,
                diagnostics:
                [
                    Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        DiagnosticSeverity.Error,
                        "The pending selection no longer belongs to the current Scene."),
                ]);
        }

        Canvas2DSceneHitTestResult? releaseHit = null;
        if (TryConvertPoint(observed, input.CssPoint, out var documentPoint, out var failure))
        {
            try
            {
                releaseHit = _hitTestService.HitTest(observed.CurrentScene!, documentPoint);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return FailureResult(
                    observed,
                    Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                    "The current Canvas2DScene could not be hit tested for selection.",
                    exception);
            }
        }
        else
        {
            return failure!;
        }

        var releasedVisualStateId = ResolveSelectableVisualStateId(
            observed.CurrentScene!,
            releaseHit);
        var releasedSemanticElementId = releasedVisualStateId is null
            ? ResolveSelectableSemanticElementId(observed.CurrentScene!, releaseHit)
            : null;
        var updated = pending.ControlKey
            ? WithToggledSelection(
                observed.EditorState,
                releasedVisualStateId,
                releasedSemanticElementId)
            : WithPlainSelection(
                observed.EditorState,
                releasedVisualStateId,
                releasedSemanticElementId);
        var cssCursor = CssCursor(observed.CurrentScene!, releaseHit);
        if (updated.Equals(observed.EditorState))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                observed,
                releaseHit,
                cssCursor: cssCursor);
        }

        return await ApplyEditorStateAsync(
            observed,
            updated,
            releaseHit,
            cancellationToken,
            cssCursor).ConfigureAwait(false);
    }

    private static bool ShouldActivatePendingMove(
        Canvas2DPointerInput input,
        PendingPointerState pending)
    {
        if (pending.MoveTarget is null || input.PointerId != pending.PointerId)
        {
            return false;
        }

        try
        {
            var current = Canvas2DRenderer.ConvertCssToDocument(pending.Scene, input.CssPoint);
            return IsBounded(current) &&
                HasReachedMoveActivationThreshold(current - pending.StartDocumentPoint);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return false;
        }
    }

    private static bool HasReachedMoveActivationThreshold(VectorD delta)
    {
        var scale = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
        if (scale == 0d)
        {
            return false;
        }

        var normalizedX = delta.X / scale;
        var normalizedY = delta.Y / scale;
        return scale * Math.Sqrt(
            (normalizedX * normalizedX) + (normalizedY * normalizedY)) >=
            MoveActivationThreshold;
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(
        Canvas2DPointerInput input,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        if (!await TryEnterAsync(linked.Token).ConfigureAwait(false))
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            if (_pendingPointer is { } pending)
            {
                if (ShouldActivatePendingMove(input, pending))
                {
                    var activation = await ExecutePendingPointerMoveUnderGateAsync(
                        input,
                        pending,
                        linked.Token).ConfigureAwait(false);
                    if (_persistentGesture is null)
                    {
                        return activation;
                    }
                }
                else
                {
                    return await CompletePendingPointerClickUnderGateAsync(
                        input,
                        pending,
                        linked.Token).ConfigureAwait(false);
                }
            }

            var gesture = _persistentGesture;
            if (gesture is null)
            {
                if (!input.IsPrimary || input.Button != 0)
                {
                    return new Canvas2DInteractionResult(
                        Canvas2DInteractionStatus.Unchanged,
                        _session.CaptureState());
                }

                return await ExecutePointInteractionUnderGateAsync(
                    input.CssPoint,
                    InteractionKind.Selection,
                    linked.Token).ConfigureAwait(false);
            }

            if (input.PointerId != gesture.PointerId)
            {
                return GestureUnavailableResult(
                    Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive,
                    "The pointer release does not belong to the active persistent gesture.");
            }

            var observed = _session.CaptureState();
            if (!IsCurrentGesture(observed, gesture))
            {
                return await CancelStaleGestureUnderGateAsync(
                    gesture,
                    "The persistent gesture became stale before pointer release.")
                    .ConfigureAwait(false);
            }

            if (gesture.Kind == PersistentGestureKind.ConnectorEndpointReconnection)
            {
                return await CompleteConnectorEndpointReconnectionUnderGateAsync(
                    observed,
                    gesture,
                    input,
                    linked.Token).ConfigureAwait(false);
            }

            if (gesture.Kind == PersistentGestureKind.AnchorConnectionCreation)
            {
                return await CompleteAnchorConnectionUnderGateAsync(
                    observed,
                    gesture,
                    input,
                    linked.Token).ConfigureAwait(false);
            }

            if (!TryConvertPoint(
                    observed,
                    input.CssPoint,
                    out var finalDocumentPoint,
                    out var conversionFailure))
            {
                _persistentGesture = null;
                return await CleanupFailedGestureUpdateUnderGateAsync(
                    gesture.GestureId,
                    conversionFailure!).ConfigureAwait(false);
            }

            if (!TryCalculatePersistentGestureGeometry(
                    gesture,
                    observed.CurrentScene!,
                    ClampPersistentGesturePoint(gesture, finalDocumentPoint),
                    out var finalBounds,
                    out var finalRoute,
                    out var finalMoves,
                    out var finalLabelPlacement,
                    out var finalNodeLabelOverride,
                    out var finalBoundaryAttachment))
            {
                return await TerminateInvalidGeometryGestureUnderGateAsync(gesture)
                    .ConfigureAwait(false);
            }

            var clearedEditorState = Copy(
                observed.EditorState,
                observed.EditorState.Selection,
                observed.EditorState.HoveredObjectId,
                activeGesture: null);
            _persistentGesture = null;
            _suppressNextActivation = gesture.Kind is
                PersistentGestureKind.Move or
                PersistentGestureKind.BoundaryAttachmentMove or
                PersistentGestureKind.NodeLabelEdit;

            if ((gesture.Kind != PersistentGestureKind.Move || finalMoves.IsEmpty) &&
                finalBounds == gesture.OriginalBounds &&
                finalRoute.AsSpan().SequenceEqual(gesture.OriginalRoute.AsSpan()) &&
                (gesture.Kind != PersistentGestureKind.BoundaryAttachmentMove ||
                 Equals(finalBoundaryAttachment, gesture.OriginalBoundaryAttachment)))
            {
                var cleared = await _session.UpdateEditorStateFromInteractionAsync(
                    observed.CurrentScene!,
                    observed.Generation,
                    observed.EditorState,
                    clearedEditorState,
                    linked.Token).ConfigureAwait(false);
                var idlePresentation = ResolvePointerPresentation(
                    cleared.State,
                    input.CssPoint);
                return FromSessionResult(
                    cleared,
                    idlePresentation.HitResult,
                    idlePresentation.CssCursor);
            }

            var persistentFinalBounds = gesture.SpatialRegion?.MapSceneToLocal(
                finalBounds) ?? finalBounds;
            var persistentOriginalOwnerBounds = gesture.SpatialRegion?.MapSceneToLocal(
                gesture.OriginalOwnerBounds) ?? gesture.OriginalOwnerBounds;
            ICommand command = gesture.Kind switch
            {
                PersistentGestureKind.Move when finalMoves.Length == 1 =>
                    new MoveVisualStateCommand(
                        gesture.DocumentId,
                        gesture.DocumentRevision,
                        finalMoves[0].VisualStateId,
                        finalMoves[0].TargetPosition,
                        VisualPlacementMode.Pinned),
                PersistentGestureKind.Move => new MoveVisualStatesCommand(
                    gesture.DocumentId,
                    gesture.DocumentRevision,
                    finalMoves),
                PersistentGestureKind.BoundaryAttachmentMove =>
                    new UpdateBoundaryAttachmentCommand(
                        gesture.DocumentId,
                        gesture.DocumentRevision,
                        gesture.TargetVisualStateId,
                        finalBoundaryAttachment!,
                        persistentOriginalOwnerBounds),
                PersistentGestureKind.Resize => new ResizeVisualStateCommand(
                    gesture.DocumentId,
                    gesture.DocumentRevision,
                    gesture.TargetVisualStateId,
                    persistentFinalBounds,
                    VisualPlacementMode.Pinned),
                PersistentGestureKind.RouteBend => new UpdateConnectionRouteCommand(
                    gesture.DocumentId,
                    gesture.DocumentRevision,
                    gesture.TargetVisualStateId,
                    finalRoute),
                PersistentGestureKind.NodeLabelEdit =>
                    new UpdateNodeLabelVisualOverrideCommand(
                        gesture.DocumentId,
                        gesture.DocumentRevision,
                        gesture.TargetVisualStateId,
                        finalNodeLabelOverride),
                _ => new MoveLabelCommand(
                    gesture.DocumentId,
                    gesture.DocumentRevision,
                    gesture.TargetVisualStateId,
                    finalLabelPlacement),
            };
            if (gesture.Kind == PersistentGestureKind.Move && observed.CurrentScene!.SpatialPresentationPlan is not null)
            {
                if (!TryPlanSpatialMove(gesture, observed, finalDocumentPoint, out var spatialCommand, out var spatialDiagnostics))
                {
                    return await CleanupFailedGestureUpdateUnderGateAsync(gesture.GestureId,
                        new Canvas2DInteractionResult(Canvas2DInteractionStatus.Failed, observed,
                            diagnostics: spatialDiagnostics)).ConfigureAwait(false);
                }
                if (spatialCommand is null)
                {
                    var cleared = await _session.UpdateEditorStateFromInteractionAsync(
                        observed.CurrentScene!, observed.Generation, observed.EditorState,
                        clearedEditorState, linked.Token).ConfigureAwait(false);
                    return FromSessionResult(cleared, null, "default");
                }
                command = spatialCommand;
            }
            var commandResult = command is MoveVisualStateCommand moveCommand
                ? await _session.CompleteMoveGestureAsync(
                    observed.CurrentScene!,
                    observed.Generation,
                    observed.EditorState,
                    clearedEditorState,
                    moveCommand,
                    linked.Token).ConfigureAwait(false)
                : await _session.CompletePersistentGestureAsync(
                    observed.CurrentScene!,
                    observed.Generation,
                    observed.EditorState,
                    clearedEditorState,
                    command,
                    linked.Token).ConfigureAwait(false);
            if (commandResult.IsCommitted)
            {
                var committedState = _session.CaptureState();
                var idlePresentation = ResolvePointerPresentation(
                    committedState,
                    input.CssPoint);
                var committedCursor = gesture.Kind is
                    PersistentGestureKind.Move or
                    PersistentGestureKind.BoundaryAttachmentMove
                    ? "grab"
                    : idlePresentation.CssCursor;
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Committed,
                    committedState,
                    idlePresentation.HitResult,
                    diagnostics: commandResult.Diagnostics,
                    persistentOperation: commandResult,
                    cssCursor: committedCursor);
            }

            var cleanup = await _session.ClearPersistentGestureFromInteractionAsync(
                gesture.GestureId,
                CancellationToken.None).ConfigureAwait(false);
            var diagnostics = commandResult.Diagnostics.Concat(cleanup.Diagnostics);
            var status = commandResult.Status == HistoryOperationStatus.Cancelled
                ? Canvas2DInteractionStatus.Cancelled
                : commandResult.Diagnostics.Any(diagnostic =>
                    StringComparer.Ordinal.Equals(
                        diagnostic.Code,
                        Canvas2DInteractionDiagnosticCodes.StaleGesture))
                    ? Canvas2DInteractionStatus.Stale
                    : Canvas2DInteractionStatus.Failed;
            if (status == Canvas2DInteractionStatus.Failed)
            {
                diagnostics = diagnostics.Append(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.CommandRejected,
                    DiagnosticSeverity.Error,
                    "The persistent gesture command was rejected without changing the Document."));
            }

            return new Canvas2DInteractionResult(
                status,
                cleanup.State,
                diagnostics: diagnostics,
                persistentOperation: commandResult);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointerCancelledAsync(
        long pointerId,
        CancellationToken cancellationToken) =>
        await ExecutePointerCancelledCoreAsync(pointerId, cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<Canvas2DInteractionResult>
        CompleteAnchorConnectionUnderGateAsync(
            EditingSessionState observed,
            PersistentGestureState gesture,
            Canvas2DPointerInput input,
            CancellationToken cancellationToken)
    {
        var connection = gesture.AnchorConnection!;
        _suppressNextActivation = true;
        var documentPoint = default(PointD);
        Canvas2DInteractionResult? conversionFailure = null;
        if (!input.IsPrimary || input.Button != 0 ||
            !TryConvertPoint(
                observed,
                input.CssPoint,
                out documentPoint,
                out conversionFailure))
        {
            if (conversionFailure is not null)
            {
                _persistentGesture = null;
                return await CleanupFailedGestureUpdateUnderGateAsync(
                    gesture.GestureId,
                    conversionFailure).ConfigureAwait(false);
            }

            return await CancelAnchorConnectionUnderGateAsync(gesture, input.CssPoint)
                .ConfigureAwait(false);
        }

        if (!DocumentGeometryBoundary.Contains(documentPoint))
        {
            return await CancelAnchorConnectionUnderGateAsync(gesture, input.CssPoint)
                .ConfigureAwait(false);
        }

        Canvas2DSceneHitTestResult? hit;
        try
        {
            hit = _hitTestService.HitTest(observed.CurrentScene!, documentPoint);
        }
#pragma warning disable CA1031 // Malformed transient geometry becomes a stable interaction diagnostic.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _persistentGesture = null;
            var failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                "The current Canvas2DScene could not be hit tested for connection completion.",
                exception);
            return await CleanupFailedGestureUpdateUnderGateAsync(
                gesture.GestureId,
                failure).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        if (!TryResolveConnectionTarget(
                observed,
                hit,
                connection,
                out var target,
                out _))
        {
            return await CancelAnchorConnectionUnderGateAsync(gesture, input.CssPoint)
                .ConfigureAwait(false);
        }

        if (!_session.TryCaptureDocumentSnapshot(out var document) ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !_connectionCreationCatalog.TryGetRegistration(
                connection.Registration.CreationId,
                out var currentRegistration) ||
            !ReferenceEquals(currentRegistration, connection.Registration) ||
            !TryResolveAvailablePersistentAnchor(
                document,
                connection.SourceSemanticElementId,
                connection.SourceVisualStateId,
                connection.SourceAnchorId,
                ConnectorAnchorRole.Source) ||
            !IsTargetAcquisitionCurrent(observed, document, connection, target))
        {
            return await CancelStaleGestureUnderGateAsync(
                gesture,
                "The connection endpoints no longer belong to the current persistent Document.")
                .ConfigureAwait(false);
        }

        var sourceRequest = new AnchorConnectionCreationSourceRequest(
            document,
            observed.DocumentRevision,
            connection.SourceSemanticElementId,
            connection.SourceVisualStateId,
            connection.SourceAnchorId);
        bool stillMatches;
        try
        {
            stillMatches = currentRegistration.CommandFactory.CanStart(sourceRequest);
        }
#pragma warning disable CA1031 // Plugin matching failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _persistentGesture = null;
            var failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.ConnectionCreationFactoryFailed,
                "The selected anchor-connection factory failed during completion revalidation.",
                exception);
            return await CleanupFailedGestureUpdateUnderGateAsync(
                gesture.GestureId,
                failure).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        if (!stillMatches || _creationIdentityProvider is null)
        {
            _persistentGesture = null;
            return await EndFailedAnchorConnectionUnderGateAsync(
                gesture,
                [Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.ConnectionCreationFactoryFailed,
                    DiagnosticSeverity.Error,
                    "The selected anchor-connection capability no longer matches the Source anchor.")])
                .ConfigureAwait(false);
        }

        var clearedEditorState = Copy(
            observed.EditorState,
            observed.EditorState.Selection,
            hoveredObjectId: null,
            activeGesture: null);
        _persistentGesture = null;
        AnchorConnectionCreationPlanResult? planned;
        try
        {
            planned = currentRegistration.CommandFactory.CreatePlan(
                new AnchorConnectionCreationRequest(
                    document,
                    observed.DocumentRevision,
                    connection.SourceSemanticElementId,
                    connection.SourceVisualStateId,
                    connection.SourceAnchorId,
                    target.Acquisition.TargetSemanticElementId!,
                    target.Acquisition.TargetVisualStateId!,
                    target.Acquisition.AnchorId!,
                    _creationIdentityProvider,
                    target.IsSmart ? target.Acquisition : null));
        }
#pragma warning disable CA1031 // Plugin planning failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            var failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.ConnectionCreationFactoryFailed,
                "The selected anchor-connection factory could not create a Command plan.",
                exception);
            return await EndFailedAnchorConnectionUnderGateAsync(
                gesture,
                failure.Diagnostics).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        if (planned is null)
        {
            return await EndFailedAnchorConnectionUnderGateAsync(
                gesture,
                [Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.ConnectionCreationFactoryFailed,
                    DiagnosticSeverity.Error,
                    "The selected anchor-connection factory returned no Command plan result.")])
                .ConfigureAwait(false);
        }

        if (!planned.Succeeded || planned.Plan is not { } plan)
        {
            return await EndFailedAnchorConnectionUnderGateAsync(
                gesture,
                planned.Diagnostics).ConfigureAwait(false);
        }

        if (plan.Command.TargetDocumentId != observed.DocumentId ||
            plan.Command.ExpectedRevision != observed.DocumentRevision)
        {
            return await EndFailedAnchorConnectionUnderGateAsync(
                gesture,
                planned.Diagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.StaleGesture,
                    DiagnosticSeverity.Error,
                    "The connection-creation plan does not target the captured Document revision.")))
                .ConfigureAwait(false);
        }

        if (!_session.TryCaptureDocumentSnapshot(out var executionDocument) ||
            executionDocument.DocumentId != observed.DocumentId ||
            executionDocument.Revision != observed.DocumentRevision ||
            !TryResolveAvailablePersistentAnchor(
                executionDocument,
                connection.SourceSemanticElementId,
                connection.SourceVisualStateId,
                connection.SourceAnchorId,
                ConnectorAnchorRole.Source) ||
            !IsTargetAcquisitionCurrent(observed, executionDocument, connection, target))
        {
            return await EndFailedAnchorConnectionUnderGateAsync(
                gesture,
                planned.Diagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.StaleGesture,
                    DiagnosticSeverity.Error,
                    "The connection endpoints are no longer free in the current persistent Document revision.")))
                .ConfigureAwait(false);
        }

        var commandResult = await _session.CompletePersistentGestureAsync(
            observed.CurrentScene!,
            observed.Generation,
            observed.EditorState,
            clearedEditorState,
            plan.Command,
            cancellationToken).ConfigureAwait(false);
        if (!commandResult.IsCommitted)
        {
            var cleanup = await _session.ClearPersistentGestureFromInteractionAsync(
                gesture.GestureId,
                CancellationToken.None).ConfigureAwait(false);
            var diagnostics = planned.Diagnostics
                .AddRange(commandResult.Diagnostics)
                .AddRange(cleanup.Diagnostics);
            var status = commandResult.Status == HistoryOperationStatus.Cancelled
                ? Canvas2DInteractionStatus.Cancelled
                : diagnostics.Any(diagnostic => StringComparer.Ordinal.Equals(
                    diagnostic.Code,
                    Canvas2DInteractionDiagnosticCodes.StaleGesture))
                    ? Canvas2DInteractionStatus.Stale
                    : Canvas2DInteractionStatus.Failed;
            if (status == Canvas2DInteractionStatus.Failed &&
                !diagnostics.Any(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics = diagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.CommandRejected,
                    DiagnosticSeverity.Error,
                    "The connection-creation Command was rejected without changing the Document."));
            }

            return new Canvas2DInteractionResult(
                status,
                cleanup.State,
                diagnostics: diagnostics,
                persistentOperation: commandResult);
        }

        await _session.WaitForIdleAsync(CancellationToken.None).ConfigureAwait(false);
        var rebuilt = _session.CaptureState();
        var completionDiagnostics = planned.Diagnostics.AddRange(commandResult.Diagnostics);
        var connectorIsInScene = rebuilt.CurrentScene?.Items.Any(item =>
            item.Origin.VisualStateId == plan.CreatedConnectorVisualStateId) == true;
        if (rebuilt.IsGraphicalInteractionEnabled &&
            rebuilt.DocumentId == observed.DocumentId &&
            rebuilt.DocumentRevision >= commandResult.CommittedRevision &&
            rebuilt.CurrentScene is { } rebuiltScene &&
            connectorIsInScene &&
            rebuilt.EditorState.ActiveGesture is null)
        {
            var selectedEditorState = Copy(
                rebuilt.EditorState,
                [plan.CreatedConnectorVisualStateId],
                hoveredObjectId: null,
                activeGesture: null);
            var selected = await _session.UpdateEditorStateFromInteractionAsync(
                rebuiltScene,
                rebuilt.Generation,
                rebuilt.EditorState,
                selectedEditorState,
                cancellationToken).ConfigureAwait(false);
            completionDiagnostics = completionDiagnostics.AddRange(selected.Diagnostics);
            if (selected.Succeeded)
            {
                rebuilt = selected.State;
            }
            else
            {
                completionDiagnostics = completionDiagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.EditorStateUpdateFailed,
                    DiagnosticSeverity.Error,
                    "The created connector could not become the sole transient selection."));
            }
        }
        else
        {
            completionDiagnostics = completionDiagnostics.Add(Diagnostic(
                Canvas2DInteractionDiagnosticCodes.EditorStateUpdateFailed,
                DiagnosticSeverity.Error,
                "The created connector is unavailable in the authoritative rebuilt Scene."));
        }

        return new Canvas2DInteractionResult(
            Canvas2DInteractionStatus.Committed,
            rebuilt,
            diagnostics: completionDiagnostics,
            persistentOperation: commandResult,
            cssCursor: "pointer");
    }

    private async ValueTask<Canvas2DInteractionResult>
        CancelAnchorConnectionUnderGateAsync(
            PersistentGestureState gesture,
            PointD cssPoint)
    {
        _persistentGesture = null;
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gesture.GestureId,
            CancellationToken.None).ConfigureAwait(false);
        var presentation = ResolvePointerPresentation(cleared.State, cssPoint);
        return FromSessionResult(
            cleared,
            presentation.HitResult,
            presentation.CssCursor);
    }

    private async ValueTask<Canvas2DInteractionResult>
        EndFailedAnchorConnectionUnderGateAsync(
            PersistentGestureState gesture,
            IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _persistentGesture = null;
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gesture.GestureId,
            CancellationToken.None).ConfigureAwait(false);
        var combined = diagnostics.Concat(cleared.Diagnostics).ToImmutableArray();
        return new Canvas2DInteractionResult(
            combined.Any(static diagnostic => StringComparer.Ordinal.Equals(
                diagnostic.Code,
                Canvas2DInteractionDiagnosticCodes.StaleGesture))
                ? Canvas2DInteractionStatus.Stale
                : Canvas2DInteractionStatus.Failed,
            cleared.State,
            diagnostics: combined);
    }

    private async ValueTask<Canvas2DInteractionResult> ExecutePointerCancelledCoreAsync(
        long pointerId,
        CancellationToken cancellationToken)
    {
        if (pointerId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pointerId),
                pointerId,
                "The pointer identity must be non-negative.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        if (!await TryEnterAsync(linked.Token).ConfigureAwait(false))
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            if (_pendingPointer is { } pending && pending.PointerId == pointerId)
            {
                _pendingPointer = null;
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    _session.CaptureState());
            }

            var gesture = _persistentGesture;
            if (gesture is null || gesture.PointerId != pointerId)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    _session.CaptureState());
            }

            _persistentGesture = null;
            var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
                gesture.GestureId,
                CancellationToken.None).ConfigureAwait(false);
            return FromSessionResult(cleared, hitResult: null);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<Canvas2DInteractionResult> ExecuteCancelActiveGestureAsync(
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeToken);
        if (!await TryEnterAsync(linked.Token).ConfigureAwait(false))
        {
            return CancellationOrDisposalResult();
        }

        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return DisposedResult();
            }

            _pendingPointer = null;
            var gesture = _persistentGesture;
            if (gesture is null)
            {
                return new Canvas2DInteractionResult(
                    Canvas2DInteractionStatus.Unchanged,
                    _session.CaptureState());
            }

            _persistentGesture = null;
            _suppressNextActivation = true;
            var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
                gesture.GestureId,
                CancellationToken.None).ConfigureAwait(false);
            return FromSessionResult(cleared, hitResult: null);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static bool IsCurrentGesture(
        EditingSessionState state,
        PersistentGestureState gesture) =>
        state.IsGraphicalInteractionEnabled &&
        state.DocumentId == gesture.DocumentId &&
        state.DocumentRevision == gesture.DocumentRevision &&
        state.Generation == gesture.CurrentGeneration &&
        ReferenceEquals(state.CurrentScene, gesture.CurrentScene) &&
        ReferenceEquals(state.EditorState, gesture.CurrentEditorState) &&
        (gesture.SpatialRegion is null ||
            state.CurrentScene!.SpatialPresentationPlan!.Regions.Any(instance =>
                instance.Equals(gesture.SpatialRegion))) &&
        state.EditorState.ActiveGesture is { } active &&
        StringComparer.Ordinal.Equals(active.Id, gesture.GestureId);

    private static bool IsCurrentPendingPointer(
        EditingSessionState state,
        PendingPointerState pending) =>
        state.IsGraphicalInteractionEnabled &&
        state.Generation == pending.Generation &&
        ReferenceEquals(state.CurrentScene, pending.Scene) &&
        ReferenceEquals(state.EditorState, pending.EditorState) &&
        state.EditorState.ActiveGesture is null;

    private static bool TryConvertPoint(
        EditingSessionState observed,
        PointD cssPoint,
        out PointD documentPoint,
        out Canvas2DInteractionResult? failure)
    {
        try
        {
            documentPoint = Canvas2DRenderer.ConvertCssToDocument(
                observed.CurrentScene!,
                cssPoint);
            failure = null;
            return true;
        }
#pragma warning disable CA1031 // Malformed transient input becomes a stable interaction diagnostic.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            documentPoint = default;
            failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.CoordinateConversionFailed,
                "Canvas-relative CSS coordinates could not be converted to document coordinates.",
                exception);
            return false;
        }
#pragma warning restore CA1031
    }

    private async ValueTask<Canvas2DInteractionResult> CancelStaleGestureUnderGateAsync(
        PersistentGestureState gesture,
        string message)
    {
        _persistentGesture = null;
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gesture.GestureId,
            CancellationToken.None).ConfigureAwait(false);
        return new Canvas2DInteractionResult(
            Canvas2DInteractionStatus.Stale,
            cleared.State,
            diagnostics: cleared.Diagnostics.Append(Diagnostic(
                Canvas2DInteractionDiagnosticCodes.StaleGesture,
                DiagnosticSeverity.Error,
                message)));
    }

    private async ValueTask<Canvas2DInteractionResult>
        TerminateInvalidGeometryGestureUnderGateAsync(PersistentGestureState gesture)
    {
        _persistentGesture = null;
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gesture.GestureId,
            CancellationToken.None).ConfigureAwait(false);
        return new Canvas2DInteractionResult(
            Canvas2DInteractionStatus.Failed,
            cleared.State,
            diagnostics: cleared.Diagnostics.Append(Diagnostic(
                Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry,
                DiagnosticSeverity.Error,
                "The pointer sample would produce geometry outside the finite Canvas2D document space.")));
    }

    private async ValueTask<Canvas2DInteractionResult> CleanupFailedGestureUpdateUnderGateAsync(
        string gestureId,
        Canvas2DInteractionResult failedUpdate)
    {
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gestureId,
            CancellationToken.None).ConfigureAwait(false);
        return new Canvas2DInteractionResult(
            failedUpdate.Status,
            cleared.State,
            failedUpdate.HitResult,
            failedUpdate.Diagnostics.Concat(cleared.Diagnostics),
            failedUpdate.PersistentOperation);
    }

    private async ValueTask<Canvas2DInteractionResult> ApplyEditorStateAsync(
        EditingSessionState observed,
        EditorStateSnapshot updated,
        Canvas2DSceneHitTestResult? hitResult,
        CancellationToken cancellationToken,
        string cssCursor = "default",
        Canvas2DConnectorRouteContextAction? connectorRouteContextAction = null,
        Canvas2DConnectorAnchorContextAction? connectorAnchorContextAction = null,
        Canvas2DNodeLabelContextAction? nodeLabelContextAction = null)
    {
        try
        {
            var update = await _session.UpdateEditorStateFromInteractionAsync(
                observed.CurrentScene!,
                observed.Generation,
                observed.EditorState,
                updated,
                cancellationToken).ConfigureAwait(false);
            return FromSessionResult(
                update,
                hitResult,
                cssCursor,
                connectorRouteContextAction,
                connectorAnchorContextAction,
                nodeLabelContextAction);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CancellationOrDisposalResult();
        }
#pragma warning disable CA1031 // Session-bound failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FailureResult(
                _session.CaptureState(),
                Canvas2DInteractionDiagnosticCodes.EditorStateUpdateFailed,
                "The transient Editor State interaction update failed.",
                exception);
        }
#pragma warning restore CA1031
    }

    private async ValueTask<Canvas2DInteractionResult> ClearHoverAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var update = await _session.ClearHoverFromInteractionAsync(
                cancellationToken).ConfigureAwait(false);
            return FromSessionResult(update, hitResult: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CancellationOrDisposalResult();
        }
#pragma warning disable CA1031 // Session-bound failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FailureResult(
                _session.CaptureState(),
                Canvas2DInteractionDiagnosticCodes.EditorStateUpdateFailed,
                "The transient hover-clear interaction update failed.",
                exception);
        }
#pragma warning restore CA1031
    }

    private Canvas2DInteractionResult FromSessionResult(
        EditingSessionOperationResult update,
        Canvas2DSceneHitTestResult? hitResult,
        string cssCursor = "default",
        Canvas2DConnectorRouteContextAction? connectorRouteContextAction = null,
        Canvas2DConnectorAnchorContextAction? connectorAnchorContextAction = null,
        Canvas2DNodeLabelContextAction? nodeLabelContextAction = null)
    {
        var status = update.Status switch
        {
            EditingSessionOperationStatus.Succeeded => Canvas2DInteractionStatus.Updated,
            EditingSessionOperationStatus.Superseded => Canvas2DInteractionStatus.Stale,
            EditingSessionOperationStatus.Rejected => Canvas2DInteractionStatus.Unavailable,
            EditingSessionOperationStatus.Cancelled =>
                Volatile.Read(ref _disposed) != 0
                    ? Canvas2DInteractionStatus.Disposed
                    : Canvas2DInteractionStatus.Cancelled,
            EditingSessionOperationStatus.Closed => Canvas2DInteractionStatus.Disposed,
            _ => Canvas2DInteractionStatus.Failed,
        };
        return new Canvas2DInteractionResult(
            status,
            update.State,
            hitResult,
            update.Diagnostics,
            cssCursor: status == Canvas2DInteractionStatus.Updated
                ? cssCursor
                : "default",
            connectorRouteContextAction: connectorRouteContextAction,
            connectorAnchorContextAction: connectorAnchorContextAction,
            nodeLabelContextAction: nodeLabelContextAction);
    }

    private Canvas2DInteractionResult CancellationOrDisposalResult() =>
        Volatile.Read(ref _disposed) != 0
            ? DisposedResult()
            : new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Cancelled,
                _session.CaptureState(),
                diagnostics:
                [
                    Diagnostic(
                        Canvas2DInteractionDiagnosticCodes.Cancelled,
                        DiagnosticSeverity.Information,
                        "The Canvas2D interaction operation was cancelled."),
                ]);

    private Canvas2DInteractionResult DisposedResult() =>
        new(
            Canvas2DInteractionStatus.Disposed,
            _session.CaptureState(),
            diagnostics:
            [
                Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.Disposed,
                    DiagnosticSeverity.Error,
                    "The Canvas2D interaction controller is disposed."),
            ]);

    private Canvas2DInteractionResult UnavailableResult(EditingSessionState state) =>
        new(
            Canvas2DInteractionStatus.Unavailable,
            state,
            diagnostics:
            [
                Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.UnavailableSession,
                    DiagnosticSeverity.Error,
                    "Graphical interaction requires a Ready Editing Session with a current Canvas2DScene."),
            ]);

    private Canvas2DInteractionResult GestureUnavailableResult(
        string diagnosticCode,
        string message,
        EditingSessionState? state = null,
        Canvas2DSceneHitTestResult? hitResult = null) =>
        new(
            Canvas2DInteractionStatus.Unavailable,
            state ?? _session.CaptureState(),
            hitResult,
            [Diagnostic(diagnosticCode, DiagnosticSeverity.Error, message)]);

    private static Canvas2DInteractionResult FailureResult(
        EditingSessionState state,
        string code,
        string message,
        Exception exception) =>
        new(
            Canvas2DInteractionStatus.Failed,
            state,
            diagnostics:
            [
                new Diagnostic(
                    code,
                    DiagnosticSeverity.Error,
                    message,
                    state.DocumentId.Value,
                    [
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            exception.GetType().FullName ?? exception.GetType().Name),
                    ]),
            ]);

    private Diagnostic Diagnostic(string code, DiagnosticSeverity severity, string message) =>
        new(code, severity, message, _session.CaptureState().DocumentId.Value);

    private static EditorStateSnapshot WithHover(
        EditorStateSnapshot source,
        SceneObjectId? hoveredObjectId) =>
        Copy(source, source.Selection, hoveredObjectId);

    private static EditorStateSnapshot WithPlainSelection(
        EditorStateSnapshot source,
        VisualStateId? selectedVisualStateId,
        SemanticElementId? selectedSemanticElementId = null) =>
        Copy(
            source,
            selectedVisualStateId is null ? [] : [selectedVisualStateId],
            source.HoveredObjectId,
            activeGesture: source.ActiveGesture,
            semanticSceneSelection: selectedSemanticElementId);

    private static EditorStateSnapshot WithToggledSelection(
        EditorStateSnapshot source,
        VisualStateId? selectedVisualStateId,
        SemanticElementId? selectedSemanticElementId = null)
    {
        if (selectedVisualStateId is null && selectedSemanticElementId is null)
        {
            return source;
        }

        if (selectedSemanticElementId is not null)
        {
            return Copy(
                source,
                [],
                source.HoveredObjectId,
                source.ActiveGesture,
                source.SemanticSceneSelection == selectedSemanticElementId
                    ? null
                    : selectedSemanticElementId);
        }

        return Copy(
            source,
            source.Selection.Contains(selectedVisualStateId!)
                ? source.Selection.Where(item => item != selectedVisualStateId)
                : source.Selection.Append(selectedVisualStateId!),
            source.HoveredObjectId,
            source.ActiveGesture,
            semanticSceneSelection: null);
    }

    private static EditorStateSnapshot Copy(
        EditorStateSnapshot source,
        IEnumerable<VisualStateId> selection,
        SceneObjectId? hoveredObjectId) =>
        Copy(
            source,
            selection,
            hoveredObjectId,
            source.ActiveGesture,
            selection.Any() ? null : source.SemanticSceneSelection);

    private static EditorStateSnapshot Copy(
        EditorStateSnapshot source,
        IEnumerable<VisualStateId> selection,
        SceneObjectId? hoveredObjectId,
        EditorGestureSnapshot? activeGesture,
        SemanticElementId? semanticSceneSelection = null) =>
        new(
            selection,
            hoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            activeGesture,
            source.TemporaryFeedback,
            source.ToolState,
            semanticSceneSelection);

    private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateMoveProperties(
        ImmutableArray<MoveGestureTarget> targets)
    {
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DMoveGestureMetadata.TargetCount,
            PropertyValue.FromInteger(targets.Length));
        for (var index = 0; index < targets.Length; index++)
        {
            yield return new KeyValuePair<string, PropertyValue>(
                Canvas2DMoveGestureMetadata.IndexedSceneObjectId(index),
                PropertyValue.FromText(targets[index].SceneObjectId.Value));
            yield return new KeyValuePair<string, PropertyValue>(
                Canvas2DMoveGestureMetadata.IndexedVisualStateId(index),
                PropertyValue.FromText(targets[index].VisualStateId.Value));
        }
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateResizeProperties(
        SceneObjectId sceneObjectId,
        VisualStateId visualStateId,
        Canvas2DResizeDirection direction) =>
        [
            new KeyValuePair<string, PropertyValue>(
                Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                PropertyValue.FromText(sceneObjectId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DResizeGestureMetadata.TargetVisualStateId,
                PropertyValue.FromText(visualStateId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DResizeGestureMetadata.HandleRole,
                PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction))),
        ];

    private static bool TryResolveResizeTarget(
        Canvas2DScene scene,
        EditorStateSnapshot editorState,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem target,
        out Canvas2DResizeDirection direction)
    {
        target = null!;
        direction = default;
        if ((hitItem.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DResizeGestureMetadata.HandleRole,
                out var role) ||
            role.Kind != PropertyValueKind.Text ||
            !Canvas2DResizeGeometry.TryParseRole(role.TextValue, out direction) ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                out var targetIdValue) ||
            targetIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DResizeGestureMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            visualIdValue.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        var targetId = new SceneObjectId(targetIdValue.TextValue);
        target = scene.Items.SingleOrDefault(item => item.Id == targetId)!;
        return target is not null &&
            editorState.Selection.Length == 1 &&
            editorState.Selection.Contains(target.Origin.VisualStateId!) &&
            StringComparer.Ordinal.Equals(
                target.Origin.VisualStateId?.Value,
                visualIdValue.TextValue) &&
            hitItem.Origin.VisualStateId == target.Origin.VisualStateId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(targetId) &&
            HasBooleanMetadata(target, Canvas2DResizeGestureMetadata.ResizeCapable);
    }

    private static bool TryResolveNodeLabelTarget(
        Canvas2DScene scene,
        EditorStateSnapshot editorState,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem labelTarget,
        out Canvas2DSceneItem nodeTarget,
        out ProjectedObjectId? labelProjectedObjectId,
        out Canvas2DNodeLabelGestureOperation operation,
        out Canvas2DResizeDirection resizeDirection)
    {
        labelTarget = null!;
        nodeTarget = null!;
        labelProjectedObjectId = null;
        operation = Canvas2DNodeLabelGestureOperation.Move;
        resizeDirection = Canvas2DResizeDirection.SouthEast;
        if (!TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                out var nodeIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                out var labelSceneIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                out var labelIdValue))
        {
            return false;
        }

        if (TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.Operation,
                out var operationValue))
        {
            if (!StringComparer.Ordinal.Equals(
                    operationValue,
                    Canvas2DNodeLabelGestureMetadata.ResizeOperation) ||
                !TryGetTextMetadata(
                    hitItem,
                    Canvas2DNodeLabelGestureMetadata.ResizeDirection,
                    out var directionValue) ||
                !Canvas2DResizeGeometry.TryParseRole(
                    directionValue,
                    out resizeDirection))
            {
                return false;
            }

            operation = Canvas2DNodeLabelGestureOperation.Resize;
        }

        var visualStateId = new VisualStateId(visualIdValue);
        var nodeSceneObjectId = new SceneObjectId(nodeIdValue);
        var labelSceneObjectId = new SceneObjectId(labelSceneIdValue);
        var projectedLabelObjectId = new ProjectedObjectId(labelIdValue);
        labelProjectedObjectId = projectedLabelObjectId;
        nodeTarget = scene.Items.SingleOrDefault(item =>
            item.Id == nodeSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)!;
        labelTarget = scene.Items.SingleOrDefault(item =>
            item.Id == labelSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId == projectedLabelObjectId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            HasBooleanMetadata(
                item,
                Canvas2DNodeLabelGestureMetadata.InteractionCapable))!;
        if (nodeTarget is null || labelTarget is null ||
            labelTarget.Bounds.IsEmpty ||
            !labelTarget.Origin.RelatedSceneObjectIds.Contains(nodeSceneObjectId))
        {
            return false;
        }

        if (operation == Canvas2DNodeLabelGestureOperation.Move)
        {
            return hitItem.Id == labelTarget.Id &&
                (hitItem.Origin.Categories &
                    Canvas2DSceneOriginCategory.EditorState) == 0;
        }

        return editorState.Selection.Length == 1 &&
            editorState.Selection.Contains(visualStateId) &&
            (hitItem.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
            hitItem.Origin.VisualStateId == visualStateId &&
            hitItem.Origin.ProjectedObjectId == projectedLabelObjectId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(labelTarget.Id) &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(nodeTarget.Id);
    }

    private static bool TryResolveRouteTarget(
        Canvas2DScene scene,
        EditorStateSnapshot editorState,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem target,
        out int bendIndex)
    {
        target = null!;
        bendIndex = -1;
        if ((hitItem.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.HandleRole,
                out var role) ||
            role.Kind != PropertyValueKind.Text ||
            !StringComparer.Ordinal.Equals(role.TextValue, Canvas2DRouteGestureMetadata.BendRole) ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.TargetSceneObjectId,
                out var targetIdValue) ||
            targetIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            visualIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.BendIndex,
                out var bendIndexValue) ||
            bendIndexValue.Kind != PropertyValueKind.Integer ||
            bendIndexValue.IntegerValue is < 1 or > int.MaxValue)
        {
            return false;
        }

        var targetId = new SceneObjectId(targetIdValue.TextValue);
        bendIndex = (int)bendIndexValue.IntegerValue;
        target = scene.Items.SingleOrDefault(item => item.Id == targetId)!;
        return target is not null &&
            editorState.Selection.Length == 1 &&
            editorState.Selection.Contains(target.Origin.VisualStateId!) &&
            target.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            bendIndex < Canvas2DConnectorPathMetadata.ResolveEditable(target).Length - 1 &&
            StringComparer.Ordinal.Equals(
                target.Origin.VisualStateId?.Value,
                visualIdValue.TextValue) &&
            hitItem.Origin.VisualStateId == target.Origin.VisualStateId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(targetId) &&
            HasBooleanMetadata(target, Canvas2DRouteGestureMetadata.RouteEditable);
    }

    private static bool TryResolveLabelTarget(
        Canvas2DScene scene,
        EditorStateSnapshot editorState,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem target,
        out ProjectedObjectId? labelProjectedObjectId)
    {
        target = null!;
        labelProjectedObjectId = null;
        if ((hitItem.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 ||
            hitItem.Layer != Canvas2DSceneLayer.Label ||
            !HasBooleanMetadata(hitItem, Canvas2DLabelGestureMetadata.LabelMoveCapable) ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DLabelGestureMetadata.TargetConnectorSceneObjectId,
                out var targetIdValue) ||
            targetIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DLabelGestureMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            visualIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId,
                out var labelIdValue) ||
            labelIdValue.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        var targetId = new SceneObjectId(targetIdValue.TextValue);
        var visualStateId = new VisualStateId(visualIdValue.TextValue);
        labelProjectedObjectId = new ProjectedObjectId(labelIdValue.TextValue);
        target = scene.Items.SingleOrDefault(item => item.Id == targetId)!;
        return target is not null &&
            editorState.Selection.Contains(visualStateId) &&
            target.Layer == Canvas2DSceneLayer.Connector &&
            target.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            Canvas2DConnectorPathMetadata.Resolve(target).Length >= 2 &&
            target.Origin.VisualStateId == visualStateId &&
            hitItem.Origin.VisualStateId == visualStateId &&
            hitItem.Origin.ProjectedObjectId == labelProjectedObjectId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(targetId);
    }

    private static Canvas2DNodeLabelContextAction? ResolveNodeLabelContextAction(
        Canvas2DScene scene,
        Canvas2DSceneHitTestResult? hit)
    {
        if (hit is null)
        {
            return null;
        }

        var hitItem = scene.Items.Single(item => item.Id == hit.SceneObjectId);
        if (!TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                out var labelSceneIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                out var nodeSceneIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                out var visualStateIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                out var projectedLabelIdValue))
        {
            return null;
        }

        var labelSceneId = new SceneObjectId(labelSceneIdValue);
        var nodeSceneId = new SceneObjectId(nodeSceneIdValue);
        var visualStateId = new VisualStateId(visualStateIdValue);
        var projectedLabelId = new ProjectedObjectId(projectedLabelIdValue);
        var labelTarget = scene.Items.SingleOrDefault(item =>
            item.Id == labelSceneId &&
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId == projectedLabelId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            HasBooleanMetadata(
                item,
                Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        var nodeTarget = scene.Items.SingleOrDefault(item =>
            item.Id == nodeSceneId &&
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        return labelTarget is not null &&
            nodeTarget is not null &&
            labelTarget.Origin.RelatedSceneObjectIds.Contains(nodeSceneId) &&
            NodeLabelVisualOverride.TryRead(labelTarget.PersistentAppearance, out _)
                ? new Canvas2DNodeLabelContextAction(
                    visualStateId,
                    projectedLabelId,
                    labelSceneId,
                    nodeSceneId)
                : null;
    }

    private static bool HasBooleanMetadata(Canvas2DSceneItem item, string key) =>
        item.Metadata.TryGetValue(key, out var value) &&
        value.Kind == PropertyValueKind.Boolean &&
        value.BooleanValue;

    private static bool IsConnectorEndpointHandle(Canvas2DSceneItem item) =>
        (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
        item.Metadata.TryGetValue(
            Canvas2DConnectorEndpointMetadata.HandleRole,
            out var role) &&
        role.Kind == PropertyValueKind.Text &&
        (StringComparer.Ordinal.Equals(
             role.TextValue,
             Canvas2DConnectorEndpointMetadata.StartEndpointRole) ||
         StringComparer.Ordinal.Equals(
             role.TextValue,
             Canvas2DConnectorEndpointMetadata.EndEndpointRole));

    private static bool IsConnectorAnchorHandle(Canvas2DSceneItem item) =>
        (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
        item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var anchorId) &&
        anchorId.Kind == PropertyValueKind.Text &&
        !string.IsNullOrWhiteSpace(anchorId.TextValue) &&
        item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.Side, out var side) &&
        side.Kind == PropertyValueKind.Text &&
        item.Metadata.TryGetValue(
            Canvas2DConnectorAnchorMetadata.RoleCapability,
            out var roleCapability) &&
        roleCapability.Kind == PropertyValueKind.Integer &&
        item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorKind, out var anchorKind) &&
        anchorKind.Kind == PropertyValueKind.Integer;

    private Canvas2DConnectorAnchorContextAction?
        ResolveConnectorAnchorContextAction(
            EditingSessionState observed,
            Canvas2DSceneHitTestResult? hitResult)
    {
        if (hitResult is null || observed.CurrentScene is null)
        {
            return null;
        }

        var scene = observed.CurrentScene;
        var hitItem = scene.Items.Single(item => item.Id == hitResult.SceneObjectId);
        if (TryResolveConnectorAnchorHandle(
                scene,
                observed.EditorState,
                hitItem,
                out var anchorTarget,
                out var anchorId,
                out var side,
                out var anchorKind,
                out var roleCapability,
                out var role,
                out var canDelete))
        {
            if (anchorKind == ResolvedConnectorAnchorKind.Predefined)
            {
                return null;
            }

            var visualStateId = anchorTarget.Origin.VisualStateId!;
            if (!_session.TryCaptureAnchorPolicyForInteraction(
                scene,
                observed.Generation,
                observed.EditorState,
                visualStateId,
                out var visualState,
                out var policy,
                out var visualModel) ||
                visualState is null ||
                policy is null ||
                visualModel is null)
            {
                return null;
            }

            var anchor = visualState.ConnectorAnchors.SingleOrDefault(candidate =>
                candidate.Id == anchorId);
            if (anchor is null ||
                role is null ||
                anchor.Side != side ||
                anchor.Role != role ||
                !Allows(roleCapability, role.Value))
            {
                return null;
            }

            canDelete = canDelete &&
                ElementConnectorAnchorPolicyEvaluator.CanRemove(
                    policy,
                    visualState,
                    anchorId) &&
                !ConnectorAnchorOccupancy.IsOccupied(visualModel, anchorId);

            var sideCount = visualState.ConnectorAnchors.Count(candidate =>
                candidate.Side == side);
            if (anchor.Order < 0 || anchor.Order >= sideCount)
            {
                return null;
            }

            return new Canvas2DConnectorAnchorContextAction(
                Canvas2DConnectorAnchorContextActionKind.DeleteAnchor,
                visualStateId,
                hitItem.Id,
                anchorTarget.Id,
                side,
                anchor.Order,
                ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(
                    anchor.Order,
                    sideCount),
                anchorId,
                role.Value,
                canDelete);
        }

        if (!TryResolveConnectorAnchorAddTarget(
                scene,
                observed.EditorState,
                hitItem,
                hitResult.DocumentPoint,
                out var edgeTarget,
                out side) ||
            edgeTarget.Origin.VisualStateId is not { } targetVisualStateId)
        {
            return null;
        }

        if (!_session.TryCaptureAnchorPolicyForInteraction(
            scene,
            observed.Generation,
            observed.EditorState,
            targetVisualStateId,
            out var targetVisualState,
            out var targetPolicy,
            out _) ||
            targetVisualState is null ||
            targetPolicy is null)
        {
            return null;
        }

        var allowedRoles = ConnectorAnchorRoleCapability.None;
        if (ElementConnectorAnchorPolicyEvaluator.CanAdd(
                targetPolicy,
                targetVisualState,
                side,
                ConnectorAnchorRole.Source))
        {
            allowedRoles |= ConnectorAnchorRoleCapability.Source;
        }

        if (ElementConnectorAnchorPolicyEvaluator.CanAdd(
                targetPolicy,
                targetVisualState,
                side,
                ConnectorAnchorRole.Target))
        {
            allowedRoles |= ConnectorAnchorRoleCapability.Target;
        }

        if (allowedRoles == ConnectorAnchorRoleCapability.None)
        {
            return null;
        }

        var edgeParameter = ConnectorAnchorGeometryResolver.ResolveEdgeParameter(
            edgeTarget.Bounds,
            side,
            hitResult.DocumentPoint);
        var insertionIndex = ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            targetVisualState.ConnectorAnchors,
            side,
            edgeParameter);
        return new Canvas2DConnectorAnchorContextAction(
            Canvas2DConnectorAnchorContextActionKind.AddAnchor,
            targetVisualStateId,
            hitItem.Id,
            edgeTarget.Id,
            side,
            insertionIndex,
            edgeParameter,
            allowedRoles: allowedRoles);
    }

    private static bool TryResolveConnectorAnchorAddTarget(
        Canvas2DScene scene,
        EditorStateSnapshot editorState,
        Canvas2DSceneItem hitItem,
        PointD documentPoint,
        out Canvas2DSceneItem target,
        out ConnectorAnchorSide side)
    {
        side = default;
        if (TryResolveResizeTarget(
                scene,
                editorState,
                hitItem,
                out target,
                out var direction))
        {
            return !Canvas2DResizeGeometry.IsCorner(direction) &&
                TryMapAnchorSide(direction, out side);
        }

        target = hitItem;
        if (!Canvas2DNodeBodyMetadata.IsNodeBody(target) ||
            target.Bounds.IsEmpty ||
            target.Origin.VisualStateId is not { } visualStateId ||
            editorState.Selection.Length != 1 ||
            !editorState.Selection.Contains(visualStateId) ||
            (target.Metadata.TryGetValue(
                 Canvas2DResizeGestureMetadata.ResizeCapable,
                 out var resizeCapable) &&
             (resizeCapable.Kind != PropertyValueKind.Boolean ||
              resizeCapable.BooleanValue)))
        {
            return false;
        }

        foreach (var candidate in Canvas2DResizeGeometry.Directions)
        {
            if (Canvas2DResizeGeometry.IsCorner(candidate) &&
                Canvas2DResizeGeometry.InteractionBounds(target.Bounds, candidate)
                    .Contains(documentPoint))
            {
                return false;
            }
        }

        foreach (var candidate in Canvas2DResizeGeometry.Directions)
        {
            if (!Canvas2DResizeGeometry.IsCorner(candidate) &&
                Canvas2DResizeGeometry.InteractionBounds(target.Bounds, candidate)
                    .Contains(documentPoint) &&
                TryMapAnchorSide(candidate, out side))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveConnectorAnchorHandle(
        Canvas2DScene scene,
        EditorStateSnapshot editorState,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem target,
        out ConnectorAnchorId anchorId,
        out ConnectorAnchorSide side,
        out ResolvedConnectorAnchorKind anchorKind,
        out ConnectorAnchorRoleCapability roleCapability,
        out ConnectorAnchorRole? role,
        out bool canDelete,
        bool requireSelectedOwner = true)
    {
        target = null!;
        anchorId = null!;
        side = default;
        anchorKind = default;
        roleCapability = default;
        role = default;
        canDelete = false;
        if (!IsConnectorAnchorHandle(hitItem) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorAnchorMetadata.AnchorId,
                out var anchorIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorAnchorMetadata.Side,
                out var sideValue) ||
            !Enum.TryParse(sideValue, ignoreCase: false, out side) ||
            !Enum.IsDefined(side) ||
            !TryGetIntegerMetadata(
                hitItem,
                Canvas2DConnectorAnchorMetadata.RoleCapability,
                out var roleCapabilityValue) ||
            !TryParseRoleCapability(roleCapabilityValue, out roleCapability) ||
            !TryGetIntegerMetadata(
                hitItem,
                Canvas2DConnectorAnchorMetadata.AnchorKind,
                out var anchorKindValue) ||
            !TryParseAnchorKind(anchorKindValue, out anchorKind) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorAnchorMetadata.TargetSceneObjectId,
                out var targetIdValue) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorAnchorMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DConnectorAnchorMetadata.DeleteCapable,
                out var deleteCapable) ||
            deleteCapable.Kind != PropertyValueKind.Boolean)
        {
            return false;
        }

        if (anchorKind == ResolvedConnectorAnchorKind.Dynamic)
        {
            if (!TryGetTextMetadata(
                    hitItem,
                    Canvas2DConnectorAnchorMetadata.Role,
                    out var roleValue) ||
                !Enum.TryParse(roleValue, ignoreCase: false, out ConnectorAnchorRole parsedRole) ||
                !Enum.IsDefined(parsedRole) ||
                !Allows(roleCapability, parsedRole))
            {
                return false;
            }

            role = parsedRole;
        }

        anchorId = new ConnectorAnchorId(anchorIdValue);
        var targetId = new SceneObjectId(targetIdValue);
        target = scene.Items.SingleOrDefault(item => item.Id == targetId)!;
        canDelete = deleteCapable.BooleanValue;
        return target is not null &&
            target.Layer == Canvas2DSceneLayer.Content &&
            target.Origin.VisualStateId is { } targetVisualStateId &&
            (!requireSelectedOwner || editorState.Selection.Contains(targetVisualStateId)) &&
            StringComparer.Ordinal.Equals(targetVisualStateId.Value, visualIdValue) &&
            hitItem.Origin.VisualStateId == targetVisualStateId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(targetId);
    }

    private bool TryResolveConnectionTarget(
        EditingSessionState observed,
        Canvas2DSceneHitTestResult? hitResult,
        AnchorConnectionGestureState connection,
        out AnchorConnectionTarget target,
        out ConnectorAnchorId? proposedAnchorId)
    {
        target = null!;
        proposedAnchorId = connection.ProposedAnchorId;
        var stableProposedAnchorId = proposedAnchorId;
        if (hitResult is null || observed.CurrentScene is null)
        {
            return false;
        }

        var hitItem = observed.CurrentScene.Items.SingleOrDefault(
            item => item.Id == hitResult.SceneObjectId);
        if (hitItem is null)
        {
            return false;
        }

        if (hitItem.Origin.VisualStateId is not null &&
            !IsEditableVisualPresentation(hitItem))
        {
            return false;
        }

        if (TryResolveConnectorAnchorHandle(
                observed.CurrentScene,
                observed.EditorState,
                hitItem,
                out var ownerTarget,
                out var anchorId,
                out var side,
                out var anchorKind,
                out _,
                out var role,
                out _,
                requireSelectedOwner: false) &&
            anchorKind == ResolvedConnectorAnchorKind.Dynamic &&
            role == ConnectorAnchorRole.Target &&
            ownerTarget.Origin.SemanticElementId is { } exactSemanticElementId &&
            ownerTarget.Origin.VisualStateId is { } exactVisualStateId &&
            IsEditableVisualPresentation(ownerTarget) &&
            _session.TryCaptureDocumentSnapshot(out var exactDocument) &&
            exactDocument.DocumentId == observed.DocumentId &&
            exactDocument.Revision == observed.DocumentRevision &&
            TryResolveAvailablePersistentAnchor(
                exactDocument,
                exactSemanticElementId,
                exactVisualStateId,
                anchorId,
                ConnectorAnchorRole.Target) &&
            CanAcquireConnectionTarget(
                connection,
                exactDocument,
                exactSemanticElementId,
                exactVisualStateId))
        {
            var documentPoint = MapSceneToProcessLocal(ownerTarget, Center(hitItem.Bounds));
            target = new AnchorConnectionTarget(
                TargetAnchorAcquisitionResult.Existing(
                    exactSemanticElementId,
                    exactVisualStateId,
                    side,
                    anchorId,
                    documentPoint),
                MapSceneToProcessLocal(ownerTarget, hitResult.DocumentPoint),
                ownerTarget.SpatialRegion?.MapSceneToLocal(ownerTarget.Bounds) ?? ownerTarget.Bounds,
                IsSmart: false,
                ownerTarget.SpatialRegion);
            return true;
        }

        if (connection.Registration.TargetEligibility is not { } eligibility ||
            _creationIdentityProvider is null ||
            !Canvas2DNodeBodyMetadata.IsNodeBody(hitItem) ||
            !IsEditableVisualPresentation(hitItem) ||
            hitItem.Origin.SemanticElementId is not { } semanticElementId ||
            hitItem.Origin.VisualStateId is not { } visualStateId ||
            !_session.TryCaptureDocumentSnapshot(out var document) ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !_session.TryCaptureAnchorPolicyForInteraction(
                observed.CurrentScene,
                observed.Generation,
                observed.EditorState,
                visualStateId,
                out var visual,
                out var policy,
                out _) ||
            visual is null ||
            visual.SemanticElementId != semanticElementId ||
            policy is null)
        {
            return false;
        }

        try
        {
            if (!eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
                    document,
                    observed.DocumentRevision,
                    connection.SourceSemanticElementId,
                    connection.SourceVisualStateId,
                    connection.SourceAnchorId,
                    semanticElementId,
                    visualStateId)))
            {
                return false;
            }

            var acquisition = ConnectorTargetAnchorAcquisition.Acquire(
                new TargetAnchorAcquisitionRequest(
                    document,
                    observed.DocumentRevision,
                    semanticElementId,
                    visualStateId,
                    hitItem.SpatialRegion?.MapSceneToLocal(hitItem.Bounds) ?? hitItem.Bounds,
                    MapSceneToProcessLocal(hitItem, hitResult.DocumentPoint),
                    policy),
                () => stableProposedAnchorId ??=
                    _creationIdentityProvider.CreateConnectorAnchorId());
            if (!acquisition.IsAccepted)
            {
                return false;
            }

            target = new AnchorConnectionTarget(
                acquisition,
                MapSceneToProcessLocal(hitItem, hitResult.DocumentPoint),
                hitItem.SpatialRegion?.MapSceneToLocal(hitItem.Bounds) ?? hitItem.Bounds,
                IsSmart: true,
                hitItem.SpatialRegion);
            proposedAnchorId = stableProposedAnchorId;
        }
#pragma warning disable CA1031 // Optional plugin acquisition failures reject the transient target.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return false;
        }
#pragma warning restore CA1031

        return true;
    }

    private bool IsTargetAcquisitionCurrent(
        EditingSessionState observed,
        DocumentSnapshot document,
        AnchorConnectionGestureState connection,
        AnchorConnectionTarget target)
    {
        var acquisition = target.Acquisition;
        if (!CanAcquireConnectionTarget(
                connection,
                document,
                acquisition.TargetSemanticElementId!,
                acquisition.TargetVisualStateId!))
        {
            return false;
        }

        if (!target.IsSmart || acquisition.Kind == TargetAnchorAcquisitionKind.Existing)
        {
            return TryResolveAvailablePersistentAnchor(
                document,
                acquisition.TargetSemanticElementId!,
                acquisition.TargetVisualStateId!,
                acquisition.AnchorId!,
                ConnectorAnchorRole.Target);
        }

        if (connection.Registration.TargetEligibility is not { } eligibility ||
            acquisition.Kind != TargetAnchorAcquisitionKind.Proposed ||
            observed.CurrentScene is null ||
            !_session.TryCaptureAnchorPolicyForInteraction(
                observed.CurrentScene,
                observed.Generation,
                observed.EditorState,
                acquisition.TargetVisualStateId!,
                out var targetVisual,
                out var policy,
                out _) ||
            targetVisual?.SemanticElementId != acquisition.TargetSemanticElementId ||
            policy is null)
        {
            return false;
        }

        try
        {
            if (!eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
                    document,
                    document.Revision,
                    connection.SourceSemanticElementId,
                    connection.SourceVisualStateId,
                    connection.SourceAnchorId,
                    acquisition.TargetSemanticElementId!,
                    acquisition.TargetVisualStateId!)))
            {
                return false;
            }

            var current = ConnectorTargetAnchorAcquisition.Acquire(
                new TargetAnchorAcquisitionRequest(
                    document,
                    document.Revision,
                    acquisition.TargetSemanticElementId!,
                    acquisition.TargetVisualStateId!,
                    target.TargetBounds,
                    target.PointerDocumentPoint,
                    policy),
                () => acquisition.AnchorId!);
            return current.Equals(acquisition);
        }
#pragma warning disable CA1031 // Optional plugin acquisition failures invalidate the proposal.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static bool TryResolveAvailablePersistentAnchor(
        DocumentSnapshot document,
        SemanticElementId semanticElementId,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorRole requiredRole) =>
        TryResolvePersistentAnchor(
            document,
            semanticElementId,
            visualStateId,
            anchorId,
            requiredRole) &&
        !ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchorId);

    private static bool CanAcquireConnectionTarget(
        AnchorConnectionGestureState connection,
        DocumentSnapshot document,
        SemanticElementId semanticElementId,
        VisualStateId visualStateId)
    {
        if (connection.Registration.TargetEligibility is not { } eligibility)
        {
            return true;
        }

#pragma warning disable CA1031 // Optional plugin eligibility rejects the target on failure.
        try
        {
            return eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
                document,
                document.Revision,
                connection.SourceSemanticElementId,
                connection.SourceVisualStateId,
                connection.SourceAnchorId,
                semanticElementId,
                visualStateId));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static bool TryResolvePersistentAnchor(
        DocumentSnapshot document,
        SemanticElementId semanticElementId,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorRole requiredRole)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.VisualModel.TryGetVisualState(visualStateId, out var visualState) &&
            visualState is not null &&
            visualState.SemanticElementId == semanticElementId &&
            visualState.ConnectorAnchors.Any(anchor =>
                anchor.Id == anchorId && anchor.Role == requiredRole);
    }

    private static bool TryParseRoleCapability(
        long value,
        out ConnectorAnchorRoleCapability capability)
    {
        capability = value switch
        {
            (long)ConnectorAnchorRoleCapability.Source =>
                ConnectorAnchorRoleCapability.Source,
            (long)ConnectorAnchorRoleCapability.Target =>
                ConnectorAnchorRoleCapability.Target,
            (long)ConnectorAnchorRoleCapability.SourceOrTarget =>
                ConnectorAnchorRoleCapability.SourceOrTarget,
            _ => ConnectorAnchorRoleCapability.None,
        };
        return capability != ConnectorAnchorRoleCapability.None;
    }

    private static bool TryParseAnchorKind(
        long value,
        out ResolvedConnectorAnchorKind kind)
    {
        if (value == (long)ResolvedConnectorAnchorKind.Dynamic)
        {
            kind = ResolvedConnectorAnchorKind.Dynamic;
            return true;
        }

        if (value == (long)ResolvedConnectorAnchorKind.Predefined)
        {
            kind = ResolvedConnectorAnchorKind.Predefined;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool Allows(
        ConnectorAnchorRoleCapability capability,
        ConnectorAnchorRole role)
    {
        var required = role switch
        {
            ConnectorAnchorRole.Source => ConnectorAnchorRoleCapability.Source,
            ConnectorAnchorRole.Target => ConnectorAnchorRoleCapability.Target,
            _ => ConnectorAnchorRoleCapability.None,
        };
        return required != ConnectorAnchorRoleCapability.None &&
            (capability & required) != ConnectorAnchorRoleCapability.None;
    }

    private static bool TryGetIntegerMetadata(
        Canvas2DSceneItem item,
        string key,
        out long value)
    {
        if (item.Metadata.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Integer)
        {
            value = property.IntegerValue;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetTextMetadata(
        Canvas2DSceneItem item,
        string key,
        out string value)
    {
        if (item.Metadata.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryMapAnchorSide(
        Canvas2DResizeDirection direction,
        out ConnectorAnchorSide side)
    {
        side = direction switch
        {
            Canvas2DResizeDirection.North => ConnectorAnchorSide.Top,
            Canvas2DResizeDirection.East => ConnectorAnchorSide.Right,
            Canvas2DResizeDirection.South => ConnectorAnchorSide.Bottom,
            Canvas2DResizeDirection.West => ConnectorAnchorSide.Left,
            _ => default,
        };
        return direction is
            Canvas2DResizeDirection.North or
            Canvas2DResizeDirection.East or
            Canvas2DResizeDirection.South or
            Canvas2DResizeDirection.West;
    }

    private static Canvas2DConnectorRouteContextAction?
        ResolveConnectorRouteContextAction(
            Canvas2DScene scene,
            Canvas2DSceneHitTestResult? hitResult)
    {
        if (hitResult is null)
        {
            return null;
        }

        var hitItem = scene.Items.Single(item => item.Id == hitResult.SceneObjectId);
        if (IsConnectorEndpointHandle(hitItem))
        {
            return null;
        }

        if (TryResolveContextBend(scene, hitItem, out var bendTarget, out var bendIndex))
        {
            var editableRoute = Canvas2DConnectorPathMetadata.ResolveEditable(bendTarget);
            var routePoint = bendTarget.Transform.TransformPoint(editableRoute[bendIndex]);
            return new Canvas2DConnectorRouteContextAction(
                Canvas2DConnectorRouteContextActionKind.DeletePoint,
                bendTarget.Origin.VisualStateId!,
                bendTarget.Id,
                bendIndex,
                MapSceneToProcessLocal(hitItem, hitResult.DocumentPoint),
                MapSceneToProcessLocal(bendTarget, routePoint));
        }

        var processLocalHitPoint = MapSceneToProcessLocal(hitItem, hitResult.DocumentPoint);
        if (!DocumentGeometryBoundary.Contains(processLocalHitPoint))
        {
            return null;
        }

        if (hitItem.Layer != Canvas2DSceneLayer.Connector ||
            hitItem.Origin.VisualStateId is not { } visualStateId ||
            hitItem.Geometry.Kind != Canvas2DSceneGeometryKind.Path ||
            HasBooleanMetadata(hitItem, Canvas2DConnectorArrowMetadata.TargetArrow))
        {
            return null;
        }

        var documentPath = Canvas2DConnectorPathMetadata.Resolve(hitItem)
            .Select(hitItem.Transform.TransformPoint)
            .ToImmutableArray();
        if (documentPath.Length < 2)
        {
            return null;
        }

        var projection = Canvas2DConnectorPathGeometry.FindNearest(
            documentPath,
            hitResult.DocumentPoint);
        var pointTolerance = Canvas2DConnectorInteractionConfiguration.Default
            .RoutePointProximityTolerance;
        var pointToleranceSquared = pointTolerance * pointTolerance;
        IEnumerable<PointD> protectedPoints = HasBooleanMetadata(
                hitItem,
                Canvas2DRouteGestureMetadata.RouteEditable)
            ? Canvas2DConnectorPathMetadata.ResolveEditable(hitItem)
                .Select(hitItem.Transform.TransformPoint)
            : [documentPath[0], documentPath[^1]];
        if (protectedPoints.Any(point =>
            DistanceSquared(point, projection.RoutePoint) <= pointToleranceSquared))
        {
            return null;
        }

        return new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            visualStateId,
            hitItem.Id,
            projection.SegmentIndex,
            processLocalHitPoint,
            MapSceneToProcessLocal(hitItem, projection.RoutePoint));
    }

    private static PointD MapSceneToProcessLocal(Canvas2DSceneItem item, PointD point) =>
        item.ConnectorPresentationMapping?.MapSceneGuidanceToCanonical(point) ??
        item.SpatialRegion?.MapSceneToLocal(point) ?? point;

    private static bool TryResolveContextBend(
        Canvas2DScene scene,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem target,
        out int bendIndex)
    {
        target = null!;
        bendIndex = -1;
        if ((hitItem.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.HandleRole,
                out var role) ||
            role.Kind != PropertyValueKind.Text ||
            !StringComparer.Ordinal.Equals(role.TextValue, Canvas2DRouteGestureMetadata.BendRole) ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.TargetSceneObjectId,
                out var targetIdValue) ||
            targetIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            visualIdValue.Kind != PropertyValueKind.Text ||
            !hitItem.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.BendIndex,
                out var bendIndexValue) ||
            bendIndexValue.Kind != PropertyValueKind.Integer ||
            bendIndexValue.IntegerValue is < 1 or > int.MaxValue)
        {
            return false;
        }

        bendIndex = (int)bendIndexValue.IntegerValue;
        var targetId = new SceneObjectId(targetIdValue.TextValue);
        target = scene.Items.SingleOrDefault(item => item.Id == targetId)!;
        return target is not null &&
            target.Origin.VisualStateId is { } targetVisualStateId &&
            StringComparer.Ordinal.Equals(targetVisualStateId.Value, visualIdValue.TextValue) &&
            target.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            bendIndex < Canvas2DConnectorPathMetadata.ResolveEditable(target).Length - 1 &&
            hitItem.Origin.VisualStateId == targetVisualStateId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(target.Id);
    }

    private static double DistanceSquared(PointD left, PointD right)
    {
        var delta = left - right;
        return (delta.X * delta.X) + (delta.Y * delta.Y);
    }

    private static VisualStateId? ResolveSelectableVisualStateId(
        Canvas2DScene scene,
        Canvas2DSceneHitTestResult? hitResult)
    {
        if (hitResult?.Origin.VisualStateId is not { } visualStateId)
        {
            return null;
        }

        return ResolveCanonicalSceneTarget(
            scene,
            visualStateId,
            requireMoveCapability: false) is null
                ? null
                : visualStateId;
    }

    private static SemanticElementId? ResolveSelectableSemanticElementId(
        Canvas2DScene scene,
        Canvas2DSceneHitTestResult? hitResult)
    {
        if (hitResult?.Origin.SemanticElementId is not { } semanticElementId ||
            hitResult.Origin.VisualStateId is not null)
        {
            return null;
        }

        var hitItem = scene.Items.SingleOrDefault(item => item.Id == hitResult.SceneObjectId);
        return hitItem is not null &&
            hitItem.IsVisible &&
            hitItem.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
            Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(hitItem)
                ? semanticElementId
                : null;
    }

    private static Canvas2DSceneItem? ResolveCanonicalSceneTarget(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        bool requireMoveCapability) =>
        scene.Items
            .Where(item =>
                item.Origin.VisualStateId == visualStateId &&
                IsEditableVisualPresentation(item) &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                (!requireMoveCapability || HasBooleanMetadata(
                    item,
                    Canvas2DMoveGestureMetadata.MoveCapable)))
            .OrderBy(static item => InteractionTargetRank(item.Layer))
            .ThenBy(static item => item.ZIndex)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool IsEditableVisualPresentation(Canvas2DSceneItem item) =>
        item.IsVisible && item.Origin.VisualStateId is not null;

    private static bool TryResolveBoundaryAttachmentContext(
        EditingSessionState state,
        Canvas2DSceneItem target,
        out ProjectedBoundaryAttachment? attachment,
        out Canvas2DSceneItem? ownerTarget)
    {
        attachment = null;
        ownerTarget = null;
        if (!state.TryGetCurrentProcessInteraction(out var scopeScene) ||
            scopeScene is null ||
            state.CurrentScene is null ||
            target.Origin.ProjectedObjectId is not { } projectedObjectId)
        {
            return false;
        }

        var node = scopeScene.ProjectedGraph!.Nodes.SingleOrDefault(candidate =>
            candidate.Id == projectedObjectId);
        var resolvedAttachment = node?.GeometryInteractionPolicy ==
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize
            ? node.PlacementHint?.BoundaryAttachment
            : null;
        if (resolvedAttachment is null)
        {
            return false;
        }

        var ownerNode = scopeScene.ProjectedGraph!.Nodes.SingleOrDefault(candidate =>
            candidate.Source.SemanticElementId == resolvedAttachment.AttachedToElementId &&
            candidate.Source.VisualStateId is not null);
        ownerTarget = ownerNode?.Source.VisualStateId is { } ownerVisualStateId
            ? ResolveCanonicalSceneTarget(
                state.CurrentScene,
                ownerVisualStateId,
                requireMoveCapability: false)
            : null;
        attachment = resolvedAttachment;
        return ownerTarget is not null && !ownerTarget.Bounds.IsEmpty;
    }

    private static bool IsAttachedFixedSizeNode(
        EditingSessionState state,
        Canvas2DSceneItem target) =>
        state.TryGetCurrentProcessInteraction(out var scopeScene) &&
        scopeScene is not null &&
        target.Origin.ProjectedObjectId is { } projectedObjectId &&
        scopeScene.ProjectedGraph!.Nodes.Any(node =>
            node.Id == projectedObjectId &&
            node.GeometryInteractionPolicy ==
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);

    private static IEnumerable<Canvas2DSceneItem> ResolveAttachedDependentTargets(
        EditingSessionState state,
        IEnumerable<Canvas2DSceneItem> ownerTargets)
    {
        var owners = ownerTargets.ToArray();
        if (!state.TryGetCurrentProcessInteraction(out var scopeScene) ||
            scopeScene is null || state.CurrentScene is null)
        {
            yield break;
        }

        var ownerSemanticIds = owners
            .Select(static target => target.Origin.SemanticElementId)
            .Where(static id => id is not null)
            .Cast<SemanticElementId>()
            .ToHashSet();
        foreach (var node in scopeScene.ProjectedGraph!.Nodes
                     .Where(node =>
                         node.PlacementHint?.BoundaryAttachment is { } attachment &&
                         ownerSemanticIds.Contains(attachment.AttachedToElementId) &&
                         node.Source.VisualStateId is not null)
                     .OrderBy(static node => node.Source.VisualStateId!.Value,
                         StringComparer.Ordinal))
        {
            var target = ResolveCanonicalSceneTarget(
                state.CurrentScene,
                node.Source.VisualStateId!,
                requireMoveCapability: false);
            if (target is not null)
            {
                yield return target;
            }
        }
    }

    private static int InteractionTargetRank(Canvas2DSceneLayer layer) => layer switch
    {
        Canvas2DSceneLayer.Content => 0,
        Canvas2DSceneLayer.Connector => 1,
        Canvas2DSceneLayer.Label => 2,
        Canvas2DSceneLayer.Decoration => 3,
        Canvas2DSceneLayer.Background => 4,
        _ => 5,
    };

    private static string CssCursor(
        Canvas2DScene scene,
        Canvas2DSceneHitTestResult? hitResult)
    {
        if (hitResult is null)
        {
            return "default";
        }

        var item = scene.Items.Single(sceneItem => sceneItem.Id == hitResult.SceneObjectId);

        if (item.Metadata.TryGetValue(
                Canvas2DNodeLabelGestureMetadata.ResizeDirection,
                out var labelResizeRole) &&
            labelResizeRole.Kind == PropertyValueKind.Text &&
            Canvas2DResizeGeometry.TryParseRole(
                labelResizeRole.TextValue,
                out var labelResizeDirection))
        {
            return Canvas2DResizeGeometry.CssCursor(labelResizeDirection);
        }

        if (item.Metadata.TryGetValue(Canvas2DResizeGestureMetadata.HandleRole, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            Canvas2DResizeGeometry.TryParseRole(role.TextValue, out var direction))
        {
            return Canvas2DResizeGeometry.CssCursor(direction);
        }

        if (item.Metadata.TryGetValue(Canvas2DRouteGestureMetadata.HandleRole, out var routeRole) &&
            routeRole.Kind == PropertyValueKind.Text)
        {
            return "pointer";
        }

        if (HasBooleanMetadata(item, Canvas2DLabelGestureMetadata.LabelMoveCapable))
        {
            return "grab";
        }

        if (HasBooleanMetadata(
                item,
                Canvas2DNodeLabelGestureMetadata.InteractionCapable))
        {
            return "grab";
        }

        if (HasBooleanMetadata(item, Canvas2DMoveGestureMetadata.MoveCapable))
        {
            return "grab";
        }

        return item.Origin.VisualStateId is null ? "default" : "pointer";
    }

    private static string CssCursor(PersistentGestureState gesture) =>
        gesture.Kind switch
        {
            PersistentGestureKind.AnchorConnectionCreation => "crosshair",
            PersistentGestureKind.ConnectorEndpointReconnection => "crosshair",
            PersistentGestureKind.Resize =>
                Canvas2DResizeGeometry.CssCursor(gesture.ResizeDirection),
            PersistentGestureKind.Move => "grabbing",
            PersistentGestureKind.BoundaryAttachmentMove => "grabbing",
            PersistentGestureKind.LabelMove => "grabbing",
            PersistentGestureKind.NodeLabelEdit when
                gesture.NodeLabelOperation == Canvas2DNodeLabelGestureOperation.Resize =>
                Canvas2DResizeGeometry.CssCursor(gesture.ResizeDirection),
            PersistentGestureKind.NodeLabelEdit => "grabbing",
            _ => "pointer",
        };

    private PointerPresentation ResolvePointerPresentation(
        EditingSessionState state,
        PointD cssPoint)
    {
        if (!state.IsGraphicalInteractionEnabled || state.CurrentScene is null)
        {
            return new PointerPresentation(null, "default");
        }

#pragma warning disable CA1031 // Completion is authoritative; presentation refresh falls back safely.
        try
        {
            var documentPoint = Canvas2DRenderer.ConvertCssToDocument(
                state.CurrentScene,
                cssPoint);
            var hit = _hitTestService.HitTest(state.CurrentScene, documentPoint);
            return new PointerPresentation(hit, CssCursor(state.CurrentScene, hit));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return new PointerPresentation(null, "default");
        }
#pragma warning restore CA1031
    }

    private static bool TryCalculatePersistentGestureGeometry(
        PersistentGestureState gesture,
        Canvas2DScene scene,
        PointD currentDocumentPoint,
        out RectD finalBounds,
        out ImmutableArray<PointD> finalRoute,
        out ImmutableArray<VisualStateMove> finalMoves,
        out ConnectorLabelPlacement? finalLabelPlacement,
        out NodeLabelVisualOverride? finalNodeLabelOverride,
        out BoundaryAttachmentPlacement? finalBoundaryAttachment)
    {
        finalBounds = gesture.OriginalBounds;
        finalRoute = gesture.OriginalRoute;
        finalMoves = [];
        finalLabelPlacement = null;
        finalNodeLabelOverride = null;
        finalBoundaryAttachment = null;
#pragma warning disable CA1031 // Invalid transient arithmetic is rejected before Scene installation.
        try
        {
            if (!IsBounded(currentDocumentPoint))
            {
                return false;
            }

            // Callers normalize the pointer once before both preview and commit.
            // Reapplying gesture clamping here would project boundary-attached
            // movement from an already translated synthetic point.
            var effectiveDocumentPoint = currentDocumentPoint;
            var delta = effectiveDocumentPoint - gesture.StartDocumentPoint;
            switch (gesture.Kind)
            {
                case PersistentGestureKind.Move:
                    var moves = ImmutableArray.CreateBuilder<VisualStateMove>(
                        gesture.MoveTargets.Length);
                    foreach (var target in gesture.MoveTargets)
                    {
                        var movedBounds = target.OriginalBounds.Translate(delta);
                        ValidateMovePreview(scene, target.VisualStateId, delta);
                        if (movedBounds != target.OriginalBounds)
                        {
                            if (!target.IsPreviewOnly)
                            {
                                var persistentBounds = target.SpatialRegion?.MapSceneToLocal(
                                    movedBounds) ?? movedBounds;
                                moves.Add(new VisualStateMove(
                                    target.VisualStateId,
                                    persistentBounds.TopLeft,
                                    VisualPlacementMode.Pinned));
                            }
                        }

                        if (target.VisualStateId == gesture.TargetVisualStateId)
                        {
                            finalBounds = movedBounds;
                        }
                    }

                    finalMoves = moves.ToImmutable();
                    break;
                case PersistentGestureKind.BoundaryAttachmentMove:
                    finalBounds = gesture.OriginalBounds.Translate(delta);
                    ValidateMovePreview(
                        scene,
                        gesture.TargetVisualStateId,
                        delta);
                    if (!BoundaryAttachmentPlacement.TryProjectToBoundary(
                            gesture.OriginalOwnerBounds,
                            Center(finalBounds),
                            new SizeD(finalBounds.Width, finalBounds.Height),
                            out finalBoundaryAttachment) ||
                        finalBoundaryAttachment is null)
                    {
                        return false;
                    }

                    break;
                case PersistentGestureKind.Resize:
                    finalBounds = Canvas2DResizeGeometry.CalculateBounds(
                        gesture.OriginalBounds,
                        delta,
                        gesture.ResizeDirection);
                    ValidateResizePreview(
                        scene,
                        gesture.TargetVisualStateId,
                        gesture.SourceSceneObjectId,
                        gesture.OriginalBounds,
                        finalBounds);
                    break;
                case PersistentGestureKind.RouteBend:
                    finalRoute = ReplaceRouteBend(
                        gesture.OriginalRoute,
                        gesture.BendIndex,
                        delta);
                    ValidateRoutePreview(
                        scene,
                        gesture.SourceSceneObjectId,
                        gesture.BendIndex,
                        finalRoute);
                    break;
                case PersistentGestureKind.LabelMove:
                    var targetAnchor = gesture.OriginalBounds.TopLeft + delta;
                    var projection = Canvas2DConnectorPathGeometry.FindNearest(
                        gesture.OriginalRoute,
                        targetAnchor);
                    finalBounds = new RectD(targetAnchor.X, targetAnchor.Y, 0d, 0d);
                    finalLabelPlacement = new ConnectorLabelPlacement(
                        projection.PathPosition,
                        projection.Offset);
                    break;
                case PersistentGestureKind.NodeLabelEdit:
                    finalBounds = gesture.NodeLabelOperation ==
                        Canvas2DNodeLabelGestureOperation.Move
                            ? gesture.OriginalBounds.Translate(delta)
                            : Canvas2DResizeGeometry.CalculateBounds(
                                gesture.OriginalBounds,
                                delta,
                                gesture.ResizeDirection,
                                NodeLabelVisualOverride.MinimumWidth,
                                NodeLabelVisualOverride.MinimumHeight);
                    ValidateNodeLabelPreview(scene, gesture);
                    var labelCenter = new PointD(
                        finalBounds.Left + (finalBounds.Width / 2d),
                        finalBounds.Top + (finalBounds.Height / 2d));
                    var ownerCenter = new PointD(
                        gesture.OriginalOwnerBounds.Left +
                            (gesture.OriginalOwnerBounds.Width / 2d),
                        gesture.OriginalOwnerBounds.Top +
                            (gesture.OriginalOwnerBounds.Height / 2d));
                    finalNodeLabelOverride = NodeLabelVisualOverride.FromBounds(
                        gesture.OriginalOwnerBounds,
                        finalBounds);
                    break;
                default:
                    return false;
            }

            return true;
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            finalBounds = gesture.OriginalBounds;
            finalRoute = gesture.OriginalRoute;
            finalMoves = [];
            finalLabelPlacement = null;
            finalNodeLabelOverride = null;
            finalBoundaryAttachment = null;
            return false;
        }
#pragma warning restore CA1031
    }

    private static void ValidateMovePreview(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        VectorD delta)
    {
        var translation = Matrix2D.CreateTranslation(delta);
        foreach (var item in PersistentVisualItems(scene, visualStateId))
        {
            _ = item.Bounds.Translate(delta);
            _ = item.Transform.Then(translation);
            _ = item.Clip?.Translate(delta);
        }
    }

    private static void ValidateResizePreview(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        SceneObjectId targetId,
        RectD originalBounds,
        RectD finalBounds)
    {
        var target = scene.Items.Single(item =>
            item.Id == targetId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        if (target.Bounds != originalBounds || originalBounds.IsEmpty)
        {
            throw new InvalidOperationException(
                "The resize target no longer matches its original persistent bounds.");
        }

        var adjustment = Canvas2DResizeGeometry.CreateAdjustment(originalBounds, finalBounds);
        foreach (var item in PersistentVisualItems(scene, visualStateId))
        {
            _ = item.Transform.Then(adjustment);
            _ = Canvas2DResizeGeometry.ScaleBounds(item.Bounds, originalBounds, finalBounds);
            _ = item.Clip is { } clip
                ? Canvas2DResizeGeometry.ScaleBounds(clip, originalBounds, finalBounds)
                : (RectD?)null;
        }

        foreach (var direction in Canvas2DResizeGeometry.Directions)
        {
            _ = Canvas2DResizeGeometry.InteractionBounds(finalBounds, direction);
        }
    }

    private static void ValidateNodeLabelPreview(
        Canvas2DScene scene,
        PersistentGestureState gesture)
    {
        if (gesture.LabelProjectedObjectId is not { } projectedLabelId ||
            gesture.NodeLabelOwnerSceneObjectId is not { } nodeSceneObjectId ||
            gesture.OriginalBounds.IsEmpty ||
            gesture.OriginalOwnerBounds.IsEmpty)
        {
            throw new InvalidOperationException(
                "The node-label gesture does not identify usable persistent geometry.");
        }

        var labelTarget = scene.Items.Single(item =>
            item.Id == gesture.SourceSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == gesture.TargetVisualStateId &&
            item.Origin.ProjectedObjectId == projectedLabelId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            HasBooleanMetadata(
                item,
                Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        if (labelTarget.Bounds != gesture.OriginalBounds ||
            !TryGetTextMetadata(
                labelTarget,
                Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                out var nodeSceneObjectIdValue))
        {
            throw new InvalidOperationException(
                "The node-label target no longer matches its original persistent bounds.");
        }

        if (!StringComparer.Ordinal.Equals(
                nodeSceneObjectIdValue,
                nodeSceneObjectId.Value))
        {
            throw new InvalidOperationException(
                "The node-label gesture no longer identifies its original owning node.");
        }

        var nodeTarget = scene.Items.Single(item =>
            item.Id == nodeSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == gesture.TargetVisualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        if (nodeTarget.Bounds != gesture.OriginalOwnerBounds ||
            !labelTarget.Origin.RelatedSceneObjectIds.Contains(nodeTarget.Id) ||
            !TryGetTextMetadata(
                labelTarget,
                Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                out var labelSceneObjectIdValue) ||
            !StringComparer.Ordinal.Equals(
                labelSceneObjectIdValue,
                labelTarget.Id.Value) ||
            !TryGetTextMetadata(
                labelTarget,
                Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                out var visualStateIdValue) ||
            !StringComparer.Ordinal.Equals(
                visualStateIdValue,
                gesture.TargetVisualStateId.Value) ||
            !TryGetTextMetadata(
                labelTarget,
                Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                out var projectedLabelIdValue) ||
            !StringComparer.Ordinal.Equals(
                projectedLabelIdValue,
                projectedLabelId.Value))
        {
            throw new InvalidOperationException(
                "The node-label target no longer matches its owning persistent node.");
        }
    }

    private static void ValidateRoutePreview(
        Canvas2DScene scene,
        SceneObjectId targetId,
        int bendIndex,
        ImmutableArray<PointD> finalRoute)
    {
        var target = scene.Items.Single(item =>
            item.Id == targetId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        _ = Canvas2DSceneGeometry.Path(finalRoute, target.Geometry.IsClosed);
        foreach (var point in finalRoute)
        {
            _ = target.Transform.TransformPoint(point);
        }

        var bend = target.Transform.TransformPoint(finalRoute[bendIndex]);
        var halfExtent = Canvas2DRouteGestureMetadata.HandleExtent / 2d;
        _ = new RectD(
            bend.X - halfExtent,
            bend.Y - halfExtent,
            Canvas2DRouteGestureMetadata.HandleExtent,
            Canvas2DRouteGestureMetadata.HandleExtent);
    }

    private static IEnumerable<Canvas2DSceneItem> PersistentVisualItems(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Where(item =>
            item.Origin.VisualStateId == visualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);

    private static bool IsBounded(PointD point) =>
        Math.Abs(point.X) <= MaximumInteractiveMagnitude &&
        Math.Abs(point.Y) <= MaximumInteractiveMagnitude;

    private static PointD ClampPersistentGesturePoint(
        PersistentGestureState gesture,
        PointD requestedPoint)
    {
        if (gesture.Kind == PersistentGestureKind.Move)
        {
            if (gesture.CurrentScene.SpatialPresentationPlan is not null)
            {
                // Crossing a presented region must not be clamped against the source region's
                // canonical origin. The destination inverse is validated before command execution.
                return requestedPoint;
            }
            var translation = DocumentGeometryBoundary.ClampTranslation(
                gesture.MoveTargets.Select(static target =>
                    target.SpatialRegion?.MapSceneToLocal(target.BoundaryBounds) ??
                    target.BoundaryBounds),
                requestedPoint - gesture.StartDocumentPoint);
            return gesture.StartDocumentPoint + translation;
        }

        if (gesture.Kind == PersistentGestureKind.BoundaryAttachmentMove)
        {
            var attachedSize = new SizeD(
                gesture.OriginalBounds.Width,
                gesture.OriginalBounds.Height);
            if (!BoundaryAttachmentPlacement.TryProjectToBoundary(
                    gesture.OriginalOwnerBounds,
                    requestedPoint,
                    attachedSize,
                    out var placement) ||
                placement is null)
            {
                return gesture.CurrentDocumentPoint;
            }

            var targetBounds = placement.ResolveBounds(
                gesture.OriginalOwnerBounds,
                attachedSize);
            return gesture.StartDocumentPoint +
                (targetBounds.TopLeft - gesture.OriginalBounds.TopLeft);
        }

        if (gesture.Kind == PersistentGestureKind.NodeLabelEdit &&
            gesture.NodeLabelOperation == Canvas2DNodeLabelGestureOperation.Move)
        {
            var translation = DocumentGeometryBoundary.ClampTranslation(
                [gesture.SpatialRegion?.MapSceneToLocal(gesture.OriginalBounds) ??
                    gesture.OriginalBounds],
                requestedPoint - gesture.StartDocumentPoint);
            return gesture.StartDocumentPoint + translation;
        }

        if (gesture.Kind is
            PersistentGestureKind.Resize or
            PersistentGestureKind.BoundaryAttachmentMove or
            PersistentGestureKind.RouteBend or
            PersistentGestureKind.NodeLabelEdit)
        {
            if (gesture.SpatialRegion is { } presentation)
            {
                return presentation.MapLocalToScene(
                    DocumentGeometryBoundary.Clamp(
                        presentation.MapSceneToLocal(requestedPoint)));
            }

            return DocumentGeometryBoundary.Clamp(requestedPoint);
        }

        return requestedPoint;
    }

    private static RectD ResolveMoveBoundaryBounds(
        RectD nodeBounds,
        VisualStateSnapshot visualState)
    {
        if (!NodeLabelVisualOverride.TryRead(visualState.Properties, out var visualOverride))
        {
            return nodeBounds;
        }

        var labelBounds = visualOverride!.ResolveBounds(nodeBounds);
        var left = Math.Min(nodeBounds.Left, labelBounds.Left);
        var top = Math.Min(nodeBounds.Top, labelBounds.Top);
        var right = Math.Max(nodeBounds.Right, labelBounds.Right);
        var bottom = Math.Max(nodeBounds.Bottom, labelBounds.Bottom);
        return new RectD(left, top, right - left, bottom - top);
    }

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static ImmutableArray<PointD> ReplaceRouteBend(
        ImmutableArray<PointD> route,
        int bendIndex,
        VectorD delta)
    {
        var updated = route.ToArray();
        updated[bendIndex] += delta;
        return [.. updated];
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateRouteProperties(
        SceneObjectId sceneObjectId,
        VisualStateId visualStateId,
        int bendIndex) =>
        [
            new KeyValuePair<string, PropertyValue>(
                Canvas2DRouteGestureMetadata.TargetSceneObjectId,
                PropertyValue.FromText(sceneObjectId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DRouteGestureMetadata.TargetVisualStateId,
                PropertyValue.FromText(visualStateId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DRouteGestureMetadata.BendIndex,
                PropertyValue.FromInteger(bendIndex)),
        ];

    private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateLabelProperties(
        SceneObjectId connectorSceneObjectId,
        VisualStateId visualStateId,
        ProjectedObjectId labelProjectedObjectId) =>
        [
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.TargetConnectorSceneObjectId,
                PropertyValue.FromText(connectorSceneObjectId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.TargetVisualStateId,
                PropertyValue.FromText(visualStateId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId,
                PropertyValue.FromText(labelProjectedObjectId.Value)),
        ];

    private static IEnumerable<KeyValuePair<string, PropertyValue>>
        CreateNodeLabelProperties(
            SceneObjectId nodeSceneObjectId,
            SceneObjectId labelSceneObjectId,
            VisualStateId visualStateId,
            ProjectedObjectId labelProjectedObjectId,
            Canvas2DNodeLabelGestureOperation operation,
            Canvas2DResizeDirection resizeDirection)
    {
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
            PropertyValue.FromText(nodeSceneObjectId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
            PropertyValue.FromText(labelSceneObjectId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
            PropertyValue.FromText(visualStateId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
            PropertyValue.FromText(labelProjectedObjectId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.Operation,
            PropertyValue.FromText(
                operation == Canvas2DNodeLabelGestureOperation.Move
                    ? Canvas2DNodeLabelGestureMetadata.MoveOperation
                    : Canvas2DNodeLabelGestureMetadata.ResizeOperation));
        if (operation == Canvas2DNodeLabelGestureOperation.Resize)
        {
            yield return new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.ResizeDirection,
                PropertyValue.FromText(Canvas2DResizeGeometry.Role(resizeDirection)));
        }
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private enum InteractionKind
    {
        Hover,
        Selection,
        ContextMenu,
        NodeBodyActivation,
    }

    private sealed record PersistentGestureState(
        PersistentGestureKind Kind,
        Canvas2DResizeDirection ResizeDirection,
        long PointerId,
        string GestureId,
        DocumentId DocumentId,
        DocumentRevision DocumentRevision,
        SceneObjectId SourceSceneObjectId,
        SceneObjectId? NodeLabelOwnerSceneObjectId,
        VisualStateId TargetVisualStateId,
        RectD OriginalBounds,
        RectD OriginalOwnerBounds,
        ImmutableArray<PointD> OriginalRoute,
        int BendIndex,
        ProjectedObjectId? LabelProjectedObjectId,
        Canvas2DNodeLabelGestureOperation NodeLabelOperation,
        ImmutableArray<MoveGestureTarget> MoveTargets,
        PointD StartDocumentPoint,
        PointD CurrentDocumentPoint,
        Canvas2DScene CurrentScene,
        EditingSessionGeneration CurrentGeneration,
        EditorStateSnapshot CurrentEditorState,
        AnchorConnectionGestureState? AnchorConnection = null,
        ConnectorEndpointReconnectionGestureState? EndpointReconnection = null,
        BoundaryAttachmentPlacement? OriginalBoundaryAttachment = null,
        Canvas2DSpatialRegion? SpatialRegion = null);

    private sealed record AnchorConnectionGestureState(
        AnchorConnectionCreationRegistration Registration,
        SemanticElementId SourceSemanticElementId,
        VisualStateId SourceVisualStateId,
        ConnectorAnchorId SourceAnchorId,
        TargetAnchorAcquisitionResult? TargetAcquisition,
        ConnectorAnchorId? ProposedAnchorId);

    private sealed record AnchorConnectionTarget(
        TargetAnchorAcquisitionResult Acquisition,
        PointD PointerDocumentPoint,
        RectD TargetBounds,
        bool IsSmart,
        Canvas2DSpatialRegion? SpatialRegion);

    private sealed record ConnectorEndpointReconnectionGestureState(
        ConnectorEndpointReconnectionRegistration Registration,
        SemanticElementId RelationshipId,
        VisualStateId ConnectorVisualStateId,
        SceneObjectId ConnectorSceneObjectId,
        ConnectorEndpointKind EndpointKind,
        SemanticElementId OriginalSemanticEndpointId,
        ConnectorAnchorId OriginalAnchorId,
        SemanticElementId? CandidateSemanticElementId,
        VisualStateId? CandidateVisualStateId,
        ConnectorAnchorId? CandidateAnchorId);

    private sealed record ConnectorEndpointReconnectionTarget(
        SemanticElementId SemanticElementId,
        VisualStateId VisualStateId,
        ConnectorAnchorId AnchorId,
        PointD DocumentPoint);

    private sealed record MoveGestureTarget(
        SceneObjectId SceneObjectId,
        VisualStateId VisualStateId,
        RectD OriginalBounds,
        RectD BoundaryBounds,
        ImmutableArray<PointD> OriginalRoute,
        bool IsPreviewOnly = false,
        Canvas2DSpatialRegion? SpatialRegion = null);

    private sealed record PendingPointerState(
        long PointerId,
        bool ControlKey,
        PointD StartDocumentPoint,
        VisualStateId? SelectedVisualStateId,
        SemanticElementId? SelectedSemanticElementId,
        Canvas2DSceneItem? MoveTarget,
        Canvas2DScene Scene,
        EditingSessionGeneration Generation,
        EditorStateSnapshot EditorState);

    private readonly record struct PointerPresentation(
        Canvas2DSceneHitTestResult? HitResult,
        string CssCursor);

    private enum PersistentGestureKind
    {
        AnchorConnectionCreation,
        ConnectorEndpointReconnection,
        Move,
        BoundaryAttachmentMove,
        Resize,
        RouteBend,
        LabelMove,
        NodeLabelEdit,
    }
}
