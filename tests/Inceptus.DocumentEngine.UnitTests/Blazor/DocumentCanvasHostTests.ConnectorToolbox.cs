using System.Net;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task ConnectorToolboxRequiresCompletedClickAndSmartEditAndSurvivesHover()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("connector-toolbox", "container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var path = ConnectorToolboxPath(scene);
        var id = path.Origin.VisualStateId!;
        var point = ConnectorToolboxPoint(path);
        await A24Select(host, id);
        Assert.Null(host.CaptureState().ElementToolbox);
        var observer = PointerObserver(host);
        scene = session.CaptureState().CurrentScene!;
        await observer.DownDocumentPointAsync(scene, point);
        Assert.Null(host.CaptureState().ElementToolbox);
        await observer.UpDocumentPointAsync(scene, point, 1);
        var toolbox = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        Assert.True(toolbox.IsConnector);
        Assert.Empty(toolbox.Candidates);
        Assert.Null(toolbox.OpenSubProcessScopeId);
        Assert.True(toolbox.CanDelete);
        Assert.True(toolbox.ActionsPosition.Y + 58 <= toolbox.Anchor.Y);
        Assert.Null(await host.AutoPointsAsync());
        Assert.Null(await host.SmartCreateAsync(new("bpmn:toolbox:task")));
        await observer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new(10, 10));
        Assert.Equal(toolbox, host.CaptureState().ElementToolbox);
        var properties = await host.OpenPropertiesAsync(id, true);
        Assert.NotNull(properties);
        Assert.Null(host.CaptureState().ElementToolbox);
        host.UpdatePropertiesFormState(false, null, false);
        Assert.Null(host.CaptureState().ElementToolbox);
        await observer.ClickDocumentPointAsync(session.CaptureState().CurrentScene!, point);
        Assert.NotNull(host.CaptureState().ElementToolbox);
        host.SetSmartEditEnabled(false);
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Null(await host.DeleteToolboxElementAsync());
        Assert.Null(await host.OpenPropertiesAsync(id, true));
        await observer.ClickDocumentPointAsync(session.CaptureState().CurrentScene!, point);
        Assert.Null(host.CaptureState().ElementToolbox);
        host.SetSmartEditEnabled(true);
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("drag")]
    [InlineData("control")]
    public async Task ConnectorToolboxDoesNotOpenAfterNonClickGestures(string gesture)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("connector-no-click", "container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var point = ConnectorToolboxPoint(ConnectorToolboxPath(scene));
        var observer = PointerObserver(host);
        await observer.DownDocumentPointAsync(scene, point, controlKey: gesture == "control");
        if (gesture == "cancel") await observer.CancelAsync(1);
        if (gesture == "drag")
        {
            await observer.MoveDocumentPointAsync(scene, new(point.X + 25, point.Y + 25), buttons: 1);
            await observer.MoveDocumentPointAsync(scene, point, buttons: 1);
        }
        await observer.UpDocumentPointAsync(scene, point, 1, controlKey: gesture == "control");
        Assert.Null(host.CaptureState().ElementToolbox);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectorToolboxDeleteUsesHistoryAndRestoresExactConnection(bool message)
    {
        await using var host = message ? A22ConnectionHost(new ToolboxSelectionState()) : A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("connector-delete", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        VisualStateId? id = null;
        if (message)
        {
            id = new("toolbox:message:visual");
            Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
                new("toolbox:message"), id, A22ViewTaskId, A22ToolPeerTask,
                A22SourceAnchor(A22ViewTaskVisualId), new(A22ToolPeerVisual.Value + ":target")))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var scene = session.CaptureState().CurrentScene!;
        var path = ConnectorToolboxPath(scene, id);
        id = path.Origin.VisualStateId!;
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        await PointerObserver(host).ClickDocumentPointAsync(scene, ConnectorToolboxPoint(path));
        Assert.True(host.CaptureState().ElementToolbox?.IsConnector);
        Assert.True((await host.DeleteToolboxElementAsync())?.IsCommitted);
        Assert.DoesNotContain(document.VisualModel.VisualStates, visual => visual.Id == id);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        await host.UndoAsync();
        Assert.Equal(before.VisualModel.VisualStates.OrderBy(visual => visual.Id.Value),
            document.VisualModel.VisualStates.OrderBy(visual => visual.Id.Value));
        Assert.Equal(before.VisualModel.EnumerateConnectorRoutingRecords().OrderBy(route => route.VisualStateId.Value),
            document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().OrderBy(route => route.VisualStateId.Value));
        Assert.Equal(before.SemanticModel.Relationships.OrderBy(flow => flow.Id.Value), document.SemanticModel.Relationships.OrderBy(flow => flow.Id.Value));
        Assert.Null(host.CaptureState().ElementToolbox);
        await host.RedoAsync();
        Assert.DoesNotContain(document.VisualModel.VisualStates, visual => visual.Id == id);
    }

    [Fact]
    public async Task ConnectorToolboxMarkupContainsExactlyTwoAccessibleActions()
    {
        using var culture = new ModelerCultureScope("en-GB");
        using var services = PublishComponentServices(new PublishComponentActivator(null!), new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var state = new ElementToolboxState(new("connector"), default, new(10, 10), default, 0, false, true, []) { IsConnector = true };
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ElementToolbox>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ElementToolbox.State)] = state })));
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Equal(2, Regex.Count(markup, "<button "));
        Assert.Contains("connector-actions", markup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Properties\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Delete\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("element-creation", markup, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConnectorToolboxSequenceCases))]
    public async Task ConnectorToolboxSequenceDeleteHandlesPathOrientationAndRestoresExactState(
        string orientation, ConnectorRoutingType routingType, bool partlyVisible)
    {
        await using var host = A23Host(ConnectorToolboxOrientationSnapshot(orientation));
        await host.InitializeAsync("connector-orientation", "container");
        Assert.True(host.CaptureState().IsInitialized, string.Join("; ", host.CaptureState().HostDiagnostics.Select(d => d.Code + ": " + d.Message)));
        var session = Session(host);
        var document = AttachedDocument(session);
        if (routingType != ConnectorRoutingType.Automatic)
        {
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
                document.Revision, new("orientation:flow-visual"), routingType))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var scene = session.CaptureState().CurrentScene!;
        var path = ConnectorToolboxPath(scene);
        var id = path.Origin.VisualStateId!;
        Assert.Equal(orientation == "vertical", path.Bounds.Width == 0);
        Assert.Equal(orientation == "horizontal", path.Bounds.Height == 0);
        if (partlyVisible)
        {
            var bounds = path.Bounds;
            var pan = orientation == "vertical" ? new VectorD(100 - bounds.Left * .5, -(bounds.Top + bounds.Height * .1) * .5) :
                new VectorD(-(bounds.Left + bounds.Width * .1) * .5, 100 - bounds.Top * .5);
            await session.UpdateViewportAsync(new ViewportSnapshot(.5, pan));
            await session.WaitForIdleAsync();
            scene = session.CaptureState().CurrentScene!;
            path = ConnectorToolboxPath(scene);
        }
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        await PointerObserver(host).ClickDocumentPointAsync(scene, ConnectorToolboxPoint(path));
        Assert.True(host.CaptureState().ElementToolbox is { IsConnector: true, CanDelete: true });
        Assert.True((await host.DeleteToolboxElementAsync())?.IsCommitted);
        Assert.Empty(document.SemanticModel.Relationships);
        Assert.DoesNotContain(document.VisualModel.VisualStates, visual => visual.Id == id);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        await host.UndoAsync();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        Assert.Empty(document.SemanticModel.Relationships);
        Assert.DoesNotContain(document.VisualModel.VisualStates, visual => visual.Id == id);
    }

    public static TheoryData<string, ConnectorRoutingType, bool> ConnectorToolboxSequenceCases
    {
        get
        {
            var cases = new TheoryData<string, ConnectorRoutingType, bool>();
            foreach (var orientation in new[] { "horizontal", "vertical", "diagonal" })
                foreach (var mode in new[] { ConnectorRoutingType.Automatic, ConnectorRoutingType.Straight, ConnectorRoutingType.Manual })
                    foreach (var partlyVisible in new[] { false, true })
                        cases.Add(orientation, mode, partlyVisible);
            return cases;
        }
    }

    [Theory]
    [InlineData("horizontal", -3000, 0)]
    [InlineData("horizontal", 0, -3000)]
    [InlineData("vertical", -3000, 0)]
    [InlineData("vertical", 0, -3000)]
    [InlineData("diagonal", -3000, 0)]
    [InlineData("diagonal", 0, -3000)]
    public async Task ConnectorToolboxDeleteStillRejectsOffscreenPaths(string orientation, double x, double y)
    {
        await using var host = A23Host(ConnectorToolboxOrientationSnapshot(orientation));
        await host.InitializeAsync("connector-offscreen", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(.5, new(x, y)));
        await session.WaitForIdleAsync();
        var scene = session.CaptureState().CurrentScene!;
        // A stale/outside-surface click must not bypass the operation's viewport guard.
        await PointerObserver(host).ClickDocumentPointAsync(scene, ConnectorToolboxPoint(ConnectorToolboxPath(scene)));
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.Null(await host.DeleteToolboxElementAsync());
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual)]
    public async Task ConnectorToolboxMessageDeleteUndoPreservesScopeRouting(ConnectorRoutingType routingType)
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("connector-message-history", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var id = new VisualStateId("toolbox:message:history");
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            A22ToolPeerVisual, new(250, 600)))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            new("toolbox:message:history-flow"), id, A22ViewTaskId, A22ToolPeerTask,
            A22SourceAnchor(A22ViewTaskVisualId), new(A22ToolPeerVisual.Value + ":target"), routingType))).IsCommitted);
        await session.WaitForIdleAsync();
        if (routingType == ConnectorRoutingType.Manual)
        {
            var manual = Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords());
            Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId, document.Revision,
                id, [manual.Path[0], new(450, 450), new(500, 500), manual.Path[^1]]))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var before = document.CaptureSnapshot();
        var route = Assert.Single(before.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors), record => record.VisualStateId == id);
        Assert.Equal(routingType, route.RoutingType);
        var scene = session.CaptureState().CurrentScene!;
        var path = ConnectorToolboxPath(scene, id);
        Assert.False(path.Bounds.IsEmpty);
        await PointerObserver(host).ClickDocumentPointAsync(scene, ConnectorToolboxPoint(path));
        Assert.True((await host.DeleteToolboxElementAsync())?.IsCommitted);
        Assert.DoesNotContain(document.SemanticModel.Relationships, flow => flow.Id.Value == "toolbox:message:history-flow");
        await host.UndoAsync();
        var restored = Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords(), record => record.VisualStateId == id);
        Assert.Equal(routingType, restored.RoutingType);
        Assert.Equal(route, restored);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        await host.RedoAsync();
        Assert.DoesNotContain(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords(), record => record.VisualStateId == id);
        await host.UndoAsync();
        Assert.Equal(route, Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords(), record => record.VisualStateId == id));
    }

    [Theory]
    [InlineData("diagonal", ConnectorRoutingType.Automatic)]
    [InlineData("vertical", ConnectorRoutingType.Straight)]
    [InlineData("diagonal", ConnectorRoutingType.Straight)]
    [InlineData("diagonal", ConnectorRoutingType.Manual)]
    public async Task ConnectorToolboxMessageDeleteHandlesPathOrientation(string orientation, ConnectorRoutingType routingType)
    {
        await using var host = A23Host(ConnectorToolboxOrientationSnapshot(orientation, message: true));
        await host.InitializeAsync("message-orientation", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var view = session.CaptureState().ModelProfileViewState;
        Assert.True((await session.UpdateModelProfileViewStateAsync(view.WithPreferredVisibility(OrganizationalModelProfile.Id, false))).Succeeded);
        await session.WaitForIdleAsync();
        var id = new VisualStateId("orientation:flow-visual");
        Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            new("orientation:flow"), id, A22ViewTaskId, A22ToolPeerTask,
            new("orientation:source"), new("orientation:target"), routingType))).IsCommitted);
        await session.WaitForIdleAsync();
        var scene = session.CaptureState().CurrentScene!;
        var path = ConnectorToolboxPath(scene, id);
        Assert.Equal(orientation == "vertical", path.Bounds.Width == 0);
        Assert.Equal(orientation == "horizontal", path.Bounds.Height == 0);
        var before = document.CaptureSnapshot();
        await PointerObserver(host).ClickDocumentPointAsync(scene, ConnectorToolboxPoint(path));
        Assert.True(host.CaptureState().ElementToolbox is { IsConnector: true, CanDelete: true });
        Assert.True((await host.DeleteToolboxElementAsync())?.IsCommitted);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        await host.RedoAsync();
        Assert.DoesNotContain(document.VisualModel.VisualStates, visual => visual.Id == id);
    }

    private static DocumentSnapshot ConnectorToolboxOrientationSnapshot(string orientation, bool message = false)
    {
        var source = message ? A22ViewSnapshot() : A23Snapshot([("source", "target")]);
        var sourceId = message ? A22ViewTaskId : new SemanticElementId("source");
        var targetId = message ? A22ToolPeerTask : new SemanticElementId("target");
        var flowId = new SemanticElementId("orientation:flow");
        var sourceAnchor = new ConnectorAnchorId("orientation:source");
        var targetAnchor = new ConnectorAnchorId("orientation:target");
        var vertical = orientation == "vertical";
        var semantic = source.SemanticModel;
        var visuals = source.VisualModel.VisualStates.Where(visual =>
            semantic.Elements.Any(element => element.Id == visual.SemanticElementId)).Select(visual =>
            visual.SemanticElementId == sourceId || visual.SemanticElementId == targetId
                ? new VisualStateSnapshot(visual.Id, visual.SemanticElementId,
                    visual.SemanticElementId == sourceId ? new(100, 100) : vertical ? new(100, 400) :
                        orientation == "horizontal" ? new(500, 100) : new(500, 400),
                    new(120, 80), VisualPlacementMode.Pinned, connectorAnchors:
                    [new(visual.SemanticElementId == sourceId ? sourceAnchor : targetAnchor,
                        visual.SemanticElementId == sourceId ? vertical ? ConnectorAnchorSide.Bottom : ConnectorAnchorSide.Right :
                            vertical ? ConnectorAnchorSide.Top : ConnectorAnchorSide.Left,
                        visual.SemanticElementId == sourceId ? ConnectorAnchorRole.Source : ConnectorAnchorRole.Target, 0)])
                : visual);
        if (!message)
            visuals = visuals.Append(new VisualStateSnapshot(new("orientation:flow-visual"), flowId, default, default,
                VisualPlacementMode.Automatic, sourceAnchorId: sourceAnchor, targetAnchorId: targetAnchor));
        return new(new SemanticModelSnapshot(source.DocumentId, source.Revision, semantic.Elements,
                message ? [] : [new(flowId, BpmnSemanticTypes.SequenceFlow, sourceId, targetId)],
                semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments),
            new VisualModelSnapshot(source.DocumentId, source.Revision, visuals), source.Metadata);
    }

    private static Canvas2DSceneItem ConnectorToolboxPath(Canvas2DScene scene, VisualStateId? id = null) =>
        scene.Items.First(item => item.Layer == Canvas2DSceneLayer.Connector &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path && item.Geometry.Points.Length >= 2 &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
            (id is null || item.Origin.VisualStateId == id));

    private static PointD ConnectorToolboxPoint(Canvas2DSceneItem path)
    {
        var segment = path.Geometry.Points.Zip(path.Geometry.Points.Skip(1))
            .OrderByDescending(pair => Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Y - pair.Second.Y)).First();
        return path.Transform.TransformPoint(new((segment.First.X + segment.Second.X) / 2, (segment.First.Y + segment.Second.Y) / 2));
    }
}
