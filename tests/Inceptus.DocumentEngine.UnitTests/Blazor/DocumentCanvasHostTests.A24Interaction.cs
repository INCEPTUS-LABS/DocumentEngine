using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("reconnect")]
    [InlineData("placement")]
    public async Task A24OverlayHiddenDuringGestureAndReturnsAfterwards(string kind)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-gesture", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var editor = session.CaptureState().EditorState;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(editor.Selection, null, null, null, editor.Viewport,
            new EditorGestureSnapshot("a24-test", kind, default, default)))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Null(host.CaptureState().ElementToolbox);
        await A24Select(host, new VisualStateId("v-source"));
        Assert.NotNull(host.CaptureState().ElementToolbox);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task A24SelectionConnectionModeAndEscapeHaveNoDocumentEffect()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("a24-modes", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        await A24Select(host, new VisualStateId("v-a"), new VisualStateId("v-b"));
        Assert.Null(host.CaptureState().ElementToolbox);
        await A24Select(host, new VisualStateId("v-flow-0"));
        Assert.Null(host.CaptureState().ElementToolbox);
        await A24Select(host, new VisualStateId("v-a"));
        host.DismissElementToolbox();
        Assert.Null(host.CaptureState().ElementToolbox);
        await A24Select(host);
        host.CaptureState();
        await A24Select(host, new VisualStateId("v-a"));
        Assert.NotNull(host.CaptureState().ElementToolbox);
        Assert.True((await session.UpdateEditorStateAsync(ConnectionToolState.WithSelection(session.CaptureState().EditorState,
            A22FlowTool("Sequence Flow").CreationId))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(.3d)]
    public async Task A24OverlayClampsInViewportAndControlsKeepTheirScreenSize(double zoom)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-zoom", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var original = host.CaptureState().ElementToolbox!;
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new(6 - 120 * zoom, 6 - 250 * zoom)));
        await session.WaitForIdleAsync();
        var overlay = host.CaptureState().ElementToolbox!;
        Assert.Equal(original.Anchor.Width * zoom, overlay.Anchor.Width, 6);
        Assert.Equal(original.CreationWidth, overlay.CreationWidth);
        Assert.InRange(overlay.ActionsPosition.X, 8, 1920 - 132);
        Assert.InRange(overlay.CreationPosition.X, 8, 1920 - overlay.CreationWidth - 8);
        Assert.InRange(overlay.CreationPosition.Y, 8, 1080 - 158 - 8);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new(-1000, -1000)));
        await session.WaitForIdleAsync();
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task A24ToolbarKeepsNodeVisibleWhenThereIsNoSpaceAboveIt()
    {
        await using var host = A23Host(A24Snapshot(position: new(0, 0)));
        await host.InitializeAsync("a24-top-edge", "container");
        await A24Select(host, new VisualStateId("v-source"));
        await Session(host).UpdateViewportAsync(new ViewportSnapshot(.3, default));
        await Session(host).WaitForIdleAsync();
        var overlay = host.CaptureState().ElementToolbox!;
        Assert.False(new RectD(overlay.ActionsPosition.X, overlay.ActionsPosition.Y, 124, 48).Intersects(overlay.Anchor));
    }

    [Fact]
    public async Task A24VisibleSubProcessCreatesInParentWithoutTouchingChild()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-subprocess", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:sub-process")))!.IsCommitted);
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var selected = Session(host).CaptureState().EditorState.Selection.Single();
        var target = document.VisualModel.VisualStates.Single(visual => visual.Id == selected);
        Assert.Equal(before.SemanticModel.RootScopeId, document.SemanticModel.GetScope(target.SemanticElementId).Id);
        Assert.Equal(before.SemanticModel.NestedScopes.AsEnumerable(), document.SemanticModel.NestedScopes);
    }

    [Fact]
    public async Task A24PopulatedDiagramOverlayReusesSceneAndCandidatesWithoutPersistentWork()
    {
        var snapshot = A23Snapshot(Enumerable.Range(0, 50).Select(index => ("task-" + index, "task-" + (index + 1))).ToArray());
        await using var host = A23Host(snapshot);
        await host.InitializeAsync("a24-populated", "container");
        await A24Select(host, new VisualStateId("v-task-2"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var scene = session.CaptureState().CurrentScene;
        var bytes = NativeDocumentSerializer.Export(before);
        Assert.NotNull(host.CaptureState().ElementToolbox);
        var count = host.ElementCandidateEvaluationCount;
        for (var index = 0; index < 100; index++) Assert.NotNull(host.CaptureState().ElementToolbox);
        Assert.Equal(count, host.ElementCandidateEvaluationCount);
        Assert.Same(scene, session.CaptureState().CurrentScene);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(document.CaptureSnapshot()).ToArray());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual)]
    public async Task A24AutoPointsPreservesModesAndDoesNotWorsenQuality(ConnectorRoutingType mode)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("a24-route-quality", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        foreach (var record in document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().ToArray())
            if (record.RoutingType != mode)
            {
                Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId, document.Revision, record.VisualStateId, mode))).IsCommitted);
                await session.WaitForIdleAsync();
            }
        await A24Select(host, new VisualStateId("v-a"));
        var before = document.CaptureSnapshot();
        var ids = before.VisualModel.EnumerateConnectorRoutingRecords().Select(record => record.VisualStateId).ToHashSet();
        Assert.Equal(mode != ConnectorRoutingType.Manual, host.CaptureState().ElementToolbox!.CanAutoPoints);
        await host.AutoPointsAsync();
        var after = document.CaptureSnapshot();
        Assert.All(after.VisualModel.EnumerateConnectorRoutingRecords(), record => Assert.Equal(mode, record.RoutingType));
        Assert.True(Quality(after).CompareTo(Quality(before)) <= 0);
        if (mode == ConnectorRoutingType.Manual) Assert.Same(before, after);
        (int Crossings, int Bends, double Length) Quality(Contracts.Documents.DocumentSnapshot snapshot)
        {
            var routes = snapshot.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
            var scores = routes.Where(record => ids.Contains(record.VisualStateId)).Select(record => ConnectorPathQuality.Score(record.Path,
                routes.Where(other => other.VisualStateId != record.VisualStateId).Select(other => other.Path))).ToArray();
            return (scores.Sum(score => score.Crossings), scores.Sum(score => score.Bends), scores.Sum(score => score.Length));
        }
    }

    [Fact]
    public async Task A24AutoPointsPreservesMessageFlowAndPoolAssignment()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a24-message-points", "container");
        var session = Session(host);
        var tool = A22FlowTool("Message Flow");
        Assert.True(selection.Select(tool.ItemId, tool.CreationId));
        await host.RefreshToolboxPlacementAsync();
        var pointer = PointerObserver(host);
        var start = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, start, pointerId: 411);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, start, pointerId: 411);
        var target = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ToolPeerTask).Bounds);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 412);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 412);
        await session.WaitForIdleAsync();
        await A24Select(host, A22ViewTaskVisualId);
        var sourceBounds = A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds;
        await session.UpdateViewportAsync(new ViewportSnapshot(1, new(100 - sourceBounds.X, 100 - sourceBounds.Y)));
        await session.WaitForIdleAsync();
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        Assert.Equal(BpmnSemanticTypes.MessageFlow, Assert.Single(before.SemanticModel.Relationships).TypeId);
        var result = await host.AutoPointsAsync();
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Contracts.Diagnostics.DiagnosticSeverity.Error);
        var after = document.CaptureSnapshot();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        foreach (var scope in before.VisualModel.RoutingScopes!.Value)
            Assert.Equal(scope.Geometry, after.VisualModel.RoutingScopes!.Value.Single(current => current.ScopeId == scope.ScopeId).Geometry);
        foreach (var node in before.VisualModel.VisualStates)
        {
            var current = after.VisualModel.VisualStates.Single(visual => visual.Id == node.Id);
            Assert.Equal(node.Position, current.Position);
            Assert.Equal(node.Size, current.Size);
            Assert.Equal(node.SourceAnchorId, current.SourceAnchorId);
            Assert.Equal(node.TargetAnchorId, current.TargetAnchorId);
        }
    }

    [Fact]
    public async Task A24AutoPointsImprovesBackwardAnchorWithExactFocusedHistory()
    {
        var snapshot = A23Snapshot([("a", "b")]);
        var visual = new Contracts.Visuals.VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates.Select(state =>
            state.Id.Value is "v-a" or "v-b" ? new Contracts.Visuals.VisualStateSnapshot(state.Id, state.SemanticElementId,
                new(state.Id.Value == "v-a" ? 100 : 450, 200), state.Size, state.PlacementMode, connectorAnchors: state.ConnectorAnchors.Select(anchor =>
                    new Contracts.Visuals.ConnectorAnchor(anchor.Id, Contracts.Visuals.ConnectorAnchorSide.Left, anchor.Role, anchor.Order))) : state));
        await using var host = A23Host(new(snapshot.SemanticModel, visual, snapshot.Metadata));
        await host.InitializeAsync("a24-improve-points", "container");
        await A24Select(host, new VisualStateId("v-a"));
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        var result = await host.AutoPointsAsync();
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        var anchor = after.VisualModel.VisualStates.Single(state => state.Id.Value == "v-a").ConnectorAnchors.Single();
        Assert.Equal(Contracts.Visuals.ConnectorAnchorSide.Right, anchor.Side);
        Assert.Equal(before.VisualModel.VisualStates.Single(state => state.Id.Value == "v-a").ConnectorAnchors.Single().Id, anchor.Id);
        Assert.Equal(1, Session(host).CaptureState().HistoryStatus.EntryCount);
        await host.AutoPointsAsync();
        Assert.Same(after, document.CaptureSnapshot());
        await host.UndoAsync(); A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync(); A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Fact]
    public async Task A24MarkupHasNamedNativeButtonsInGroupOrderAndNoInvalidTabStops()
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-markup", "container");
        await A24Select(host, new VisualStateId("v-source"));
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var markup = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ElementToolbox>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ElementToolbox.State)] = host.CaptureState().ElementToolbox }));
            return rendered.ToHtmlString();
        });
        Assert.True(markup.IndexOf("aria-label=\"Properties\"", StringComparison.Ordinal) < markup.IndexOf("aria-label=\"Auto Points\"", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("aria-label=\"Auto Points\"", StringComparison.Ordinal) < markup.IndexOf("aria-label=\"Delete\"", StringComparison.Ordinal));
        Assert.Contains("aria-label=\"Events\"", markup);
        Assert.Contains("aria-label=\"Activities\"", markup);
        Assert.Contains("aria-label=\"Gateways\"", markup);
        Assert.DoesNotContain("aria-label=\"Start Event\"", markup);
        Assert.DoesNotContain("Auto Arrange", markup);
        Assert.Equal(20, markup.Split("<button", StringSplitOptions.None).Length - 1);
        Assert.Contains("focusable=\"false\"", markup);
    }
}
