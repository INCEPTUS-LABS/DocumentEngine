using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    /// <summary>
    /// Executes an ordinary Command for an exact current Scene target. The source presentation
    /// supplies interaction coordinates only; the active Process scope remains authoritative.
    /// A null target item permits placement within an exact spatial destination region.
    /// </summary>
    public async ValueTask<HistoryOperationResult> ExecuteForSceneTargetAsync(
        ICommand command,
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        SceneObjectId? targetSceneObjectId,
        Canvas2DSpatialRegion? expectedPresentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(expectedScene);
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelled = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelled.DocumentId, command.TypeId, HistoryOperationStatus.Cancelled,
                cancelled.DocumentRevision, cancelled.HistoryStatus,
                [CancelledDiagnostic(cancelled.DocumentId.Value)]);
        }

        try
        {
            lock (_sync)
            {
                var state = CaptureStateUnderLock();
                var document = AttachedDocument.CaptureSnapshot();
                if (_closing || _closed || state.Status != EditingSessionStatus.Ready ||
                    state.EditorState.ActiveGesture is not null ||
                    state.Generation != expectedGeneration ||
                    !ReferenceEquals(state.CurrentScene, expectedScene) ||
                    command.TargetDocumentId != document.DocumentId ||
                    command.ExpectedRevision != document.Revision ||
                    document.DocumentId != expectedScene.DocumentId ||
                    document.Revision != expectedScene.SourceRevision ||
                    !IsCurrentSceneTarget(document, expectedScene, state.PresentationContext,
                        targetSceneObjectId, expectedPresentation))
                {
                    return InteractionCommandRejected(command, state,
                        Canvas2DInteractionDiagnosticCodes.StaleScene,
                        "The Scene interaction target is no longer current.");
                }

                var target = targetSceneObjectId is null ? null : expectedScene.Items
                    .FirstOrDefault(item => item.Id == targetSceneObjectId);
                if (target?.Origin is { VisualStateId: null, SemanticElementId: { } semanticId } &&
                    (state.EditorState.SemanticSceneSelection != semanticId ||
                     !state.EditorState.Selection.IsEmpty))
                {
                    return InteractionCommandRejected(command, state,
                        Canvas2DInteractionDiagnosticCodes.StaleScene,
                        "The semantic Scene target is no longer selected.");
                }
            }

            var result = await History.ExecuteAsync(
                _commandProcessor, command, cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private static bool IsCurrentSceneTarget(
        DocumentSnapshot document,
        Canvas2DScene scene,
        PresentationViewContext context,
        SceneObjectId? sceneObjectId,
        Canvas2DSpatialRegion? expectedPresentation)
    {
        if (context is ProcessScopePresentationContext process && !document.SemanticModel.TryGetScope(process.ScopeId, out _))
        {
            return false;
        }

        if (expectedPresentation is not null &&
            (scene.SpatialPresentationPlan is null ||
             !scene.SpatialPresentationPlan.Regions.Any(region => region.Equals(expectedPresentation)) ||
             expectedPresentation.ContainerSemanticElementId is { } containerId &&
             !document.SemanticModel.TryGetElement(containerId, out _)))
        {
            return false;
        }

        if (sceneObjectId is null)
        {
            return expectedPresentation is not null ||
                scene.SpatialPresentationPlan is null;
        }

        var target = scene.Items.FirstOrDefault(item => item.Id == sceneObjectId);
        if (target is null || !target.IsVisible ||
            !Equals(target.SpatialRegion, expectedPresentation))
        {
            return false;
        }

        if (target.Origin.VisualStateId is not { } visualId)
        {
            return target.Origin.SemanticElementId is { } semanticId &&
                (Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(target) ||
                 target.Origin.ProjectedObjectId is { } projectedId && target.Layer == Canvas2DSceneLayer.Content &&
                 target.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedId, "node")) &&
                target.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                document.SemanticModel.TryGetElement(semanticId, out var semantic) && semantic is not null &&
                (context is ProcessScopePresentationContext && semantic.ContainmentKind == SemanticElementContainmentKind.Document ||
                 IsPresentationMember(document, scene, context, semanticId));
        }

        return document.VisualModel.TryGetVisualState(visualId, out var visual) &&
            visual is not null &&
            (target.Origin.SemanticElementId is null ||
             target.Origin.SemanticElementId == visual.SemanticElementId) &&
            IsPresentationMember(document, scene, context, visual.SemanticElementId);
    }

    private static bool IsCurrentVisualSelection(
        DocumentSnapshot document, Canvas2DScene scene, PresentationViewContext context,
        EditorStateSnapshot editorState) => editorState.Selection.All(visualId =>
        document.VisualModel.TryGetVisualState(visualId, out var visual) && visual is not null &&
        IsPresentationMember(document, scene, context, visual.SemanticElementId) &&
        scene.Items.Any(item => item.IsVisible && item.Origin.VisualStateId == visualId));

    private static bool IsPresentationMember(DocumentSnapshot document, Canvas2DScene scene,
        PresentationViewContext context, SemanticElementId id) => context switch
        {
            ProcessScopePresentationContext process => document.SemanticModel.TryGetScope(id, out var scope) && scope?.Id == process.ScopeId ||
                scene.PresentationPlan is { } scopePlan && scopePlan.Context == context && scopePlan.DocumentId == document.DocumentId &&
                scopePlan.SourceRevision == document.Revision && scopePlan.Contains(id),
            DocumentPresentationContext => scene.PresentationPlan is { } plan && plan.Context == context &&
                plan.DocumentId == document.DocumentId && plan.SourceRevision == document.Revision && plan.Contains(id),
            _ => false,
        };
}
