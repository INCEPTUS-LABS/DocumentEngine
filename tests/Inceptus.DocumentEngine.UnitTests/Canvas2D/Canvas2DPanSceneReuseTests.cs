using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DPanSceneReuseTests
{
    [Fact]
    public void LegacyDescriptorIsConservativeAndDependencyParticipatesInIdentity()
    {
        var id = new Canvas2DSceneContributorId("test:pan:contributor");
        var legacy = new Canvas2DSceneContributorDescriptor(id, "1");
        Assert.Equal(Canvas2DScenePanDependency.Unknown, legacy.PanDependency);
        Assert.Equal(legacy, new Canvas2DSceneContributorDescriptor(id, "1", Canvas2DScenePanDependency.Unknown));
        Assert.NotEqual(legacy, new Canvas2DSceneContributorDescriptor(id, "1", Canvas2DScenePanDependency.Invariant));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSceneContributorDescriptor(id, "1", (Canvas2DScenePanDependency)99));
    }

    [Theory]
    [InlineData(Canvas2DScenePanDependency.Unknown, false)]
    [InlineData(Canvas2DScenePanDependency.Unknown, true)]
    [InlineData(Canvas2DScenePanDependency.Dependent, false)]
    [InlineData(Canvas2DScenePanDependency.Dependent, true)]
    [InlineData(Canvas2DScenePanDependency.Invariant, false)]
    [InlineData(Canvas2DScenePanDependency.Invariant, true)]
    public async Task EmptyContributorCannotBecomeEligibleThroughRegistrationOrder(
        Canvas2DScenePanDependency dependency, bool reverse)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var first = new CountingContributor();
        var second = new CountingContributor();
        var registrations = new[]
        {
            Register("test:pan:a", first, Canvas2DScenePanDependency.Invariant),
            Register("test:pan:z", second, dependency),
        };
        var builder = new Canvas2DSceneBuilder(contributors: reverse ? registrations.Reverse() : registrations);
        var original = EditingSessionTestHarness.Configuration();
        var configuration = new EditingSessionConfiguration(original.ProjectionEngine, original.LayoutEngine,
            original.LayoutAlgorithmId, original.RoutingEngine, original.RoutingAlgorithmId, builder);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var rendererLifetime = renderer;
        var pipeline = new EditingSessionPipeline(configuration, renderer);
        var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, document.SemanticModel.RootScopeId);
        var before = await pipeline.RebuildSceneAsync(document, artifacts, EditorStateSnapshot.Empty,
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        var scene = Assert.IsType<Canvas2DScene>(before.Scene);
        var calls = execution.Calls.Count;
        var after = await pipeline.RebuildSceneForPanAsync(scene, document, artifacts,
            new EditorStateSnapshot(viewport: new ViewportSnapshot(1, new VectorD(17, -9))),
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);

        Assert.Equal(EditingSessionPipelineStatus.Succeeded, after.Status);
        Assert.Equal(dependency == Canvas2DScenePanDependency.Invariant, after.ReusedPanContent);
        Assert.Equal(after.ReusedPanContent ? 1 : 2, first.Calls);
        Assert.Equal(first.Calls, second.Calls);
        Assert.Same(artifacts, after.Artifacts);
        Assert.Equal(calls, execution.Calls.Count); // Warm text metrics and no render at this stage.
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.True((await renderer.RenderAsync(after.Scene!)).Succeeded);
        Assert.Equal(after.ReusedPanContent ? "renderViewport" : "render", execution.Calls[^1]);
        if (after.ReusedPanContent)
        {
            Assert.True(scene.Items == after.Scene!.Items);
            Assert.All(scene.Items, item => Assert.Same(item, after.Scene.Items.Single(next => next.Id == item.Id)));
        }
        else
        {
            Assert.NotSame(scene.Items[0], after.Scene!.Items[0]);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0.8d)]
    [InlineData(1.75d)]
    public void GuidesCrossOriginWithoutDuplicatingOrRebuildingProcessItems(double zoom)
    {
        var inputs = Canvas2DSceneTestData.Create().WithCrossingConnector();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var builder = new Canvas2DSceneBuilder();
        var editor = EditorAt(new VectorD(-30, -20), zoom);
        var scene = Build(builder, inputs, document, editor);
        var canonical = scene.Items.Where(item => !IsGuide(item)).ToDictionary(item => item.Id);
        Assert.Contains(canonical.Values, item => item.Id.Value.Contains("line-jump", StringComparison.Ordinal));
        foreach (var pan in new[] { new VectorD(20, 30), new VectorD(-25, 40), new VectorD(45, -10),
                     new VectorD(-50, -60), new VectorD(900, 800), new VectorD(5, 6), new VectorD(-10, -20) })
        {
            var viewport = new ViewportSnapshot(zoom, pan);
            editor = new EditorStateSnapshot(viewport: new ViewportSnapshot(zoom, pan,
                Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(viewport,
                    Canvas2DSceneBuilder.CalculateCanvasCssSurface(scene.Viewport))));
            var next = Assert.IsType<Canvas2DScene>(Reuse(builder, inputs, document, scene, editor));
            var rebuilt = Build(builder, inputs, document, editor);
            Assert.Equal(rebuilt, next);
            Assert.All(next.Items.Where(item => !IsGuide(item)), item => Assert.Same(canonical[item.Id], item));
            var guides = next.BoundaryGuides.Items;
            Assert.InRange(guides.Length, 0, 2);
            Assert.Equal(guides.Length, guides.Select(item => item.Id).Distinct().Count());
            Assert.All(guides, guide =>
            {
                Assert.Equal(Canvas2DHitTestMode.None, guide.HitTestPolicy.Mode);
                Assert.Equal(1d / zoom, guide.Style.StrokeWidth);
                Assert.All(guide.Geometry.Points, point => Assert.True(point.X >= 0 && point.Y >= 0));
                var point = guide.Geometry.Points[0];
                Assert.NotEqual(guide.Id, new Canvas2DSceneHitTestService().HitTest(next, point)?.SceneObjectId);
            });
            Assert.True(scene.Items == next.Items);
            Assert.Equal(pan.X, next.ViewportTransform.TransformPoint(default).X);
            scene = next;
        }
    }

    [Theory]
    [InlineData("document")]
    [InlineData("graph")]
    [InlineData("layout")]
    [InlineData("routing")]
    [InlineData("scope")]
    [InlineData("profile")]
    [InlineData("collapse")]
    [InlineData("builder")]
    [InlineData("zoom")]
    [InlineData("resize")]
    [InlineData("selection")]
    [InlineData("hover")]
    [InlineData("tool")]
    [InlineData("feedback")]
    public void ChangedAuthorityOrEditorDependencyPreventsReuse(string change)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var builder = new Canvas2DSceneBuilder();
        var scene = Build(builder, inputs, document, EditorAt(default));
        var pan = new VectorD(10, 20);
        var viewport = EditorAt(pan).Viewport;
        var editor = new EditorStateSnapshot(viewport: viewport);
        var graph = inputs.Graph;
        var layout = inputs.Layout;
        var routing = inputs.Routing;
        var scope = document.SemanticModel.RootScopeId;
        var profile = ModelProfileViewStateSnapshot.Empty;
        var collapse = ModelProfileElementViewStateSnapshot.Empty;
        switch (change)
        {
            case "document": document = new DocumentSnapshot(document.SemanticModel, document.VisualModel, document.Metadata); break;
            case "graph": graph = new ProjectedGraph(graph.DocumentId, graph.SourceRevision, graph.Nodes, graph.Edges, graph.Groups, graph.Ports, graph.Labels); break;
            case "layout": layout = new LayoutResult(layout.DocumentId, layout.SourceRevision, layout.AlgorithmId, layout.Computation); break;
            case "routing": routing = new RoutingResult(routing.DocumentId, routing.SourceRevision, routing.LayoutAlgorithmId, routing.RoutingAlgorithmId, routing.Computation); break;
            case "scope": scope = new DocumentScopeId("test:pan:other-scope"); break;
            case "profile": profile = new ModelProfileViewStateSnapshot(); break;
            case "collapse": collapse = new ModelProfileElementViewStateSnapshot(); break;
            case "builder": builder = new Canvas2DSceneBuilder(); break;
            case "zoom": editor = EditorAt(pan, 2); break;
            case "resize": editor = new EditorStateSnapshot(viewport: new ViewportSnapshot(1, pan, new RectD(-10, -20, 900, 450))); break;
            case "selection": editor = new EditorStateSnapshot(selection: [inputs.VisualModel.VisualStates[0].Id], viewport: viewport); break;
            case "hover": editor = new EditorStateSnapshot(hoveredObjectId: scene.Items[0].Id, viewport: viewport); break;
            case "tool": editor = new EditorStateSnapshot(activeToolId: "test:pan:placement", viewport: viewport); break;
            case "feedback": editor = new EditorStateSnapshot(viewport: viewport, temporaryFeedback: [new EditorFeedbackSnapshot("preview", "placement", new RectD(1, 2, 3, 4))]); break;
        }

        Assert.Null(builder.TryReuseForPan(scene, document, scope, profile, collapse, graph, layout, routing, editor));
    }

    private static EditorStateSnapshot EditorAt(VectorD pan, double zoom = 1)
    {
        var viewport = new ViewportSnapshot(zoom, pan);
        return new(viewport: new ViewportSnapshot(zoom, pan,
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(viewport, new Canvas2DSurfaceSize(800, 450, 1))));
    }

    private static Canvas2DScene Build(Canvas2DSceneBuilder builder, Canvas2DSceneTestData inputs,
        DocumentSnapshot document, EditorStateSnapshot editor) => Assert.IsType<Canvas2DScene>(builder.Build(
            document, document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing,
            document.VisualModel, editor).Scene);

    private static Canvas2DScene? Reuse(Canvas2DSceneBuilder builder, Canvas2DSceneTestData inputs,
        DocumentSnapshot document, Canvas2DScene scene, EditorStateSnapshot editor) => builder.TryReuseForPan(
            scene, document, document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing, editor);

    private static bool IsGuide(Canvas2DSceneItem item) =>
        item.Origin.StableSourceKey?.StartsWith("document-boundary:", StringComparison.Ordinal) == true;

    private static Canvas2DSceneContributorRegistration Register(string id, CountingContributor contributor,
        Canvas2DScenePanDependency dependency) => new(new(new(id), "1", dependency), contributor);

    private sealed class CountingContributor : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution());
        }
    }
}
