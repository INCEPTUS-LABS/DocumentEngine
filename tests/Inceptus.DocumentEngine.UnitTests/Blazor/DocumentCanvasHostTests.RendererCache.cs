using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementInstallsIndependentContentBeforeSubsequentPanCanUseCache(bool newDiagram)
    {
        var first = new RecordingRenderExecution();
        var replacement = new RecordingRenderExecution();
        await using var host = CreateHost(first,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            replacementRendererFactory: () => CreateRenderer(replacement));
        await host.InitializeAsync("active", "standby", "container");
        var previous = Session(host);
        Assert.True((await previous.PanViewportAsync(new VectorD(-500, -500))).Succeeded);
        Assert.True((await previous.PanViewportAsync(new VectorD(-10, -10))).Succeeded);
        Assert.True(first.ViewportRenderCount > 0);
        if (newDiagram)
        {
            Assert.True((await host.NewDiagramAsync()).Succeeded);
        }
        else
        {
            Assert.True((await host.ImportNativeDocumentAsync(NativeDocumentSerializer.Export(
                CreateImportedDocument("test:a123:import", 31)).AsMemory())).Succeeded);
        }
        var current = Session(host);
        Assert.NotSame(previous, current);
        Assert.True(replacement.FullUploadCount > 0);
        Assert.Equal(current.CaptureState().CurrentScene!.Items.Select(item => item.Id.Value),
            replacement.LastContent!.Items.Select(item => item.Id));
        Assert.Equal(1, first.DisposeCount);
        Assert.True((await current.PanViewportAsync(new VectorD(-500, -500))).Succeeded);
        var full = replacement.FullUploadCount;
        var viewport = replacement.ViewportRenderCount;
        Assert.True((await current.PanViewportAsync(new VectorD(-10, -10))).Succeeded);
        Assert.Equal(full, replacement.FullUploadCount);
        Assert.Equal(viewport + 1, replacement.ViewportRenderCount);
    }
}
