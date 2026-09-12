using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN82ScopeGeometryStabilityIntegrationTests
{
    [Fact]
    public async Task ChildStructuralEditsPreserveDriftSensitiveRootPresentationExactly()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = Renderer("phase-n82-root-geometry-stability");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        await MoveEffectiveNodeAsync(
            session,
            composition,
            BpmnDemoPipeline.TaskId,
            BpmnDemoPipeline.TaskVisualId,
            new VectorD(57d, 31d));
        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var rootBefore = CapturePresentation(session);
        AssertScopeIsolation(session, composition, rootScopeId);
        var revisionBeforeChildEdits = composition.Document.Revision;

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        _ = await CreateDriftSensitivePairAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId,
            "root-return-child-pair",
            new PointD(160d, 360d));
        var nested = await CreateNestedSubProcessAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId,
            "subprocess-2",
            new PointD(610d, 340d));
        Assert.True(composition.Document.Revision > revisionBeforeChildEdits);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            composition.Document.SemanticModel.GetScope(nested.ElementId).Id);
        AssertScopeIsolation(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId);

        await NavigateAsync(session, rootScopeId);
        var rootAfter = CapturePresentation(session);

        AssertPresentationEqual(rootBefore, rootAfter);
        AssertScopeIsolation(session, composition, rootScopeId);
        Assert.DoesNotContain(rootAfter.NodeSemanticIds, id => id == nested.ElementId);
        Assert.DoesNotContain(
            session.CaptureState().CurrentScene!.Items,
            item =>
                item.Origin.SemanticElementId == nested.ElementId ||
                item.Origin.VisualStateId == nested.VisualStateId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    [Fact]
    public async Task RootEditPreservesDriftSensitiveChildPresentationExactly()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = Renderer("phase-n82-child-geometry-stability");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        _ = await CreateDriftSensitivePairAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId,
            "child-preservation-pair",
            new PointD(150d, 350d));
        var childBefore = CapturePresentation(session);
        AssertScopeIsolation(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId);

        await NavigateAsync(session, rootScopeId);
        await MoveEffectiveNodeAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderSubProcessId,
            BpmnDemoPipeline.ProcessOrderSubProcessVisualId,
            new VectorD(29d, 17d));
        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var childAfter = CapturePresentation(session);

        AssertPresentationEqual(childBefore, childAfter);
        AssertScopeIsolation(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    [Fact]
    public async Task InactiveScopeUndoRedoPreservesActiveScopePresentationExactly()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = Renderer("phase-n82-inactive-scope-history-stability");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        _ = await CreateDriftSensitivePairAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId,
            "inactive-history-child-pair",
            new PointD(170d, 350d));

        await NavigateAsync(session, rootScopeId);
        var rootTaskId = new SemanticElementId("bpmn:n8.2:geometry:inactive-history-root-task");
        var rootTaskVisualId = new VisualStateId($"{rootTaskId.Value}:visual");
        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            rootTaskId,
            rootTaskVisualId,
            new PointD(1650d, 820d),
            new SizeD(140d, 82d),
            "INACTIVE_HISTORY_ROOT_TASK",
            "Inactive history root task",
            997,
            VisualPlacementMode.Manual));

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var childBefore = CapturePresentation(session);

        var undoNavigation = await session.UndoAsync();
        Assert.True(undoNavigation.IsApplied, Diagnostics(undoNavigation.Diagnostics));
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        var undo = await session.UndoAsync();
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(
            composition.Document.SemanticModel.Elements,
            element => element.Id == rootTaskId);

        var redo = await session.RedoAsync();
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.Contains(
            composition.Document.SemanticModel.Elements,
            element => element.Id == rootTaskId);

        var redoNavigation = await session.RedoAsync();
        Assert.True(redoNavigation.IsApplied, Diagnostics(redoNavigation.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertPresentationEqual(childBefore, CapturePresentation(session));
        AssertScopeIsolation(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    [Fact]
    public async Task RepeatedRootChildNestedNavigationRestoresIndependentPresentationsWithoutLeakage()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = Renderer("phase-n82-nested-geometry-stability");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        await MoveEffectiveNodeAsync(
            session,
            composition,
            BpmnDemoPipeline.TaskId,
            BpmnDemoPipeline.TaskVisualId,
            new VectorD(43d, 23d));
        var root = CapturePresentation(session);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        _ = await CreateDriftSensitivePairAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId,
            "nested-sequence-child-pair",
            new PointD(150d, 360d));
        var nested = await CreateNestedSubProcessAsync(
            session,
            composition,
            BpmnDemoPipeline.ProcessOrderScopeId,
            "nested-sequence-subprocess-2",
            new PointD(620d, 350d));
        var child = CapturePresentation(session);
        Assert.Contains(child.NodeSemanticIds, id => id == nested.ElementId);

        await NavigateAsync(session, nested.ScopeId);
        _ = await CreateDriftSensitivePairAsync(
            session,
            composition,
            nested.ScopeId,
            "nested-sequence-inner-pair",
            new PointD(180d, 190d));
        var inner = CapturePresentation(session);

        for (var iteration = 0; iteration < 5; iteration++)
        {
            await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
            AssertPresentationEqual(child, CapturePresentation(session));
            AssertScopeIsolation(
                session,
                composition,
                BpmnDemoPipeline.ProcessOrderScopeId);

            await NavigateAsync(session, rootScopeId);
            AssertPresentationEqual(root, CapturePresentation(session));
            AssertScopeIsolation(session, composition, rootScopeId);
        }

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        AssertPresentationEqual(child, CapturePresentation(session));
        await NavigateAsync(session, nested.ScopeId);
        AssertPresentationEqual(inner, CapturePresentation(session));
        AssertScopeIsolation(session, composition, nested.ScopeId);
        Assert.DoesNotContain(inner.NodeSemanticIds, id => id == nested.ElementId);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        AssertPresentationEqual(child, CapturePresentation(session));
        await NavigateAsync(session, rootScopeId);
        var restoredRoot = CapturePresentation(session);
        AssertPresentationEqual(root, restoredRoot);
        Assert.DoesNotContain(restoredRoot.NodeSemanticIds, id => id == nested.ElementId);
        Assert.DoesNotContain(
            session.CaptureState().CurrentScene!.Items,
            item =>
                item.Origin.SemanticElementId == nested.ElementId ||
                item.Origin.VisualStateId == nested.VisualStateId);

        await NavigateAsync(session, nested.ScopeId);
        AssertPresentationEqual(inner, CapturePresentation(session));
        AssertScopeIsolation(session, composition, nested.ScopeId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static async ValueTask<CreatedPair> CreateDriftSensitivePairAsync(
        EditingSession session,
        DocumentCanvasComposition composition,
        DocumentScopeId scopeId,
        string prefix,
        PointD position)
    {
        var taskId = new SemanticElementId($"bpmn:n8.2:geometry:{prefix}:task");
        var taskVisualId = new VisualStateId($"{taskId.Value}:visual");
        var gatewayId = new SemanticElementId($"bpmn:n8.2:geometry:{prefix}:gateway");
        var gatewayVisualId = new VisualStateId($"{gatewayId.Value}:visual");
        var flowId = new SemanticElementId($"bpmn:n8.2:geometry:{prefix}:flow");
        var flowVisualId = new VisualStateId($"{flowId.Value}:visual");
        var taskSourceAnchorId = new ConnectorAnchorId(
            $"bpmn:n8.2:geometry:{prefix}:task:right:source");
        var gatewayTargetAnchorId = new ConnectorAnchorId(
            $"bpmn:n8.2:geometry:{prefix}:gateway:left:target");

        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskId,
            taskVisualId,
            position,
            new SizeD(140d, 82d),
            $"{prefix.ToUpperInvariant()}_TASK",
            $"{prefix} task",
            900,
            VisualPlacementMode.Manual,
            targetScopeId: scopeId));
        await ExecuteAsync(session, new CreateBpmnExclusiveGatewayCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            gatewayId,
            gatewayVisualId,
            new PointD(position.X + 250d, position.Y + 17d),
            new SizeD(48d, 48d),
            $"{prefix.ToUpperInvariant()}_GATEWAY",
            $"{prefix} gateway",
            VisualPlacementMode.Manual,
            targetScopeId: scopeId));
        await AddAnchorAsync(
            session,
            composition,
            taskVisualId,
            taskSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await AddAnchorAsync(
            session,
            composition,
            gatewayVisualId,
            gatewayTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);
        await ExecuteAsync(session, new CreateBpmnSequenceFlowCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            flowId,
            flowVisualId,
            taskId,
            gatewayId,
            taskSourceAnchorId,
            gatewayTargetAnchorId));

        await MoveEffectiveNodeAsync(
            session,
            composition,
            taskId,
            taskVisualId,
            new VectorD(61d, 37d));
        return new CreatedPair(taskId, taskVisualId, gatewayId, gatewayVisualId, flowId);
    }

    private static async ValueTask<CreatedSubProcess> CreateNestedSubProcessAsync(
        EditingSession session,
        DocumentCanvasComposition composition,
        DocumentScopeId parentScopeId,
        string prefix,
        PointD position)
    {
        var elementId = new SemanticElementId($"bpmn:n8.2:geometry:{prefix}");
        var visualStateId = new VisualStateId($"{elementId.Value}:visual");
        var scopeId = new DocumentScopeId($"{elementId.Value}:scope");
        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            elementId,
            visualStateId,
            parentScopeId,
            scopeId,
            position,
            new SizeD(120d, 80d),
            prefix.ToUpperInvariant(),
            "SubProcess 2",
            VisualPlacementMode.Pinned));
        return new CreatedSubProcess(elementId, visualStateId, scopeId);
    }

    private static async ValueTask MoveEffectiveNodeAsync(
        EditingSession session,
        DocumentCanvasComposition composition,
        SemanticElementId semanticElementId,
        VisualStateId visualStateId,
        VectorD delta)
    {
        var state = session.CaptureState();
        var node = Assert.Single(state.ProjectedGraph!.Nodes, candidate =>
            candidate.Source.SemanticElementId == semanticElementId &&
            candidate.Source.VisualStateId == visualStateId);
        var geometry = Assert.Single(state.LayoutResult!.Nodes, candidate =>
            candidate.ProjectedObjectId == node.Id);
        await ExecuteAsync(session, new MoveVisualStateCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            visualStateId,
            geometry.Bounds.TopLeft + delta,
            VisualPlacementMode.Pinned));
    }

    private static async ValueTask AddAnchorAsync(
        EditingSession session,
        DocumentCanvasComposition composition,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role) =>
        await ExecuteAsync(session, new AddConnectorAnchorCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            visualStateId,
            anchorId,
            side,
            role,
            0));

    private static async ValueTask ExecuteAsync(
        EditingSession session,
        Inceptus.DocumentEngine.Contracts.Commands.ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();
        Assert.True(
            state.Status == EditingSessionStatus.Ready,
            Diagnostics(state.RuntimeDiagnostics));
    }

    private static async ValueTask NavigateAsync(
        EditingSession session,
        DocumentScopeId scopeId)
    {
        var result = await session.NavigateToScopeAsync(scopeId);
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(scopeId, state.ActiveScopeId);
    }

    private static ScopePresentation CapturePresentation(EditingSession session)
    {
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<Inceptus.DocumentEngine.Contracts.Layout.LayoutResult>(
            state.LayoutResult);
        var routing = Assert.IsType<RoutingResult>(state.RoutingResult);
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var nodesById = graph.Nodes.ToDictionary(static node => node.Id);
        var nodes = layout.Nodes.ToDictionary(
            static geometry => geometry.ProjectedObjectId,
            geometry =>
            {
                var source = nodesById[geometry.ProjectedObjectId].Source;
                return new EffectiveNodeGeometry(
                    source.SemanticElementId,
                    Assert.IsType<VisualStateId>(source.VisualStateId),
                    geometry.Bounds,
                    geometry.Transform);
            });
        var labels = scene.Items
            .Where(static item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var routes = routing.Routes
            .OrderBy(static route => route.ProjectedEdgeId.Value, StringComparer.Ordinal)
            .ToArray();
        var noRouteEdgeIds = routing.NoRouteEdgeIds
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();
        return new ScopePresentation(
            nodes,
            labels,
            routes,
            noRouteEdgeIds,
            graph.Nodes.Select(static node => node.Source.SemanticElementId).ToHashSet(),
            graph.Nodes.Select(static node => node.Id).OrderBy(static id => id.Value).ToArray(),
            graph.Edges.Select(static edge => edge.Id).OrderBy(static id => id.Value).ToArray());
    }

    private static void AssertPresentationEqual(
        ScopePresentation expected,
        ScopePresentation actual)
    {
        Assert.Equal(
            expected.Nodes.Keys.OrderBy(static id => id.Value),
            actual.Nodes.Keys.OrderBy(static id => id.Value));
        foreach (var (id, geometry) in expected.Nodes)
        {
            Assert.Equal(geometry, actual.Nodes[id]);
        }

        Assert.Equal(expected.Labels.AsEnumerable(), actual.Labels.AsEnumerable());
        Assert.Equal(expected.Routes.AsEnumerable(), actual.Routes.AsEnumerable());
        Assert.Equal(
            expected.NoRouteEdgeIds.AsEnumerable(),
            actual.NoRouteEdgeIds.AsEnumerable());
        Assert.Equal(expected.NodeIds.AsEnumerable(), actual.NodeIds.AsEnumerable());
        Assert.Equal(expected.EdgeIds.AsEnumerable(), actual.EdgeIds.AsEnumerable());
    }

    private static void AssertScopeIsolation(
        EditingSession session,
        DocumentCanvasComposition composition,
        DocumentScopeId expectedScopeId)
    {
        var state = session.CaptureState();
        Assert.Equal(expectedScopeId, state.ActiveScopeId);
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<Inceptus.DocumentEngine.Contracts.Layout.LayoutResult>(
            state.LayoutResult);
        var routing = Assert.IsType<RoutingResult>(state.RoutingResult);
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var snapshot = composition.Document.CaptureSnapshot();

        var expectedElementIds = snapshot.SemanticModel.Elements
            .Where(element => snapshot.SemanticModel.GetScope(element.Id).Id == expectedScopeId)
            .Select(static element => element.Id)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            expectedElementIds.AsEnumerable(),
            graph.Nodes
                .Select(static node => node.Source.SemanticElementId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal));

        foreach (var source in graph.Nodes.Select(static node => node.Source)
                     .Concat(graph.Edges.Select(static edge => edge.Source))
                     .Concat(graph.Groups.Select(static group => group.Source))
                     .Concat(graph.Ports.Select(static port => port.Source))
                     .Concat(graph.Labels.Select(static label => label.Source)))
        {
            Assert.Equal(
                expectedScopeId,
                snapshot.SemanticModel.GetScope(source.SemanticElementId).Id);
        }

        Assert.Equal(
            graph.Nodes.Select(static node => node.Id).OrderBy(static id => id.Value),
            layout.Nodes.Select(static node => node.ProjectedObjectId)
                .OrderBy(static id => id.Value));
        Assert.Equal(
            graph.Groups.Select(static group => group.Id).OrderBy(static id => id.Value),
            layout.Groups.Select(static group => group.ProjectedObjectId)
                .OrderBy(static id => id.Value));

        var routedOrRejectedEdgeIds = routing.Routes
            .Select(static route => route.ProjectedEdgeId)
            .Concat(routing.NoRouteEdgeIds)
            .OrderBy(static id => id.Value)
            .ToArray();
        Assert.Equal(
            graph.Edges.Select(static edge => edge.Id).OrderBy(static id => id.Value),
            routedOrRejectedEdgeIds.AsEnumerable());

        var projectedIds = graph.Nodes.Select(static node => node.Id)
            .Concat(graph.Edges.Select(static edge => edge.Id))
            .Concat(graph.Groups.Select(static group => group.Id))
            .Concat(graph.Ports.Select(static port => port.Id))
            .Concat(graph.Labels.Select(static label => label.Id))
            .ToHashSet();
        foreach (var item in scene.Items)
        {
            if (item.Origin.SemanticElementId is { } semanticElementId)
            {
                Assert.Equal(
                    expectedScopeId,
                    snapshot.SemanticModel.GetScope(semanticElementId).Id);
            }

            if (item.Origin.VisualStateId is { } visualStateId)
            {
                Assert.True(snapshot.VisualModel.TryGetVisualState(
                    visualStateId,
                    out var visualState));
                Assert.Equal(
                    expectedScopeId,
                    snapshot.SemanticModel.GetScope(visualState!.SemanticElementId).Id);
            }

            if (item.Origin.ProjectedObjectId is { } projectedObjectId)
            {
                Assert.Contains(projectedObjectId, projectedIds);
            }

            Assert.All(item.Origin.RelatedProjectedObjectIds, id =>
                Assert.Contains(id, projectedIds));
        }
    }

    private static Canvas2DRenderer Renderer(string canvasId)
    {
        var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var initialization = renderer.InitializeAsync(
            canvasId,
            new Canvas2DSurfaceSize(1000d, 700d, 1.25d)).AsTask().GetAwaiter().GetResult();
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        return renderer;
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record CreatedPair(
        SemanticElementId TaskId,
        VisualStateId TaskVisualId,
        SemanticElementId GatewayId,
        VisualStateId GatewayVisualId,
        SemanticElementId FlowId);

    private sealed record CreatedSubProcess(
        SemanticElementId ElementId,
        VisualStateId VisualStateId,
        DocumentScopeId ScopeId);

    private sealed record EffectiveNodeGeometry(
        SemanticElementId SemanticElementId,
        VisualStateId VisualStateId,
        RectD Bounds,
        Matrix2D Transform);

    private sealed record ScopePresentation(
        IReadOnlyDictionary<ProjectedObjectId, EffectiveNodeGeometry> Nodes,
        Canvas2DSceneItem[] Labels,
        RoutedConnectorGeometry[] Routes,
        ProjectedObjectId[] NoRouteEdgeIds,
        HashSet<SemanticElementId> NodeSemanticIds,
        ProjectedObjectId[] NodeIds,
        ProjectedObjectId[] EdgeIds);
}
