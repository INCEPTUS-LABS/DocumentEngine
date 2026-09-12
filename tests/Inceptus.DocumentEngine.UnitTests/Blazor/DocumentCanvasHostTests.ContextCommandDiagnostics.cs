using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedContextCommandRetainsOneVisibleDiagnosticWithoutChangingDocument(
        bool throughComponent)
    {
        var snapshot = CreateContextDiagnosticSnapshot(ulong.MaxValue);
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("context-diagnostic-canvas", "context-diagnostic-standby",
            "context-diagnostic-container");
        var session = Session(host);
        await OpenContextDiagnosticAnchorMenuAsync(host);
        var before = host.CaptureDocumentSnapshot().Snapshot!;
        var history = session.CaptureState().HistoryStatus;
        var activator = new PublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());

        if (throughComponent)
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var operation = typeof(DocumentCanvas).GetMethod(
                    "ExecuteConnectorAnchorContextActionAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                await Assert.IsAssignableFrom<Task>(operation.Invoke(
                    activator.Component, [ConnectorAnchorRole.Source]));
                await activator.Component.RefreshAsync();
            });
        }
        else
        {
            var result = await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source);
            Assert.NotNull(result);
            Assert.False(result.IsCommitted);
            Assert.Equal("CMD_REVISION_PREPARATION_FAILURE", Assert.Single(result.Diagnostics).Code);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                typeof(DocumentCanvas).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(activator.Component, host.CaptureState());
                await activator.Component.RefreshAsync();
            });
        }
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("role=\"alert\"", markup, StringComparison.Ordinal);
        var diagnostic = Assert.Single(host.CaptureState().InteractionDiagnostics);
        Assert.Equal("CMD_REVISION_PREPARATION_FAILURE", diagnostic.Code);
        Assert.Contains(diagnostic.Message, markup, StringComparison.Ordinal);
        Assert.Contains("data-interaction-diagnostic-code=\"CMD_REVISION_PREPARATION_FAILURE\"",
            markup, StringComparison.Ordinal);
        Assert.Equal(1, markup.Split(diagnostic.Message, StringSplitOptions.None).Length - 1);
        Assert.Same(before, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(snapshot, before);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Empty(ModelerChanges(log));
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(host.CaptureState().HostDiagnostics);
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Null(host.CaptureState().ContextMenu);

        // The consumed menu is a legitimate no-op and cannot duplicate feedback.
        Assert.Null(await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source));
        Assert.Same(diagnostic, Assert.Single(host.CaptureState().InteractionDiagnostics));
        Assert.Same(before, host.CaptureDocumentSnapshot().Snapshot);

        var replacement = CreateContextDiagnosticSnapshot(7);
        var loaded = await host.LoadDocumentAsync(replacement);
        Assert.True(loaded.Succeeded, NewDiagramDiagnostics(loaded.Diagnostics));
        Assert.NotSame(session, Session(host));
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        await OpenContextDiagnosticAnchorMenuAsync(host);
        var accepted = await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source);
        Assert.True(accepted?.IsCommitted);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        var current = host.CaptureDocumentSnapshot().Snapshot!;
        Assert.Equal(replacement.Revision.Increment(), current.Revision);
        Assert.Equal(replacement.Publication, current.Publication);
        Assert.Equal(1, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Equal(2, ModelerChanges(log).Length);
        Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, ModelerChanges(log)[0].Kind);
        Assert.Equal(BpmnModelerDocumentChangeKind.PersistentMutation, ModelerChanges(log)[1].Kind);
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
    }

    private static async Task OpenContextDiagnosticAnchorMenuAsync(DocumentCanvasHost host)
    {
        var session = Session(host);
        var target = new VisualStateId("test:anchor-context:visual:source-task");
        var pointer = ActiveReplacementPointerObserver(host);
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId == target &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        await pointer.ClickDocumentPointAsync(scene, Center(body.Bounds));
        scene = session.CaptureState().CurrentScene!;
        body = scene.Items.Single(item => item.Origin.VisualStateId == target &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var edge = new PointD(body.Bounds.Left + (body.Bounds.Width * 0.5d), body.Bounds.Top);
        await pointer.MoveDocumentPointAsync(scene, edge);
        await pointer.ContextMenuDocumentPointAsync(session.CaptureState().CurrentScene!, edge);
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(menu.ConnectorAnchorAction);
        Assert.Equal(target, action.TargetVisualStateId);
        Assert.True(action.CanAdd(ConnectorAnchorRole.Source));
        await pointer.LeaveAsync();
        Assert.Same(menu, host.CaptureState().ContextMenu);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledContextCommandDoesNotPublishRejectionFeedback(bool cancelWhileWaiting)
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(
                initialDocument: CreateContextDiagnosticSnapshot(ulong.MaxValue)));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("cancel-context-canvas", "cancel-context-container");
        await OpenContextDiagnosticAnchorMenuAsync(host);
        var session = Session(host);
        var snapshot = host.CaptureDocumentSnapshot().Snapshot;
        var history = session.CaptureState().HistoryStatus;
        using var cancellation = new CancellationTokenSource();
        if (cancelWhileWaiting)
        {
            var commandGate = Assert.IsType<SemaphoreSlim>(typeof(EditingSession)
                .GetField("_commandGate", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(session));
            await commandGate.WaitAsync();
            try
            {
                var operation = host.ExecuteConnectorAnchorContextActionAsync(
                    ConnectorAnchorRole.Source, cancellation.Token);
                Assert.False(operation.IsCompleted);
                Assert.Null(host.CaptureState().ContextMenu);
                cancellation.Cancel();
                var result = await operation;
                Assert.NotNull(result);
                Assert.Equal(HistoryOperationStatus.Cancelled, result.Status);
            }
            finally
            {
                commandGate.Release();
            }
        }
        else
        {
            cancellation.Cancel();
            Assert.Null(await host.ExecuteConnectorAnchorContextActionAsync(
                ConnectorAnchorRole.Source, cancellation.Token));
            Assert.NotNull(host.CaptureState().ContextMenu);
        }

        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(snapshot, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Empty(host.CaptureState().HostDiagnostics);
        Assert.Empty(ModelerChanges(log));
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
    }

    [Theory]
    [InlineData("closed-menu")]
    [InlineData("stale-revision")]
    [InlineData("replacement")]
    [InlineData("disposed")]
    public async Task InactiveContextCommandRemainsANoOpWithoutFailureFeedback(string reason)
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(
                initialDocument: CreateContextDiagnosticSnapshot(7)),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("inactive-context-canvas", "inactive-context-standby",
            "inactive-context-container");
        await OpenContextDiagnosticAnchorMenuAsync(host);
        var originalSession = Session(host);
        var originalDocument = AttachedDocument(originalSession);
        if (reason == "closed-menu")
        {
            host.CloseContextMenu();
        }
        else if (reason == "stale-revision")
        {
            var current = host.CaptureDocumentSnapshot().Snapshot!;
            var target = current.VisualModel.VisualStates[0];
            Assert.True((await originalSession.ExecuteAsync(new MoveVisualStateCommand(
                current.DocumentId, current.Revision, target.Id,
                target.Position + new VectorD(10d, 10d), VisualPlacementMode.Pinned))).IsCommitted);
            await originalSession.WaitForIdleAsync();
            await host.ZoomInAsync();
        }
        else if (reason == "replacement")
        {
            Assert.True((await host.LoadDocumentAsync(CreateContextDiagnosticSnapshot(12))).Succeeded);
            Assert.NotSame(originalSession, Session(host));
        }
        else
        {
            await host.DisposeAsync();
        }
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        var session = Session(host);
        var document = reason == "disposed" ? originalDocument : AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var callbacks = log.Count;

        Assert.Null(await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source));

        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Equal(callbacks, log.Count);
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Empty(host.CaptureState().HostDiagnostics);
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
    }

    private static DocumentSnapshot CreateContextDiagnosticSnapshot(ulong revisionValue)
    {
        var original = CreateStandaloneAnchorContextSnapshot(withPublication: true);
        var revision = new DocumentRevision(revisionValue);
        var id = original.DocumentId;
        return new DocumentSnapshot(
            new SemanticModelSnapshot(id, revision, original.SemanticModel.Elements),
            new VisualModelSnapshot(id, revision, original.VisualModel.VisualStates),
            new DocumentMetadataSnapshot(id, revision,
                extensionProperties: [new("test:context-diagnostic", PropertyValue.FromText("preserve me"))]),
            original.Publication);
    }
}
