using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private PointD? _placementPointerCssPoint;
    private EditingSessionState? _placementEvaluatedState;
    private EditingSessionState? _placementPreviewSource;

    // Coalesce only intermediate presentation notifications inside the awaited pointer
    // preview operation. Session state and command readiness remain authoritative/live.
    private bool CanDeferPlacementPreviewNotification(EditingSessionState state) =>
        _placementPreviewSource is { IsGraphicalInteractionEnabled: true } source &&
        !state.IsClosed && !state.IsCurrentScenePresented &&
        state.Status is EditingSessionStatus.Rebuilding or EditingSessionStatus.Ready &&
        !_surfaceRenderPending && !_propertiesFormOpen && !_modelViewPropertiesFormOpen &&
        _contextMenu is null && _hostDiagnostics.IsEmpty &&
        state.RuntimeDiagnostics.IsEmpty && state.PresentationDiagnostics.IsEmpty &&
        state.DocumentId == source.DocumentId && state.DocumentRevision == source.DocumentRevision &&
        state.PresentationContext == source.PresentationContext &&
        state.HistoryStatus.Equals(source.HistoryStatus) &&
        state.ModelProfileState.Equals(source.ModelProfileState) &&
        state.ModelProfileViewState.Equals(source.ModelProfileViewState) &&
        state.ModelProfileElementViewState.Equals(source.ModelProfileElementViewState) &&
        WithoutPlacementFeedback(state.EditorState).Equals(WithoutPlacementFeedback(source.EditorState));

    private static EditorStateSnapshot WithoutPlacementFeedback(EditorStateSnapshot state) => new(
        state.Selection, state.HoveredObjectId, state.ActiveToolId, state.FocusTargetId,
        state.Viewport, state.ActiveGesture,
        state.TemporaryFeedback.Where(static feedback => feedback.PlacementPreview is null),
        state.ToolState, state.SemanticSceneSelection);

    private void RememberPlacementEvaluation(EditingSession session)
    {
        var state = session.CaptureState();
        lock (_sync)
        {
            _placementEvaluatedState = state.EditorState.TemporaryFeedback.Any(
                static feedback => feedback.PlacementPreview is not null) ? state : null;
        }
    }

    private string PlacementCursor(EditingSession session, ToolboxPlacementController? controller)
    {
        if (controller?.IsPlacementActive != true)
        {
            return "default";
        }

        lock (_sync)
        {
            if (_placementPointerCssPoint is null || _viewportPanGesture is not null)
            {
                return "crosshair";
            }
        }

        var state = session.CaptureState();
        if (state.IsCurrentScenePresented && state.CurrentScene is { } scene)
        {
            foreach (var feedback in state.EditorState.TemporaryFeedback)
            {
                if (feedback.PlacementPreview is not { } preview ||
                    preview.ToolboxItemId != controller.ActiveItemId || feedback.Bounds is not { } bounds)
                {
                    continue;
                }

                // The immutable feedback alone cannot prove that its contributor produced pixels.
                // Require the actual body on the acknowledged current surface before hiding input.
                if (scene.Items.Any(item => item.IsVisible && item.Style.Opacity > 0 &&
                    item.Bounds == bounds && !item.Bounds.IsEmpty &&
                    item.Origin.StableSourceKey == $"feedback:{feedback.Id}:body" &&
                    (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
                    item.Origin.SemanticElementId is null && item.Origin.VisualStateId is null &&
                    item.HitTestPolicy.Mode == Canvas2DHitTestMode.None))
                {
                    return "none";
                }
            }
        }

        return "crosshair";
    }

    private async ValueTask SuspendPlacementPreviewUnderGateAsync(
        EditingSession session,
        bool forgetPointer,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _placementEvaluatedState = null;
            if (forgetPointer)
            {
                _placementPointerCssPoint = null;
            }
        }

        _ = await SetPointerCursorUnderGateAsync("default", cancellationToken).ConfigureAwait(false);
        _ = await ToolboxPlacementController.ClearPreviewAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RefreshPlacementAtPointerUnderGateAsync(
        EditingSession session,
        CancellationToken cancellationToken)
    {
        PointD? pointer;
        ToolboxPlacementController? controller;
        lock (_sync)
        {
            pointer = _placementPointerCssPoint;
            controller = _toolboxPlacementController;
        }

        if (pointer is null || controller?.IsPlacementActive != true ||
            !session.CaptureState().IsGraphicalInteractionEnabled)
        {
            return;
        }

        var result = await controller.UpdatePreviewAtCssPointAsync(session, pointer.Value, cancellationToken)
            .ConfigureAwait(false);
        lock (_sync)
        {
            _ = ReplaceInteractionDiagnosticsUnderLock(result.Diagnostics);
        }
        RememberPlacementEvaluation(session);
        _ = await SetPointerCursorUnderGateAsync(PlacementCursor(session, controller), cancellationToken)
            .ConfigureAwait(false);
    }

    private bool PlacementContextChanged(EditingSessionState state) =>
        _placementEvaluatedState is { } evaluated &&
        (state.DocumentId != evaluated.DocumentId ||
         state.DocumentRevision != evaluated.DocumentRevision ||
         state.PresentationContext != evaluated.PresentationContext ||
         !state.EditorState.Viewport.Equals(evaluated.EditorState.Viewport) ||
         !state.ModelProfileState.Equals(evaluated.ModelProfileState) ||
         !state.ModelProfileViewState.Equals(evaluated.ModelProfileViewState) ||
         !state.ModelProfileElementViewState.Equals(evaluated.ModelProfileElementViewState) ||
         state.IsClosed || HasError(state.PresentationDiagnostics));
}
