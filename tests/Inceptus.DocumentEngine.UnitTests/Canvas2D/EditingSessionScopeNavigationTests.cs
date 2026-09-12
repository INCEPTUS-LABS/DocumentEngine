using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionScopeNavigationTests
{
    private static readonly DocumentScopeId ScopeAId = new("test:session:scope-a");
    private static readonly DocumentScopeId ScopeBId = new("test:session:scope-b");

    [Fact]
    public async Task AttachUsesRootAndSameOrInvalidNavigationDoesNotLoseState()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var beforeDocument = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var same = await session.NavigateToScopeAsync(before.ActiveScopeId);
        var invalid = await session.NavigateToScopeAsync(
            new DocumentScopeId("test:session:missing-scope"));
        var after = session.CaptureState();

        Assert.True(same.Succeeded);
        Assert.Equal(EditingSessionOperationStatus.Rejected, invalid.Status);
        Assert.Contains(invalid.Diagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.InvalidScope);
        Assert.Equal(document.SemanticModel.RootScopeId, after.ActiveScopeId);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task NavigationRunsExactScopePipelineAndClearsPreviousSceneTransients()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var surface = new Canvas2DSurfaceSize(800d, 600d, 1.25d);
        var rootViewport = new ViewportSnapshot(0.8d, new VectorD(24d, -18d));
        var initialEditorState = new EditorStateSnapshot(
            selection: [inputs.VisualModel.VisualStates[0].Id],
            activeToolId: "tool:test-placement",
            focusTargetId: "focus:test",
            viewport: new ViewportSnapshot(
                rootViewport.Zoom,
                rootViewport.Pan,
                Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(rootViewport, surface)),
            activeGesture: new EditorGestureSnapshot(
                "gesture:test-navigation",
                "test-drag",
                new PointD(10d, 20d),
                new PointD(30d, 40d)),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback:test-navigation",
                    "test-feedback",
                    new RectD(1d, 2d, 3d, 4d)),
            ]);
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var beforeDocument = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var navigation = await session.NavigateToScopeAsync(ScopeAId);
        var after = session.CaptureState();

        Assert.True(navigation.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.Equal(ScopeAId, after.ActiveScopeId);
        Assert.NotSame(before.CurrentScene, after.CurrentScene);
        Assert.Null(after.LastKnownGoodScene);
        Assert.Empty(Assert.IsType<ProjectedGraph>(after.ProjectedGraph).Nodes);
        Assert.Empty(after.ProjectedGraph.Edges);
        Assert.Empty(after.EditorState.Selection);
        Assert.Null(after.EditorState.HoveredObjectId);
        Assert.Null(after.EditorState.ActiveToolId);
        Assert.Null(after.EditorState.FocusTargetId);
        Assert.Null(after.EditorState.ActiveGesture);
        Assert.Empty(after.EditorState.TemporaryFeedback);
        Assert.Empty(after.EditorState.ToolState);
        Assert.Equal(ViewportSnapshot.Default.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(ViewportSnapshot.Default.Pan, after.EditorState.Viewport.Pan);
        Assert.Equal(
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                ViewportSnapshot.Default,
                surface),
            after.EditorState.Viewport.VisibleDocumentRegion);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), after.HistoryStatus);
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal([Root(inputs), ScopeAId], pipeline.FullRunScopeIds);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));

        var sameScopeState = session.CaptureState();
        var sameScope = await session.NavigateToScopeAsync(ScopeAId);
        Assert.True(sameScope.Succeeded);
        Assert.Equal(2, pipeline.FullRunCount);
        Assert.Same(sameScopeState.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Same(sameScopeState.EditorState, session.CaptureState().EditorState);
    }

    [Fact]
    public async Task ViewportIsRestoredPerScopeWhileFirstVisitUsesNeutralTransform()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var surface = new Canvas2DSurfaceSize(800d, 600d, 1d);
        var rootTransform = new ViewportSnapshot(1.1d, new VectorD(35d, -25d));
        var rootViewport = new ViewportSnapshot(
            rootTransform.Zoom,
            rootTransform.Pan,
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(rootTransform, surface));
        var initialEditorState = new EditorStateSnapshot(viewport: rootViewport);
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);
        Assert.Equal(ViewportSnapshot.Default.Zoom, session.CaptureState().EditorState.Viewport.Zoom);
        Assert.Equal(ViewportSnapshot.Default.Pan, session.CaptureState().EditorState.Viewport.Pan);

        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        var childTransform = new ViewportSnapshot(0.6d, new VectorD(-42d, 17d));
        Assert.True((await session.UpdateViewportAsync(childTransform)).Succeeded);
        var childViewport = session.CaptureState().EditorState.Viewport;

        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        Assert.True((await session.NavigateToScopeAsync(Root(inputs))).Succeeded);
        AssertViewport(rootViewport, session.CaptureState().EditorState.Viewport);

        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);
        AssertViewport(childViewport, session.CaptureState().EditorState.Viewport);
        Assert.Equal(new VectorD(-42d, 17d), childViewport.Pan);
        Assert.Equal(0.6d, childViewport.Zoom);
        Assert.Equal(new(3, true, false), session.CaptureState().HistoryStatus);
        Assert.Equal(2, pipeline.FullRunCount);
        Assert.Equal(3, pipeline.SceneRebuildCount);
    }

    [Fact]
    public async Task NavigationUndoRedoUsesOneHistoryEntryWithoutChangingDocument()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var original = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        var opened = await session.NavigateToScopeAsync(ScopeAId);

        Assert.True(opened.Succeeded);
        Assert.Equal(ScopeAId, session.CaptureState().ActiveScopeId);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        Assert.Equal(original, document.CaptureSnapshot());

        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        var undo = await session.UndoAsync();

        Assert.True(undo.IsApplied);
        Assert.False(undo.IsCommitted);
        Assert.Equal(Root(inputs), session.CaptureState().ActiveScopeId);
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true),
            session.CaptureState().HistoryStatus);
        Assert.Equal(original, document.CaptureSnapshot());

        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        var redo = await session.RedoAsync();

        Assert.True(redo.IsApplied);
        Assert.False(redo.IsCommitted);
        Assert.Equal(ScopeAId, session.CaptureState().ActiveScopeId);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        Assert.Equal(original, document.CaptureSnapshot());
        Assert.Equal(2, pipeline.FullRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);
    }

    [Fact]
    public async Task DirectAncestorNavigationIsOneChronologicalHistoryAction()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(
            inputs,
            Scope(ScopeAId, Root(inputs)),
            Scope(ScopeBId, ScopeAId));
        var original = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        async ValueTask NavigateAsync(DocumentScopeId scopeId, bool firstVisit)
        {
            if (firstVisit)
            {
                pipeline.EnqueueScopedFull((snapshot, targetScopeId, editorState, _) =>
                    ValueTask.FromResult(SuccessfulEmptyScope(
                        snapshot,
                        targetScopeId,
                        editorState)));
            }
            else
            {
                pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
                    ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                        artifacts,
                        visualModel,
                        editorState)));
            }

            Assert.True((await session.NavigateToScopeAsync(scopeId)).Succeeded);
        }

        await NavigateAsync(ScopeAId, firstVisit: true);
        await NavigateAsync(ScopeBId, firstVisit: true);
        await NavigateAsync(Root(inputs), firstVisit: false);
        Assert.Equal(new HistoryStatus(3, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);

        foreach (var expectedScopeId in new[] { ScopeBId, ScopeAId, Root(inputs) })
        {
            pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
                ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                    artifacts,
                    visualModel,
                    editorState)));
            var undo = await session.UndoAsync();
            Assert.True(undo.IsApplied);
            Assert.Equal(expectedScopeId, session.CaptureState().ActiveScopeId);
        }

        foreach (var expectedScopeId in new[] { ScopeAId, ScopeBId, Root(inputs) })
        {
            pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
                ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                    artifacts,
                    visualModel,
                    editorState)));
            var redo = await session.RedoAsync();
            Assert.True(redo.IsApplied);
            Assert.Equal(expectedScopeId, session.CaptureState().ActiveScopeId);
        }

        Assert.Equal(original, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(3, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task ReusedScopeIdWithDifferentOwnerDoesNotInheritRuntimePresentation()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var originalScope = new DocumentScopeSnapshot(
            ScopeAId,
            Root(inputs),
            new SemanticElementId("test:scope-owner:original"));
        var replacementScope = new DocumentScopeSnapshot(
            ScopeAId,
            Root(inputs),
            new SemanticElementId("test:scope-owner:replacement"));
        var document = CreateScopedDocument(inputs, originalScope);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        Assert.True((await session.UpdateViewportAsync(
            new ViewportSnapshot(0.65d, new VectorD(73d, -29d)))).Succeeded);

        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        Assert.True((await session.NavigateToScopeAsync(Root(inputs))).Succeeded);

        var previous = document.CaptureState();
        var committed = RebindDocument(
            previous.Snapshot,
            previous.Snapshot.Revision.Increment(),
            [replacementScope]);
        Assert.True(document.TryInstallState(previous, new DocumentState(committed)));
        pipeline.EnqueuePreservingScopeLayout((snapshot, _, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                RootArtifacts(inputs, snapshot, Root(inputs)),
                snapshot.VisualModel,
                editorState)));
        session.ObserveDocumentChanged(Change(previous.Snapshot, committed));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);
        var state = session.CaptureState();

        Assert.Equal(ViewportSnapshot.Default.Zoom, state.EditorState.Viewport.Zoom);
        Assert.Equal(ViewportSnapshot.Default.Pan, state.EditorState.Viewport.Pan);
        Assert.Equal(3, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingScopeLayoutRunCount);
    }

    [Fact]
    public async Task UnchangedCachedScopeCarriesExactLayoutAcrossUnrelatedGlobalRevision()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scopeA = Scope(ScopeAId, Root(inputs));
        var document = CreateScopedDocument(inputs, scopeA);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var rootBefore = session.CaptureState();

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);

        var previous = document.CaptureState();
        var committed = RebindDocument(
            previous.Snapshot,
            previous.Snapshot.Revision.Increment(),
            [scopeA]);
        Assert.True(document.TryInstallState(previous, new DocumentState(committed)));
        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        session.ObserveDocumentChanged(Change(previous.Snapshot, committed));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        pipeline.EnqueuePreservingScopeLayout((snapshot, previousArtifacts, editorState, _) =>
        {
            Assert.Equal(Root(inputs), previousArtifacts.ScopeId);
            Assert.Equal(rootBefore.DocumentRevision, previousArtifacts.ProjectedGraph.SourceRevision);
            Assert.Same(
                rootBefore.LayoutResult!.Computation,
                previousArtifacts.LayoutResult.Computation);
            var artifacts = RootArtifacts(inputs, snapshot, Root(inputs));
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });

        Assert.True((await session.NavigateToScopeAsync(Root(inputs))).Succeeded);
        var restored = session.CaptureState();

        Assert.Equal(Root(inputs), restored.ActiveScopeId);
        Assert.Equal(committed.Revision, restored.DocumentRevision);
        Assert.Equal(2, pipeline.FullRunCount);
        Assert.Equal(2, pipeline.PreservingScopeLayoutRunCount);
        Assert.Equal(0, pipeline.PreservingNodeLayoutRunCount);
        Assert.Empty(pipeline.NodeGeometryImpacts);
    }

    [Fact]
    public async Task CrossScopeResultAtSameRevisionCannotBecomeAuthoritative()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueScopedFull((snapshot, _, editorState, _) =>
        {
            var rootArtifacts = RootArtifacts(inputs, snapshot, Root(inputs));
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                rootArtifacts,
                snapshot.VisualModel,
                editorState));
        });
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        var navigation = await session.NavigateToScopeAsync(ScopeAId);
        var state = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Failed, navigation.Status);
        Assert.Equal(ScopeAId, state.ActiveScopeId);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, state.Status);
        Assert.Null(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.Null(state.ProjectedGraph);
        Assert.Contains(state.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.StalePipelineResult);
    }

    [Fact]
    public async Task RuntimeFaultedSessionRejectsNavigationWithoutChangingFaultPolicy()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var navigation = await session.NavigateToScopeAsync(ScopeAId);
        var after = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Rejected, navigation.Status);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, after.Status);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.Equal(before.Generation, after.Generation);
        Assert.True(before.RuntimeDiagnostics.SequenceEqual(after.RuntimeDiagnostics));
        Assert.Equal(1, pipeline.FullRunCount);
    }

    [Fact]
    public async Task DeletedActiveScopeRecoversNearestAncestorAndRestorationDoesNotReenter()
    {
        var inputs = Canvas2DSceneTestData.Create();
        DocumentScopeSnapshot[] hierarchy =
        [
            Scope(ScopeAId, Root(inputs)),
            Scope(ScopeBId, ScopeAId),
        ];
        var document = CreateScopedDocument(inputs, hierarchy);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);
        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        Assert.True((await session.NavigateToScopeAsync(ScopeBId)).Succeeded);
        var historyBeforeRecovery = session.CaptureState().HistoryStatus;
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false),
            historyBeforeRecovery);

        var beforeRemoval = document.CaptureState();
        var removedSnapshot = RebindDocument(
            beforeRemoval.Snapshot,
            beforeRemoval.Snapshot.Revision.Increment(),
            [hierarchy[0]]);
        Assert.True(document.TryInstallState(beforeRemoval, new DocumentState(removedSnapshot)));
        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        session.ObserveDocumentChanged(Change(beforeRemoval.Snapshot, removedSnapshot));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var recovered = session.CaptureState();

        Assert.Equal(EditingSessionStatus.Ready, recovered.Status);
        Assert.Equal(ScopeAId, recovered.ActiveScopeId);
        Assert.Null(recovered.LastKnownGoodScene);
        Assert.Empty(recovered.EditorState.Selection);
        Assert.Equal(historyBeforeRecovery, recovered.HistoryStatus);

        var undoRecoveredNavigation = await session.UndoAsync();
        Assert.True(undoRecoveredNavigation.IsApplied);
        Assert.Equal(ScopeAId, session.CaptureState().ActiveScopeId);
        var beforeMissingReplay = session.CaptureState().HistoryStatus;
        Assert.True(beforeMissingReplay.CanRedo);

        var missingReplay = await session.RedoAsync();
        Assert.Equal(HistoryOperationStatus.InternalFailure, missingReplay.Status);
        Assert.Contains(missingReplay.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.RuntimeReplayFailed);
        Assert.Equal(beforeMissingReplay, session.CaptureState().HistoryStatus);
        Assert.Equal(ScopeAId, session.CaptureState().ActiveScopeId);

        var beforeRestoration = document.CaptureState();
        var restoredSnapshot = RebindDocument(
            beforeRestoration.Snapshot,
            beforeRestoration.Snapshot.Revision.Increment(),
            hierarchy);
        Assert.True(document.TryInstallState(
            beforeRestoration,
            new DocumentState(restoredSnapshot)));
        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        session.ObserveDocumentChanged(Change(beforeRestoration.Snapshot, restoredSnapshot));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var restored = session.CaptureState();

        Assert.Equal(EditingSessionStatus.Ready, restored.Status);
        Assert.Equal(ScopeAId, restored.ActiveScopeId);
        Assert.Equal(beforeMissingReplay, restored.HistoryStatus);
        Assert.Contains(restoredSnapshot.SemanticModel.NestedScopes, scope => scope.Id == ScopeBId);

        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));
        var replayedAfterRestoration = await session.RedoAsync();
        Assert.True(replayedAfterRestoration.IsApplied);
        Assert.Equal(ScopeBId, session.CaptureState().ActiveScopeId);
        Assert.Equal([Root(inputs), ScopeAId, ScopeBId, ScopeBId], pipeline.FullRunScopeIds);
        Assert.Equal(
            [Root(inputs), ScopeAId, ScopeBId, ScopeAId, ScopeAId, ScopeBId],
            pipeline.DocumentRunScopeIds);
        Assert.Equal(2, pipeline.PreservingScopeLayoutRunCount);
    }

    [Fact]
    public void PipelineArtifactCompatibilityAndRevisionRebindIncludeScopeIdentity()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var rootScopeId = Root(inputs);
        var rootArtifacts = EditingSessionTestHarness.CreateArtifacts(inputs);
        var childArtifacts = EditingSessionTestHarness.CreateArtifacts(inputs, ScopeAId);

        Assert.True(rootArtifacts.IsCompatibleWith(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            rootScopeId));
        Assert.False(rootArtifacts.IsCompatibleWith(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            ScopeAId));
        Assert.True(childArtifacts.IsCompatibleWith(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            ScopeAId));
        Assert.False(childArtifacts.IsCompatibleWith(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            rootScopeId));

        var rebound = childArtifacts.RebindToCommittedRevision(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.SourceRevision.Increment());
        Assert.Equal(ScopeAId, rebound.ScopeId);
        Assert.True(rebound.IsCompatibleWith(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision.Increment(),
            ScopeAId));
    }

    private static Document CreateScopedDocument(
        Canvas2DSceneTestData inputs,
        params DocumentScopeSnapshot[] scopes)
    {
        var source = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        return new Document(new DocumentSnapshot(
            new SemanticModelSnapshot(
                source.DocumentId,
                source.Revision,
                source.SemanticModel.Elements,
                source.SemanticModel.Relationships,
                scopes),
            source.VisualModel,
            source.Metadata));
    }

    private static DocumentSnapshot RebindDocument(
        DocumentSnapshot source,
        DocumentRevision revision,
        IEnumerable<DocumentScopeSnapshot> scopes) =>
        new(
            new SemanticModelSnapshot(
                source.DocumentId,
                revision,
                source.SemanticModel.Elements,
                source.SemanticModel.Relationships,
                scopes,
                source.SemanticModel.ScopeMemberships.Where(membership =>
                    scopes.Any(scope => scope.Id == membership.ScopeId))),
            new VisualModelSnapshot(
                source.DocumentId,
                revision,
                source.VisualModel.VisualStates),
            new DocumentMetadataSnapshot(
                source.DocumentId,
                revision,
                source.Metadata.SystemManagedProperties,
                source.Metadata.ExtensionProperties));

    private static DocumentChangedEvent Change(
        DocumentSnapshot previous,
        DocumentSnapshot committed) =>
        new(
            previous.DocumentId,
            previous.Revision,
            committed.Revision,
            AuthoritativeDocumentComponent.SemanticModel,
            new CommandTypeId("test:session:scope-change"),
            committed);

    private static EditingSessionPipelineResult SuccessfulEmptyScope(
        DocumentSnapshot snapshot,
        DocumentScopeId scopeId,
        EditorStateSnapshot editorState)
    {
        var artifacts = EmptyArtifacts(snapshot, scopeId);
        return ControlledEditingSessionPipeline.Success(
            artifacts,
            snapshot.VisualModel,
            editorState);
    }

    private static EditingSessionPipelineArtifacts EmptyArtifacts(
        DocumentSnapshot snapshot,
        DocumentScopeId scopeId)
    {
        var graph = new ProjectedGraph(snapshot.DocumentId, snapshot.Revision);
        var layout = new LayoutResult(
            snapshot.DocumentId,
            snapshot.Revision,
            new AlgorithmId("test:layout"),
            LayoutComputation.Empty);
        var routing = new RoutingResult(
            snapshot.DocumentId,
            snapshot.Revision,
            layout.AlgorithmId,
            new AlgorithmId("test:routing"),
            RoutingComputation.Empty);
        return new EditingSessionPipelineArtifacts(scopeId, graph, layout, routing);
    }

    private static EditingSessionPipelineArtifacts RootArtifacts(
        Canvas2DSceneTestData inputs,
        DocumentSnapshot snapshot,
        DocumentScopeId rootScopeId)
    {
        var rebound = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
        return new EditingSessionPipelineArtifacts(
            rootScopeId,
            rebound.ProjectedGraph,
            rebound.LayoutResult,
            rebound.RoutingResult);
    }

    private static DocumentScopeSnapshot Scope(
        DocumentScopeId id,
        DocumentScopeId parentScopeId) =>
        new(id, parentScopeId);

    private static DocumentScopeId Root(Canvas2DSceneTestData inputs) =>
        new(inputs.Graph.DocumentId.Value);

    private static void AssertViewport(ViewportSnapshot expected, ViewportSnapshot actual)
    {
        Assert.Equal(expected.Zoom, actual.Zoom);
        Assert.Equal(expected.Pan, actual.Pan);
        Assert.NotNull(expected.VisibleDocumentRegion);
        Assert.NotNull(actual.VisibleDocumentRegion);
        Assert.Equal(expected.VisibleDocumentRegion.Value.X, actual.VisibleDocumentRegion.Value.X, 10);
        Assert.Equal(expected.VisibleDocumentRegion.Value.Y, actual.VisibleDocumentRegion.Value.Y, 10);
        Assert.Equal(
            expected.VisibleDocumentRegion.Value.Width,
            actual.VisibleDocumentRegion.Value.Width,
            10);
        Assert.Equal(
            expected.VisibleDocumentRegion.Value.Height,
            actual.VisibleDocumentRegion.Value.Height,
            10);
    }
}
