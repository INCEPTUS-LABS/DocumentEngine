using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("source-task", ConnectorAnchorRole.Source, false)]
    [InlineData("target-task", ConnectorAnchorRole.Target, false)]
    [InlineData("start", ConnectorAnchorRole.Source, false)]
    [InlineData("end", ConnectorAnchorRole.Target, false)]
    [InlineData("source-task", ConnectorAnchorRole.Source, true)]
    [InlineData("target-task", ConnectorAnchorRole.Target, true)]
    [InlineData("start", ConnectorAnchorRole.Source, true)]
    [InlineData("end", ConnectorAnchorRole.Target, true)]
    public async Task NodeAnchorContextActionOnStandaloneBpmnNodeSurvivesLeaveAndCommitsOnce(
        string nodeName,
        ConnectorAnchorRole role,
        bool withPublication) =>
        await AssertNodeAnchorContextActionAsync(CreateStandaloneAnchorContextSnapshot(withPublication),
            new VisualStateId($"test:anchor-context:visual:{nodeName}"), nodeName, role);

    [Theory]
    [InlineData("task", ConnectorAnchorRole.Source)]
    [InlineData("task", ConnectorAnchorRole.Target)]
    [InlineData("start", ConnectorAnchorRole.Source)]
    [InlineData("end", ConnectorAnchorRole.Target)]
    public async Task NodeAnchorContextActionOnConnectedConsumerFixtureSurvivesLeaveAndCommitsOnce(
        string nodeName,
        ConnectorAnchorRole role) =>
        await AssertNodeAnchorContextActionAsync(CreateConnectedAnchorContextSnapshot(),
            new VisualStateId($"consumer:fixture:visual:{nodeName}"), nodeName, role);

    private static async Task AssertNodeAnchorContextActionAsync(
        DocumentSnapshot snapshot,
        VisualStateId target,
        string nodeName,
        ConnectorAnchorRole role)
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var otherLog = new ConcurrentQueue<object>();
        using var otherNotifications = RecordModelerNotifications(otherLog);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1.25d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot));
        await using var otherHost = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(640d, 420d, 1.25d)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot));
        host.ModelerNotifications = notifications;
        otherHost.ModelerNotifications = otherNotifications;
        await host.InitializeAsync("active", "standby", "container");
        await otherHost.InitializeAsync("other-active", "other-standby", "other-container");
        var otherBefore = otherHost.CaptureDocumentSnapshot().Snapshot;
        var otherStateBefore = otherHost.CaptureState().Session!;
        Assert.NotSame(Session(host), Session(otherHost));
        Assert.True(host.CaptureState().LatestPresentationSucceeded,
            string.Join("; ", host.CaptureState().HostDiagnostics
                .Concat(host.CaptureState().Session!.RuntimeDiagnostics)
                .Concat(host.CaptureState().Session!.PresentationDiagnostics)
                .Select(static item => $"{item.Code}: {item.Message}")));
        var session = Session(host);
        var pointer = PointerObserver(host);
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId == target &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        await pointer.ClickDocumentPointAsync(scene, Center(body.Bounds));
        scene = session.CaptureState().CurrentScene!;
        body = scene.Items.Single(item => item.Origin.VisualStateId == target &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var edgePoint = new PointD(body.Bounds.Left + (body.Bounds.Width * 0.5d), body.Bounds.Top);
        await pointer.MoveDocumentPointAsync(scene, edgePoint);
        await pointer.ContextMenuDocumentPointAsync(session.CaptureState().CurrentScene!, edgePoint);
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(menu.ConnectorAnchorAction);
        Assert.Equal(target, action.TargetVisualStateId);
        Assert.Equal(ConnectorAnchorSide.Top, action.Side);
        Assert.True(action.CanAdd(role));
        if (nodeName is "start" or "end")
        {
            Assert.False(action.CanAdd(role == ConnectorAnchorRole.Source
                ? ConnectorAnchorRole.Target : ConnectorAnchorRole.Source));
        }

        var before = host.CaptureDocumentSnapshot().Snapshot!;
        var beforeVisual = before.VisualModel.VisualStates.Single(visual => visual.Id == target);
        Assert.DoesNotContain(beforeVisual.ConnectorAnchors,
            static anchor => anchor.Side == ConnectorAnchorSide.Top);
        Assert.True(action.IsCurrent(session.CaptureState().CurrentScene!, beforeVisual),
            "The offered node anchor action must be current immediately after menu acquisition.");
        await pointer.LeaveAsync();
        Assert.Same(menu, host.CaptureState().ContextMenu);
        Assert.Null(session.CaptureState().EditorState.HoveredObjectId);
        Assert.True(action.IsCurrent(session.CaptureState().CurrentScene!, beforeVisual),
            "Clearing hover when the pointer enters the context menu must preserve its node target.");
        Assert.Equal(before, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Empty(ModelerChanges(log));

        var result = await host.ExecuteConnectorAnchorContextActionAsync(role);

        Assert.True(result?.IsCommitted, result is null ? "Host rejected the captured anchor action." :
            string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var after = host.CaptureDocumentSnapshot().Snapshot!;
        var afterVisual = after.VisualModel.VisualStates.Single(visual => visual.Id == target);
        var anchor = Assert.Single(afterVisual.ConnectorAnchors,
            candidate => !beforeVisual.ConnectorAnchors.Any(original => original.Id == candidate.Id));
        Assert.Equal(beforeVisual.ConnectorAnchors.Length + 1, afterVisual.ConnectorAnchors.Length);
        Assert.True(beforeVisual.ConnectorAnchors.SequenceEqual(
            afterVisual.ConnectorAnchors.Where(candidate => candidate.Id != anchor.Id)));
        Assert.Equal(role, anchor.Role);
        Assert.Equal(ConnectorAnchorSide.Top, anchor.Side);
        Assert.Equal(0, anchor.Order);
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(before.Publication, after.Publication);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(after, Assert.Single(ModelerChanges(log)).Snapshot);
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
        Assert.True(before.SemanticModel.Elements.SequenceEqual(after.SemanticModel.Elements));
        Assert.True(before.SemanticModel.Relationships.SequenceEqual(after.SemanticModel.Relationships));
        Assert.Equal(beforeVisual.Position, afterVisual.Position);
        Assert.Equal(beforeVisual.Size, afterVisual.Size);
        Assert.Equal(before.VisualModel.VisualStates.Where(visual => visual.Id != target),
            after.VisualModel.VisualStates.Where(visual => visual.Id != target));
        Assert.Null(host.CaptureState().ContextMenu);

        await host.UndoAsync();
        Assert.Equal(before.Publication, host.CaptureDocumentSnapshot().Snapshot!.Publication);
        Assert.True(beforeVisual.ConnectorAnchors.SequenceEqual(host.CaptureDocumentSnapshot().Snapshot!
            .VisualModel.VisualStates.Single(visual => visual.Id == target).ConnectorAnchors));
        Assert.Equal(2, ModelerChanges(log).Length);
        await host.RedoAsync();
        Assert.Equal(before.Publication, host.CaptureDocumentSnapshot().Snapshot!.Publication);
        Assert.True(afterVisual.ConnectorAnchors.SequenceEqual(host.CaptureDocumentSnapshot().Snapshot!
            .VisualModel.VisualStates.Single(visual => visual.Id == target).ConnectorAnchors));
        Assert.Equal(3, ModelerChanges(log).Length);
        Assert.Equal(otherBefore, otherHost.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(otherStateBefore.HistoryStatus, otherHost.CaptureState().Session!.HistoryStatus);
        Assert.Equal(otherStateBefore.EditorState, otherHost.CaptureState().Session!.EditorState);
        Assert.Single(otherLog.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(ModelerChanges(otherLog));
        Assert.Empty(otherLog.OfType<BpmnModelerOperationFailedEventArgs>());
    }

    private static DocumentSnapshot CreateStandaloneAnchorContextSnapshot(bool withPublication)
    {
        var id = new DocumentId("test:anchor-context:document");
        var revision = new DocumentRevision(7);
        var nodes = new[]
        {
            (Name: "source-task", Type: "BPMN.Task", X: 100d, Y: 100d, Width: 120d, Height: 80d),
            (Name: "target-task", Type: "BPMN.Task", X: 380d, Y: 100d, Width: 120d, Height: 80d),
            (Name: "start", Type: "BPMN.StartEvent", X: 100d, Y: 300d, Width: 40d, Height: 40d),
            (Name: "end", Type: "BPMN.EndEvent", X: 380d, Y: 300d, Width: 40d, Height: 40d),
        };
        return new DocumentSnapshot(
            new SemanticModelSnapshot(id, revision, nodes.Select(node =>
                new SemanticElementSnapshot(new SemanticElementId($"test:anchor-context:{node.Name}"),
                    new SemanticTypeId(node.Type),
                    [new("BPMN.Name", PropertyValue.FromText(node.Name))]))),
            new VisualModelSnapshot(id, revision, nodes.Select(node =>
                new VisualStateSnapshot(new VisualStateId($"test:anchor-context:visual:{node.Name}"),
                    new SemanticElementId($"test:anchor-context:{node.Name}"),
                    new PointD(node.X, node.Y), new SizeD(node.Width, node.Height),
                    VisualPlacementMode.Pinned))),
            new DocumentMetadataSnapshot(id, revision),
            withPublication ? new DocumentPublicationSnapshot("anchor-context", "Anchor context",
                "Publication metadata must survive a visual-only anchor edit.") : null);
    }

    // Preserve the reported consumer's native identities, anchors and routes without
    // depending on its disposable host or preparing the anchor exercised by the test.
    private static DocumentSnapshot CreateConnectedAnchorContextSnapshot()
    {
        var documentId = new DocumentId("consumer:fixture:document");
        var revision = new DocumentRevision(7);
        var startId = new SemanticElementId("consumer:fixture:start");
        var taskId = new SemanticElementId("consumer:fixture:task");
        var endId = new SemanticElementId("consumer:fixture:end");
        var firstFlowId = new SemanticElementId("consumer:fixture:start-to-task");
        var secondFlowId = new SemanticElementId("consumer:fixture:task-to-end");
        var startSource = new ConnectorAnchorId("consumer:fixture:start:source");
        var taskTarget = new ConnectorAnchorId("consumer:fixture:task:target");
        var taskSource = new ConnectorAnchorId("consumer:fixture:task:source");
        var endTarget = new ConnectorAnchorId("consumer:fixture:end:target");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision,
            [
                new SemanticElementSnapshot(startId, new SemanticTypeId("BPMN.StartEvent"),
                    [new("BPMN.Name", PropertyValue.FromText("Request received"))]),
                new SemanticElementSnapshot(taskId, new SemanticTypeId("BPMN.Task"),
                    [new("BPMN.Name", PropertyValue.FromText("Review request"))]),
                new SemanticElementSnapshot(endId, new SemanticTypeId("BPMN.EndEvent"),
                    [new("BPMN.Name", PropertyValue.FromText("Request completed"))]),
            ],
            [
                new SemanticRelationshipSnapshot(firstFlowId, new SemanticTypeId("BPMN.SequenceFlow"),
                    startId, taskId),
                new SemanticRelationshipSnapshot(secondFlowId, new SemanticTypeId("BPMN.SequenceFlow"),
                    taskId, endId),
            ]),
            new VisualModelSnapshot(documentId, revision,
            [
                new VisualStateSnapshot(new VisualStateId("consumer:fixture:visual:start"),
                    startId, new PointD(100d, 140d), new SizeD(40d, 40d), VisualPlacementMode.Pinned,
                    connectorAnchors: [new ConnectorAnchor(startSource, ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source, 0)]),
                new VisualStateSnapshot(new VisualStateId("consumer:fixture:visual:task"),
                    taskId, new PointD(260d, 120d), new SizeD(120d, 80d), VisualPlacementMode.Pinned,
                    connectorAnchors:
                    [
                        new ConnectorAnchor(taskTarget, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0),
                        new ConnectorAnchor(taskSource, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                    ]),
                new VisualStateSnapshot(new VisualStateId("consumer:fixture:visual:end"),
                    endId, new PointD(500d, 140d), new SizeD(40d, 40d), VisualPlacementMode.Pinned,
                    connectorAnchors: [new ConnectorAnchor(endTarget, ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target, 0)]),
                new VisualStateSnapshot(new VisualStateId("consumer:fixture:visual:start-to-task"),
                    firstFlowId, default, default, VisualPlacementMode.Automatic,
                    [new PointD(140d, 160d), new PointD(260d, 160d)],
                    sourceAnchorId: startSource, targetAnchorId: taskTarget),
                new VisualStateSnapshot(new VisualStateId("consumer:fixture:visual:task-to-end"),
                    secondFlowId, default, default, VisualPlacementMode.Automatic,
                    [new PointD(380d, 160d), new PointD(500d, 160d)],
                    sourceAnchorId: taskSource, targetAnchorId: endTarget),
            ]),
            new DocumentMetadataSnapshot(documentId, revision),
            new DocumentPublicationSnapshot("consumer-request-review", "Consumer request review",
                "Independent NuGet consumer Start-Task-End fixture."));
    }
}
