using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private const string HostilePublishedText = "</script><script>alert(1)</script>";
    private const string PublishedDataBootstrapPrefix =
        "globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = ";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedPublishRetainsEveryReasonOutsideTheClosedPublicationDialog(
        bool multipleReasons)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(
            new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: multipleReasons ? BpmnModelerTestComposition.DemoFactory : null,
            publishPackageBuilder: multipleReasons
                ? new PublishedProcessPackageBuilder(new RejectedPublishRoleClassifier())
                : null);
        await host.InitializeAsync("publish-feedback-canvas", "publish-feedback-container");
        await SaveDefaultPublicationAsync(Session(host));
        var expected = await host.PublishProcessAsync();
        Assert.False(expected.Succeeded);
        Assert.NotEmpty(expected.Diagnostics);
        if (multipleReasons)
        {
            Assert.True(expected.Diagnostics.Length > 1, PublishDiagnostics(expected.Diagnostics));
        }
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var beforeHistory = host.CaptureState().Session!.HistoryStatus;
        var activator = new PublishComponentActivator(host);
        using var services = new ServiceCollection()
            .AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator)
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InitializePublishDialog(activator.Component, host);
            await InvokePublishComponentAsync(activator.Component, "PublishPublicationDraftAsync");
            await activator.Component.RefreshAsync();
        });
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        var text = WebUtility.HtmlDecode(markup);

        Assert.Contains("data-publication-dialog=\"closed\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"dialog\"", markup, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", markup, StringComparison.Ordinal);
        foreach (var diagnostic in expected.Diagnostics)
        {
            Assert.Contains(diagnostic.Message, text, StringComparison.Ordinal);
            if (diagnostic.SourceIdentity is { } identity)
            {
                Assert.Contains(identity, text, StringComparison.Ordinal);
            }
            if (diagnostic.Context.TryGetValue("ElementName", out var elementName) &&
                !string.IsNullOrWhiteSpace(elementName))
            {
                Assert.Contains(elementName, text, StringComparison.Ordinal);
            }
        }
        Assert.Contains("Publish failed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", markup, StringComparison.Ordinal);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(beforeHistory, host.CaptureState().Session!.HistoryStatus);
    }

    [Fact]
    public async Task FailedPublishFeedbackSurvivesDialogCancelAndDismissalIsReadOnly()
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)));
        await host.InitializeAsync("publish-dismiss-canvas", "publish-dismiss-container");
        await SaveDefaultPublicationAsync(Session(host));
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var beforeHistory = host.CaptureState().Session!.HistoryStatus;
        var activator = new PublishComponentActivator(host);
        var browser = new PublishDownloadRuntime();
        using var services = PublishComponentServices(activator, browser);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InitializePublishDialog(activator.Component, host);
            await InvokePublishComponentAsync(activator.Component, "PublishPublicationDraftAsync");
            InitializePublishDialog(activator.Component, host);
            InvokePublishComponent(activator.Component, "CancelPublicationDialog");
            await activator.Component.RefreshAsync();
        });
        var retained = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("Publish failed", retained, StringComparison.Ordinal);
        Assert.Contains("data-publication-dialog=\"closed\"", retained, StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InvokePublishComponent(activator.Component, "DismissPublishOperationError");
            await activator.Component.RefreshAsync();
        });
        var dismissed = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.DoesNotContain("Publish failed", dismissed, StringComparison.Ordinal);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(beforeHistory, host.CaptureState().Session!.HistoryStatus);
        Assert.Equal(0, browser.DownloadCount);
    }

    [Fact]
    public async Task PackageGenerationFailureIsBoundedAndDoesNotChangeTheEditor()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            publishPackageBuilder: new PublishedProcessPackageBuilder(
                new BpmnPublishedTokenRoleClassifier(), new ThrowingPublishNodeDataMapper()));
        await host.InitializeAsync("publish-package-failure", "publish-package-container");
        await SaveDefaultPublicationAsync(Session(host));
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var state = host.CaptureState().Session!;

        var result = await host.PublishProcessAsync();

        Assert.Equal(PublishedProcessHostOperationStatus.Failed, result.Status);
        Assert.Empty(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PUBLISH_PACKAGE_BUILD_FAILED");
        Assert.DoesNotContain("private implementation detail", PublishDiagnostics(result.Diagnostics),
            StringComparison.Ordinal);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(state.HistoryStatus, host.CaptureState().Session!.HistoryStatus);
        Assert.Same(state.EditorState, host.CaptureState().Session!.EditorState);
    }

    [Fact]
    public async Task IncoherentPublishCaptureIsRejectedWithoutChangingStateAndCanRetry()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-capture-failure", "publish-capture-container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var before = AttachedDocument(session).CaptureSnapshot();
        var state = session.CaptureState();
        var artifactsField = typeof(EditingSession).GetField(
            "_artifacts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var artifacts = artifactsField.GetValue(session);
        try
        {
            // Simulate the existing capture boundary lacking its retained artifacts; do not
            // alter the authoritative snapshot or add an engine-specific failure hook.
            artifactsField.SetValue(session, null);
            var result = await host.PublishProcessAsync();
            Assert.Equal(PublishedProcessHostOperationStatus.Unavailable, result.Status);
            Assert.Empty(result.Payload);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "EDITING_SESSION_PRESENTATION_CAPTURE_UNAVAILABLE" ||
                diagnostic.Code == "CANVAS_PUBLISH_UNAVAILABLE");
            Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
            Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
        }
        finally
        {
            artifactsField.SetValue(session, artifacts);
        }
        var retry = await host.PublishProcessAsync();
        Assert.True(retry.Succeeded, PublishDiagnostics(retry.Diagnostics));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishCapturePreparationFailureUsesItsOwnBoundedDiagnostic(bool throws)
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-preparation-failure", "publish-preparation-container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var before = AttachedDocument(session).CaptureSnapshot();
        var state = session.CaptureState();
        var pipelineField = typeof(EditingSession).GetField(
            "_pipeline", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var original = pipelineField.GetValue(session);
        var controlled = new ControlledEditingSessionPipeline();
        controlled.EnqueueScene((_, _, _, _) => throws
            ? ValueTask.FromException<EditingSessionPipelineResult>(
                new InvalidOperationException("private implementation detail " + HostilePublishedText))
            : ValueTask.FromResult(ControlledEditingSessionPipeline.Failure("TEST_CAPTURE_FAILURE")));
        try
        {
            // Reuse the canonical controlled pipeline to isolate the detached capture stage.
            pipelineField.SetValue(session, controlled);
            var result = await host.PublishProcessAsync();
            Assert.Equal(PublishedProcessHostOperationStatus.Failed, result.Status);
            Assert.Empty(result.Payload);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "CANVAS_PUBLISH_PREPARATION_FAILED");
            if (!throws)
            {
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TEST_CAPTURE_FAILURE");
            }
            Assert.DoesNotContain("private implementation detail", PublishDiagnostics(result.Diagnostics),
                StringComparison.Ordinal);
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Code == "CANVAS_PUBLISH_DOWNLOAD_FAILED" ||
                diagnostic.Code == "PUBLISH_PACKAGE_BUILD_FAILED");
            Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
            Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
            Assert.Same(state.EditorState, session.CaptureState().EditorState);
        }
        finally
        {
            pipelineField.SetValue(session, original);
        }
        Assert.True((await host.PublishProcessAsync()).Succeeded);
    }

    [Fact]
    public async Task CancelledPublishDoesNotSaveMetadataCreateAnArtifactOrReportFailure()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-cancel-canvas", "publish-cancel-container");
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var state = host.CaptureState();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await host.PublishPublicationAsync(state.DocumentSessionVersion,
            state.Session!.DocumentId, state.Session.DocumentRevision,
            new DocumentPublicationSnapshot("cancelled", "Cancelled draft", "Unsaved"),
            cancellation.Token);

        Assert.Equal(PublishedProcessHostOperationStatus.Cancelled, result.Status);
        Assert.False(result.PublicationChanged);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Payload);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(state.Session.HistoryStatus, host.CaptureState().Session!.HistoryStatus);
    }

    [Fact]
    public async Task PublishDownloadFailureHasABoundedMessageAndSuccessfulRetryClearsFeedback()
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-download-retry", "publish-download-container");
        await SaveDefaultPublicationAsync(Session(host));
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var history = host.CaptureState().Session!.HistoryStatus;
        var activator = new PublishComponentActivator(host);
        var browser = new PublishDownloadRuntime
        {
            Failure = new JSException("private implementation detail " + HostilePublishedText),
        };
        using var services = PublishComponentServices(activator, browser);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InitializePublishDialog(activator.Component, host);
            await InvokePublishComponentAsync(activator.Component, "PublishPublicationDraftAsync");
            await activator.Component.RefreshAsync();
        });
        var failed = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("CANVAS_PUBLISH_DOWNLOAD_FAILED", failed, StringComparison.Ordinal);
        Assert.Contains("Publish failed", failed, StringComparison.Ordinal);
        Assert.Contains("download", failed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private implementation detail", failed, StringComparison.Ordinal);
        Assert.DoesNotContain(HostilePublishedText, failed, StringComparison.Ordinal);
        Assert.Equal(1, browser.DownloadCount);
        browser.Failure = null;

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InitializePublishDialog(activator.Component, host);
            await InvokePublishComponentAsync(activator.Component, "PublishPublicationDraftAsync");
            await activator.Component.RefreshAsync();
        });
        var succeeded = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.DoesNotContain("Publish failed", succeeded, StringComparison.Ordinal);
        Assert.Contains("data-publish-error-code=\"\"", succeeded, StringComparison.Ordinal);
        Assert.Equal(2, browser.DownloadCount);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(history, host.CaptureState().Session!.HistoryStatus);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReplacementClearsPublishFeedbackAndRejectsLateBrowserFailure(
        bool import, bool delayed)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("publish-replacement", "publish-replacement-container");
        await SaveDefaultPublicationAsync(Session(host));
        var native = await host.ExportNativeDocumentAsync();
        Assert.True(native.Succeeded);
        var oldVersion = host.CaptureState().DocumentSessionVersion;
        var activator = new PublishComponentActivator(host);
        var browser = new PublishDownloadRuntime
        {
            DelayDownload = delayed,
            Failure = new JSException("retired private browser failure"),
        };
        using var services = PublishComponentServices(activator, browser);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());
        var publishing = renderer.Dispatcher.InvokeAsync(async () =>
        {
            InitializePublishDialog(activator.Component, host);
            await InvokePublishComponentAsync(activator.Component, "PublishPublicationDraftAsync");
        });
        DocumentSnapshot? replaced = null;
        try
        {
            await browser.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!delayed)
            {
                await publishing.WaitAsync(TimeSpan.FromSeconds(5));
                await renderer.Dispatcher.InvokeAsync(activator.Component.RefreshAsync);
                Assert.Contains("Publish failed",
                    await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
            }
            if (import)
            {
                Assert.True((await host.ImportNativeDocumentAsync(native.Payload.AsMemory())).Succeeded);
            }
            else
            {
                Assert.True((await host.NewDiagramAsync()).Succeeded);
            }
            replaced = AttachedDocument(Session(host)).CaptureSnapshot();
            Assert.NotEqual(oldVersion, host.CaptureState().DocumentSessionVersion);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                typeof(DocumentCanvas).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(activator.Component, host.CaptureState());
                InvokePublishComponent(activator.Component, "ReconcilePublishOperation");
                await activator.Component.RefreshAsync();
            });
        }
        finally
        {
            browser.ReleaseDownload.TrySetResult();
        }
        await publishing.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.Dispatcher.InvokeAsync(activator.Component.RefreshAsync);
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.DoesNotContain("Publish failed", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("retired private browser failure", markup, StringComparison.Ordinal);
        Assert.Contains("data-publish-error-code=\"\"", markup, StringComparison.Ordinal);
        Assert.Same(replaced, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(0, host.CaptureState().Session!.HistoryStatus.EntryCount);
        Assert.Equal(1, browser.DownloadCount);
    }

    [Fact]
    public async Task PublicationSaveChangedAndNoOpUseOneOrZeroPersistentActions()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("publication-save-canvas", "publication-save-container");
        var before = host.CaptureState();
        var beforeDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var publication = new DocumentPublicationSnapshot(
            "kompletacja-zamowienia",
            "Proces kompletacji zamówienia",
            "Przebieg procesu kompletacji zamówienia.");

        var changed = await host.SavePublicationAsync(
            before.DocumentSessionVersion,
            before.Session!.DocumentId,
            before.Session.DocumentRevision,
            publication);
        var afterChanged = host.CaptureState();
        var noOp = await host.SavePublicationAsync(
            afterChanged.DocumentSessionVersion,
            afterChanged.Session!.DocumentId,
            afterChanged.Session.DocumentRevision,
            publication);
        var afterNoOp = host.CaptureState();

        Assert.True(changed.Succeeded, PublishDiagnostics(changed.Diagnostics));
        Assert.True(changed.PublicationChanged);
        Assert.Empty(changed.Payload);
        Assert.Equal(before.Session.DocumentRevision.Increment(),
            afterChanged.Session.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            afterChanged.Session.HistoryStatus.EntryCount);
        Assert.Equal(publication, afterChanged.Publication);
        AssertDocumentStateExceptPublication(
            beforeDocument,
            AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(before.PipelineCounters!.ProjectionRuleInvocationCount,
            afterChanged.PipelineCounters!.ProjectionRuleInvocationCount);
        Assert.Equal(before.PipelineCounters.LayoutInvocationCount,
            afterChanged.PipelineCounters.LayoutInvocationCount);
        Assert.Equal(before.PipelineCounters.RoutingInvocationCount,
            afterChanged.PipelineCounters.RoutingInvocationCount);
        Assert.Equal(before.PipelineCounters.SceneContributionInvocationCount,
            afterChanged.PipelineCounters.SceneContributionInvocationCount);
        Assert.Equal(before.SuccessfulRenderCount, afterChanged.SuccessfulRenderCount);
        Assert.Equal(before.Session.CurrentScene!.Items,
            afterChanged.Session.CurrentScene!.Items);
        Assert.True(noOp.Succeeded, PublishDiagnostics(noOp.Diagnostics));
        Assert.False(noOp.PublicationChanged);
        Assert.Empty(noOp.Payload);
        Assert.Equal(afterChanged.Session.DocumentRevision,
            afterNoOp.Session!.DocumentRevision);
        Assert.Equal(afterChanged.Session.HistoryStatus, afterNoOp.Session.HistoryStatus);
    }

    [Fact]
    public async Task ChangedAndUnchangedPublishUseCommittedPublicationAndEquivalentBootstrap()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publication-publish-canvas", "publication-publish-container");
        var before = host.CaptureState();
        var publication = new DocumentPublicationSnapshot(
            "hostile-process",
            HostilePublishedText + " Tytuł",
            HostilePublishedText + " Opis");

        var changed = await host.PublishPublicationAsync(
            before.DocumentSessionVersion,
            before.Session!.DocumentId,
            before.Session.DocumentRevision,
            publication);
        var committed = host.CaptureState();
        var unchanged = await host.PublishPublicationAsync(
            committed.DocumentSessionVersion,
            committed.Session!.DocumentId,
            committed.Session.DocumentRevision,
            publication);
        var after = host.CaptureState();

        Assert.True(changed.Succeeded, PublishDiagnostics(changed.Diagnostics));
        Assert.True(changed.PublicationChanged);
        Assert.NotEmpty(changed.Payload);
        Assert.True(unchanged.Succeeded, PublishDiagnostics(unchanged.Diagnostics));
        Assert.False(unchanged.PublicationChanged);
        Assert.NotEmpty(unchanged.Payload);
        Assert.Equal(committed.Session.DocumentRevision, after.Session!.DocumentRevision);
        Assert.Equal(committed.Session.HistoryStatus, after.Session.HistoryStatus);
        var entries = ReadPublishedArchive(changed.Payload);
        var processJson = Encoding.UTF8.GetString(entries["process.json"]);
        var processData = Encoding.UTF8.GetString(entries["process.data.js"]);
        Assert.Equal(processJson, processData[PublishedDataBootstrapPrefix.Length..^2]);
        Assert.DoesNotContain("processId", processJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", processData, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(processJson);
        var actual = json.RootElement.GetProperty("publication");
        Assert.Equal(publication.Code, actual.GetProperty("code").GetString());
        Assert.Equal(publication.Title, actual.GetProperty("title").GetString());
        Assert.Equal(publication.Description, actual.GetProperty("description").GetString());
        Assert.Equal(publication, committed.Publication);
    }

    [Fact]
    public async Task PackageFailureAfterSaveKeepsPublicationAndStaleSessionCannotBeUpdated()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("publication-failure-canvas", "publication-failure-container");
        var before = host.CaptureState();
        var publication = new DocumentPublicationSnapshot(
            "invalid-token-graph",
            "Valid Publication",
            "The token graph remains independently invalid.");

        var failedPublish = await host.PublishPublicationAsync(
            before.DocumentSessionVersion,
            before.Session!.DocumentId,
            before.Session.DocumentRevision,
            publication);
        var saved = host.CaptureState();

        Assert.False(failedPublish.Succeeded);
        Assert.True(failedPublish.PublicationChanged);
        Assert.Empty(failedPublish.Payload);
        Assert.Equal(publication, saved.Publication);
        Assert.Equal(before.Session.DocumentRevision.Increment(), saved.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            saved.Session.HistoryStatus.EntryCount);
        Assert.DoesNotContain(failedPublish.Diagnostics, diagnostic =>
            diagnostic.Code == "PUBLISH_PUBLICATION_REQUIRED");
        var savedDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var unchangedFailure = await host.PublishPublicationAsync(saved.DocumentSessionVersion,
            saved.Session.DocumentId, saved.Session.DocumentRevision, publication);
        Assert.False(unchangedFailure.Succeeded);
        Assert.False(unchangedFailure.PublicationChanged);
        Assert.Empty(unchangedFailure.Payload);
        Assert.Same(savedDocument, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(saved.Session.HistoryStatus, host.CaptureState().Session!.HistoryStatus);

        var replacement = await host.NewDiagramAsync();
        Assert.True(replacement.Succeeded);
        var fresh = host.CaptureState();
        var stale = await host.SavePublicationAsync(
            saved.DocumentSessionVersion,
            saved.Session.DocumentId,
            saved.Session.DocumentRevision,
            new DocumentPublicationSnapshot("stale", "Stale", "Stale"));

        Assert.False(stale.Succeeded);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == "CANVAS_PUBLICATION_STALE");
        Assert.Null(host.CaptureState().Publication);
        Assert.NotEqual(saved.DocumentSessionVersion, fresh.DocumentSessionVersion);
        Assert.NotEqual(saved.Session.DocumentId, fresh.Session!.DocumentId);
    }

    [Fact]
    public async Task PublishCreatesDeterministicSelfContainedVersionedPackageAndIsReadOnly()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-canvas", "publish-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        await SaveDefaultPublicationAsync(session);
        var taskVisual = document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ProcessOrderSubProcessVisualId);
        await ExecutePublishCommandAsync(session, new ResizeVisualStateCommand(
            document.DocumentId,
            document.Revision,
            taskVisual.Id,
            new RectD(
                taskVisual.Position.X,
                taskVisual.Position.Y,
                480d,
                taskVisual.Size.Height),
            VisualPlacementMode.Pinned));
        var renamed = await session.ExecuteAsync(new UpdateSemanticElementNameCommand(
            document.DocumentId,
            document.Revision,
            BpmnDemoPipeline.ProcessOrderSubProcessId,
            BpmnSemanticProperties.Name,
            HostilePublishedText));
        Assert.True(renamed.IsCommitted);
        await session.WaitForIdleAsync();
        var selectedVisualId = session.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        var transient = new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "test:n10.4:transient-tool",
            viewport: new ViewportSnapshot(1.37d, new VectorD(41d, -23d)));
        Assert.True((await session.UpdateEditorStateAsync(transient)).Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();

        var first = await host.PublishProcessAsync();
        var second = await host.PublishProcessAsync();

        Assert.True(first.Succeeded, PublishDiagnostics(first.Diagnostics));
        Assert.True(second.Succeeded, PublishDiagnostics(second.Diagnostics));
        Assert.True(first.Payload.AsSpan().SequenceEqual(second.Payload.AsSpan()));
        var entries = ReadPublishedArchive(first.Payload);
        Assert.Equal(
            ["inceptus.publish.js", "index.html", "process.data.js", "process.json", "styles.css"],
            entries.Keys.Order(StringComparer.Ordinal));
        Assert.All(entries.Keys, name =>
        {
            Assert.False(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            Assert.False(name.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("_framework", name, StringComparison.OrdinalIgnoreCase);
        });

        using var json = JsonDocument.Parse(entries["process.json"]);
        var root = json.RootElement;
        Assert.Equal(PublishedProcessPackageBuilder.Format, root.GetProperty("format").GetString());
        Assert.Equal(PublishedProcessPackageBuilder.FormatVersion,
            root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(before.DocumentId.Value,
            root.GetProperty("source").GetProperty("documentId").GetString());
        Assert.Equal(before.DocumentRevision.Value,
            root.GetProperty("source").GetProperty("revision").GetUInt64());
        Assert.Equal(before.ActiveScopeId.Value,
            root.GetProperty("source").GetProperty("scopeId").GetString());
        var runtime = root.GetProperty("runtime");
        Assert.Equal(2000, runtime.GetProperty("activityDelayMs").GetInt32());
        Assert.Equal(300d, runtime.GetProperty("tokenSpeedPxPerSecond").GetDouble());
        Assert.Equal(150, runtime.GetProperty("minimumConnectorDurationMs").GetInt32());

        var processJson = Encoding.UTF8.GetString(entries["process.json"]);
        var processDataJavaScript = Encoding.UTF8.GetString(entries["process.data.js"]);
        Assert.StartsWith(PublishedDataBootstrapPrefix, processDataJavaScript,
            StringComparison.Ordinal);
        Assert.EndsWith(";\n", processDataJavaScript, StringComparison.Ordinal);
        var bootstrapJson = processDataJavaScript[
            PublishedDataBootstrapPrefix.Length..^2];
        Assert.Equal(processJson, bootstrapJson);
        using var bootstrapDocument = JsonDocument.Parse(bootstrapJson);
        Assert.True(JsonElement.DeepEquals(root, bootstrapDocument.RootElement));
        Assert.DoesNotContain("<script>", processJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003C/script\\u003E", processJson, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", processDataJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script>", processDataJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003C/script\\u003E", processDataJavaScript,
            StringComparison.Ordinal);
        var publishedText = string.Join(' ',
            root.GetProperty("presentation").GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("content").ValueKind == JsonValueKind.String
                    ? item.GetProperty("content").GetString()
                    : null)
                .Where(static content => content is not null));
        Assert.Contains("</script>", publishedText, StringComparison.Ordinal);
        Assert.Contains("alert(1)", publishedText, StringComparison.Ordinal);
        var bootstrapPublishedText = string.Join(' ',
            bootstrapDocument.RootElement.GetProperty("presentation").GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("content").ValueKind == JsonValueKind.String
                    ? item.GetProperty("content").GetString()
                    : null)
                .Where(static content => content is not null));
        Assert.Contains(HostilePublishedText, bootstrapPublishedText,
            StringComparison.Ordinal);
        var staticText = string.Join('\n', entries
            .Where(static entry => entry.Key != "process.json")
            .Select(static entry => Encoding.UTF8.GetString(entry.Value)));
        Assert.DoesNotContain("blazor", staticText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".wasm", staticText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework", staticText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", staticText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", staticText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("innerHTML", staticText, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", staticText, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", staticText, StringComparison.Ordinal);
        Assert.Contains("fillText", staticText, StringComparison.Ordinal);

        var after = session.CaptureState();
        Assert.Same(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.Equal(before.ModelProfileViewState, after.ModelProfileViewState);
        Assert.Equal(before.ModelProfileElementViewState, after.ModelProfileElementViewState);
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
    }

    [Fact]
    public async Task PublishUsesExpandedOrganizationalGeometryWithoutDecorationsOrStateMutation()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1600d, 1000d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-pools-canvas", "publish-pools-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        await SaveDefaultPublicationAsync(session);
        await EnableOrganizationalProfileAsync(session);
        var poolA = new SemanticElementId("test:n10.4:pool:a");
        var poolB = new SemanticElementId("test:n10.4:pool:b");
        await ExecutePublishCommandAsync(session, new CreateOrganizationalPoolCommand(
            document.DocumentId,
            document.Revision,
            poolA,
            session.CaptureState().ActiveScopeId,
            OrganizationalPoolCreationMode.AdoptEligibleUnassigned,
            "Operations"));
        await ExecutePublishCommandAsync(session, new CreateOrganizationalPoolCommand(
            document.DocumentId,
            document.Revision,
            poolB,
            session.CaptureState().ActiveScopeId,
            OrganizationalPoolCreationMode.Empty,
            "Fulfilment"));
        await ExecutePublishCommandAsync(session, new AssignOrganizationalElementCommand(
            document.DocumentId,
            document.Revision,
            BpmnDemoPipeline.TaskId,
            poolB));
        await ExecutePublishCommandAsync(session, new UnassignOrganizationalElementCommand(
            document.DocumentId,
            document.Revision,
            BpmnDemoPipeline.StartEventId));

        var taskVisual = document.VisualModel.VisualStates.Single(
            visual => visual.Id == BpmnDemoPipeline.TaskVisualId);
        await ExecutePublishCommandAsync(session, new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            taskVisual.Id,
            new PointD(taskVisual.Position.X + 31d, taskVisual.Position.Y + 17d),
            VisualPlacementMode.Pinned));
        var state = session.CaptureState();
        var firstEdge = state.ProjectedGraph!.Edges.Single(edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.FirstSequenceFlowId);
        var firstRoute = state.RoutingResult!.Routes.Single(route =>
            route.ProjectedEdgeId == firstEdge.Id).Path;
        var bend = new PointD(
            (firstRoute[0].X + firstRoute[^1].X) / 2d,
            Math.Max(firstRoute[0].Y, firstRoute[^1].Y) + 47d);
        await ExecutePublishCommandAsync(session, new UpdateConnectionRouteCommand(
            document.DocumentId,
            document.Revision,
            BpmnDemoPipeline.FirstSequenceFlowVisualId,
            [firstRoute[0], bend, firstRoute[^1]]));

        var shown = session.CaptureState().ModelProfileViewState
            .WithPreferredVisibility(OrganizationalModelProfile.Id, isVisible: true);
        Assert.True((await session.UpdateModelProfileViewStateAsync(shown)).Succeeded);
        var collapsed = session.CaptureState().ModelProfileElementViewState
            .WithCollapsed(OrganizationalModelProfile.Id, poolA, isCollapsed: true);
        Assert.True((await session.UpdateModelProfileElementViewStateAsync(collapsed)).Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();
        Assert.True(before.ModelProfileViewState.IsPreferredVisible(
            OrganizationalModelProfile.Id));
        Assert.True(before.ModelProfileElementViewState.IsCollapsed(
            OrganizationalModelProfile.Id,
            poolA));
        var poolAElement = document.SemanticModel.ProfileAssignments
            .Where(assignment => assignment.ProfileId == OrganizationalModelProfile.Id &&
                                 assignment.ContainerSemanticElementId == poolA)
            .Select(static assignment => assignment.SemanticElementId)
            .First(elementId => before.ProjectedGraph!.Nodes.Any(node =>
                node.Source.SemanticElementId == elementId));
        Assert.DoesNotContain(before.CurrentScene!.Items, item => item.IsVisible &&
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.SemanticElementId == poolAElement);

        var published = await host.PublishProcessAsync();

        Assert.True(published.Succeeded, PublishDiagnostics(published.Diagnostics));
        var entries = ReadPublishedArchive(published.Payload);
        var packageJson = entries["process.json"];
        var publishView = before.ModelProfileViewState
            .WithPreferredVisibility(OrganizationalModelProfile.Id, isVisible: false);
        var publishElements = new ModelProfileElementViewStateSnapshot(
            before.ModelProfileElementViewState.CollapsedElements.Where(entry =>
                entry.ProfileId != OrganizationalModelProfile.Id));
        var captureResult = await session.CapturePresentationAsync(publishView, publishElements);
        Assert.True(captureResult.Succeeded, PublishDiagnostics(captureResult.Diagnostics));
        var capture = Assert.IsType<EditingSessionPresentationCapture>(captureResult.Capture);
        var directBuild = new PublishedProcessPackageBuilder(
            new BpmnPublishedTokenRoleClassifier(),
            new BpmnPublishedNodeDataMapper()).Build(capture);
        Assert.True(directBuild.Succeeded, PublishDiagnostics(directBuild.Diagnostics));
        var snapshot = Assert.IsType<PublishedProcessPackage>(directBuild.Package).Snapshot;
        Assert.True(packageJson.AsSpan().SequenceEqual(directBuild.Package!.ProcessJson.AsSpan()));

        Assert.Equal(capture.ProjectedGraph.Nodes.Length, snapshot.Presentation.Nodes.Length);
        Assert.Contains(snapshot.Presentation.Nodes, node => node.Id == poolAElement.Value);
        Assert.Contains(snapshot.Presentation.Nodes, node => node.Id == BpmnDemoPipeline.TaskId.Value);
        Assert.Contains(snapshot.Presentation.Nodes, node => node.Id == BpmnDemoPipeline.StartEventId.Value);
        foreach (var node in capture.ProjectedGraph.Nodes)
        {
            var expectedItem = capture.Scene.Items.Single(item => item.IsVisible &&
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"));
            var actual = snapshot.Presentation.Nodes.Single(publishedNode =>
                publishedNode.Id == node.Source.SemanticElementId.Value);
            AssertPublishedRect(expectedItem.Bounds, actual.Bounds);
        }
        foreach (var edge in capture.ProjectedGraph.Edges)
        {
            var expectedItem = capture.Scene.Items.Single(item => item.IsVisible &&
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
            var expectedPoints = Canvas2DConnectorPathMetadata.Resolve(expectedItem);
            var actual = snapshot.Presentation.Connectors.Single(connector =>
                connector.Id == edge.Source.SemanticElementId.Value);
            Assert.Equal(expectedPoints.Length, actual.Points.Length);
            for (var index = 0; index < expectedPoints.Length; index++)
            {
                Assert.Equal(expectedPoints[index].X, actual.Points[index].X, 9);
                Assert.Equal(expectedPoints[index].Y, actual.Points[index].Y, 9);
            }
        }
        var manualRoute = snapshot.Presentation.Connectors.Single(connector =>
            connector.Id == BpmnDemoPipeline.FirstSequenceFlowId.Value);
        Assert.True(manualRoute.Points.Length >= 3);

        var decoded = Encoding.UTF8.GetString(packageJson);
        Assert.DoesNotContain(poolA.Value, decoded, StringComparison.Ordinal);
        Assert.DoesNotContain(poolB.Value, decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Fulfilment", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Unassigned", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pool", decoded, StringComparison.OrdinalIgnoreCase);

        var assignedToA = snapshot.Presentation.Nodes.Single(node => node.Id == poolAElement.Value);
        var assignedToB = snapshot.Presentation.Nodes.Single(node =>
            node.Id == BpmnDemoPipeline.TaskId.Value);
        var unassigned = snapshot.Presentation.Nodes.Single(node =>
            node.Id == BpmnDemoPipeline.StartEventId.Value);
        Assert.NotEqual(assignedToA.Bounds.Y, assignedToB.Bounds.Y);
        Assert.NotEqual(assignedToB.Bounds.Y, unassigned.Bounds.Y);

        var after = session.CaptureState();
        Assert.Same(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.Equal(before.ModelProfileViewState, after.ModelProfileViewState);
        Assert.Equal(before.ModelProfileElementViewState, after.ModelProfileElementViewState);
    }

    [Fact]
    public async Task PublishAfterEditUsesNewGeometryWithoutChangingPreviousArtifact()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("publish-edit-canvas", "publish-edit-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        await SaveDefaultPublicationAsync(session);
        var first = await host.PublishProcessAsync();
        Assert.True(first.Succeeded, PublishDiagnostics(first.Diagnostics));
        var firstBytes = first.Payload.ToArray();
        var firstBounds = ReadPublishedNodeBounds(first.Payload, BpmnDemoPipeline.TaskId.Value);
        var visual = document.VisualModel.VisualStates.Single(item =>
            item.Id == BpmnDemoPipeline.TaskVisualId);

        await ExecutePublishCommandAsync(session, new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(visual.Position.X + 80d, visual.Position.Y + 35d),
            VisualPlacementMode.Pinned));
        var second = await host.PublishProcessAsync();

        Assert.True(second.Succeeded, PublishDiagnostics(second.Diagnostics));
        var secondBounds = ReadPublishedNodeBounds(second.Payload, BpmnDemoPipeline.TaskId.Value);
        Assert.NotEqual(firstBounds, secondBounds);
        Assert.True(firstBytes.AsSpan().SequenceEqual(first.Payload.AsSpan()));
        Assert.False(first.Payload.AsSpan().SequenceEqual(second.Payload.AsSpan()));
    }

    [Fact]
    public async Task UnsupportedTokenGraphRejectsPublishWithoutChangingEditor()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("publish-reject-canvas", "publish-reject-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        await SaveDefaultPublicationAsync(session);
        var before = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();

        var result = await host.PublishProcessAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(PublishedProcessHostOperationStatus.Rejected, result.Status);
        Assert.Empty(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "PUBLISH_TOKEN_NODE_UNSUPPORTED" ||
            diagnostic.Code == "PUBLISH_TOKEN_START_INVALID");
        var after = session.CaptureState();
        Assert.Same(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
    }

    [Theory]
    [InlineData("start", 0, 1, PublishedTokenRole.Start)]
    [InlineData("task", 1, 1, PublishedTokenRole.ActivityDelay)]
    [InlineData("exclusive", 1, 3, PublishedTokenRole.SplitInvariant)]
    [InlineData("exclusive-merge", 3, 1, PublishedTokenRole.MergeInvariant)]
    [InlineData("parallel", 3, 1, PublishedTokenRole.ParallelSynchronize)]
    [InlineData("event", 1, 1, PublishedTokenRole.PassThrough)]
    [InlineData("end", 2, 0, PublishedTokenRole.End)]
    public void BpmnPublishClassificationUsesSemanticTypeAndUnambiguousTopology(
        string kind,
        int incoming,
        int outgoing,
        PublishedTokenRole expected)
    {
        var type = kind switch
        {
            "start" => BpmnSemanticTypes.StartEvent,
            "task" => BpmnSemanticTypes.Task,
            "exclusive" or "exclusive-merge" => BpmnSemanticTypes.ExclusiveGateway,
            "parallel" => BpmnSemanticTypes.ParallelGateway,
            "event" => BpmnSemanticTypes.MessageCatchEvent,
            "end" => BpmnSemanticTypes.EndEvent,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var result = new BpmnPublishedTokenRoleClassifier().Classify(
            new PublishedTokenRoleClassificationRequest(
                new SemanticElementId($"test:n10.4:{kind}"),
                type,
                incoming,
                outgoing));

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Role);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void BpmnParallelGatewayAlwaysUsesDistinctParallelSynchronizationRole(
        int incoming,
        int outgoing)
    {
        var result = new BpmnPublishedTokenRoleClassifier().Classify(
            new PublishedTokenRoleClassificationRequest(
                new SemanticElementId("test:n10.4:parallel"),
                BpmnSemanticTypes.ParallelGateway,
                incoming,
                outgoing));

        Assert.True(result.Succeeded);
        Assert.Equal(PublishedTokenRole.ParallelSynchronize, result.Role);
        Assert.NotEqual(PublishedTokenRole.SplitInvariant, result.Role);
        Assert.NotEqual(PublishedTokenRole.MergeInvariant, result.Role);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void BpmnParallelGatewayRejectsMissingInputOrOutput(int incoming, int outgoing)
    {
        var result = new BpmnPublishedTokenRoleClassifier().Classify(
            new PublishedTokenRoleClassificationRequest(
                new SemanticElementId("test:n10.4:invalid-parallel"),
                BpmnSemanticTypes.ParallelGateway,
                incoming,
                outgoing));

        Assert.False(result.Succeeded);
        Assert.Equal("PUBLISH_TOKEN_PARALLEL_GATEWAY_INVALID", result.DiagnosticCode);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(1, 0)]
    public void BpmnGatewayClassificationRejectsAmbiguousTopology(int incoming, int outgoing)
    {
        var result = new BpmnPublishedTokenRoleClassifier().Classify(
            new PublishedTokenRoleClassificationRequest(
                new SemanticElementId("test:n10.4:ambiguous-gateway"),
                BpmnSemanticTypes.ExclusiveGateway,
                incoming,
                outgoing));

        Assert.False(result.Succeeded);
        Assert.Equal("PUBLISH_TOKEN_GATEWAY_AMBIGUOUS", result.DiagnosticCode);
    }

    private static void InitializePublishDialog(DocumentCanvas component, DocumentCanvasHost host)
    {
        var state = host.CaptureState();
        typeof(DocumentCanvas).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(component, state);
        typeof(DocumentCanvas).GetMethod(
            "InitializePublicationDialogDraft", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(component, [state, state.Session]);
    }

    private static Task InvokePublishComponentAsync(DocumentCanvas component, string methodName) =>
        Assert.IsAssignableFrom<Task>(typeof(DocumentCanvas).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null));

    private static void InvokePublishComponent(DocumentCanvas component, string methodName) =>
        typeof(DocumentCanvas).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(component, null);

    private static ServiceProvider PublishComponentServices(
        PublishComponentActivator activator, PublishDownloadRuntime browser) =>
        new ServiceCollection()
            .AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(browser)
            .AddSingleton<IComponentActivator>(activator)
            .BuildServiceProvider();

    private sealed class RejectedPublishRoleClassifier : IPublishedTokenRoleClassifier
    {
        private readonly BpmnPublishedTokenRoleClassifier _real = new();

        public PublishedTokenRoleClassification Classify(PublishedTokenRoleClassificationRequest request) =>
            request.SemanticTypeId == BpmnSemanticTypes.Task
                ? PublishedTokenRoleClassification.Failure("PUBLISH_TOKEN_NODE_UNSUPPORTED",
                    $"The Task '{request.SemanticElementId}' cannot be published: {HostilePublishedText}")
                : _real.Classify(request);
    }

    private sealed class ThrowingPublishNodeDataMapper : IPublishedNodeDataMapper
    {
        public PublishedNodeData Map(
            Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementSnapshot semanticElement) =>
            throw new InvalidOperationException("private implementation detail " + HostilePublishedText);
    }

    private sealed class PublishComponentActivator(DocumentCanvasHost host) : IComponentActivator
    {
        internal PublishTestDocumentCanvas Component { get; private set; } = null!;

        public IComponent CreateInstance(Type componentType)
        {
            if (componentType != typeof(DocumentCanvas))
            {
                return (IComponent)Activator.CreateInstance(componentType)!;
            }

            Component = new PublishTestDocumentCanvas();
            typeof(DocumentCanvas).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(Component, host);
            typeof(DocumentCanvas).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(Component, host.CaptureState());
            return Component;
        }
    }

    private sealed class PublishTestDocumentCanvas : DocumentCanvas
    {
        internal Task RefreshAsync() => InvokeAsync(StateHasChanged);
    }

    private sealed class PublishDownloadRuntime : IJSRuntime
    {
        internal Exception? Failure { get; set; }

        internal int DownloadCount { get; private set; }

        internal bool DelayDownload { get; set; }

        internal TaskCompletionSource DownloadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseDownload { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("import", identifier);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult((TValue)(object)new DownloadModule(this));
        }

        private sealed class DownloadModule(PublishDownloadRuntime owner) : IJSObjectReference
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
                InvokeAsync<TValue>(identifier, CancellationToken.None, args);

            public async ValueTask<TValue> InvokeAsync<TValue>(string identifier,
                CancellationToken cancellationToken, object?[]? args)
            {
                Assert.Equal("downloadFile", identifier);
                Assert.NotEmpty(Assert.IsType<byte[]>(args![0]));
                Assert.Equal("application/zip", args[1]);
                Assert.Equal("published-process.zip", args[2]);
                owner.DownloadCount++;
                owner.DownloadStarted.TrySetResult();
                if (owner.DelayDownload)
                {
                    // Deliberately finish late even if cancellation is requested: the owning
                    // component must reject a retired document session's browser completion.
                    await owner.ReleaseDownload.Task;
                }
                if (owner.Failure is { } failure)
                {
                    throw failure;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return default!;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async ValueTask ExecutePublishCommandAsync(
        EditingSession session,
        ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, PublishDiagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static ValueTask SaveDefaultPublicationAsync(EditingSession session)
    {
        var document = AttachedDocument(session);
        return ExecutePublishCommandAsync(
            session,
            new UpdateDocumentPublicationCommand(
                document.DocumentId,
                document.Revision,
                "test-process",
                "Test process",
                "Published process test fixture"));
    }

    private static SortedDictionary<string, byte[]> ReadPublishedArchive(
        ImmutableArray<byte> payload)
    {
        using var input = new MemoryStream(payload.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            result.Add(entry.FullName, memory.ToArray());
        }

        return result;
    }

    private static PublishedRect ReadPublishedNodeBounds(
        ImmutableArray<byte> payload,
        string nodeId)
    {
        using var json = JsonDocument.Parse(ReadPublishedArchive(payload)["process.json"]);
        var node = json.RootElement.GetProperty("presentation").GetProperty("nodes")
            .EnumerateArray().Single(item => item.GetProperty("id").GetString() == nodeId);
        var bounds = node.GetProperty("bounds");
        return new PublishedRect(
            bounds.GetProperty("x").GetDouble(),
            bounds.GetProperty("y").GetDouble(),
            bounds.GetProperty("width").GetDouble(),
            bounds.GetProperty("height").GetDouble());
    }

    private static void AssertPublishedRect(RectD expected, PublishedRect actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
        Assert.Equal(expected.Height, actual.Height, 9);
    }

    private static void AssertDocumentStateExceptPublication(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot expected,
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot actual)
    {
        var expectedAtActualRevision = DocumentSnapshotCloner.CloneAtRevision(
            expected,
            actual.Revision);

        Assert.Equal(expectedAtActualRevision.SemanticModel, actual.SemanticModel);
        Assert.Equal(expectedAtActualRevision.VisualModel, actual.VisualModel);
        Assert.Equal(expectedAtActualRevision.Metadata, actual.Metadata);
    }

    private static string PublishDiagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message} " +
            string.Join(", ", diagnostic.Context.Select(static entry =>
                $"{entry.Key}={entry.Value}"))));
}
