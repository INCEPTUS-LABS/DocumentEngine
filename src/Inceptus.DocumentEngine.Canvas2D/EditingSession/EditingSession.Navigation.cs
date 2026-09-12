using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask<EditingSessionOperationResult> NavigateToScopeCoreAsync(
        DocumentScopeId scopeId,
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
        EditingSessionOperationResult? immediateResult;
        PreparedHistoryMutation? historyMutation;
        try
        {
            (runTask, immediateResult, historyMutation) = BeginScopeNavigationUnderCommandGate(
                scopeId,
                recordHistory: true,
                cancellationToken);
            if (immediateResult is not null)
            {
                return immediateResult;
            }

            if (runTask is null)
            {
                return OperationResult(EditingSessionOperationStatus.Superseded);
            }

            NotifyStateChanged();
            var completed = await runTask.ConfigureAwait(false);
            if (completed.Succeeded && historyMutation is not null)
            {
                History.InstallPrepared(historyMutation);
                NotifyStateChanged();
            }

            return completed;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private (
        Task<EditingSessionOperationResult>? RunTask,
        EditingSessionOperationResult? ImmediateResult,
        PreparedHistoryMutation? HistoryMutation)
        BeginScopeNavigationUnderCommandGate(
            DocumentScopeId scopeId,
            bool recordHistory,
            CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return (
                null,
                OperationResult(
                    EditingSessionOperationStatus.Cancelled,
                    [CancelledDiagnostic(_documentId.Value)]),
                null);
        }

        DocumentSnapshot snapshot;
        EditingSessionGeneration expectedGeneration;
        ScopeNavigationTransition transition;
        lock (_sync)
        {
            if (_closing || _closed)
            {
                return (null, OperationResult(EditingSessionOperationStatus.Closed), null);
            }

            var state = CaptureStateUnderLock();
            if (state.Status != EditingSessionStatus.Ready || state.CurrentScene is null)
            {
                return (
                    null,
                    OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidOperation,
                            "Scope navigation requires a Ready Editing Session with a current scene.",
                            _documentId.Value)]),
                    null);
            }

            if (scopeId == _activeScopeId)
            {
                return (
                    null,
                    OperationResult(EditingSessionOperationStatus.Succeeded),
                    null);
            }

            snapshot = AttachedDocument.CaptureSnapshot();
            if (!ScopeExists(snapshot.SemanticModel, scopeId))
            {
                return (
                    null,
                    OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            EditingSessionDiagnosticCodes.InvalidScope,
                            $"Document scope '{scopeId}' does not exist in the attached Document.",
                            scopeId.Value)]),
                    null);
            }

            var sourceEditorState = EditorState.CaptureSnapshot();
            var cachedView = GetCompatibleCachedView(
                snapshot.SemanticModel,
                scopeId);
            var targetViewport = CreateTargetViewport(
                sourceEditorState.Viewport,
                cachedView?.Viewport);
            transition = new ScopeNavigationTransition(
                _activeScopeId,
                scopeId,
                sourceEditorState,
                new EditorStateSnapshot(viewport: targetViewport),
                _activeModelProfileViewState,
                cachedView?.ModelProfileViewState ??
                    ModelProfileViewStateSnapshot.Empty,
                CreateScopePath(snapshot.SemanticModel, scopeId),
                CacheSourceViewState: true,
                CacheSourceArtifacts: true);
            expectedGeneration = _generation;
        }

        var historyPreparation = recordHistory
            ? History.PrepareScopeNavigation(
                transition.SourceScopeId,
                transition.TargetScopeId)
            : null;
        if (historyPreparation is { Succeeded: false })
        {
            return (
                null,
                OperationResult(
                    EditingSessionOperationStatus.Rejected,
                    historyPreparation.Diagnostics),
                null);
        }

        var started = BeginRun(
            snapshot,
            sceneOnlyArtifacts: null,
            cancellationToken,
            expectedGeneration,
            EditingSessionStatus.Ready,
            scopeTransition: transition);
        if (started.Task is null)
        {
            return (
                null,
                OperationResult(
                    _closing || _closed
                        ? EditingSessionOperationStatus.Closed
                        : EditingSessionOperationStatus.Superseded,
                    started.Diagnostics),
                null);
        }

        return (started.Task, null, historyPreparation?.Mutation);
    }

    private async ValueTask<bool> ReplayScopeNavigationUnderCommandGateAsync(
        DocumentScopeId scopeId,
        CancellationToken cancellationToken)
    {
        var (runTask, immediateResult, _) = BeginScopeNavigationUnderCommandGate(
            scopeId,
            recordHistory: false,
            cancellationToken);
        if (immediateResult is not null)
        {
            return immediateResult.Succeeded;
        }

        if (runTask is null)
        {
            return false;
        }

        NotifyStateChanged();
        var completed = await runTask.ConfigureAwait(false);
        return completed.Succeeded;
    }

    private static ViewportSnapshot CreateTargetViewport(
        ViewportSnapshot currentViewport,
        ViewportSnapshot? cachedViewport)
    {
        var zoom = cachedViewport?.Zoom ?? ViewportSnapshot.Default.Zoom;
        var pan = cachedViewport?.Pan ?? ViewportSnapshot.Default.Pan;
        var target = new ViewportSnapshot(zoom, pan);
        if (currentViewport.VisibleDocumentRegion is null)
        {
            return target;
        }

        var surface = Canvas2DSceneBuilder.CalculateCanvasCssSurface(currentViewport);
        return new ViewportSnapshot(
            zoom,
            pan,
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(target, surface));
    }

    private static ImmutableArray<DocumentScopeId> CreateScopePath(
        SemanticModelSnapshot semanticModel,
        DocumentScopeId scopeId) =>
        [
            .. semanticModel.GetAncestors(scopeId)
                .Reverse()
                .Select(static scope => scope.Id),
            scopeId,
        ];

    private static bool ScopeExists(
        SemanticModelSnapshot semanticModel,
        DocumentScopeId scopeId) =>
        scopeId == semanticModel.RootScopeId ||
        semanticModel.NestedScopes.Any(scope => scope.Id == scopeId);

    private ScopeViewRuntimeState? GetCompatibleCachedView(
        SemanticModelSnapshot semanticModel,
        DocumentScopeId scopeId)
    {
        if (!_inactiveScopeViews.TryGetValue(scopeId, out var cached))
        {
            return null;
        }

        var currentIdentity = GetScopeIdentity(semanticModel, scopeId);
        if (cached.ScopeIdentity == currentIdentity)
        {
            return cached;
        }

        _inactiveScopeViews.Remove(scopeId);
        return null;
    }

    private EditingSessionPipelineArtifacts? TakeCompatibleCachedArtifacts(
        SemanticModelSnapshot semanticModel,
        DocumentScopeId scopeId)
    {
        if (!_inactiveScopeArtifacts.Remove(scopeId, out var cached))
        {
            return null;
        }

        return cached.ScopeIdentity == GetScopeIdentity(semanticModel, scopeId)
            ? cached.Artifacts
            : null;
    }

    private static DocumentScopeSnapshot GetScopeIdentity(
        SemanticModelSnapshot semanticModel,
        DocumentScopeId scopeId) =>
        scopeId == semanticModel.RootScopeId
            ? semanticModel.GetRootScope()
            : semanticModel.NestedScopes.Single(scope => scope.Id == scopeId);

    private static DocumentScopeId ResolveSurvivingScope(
        SemanticModelSnapshot semanticModel,
        ImmutableArray<DocumentScopeId> previousPath)
    {
        for (var index = previousPath.Length - 1; index >= 0; index--)
        {
            if (ScopeExists(semanticModel, previousPath[index]))
            {
                return previousPath[index];
            }
        }

        return semanticModel.RootScopeId;
    }

    private sealed record ScopeNavigationTransition(
        DocumentScopeId SourceScopeId,
        DocumentScopeId TargetScopeId,
        EditorStateSnapshot SourceEditorState,
        EditorStateSnapshot TargetEditorState,
        ModelProfileViewStateSnapshot SourceModelProfileViewState,
        ModelProfileViewStateSnapshot TargetModelProfileViewState,
        ImmutableArray<DocumentScopeId> TargetScopePath,
        bool CacheSourceViewState,
        bool CacheSourceArtifacts);

    private sealed record ScopeViewRuntimeState(
        DocumentScopeSnapshot ScopeIdentity,
        ViewportSnapshot Viewport,
        ModelProfileViewStateSnapshot ModelProfileViewState);

    private sealed record ScopePipelineRuntimeState(
        DocumentScopeSnapshot ScopeIdentity,
        EditingSessionPipelineArtifacts Artifacts);
}
