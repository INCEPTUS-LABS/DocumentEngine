using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class EditingSessionScopeNavigationTests
{
    [Fact]
    public async Task LegacyScopeViewportsNormalizeWhenRestoredAfterSurfaceObservationIncludingHistory()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateScopedDocument(inputs, Scope(ScopeAId, Root(inputs)), Scope(ScopeBId, ScopeAId));
        var initial = new EditorStateSnapshot(viewport: new ViewportSnapshot(1.1, new VectorD(35, -25)));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull((_, editor, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(inputs, editor)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(document, renderer,
            EditingSessionTestHarness.Configuration(initial), pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var snapshot = document.CaptureSnapshot();
        void QueueScene() => pipeline.EnqueueScene((artifacts, visuals, editor, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(artifacts, visuals, editor)));
        void QueueScope() => pipeline.EnqueueScopedFull((state, scope, editor, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(state, scope, editor)));
        void Check(double zoom, VectorD pan)
        {
            var viewport = session.CaptureState().EditorState.Viewport;
            Assert.Equal(zoom, viewport.Zoom);
            Assert.Equal(pan, viewport.Pan);
            Assert.True(viewport.VisibleDocumentRegion!.Value.Left >= 0);
            Assert.True(viewport.VisibleDocumentRegion.Value.Top >= 0);
            Assert.Equal(800 / zoom, viewport.VisibleDocumentRegion.Value.Width, 9);
            Assert.Equal(600 / zoom, viewport.VisibleDocumentRegion.Value.Height, 9);
            Assert.Same(snapshot, document.CaptureSnapshot());
        }

        // Old transforms can be cached before any logical CSS surface is observed.
        QueueScope();
        Assert.True((await session.NavigateToScopeAsync(ScopeAId)).Succeeded);
        QueueScene();
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(0.6, new VectorD(-42, 17)))).Succeeded);
        QueueScope();
        Assert.True((await session.NavigateToScopeAsync(ScopeBId)).Succeeded);
        QueueScene();
        Assert.True((await session.ResizeAsync(new Canvas2DSurfaceSize(800, 600, 1.25))).Succeeded);
        Check(1, default);

        QueueScene();
        Assert.True((await session.UndoAsync()).IsApplied);
        Check(0.6, new VectorD(-42, 0));
        QueueScene();
        Assert.True((await session.UndoAsync()).IsApplied);
        Check(1.1, new VectorD(0, -25));
        QueueScene();
        Assert.True((await session.RedoAsync()).IsApplied);
        Check(0.6, new VectorD(-42, 0));
        QueueScene();
        Assert.True((await session.NavigateToScopeAsync(Root(inputs))).Succeeded);
        Check(1.1, new VectorD(0, -25));
        Assert.Equal(3, pipeline.FullRunCount);
    }
}
