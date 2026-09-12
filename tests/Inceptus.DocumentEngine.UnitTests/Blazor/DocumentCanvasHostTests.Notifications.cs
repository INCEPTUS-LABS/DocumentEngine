using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelerNotificationsStartupEmitsOneReadyWithoutInitialChange(bool useDemo)
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        var factory = useDemo
            ? new BpmnModelerCompositionFactory([BpmnDemoStartupDocumentProvider.Instance])
            : new BpmnModelerCompositionFactory();
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: factory);
        host.ModelerNotifications = notifications;

        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        var ready = Assert.IsType<BpmnModelerReadyEventArgs>(Assert.Single(log));
        Assert.Equal(AttachedDocument(Session(host)).CaptureSnapshot(), ready.Snapshot);
        Assert.Equal(useDemo ? 26 : 0, ready.Snapshot.SemanticModel.ElementCount);
        Assert.Equal(useDemo ? new DocumentRevision(103) : DocumentRevision.Zero,
            ready.Snapshot.Revision);
        Assert.Equal(EditingSessionStatus.Ready, Session(host).CaptureState().Status);
        Assert.True(host.CaptureState().LatestPresentationSucceeded);

        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        await host.ZoomInAsync();
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(log);
    }

    [Fact]
    public async Task ModelerNotificationsStartupFailureEmitsOneBoundedFailureWithoutReady()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(
                [new ThrowingStartupDocumentProvider()]));
        host.ModelerNotifications = notifications;

        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        var failure = Assert.IsType<BpmnModelerOperationFailedEventArgs>(Assert.Single(log));
        Assert.Equal(BpmnModelerOperation.Startup, failure.Operation);
        Assert.Equal(BpmnModelerOperationStatus.Failed, failure.Status);
        Assert.Equal("BPMN_MODELER_STARTUP_FAILED", Assert.Single(failure.Diagnostics).Code);
        Assert.Null(host.CaptureState().Session);
        Assert.Null(host.CaptureState().ValidationSnapshot);
    }

    [Fact]
    public async Task ModelerNotificationsPersistentCreateMovePropertiesUndoRedoAreExactAndOrdered()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(),
            toolboxSelection: selection);
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);

        await CreateNotifiedTaskAsync(host, selection, new PointD(400d, 240d));
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        var created = Assert.Single(ModelerChanges(log));
        var visual = Assert.Single(created.Snapshot.VisualModel.VisualStates);
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId == visual.Id &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var start = Center(body.Bounds);
        var end = start + new VectorD(55d, 35d);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 73);
        await pointer.MoveDocumentPointAsync(scene, end, pointerId: 73, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 73);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, ModelerChanges(log).Length);
        var moved = ModelerChanges(log)[1].Snapshot;
        Assert.NotEqual(visual.Position, Assert.Single(moved.VisualModel.VisualStates).Position);

        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(visual.Id));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        Assert.True(draft.TryGetDataField(new ElementPropertyFieldId("name"), out var name));
        name!.EditorValue = "Notified Task";
        Assert.True(host.UpdatePropertiesFormState(isOpen: true,
            targetVisualStateId: visual.Id, isDirty: true));
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed,
            (await host.ApplyPropertiesAsync(draft)).Status);
        Assert.True(host.UpdatePropertiesFormState(isOpen: false,
            targetVisualStateId: visual.Id, isDirty: false));
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, ModelerChanges(log).Length);

        await host.UndoAsync();
        await host.RedoAsync();
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        var changes = ModelerChanges(log);
        Assert.Equal(5, changes.Length);
        Assert.Equal(new ulong[] { 1, 2, 3, 4, 5 },
            changes.Select(static change => change.Snapshot.Revision.Value));
        Assert.All(changes, static change =>
            Assert.Equal(BpmnModelerDocumentChangeKind.PersistentMutation, change.Kind));
        Assert.Equal(AttachedDocument(session).CaptureSnapshot(), changes[^1].Snapshot);
        Assert.Equal("Notified Task", Assert.Single(changes[^1].Snapshot.SemanticModel.Elements)
            .Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
    }

    [Fact]
    public async Task ModelerNotificationsMetadataOnlyCommitIsReportedWithoutPipelineRebuild()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory());
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var before = host.CaptureState();
        var publication = new DocumentPublicationSnapshot("notification-test", "Notification test");

        var result = await host.SavePublicationAsync(before.DocumentSessionVersion,
            before.Session!.DocumentId, before.Session.DocumentRevision, publication);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Succeeded);
        var change = Assert.Single(ModelerChanges(log));
        Assert.Equal(publication, change.Snapshot.Publication);
        Assert.Equal(before.Session.DocumentRevision.Increment(), change.Snapshot.Revision);
        Assert.Equal(before.SuccessfulRenderCount, host.CaptureState().SuccessfulRenderCount);
        Assert.Equal(BpmnModelerDocumentChangeKind.PersistentMutation, change.Kind);

        var current = host.CaptureState();
        Assert.True((await host.SavePublicationAsync(current.DocumentSessionVersion,
            current.Session!.DocumentId, current.Session.DocumentRevision, publication)).Succeeded);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(ModelerChanges(log));
    }

    [Fact]
    public async Task ModelerNotificationsTransientOperationsNeverEnterDocumentChangeStream()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var snapshot = AttachedDocument(session).CaptureSnapshot();
        var editor = session.CaptureState().EditorState;

        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [BpmnDemoPipeline.TaskVisualId], viewport: editor.Viewport))).Succeeded);
        await host.ZoomInAsync();
        await host.ZoomOutAsync();
        await PointerObserver(host).WheelAsync(deltaX: 35d, deltaY: 65d);
        await PointerObserver(host).MoveDocumentPointAsync(
            session.CaptureState().CurrentScene!, new PointD(-50d, -50d));
        Assert.NotNull(await host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));
        Assert.True(host.UpdatePropertiesFormState(isOpen: false,
            targetVisualStateId: BpmnDemoPipeline.TaskVisualId, isDirty: false));
        Assert.NotNull(await host.ValidateAsync());
        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await session.WaitForIdleAsync();
        await host.ZoomInAsync();
        await host.UndoAsync();
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
        Assert.Empty(ModelerChanges(log));
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
    }

    [Fact]
    public async Task ModelerNotificationsNewLoadImportEachEmitExactlyOneReplacement()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var initialId = Session(host).CaptureState().DocumentId;
        var loaded = CreateImportedDocument("test:p1.3:notification-load", 31).CaptureSnapshot();
        var imported = CreateImportedDocument("test:p1.3:notification-import", 47).CaptureSnapshot();

        Assert.True((await host.NewDiagramAsync()).Succeeded);
        Assert.True((await host.LoadDocumentAsync(loaded)).Succeeded);
        Assert.True((await host.ImportNativeDocumentAsync(
            NativeDocumentSerializer.Export(imported).AsMemory())).Succeeded);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        var changes = ModelerChanges(log);
        Assert.Equal(3, changes.Length);
        Assert.NotEqual(initialId, changes[0].Snapshot.DocumentId);
        Assert.Equal(DocumentRevision.Zero, changes[0].Snapshot.Revision);
        Assert.Equal(loaded, changes[1].Snapshot);
        Assert.Equal(imported, changes[2].Snapshot);
        Assert.All(changes, static change =>
            Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, change.Kind));
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());

        Assert.False((await host.ImportNativeDocumentAsync("{"u8.ToArray())).Succeeded);
        Assert.False((await host.LoadDocumentAsync(null)).Succeeded);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, ModelerChanges(log).Length);
        Assert.Equal(imported, AttachedDocument(Session(host)).CaptureSnapshot());
    }

    [Fact]
    public async Task ModelerNotificationsRetiredSessionAndSourceCannotNotifyAfterReplacement()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var oldSession = Session(host);
        var oldState = oldSession.CaptureState();
        var source = Assert.IsType<BpmnModelerDocumentNotificationSource>(typeof(DocumentCanvasHost)
            .GetField("_documentNotifications", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host));
        Assert.True((await host.NewDiagramAsync()).Succeeded);
        var newDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var delayedSnapshot = CreateImportedDocument(oldState.DocumentId.Value,
            oldState.DocumentRevision.Increment().Value).CaptureSnapshot();

        await source.OnDocumentChangedAsync(new DocumentChangedEvent(oldState.DocumentId,
            oldState.DocumentRevision, delayedSnapshot.Revision,
            AuthoritativeDocumentComponent.Metadata, new CommandTypeId("test:p1.3:delayed"),
            delayedSnapshot));
        typeof(DocumentCanvasHost).GetMethod("HandleSessionStateChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host,
            [oldSession, new EditingSessionStateChangedEventArgs(oldState)]);
        await host.ZoomInAsync();
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(newDocument, Assert.Single(ModelerChanges(log)).Snapshot);
        Assert.Equal(newDocument, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Empty(source.TakePendingThrough(delayedSnapshot.Revision));
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
    }

    [Fact]
    public async Task ModelerNotificationsTwoHostsHaveIndependentDocumentsAndCallbackStreams()
    {
        var logA = new ConcurrentQueue<object>();
        var logB = new ConcurrentQueue<object>();
        using var notificationsA = RecordModelerNotifications(logA);
        using var notificationsB = RecordModelerNotifications(logB);
        var factory = new BpmnModelerCompositionFactory();
        var selectionA = new ToolboxSelectionState();
        var selectionB = new ToolboxSelectionState();
        await using var hostA = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: factory, toolboxSelection: selectionA);
        await using var hostB = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: factory, toolboxSelection: selectionB);
        hostA.ModelerNotifications = notificationsA;
        hostB.ModelerNotifications = notificationsB;
        await hostA.InitializeAsync("a-active", "a-standby", "a-container");
        await hostB.InitializeAsync("b-active", "b-standby", "b-container");
        Assert.NotSame(Session(hostA), Session(hostB));
        Assert.NotSame(AttachedDocument(Session(hostA)), AttachedDocument(Session(hostB)));
        Assert.NotEqual(Session(hostA).CaptureState().DocumentId,
            Session(hostB).CaptureState().DocumentId);

        await CreateNotifiedTaskAsync(hostA, selectionA, new PointD(380d, 240d));
        await notificationsA.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(ModelerChanges(logA));
        Assert.Empty(ModelerChanges(logB));
        Assert.Empty(AttachedDocument(Session(hostB)).SemanticModel.Elements);

        await CreateNotifiedTaskAsync(hostB, selectionB, new PointD(420d, 270d));
        await notificationsB.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(ModelerChanges(logA));
        Assert.Single(ModelerChanges(logB));
        await hostA.UndoAsync();
        await notificationsA.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, ModelerChanges(logA).Length);
        Assert.Single(ModelerChanges(logB));
        Assert.Single(AttachedDocument(Session(hostB)).SemanticModel.Elements);
    }

    [Fact]
    public async Task ModelerNotificationsPendingInvocationPreventsConcurrentReplacementOvertaking()
    {
        var log = new ConcurrentQueue<object>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changeCount = 0;
        using var notifications = new BpmnModelerNotifications(async notification =>
        {
            log.Enqueue(notification);
            if (notification is BpmnModelerDocumentChangedEventArgs &&
                Interlocked.Increment(ref changeCount) == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
        });
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var loaded = CreateImportedDocument("test:p1.3:ordered-load", 53).CaptureSnapshot();
        var newOperation = host.NewDiagramAsync().AsTask();
        Task<NativeDocumentHostImportResult>? loadOperation = null;
        DocumentSnapshot? firstSnapshot = null;
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            firstSnapshot = Assert.Single(ModelerChanges(log)).Snapshot;
            loadOperation = host.LoadDocumentAsync(loaded).AsTask();

            Assert.False(newOperation.IsCompleted);
            Assert.False(loadOperation.IsCompleted);
            Assert.Equal(firstSnapshot, AttachedDocument(Session(host)).CaptureSnapshot());
            Assert.NotEqual(loaded.DocumentId, Session(host).CaptureState().DocumentId);
            Assert.Single(ModelerChanges(log));
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        var firstResult = await newOperation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(loadOperation);
        var secondResult = await loadOperation.WaitAsync(TimeSpan.FromSeconds(10));
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.Equal(firstSnapshot, firstResult.Snapshot);
        Assert.Equal(loaded, secondResult.Snapshot);
        Assert.Equal(loaded, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Collection(ModelerChanges(log),
            change => Assert.Equal(firstSnapshot, change.Snapshot),
            change => Assert.Equal(loaded, change.Snapshot));
    }

    [Fact]
    public async Task ModelerNotificationsCallbackCanStartNextReplacementWithoutChangingFirstResult()
    {
        var log = new ConcurrentQueue<object>();
        DocumentCanvasHost? callbackHost = null;
        Task<NewDiagramHostResult>? nestedOperation = null;
        var startedNested = false;
        using var notifications = new BpmnModelerNotifications(notification =>
        {
            log.Enqueue(notification);
            if (notification is BpmnModelerDocumentChangedEventArgs && !startedNested)
            {
                startedNested = true;
                nestedOperation = callbackHost!.NewDiagramAsync().AsTask();
            }

            // Production invokes the host callback but observes its asynchronous work separately.
            return Task.CompletedTask;
        });
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        callbackHost = host;
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");

        var firstResult = await host.NewDiagramAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(nestedOperation);
        var nestedResult = await nestedOperation.WaitAsync(TimeSpan.FromSeconds(10));
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(firstResult.Succeeded);
        Assert.True(nestedResult.Succeeded);
        Assert.NotNull(firstResult.Snapshot);
        Assert.NotNull(nestedResult.Snapshot);
        Assert.NotEqual(firstResult.Snapshot.DocumentId, nestedResult.Snapshot.DocumentId);
        Assert.Equal(nestedResult.Snapshot, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Collection(ModelerChanges(log),
            change => Assert.Equal(firstResult.Snapshot, change.Snapshot),
            change => Assert.Equal(nestedResult.Snapshot, change.Snapshot));
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
    }

    [Fact]
    public async Task ModelerNotificationsDisposalCancelsWaitingReplacementAndSuppressesLaterDelivery()
    {
        var log = new ConcurrentQueue<object>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var notifications = new BpmnModelerNotifications(async notification =>
        {
            log.Enqueue(notification);
            if (notification is BpmnModelerDocumentChangedEventArgs)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
        });
        var candidateCount = 0;
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(),
            replacementRendererFactory: () =>
            {
                candidateCount++;
                return CreateRenderer(new RecordingRenderExecution());
            });
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var newOperation = host.NewDiagramAsync().AsTask();
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var installed = AttachedDocument(Session(host)).CaptureSnapshot();
            var generation = host.CaptureState().DocumentSessionVersion;
            var loadOperation = host.LoadDocumentAsync(
                CreateImportedDocument("test:p1.3:disposed-waiter", 59).CaptureSnapshot()).AsTask();
            Assert.False(loadOperation.IsCompleted);

            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            var loadResult = await loadOperation.WaitAsync(TimeSpan.FromSeconds(10));
            var newResult = await newOperation.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(NativeDocumentHostOperationStatus.Cancelled, loadResult.Status);
            Assert.True(newResult.Succeeded);
            Assert.Equal(installed, newResult.Snapshot);
            Assert.Equal(generation, host.CaptureState().DocumentSessionVersion);
            Assert.Equal(1, candidateCount);
            Assert.Equal(installed, Assert.Single(ModelerChanges(log)).Snapshot);
            Assert.True(host.CaptureState().IsDisposed);
            Assert.Equal(NewDiagramHostOperationStatus.Cancelled,
                (await host.NewDiagramAsync()).Status);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(ModelerChanges(log));
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
    }

    private static BpmnModelerNotifications RecordModelerNotifications(ConcurrentQueue<object> log) =>
        new(notification =>
        {
            log.Enqueue(notification);
            return Task.CompletedTask;
        });

    private static BpmnModelerDocumentChangedEventArgs[] ModelerChanges(ConcurrentQueue<object> log) =>
        log.OfType<BpmnModelerDocumentChangedEventArgs>().ToArray();

    private static async Task CreateNotifiedTaskAsync(
        DocumentCanvasHost host,
        ToolboxSelectionState selection,
        PointD point)
    {
        var task = BpmnModelerCompositionFactory.ToolboxCatalog.Items.Single(
            static item => item.ElementTypeId == BpmnSemanticTypes.Task);
        Assert.True(selection.Select(task.ItemId));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).ClickCssPointAsync(point);
        await Session(host).WaitForIdleAsync();
    }
}
