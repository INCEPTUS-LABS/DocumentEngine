using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.UnitTests.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandardHostAsyncImportPreparesGeometryAndRoundTripsWithoutAcknowledgingNativeSave(bool collaboration)
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400, 900, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("standard-active", "standard-standby", "standard-container");
        await using var measurement = new Canvas2DRenderer(new RecordingRenderExecution(), DemoBpmnImportPreparation.RendererConfiguration);
        Assert.True((await measurement.InitializeAsync("measurement", new(1, 1, 1))).Succeeded);
        var preparer = DemoBpmnImportPreparation.Create(measurement);
        var operations = new DemoDocumentOperations(host);
        var xml = BpmnStandardConverterTests.Xml(collaboration ? BpmnStandardConverterTests.CollaborationXml : BpmnStandardConverterTests.DiExample);
        var imported = await operations.ImportStandardDocumentAsync(xml, preparer);
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        var document = host.GetDocument().Snapshot!;
        Assert.NotNull(document.VisualModel.RoutingScopes);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        var route = Assert.Single(document.VisualModel.EnumerateConnectorRoutingRecords());
        if (!collaboration)
        {
            Assert.Equal(ConnectorRoutingType.Manual, route.RoutingType);
            Assert.Equal<PointD>([new(80, 60), new(140, 60), new(140, 80), new(200, 80)], route.Path);
        }
        var saveStatus = host.NativeSaveStatus;
        var exported = await operations.ExportStandardDocumentAsync();
        Assert.True(exported.Succeeded);
        Assert.Equal("document.xml", exported.Artifact!.FileName);
        Assert.Equal("application/xml;charset=utf-8", exported.Artifact.ContentType);
        Assert.Null(exported.SaveCheckpoint);
        Assert.Equal(saveStatus!.IsDirty, host.NativeSaveStatus!.IsDirty);
        Assert.Equal(saveStatus.SavedCheckpoint, host.NativeSaveStatus.SavedCheckpoint);
        Assert.Equal(saveStatus.CurrentCheckpoint.DocumentId, host.NativeSaveStatus.CurrentCheckpoint.DocumentId);
        Assert.Equal(saveStatus.CurrentCheckpoint.Revision, host.NativeSaveStatus.CurrentCheckpoint.Revision);
        Assert.Contains(operations.ConversionLog, entry => entry.Disposition == BpmnConversionDisposition.Omitted);
        Assert.True((await operations.ImportStandardDocumentAsync(Encoding.UTF8.GetString(exported.Payload.AsSpan()), preparer)).Succeeded);
        var roundTripRoute = Assert.Single(host.GetDocument().Snapshot!.VisualModel.EnumerateConnectorRoutingRecords());
        // A2 Message Flow endpoint anchors are resolved again against the imported envelopes.
        // Standard DI retains its authored interior waypoints, not native anchor metadata.
        Assert.Equal<PointD>(collaboration ? route.Path.Skip(1).SkipLast(1) : route.Path,
            collaboration ? roundTripRoute.Path.Skip(1).SkipLast(1) : roundTripRoute.Path);
        Assert.NotNull(await operations.ValidateAsync());
        var native = await operations.ExportNativeDocumentAsync();
        Assert.True(native.Succeeded);
        Assert.True((await operations.ImportNativeDocumentAsync(native.Payload.AsMemory())).Succeeded);
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("<definitions xmlns='http://www.omg.org/spec/BPMN/20100524/MODEL' targetNamespace='urn:test'><process id='p'><sequenceFlow id='f' sourceRef='missing' targetRef='missing'/></process></definitions>")]
    public async Task StandardHostBlockingImportPreservesDocumentAndHistory(string xml)
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("standard-failure", "standard-container");
        var source = host.GetDocument().Snapshot;
        var history = Session(host).CaptureState().HistoryStatus;
        var operations = new DemoDocumentOperations(host);
        var result = await operations.ImportStandardDocumentAsync(xml, BpmnStandardConverterTests.Preparer());
        Assert.False(result.Succeeded);
        Assert.Empty(operations.ConversionLog);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity.Error);
        Assert.Same(source, host.GetDocument().Snapshot);
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandardHostExportDownloadsBpmnAndShowsDismissibleConversionFeedback(bool failDownload)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync());
        await host.InitializeAsync("standard-download", "standard-container");
        var document = host.GetDocument().Snapshot;
        var activator = new DemoPublishComponentActivator(host);
        var browser = new StandardDownloadRuntime(failDownload);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(browser).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(BpmnDemoHost.Editor)] = host })));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePublishComponentAsync(activator.Component, "ExportStandardDocumentAsync");
            await activator.Component.RefreshAsync();
        });
        Assert.Equal(1, browser.Downloads);
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains(failDownload ? "BPMN export failed" : "BPMN export completed", markup, StringComparison.Ordinal);
        Assert.Contains("data-conversion-disposition=\"Omitted\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-disposition=\"Omitted\"", markup, StringComparison.Ordinal);
        var losses = BpmnStandardConverter.Export(document!).ConversionLog;
        var repeated = losses.GroupBy(entry => (entry.Disposition, entry.Code, entry.Message)).Where(group => group.Count() > 1).ToArray();
        Assert.NotEmpty(repeated);
        foreach (var group in repeated)
        {
            Assert.Single(Regex.Matches(markup, Regex.Escape(HtmlEncoder.Default.Encode(group.Key.Message)), RegexOptions.CultureInvariant));
            Assert.Contains($"{group.Count()} affected entries", markup, StringComparison.Ordinal);
            foreach (var entry in group)
            {
                Assert.Contains(HtmlEncoder.Default.Encode(entry.SourceIdentity!), markup, StringComparison.Ordinal);
                Assert.Contains(HtmlEncoder.Default.Encode(entry.Property!), markup, StringComparison.Ordinal);
            }
        }
        Assert.NotEmpty(Regex.Matches(markup, "<details[^>]*class=\"conversion-loss-details\"[^>]*>", RegexOptions.CultureInvariant));
        Assert.DoesNotContain("<details open", markup, StringComparison.Ordinal);
        Assert.DoesNotContain(" open>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-conversion-disposition=\"Converted\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-conversion-disposition=\"Warning\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-conversion-disposition=\"Error\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-conversion-code=\"BPMN_DOWNLOAD_FAILED\"", markup, StringComparison.Ordinal);
        if (failDownload)
            Assert.Contains("data-operation-code=\"BPMN_DOWNLOAD_FAILED\"", markup, StringComparison.Ordinal);
        Assert.Contains("Import BPMN 2.0.2", markup, StringComparison.Ordinal);
        Assert.Contains("Export Inceptus JSON", markup, StringComparison.Ordinal);
        Assert.Same(document, host.GetDocument().Snapshot);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InvokePublishComponent(activator.Component, "DismissOperationFeedback");
            await activator.Component.RefreshAsync();
        });
        Assert.DoesNotContain("operation-feedback-panel", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
    }

    private sealed class StandardDownloadRuntime(bool fail) : IJSRuntime, IJSObjectReference
    {
        internal int Downloads { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            Assert.Equal("downloadFile", identifier);
            Assert.Equal(DemoDocumentOperations.StandardDocumentContentType, args![1]);
            Assert.Equal("document.xml", args[2]);
            BpmnStandardConverterTests.Success(BpmnStandardConverter.Import(Encoding.UTF8.GetString(Assert.IsType<byte[]>(args[0]))));
            Downloads++;
            if (fail) throw new JSException("Download failed");
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
