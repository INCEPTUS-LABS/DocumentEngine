using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSceneTests
{
    [Fact]
    public void SceneIsImmutableTransientRenderingPlanWithCanonicalItems()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);

        Assert.All(
            typeof(Canvas2DScene).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.IsType<ImmutableArray<Canvas2DSceneItem>>(scene.Items);
        Assert.Equal(
            scene.Items.OrderBy(static item => item.Layer)
                .ThenBy(static item => item.ZIndex)
                .ThenBy(static item => item.Id.Value, StringComparer.Ordinal),
            scene.Items);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Canvas2DSceneItem>)scene.Items).Add(scene.Items[0]));
        Assert.DoesNotContain(typeof(Canvas2DScene).GetProperties(), property =>
            property.PropertyType.FullName is
                "Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot" or
                "Inceptus.DocumentEngine.Runtime.Documents.Document");
    }

    [Fact]
    public void SceneStructuralEqualityIncludesEveryObservableDependency()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var first = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
        var equivalent = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
        var configured = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder(
            new Canvas2DSceneConfiguration("test:alternate", "1")).Build(
                inputs.Graph,
                inputs.Layout,
                inputs.Routing,
                inputs.VisualModel,
                inputs.EditorState).Scene);

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, configured);
    }

    [Fact]
    public void SceneDisposalIsIdempotentAndOwnsNoBrowserResources()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);

        scene.Dispose();
        scene.Dispose();

        Assert.Equal(4, scene.ItemCount);
        Assert.DoesNotContain(typeof(Canvas2DScene).GetProperties(), property =>
            IsBrowserResource(property.PropertyType));
    }

    [Fact]
    public void FailedBuildResultContainsNoPartialSceneAndImmutableDiagnostics()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var foreignVisual = new Inceptus.DocumentEngine.Contracts.Visuals.VisualModelSnapshot(
            new Inceptus.DocumentEngine.Contracts.Primitives.DocumentId("test:foreign"),
            inputs.Graph.SourceRevision,
            inputs.VisualModel.VisualStates);

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            foreignVisual,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Equal(Canvas2DSceneBuildStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)result.Diagnostics).Add(result.Diagnostics[0]));
    }

    private static bool IsBrowserResource(Type type) =>
        type.Name is "HTMLCanvasElement" or "CanvasRenderingContext2D" or "Path2D" or
            "ImageBitmap" or "CanvasGradient" or "CanvasPattern" ||
        type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith(
            "System.Runtime.InteropServices.JavaScript",
            StringComparison.Ordinal) == true;
}
