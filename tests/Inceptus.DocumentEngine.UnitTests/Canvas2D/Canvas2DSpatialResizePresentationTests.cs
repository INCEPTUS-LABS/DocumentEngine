using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSpatialResizePresentationTests
{
    [Theory]
    [InlineData(Canvas2DSpatialResizeEdge.Bottom, 1)]
    [InlineData(Canvas2DSpatialResizeEdge.Right, 1.75)]
    public void SpatialEdgeResizeFamilyMatchesIndependentFullCompositionAndPreservesBase(Canvas2DSpatialResizeEdge edge, double zoom)
    {
        var fixture = new Fixture();
        var viewport = new ViewportSnapshot(zoom, new VectorD(17, -29));
        var empty = new EditorStateSnapshot(viewport: viewport);
        var scene = fixture.Build(empty);
        var content = scene.RenderContent;
        foreach (var state in new[]
        {
            State(edge, 800, Canvas2DSpatialResizePhase.Hover, viewport),
            State(edge, 825, Canvas2DSpatialResizePhase.Drag, viewport),
            State(edge, 400, Canvas2DSpatialResizePhase.Drag, viewport),
            State(edge, 860, Canvas2DSpatialResizePhase.Drag, viewport),
            empty,
        })
        {
            var calls = fixture.Contributor.Calls;
            scene = Assert.IsType<Canvas2DScene>(fixture.Reuse(scene, state));
            Assert.Equal(calls, fixture.Contributor.Calls);
            var oracle = fixture.Build(state, fixture.FreshBuilder());
            Assert.Equal(oracle, scene);
            Assert.Equal(content, scene.RenderContent);
            Assert.All(content, item => Assert.Same(item, scene.Items.Single(value => value.Id == item.Id)));
            var guides = scene.Items.Where(IsGuide).ToArray();
            var feedback = state.TemporaryFeedback.FirstOrDefault()?.SpatialResize;
            Assert.Equal(feedback is null ? 0 : feedback.Phase == Canvas2DSpatialResizePhase.Hover ? 1 : edge == Canvas2DSpatialResizeEdge.Right ? 3 : 2,
                guides.Length);
            Assert.All(guides, guide =>
            {
                Assert.Equal(Canvas2DHitTestMode.None, guide.HitTestPolicy.Mode);
                Assert.Equal(Canvas2DSceneOriginCategory.EditorState, guide.Origin.Categories);
                Assert.Null(guide.Origin.VisualStateId);
                Assert.Null(guide.Origin.SemanticElementId);
                Assert.Empty(guide.Metadata);
                Assert.Empty(guide.PersistentAppearance);
                Assert.Null(guide.SpatialRegion);
            });
            Assert.DoesNotContain(scene.RenderContent, IsGuide);
            Assert.Equal(scene.Items.Length, scene.Items.Select(item => item.Id).Distinct().Count());
        }
    }

    [Theory]
    [InlineData(Canvas2DSceneTransientDependency.Unknown)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent)]
    public void SpatialEdgeResizeRequiresItsOwnExplicitInvariance(Canvas2DSceneTransientDependency dependency)
    {
        var fixture = new Fixture(dependency);
        var before = fixture.Build(new());
        var state = State(Canvas2DSpatialResizeEdge.Right, 850);
        Assert.Null(fixture.Reuse(before, state));
        var calls = fixture.Contributor.Calls;
        var fallback = fixture.Build(state);
        Assert.True(fixture.Contributor.Calls > calls);
        Assert.Contains(fallback.Items, IsGuide);
        Assert.Equal(fixture.Build(state, fixture.FreshBuilder()), fallback);
        var legacy = new Canvas2DSceneContributorDescriptor(new("legacy"), "1", Canvas2DScenePanDependency.Invariant,
            Canvas2DSceneMoveGestureDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DScenePlacementDependency.Invariant);
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown, legacy.RegionResizeDependency);
    }

    [Fact]
    public async Task SpatialEdgeResizeTransportReplacesFamilyAndRecoversAfterFailureAndDprReplacement()
    {
        var fixture = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var before = fixture.Build(new());
        Assert.True((await renderer.RenderAsync(before)).Succeeded);
        var version = execution.LastFrame!.ContentVersion;
        var active = Assert.IsType<Canvas2DScene>(fixture.Reuse(before, State(Canvas2DSpatialResizeEdge.Right, 850)));
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        Assert.Equal(version, execution.LastViewportFrame!.ContentVersion);
        Assert.Single(execution.Calls, call => call == "render");
        Assert.Equal(3, execution.LastViewportFrame.PresentationItems!.Length);
        var red = Assert.IsType<Canvas2DScene>(fixture.Reuse(active, State(Canvas2DSpatialResizeEdge.Bottom, 400)));
        Assert.True((await renderer.RenderAsync(red)).Succeeded);
        Assert.Equal(2, execution.LastViewportFrame.PresentationItems!.Length);
        var retired = Assert.IsType<Canvas2DScene>(fixture.Reuse(red, new()));
        Assert.True((await renderer.RenderAsync(retired)).Succeeded);
        Assert.Empty(execution.LastViewportFrame.PresentationItems!);
        execution.RenderResult = Canvas2DRendererTestExecution.Failure("test:failure", "test");
        Assert.False((await renderer.RenderAsync(active)).Succeeded);
        Assert.False(renderer.HasAcknowledgedContent(active));
        execution.RenderResult = Canvas2DRendererTestExecution.Success();
        Assert.True((await renderer.RenderAsync(retired)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.True((await renderer.ResizeAsync(new(901, 601, 2))).Succeeded);
        Assert.False(renderer.HasAcknowledgedContent(retired));
        Assert.True((await renderer.RenderAsync(retired)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.Empty(execution.LastFrame!.PresentationItems);
    }

    [Fact]
    public void SpatialEdgeResizeRejectsUnrelatedChangesAndUnusableProvenance()
    {
        var fixture = new Fixture();
        var before = fixture.Build(new());
        var feedback = State(Canvas2DSpatialResizeEdge.Right, 850).TemporaryFeedback;
        Assert.Null(fixture.Reuse(before, new(viewport: new(2, default), temporaryFeedback: feedback)));
        Assert.Null(fixture.Reuse(before, new(activeToolId: "another-tool", temporaryFeedback: feedback)));
        Assert.Null(fixture.Reuse(before, new(selection: [new("test:visual:a")], temporaryFeedback: feedback)));
        Assert.Null(fixture.Reuse(before, new()));
        Assert.Null(fixture.Reuse(fixture.Build(new(), fixture.FreshBuilder()), State(Canvas2DSpatialResizeEdge.Right, 850)));
    }

    private static bool IsGuide(Canvas2DSceneItem item) => item.Origin.StableSourceKey?.StartsWith("spatial-resize:", StringComparison.Ordinal) == true;
    private static EditorStateSnapshot State(Canvas2DSpatialResizeEdge edge, double extent,
        Canvas2DSpatialResizePhase phase = Canvas2DSpatialResizePhase.Drag, ViewportSnapshot? viewport = null)
    {
        var region = new Canvas2DSpatialRegionId("test:region");
        var target = new Canvas2DSpatialResizeTarget(new("test:profile"), region,
            edge == Canvas2DSpatialResizeEdge.Bottom ? region : null, edge, new("test:border"),
            new RectD(40, 40, 800, 800), 800, new(520, 10000, []), [region], new RectD(40, 40, 800, 1600));
        return new(viewport: viewport, temporaryFeedback: [new EditorFeedbackSnapshot("resize", new Canvas2DSpatialResizeFeedback(target, phase, extent))]);
    }

    private sealed class Fixture(Canvas2DSceneTransientDependency dependency = Canvas2DSceneTransientDependency.Invariant)
    {
        internal Canvas2DSceneTestData Inputs { get; } = Canvas2DSceneTestData.CreateWithNodeLabels();
        internal Counter Contributor { get; } = new();
        private Canvas2DSceneBuilder? _builder;
        private DocumentSnapshot? _document;
        internal DocumentSnapshot Document => _document ??= EditingSessionTestHarness.CreateDocument(Inputs).CaptureSnapshot();
        internal Canvas2DSceneBuilder Builder => _builder ??= CreateBuilder(Contributor);
        internal Canvas2DSceneBuilder FreshBuilder() => CreateBuilder(new());
        private Canvas2DSceneBuilder CreateBuilder(Counter counter) => new(contributors:
            [new(new(new("test:resize-invariance"), "1", Canvas2DScenePanDependency.Invariant,
                Canvas2DSceneMoveGestureDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
                Canvas2DSceneTransientDependency.Invariant, Canvas2DScenePlacementDependency.Invariant, dependency), counter)]);
        internal Canvas2DScene Build(EditorStateSnapshot state, Canvas2DSceneBuilder? builder = null) =>
            Assert.IsType<Canvas2DScene>((builder ?? Builder).Build(Document, Document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                Inputs.Graph, Inputs.Layout, Inputs.Routing, Document.VisualModel, state).Scene);
        internal Canvas2DScene? Reuse(Canvas2DScene scene, EditorStateSnapshot state) => Builder.TryReuseForSpatialResize(scene,
            Document, Document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
            Inputs.Graph, Inputs.Layout, Inputs.Routing, state, default);
    }

    private sealed class Counter : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution());
        }
    }
}
