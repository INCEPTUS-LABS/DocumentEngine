using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("en", "Initial View", "Use current Editor view", "Fit entire diagram")]
    [InlineData("pl", "Widok początkowy", "Użyj bieżącego widoku edytora", "Dopasuj cały diagram")]
    [InlineData("fr", "Vue initiale", "Utiliser la vue actuelle de l’éditeur", "Ajuster le diagramme entier")]
    [InlineData("de", "Startansicht", "Aktuelle Editoransicht verwenden", "Gesamtes Diagramm einpassen")]
    [InlineData("es", "Vista inicial", "Usar la vista actual del editor", "Ajustar todo el diagrama")]
    public async Task PublishInitialViewDialogDefaultsToCurrentAndKeepsPreferencesOutOfMetadata(
        string language, string title, string currentLabel, string fitLabel)
    {
        using var culture = new ModelerCultureScope(language);
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("initial-view-ui", "container");
        await SaveDefaultPublicationAsync(Session(host));
        await Session(host).UpdateViewportAsync(new ViewportSnapshot(1.5, new(-400, -150)));
        await Session(host).WaitForIdleAsync();
        var before = host.CaptureEditorContext()!;
        var activator = new DemoPublishComponentActivator(host);
        var browser = new PublishDownloadRuntime();
        using var services = PublishComponentServices(activator, browser);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(BpmnDemoHost.Editor)] = host })));
        var modeField = typeof(BpmnDemoHost).GetField("_publicationInitialViewMode", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InitializePublishDialog(activator.Component, host);
            await activator.Component.RefreshAsync();
        });
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains(title, markup, StringComparison.Ordinal);
        Assert.Contains(currentLabel, markup, StringComparison.Ordinal);
        Assert.Contains(fitLabel, markup, StringComparison.Ordinal);
        Assert.Matches("<input[^>]*value=\"current\"[^>]*checked", markup);
        Assert.DoesNotMatch("<input[^>]*value=\"fit\"[^>]*checked", markup);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            modeField.SetValue(activator.Component, BpmnPublishingInitialViewMode.FitEntireDiagram);
            await InvokePublishComponentAsync(activator.Component, "SavePublicationDraftAsync");
        });
        Assert.Equal(0, browser.DownloadCount);
        foreach (var mode in new[] { BpmnPublishingInitialViewMode.CurrentEditorView, BpmnPublishingInitialViewMode.FitEntireDiagram })
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                InitializePublishDialog(activator.Component, host);
                Assert.Equal(BpmnPublishingInitialViewMode.CurrentEditorView, modeField.GetValue(activator.Component));
                modeField.SetValue(activator.Component, mode);
                await InvokePublishComponentAsync(activator.Component, "PublishPublicationDraftAsync");
            });
            using var json = JsonDocument.Parse(ReadPublishedArchive(browser.Payload.ToImmutableArray())["process.json"]);
            Assert.Equal(mode == BpmnPublishingInitialViewMode.CurrentEditorView, json.RootElement.TryGetProperty("initialView", out var view));
            if (mode == BpmnPublishingInitialViewMode.CurrentEditorView)
                Assert.Equal(before.Presentation.EditorState.Viewport.VisibleDocumentRegion!.Value.X, view.GetProperty("visibleRegion").GetProperty("x").GetDouble());
            Assert.Same(before.Document, host.CaptureEditorContext()!.Document);
            Assert.Equal(before.Presentation.HistoryStatus, Session(host).CaptureState().HistoryStatus);
        }
        Assert.Equal(2, browser.DownloadCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("document")]
    [InlineData("revision")]
    [InlineData("scope")]
    [InlineData("mode")]
    public async Task PublishInitialViewPublicApiRejectsInvalidDefinition(string invalid)
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("initial-view-api", "container");
        await SaveDefaultPublicationAsync(Session(host));
        var context = host.CaptureEditorContext()!;
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var view = new BpmnPublishingEditorView(context.Document.DocumentId, context.Document.Revision,
            context.Presentation.ActiveScopeId, new(10, 20, 400, 300));
        view = invalid switch
        {
            "missing" => null,
            "empty" => view with { VisibleRegion = default },
            "document" => view with { DocumentId = new("unrelated") },
            "revision" => view with { Revision = view.Revision.Increment() },
            "scope" => view with { ScopeId = new("unrelated") },
            _ => view,
        };
        var result = new PublishingPackageGenerator().Generate(context.Document, capture, new BpmnPublishingOptions
        { InitialViewMode = invalid == "mode" ? (BpmnPublishingInitialViewMode)99 : BpmnPublishingInitialViewMode.CurrentEditorView, EditorView = view });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "PUBLISH_INITIAL_VIEW_INVALID");
        Assert.Same(context.Document, host.CaptureEditorContext()!.Document);
        Assert.Equal(context.Presentation.HistoryStatus, Session(host).CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(double.MaxValue, 100)]
    public async Task PublishInitialViewBuilderRejectsNonFiniteOrUnusableGeometry(double x, double width)
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("initial-view-invalid", "container");
        await SaveDefaultPublicationAsync(Session(host));
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var result = new PublishedProcessPackageBuilder(new Inceptus.DocumentEngine.Bpmn.Publishing.BpmnPublishedTokenRoleClassifier())
            .Build(capture, ImmutableHashSet<SemanticElementId>.Empty, new(new(x, 0, width, 100)));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "PUBLISH_INITIAL_VIEW_INVALID");
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("missing-surface")]
    [InlineData("busy")]
    [InlineData("attachment")]
    [InlineData("scope")]
    [InlineData("viewport")]
    [InlineData("revision")]
    [InlineData("cancel")]
    public async Task PublishInitialViewCannotMixStaleEditorAndPresentation(string change)
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("initial-view-race", "container");
        await SaveDefaultPublicationAsync(Session(host));
        using var cancellation = new CancellationTokenSource();
        var probe = new PublishViewEditorProbe(host);
        probe.Context = context => change switch
        {
            "missing-context" => null,
            "missing-surface" => context! with { SurfaceSize = null },
            "busy" => context! with { CanReplaceDocument = false },
            "attachment" when probe.Captured => context! with { AttachmentVersion = context.AttachmentVersion + 1 },
            _ => context,
        };
        probe.AfterCapture = async () =>
        {
            if (change == "scope") await host.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId);
            if (change == "viewport") await Session(host).UpdateViewportAsync(new ViewportSnapshot(2, new(-120, -80)));
            if (change == "revision")
            {
                var context = host.CaptureEditorContext()!;
                await host.UpdatePublicationAsync(context.AttachmentVersion, context.Document.DocumentId, context.Document.Revision,
                    new("other-publication", "Changed", ""));
            }
            if (change == "cancel") cancellation.Cancel();
            await Session(host).WaitForIdleAsync();
        };
        var result = await new DemoDocumentOperations(probe).PublishProcessAsync(
            new BpmnPublishingOptions { InitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView }, cancellation.Token);
        Assert.False(result.Succeeded);
        Assert.Null(result.Artifact);
        Assert.True(result.Status == BpmnModelerOperationStatus.Cancelled || result.Diagnostics.Any(item =>
            item.Code is "CANVAS_PUBLISH_INITIAL_VIEW_UNAVAILABLE" or "CANVAS_PUBLISH_UNAVAILABLE"));
    }

    [Fact]
    public async Task PublishInitialViewAllowsOnlyItsOwnMetadataRevisionAdvance()
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("initial-view-save", "container");
        var before = host.CaptureEditorContext()!;
        var result = await host.Operations().PublishPublicationAsync(before.AttachmentVersion, before.Document.DocumentId,
            before.Document.Revision, new("current-view", "Current view", ""),
            new BpmnPublishingOptions { InitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView });
        Assert.True(result.Succeeded, PublishDiagnostics(result.Diagnostics));
        Assert.True(result.PublicationChanged);
        Assert.Equal(before.Document.Revision.Increment(), host.CaptureEditorContext()!.Document.Revision);
        Assert.Equal(before.Presentation.HistoryStatus.EntryCount + 1, Session(host).CaptureState().HistoryStatus.EntryCount);
        using var json = JsonDocument.Parse(ReadPublishedArchive(result.Payload)["process.json"]);
        Assert.True(json.RootElement.TryGetProperty("initialView", out _));
    }

    private sealed class PublishViewEditorProbe(IBpmnDocumentEditor inner) : IBpmnDocumentEditor
    {
        internal Func<BpmnEditorContext?, BpmnEditorContext?> Context { get; set; } = value => value;
        internal Func<Task>? AfterCapture { get; set; }
        internal bool Captured { get; private set; }
        public BpmnEditorContext? CaptureEditorContext() => Context(inner.CaptureEditorContext());
        public async ValueTask<EditingSessionPresentationCaptureResult?> CapturePresentationAsync(CancellationToken cancellationToken = default)
        {
            var result = await inner.CapturePresentationAsync(cancellationToken);
            Captured = true;
            if (AfterCapture is not null) await AfterCapture();
            return result;
        }
        public BpmnModelerDocumentResult GetDocument() => inner.GetDocument();
        public ValueTask<BpmnModelerDocumentResult> GetDocumentAsync(CancellationToken cancellationToken = default) => inner.GetDocumentAsync(cancellationToken);
        public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document, CancellationToken cancellationToken = default) => inner.SetDocumentAsync(document, cancellationToken);
        public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document, BpmnModelerDocumentLoadOptions options, CancellationToken cancellationToken = default) => inner.SetDocumentAsync(document, options, cancellationToken);
        public BpmnModelerNativeSaveStatus? NativeSaveStatus => inner.NativeSaveStatus;
        public BpmnModelerSaveCheckpoint? CreateSaveCheckpoint(DocumentSnapshot document) => inner.CreateSaveCheckpoint(document);
        public bool AcknowledgeNativeSave(BpmnModelerSaveCheckpoint checkpoint) => inner.AcknowledgeNativeSave(checkpoint);
        public ValueTask<BpmnModelerDocumentResult> UpdatePublicationAsync(long attachmentVersion, DocumentId documentId, DocumentRevision revision, DocumentPublicationSnapshot publication, CancellationToken cancellationToken = default) => inner.UpdatePublicationAsync(attachmentVersion, documentId, revision, publication, cancellationToken);
        public ValueTask<Canvas2DInteractionResult?> SelectVisualAsync(DocumentId documentId, DocumentRevision revision, PresentationViewContext context, VisualStateId visualStateId, CancellationToken cancellationToken = default) => inner.SelectVisualAsync(documentId, revision, context, visualStateId, cancellationToken);
    }
}
