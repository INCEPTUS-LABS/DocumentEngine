using System.Text.Json;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DBoundaryGuidePresentationTests
{
    [Theory]
    [InlineData(0.25)]
    [InlineData(1)]
    [InlineData(1.1)]
    [InlineData(4)]
    public async Task BoundaryTransitionsAndAlongEdgePanRetainContentAndFastPath(double zoom)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var builder = new Canvas2DSceneBuilder();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        EditorStateSnapshot Editor(double x, double y) => new(viewport:
            Canvas2DViewportNormalizer.Normalize(new ViewportSnapshot(zoom, new VectorD(x, y)),
                new Canvas2DSurfaceSize(800, 450, 1.25)));
        var scene = Assert.IsType<Canvas2DScene>(builder.Build(document,
            document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing,
            document.VisualModel, Editor(-100, -100)).Scene);
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        var content = execution.LastFrame!;
        var stableItems = scene.Items;
        var surfaces = new[]
        {
            (-120d, -100d, 0), (-120d, 0d, 1), (-160d, 0d, 1), (0d, 0d, 2),
            (0d, -50d, 1), (0d, -100d, 1), (-20d, -100d, 0), (-20d, 0d, 1),
        };
        foreach (var (x, y, count) in surfaces)
        {
            // Follow the session Pan admission's authoritative observed-surface conversion.
            var editor = new EditorStateSnapshot(viewport: Canvas2DViewportNormalizer.Normalize(
                new ViewportSnapshot(zoom, new VectorD(x, y)),
                Canvas2DSceneBuilder.CalculateCanvasCssSurface(scene.Viewport)));
            Assert.True(builder.CanDeferPanPresentation(scene, document, document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                inputs.Graph, inputs.Layout, inputs.Routing, editor));
            var next = Assert.IsType<Canvas2DScene>(builder.TryReuseForPan(scene, document,
                document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing, editor));
            Assert.Equal(stableItems, next.Items);
            Assert.Equal(count, next.BoundaryGuides.Items.Length);
            Assert.True((await renderer.RenderAsync(next)).Succeeded);
            Assert.Same(content, execution.LastFrame);
            var frame = execution.LastViewportFrame!;
            Assert.Equal(content.ContentVersion, frame.ContentVersion);
            Assert.Equal(count, frame.ViewportLines!.Length);
            Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(frame).Length, 100, 1000);
            Assert.All(next.BoundaryGuides.Items, guide =>
            {
                Assert.Equal(Canvas2DSceneLayer.Background, guide.Layer);
                Assert.Equal(0, guide.ZIndex);
                Assert.Equal(Canvas2DSceneOriginCategory.EditorState, guide.Origin.Categories);
                Assert.Equal(Canvas2DHitTestMode.None, guide.HitTestPolicy.Mode);
                Assert.Equal("#94a3b8", guide.Style.Stroke);
                Assert.Equal(1 / zoom, guide.Style.StrokeWidth);
                Assert.Equal(new[] { 4 / zoom, 4 / zoom }, guide.Style.DashPattern);
                Assert.Equal(0.8, guide.Style.Opacity);
                Assert.DoesNotContain(next.Items, item => item.Id == guide.Id);
                Assert.NotEqual(guide.Id,
                    new Canvas2DSceneHitTestService().HitTest(next, guide.Geometry.Points[0])?.SceneObjectId);
            });
            // A full recomposition is a reference for values, ordering and bounded insertion.
            var full = Assert.IsType<Canvas2DScene>(builder.Build(document,
                document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing,
                document.VisualModel, editor).Scene);
            Assert.Equal(full, next);
            Assert.Equal(full.GetHashCode(), next.GetHashCode());
            scene = next;
        }
        Assert.Single(execution.Calls, call => call == "render");
        Assert.Equal(surfaces.Length, execution.Calls.Count(call => call == "renderViewport"));
    }
}
