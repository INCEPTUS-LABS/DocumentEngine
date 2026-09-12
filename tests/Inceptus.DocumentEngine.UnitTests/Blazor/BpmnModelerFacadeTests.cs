using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly string[] FacadeExpectedPublishEntries =
        ["inceptus.publish.js", "index.html", "process.data.js", "process.json", "styles.css"];

    [Fact]
    public async Task FacadeBeforeAttachmentReturnsOneBoundedFailurePerOperation()
    {
        var notifications = new ConcurrentQueue<object>();
        using var facade = new BpmnModelerFacade(new BpmnModelerCompositionFactory(), value =>
        {
            notifications.Enqueue(value);
            return Task.CompletedTask;
        });

        var capture = facade.CaptureDocumentSnapshot();
        var created = await facade.NewDocumentAsync();
        var loaded = await facade.LoadDocumentAsync(null);
        var imported = await facade.ImportNativeDocumentAsync(ReadOnlyMemory<byte>.Empty);
        var exported = await facade.ExportNativeDocumentAsync();
        var published = await facade.PublishAsync();

        Assert.All(new[] { capture, created, loaded, imported }, static result =>
        {
            Assert.Equal(BpmnModelerOperationStatus.Unavailable, result.Status);
            Assert.Null(result.Snapshot);
            Assert.Single(result.Diagnostics);
        });
        Assert.Equal(BpmnModelerOperationStatus.Unavailable, exported.Status);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable, published.Status);
        Assert.Null(exported.Artifact);
        Assert.Null(published.Artifact);
        Assert.Equal(
            new[]
            {
                BpmnModelerOperation.Capture,
                BpmnModelerOperation.New,
                BpmnModelerOperation.Load,
                BpmnModelerOperation.Import,
                BpmnModelerOperation.Export,
                BpmnModelerOperation.Publish,
            },
            notifications.Select(value => Assert.IsType<BpmnModelerOperationFailedEventArgs>(value)
                .Operation));
    }

    [Fact]
    public async Task FacadeCaptureExportNewLoadAndImportPreserveCanonicalIdentityAndCallbacks()
    {
        await using var fixture = new FacadeTestFixture(
            new BpmnModelerCompositionFactory([BpmnDemoStartupDocumentProvider.Instance]));
        await fixture.InitializeAsync();
        var originalSession = Session(fixture.Host);
        var capture = fixture.Facade.CaptureDocumentSnapshot();
        var snapshot = Assert.IsType<DocumentSnapshot>(capture.Snapshot);
        var before = fixture.Host.CaptureState();

        var export = await fixture.Facade.ExportNativeDocumentAsync();
        var artifact = Assert.IsType<BpmnModelerFileArtifact>(export.Artifact);

        Assert.True(export.Succeeded);
        Assert.Equal(DocumentCanvas.NativeDocumentFileName, artifact.FileName);
        Assert.Equal(DocumentCanvas.NativeDocumentContentType, artifact.ContentType);
        Assert.True(NativeDocumentSerializer.Export(snapshot).AsSpan()
            .SequenceEqual(artifact.Content.AsSpan()));
        Assert.Same(snapshot, fixture.Facade.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.Session!.HistoryStatus, fixture.Host.CaptureState().Session!.HistoryStatus);
        Assert.Equal(before.SuccessfulRenderCount, fixture.Host.CaptureState().SuccessfulRenderCount);
        Assert.Single(fixture.Notifications);

        var created = await fixture.Facade.NewDocumentAsync();
        Assert.True(created.Succeeded);
        Assert.NotEqual(snapshot.DocumentId, created.Snapshot!.DocumentId);
        Assert.Equal(DocumentRevision.Zero, created.Snapshot.Revision);
        Assert.Empty(created.Snapshot.SemanticModel.Elements);
        Assert.NotSame(originalSession, Session(fixture.Host));
        Assert.Equal(0, fixture.Host.CaptureState().Session!.HistoryStatus.EntryCount);

        var loaded = await fixture.Facade.LoadDocumentAsync(snapshot);
        Assert.True(loaded.Succeeded);
        Assert.Equal(snapshot, loaded.Snapshot);
        Assert.Equal(0, fixture.Host.CaptureState().Session!.HistoryStatus.EntryCount);
        Assert.False(fixture.Host.CaptureState().Session!.HistoryStatus.CanUndo);

        var imported = await fixture.Facade.ImportNativeDocumentAsync(artifact.Content.AsMemory());
        Assert.True(imported.Succeeded);
        Assert.Equal(snapshot, imported.Snapshot);
        Assert.Equal(snapshot, fixture.Facade.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(0, fixture.Host.CaptureState().Session!.HistoryStatus.EntryCount);
        Assert.Collection(fixture.Notifications,
            ready => Assert.Equal(snapshot, Assert.IsType<BpmnModelerReadyEventArgs>(ready).Snapshot),
            change => AssertFacadeReplacement(change, created.Snapshot),
            change => AssertFacadeReplacement(change, snapshot),
            change => AssertFacadeReplacement(change, snapshot));
    }

    [Fact]
    public async Task FacadeRejectedImportLoadAndPublishPreserveStateWithoutDuplicateFailures()
    {
        await using var fixture = new FacadeTestFixture();
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var snapshot = fixture.Facade.CaptureDocumentSnapshot().Snapshot;
        var before = fixture.Host.CaptureState();

        var import = await fixture.Facade.ImportNativeDocumentAsync(Encoding.UTF8.GetBytes("{"));
        var load = await fixture.Facade.LoadDocumentAsync(null);
        var publish = await fixture.Facade.PublishAsync();

        Assert.Equal(BpmnModelerOperationStatus.Rejected, import.Status);
        Assert.Equal("NATIVE_DOCUMENT_JSON_MALFORMED", Assert.Single(import.Diagnostics).Code);
        Assert.Equal(BpmnModelerOperationStatus.Rejected, load.Status);
        Assert.Equal("CANVAS_DOCUMENT_LOAD_REQUIRED", Assert.Single(load.Diagnostics).Code);
        Assert.False(publish.Succeeded);
        Assert.Null(publish.Artifact);
        Assert.Same(session, Session(fixture.Host));
        Assert.Same(snapshot, fixture.Facade.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.Session!.HistoryStatus, fixture.Host.CaptureState().Session!.HistoryStatus);
        Assert.Null(fixture.Host.CaptureState().ValidationSnapshot);
        Assert.Collection(fixture.Notifications,
            ready => Assert.IsType<BpmnModelerReadyEventArgs>(ready),
            failure => AssertFacadeFailure(failure, BpmnModelerOperation.Import),
            failure => AssertFacadeFailure(failure, BpmnModelerOperation.Load),
            failure => AssertFacadeFailure(failure, BpmnModelerOperation.Publish));
    }

    [Theory]
    [InlineData(BpmnModelerOperation.New)]
    [InlineData(BpmnModelerOperation.Load)]
    [InlineData(BpmnModelerOperation.Import)]
    public async Task FacadeCandidateFailurePreservesExactSessionAndReportsOnce(
        BpmnModelerOperation operation)
    {
        await using var fixture = new FacadeTestFixture(replacementRendererFactory:
            () => throw new InvalidOperationException("Private renderer-construction details."));
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var snapshot = fixture.Facade.CaptureDocumentSnapshot().Snapshot!;
        var before = fixture.Host.CaptureState();

        var result = operation switch
        {
            BpmnModelerOperation.New => await fixture.Facade.NewDocumentAsync(),
            BpmnModelerOperation.Load => await fixture.Facade.LoadDocumentAsync(snapshot),
            _ => await fixture.Facade.ImportNativeDocumentAsync(
                NativeDocumentSerializer.Export(snapshot).AsMemory()),
        };

        Assert.Equal(BpmnModelerOperationStatus.Failed, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Same(session, Session(fixture.Host));
        Assert.Same(snapshot, fixture.Facade.CaptureDocumentSnapshot().Snapshot);
        var after = fixture.Host.CaptureState();
        Assert.Equal(before.Session!.HistoryStatus, after.Session!.HistoryStatus);
        Assert.Same(before.Session.EditorState, after.Session.EditorState);
        Assert.Equal(before.ActiveCanvasElementId, after.ActiveCanvasElementId);
        Assert.Null(after.ValidationSnapshot);
        Assert.Collection(fixture.Notifications,
            ready => Assert.IsType<BpmnModelerReadyEventArgs>(ready),
            failure => AssertFacadeFailure(failure, operation));
    }

    [Fact]
    public async Task FacadeCancellationDoesNotReplaceDocumentOrEmitFailureCallbacks()
    {
        await using var fixture = new FacadeTestFixture();
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var snapshot = fixture.Facade.CaptureDocumentSnapshot().Snapshot!;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var created = await fixture.Facade.NewDocumentAsync(cancellation.Token);
        var loaded = await fixture.Facade.LoadDocumentAsync(snapshot, cancellation.Token);
        var imported = await fixture.Facade.ImportNativeDocumentAsync(
            NativeDocumentSerializer.Export(snapshot).AsMemory(), cancellation.Token);
        var exported = await fixture.Facade.ExportNativeDocumentAsync(cancellation.Token);
        var published = await fixture.Facade.PublishAsync(cancellation.Token);

        Assert.All(new[] { created, loaded, imported }, static result =>
        {
            Assert.Equal(BpmnModelerOperationStatus.Cancelled, result.Status);
            Assert.Null(result.Snapshot);
        });
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, exported.Status);
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, published.Status);
        Assert.Same(session, Session(fixture.Host));
        Assert.Same(snapshot, fixture.Facade.CaptureDocumentSnapshot().Snapshot);
        Assert.IsType<BpmnModelerReadyEventArgs>(Assert.Single(fixture.Notifications));
    }

    [Fact]
    public async Task FacadePublishReturnsStandaloneZipAndLeavesDocumentAndHistoryUnchanged()
    {
        await using var fixture = new FacadeTestFixture(
            new BpmnModelerCompositionFactory([BpmnDemoStartupDocumentProvider.Instance]));
        await fixture.InitializeAsync();
        var initial = fixture.Host.CaptureState();
        var saved = await fixture.Host.SavePublicationAsync(
            initial.DocumentSessionVersion,
            initial.Session!.DocumentId,
            initial.Session.DocumentRevision,
            new DocumentPublicationSnapshot("facade-process", "Facade process", "Publish test"));
        Assert.True(saved.Succeeded);
        var before = fixture.Host.CaptureState();
        var snapshot = fixture.Facade.CaptureDocumentSnapshot().Snapshot;
        var callbackCount = fixture.Notifications.Count;

        var published = await fixture.Facade.PublishAsync();

        Assert.True(published.Succeeded, PublishDiagnostics(published.Diagnostics));
        var artifact = Assert.IsType<BpmnModelerFileArtifact>(published.Artifact);
        Assert.Equal(DocumentCanvas.PublishFileName, artifact.FileName);
        Assert.Equal(DocumentCanvas.PublishContentType, artifact.ContentType);
        var entries = ReadPublishedArchive(artifact.Content);
        Assert.Equal(
            FacadeExpectedPublishEntries,
            entries.Keys.Order(StringComparer.Ordinal));
        using var json = JsonDocument.Parse(entries["process.json"]);
        Assert.Equal("Inceptus.PublishedProcess", json.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(snapshot!.DocumentId.Value,
            json.RootElement.GetProperty("source").GetProperty("documentId").GetString());
        Assert.Same(snapshot, fixture.Facade.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.Session!.HistoryStatus, fixture.Host.CaptureState().Session!.HistoryStatus);
        Assert.Equal(callbackCount, fixture.Notifications.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FacadePublicationMetadataAfterSnapshotRecreationLeavesToolboxPlacementUsable(
        bool publish)
    {
        DocumentSnapshot savedSnapshot;
        await using (var original = new FacadeTestFixture())
        {
            await original.InitializeAsync();
            await CreateNotifiedTaskAsync(original.Host, original.Selection, new PointD(400d, 240d));
            await original.Host.UndoAsync();
            await original.Host.RedoAsync();
            savedSnapshot = original.Facade.CaptureDocumentSnapshot().Snapshot!;
            Assert.Equal(new DocumentRevision(3), savedSnapshot.Revision);
            Assert.Single(savedSnapshot.SemanticModel.Elements);
            var exported = await original.Facade.ExportNativeDocumentAsync();
            Assert.True(exported.Succeeded);
            Assert.True((await original.Facade.NewDocumentAsync()).Succeeded);
            Assert.True((await original.Facade.ImportNativeDocumentAsync(
                exported.Artifact!.Content.AsMemory())).Succeeded);
            Assert.True((await original.Facade.NewDocumentAsync()).Succeeded);
            Assert.True((await original.Facade.LoadDocumentAsync(savedSnapshot)).Succeeded);
        }

        await using var recreated = new FacadeTestFixture(
            new BpmnModelerCompositionFactory(initialDocument: savedSnapshot));
        await recreated.InitializeAsync();
        var initial = recreated.Host.CaptureState();
        var metadata = new DocumentPublicationSnapshot("incomplete-process", "Incomplete process", "Test");
        var publication = await recreated.Host.SavePublicationAsync(
            initial.DocumentSessionVersion,
            savedSnapshot.DocumentId,
            savedSnapshot.Revision,
            metadata);
        Assert.True(publication.Succeeded);

        if (publish)
        {
            var published = await recreated.Facade.PublishAsync();
            Assert.Equal(BpmnModelerOperationStatus.Rejected, published.Status);
            Assert.Contains(published.Diagnostics, static diagnostic =>
                diagnostic.Code == "PUBLISH_TOKEN_START_INVALID");
        }

        Assert.Equal(new DocumentRevision(4), recreated.Facade.CaptureDocumentSnapshot().Snapshot!.Revision);
        await CreateNotifiedTaskAsync(recreated.Host, recreated.Selection, new PointD(650d, 370d));
        var afterTask = recreated.Facade.CaptureDocumentSnapshot().Snapshot!;
        Assert.Equal(new DocumentRevision(5), afterTask.Revision);
        Assert.Equal(2, afterTask.SemanticModel.ElementCount);
        Assert.Equal(metadata, afterTask.Publication);

        var startEvent = BpmnModelerCompositionFactory.ToolboxCatalog.Items.Single(
            static item => item.ElementTypeId == BpmnSemanticTypes.StartEvent);
        Assert.True(recreated.Selection.Select(startEvent.ItemId));
        await recreated.Host.RefreshToolboxPlacementAsync();
        await PointerObserver(recreated.Host).ClickCssPointAsync(new PointD(250d, 440d));
        await Session(recreated.Host).WaitForIdleAsync();
        var afterStart = recreated.Facade.CaptureDocumentSnapshot().Snapshot!;
        Assert.Equal(new DocumentRevision(6), afterStart.Revision);
        Assert.Equal(3, afterStart.SemanticModel.ElementCount);
        Assert.Equal(metadata, afterStart.Publication);
        Assert.Contains(afterStart.SemanticModel.Elements, static element =>
            element.TypeId == BpmnSemanticTypes.StartEvent);
        Assert.Equal(3, ModelerChanges(recreated.Notifications).Length);
        if (publish)
        {
            Assert.Single(recreated.Notifications.OfType<BpmnModelerOperationFailedEventArgs>());
        }
        else
        {
            Assert.Empty(recreated.Notifications.OfType<BpmnModelerOperationFailedEventArgs>());
        }

        Assert.Empty(recreated.Host.CaptureState().InteractionDiagnostics);
    }

    [Fact]
    public async Task FacadeCommittedPublicationNotificationPrecedesSubsequentPackageFailure()
    {
        await using var fixture = new FacadeTestFixture();
        await fixture.InitializeAsync();
        var before = fixture.Host.CaptureState();
        var publication = new DocumentPublicationSnapshot("empty-process", "Empty process", "Test");

        var result = await fixture.Host.PublishPublicationAsync(
            before.DocumentSessionVersion,
            before.Session!.DocumentId,
            before.Session.DocumentRevision,
            publication);

        Assert.False(result.Succeeded);
        Assert.True(result.PublicationChanged);
        Assert.Collection(fixture.Notifications,
            ready => Assert.IsType<BpmnModelerReadyEventArgs>(ready),
            change =>
            {
                var accepted = Assert.IsType<BpmnModelerDocumentChangedEventArgs>(change);
                Assert.Equal(BpmnModelerDocumentChangeKind.PersistentMutation, accepted.Kind);
                Assert.Equal(publication, accepted.Snapshot.Publication);
            },
            failure => AssertFacadeFailure(failure, BpmnModelerOperation.Publish));
        Assert.Equal(publication, fixture.Facade.CaptureDocumentSnapshot().Snapshot!.Publication);
    }

    [Fact]
    public async Task FacadeFailureNotificationPrecedesConcurrentlyRequestedReplacement()
    {
        var failureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new FacadeTestFixture(invokeNotification: notification =>
        {
            if (notification is BpmnModelerOperationFailedEventArgs)
            {
                failureStarted.TrySetResult();
                return releaseFailure.Task;
            }

            return Task.CompletedTask;
        });
        await fixture.InitializeAsync();

        var import = fixture.Facade.ImportNativeDocumentAsync(Encoding.UTF8.GetBytes("{")).AsTask();
        Task<BpmnModelerDocumentResult> replacement;
        try
        {
            await failureStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            replacement = fixture.Facade.NewDocumentAsync().AsTask();
            Assert.False(replacement.IsCompleted);
        }
        finally
        {
            releaseFailure.TrySetResult();
        }

        Assert.False((await import).Succeeded);
        var created = await replacement;
        Assert.True(created.Succeeded);

        Assert.Collection(fixture.Notifications,
            ready => Assert.IsType<BpmnModelerReadyEventArgs>(ready),
            failure => AssertFacadeFailure(failure, BpmnModelerOperation.Import),
            changed => AssertFacadeReplacement(changed, created.Snapshot!));
    }

    [Fact]
    public async Task FacadeProviderResolutionFailureRemainsBoundedStartupWithoutReady()
    {
        var factory = new BpmnModelerCompositionFactory(
            () => throw new InvalidOperationException("Private provider constructor details."));
        await using var fixture = new FacadeTestFixture(factory);

        await fixture.InitializeAsync();

        Assert.Null(fixture.Host.CaptureState().Session);
        var failure = Assert.IsType<BpmnModelerOperationFailedEventArgs>(
            Assert.Single(fixture.Notifications));
        Assert.Equal(BpmnModelerOperation.Startup, failure.Operation);
        Assert.Equal(BpmnModelerOperationStatus.Failed, failure.Status);
        Assert.DoesNotContain("Private", Assert.Single(failure.Diagnostics).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FacadeInstancesKeepDocumentAndCallbackStreamsIndependent()
    {
        await using var first = new FacadeTestFixture();
        await using var second = new FacadeTestFixture();
        await first.InitializeAsync();
        await second.InitializeAsync();
        var secondSnapshot = second.Facade.CaptureDocumentSnapshot().Snapshot;
        Assert.NotSame(Session(first.Host), Session(second.Host));
        Assert.NotEqual(first.Facade.CaptureDocumentSnapshot().Snapshot!.DocumentId,
            secondSnapshot!.DocumentId);

        var changedFirst = await first.Facade.NewDocumentAsync();

        Assert.True(changedFirst.Succeeded);
        Assert.Equal(2, first.Notifications.Count);
        Assert.IsType<BpmnModelerReadyEventArgs>(Assert.Single(second.Notifications));
        Assert.Same(secondSnapshot, second.Facade.CaptureDocumentSnapshot().Snapshot);
        var changedSecond = await second.Facade.NewDocumentAsync();
        Assert.True(changedSecond.Succeeded);
        Assert.Equal(2, first.Notifications.Count);
        Assert.Equal(2, second.Notifications.Count);
        Assert.NotEqual(changedFirst.Snapshot!.DocumentId, changedSecond.Snapshot!.DocumentId);
    }

    [Fact]
    public async Task FacadeDisposalSuppressesCallbacksAndLeavesHostDisposalWithChild()
    {
        await using var fixture = new FacadeTestFixture();
        await fixture.InitializeAsync();

        fixture.Facade.Dispose();

        Assert.False(fixture.Host.CaptureState().IsDisposed);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            fixture.Facade.CaptureDocumentSnapshot().Status);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await fixture.Facade.NewDocumentAsync()).Status);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await fixture.Facade.LoadDocumentAsync(null)).Status);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await fixture.Facade.ImportNativeDocumentAsync(ReadOnlyMemory<byte>.Empty)).Status);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await fixture.Facade.ExportNativeDocumentAsync()).Status);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await fixture.Facade.PublishAsync()).Status);
        await fixture.Facade.Notifications.Enqueue(new BpmnModelerReadyEventArgs(
            BpmnModelerComposition.CreateEmptyDocument().CaptureSnapshot()));
        Assert.IsType<BpmnModelerReadyEventArgs>(Assert.Single(fixture.Notifications));
    }

    private static void AssertFacadeReplacement(object notification, DocumentSnapshot snapshot)
    {
        var changed = Assert.IsType<BpmnModelerDocumentChangedEventArgs>(notification);
        Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, changed.Kind);
        Assert.Equal(snapshot, changed.Snapshot);
    }

    private static void AssertFacadeFailure(object notification, BpmnModelerOperation operation)
    {
        var failure = Assert.IsType<BpmnModelerOperationFailedEventArgs>(notification);
        Assert.Equal(operation, failure.Operation);
        Assert.NotEqual(BpmnModelerOperationStatus.Succeeded, failure.Status);
        Assert.NotEmpty(failure.Diagnostics);
    }

    private sealed class FacadeTestFixture : IAsyncDisposable
    {
        internal FacadeTestFixture(
            BpmnModelerCompositionFactory? factory = null,
            Func<Canvas2DRenderer>? replacementRendererFactory = null,
            Func<object, Task>? invokeNotification = null)
        {
            Facade = new BpmnModelerFacade(factory ?? new BpmnModelerCompositionFactory(), value =>
            {
                Notifications.Enqueue(value);
                return invokeNotification?.Invoke(value) ?? Task.CompletedTask;
            });
            Host = CreateHost(
                new RecordingRenderExecution(),
                new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d)),
                compositionFactory: Facade.CompositionFactory,
                toolboxSelection: Selection,
                replacementRendererFactory: replacementRendererFactory ??
                    (() => CreateRenderer(new RecordingRenderExecution())));
            Facade.Attach(Host);
        }

        internal BpmnModelerFacade Facade { get; }

        internal DocumentCanvasHost Host { get; }

        internal ToolboxSelectionState Selection { get; } = new();

        internal ConcurrentQueue<object> Notifications { get; } = new();

        internal ValueTask InitializeAsync() =>
            Host.InitializeAsync("facade-canvas", "facade-standby", "facade-container");

        public async ValueTask DisposeAsync()
        {
            Facade.Dispose();
            await Host.DisposeAsync();
        }
    }
}
