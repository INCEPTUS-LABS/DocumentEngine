using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DViewportNormalizationTests
{
    private static readonly Canvas2DSurfaceSize Surface = new(900, 600, 1.25);

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.7)]
    [InlineData(1)]
    [InlineData(1.1)]
    [InlineData(3.25)]
    public void IndependentCorrectionsAreMinimalExactAndIdempotent(double zoom)
    {
        foreach (var (requested, expected) in new[]
                 {
                     (new VectorD(-23, -41), new VectorD(-23, -41)),
                     (new VectorD(23, -41), new VectorD(0, -41)),
                     (new VectorD(-23, 41), new VectorD(-23, 0)),
                     (new VectorD(23, 41), default(VectorD)),
                     (default(VectorD), default(VectorD)),
                 })
        {
            var candidate = new ViewportSnapshot(zoom, requested);
            var normalized = Canvas2DViewportNormalizer.Normalize(candidate, Surface);
            Assert.Equal(expected, normalized.Pan);
            Assert.Equal(zoom, normalized.Zoom);
            var region = Assert.IsType<RectD>(normalized.VisibleDocumentRegion);
            Assert.True(region.Left >= 0 && region.Top >= 0);
            Assert.Equal(Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(normalized, Surface), region);
            Assert.Same(normalized, Canvas2DViewportNormalizer.Normalize(normalized, Surface));
            Assert.Equal(normalized, Canvas2DViewportNormalizer.Normalize(normalized,
                new Canvas2DSurfaceSize(900, 600, 2)));
        }
    }

    [Theory]
    [InlineData(1e-300)]
    [InlineData(1e-14)]
    [InlineData(1.23456789012345)]
    [InlineData(1e12)]
    public void TinyAndLargeViolationsAlignToExactOriginWithoutEpsilon(double distance)
    {
        var viewport = Canvas2DViewportNormalizer.Normalize(
            new ViewportSnapshot(1.1, new VectorD(distance, distance)), Surface);
        Assert.Equal(default, viewport.Pan);
        Assert.Equal(0d, viewport.VisibleDocumentRegion!.Value.Left);
        Assert.Equal(0d, viewport.VisibleDocumentRegion.Value.Top);
        for (var i = 0; i < 100; i++)
        {
            Assert.Same(viewport, Canvas2DViewportNormalizer.Normalize(viewport, Surface));
        }
    }

    [Fact]
    public void PositiveDocumentSpaceHasNoRightOrBottomLimit()
    {
        var candidate = new ViewportSnapshot(0.7, new VectorD(-1e12, -2e12));
        var normalized = Canvas2DViewportNormalizer.Normalize(candidate, Surface);
        Assert.Equal(candidate.Pan, normalized.Pan);
        Assert.True(normalized.VisibleDocumentRegion!.Value.Left > 1e12);
        Assert.True(normalized.VisibleDocumentRegion.Value.Top > 2e12);
    }

    [Theory]
    [InlineData(-100, -100, 10, 20, -90, -80)]
    [InlineData(-100, 0, -12, 20, -112, 0)]
    [InlineData(0, -100, 20, -12, 0, -112)]
    [InlineData(-10, -20, 50, 8, 0, -12)]
    [InlineData(-10, -20, 8, 50, -2, 0)]
    [InlineData(-10, -20, 50, 50, 0, 0)]
    [InlineData(0, 0, -10, -20, -10, -20)]
    public async Task PanNormalizesOnlyTheBlockedAxesAndKeepsDocumentAndArtifacts(
        double x, double y, double dx, double dy, double finalX, double finalY)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(viewport: Observed(new VectorD(x, y)));
        var pipeline = Pipeline(inputs);
        pipeline.EnqueueScene((artifacts, visuals, editor, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(artifacts, visuals, editor)));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(document, renderer,
            EditingSessionTestHarness.Configuration(initial), pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var snapshot = document.CaptureSnapshot();
        Assert.True((await session.PanViewportAsync(new VectorD(dx, dy))).Succeeded);
        var after = session.CaptureState();
        Assert.Equal(new VectorD(finalX, finalY), after.EditorState.Viewport.Pan);
        Assert.True(after.EditorState.Viewport.VisibleDocumentRegion!.Value.Left >= 0);
        Assert.True(after.EditorState.Viewport.VisibleDocumentRegion.Value.Top >= 0);
        Assert.Equal(before.Generation.Value + 1, after.Generation.Value);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Same(snapshot, document.CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
    }

    [Theory]
    [InlineData(-100, 0, 0, 30)]
    [InlineData(0, -100, 30, 0)]
    [InlineData(0, 0, 30, 40)]
    public async Task RepeatedFullyBlockedPanHasNoStateGenerationPublicationsPipelineOrRender(
        double x, double y, double dx, double dy)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = Pipeline(inputs);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(document, renderer,
            EditingSessionTestHarness.Configuration(new(viewport: Observed(new(x, y), 1.1))), pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var calls = execution.Calls.Count;
        var publications = 0;
        session.StateChanged += (_, _) => publications++;
        for (var i = 0; i < 100; i++)
        {
            Assert.True((await session.PanViewportAsync(new VectorD(dx, dy))).Succeeded);
        }
        var after = session.CaptureState();
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(0, publications);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(calls, execution.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothProgrammaticAdmissionsNormalizeAgainstCurrentSurface(bool wholeEditorState)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = Pipeline(inputs);
        pipeline.EnqueueScene((artifacts, visuals, editor, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(artifacts, visuals, editor)));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(document, renderer,
            EditingSessionTestHarness.Configuration(new(viewport: Observed(new(-100, -100)))), pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var candidate = new ViewportSnapshot(0.5, new VectorD(120, -35));
        var operation = wholeEditorState
            ? await session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: candidate))
            : await session.UpdateViewportAsync(candidate);
        Assert.True(operation.Succeeded);
        var after = session.CaptureState();
        Assert.Equal(new VectorD(0, -35), after.EditorState.Viewport.Pan);
        Assert.Equal(0.5, after.EditorState.Viewport.Zoom);
        Assert.Equal(new RectD(0, 70, 1800, 1200), after.EditorState.Viewport.VisibleDocumentRegion);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachmentAndFirstObservedSurfaceNormalizeLegacyViewport(bool observedAtAttach)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = Pipeline(inputs);
        var requested = observedAtAttach ? Observed(new VectorD(30, 40)) : new ViewportSnapshot(1, new VectorD(30, 40));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(EditingSessionTestHarness.CreateDocument(inputs), renderer,
            EditingSessionTestHarness.Configuration(new(viewport: requested)), pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        if (!observedAtAttach)
        {
            Assert.Null(before.EditorState.Viewport.VisibleDocumentRegion);
            pipeline.EnqueueScene((artifacts, visuals, editor, _) =>
                ValueTask.FromResult(ControlledEditingSessionPipeline.Success(artifacts, visuals, editor)));
            Assert.True((await session.ResizeAsync(Surface)).Succeeded);
        }
        var after = session.CaptureState();
        Assert.Equal(default, after.EditorState.Viewport.Pan);
        Assert.Equal(new RectD(0, 0, 900, 600), after.EditorState.Viewport.VisibleDocumentRegion);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
    }

    private static ViewportSnapshot Observed(VectorD pan, double zoom = 1)
    {
        var candidate = new ViewportSnapshot(zoom, pan);
        return new(zoom, pan, Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(candidate, Surface));
    }

    private static ControlledEditingSessionPipeline Pipeline(Canvas2DSceneTestData inputs)
    {
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull((_, editor, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(inputs, editor)));
        return pipeline;
    }
}
