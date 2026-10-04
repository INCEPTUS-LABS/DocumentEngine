using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private PointD? _placementPointerCssPoint;
    private EditingSessionState? _placementEvaluatedState;

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
         state.ActiveScopeId != evaluated.ActiveScopeId ||
         !state.EditorState.Viewport.Equals(evaluated.EditorState.Viewport) ||
         !state.ModelProfileState.Equals(evaluated.ModelProfileState) ||
         !state.ModelProfileViewState.Equals(evaluated.ModelProfileViewState) ||
         !state.ModelProfileElementViewState.Equals(evaluated.ModelProfileElementViewState) ||
         state.IsClosed || HasError(state.PresentationDiagnostics));
}
