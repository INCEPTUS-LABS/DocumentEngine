using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Canvas2D.Export;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class GraphicalExportTests
{
    [Fact]
    public void GraphicalExportFiltersOriginNotDecorationLayerAndIgnoresViewport()
    {
        var capture = Capture(
            Item("far", Canvas2DSceneGeometry.Rectangle(new(4000, -2000, 100, 70))),
            Item("marker", Canvas2DSceneGeometry.Path([new(0, 0), new(8, 8)]), Canvas2DSceneLayer.Decoration),
            Item("selection", Canvas2DSceneGeometry.Rectangle(new(-10000, -10000, 1, 1)), origin: Canvas2DSceneOriginCategory.EditorState));
        var prepared = GraphicalExportPreparer.Prepare(capture).Scene!;
        Assert.Equal(["far", "marker"], prepared.Items.Select(i => i.Id.Value));
        Assert.True(prepared.Bounds.Contains(new PointD(4100, -2000)));
        Assert.True(prepared.Bounds.Width < 4200);
        Assert.Equal(24, prepared.Options.Margin);
        Assert.Equal(2, prepared.Options.RasterScale);
    }

    [Fact]
    public void GraphicalExportBoundsIncludeTransformedStrokeMiterAndDocumentClip()
    {
        var item = Item("box", Canvas2DSceneGeometry.Rectangle(new(0, 0, 100, 50)),
            transform: new(2, 0, 0, 3, 10, 20), clip: new(0, 0, 160, 150));
        var prepared = GraphicalExportPreparer.Prepare(Capture(item), new(0)).Scene!;
        Assert.Equal(new RectD(8, 17, 152, 133), prepared.Bounds);
        var path = Item("arrow", Canvas2DSceneGeometry.Path([new(0, 0), new(100, 0), new(0, 30)], true));
        var pathScene = GraphicalExportPreparer.Prepare(Capture(path), new(0)).Scene!;
        Assert.True(pathScene.Bounds.Right > 105); // Acute miter extends beyond endpoints.
    }

    [Fact]
    public void GraphicalExportSvgContainsNativeOrderedSafePrimitivesAndStyles()
    {
        const string text = "A & B <script>alert('x')</script> \"quoted\"";
        var capture = Capture(
            Item("text", Canvas2DSceneGeometry.Text(new(0, 0, 200, 20), text, new(100, 10), Canvas2DTextAlignment.Center, Canvas2DTextBaseline.Middle), Canvas2DSceneLayer.Label),
            Item("shape", Canvas2DSceneGeometry.Ellipse(new(10, 20, 40, 40)), clip: new(5, 6, 70, 80)),
            Item("flow", Canvas2DSceneGeometry.Path([new(10, 20), new(30, 20), new(30, 80)]), Canvas2DSceneLayer.Connector));
        var result = SvgGraphicalExporter.Export(GraphicalExportPreparer.Prepare(capture).Scene!);
        Assert.True(result.Succeeded);
        Assert.Equal("diagram.svg", result.Artifact!.FileName);
        Assert.Equal("image/svg+xml", result.Artifact.ContentType);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(result.Artifact.Content.AsSpan()));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(ns + "svg", xml.Root!.Name);
        Assert.Empty(xml.Descendants(ns + "script"));
        Assert.Empty(xml.Descendants(ns + "image"));
        Assert.Equal(text, Assert.Single(xml.Descendants(ns + "text")).Value);
        Assert.Equal("middle", xml.Descendants(ns + "text").Single().Attribute("text-anchor")!.Value);
        Assert.Equal("userSpaceOnUse", Assert.Single(xml.Descendants(ns + "clipPath")).Attribute("clipPathUnits")!.Value);
        Assert.Contains(xml.Descendants(ns + "g"), g => g.Attribute("stroke-dasharray")?.Value == "4 2");
        Assert.Contains(xml.Descendants(ns + "g"), g => g.Attribute("fill-opacity")?.Value == "0.7");
        Assert.Equal(["ellipse", "path", "text"], xml.Descendants().Where(e => e.Name.LocalName is "ellipse" or "path" or "text").Select(e => e.Name.LocalName));
    }

    [Theory]
    [InlineData("url(https://example.com/image)")]
    [InlineData("var(--fill)")]
    [InlineData("red;stroke:url(x)")]
    public void GraphicalExportRejectsExternalPaints(string fill)
    {
        var item = new Canvas2DSceneItem(new("bad"), Canvas2DSceneLayer.Content, 0,
            Canvas2DSceneGeometry.Rectangle(new(0, 0, 1, 1)), new(Canvas2DSceneOriginCategory.Configuration, stableSourceKey: "bad"), style: new(fill: fill));
        Assert.False(GraphicalExportPreparer.Prepare(Capture(item)).Succeeded);
    }

    [Fact]
    public void GraphicalExportRejectsMissingOrExecutableImagesAndEmbedsRaster()
    {
        var scene = GraphicalExportPreparer.Prepare(Capture(Item("image", Canvas2DSceneGeometry.Image(new(0, 0, 20, 20), "logo")))).Scene!;
        Assert.False(SvgGraphicalExporter.Export(scene).Succeeded);
        var resources = new GraphicalExportResources { Images = ImmutableDictionary<string, string>.Empty.Add("logo", "data:image/svg+xml;base64,PHN2Zz48c2NyaXB0Lz48L3N2Zz4=") };
        Assert.False(SvgGraphicalExporter.Export(scene, resources).Succeeded);
        resources = resources with { Images = ImmutableDictionary<string, string>.Empty.Add("logo", "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX9sAAAAASUVORK5CYII=") };
        Assert.True(SvgGraphicalExporter.Export(scene, resources).Succeeded);
    }

    [Fact]
    public async Task GraphicalExportEmptyInvalidOptionsOversizeAndCancellationAreBounded()
    {
        var capture = Capture();
        var prepared = GraphicalExportPreparer.Prepare(capture).Scene!;
        Assert.Equal(new RectD(-24, -24, 49, 49), prepared.Bounds);
        Assert.True(SvgGraphicalExporter.Export(prepared).Succeeded);
        Assert.False(GraphicalExportPreparer.Prepare(capture, new(-1)).Succeeded);
        Assert.False(GraphicalExportPreparer.Prepare(capture, new(RasterScale: double.NaN)).Succeeded);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(GraphicalExportStatus.Cancelled, SvgGraphicalExporter.Export(prepared, cancellationToken: cancelled.Token).Status);
        var exporter = new Canvas2DGraphicalExporter(new NoBrowserRuntime());
        Assert.Equal(GraphicalExportStatus.Cancelled, (await exporter.ExportAsync(capture, GraphicalExportFormat.Png, cancellationToken: cancelled.Token)).Status);
        var huge = Capture(Item("huge", Canvas2DSceneGeometry.Rectangle(new(0, 0, 100000, 100000))));
        Assert.Equal("GRAPHICAL_EXPORT_SIZE", Assert.Single((await exporter.ExportAsync(huge, GraphicalExportFormat.Png)).Diagnostics).Code);
    }

    [Fact]
    public void GraphicalExportChecksCoherenceAndSupportsDocumentPresentation()
    {
        var capture = Capture(Item("body", Canvas2DSceneGeometry.Rectangle(new(0, 0, 30, 30))));
        Assert.False(GraphicalExportPreparer.Prepare(capture with { PresentationContext = new DocumentPresentationContext(new("foreign")) }).Succeeded);
        var documentContext = new DocumentPresentationContext(new("document-view"));
        var scene = capture.Scene;
        var documentScene = new Canvas2DScene(scene.DocumentId, scene.SourceRevision, scene.LayoutAlgorithmId, scene.RoutingAlgorithmId,
            scene.Configuration, scene.Contributors, scene.Viewport, scene.ViewportTransform, null, null, PropertyMap.Empty, PropertyMap.Empty,
            scene.Items, presentationContext: documentContext);
        Assert.True(GraphicalExportPreparer.Prepare(capture with { Scene = documentScene, PresentationContext = documentContext }).Succeeded);
    }

    private static Canvas2DSceneItem Item(string id, Canvas2DSceneGeometry geometry, Canvas2DSceneLayer layer = Canvas2DSceneLayer.Content,
        Canvas2DSceneOriginCategory origin = Canvas2DSceneOriginCategory.Configuration, Matrix2D? transform = null, RectD? clip = null) =>
        new(new(id), layer, 0, geometry, new(origin, stableSourceKey: id), transform, clip, new(fill: "white", stroke: "black", strokeWidth: 2,
            dashPattern: [4, 2], opacity: 0.7, fontFamily: "DejaVu Sans", fontSize: 14));

    private static EditingSessionPresentationCapture Capture(params Canvas2DSceneItem[] items)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scene = new Canvas2DScene(inputs.Graph.DocumentId, inputs.Graph.SourceRevision, inputs.Layout.AlgorithmId,
            inputs.Routing.RoutingAlgorithmId, new("export-test", "1"), [], inputs.EditorState.Viewport, Matrix2D.CreateScale(0.25, 0.25),
            null, null, PropertyMap.Empty, PropertyMap.Empty, items);
        var document = new DocumentSnapshot(new SemanticModelSnapshot(scene.DocumentId, scene.SourceRevision), inputs.VisualModel,
            new DocumentMetadataSnapshot(scene.DocumentId, scene.SourceRevision));
        return new(document, scene.PresentationContext, default, inputs.Graph, inputs.Layout, inputs.Routing, scene);
    }

    private sealed class NoBrowserRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Browser must not be accessed.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("Browser must not be accessed.");
    }
}
