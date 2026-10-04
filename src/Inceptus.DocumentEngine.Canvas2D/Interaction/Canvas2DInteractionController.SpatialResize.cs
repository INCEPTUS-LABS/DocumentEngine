using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed partial class Canvas2DInteractionController
{
    private SpatialResizeGesture? _spatialResize;
    private const string SpatialResizeFeedbackId = "spatial-resize";
    private const double SpatialResizeCssTolerance = 4d;

    private sealed record SpatialResizeGesture(long PointerId, string Id, Canvas2DSpatialResizeTarget Target,
        PointD Origin, EditingSessionState Current, Canvas2DSpatialPresentationPlan Plan, long SurfaceGeneration);

    private Canvas2DSpatialResizeTarget? AcquireSpatialResize(EditingSessionState state, PointD cssPoint,
        PointD documentPoint, Canvas2DSceneHitTestResult? hit)
    {
        if (!state.IsCurrentScenePresented || state.EditorState.ActiveToolId is not null || state.EditorState.ActiveGesture is not null ||
            state.CurrentScene?.SpatialPresentationPlan is not { } plan || plan.ResizeTargets.IsEmpty)
            return null;
        // Resolve the complete ordinary hit family, including selected handles and labels.
        hit = _hitTestService.HitTest(state.CurrentScene, documentPoint) ?? hit;
        var item = hit is null ? null : state.CurrentScene.Items.FirstOrDefault(item => item.Id == hit.SceneObjectId);
        var candidates = plan.ResizeTargets.Where(target =>
        {
            if (item is not null && (item.Origin.VisualStateId is not null ||
                    item.Layer != Canvas2DSceneLayer.Background ||
                    item.Origin.SemanticElementId != plan.Regions.Single(region => region.Id == target.AcquiredRegionId).ContainerSemanticElementId))
                return false;
            return ResizeEdgeDistance(state.CurrentScene, target, cssPoint) <= SpatialResizeCssTolerance;
        });
        return candidates.OrderBy(target => ResizeEdgeDistance(state.CurrentScene, target, cssPoint))
            .ThenBy(static target => target.Edge) // Bottom wins an exact corner tie.
            .ThenBy(static target => target.AcquiredRegionId.Value, StringComparer.Ordinal).FirstOrDefault();
    }

    private static double ResizeEdgeDistance(Canvas2DScene scene, Canvas2DSpatialResizeTarget target, PointD css)
    {
        var bounds = target.PaintedBounds;
        var start = scene.ViewportTransform.TransformPoint(target.Edge == Canvas2DSpatialResizeEdge.Bottom
            ? new PointD(bounds.Left, bounds.Bottom) : new PointD(bounds.Right, bounds.Top));
        var end = scene.ViewportTransform.TransformPoint(new PointD(bounds.Right, bounds.Bottom));
        if (target.Edge == Canvas2DSpatialResizeEdge.Bottom)
            return css.X < start.X - SpatialResizeCssTolerance || css.X > end.X + SpatialResizeCssTolerance
                ? double.PositiveInfinity : Math.Abs(css.Y - start.Y);
        return css.Y < start.Y - SpatialResizeCssTolerance || css.Y > end.Y + SpatialResizeCssTolerance
            ? double.PositiveInfinity : Math.Abs(css.X - start.X);
    }

    private static string ResizeCursor(Canvas2DSpatialResizeTarget target) =>
        target.Edge == Canvas2DSpatialResizeEdge.Bottom ? "ns-resize" : "ew-resize";

    private static EditorStateSnapshot WithSpatialResize(EditorStateSnapshot state,
        Canvas2DSpatialResizeFeedback? feedback, EditorGestureSnapshot? gesture) => new(
            state.Selection, state.HoveredObjectId, state.ActiveToolId, state.FocusTargetId, state.Viewport,
            gesture, state.TemporaryFeedback.Where(static item => item.SpatialResize is null)
                .Concat(feedback is null ? [] : new[] { new EditorFeedbackSnapshot(SpatialResizeFeedbackId, feedback) }),
            state.ToolState, state.SemanticSceneSelection);

    private async ValueTask<Canvas2DInteractionResult> StartSpatialResizeAsync(EditingSessionState observed,
        Canvas2DPointerInput input, PointD point, Canvas2DSpatialResizeTarget target, CancellationToken cancellationToken)
    {
        var surfaceGeneration = _session.CaptureInteractionSurfaceGeneration();
        var id = $"spatial-resize:{observed.DocumentRevision.Value}:{input.PointerId}:{target.AcquiredRegionId.Value}:{target.Edge}";
        var feedback = new Canvas2DSpatialResizeFeedback(target, Canvas2DSpatialResizePhase.Drag, target.AuthoredExtent);
        var updated = WithSpatialResize(observed.EditorState, feedback,
            new EditorGestureSnapshot(id, Canvas2DSpatialResizeFeedback.FeedbackKind, point, point));
        var result = await ApplyEditorStateAsync(observed, updated, null, cancellationToken, ResizeCursor(target)).ConfigureAwait(false);
        if (result.Status == Canvas2DInteractionStatus.Updated && result.SessionState.IsCurrentScenePresented)
            _spatialResize = new(input.PointerId, id, target, point, result.SessionState, observed.CurrentScene!.SpatialPresentationPlan!, surfaceGeneration);
        else
            return await CleanupFailedGestureUpdateUnderGateAsync(id, result).ConfigureAwait(false);
        return result;
    }

    private bool IsCurrentSpatialResize(EditingSessionState current, SpatialResizeGesture gesture) =>
        _session.CaptureInteractionSurfaceGeneration() == gesture.SurfaceGeneration &&
        current.IsCurrentScenePresented && current.DocumentId == gesture.Current.DocumentId &&
        current.DocumentRevision == gesture.Current.DocumentRevision && current.ActiveScopeId == gesture.Current.ActiveScopeId &&
        current.Generation == gesture.Current.Generation && ReferenceEquals(current.CurrentScene, gesture.Current.CurrentScene) &&
        ReferenceEquals(current.EditorState, gesture.Current.EditorState) &&
        current.CurrentScene!.SpatialPresentationPlan!.Equals(gesture.Plan) &&
        current.EditorState.ActiveGesture?.Id == gesture.Id;

    private static bool TrySpatialResizeSample(EditingSessionState state, SpatialResizeGesture gesture, PointD cssPoint,
        out PointD point, out double extent)
    {
        extent = gesture.Target.AuthoredExtent;
        if (!TryConvertPoint(state, cssPoint, out point, out _)) return false;
        extent += gesture.Target.Edge == Canvas2DSpatialResizeEdge.Bottom
            ? point.Y - gesture.Origin.Y : point.X - gesture.Origin.X;
        var edge = (gesture.Target.Edge == Canvas2DSpatialResizeEdge.Bottom
            ? gesture.Target.PaintedBounds.Bottom : gesture.Target.PaintedBounds.Right) + extent - gesture.Target.AuthoredExtent;
        return double.IsFinite(extent) && double.IsFinite(edge) && Math.Abs(edge) <= MaximumInteractiveMagnitude;
    }

    private async ValueTask<Canvas2DInteractionResult> MoveSpatialResizeAsync(Canvas2DPointerInput input,
        CancellationToken cancellationToken)
    {
        var gesture = _spatialResize!;
        var current = _session.CaptureState();
        if (input.PointerId != gesture.PointerId)
            return new(Canvas2DInteractionStatus.Unchanged, current, cssCursor: ResizeCursor(gesture.Target));
        if (!IsCurrentSpatialResize(current, gesture) ||
            !TrySpatialResizeSample(current, gesture, input.CssPoint, out var point, out var extent))
            return await CancelSpatialResizeAsync().ConfigureAwait(false);
        var previous = current.EditorState.TemporaryFeedback.Single(item => item.SpatialResize is not null).SpatialResize!;
        if (extent == previous.RequestedExtent)
            return new(Canvas2DInteractionStatus.Unchanged, current, cssCursor: ResizeCursor(gesture.Target));
        var updated = WithSpatialResize(current.EditorState,
            new Canvas2DSpatialResizeFeedback(gesture.Target, Canvas2DSpatialResizePhase.Drag, extent),
            new EditorGestureSnapshot(gesture.Id, Canvas2DSpatialResizeFeedback.FeedbackKind, gesture.Origin, point));
        var result = await ApplyEditorStateAsync(current, updated, null, cancellationToken, ResizeCursor(gesture.Target)).ConfigureAwait(false);
        if (result.Status == Canvas2DInteractionStatus.Updated && result.SessionState.IsCurrentScenePresented)
            _spatialResize = gesture with { Current = result.SessionState };
        else
            return await CancelSpatialResizeAsync().ConfigureAwait(false);
        return result;
    }

    private async ValueTask<Canvas2DInteractionResult> CompleteSpatialResizeAsync(Canvas2DPointerInput input,
        CancellationToken cancellationToken)
    {
        var gesture = _spatialResize!;
        var current = _session.CaptureState();
        if (input.PointerId != gesture.PointerId)
            return new(Canvas2DInteractionStatus.Unchanged, current, cssCursor: ResizeCursor(gesture.Target));
        _suppressNextActivation = true;
        if (!input.IsPrimary || input.Button != 0 || !IsCurrentSpatialResize(current, gesture) ||
            !TrySpatialResizeSample(current, gesture, input.CssPoint, out _, out var extent) ||
            extent == gesture.Target.AuthoredExtent ||
            gesture.Target.Constraints.Validate(extent).Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return await CancelSpatialResizeAsync().ConfigureAwait(false);
        if (!_session.TryCaptureDocumentSnapshot(out var document) || document.Revision != current.DocumentRevision)
            return await CancelSpatialResizeAsync().ConfigureAwait(false);
        var request = new Canvas2DSpatialResizeRequest(document, current.ActiveScopeId, gesture.Target, extent);
        var planned = _spatialEditPlanners.PlanResize(request);
        if (!planned.Succeeded) return await CancelSpatialResizeAsync().ConfigureAwait(false);
        _spatialResize = null;
        var result = await _session.CompletePersistentGestureAsync(current.CurrentScene!, current.Generation,
            current.EditorState, WithSpatialResize(current.EditorState, null, null), planned.Command!, spatialMove: null,
            request, _spatialEditPlanners, gesture.SurfaceGeneration, cancellationToken).ConfigureAwait(false);
        if (!result.IsCommitted && result.Status != HistoryOperationStatus.NoChange)
            await _session.ClearPersistentGestureFromInteractionAsync(gesture.Id, CancellationToken.None).ConfigureAwait(false);
        return new(result.IsCommitted ? Canvas2DInteractionStatus.Committed :
            result.Status == HistoryOperationStatus.NoChange ? Canvas2DInteractionStatus.Unchanged : Canvas2DInteractionStatus.Failed,
            _session.CaptureState(), diagnostics: result.Diagnostics, persistentOperation: result);
    }

    private async ValueTask<Canvas2DInteractionResult> CancelSpatialResizeAsync()
    {
        var gesture = _spatialResize;
        _spatialResize = null;
        _suppressNextActivation = true;
        if (gesture is not null)
            return FromSessionResult(await _session.ClearPersistentGestureFromInteractionAsync(gesture.Id,
                CancellationToken.None).ConfigureAwait(false), null);
        var current = _session.CaptureState();
        var cleared = WithSpatialResize(current.EditorState, null, current.EditorState.ActiveGesture);
        return current.CurrentScene is not null && !cleared.Equals(current.EditorState)
            ? await ApplyEditorStateAsync(current, cleared, null, CancellationToken.None).ConfigureAwait(false)
            : new(Canvas2DInteractionStatus.Unchanged, current);
    }
}
