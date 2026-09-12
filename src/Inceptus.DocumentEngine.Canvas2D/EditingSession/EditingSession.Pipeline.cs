using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask<EditingSessionOperationResult> StartFullRebuildAsync(
        DocumentSnapshot snapshot,
        bool awaitCompletion,
        CancellationToken cancellationToken)
    {
        var started = BeginRun(snapshot, sceneOnlyArtifacts: null, cancellationToken);
        if (started.Task is null)
        {
            return OperationResult(EditingSessionOperationStatus.Closed, started.Diagnostics);
        }

        NotifyStateChanged();
        if (!awaitCompletion)
        {
            return OperationResult(EditingSessionOperationStatus.Succeeded);
        }

        return await started.Task.ConfigureAwait(false);
    }

    private (Task<EditingSessionOperationResult>? Task, ImmutableArray<Diagnostic> Diagnostics)
        BeginRun(
            DocumentSnapshot snapshot,
            EditingSessionPipelineArtifacts? sceneOnlyArtifacts,
            CancellationToken cancellationToken,
            EditingSessionGeneration? expectedGeneration = null,
            EditingSessionStatus? expectedStatus = null,
            bool requireCurrentDocumentRevision = true,
            bool clearActiveGesture = false,
            NodeGeometryPipelineImpact? nodeGeometryImpact = null,
            ScopeNavigationTransition? scopeTransition = null,
            ModelProfileViewStateSnapshot? requestedModelProfileViewState = null,
            ModelProfileElementViewStateSnapshot? requestedModelProfileElementViewState = null)
    {
        CancellationTokenSource? preceding;
        Canvas2DScene? staleToDispose;
        Canvas2DScene? currentToDispose;
        Canvas2DScene? fallbackToDispose;
        Task<EditingSessionOperationResult> task;
        lock (_sync)
        {
            if (_closing || _closed)
            {
                var diagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
                    [Error(
                        EditingSessionDiagnosticCodes.Closed,
                        "The Editing Session is closed.",
                        snapshot.DocumentId.Value)],
                    "diagnostics");
                return (null, diagnostics);
            }

            if ((expectedGeneration is not null && expectedGeneration.Value != _generation) ||
                (expectedStatus is not null && expectedStatus.Value != _status) ||
                _document is null ||
                (requireCurrentDocumentRevision && _document.Revision != snapshot.Revision) ||
                (scopeTransition is not null &&
                    (scopeTransition.SourceScopeId != _activeScopeId ||
                     !ReferenceEquals(
                         _activeModelProfileViewState,
                         scopeTransition.SourceModelProfileViewState) ||
                     !ReferenceEquals(
                         EditorState.CaptureSnapshot(),
                         scopeTransition.SourceEditorState))))
            {
                return (null, []);
            }

            preceding = _runCancellation;
            EditingSessionPipelineArtifacts? retainedTargetScopeArtifacts = null;
            if (scopeTransition is not null)
            {
                if (!EditorState.TryUpdate(
                        scopeTransition.SourceEditorState,
                        scopeTransition.TargetEditorState))
                {
                    return (null, []);
                }

                if (scopeTransition.CacheSourceViewState)
                {
                    _inactiveScopeViews[scopeTransition.SourceScopeId] =
                        new ScopeViewRuntimeState(
                            GetScopeIdentity(snapshot.SemanticModel, scopeTransition.SourceScopeId),
                            scopeTransition.SourceEditorState.Viewport,
                            scopeTransition.SourceModelProfileViewState);
                }
                else
                {
                    _inactiveScopeViews.Remove(scopeTransition.SourceScopeId);
                }

                if (scopeTransition.CacheSourceArtifacts &&
                    _artifacts is { } sourceArtifacts &&
                    sourceArtifacts.ScopeId == scopeTransition.SourceScopeId &&
                    sourceArtifacts.IsInternallyConsistent)
                {
                    _inactiveScopeArtifacts[scopeTransition.SourceScopeId] =
                        new ScopePipelineRuntimeState(
                            GetScopeIdentity(snapshot.SemanticModel, scopeTransition.SourceScopeId),
                            sourceArtifacts);
                }
                else
                {
                    _inactiveScopeArtifacts.Remove(scopeTransition.SourceScopeId);
                }

                retainedTargetScopeArtifacts = TakeCompatibleCachedArtifacts(
                    snapshot.SemanticModel,
                    scopeTransition.TargetScopeId);
                _inactiveScopeViews.Remove(scopeTransition.TargetScopeId);
                _activeScopeId = scopeTransition.TargetScopeId;
                _activeScopePath = scopeTransition.TargetScopePath;
                _activeModelProfileViewState =
                    scopeTransition.TargetModelProfileViewState;
            }

            var activeScopeId = _activeScopeId;
            var sourceModelProfileViewState = _activeModelProfileViewState;
            var pipelineModelProfileViewState = requestedModelProfileViewState ??
                sourceModelProfileViewState;
            var sourceModelProfileElementViewState =
                _activeModelProfileElementViewState;
            var pipelineModelProfileElementViewState =
                requestedModelProfileElementViewState ??
                sourceModelProfileElementViewState;
            EditingSessionPipelineArtifacts? preservedNodeLayoutArtifacts = null;
            EditingSessionPipelineArtifacts? compatibleScopeLayoutArtifacts = null;
            if (retainedTargetScopeArtifacts is not null &&
                retainedTargetScopeArtifacts.ScopeId == activeScopeId &&
                retainedTargetScopeArtifacts.ProjectedGraph.DocumentId == snapshot.DocumentId &&
                retainedTargetScopeArtifacts.ProjectedGraph.SourceRevision <= snapshot.Revision)
            {
                if (retainedTargetScopeArtifacts.ProjectedGraph.SourceRevision ==
                    snapshot.Revision)
                {
                    sceneOnlyArtifacts = retainedTargetScopeArtifacts;
                }
                else
                {
                    compatibleScopeLayoutArtifacts = retainedTargetScopeArtifacts;
                }
            }

            if (nodeGeometryImpact is not null && preservedNodeLayoutArtifacts is null)
            {
                if (_compatibleArtifacts is { } compatibleArtifacts &&
                    compatibleArtifacts.ScopeId == activeScopeId)
                {
                    preservedNodeLayoutArtifacts = compatibleArtifacts;
                }
                else if (_status == EditingSessionStatus.Rebuilding)
                {
                    for (var index = _nodeLayoutHistory.Length - 1; index >= 0; index--)
                    {
                        var entry = _nodeLayoutHistory[index];
                        if (!entry.RequiresExplicitHistoricalRevision && entry.Artifacts.ScopeId == activeScopeId)
                        {
                            preservedNodeLayoutArtifacts = entry.Artifacts;
                            break;
                        }
                    }
                }
            }

            if (sceneOnlyArtifacts is null &&
                nodeGeometryImpact is null &&
                compatibleScopeLayoutArtifacts is null &&
                _compatibleArtifacts is { } currentCompatibleArtifacts &&
                currentCompatibleArtifacts.ScopeId == activeScopeId &&
                currentCompatibleArtifacts.ProjectedGraph.DocumentId == snapshot.DocumentId &&
                currentCompatibleArtifacts.ProjectedGraph.SourceRevision < snapshot.Revision)
            {
                compatibleScopeLayoutArtifacts = currentCompatibleArtifacts;
            }

            var nodeLayoutHistory = nodeGeometryImpact is not null
                ? _nodeLayoutHistory
                    .Where(entry => entry.Artifacts.ScopeId == activeScopeId &&
                        (!entry.RequiresExplicitHistoricalRevision ||
                         nodeGeometryImpact.HistoricalSourceRevision == entry.Artifacts.ProjectedGraph.SourceRevision))
                    .Select(static entry => entry.Artifacts)
                    .ToImmutableArray()
                : [];
            if (sceneOnlyArtifacts is not null &&
                !sceneOnlyArtifacts.IsCompatibleWith(
                    snapshot.DocumentId,
                    snapshot.Revision,
                    activeScopeId))
            {
                sceneOnlyArtifacts = null;
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            _runCancellation = linked;
            _generation = new EditingSessionGeneration(checked(_generation.Value + 1));
            var generation = _generation;
            staleToDispose = null;
            currentToDispose = null;
            fallbackToDispose = null;
            if (scopeTransition is not null)
            {
                currentToDispose = _currentScene;
                staleToDispose = _lastKnownGoodScene;
                fallbackToDispose = _activeGestureFallbackScene;
                _lastKnownGoodScene = null;
                _activeGestureFallbackScene = null;
                _lastKnownGoodSceneNeedsRender = false;
            }
            else if (_currentScene is not null)
            {
                if (_currentSceneHasActiveGesture &&
                    _activeGestureFallbackScene is not null)
                {
                    currentToDispose = _currentScene;
                    staleToDispose = _lastKnownGoodScene;
                    _lastKnownGoodScene = _activeGestureFallbackScene;
                    _activeGestureFallbackScene = null;
                    _lastKnownGoodSceneNeedsRender = true;
                }
                else
                {
                    staleToDispose = !ReferenceEquals(
                        _lastKnownGoodScene,
                        _currentScene)
                            ? _lastKnownGoodScene
                            : null;
                    _lastKnownGoodScene = _currentScene;
                    _lastKnownGoodSceneNeedsRender = false;
                }
            }

            _currentScene = null;
            _currentSceneHasActiveGesture = false;
            _artifacts = null;
            _compatibleArtifacts = sceneOnlyArtifacts;
            _status = EditingSessionStatus.Rebuilding;
            _runtimeDiagnostics = [];
            var editorState = EditorState.CaptureSnapshot();
            while (clearActiveGesture &&
                   (editorState.ActiveGesture is not null ||
                    (requestedModelProfileElementViewState is not null &&
                     !editorState.TemporaryFeedback.IsEmpty)))
            {
                var clearedEditorState = requestedModelProfileElementViewState is not null
                    ? CopyEditorStateWithoutGestureAndTemporaryFeedback(editorState)
                    : CopyEditorStateWithoutGesture(editorState);
                if (EditorState.TryUpdate(editorState, clearedEditorState))
                {
                    editorState = clearedEditorState;
                    break;
                }

                editorState = EditorState.CaptureSnapshot();
            }

            task = RunPipelineAsync(
                generation,
                snapshot,
                activeScopeId,
                editorState,
                sourceModelProfileViewState,
                pipelineModelProfileViewState,
                sourceModelProfileElementViewState,
                pipelineModelProfileElementViewState,
                sceneOnlyArtifacts,
                preservedNodeLayoutArtifacts,
                compatibleScopeLayoutArtifacts,
                nodeLayoutHistory,
                nodeGeometryImpact,
                linked);
            _activeRuns.Add(generation.Value, task);
            _ = task.ContinueWith(
                _ => CompleteRunTracking(generation.Value),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            PulseStateUnderLock();
        }

        CancelRun(preceding);
        staleToDispose?.Dispose();
        if (!ReferenceEquals(currentToDispose, staleToDispose))
        {
            currentToDispose?.Dispose();
        }
        if (!ReferenceEquals(fallbackToDispose, staleToDispose) &&
            !ReferenceEquals(fallbackToDispose, currentToDispose))
        {
            fallbackToDispose?.Dispose();
        }
        return (task, []);
    }

    private async Task<EditingSessionOperationResult> RunPipelineAsync(
        EditingSessionGeneration generation,
        DocumentSnapshot snapshot,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot sourceModelProfileViewState,
        ModelProfileViewStateSnapshot pipelineModelProfileViewState,
        ModelProfileElementViewStateSnapshot sourceModelProfileElementViewState,
        ModelProfileElementViewStateSnapshot pipelineModelProfileElementViewState,
        EditingSessionPipelineArtifacts? sceneOnlyArtifacts,
        EditingSessionPipelineArtifacts? preservedNodeLayoutArtifacts,
        EditingSessionPipelineArtifacts? compatibleScopeLayoutArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        CancellationTokenSource runCancellation)
    {
        try
        {
            return await RunPipelineCoreAsync(
                generation,
                snapshot,
                activeScopeId,
                editorState,
                sourceModelProfileViewState,
                pipelineModelProfileViewState,
                sourceModelProfileElementViewState,
                pipelineModelProfileElementViewState,
                sceneOnlyArtifacts,
                preservedNodeLayoutArtifacts,
                compatibleScopeLayoutArtifacts,
                nodeLayoutHistory,
                nodeGeometryImpact,
                runCancellation).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_runCancellation, runCancellation))
                {
                    _runCancellation = null;
                }

                PulseStateUnderLock();
            }

            runCancellation.Dispose();
        }
    }

    private void CompleteRunTracking(long generation)
    {
        lock (_sync)
        {
            _activeRuns.Remove(generation);
            PulseStateUnderLock();
        }
    }

    private async Task<EditingSessionOperationResult> RunPipelineCoreAsync(
        EditingSessionGeneration generation,
        DocumentSnapshot snapshot,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot sourceModelProfileViewState,
        ModelProfileViewStateSnapshot pipelineModelProfileViewState,
        ModelProfileElementViewStateSnapshot sourceModelProfileElementViewState,
        ModelProfileElementViewStateSnapshot pipelineModelProfileElementViewState,
        EditingSessionPipelineArtifacts? sceneOnlyArtifacts,
        EditingSessionPipelineArtifacts? preservedNodeLayoutArtifacts,
        EditingSessionPipelineArtifacts? compatibleScopeLayoutArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        CancellationTokenSource runCancellation)
    {
        await Task.Yield();
        EditingSessionPipelineResult result;
        try
        {
            result = sceneOnlyArtifacts is not null
                ? await _pipeline.RebuildSceneAsync(
                    snapshot,
                    sceneOnlyArtifacts,
                    editorState,
                    pipelineModelProfileViewState,
                    pipelineModelProfileElementViewState,
                    runCancellation.Token).ConfigureAwait(false)
                : preservedNodeLayoutArtifacts is not null
                    ? await _pipeline.RunPreservingNodeLayoutAsync(
                        snapshot,
                        activeScopeId,
                        preservedNodeLayoutArtifacts,
                        nodeLayoutHistory,
                        nodeGeometryImpact!,
                        editorState,
                        pipelineModelProfileViewState,
                        pipelineModelProfileElementViewState,
                        runCancellation.Token).ConfigureAwait(false)
                    : compatibleScopeLayoutArtifacts is not null
                        ? await _pipeline.RunPreservingScopeLayoutIfCompatibleAsync(
                            snapshot,
                            activeScopeId,
                            compatibleScopeLayoutArtifacts,
                            editorState,
                            pipelineModelProfileViewState,
                            pipelineModelProfileElementViewState,
                            runCancellation.Token).ConfigureAwait(false)
                    : await _pipeline.RunFullAsync(
                        snapshot,
                        activeScopeId,
                        editorState,
                        pipelineModelProfileViewState,
                        pipelineModelProfileElementViewState,
                        runCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            result = EditingSessionPipelineResult.Cancelled(
                [CancelledDiagnostic(snapshot.DocumentId.Value)]);
        }
#pragma warning disable CA1031 // Stage faults become stable runtime diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            result = EditingSessionPipelineResult.Failure(
                [Error(
                    EditingSessionDiagnosticCodes.PipelineFailed,
                    "The Editing Session processing pipeline failed.",
                    snapshot.DocumentId.Value,
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name))]);
        }
#pragma warning restore CA1031

        Canvas2DScene? sceneToDispose = null;
        Canvas2DScene? staleToDispose = null;
        Canvas2DScene? fallbackToDispose = null;
        Canvas2DScene? staleToRender = null;
        EditingSessionPipelineArtifacts? followUpArtifacts = null;
        EditingSessionGeneration? followUpExpectedGeneration = null;
        ModelProfileViewStateSnapshot? followUpModelProfileViewState = null;
        ModelProfileElementViewStateSnapshot? followUpModelProfileElementViewState = null;
        Task<EditingSessionOperationResult>? followUpTask = null;
        var stateChanged = false;
        EditingSessionOperationStatus operationStatus;
        lock (_sync)
        {
            if (_closing || _closed || generation != _generation)
            {
                sceneToDispose = result.Scene;
                operationStatus = _closing || _closed
                    ? EditingSessionOperationStatus.Closed
                    : EditingSessionOperationStatus.Superseded;
            }
            else if (result.Status == EditingSessionPipelineStatus.Cancelled)
            {
                _status = EditingSessionStatus.RuntimeFaulted;
                _runtimeDiagnostics = result.Diagnostics;
                operationStatus = EditingSessionOperationStatus.Cancelled;
                stateChanged = true;
                PulseStateUnderLock();
            }
            else if (result.Status != EditingSessionPipelineStatus.Succeeded ||
                     result.Scene is null || result.Artifacts is null)
            {
                _status = EditingSessionStatus.RuntimeFaulted;
                _runtimeDiagnostics = EnsureFailureDiagnostic(result.Diagnostics, snapshot);
                operationStatus = EditingSessionOperationStatus.Failed;
                stateChanged = true;
                PulseStateUnderLock();
            }
            else if (HasValidProvenance(result, snapshot, activeScopeId) &&
                     TryReconcileEditorStateForScene(
                         editorState,
                         result.Scene,
                         deferAbsentSemanticSceneSelection:
                            !pipelineModelProfileViewState.Equals(
                                sourceModelProfileViewState) ||
                            !pipelineModelProfileElementViewState.Equals(
                                sourceModelProfileElementViewState)))
            {
                sceneToDispose = result.Scene;
                followUpArtifacts = result.Artifacts;
                followUpExpectedGeneration = generation;
                followUpModelProfileViewState = pipelineModelProfileViewState;
                followUpModelProfileElementViewState = pipelineModelProfileElementViewState;
                _compatibleArtifacts = result.Artifacts;
                operationStatus = EditingSessionOperationStatus.Superseded;
                stateChanged = true;
                PulseStateUnderLock();
            }
            else if (HasValidProvenance(result, snapshot, activeScopeId) &&
                     !EditorState.CaptureSnapshot().Equals(editorState))
            {
                sceneToDispose = result.Scene;
                followUpArtifacts = result.Artifacts;
                followUpExpectedGeneration = generation;
                followUpModelProfileViewState = pipelineModelProfileViewState;
                followUpModelProfileElementViewState = pipelineModelProfileElementViewState;
                _compatibleArtifacts = result.Artifacts;
                operationStatus = EditingSessionOperationStatus.Superseded;
                stateChanged = true;
                PulseStateUnderLock();
            }
            else if (!CanInstall(
                         result,
                         snapshot,
                         activeScopeId,
                         editorState,
                         sourceModelProfileViewState,
                         sourceModelProfileElementViewState))
            {
                sceneToDispose = result.Scene;
                _runtimeDiagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
                    [Error(
                        EditingSessionDiagnosticCodes.StalePipelineResult,
                        "A stale or incompatible pipeline result was not installed.",
                        snapshot.DocumentId.Value)],
                    "diagnostics");
                _status = EditingSessionStatus.RuntimeFaulted;
                _compatibleArtifacts = null;
                operationStatus = EditingSessionOperationStatus.Failed;
                stateChanged = true;
                PulseStateUnderLock();
            }
            else
            {
                var hasActiveGesture = editorState.ActiveGesture is not null;
                if (hasActiveGesture && _lastKnownGoodScene is not null)
                {
                    fallbackToDispose = _activeGestureFallbackScene;
                    _activeGestureFallbackScene = _lastKnownGoodScene;
                    _lastKnownGoodScene = null;
                }
                else if (!hasActiveGesture)
                {
                    staleToDispose = _lastKnownGoodScene;
                    fallbackToDispose = _activeGestureFallbackScene;
                    _lastKnownGoodScene = null;
                    _activeGestureFallbackScene = null;
                }

                _lastKnownGoodSceneNeedsRender = false;
                _currentScene = result.Scene;
                _currentSceneHasActiveGesture = hasActiveGesture;
                _artifacts = result.Artifacts;
                _compatibleArtifacts = result.Artifacts;
                RecordNodeLayoutHistoryUnderLock(result.Artifacts);
                _status = EditingSessionStatus.Ready;
                _runtimeDiagnostics = result.Diagnostics;
                _presentationDiagnostics = [];
                if (!_activeModelProfileViewState.Equals(pipelineModelProfileViewState))
                {
                    _activeModelProfileViewState = pipelineModelProfileViewState;
                }

                if (!_activeModelProfileElementViewState.Equals(
                        pipelineModelProfileElementViewState))
                {
                    _activeModelProfileElementViewState =
                        pipelineModelProfileElementViewState;
                }

                if ((!sourceModelProfileViewState.Equals(
                         pipelineModelProfileViewState) ||
                     !sourceModelProfileElementViewState.Equals(
                         pipelineModelProfileElementViewState)) &&
                    editorState.SemanticSceneSelection is not null &&
                    !ContainsSelectableSemanticSceneTarget(
                        result.Scene,
                        editorState.SemanticSceneSelection))
                {
                    _ = EditorState.TryUpdate(
                        editorState,
                        new EditorStateSnapshot(
                            editorState.Selection,
                            editorState.HoveredObjectId,
                            editorState.ActiveToolId,
                            editorState.FocusTargetId,
                            editorState.Viewport,
                            editorState.ActiveGesture,
                            editorState.TemporaryFeedback,
                            editorState.ToolState,
                            semanticSceneSelection: null));
                }

                operationStatus = EditingSessionOperationStatus.Succeeded;
                stateChanged = true;
                PulseStateUnderLock();
            }

            if (_status == EditingSessionStatus.RuntimeFaulted &&
                _lastKnownGoodSceneNeedsRender)
            {
                staleToRender = _lastKnownGoodScene;
                _lastKnownGoodSceneNeedsRender = false;
            }
        }

        sceneToDispose?.Dispose();
        staleToDispose?.Dispose();
        if (!ReferenceEquals(fallbackToDispose, staleToDispose) &&
            !ReferenceEquals(fallbackToDispose, sceneToDispose))
        {
            fallbackToDispose?.Dispose();
        }
        if (staleToRender is not null)
        {
            await RenderLastKnownGoodSceneAsync(generation, staleToRender)
                .ConfigureAwait(false);
        }
        if (stateChanged)
        {
            NotifyStateChanged();
        }
        if (followUpArtifacts is not null)
        {
            var followUp = BeginRun(
                snapshot,
                followUpArtifacts,
                CancellationToken.None,
                followUpExpectedGeneration,
                EditingSessionStatus.Rebuilding,
                requestedModelProfileViewState: followUpModelProfileViewState,
                requestedModelProfileElementViewState:
                    followUpModelProfileElementViewState);
            if (followUp.Task is not null)
            {
                followUpTask = followUp.Task;
                NotifyStateChanged();
            }
        }

        if (followUpTask is not null)
        {
            return await followUpTask.ConfigureAwait(false);
        }

        if (operationStatus == EditingSessionOperationStatus.Succeeded)
        {
            await RenderInstalledSceneAsync(generation, result.Scene!).ConfigureAwait(false);
        }

        return OperationResult(operationStatus, result.Diagnostics);
    }

    private void RecordNodeLayoutHistoryUnderLock(
        EditingSessionPipelineArtifacts artifacts)
    {
        for (var index = 0; index < _nodeLayoutHistory.Length; index++)
        {
            if (_nodeLayoutHistory[index].Artifacts.ScopeId == artifacts.ScopeId &&
                _nodeLayoutHistory[index].Artifacts.ProjectedGraph.SourceRevision ==
                    artifacts.ProjectedGraph.SourceRevision)
            {
                _nodeLayoutHistory = _nodeLayoutHistory.SetItem(index, new NodeLayoutHistoryEntry(artifacts));
                return;
            }
        }

        _nodeLayoutHistory = _nodeLayoutHistory.Add(new NodeLayoutHistoryEntry(artifacts));
    }

    private sealed record NodeLayoutHistoryEntry(
        EditingSessionPipelineArtifacts Artifacts,
        bool RequiresExplicitHistoricalRevision = false);

    private bool CanInstall(
        EditingSessionPipelineResult result,
        DocumentSnapshot snapshot,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot sourceModelProfileViewState,
        ModelProfileElementViewStateSnapshot sourceModelProfileElementViewState)
    {
        var scene = result.Scene!;
        var artifacts = result.Artifacts!;
        return AttachedDocument.DocumentId == snapshot.DocumentId &&
            AttachedDocument.Revision == snapshot.Revision &&
            _activeScopeId == activeScopeId &&
            _activeModelProfileViewState.Equals(sourceModelProfileViewState) &&
            _activeModelProfileElementViewState.Equals(
                sourceModelProfileElementViewState) &&
            EditorState.CaptureSnapshot().Equals(editorState) &&
            artifacts.IsCompatibleWith(
                snapshot.DocumentId,
                snapshot.Revision,
                activeScopeId) &&
            scene.DocumentId == snapshot.DocumentId &&
            scene.SourceRevision == snapshot.Revision &&
            scene.LayoutAlgorithmId == artifacts.LayoutResult.AlgorithmId &&
            scene.RoutingAlgorithmId == artifacts.RoutingResult.RoutingAlgorithmId;
    }

    private bool HasValidProvenance(
        EditingSessionPipelineResult result,
        DocumentSnapshot snapshot,
        DocumentScopeId activeScopeId)
    {
        var scene = result.Scene!;
        var artifacts = result.Artifacts!;
        return AttachedDocument.DocumentId == snapshot.DocumentId &&
            AttachedDocument.Revision == snapshot.Revision &&
            _activeScopeId == activeScopeId &&
            artifacts.IsCompatibleWith(
                snapshot.DocumentId,
                snapshot.Revision,
                activeScopeId) &&
            scene.DocumentId == snapshot.DocumentId &&
            scene.SourceRevision == snapshot.Revision &&
            scene.LayoutAlgorithmId == artifacts.LayoutResult.AlgorithmId &&
            scene.RoutingAlgorithmId == artifacts.RoutingResult.RoutingAlgorithmId;
    }

    private bool TryReconcileEditorStateForScene(
        EditorStateSnapshot source,
        Canvas2DScene scene,
        bool deferAbsentSemanticSceneSelection = false)
    {
        var selectableVisualStateIds = scene.Items
            .Where(item =>
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                item.Origin.VisualStateId is not null &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            .Select(static item => item.Origin.VisualStateId!)
            .ToHashSet();
        var selectableSemanticElementIds = scene.Items
            .Where(Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable)
            .Where(static item =>
                item.IsVisible && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            .Select(static item => item.Origin.SemanticElementId!)
            .ToHashSet();
        var selection = source.Selection
            .Where(selectableVisualStateIds.Contains)
            .ToArray();
        var hoveredObjectId = source.HoveredObjectId is not null &&
            scene.Items.Any(item =>
                item.Id == source.HoveredObjectId &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
                ? source.HoveredObjectId
                : null;
        var semanticSceneSelection = source.SemanticSceneSelection is not null &&
            selectableSemanticElementIds.Contains(source.SemanticSceneSelection)
                ? source.SemanticSceneSelection
                : null;
        if (selection.Length == source.Selection.Length &&
            hoveredObjectId == source.HoveredObjectId &&
            semanticSceneSelection == source.SemanticSceneSelection)
        {
            return false;
        }

        if (deferAbsentSemanticSceneSelection &&
            source.SemanticSceneSelection is not null &&
            semanticSceneSelection is null &&
            selection.AsSpan().SequenceEqual(source.Selection.AsSpan()) &&
            hoveredObjectId == source.HoveredObjectId)
        {
            return false;
        }

        var reconciled = new EditorStateSnapshot(
            selection,
            hoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState,
            semanticSceneSelection);
        return EditorState.TryUpdate(source, reconciled);
    }

    private static bool ContainsSelectableSemanticSceneTarget(
        Canvas2DScene scene,
        SemanticElementId semanticElementId) =>
        scene.Items.Any(item =>
            item.Origin.SemanticElementId == semanticElementId &&
            item.IsVisible &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
            Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item));

    private static EditorStateSnapshot CopyEditorStateWithoutGestureAndTemporaryFeedback(
        EditorStateSnapshot source) =>
        new(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            activeGesture: null,
            temporaryFeedback: null,
            source.ToolState,
            source.SemanticSceneSelection);

    private async ValueTask<EditingSessionOperationResult> UpdateEditorStateCoreAsync(
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken,
        DocumentScopeId? expectedScopeId = null,
        EditingSessionGeneration? requiredGeneration = null)
    {
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        EditingSessionPipelineArtifacts? artifacts;
        EditingSessionState state;
        EditingSessionGeneration expectedGeneration;
        DocumentSnapshot? snapshot = null;
        Task<EditingSessionOperationResult>? runTask = null;
        var notifyOnly = false;
        try
        {
            while (true)
            {
                lock (_sync)
                {
                    if (_closing || _closed)
                    {
                        return OperationResult(EditingSessionOperationStatus.Closed);
                    }

                    if ((expectedScopeId is not null &&
                         expectedScopeId != _activeScopeId) ||
                        (requiredGeneration is not null &&
                         requiredGeneration.Value != _generation))
                    {
                        return OperationResult(
                            EditingSessionOperationStatus.Superseded,
                            [Error(
                                EditingSessionDiagnosticCodes.StalePipelineResult,
                                "The Editor State observation no longer belongs to the current scope generation.",
                                _documentId.Value)]);
                    }

                    state = CaptureStateUnderLock();
                    artifacts = _compatibleArtifacts;
                    expectedGeneration = _generation;
                    if (state.EditorState.Equals(editorState))
                    {
                        return OperationResult(EditingSessionOperationStatus.Succeeded);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return OperationResult(
                            EditingSessionOperationStatus.Cancelled,
                            [CancelledDiagnostic(_documentId.Value)]);
                    }

                    // The close transition uses the same lock, so Editor State cannot be
                    // mutated after the session has begun closing.
                    if (EditorState.TryUpdate(state.EditorState, editorState))
                    {
                        break;
                    }
                }
            }

            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(EditingSessionOperationStatus.Closed);
                }
            }

            snapshot = AttachedDocument.CaptureSnapshot();
            if (artifacts is null ||
                !artifacts.IsCompatibleWith(
                    snapshot.DocumentId,
                    snapshot.Revision,
                    _activeScopeId))
            {
                notifyOnly = true;
            }
            else
            {
                var started = BeginRun(
                    snapshot,
                    artifacts,
                    cancellationToken,
                    expectedGeneration);
                runTask = started.Task;
            }
        }
        finally
        {
            _commandGate.Release();
        }

        if (notifyOnly)
        {
            NotifyStateChanged();
            return OperationResult(EditingSessionOperationStatus.Succeeded);
        }

        if (runTask is null)
        {
            lock (_sync)
            {
                return OperationResult(
                    _closing || _closed
                        ? EditingSessionOperationStatus.Closed
                        : EditingSessionOperationStatus.Superseded);
            }
        }

        NotifyStateChanged();
        return await runTask.ConfigureAwait(false);
    }

    private async ValueTask<EditingSessionOperationResult> RetryCoreAsync(
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        Task<EditingSessionOperationResult>? runTask;
        try
        {
            EditingSessionGeneration expectedGeneration;
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(EditingSessionOperationStatus.Closed);
                }

                if (_status != EditingSessionStatus.RuntimeFaulted)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "Pipeline retry requires a RuntimeFaulted Editing Session.",
                            _documentId.Value)]);
                }

                expectedGeneration = _generation;
            }

            var snapshot = AttachedDocument.CaptureSnapshot();
            var started = BeginRun(
                snapshot,
                sceneOnlyArtifacts: null,
                cancellationToken,
                expectedGeneration,
                EditingSessionStatus.RuntimeFaulted);
            runTask = started.Task;
            if (runTask is null)
            {
                return OperationResult(
                    EditingSessionOperationStatus.Rejected,
                    [Error(
                        EditingSessionDiagnosticCodes.InvalidOperation,
                        "The Editing Session changed before retry could begin.",
                        _documentId.Value)]);
            }
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return await runTask.ConfigureAwait(false);
    }

    private void ObserveDocumentChangedCore(DocumentChangedEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.DocumentId != _documentId)
        {
            return;
        }

        ScopeNavigationTransition? recoveryTransition = null;
        lock (_sync)
        {
            _activeModelProfileElementViewState =
                _activeModelProfileElementViewState.RetainSemanticElements(
                    change.CommittedSnapshot.SemanticModel.Elements.Select(
                        static element => element.Id));
            if (ScopeExists(change.CommittedSnapshot.SemanticModel, _activeScopeId))
            {
                _activeScopePath = CreateScopePath(
                    change.CommittedSnapshot.SemanticModel,
                    _activeScopeId);
            }
            else
            {
                var recoveredScopeId = ResolveSurvivingScope(
                    change.CommittedSnapshot.SemanticModel,
                    _activeScopePath);
                var sourceEditorState = EditorState.CaptureSnapshot();
                var cachedView = GetCompatibleCachedView(
                    change.CommittedSnapshot.SemanticModel,
                    recoveredScopeId);
                var targetViewport = CreateTargetViewport(
                    sourceEditorState.Viewport,
                    cachedView?.Viewport);
                recoveryTransition = new ScopeNavigationTransition(
                    _activeScopeId,
                    recoveredScopeId,
                    sourceEditorState,
                    new EditorStateSnapshot(viewport: targetViewport),
                    _activeModelProfileViewState,
                    cachedView?.ModelProfileViewState ??
                        ModelProfileViewStateSnapshot.Empty,
                    CreateScopePath(
                        change.CommittedSnapshot.SemanticModel,
                        recoveredScopeId),
                    CacheSourceViewState: false,
                    CacheSourceArtifacts: false);
            }

            foreach (var cachedScopeId in _inactiveScopeViews.Keys
                         .Where(scopeId => !ScopeExists(
                             change.CommittedSnapshot.SemanticModel,
                             scopeId))
                         .ToArray())
            {
                // Preserve the established viewport tombstone so an exact History
                // restoration can recover the prior view. Profile visibility is newer
                // runtime state and is reset when a deleted identity is later reused.
                _inactiveScopeViews[cachedScopeId] =
                    _inactiveScopeViews[cachedScopeId] with
                    {
                        ModelProfileViewState = ModelProfileViewStateSnapshot.Empty,
                    };
            }

            foreach (var cachedScopeId in _inactiveScopeArtifacts.Keys
                         .Where(scopeId => !ScopeExists(
                             change.CommittedSnapshot.SemanticModel,
                             scopeId))
                         .ToArray())
            {
                _inactiveScopeArtifacts.Remove(cachedScopeId);
            }

            // Scope deletion invalidates navigation/current artifacts, not exact History
            // provenance. Retain old geometry only for an explicit revision replay; a
            // newly created scope reusing the identity must never implicitly revive it.
            _nodeLayoutHistory = _nodeLayoutHistory
                .Select(entry => ScopeExists(change.CommittedSnapshot.SemanticModel, entry.Artifacts.ScopeId)
                    ? entry
                    : entry with { RequiresExplicitHistoricalRevision = true })
                .ToImmutableArray();
        }

        if (recoveryTransition is null &&
            TryRebindUnchangedPresentation(change))
        {
            NotifyStateChanged();
            return;
        }

        var sceneOnlyArtifacts = recoveryTransition is null
            ? TryRebindSceneOnlyArtifacts(change)
            : null;
        var started = BeginRun(
            change.CommittedSnapshot,
            sceneOnlyArtifacts,
            CancellationToken.None,
            requireCurrentDocumentRevision: false,
            clearActiveGesture: recoveryTransition is null,
            nodeGeometryImpact: recoveryTransition is null
                ? change.NodeGeometryImpact
                : null,
            scopeTransition: recoveryTransition);
        lock (_sync)
        {
            if (change.CommittedRevision > _observedEventRevision)
            {
                _observedEventRevision = change.CommittedRevision;
            }

            PulseStateUnderLock();
        }

        if (started.Task is not null)
        {
            NotifyStateChanged();
        }
    }

    private bool TryRebindUnchangedPresentation(DocumentChangedEvent change)
    {
        if (change.PipelineInvalidation != PipelineInvalidation.None)
        {
            return false;
        }

        lock (_sync)
        {
            if (_status != EditingSessionStatus.Ready ||
                _currentScene is not { } currentScene ||
                _artifacts is not { } artifacts ||
                !artifacts.IsCompatibleWith(
                    change.DocumentId,
                    change.PreviousRevision,
                    _activeScopeId) ||
                currentScene.DocumentId != change.DocumentId ||
                currentScene.SourceRevision != change.PreviousRevision)
            {
                return false;
            }

            var reboundArtifacts = artifacts.RebindToCommittedRevision(
                change.DocumentId,
                change.PreviousRevision,
                change.CommittedRevision);
            _currentScene = currentScene.RebindToCommittedRevision(
                change.DocumentId,
                change.PreviousRevision,
                change.CommittedRevision);
            _artifacts = reboundArtifacts;
            _compatibleArtifacts = reboundArtifacts;

            foreach (var scopeId in _inactiveScopeArtifacts.Keys.ToArray())
            {
                var cached = _inactiveScopeArtifacts[scopeId];
                if (cached.Artifacts.IsCompatibleWith(
                        change.DocumentId,
                        change.PreviousRevision,
                        scopeId))
                {
                    _inactiveScopeArtifacts[scopeId] =
                        new ScopePipelineRuntimeState(
                            GetScopeIdentity(change.CommittedSnapshot.SemanticModel, scopeId),
                            cached.Artifacts.RebindToCommittedRevision(
                                change.DocumentId,
                                change.PreviousRevision,
                                change.CommittedRevision));
                }
            }

            RecordNodeLayoutHistoryUnderLock(reboundArtifacts);
            if (change.CommittedRevision > _observedEventRevision)
            {
                _observedEventRevision = change.CommittedRevision;
            }

            PulseStateUnderLock();
            return true;
        }
    }

    private EditingSessionPipelineArtifacts? TryRebindSceneOnlyArtifacts(
        DocumentChangedEvent change)
    {
        if (change.PipelineInvalidation != PipelineInvalidation.Scene)
        {
            return null;
        }

        lock (_sync)
        {
            return _compatibleArtifacts is { } artifacts &&
                artifacts.IsCompatibleWith(
                    change.DocumentId,
                    change.PreviousRevision,
                    _activeScopeId)
                    ? artifacts.RebindToCommittedRevision(
                        change.DocumentId,
                        change.PreviousRevision,
                        change.CommittedRevision)
                    : null;
        }
    }

    private static ImmutableArray<Diagnostic> EnsureFailureDiagnostic(
        ImmutableArray<Diagnostic> diagnostics,
        DocumentSnapshot snapshot) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? diagnostics
            : EditingSessionDiagnosticCollection.CopyAndOrder(
                diagnostics.Add(Error(
                    EditingSessionDiagnosticCodes.PipelineFailed,
                    "The Editing Session processing pipeline did not produce a scene.",
                    snapshot.DocumentId.Value)),
                nameof(diagnostics));
}
