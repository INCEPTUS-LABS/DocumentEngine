using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    /// <summary>
    /// Atomically replaces transient profile-element presentation preferences and recomposes
    /// the current Scene. This operation never mutates the Document or global History.
    /// </summary>
    public ValueTask<EditingSessionOperationResult> UpdateModelProfileElementViewStateAsync(
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelProfileElementViewState);
        return UpdateModelProfileElementViewStateCoreAsync(
            modelProfileElementViewState,
            conditionalUpdate: null,
            cancellationToken);
    }

    /// <summary>
    /// Atomically applies one semantic Scene presentation preference only while the interaction
    /// target and every authoritative input observed by its caller remain current. Transient
    /// Scene generations may differ because hover-only recomposition does not invalidate the
    /// stable Scene-object identity.
    /// </summary>
    public ValueTask<EditingSessionOperationResult> TrySetModelProfileElementCollapsedAsync(
        DocumentId expectedDocumentId,
        DocumentRevision expectedDocumentRevision,
        DocumentScopeId expectedActiveScopeId,
        ModelProfileViewStateSnapshot expectedModelProfileViewState,
        ModelProfileElementViewStateSnapshot expectedModelProfileElementViewState,
        SceneObjectId expectedTargetSceneObjectId,
        ModelProfileId profileId,
        SemanticElementId semanticElementId,
        bool isCollapsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedDocumentId);
        ArgumentNullException.ThrowIfNull(expectedActiveScopeId);
        ArgumentNullException.ThrowIfNull(expectedModelProfileViewState);
        ArgumentNullException.ThrowIfNull(expectedModelProfileElementViewState);
        ArgumentNullException.ThrowIfNull(expectedTargetSceneObjectId);
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        return UpdateModelProfileElementViewStateCoreAsync(
            requestedState: null,
            new ConditionalElementViewUpdate(
                expectedDocumentId,
                expectedDocumentRevision,
                expectedActiveScopeId,
                expectedModelProfileViewState,
                expectedModelProfileElementViewState,
                expectedTargetSceneObjectId,
                profileId,
                semanticElementId,
                isCollapsed),
            cancellationToken);
    }

    private async ValueTask<EditingSessionOperationResult>
        UpdateModelProfileElementViewStateCoreAsync(
            ModelProfileElementViewStateSnapshot? requestedState,
            ConditionalElementViewUpdate? conditionalUpdate,
            CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        Task<EditingSessionOperationResult>? runTask = null;
        try
        {
            DocumentSnapshot snapshot;
            EditingSessionPipelineArtifacts? artifacts;
            EditingSessionGeneration expectedGeneration;
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(EditingSessionOperationStatus.Closed);
                }

                cancellationToken.ThrowIfCancellationRequested();
                snapshot = AttachedDocument.CaptureSnapshot();
                if (conditionalUpdate is not null &&
                    !IsConditionalElementViewUpdateCurrent(
                        conditionalUpdate,
                        snapshot))
                {
                    return OperationResult(EditingSessionOperationStatus.Superseded);
                }

                requestedState ??= _activeModelProfileElementViewState.WithCollapsed(
                    conditionalUpdate!.ProfileId,
                    conditionalUpdate.SemanticElementId,
                    conditionalUpdate.IsCollapsed);
                if (_activeModelProfileElementViewState.Equals(requestedState))
                {
                    return OperationResult(EditingSessionOperationStatus.Succeeded);
                }

                artifacts = _compatibleArtifacts;
                expectedGeneration = _generation;
                if (artifacts is null ||
                    !artifacts.IsCompatibleWith(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        _activeScopeId))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "Profile-element presentation changes require current retained " +
                            "Process artifacts.",
                            _documentId.Value)]);
                }
            }

            var started = BeginRun(
                snapshot,
                artifacts,
                cancellationToken,
                expectedGeneration,
                clearActiveGesture: true,
                requestedModelProfileElementViewState: requestedState);
            runTask = started.Task;
            if (runTask is null)
            {
                return OperationResult(
                    EditingSessionOperationStatus.Superseded,
                    started.Diagnostics);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return await runTask.ConfigureAwait(false);
    }

    private bool IsConditionalElementViewUpdateCurrent(
        ConditionalElementViewUpdate update,
        DocumentSnapshot document)
    {
        if (_status != EditingSessionStatus.Ready ||
            document.DocumentId != update.ExpectedDocumentId ||
            document.Revision != update.ExpectedDocumentRevision ||
            _activeScopeId != update.ExpectedActiveScopeId ||
            !_activeModelProfileViewState.Equals(update.ExpectedModelProfileViewState) ||
            !_activeModelProfileElementViewState.Equals(
                update.ExpectedModelProfileElementViewState) ||
            _editorState?.CaptureSnapshot() is not { } editorState ||
            editorState.ActiveGesture is not null ||
            editorState.SemanticSceneSelection != update.SemanticElementId ||
            !editorState.Selection.IsEmpty ||
            _currentScene is null)
        {
            return false;
        }

        var matches = _currentScene.Items
            .Where(item =>
                item.Id == update.ExpectedTargetSceneObjectId &&
                item.Origin.SemanticElementId == update.SemanticElementId &&
                item.Origin.VisualStateId is null &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item))
            .Take(2)
            .Count();
        return matches == 1;
    }

    private sealed record ConditionalElementViewUpdate(
        DocumentId ExpectedDocumentId,
        DocumentRevision ExpectedDocumentRevision,
        DocumentScopeId ExpectedActiveScopeId,
        ModelProfileViewStateSnapshot ExpectedModelProfileViewState,
        ModelProfileElementViewStateSnapshot ExpectedModelProfileElementViewState,
        SceneObjectId ExpectedTargetSceneObjectId,
        ModelProfileId ProfileId,
        SemanticElementId SemanticElementId,
        bool IsCollapsed);
}
