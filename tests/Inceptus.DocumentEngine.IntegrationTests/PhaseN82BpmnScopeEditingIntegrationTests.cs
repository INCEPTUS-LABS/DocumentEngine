using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN82BpmnScopeEditingIntegrationTests
{
    [Fact]
    public async Task ActiveScopeToolboxPlacementCreatesAndRoutesCoreNodeFamiliesInChild()
    {
        var identities = new[]
        {
            Identity("child-start"),
            Identity("child-task"),
            Identity("child-gateway"),
            Identity("child-end"),
        };
        await using var harness =
            await PhaseN1ToolboxPlacementIntegrationTests.PlacementHarness.CreateAsync(
                identities: new PhaseN1ToolboxPlacementIntegrationTests
                    .SequenceIdentityProvider(identities));
        var session = harness.Session;
        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);

        var placements = new[]
        {
            (BpmnSemanticTypes.StartEvent, identities[0], new PointD(120d, 420d)),
            (BpmnSemanticTypes.Task, identities[1], new PointD(280d, 420d)),
            (BpmnSemanticTypes.ExclusiveGateway, identities[2], new PointD(450d, 420d)),
            (BpmnSemanticTypes.EndEvent, identities[3], new PointD(610d, 420d)),
        };

        foreach (var (semanticTypeId, identity, center) in placements)
        {
            var item = harness.Item(semanticTypeId);
            Assert.True(harness.Selection.Select(item.ItemId));
            var scene = Assert.IsType<Canvas2DScene>(
                session.CaptureState().CurrentScene);
            var result = await harness.Controller.TryPlaceAtCssPointAsync(
                session,
                scene.ViewportTransform.TransformPoint(center));
            await harness.WaitForIdleAsync();

            Assert.True(result.Handled);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            Assert.Equal(identity.VisualStateId, result.CreatedVisualStateId);
            Assert.Equal(
                BpmnDemoPipeline.ProcessOrderScopeId,
                harness.Composition.Document.SemanticModel
                    .GetScope(identity.SemanticElementId).Id);
            Assert.Contains(
                harness.Composition.Document.SemanticModel.ScopeMemberships,
                membership =>
                    membership.SemanticElementId == identity.SemanticElementId &&
                    membership.ScopeId == BpmnDemoPipeline.ProcessOrderScopeId);
        }

        var startSource = Anchor("child-start-source");
        var taskTarget = Anchor("child-task-target");
        var taskSource = Anchor("child-task-source");
        var gatewayTarget = Anchor("child-gateway-target");
        var gatewaySource = Anchor("child-gateway-source");
        var endTarget = Anchor("child-end-target");
        await AddAnchorAsync(
            session,
            harness.Composition,
            identities[0].VisualStateId,
            startSource,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await AddAnchorAsync(
            session,
            harness.Composition,
            identities[1].VisualStateId,
            taskTarget,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);
        await AddAnchorAsync(
            session,
            harness.Composition,
            identities[1].VisualStateId,
            taskSource,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await AddAnchorAsync(
            session,
            harness.Composition,
            identities[2].VisualStateId,
            gatewayTarget,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);
        await AddAnchorAsync(
            session,
            harness.Composition,
            identities[2].VisualStateId,
            gatewaySource,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await AddAnchorAsync(
            session,
            harness.Composition,
            identities[3].VisualStateId,
            endTarget,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);

        var flowIds = new[]
        {
            new SemanticElementId("bpmn:n8.2:integration:child-core-flow-1"),
            new SemanticElementId("bpmn:n8.2:integration:child-core-flow-2"),
            new SemanticElementId("bpmn:n8.2:integration:child-core-flow-3"),
        };
        await CreateFlowAsync(
            session,
            harness.Composition,
            flowIds[0],
            identities[0].SemanticElementId,
            identities[1].SemanticElementId,
            startSource,
            taskTarget);
        await CreateFlowAsync(
            session,
            harness.Composition,
            flowIds[1],
            identities[1].SemanticElementId,
            identities[2].SemanticElementId,
            taskSource,
            gatewayTarget);
        await CreateFlowAsync(
            session,
            harness.Composition,
            flowIds[2],
            identities[2].SemanticElementId,
            identities[3].SemanticElementId,
            gatewaySource,
            endTarget);
        await harness.WaitForIdleAsync();

        var state = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, state.ActiveScopeId);
        Assert.Empty(state.RoutingResult!.NoRouteEdgeIds);
        Assert.All(flowIds, flowId =>
        {
            Assert.Equal(
                BpmnDemoPipeline.ProcessOrderScopeId,
                harness.Composition.Document.SemanticModel.GetScope(flowId).Id);
            var edge = Assert.Single(state.ProjectedGraph!.Edges, candidate =>
                candidate.Source.SemanticElementId == flowId);
            var route = Assert.Single(state.RoutingResult.Routes, candidate =>
                candidate.ProjectedEdgeId == edge.Id);
            Assert.True(route.Path.Length >= 2);
            Assert.Contains(state.CurrentScene!.Items, item =>
                item.Origin.SemanticElementId == flowId);
        });
    }

    [Fact]
    public async Task NavigatePlaceUndoRedoKeepsAuthoritativeChildScopeAndPipelineAligned()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
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
        var initialization = await renderer.InitializeAsync(
            "phase-n82-bpmn-scope-editing",
            new Canvas2DSurfaceSize(1000d, 700d, 1.25d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));

        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var ownedSession = session;

        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        var root = session.CaptureState();
        Assert.Equal(composition.Document.SemanticModel.RootScopeId, root.ActiveScopeId);
        Assert.Contains(root.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ProcessOrderSubProcessId);
        Assert.DoesNotContain(root.ProjectedGraph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ProcessOrderTaskId);

        var navigation = await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(navigation.Succeeded, Diagnostics(navigation.Diagnostics));
        var child = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, child.ActiveScopeId);
        Assert.Equal(5, child.ProjectedGraph!.NodeCount);
        Assert.Equal(4, child.ProjectedGraph.EdgeCount);
        Assert.Contains(child.ProjectedGraph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ProcessOrderTaskId);
        Assert.DoesNotContain(child.ProjectedGraph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ProcessOrderSubProcessId);
        Assert.Contains(child.CurrentScene!.Items, item =>
            item.Origin.SemanticElementId == BpmnDemoPipeline.ProcessOrderTaskId);
        Assert.DoesNotContain(child.CurrentScene.Items, item =>
            item.Origin.SemanticElementId == BpmnDemoPipeline.ProcessOrderSubProcessId ||
            item.Origin.SemanticElementId == BpmnDemoPipeline.TaskId);

        Assert.True(session.TryCaptureDocumentSnapshot(out var beforePlacement));
        var taskPlacement = Assert.Single(
            BpmnPluginRegistration.N82.ToolboxPlacementRegistrations,
            registration => registration.ToolboxItemId ==
                new ToolboxItemId("bpmn:toolbox:task"));
        var placedTaskId = new SemanticElementId("bpmn:n8.2:integration:placed-task");
        var placedTaskVisualId = new VisualStateId(
            "bpmn:n8.2:integration:placed-task:visual");
        var planResult = taskPlacement.CommandFactory.CreatePlan(
            new ToolboxPlacementRequest(
                taskPlacement.ToolboxItemId,
                beforePlacement,
                beforePlacement.Revision,
                new PointD(360d, 360d),
                new FixedIdentityProvider(new DocumentCreationIdentity(
                    placedTaskId,
                    placedTaskVisualId)),
                child.ActiveScopeId));
        Assert.True(planResult.Succeeded, Diagnostics(planResult.Diagnostics));
        var plan = Assert.IsType<ToolboxPlacementPlan>(planResult.Plan);
        var command = Assert.IsType<CreateBpmnTaskCommand>(plan.Command);
        Assert.Equal(child.ActiveScopeId, command.TargetScopeId);

        var placement = await session.ExecuteAsync(command);
        Assert.True(placement.IsCommitted, Diagnostics(placement.Diagnostics));
        await session.WaitForIdleAsync();
        var placed = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, placed.ActiveScopeId);
        Assert.Contains(placed.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == placedTaskId);
        Assert.Equal(2, placed.HistoryStatus.EntryCount);
        Assert.True(session.TryCaptureDocumentSnapshot(out var committed));
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            committed.SemanticModel.GetScope(placedTaskId).Id);
        Assert.Contains(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == placedTaskId &&
            membership.ScopeId == BpmnDemoPipeline.ProcessOrderScopeId);

        var undo = await session.UndoAsync();
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        await session.WaitForIdleAsync();
        var undone = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, undone.ActiveScopeId);
        Assert.DoesNotContain(undone.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == placedTaskId);

        var redo = await session.RedoAsync();
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        await session.WaitForIdleAsync();
        var redone = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, redone.ActiveScopeId);
        Assert.Contains(redone.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == placedTaskId);
        Assert.True(session.TryCaptureDocumentSnapshot(out var restored));
        Assert.Equal(
            committed.SemanticModel.ScopeMemberships.AsEnumerable(),
            restored.SemanticModel.ScopeMemberships.AsEnumerable());
    }

    [Fact]
    public async Task ChildScopeSequenceFlowCommitsAndProjectsInItsDerivedScope()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
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
        var initialization = await renderer.InitializeAsync(
            "phase-n82-bpmn-child-flow",
            new Canvas2DSurfaceSize(1000d, 700d, 1.25d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));

        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        var navigation = await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(navigation.Succeeded, Diagnostics(navigation.Diagnostics));
        var before = session.CaptureState();
        Assert.Equal(4, before.ProjectedGraph!.EdgeCount);

        var sourceAnchorId = new ConnectorAnchorId(
            "bpmn:n8.2:integration:child-extra-source");
        var targetAnchorId = new ConnectorAnchorId(
            "bpmn:n8.2:integration:child-extra-target");
        var flowId = new SemanticElementId(
            "bpmn:n8.2:integration:child-extra-flow");
        var flowVisualId = new VisualStateId(
            "bpmn:n8.2:integration:child-extra-flow:visual");

        var sourceAnchor = await session.ExecuteAsync(new AddConnectorAnchorCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            BpmnDemoPipeline.ProcessOrderStartEventVisualId,
            sourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            1));
        Assert.True(sourceAnchor.IsCommitted, Diagnostics(sourceAnchor.Diagnostics));
        var targetAnchor = await session.ExecuteAsync(new AddConnectorAnchorCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            BpmnDemoPipeline.ProcessOrderEndEventVisualId,
            targetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            1));
        Assert.True(targetAnchor.IsCommitted, Diagnostics(targetAnchor.Diagnostics));
        var flow = await session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            flowId,
            flowVisualId,
            BpmnDemoPipeline.ProcessOrderStartEventId,
            BpmnDemoPipeline.ProcessOrderEndEventId,
            sourceAnchorId,
            targetAnchorId));
        Assert.True(flow.IsCommitted, Diagnostics(flow.Diagnostics));
        await session.WaitForIdleAsync();

        Assert.True(session.TryCaptureDocumentSnapshot(out var committed));
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            committed.SemanticModel.GetScope(flowId).Id);
        Assert.DoesNotContain(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == flowId);
        var state = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, state.ActiveScopeId);
        Assert.Equal(5, state.ProjectedGraph!.EdgeCount);
        var edge = Assert.Single(state.ProjectedGraph.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
        Assert.Contains(state.RoutingResult!.Routes, route =>
            route.ProjectedEdgeId == edge.Id);

        var undo = await session.UndoAsync();
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.DoesNotContain(session.CaptureState().ProjectedGraph!.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);

        var redo = await session.RedoAsync();
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.Contains(session.CaptureState().ProjectedGraph!.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
    }

    [Fact]
    public async Task ValidationUsesTheActiveScopeAndChildCompletenessIsIndependent()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();

        var rootValidation = Assert.IsType<ValidationSnapshot>(
            await harness.Host.ValidateAsync());
        Assert.Equal(
            harness.Composition.Document.SemanticModel.RootScopeId,
            rootValidation.ScopeId);
        Assert.NotEmpty(rootValidation.Issues);

        var navigation = await harness.Host.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.NotNull(navigation);
        Assert.True(navigation.Succeeded, Diagnostics(navigation.Diagnostics));
        var childValidation = Assert.IsType<ValidationSnapshot>(
            await harness.Host.ValidateAsync());

        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, childValidation.ScopeId);
        Assert.Empty(childValidation.Issues);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            harness.State.ActiveScopeId);
    }

    [Fact]
    public async Task ChildMoveAndResizeLeaveRestoredRootGeometryRoutingAndSceneExact()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = Renderer("phase-n82-child-geometry-isolation");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var root = session.CaptureState();
        var rootGraph = root.ProjectedGraph!;
        var rootLayout = root.LayoutResult!.Computation;
        var rootRouting = root.RoutingResult!.Computation;
        var rootScene = root.CurrentScene!;
        var rootSubProcessVisual = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.ProcessOrderSubProcessVisualId);

        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        var childBefore = session.CaptureState();
        var childRoutingBefore = childBefore.RoutingResult!.Computation;
        var childTaskVisual = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.ProcessOrderTaskVisualId);
        var movedPosition = new PointD(
            childTaskVisual.Position.X + 80d,
            childTaskVisual.Position.Y + 45d);
        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            childTaskVisual.Id,
            movedPosition));
        Assert.True(move.IsCommitted, Diagnostics(move.Diagnostics));
        await session.WaitForIdleAsync();
        var resize = await session.ExecuteAsync(new ResizeVisualStateCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            childTaskVisual.Id,
            new RectD(
                movedPosition.X,
                movedPosition.Y,
                childTaskVisual.Size.Width + 40d,
                childTaskVisual.Size.Height + 25d)));
        Assert.True(resize.IsCommitted, Diagnostics(resize.Diagnostics));
        await session.WaitForIdleAsync();
        var changedChild = session.CaptureState();
        Assert.NotEqual(childRoutingBefore, changedChild.RoutingResult!.Computation);

        Assert.True((await session.NavigateToScopeAsync(
            composition.Document.SemanticModel.RootScopeId)).Succeeded);
        var restoredRoot = session.CaptureState();
        var restoredGraph = restoredRoot.ProjectedGraph!;
        Assert.Equal(rootGraph.Nodes.AsEnumerable(), restoredGraph.Nodes.AsEnumerable());
        Assert.Equal(rootGraph.Edges.AsEnumerable(), restoredGraph.Edges.AsEnumerable());
        Assert.Equal(rootGraph.Groups.AsEnumerable(), restoredGraph.Groups.AsEnumerable());
        Assert.Equal(rootGraph.Ports.AsEnumerable(), restoredGraph.Ports.AsEnumerable());
        Assert.Equal(rootGraph.Labels.AsEnumerable(), restoredGraph.Labels.AsEnumerable());
        Assert.Equal(rootLayout, restoredRoot.LayoutResult!.Computation);
        Assert.Equal(rootRouting, restoredRoot.RoutingResult!.Computation);
        var restoredScene = restoredRoot.CurrentScene!;
        Assert.Equal(rootScene.Configuration, restoredScene.Configuration);
        Assert.Equal(
            rootScene.Contributors.AsEnumerable(),
            restoredScene.Contributors.AsEnumerable());
        Assert.Equal(rootScene.Viewport, restoredScene.Viewport);
        Assert.Equal(rootScene.ViewportTransform, restoredScene.ViewportTransform);
        Assert.Equal(rootScene.ActiveToolId, restoredScene.ActiveToolId);
        Assert.Equal(rootScene.FocusTargetId, restoredScene.FocusTargetId);
        Assert.Equal(rootScene.ToolState, restoredScene.ToolState);
        Assert.Equal(rootScene.ContributorMetadata, restoredScene.ContributorMetadata);
        Assert.Equal(rootScene.Items.AsEnumerable(), restoredScene.Items.AsEnumerable());
        Assert.Equal(
            rootScene.Diagnostics.AsEnumerable(),
            restoredScene.Diagnostics.AsEnumerable());
        Assert.Equal(
            rootSubProcessVisual,
            Assert.Single(
                composition.Document.VisualModel.VisualStates,
                visual => visual.Id == BpmnDemoPipeline.ProcessOrderSubProcessVisualId));
        Assert.Equal(composition.Document.Revision, restoredRoot.DocumentRevision);
    }

    [Fact]
    public async Task ChildScopeEventBasedGatewayRejectsSubProcessTargetAtomically()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = Renderer("phase-n82-child-event-based-rule");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);

        var gatewayId = new SemanticElementId(
            "bpmn:n8.2:integration:child-event-based-gateway");
        var gatewayVisualId = new VisualStateId(
            "bpmn:n8.2:integration:child-event-based-gateway:visual");
        var subProcessId = new SemanticElementId(
            "bpmn:n8.2:integration:child-event-target-subprocess");
        var subProcessVisualId = new VisualStateId(
            "bpmn:n8.2:integration:child-event-target-subprocess:visual");
        var ownedScopeId = new DocumentScopeId(
            "bpmn:n8.2:integration:child-event-target-subprocess:scope");
        var gatewayCreation = await session.ExecuteAsync(
            new CreateBpmnEventBasedGatewayCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                gatewayId,
                gatewayVisualId,
                new PointD(400d, 500d),
                new SizeD(50d, 50d),
                "WAIT_FOR_EVENT",
                "Wait for event",
                VisualPlacementMode.Pinned,
                targetScopeId: BpmnDemoPipeline.ProcessOrderScopeId));
        Assert.True(gatewayCreation.IsCommitted, Diagnostics(gatewayCreation.Diagnostics));
        await session.WaitForIdleAsync();
        var subProcessCreation = await session.ExecuteAsync(
            new CreateBpmnSubProcessCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                subProcessId,
                subProcessVisualId,
                BpmnDemoPipeline.ProcessOrderScopeId,
                ownedScopeId,
                new PointD(560d, 485d),
                new SizeD(120d, 80d),
                "EVENT_TARGET_PROCESS",
                "Event target process",
                VisualPlacementMode.Pinned));
        Assert.True(
            subProcessCreation.IsCommitted,
            Diagnostics(subProcessCreation.Diagnostics));
        await session.WaitForIdleAsync();

        var sourceAnchorId = Anchor("child-event-based-source");
        var targetAnchorId = Anchor("child-event-subprocess-target");
        await AddAnchorAsync(
            session,
            composition,
            gatewayVisualId,
            sourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await AddAnchorAsync(
            session,
            composition,
            subProcessVisualId,
            targetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);
        var before = composition.Document.CaptureSnapshot();
        var historyBefore = session.CaptureState().HistoryStatus;
        var rejectedFlowId = new SemanticElementId(
            "bpmn:n8.2:integration:child-event-subprocess-flow");
        var rejection = await session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            rejectedFlowId,
            new VisualStateId($"{rejectedFlowId.Value}:visual"),
            gatewayId,
            subProcessId,
            sourceAnchorId,
            targetAnchorId));

        Assert.False(rejection.IsCommitted);
        Assert.Contains(rejection.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
        Assert.Same(before, composition.Document.CaptureSnapshot());
        var state = session.CaptureState();
        Assert.Equal(historyBefore, state.HistoryStatus);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, state.ActiveScopeId);
        Assert.DoesNotContain(state.ProjectedGraph!.Edges, edge =>
            edge.Source.SemanticElementId == rejectedFlowId);
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

    private static DocumentCreationIdentity Identity(string suffix) => new(
        new SemanticElementId($"bpmn:n8.2:integration:{suffix}"),
        new VisualStateId($"bpmn:n8.2:integration:{suffix}:visual"));

    private static ConnectorAnchorId Anchor(string suffix) =>
        new($"bpmn:n8.2:integration:{suffix}");

    private static async ValueTask AddAnchorAsync(
        EditingSession session,
        DocumentCanvasComposition composition,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role)
    {
        var result = await session.ExecuteAsync(new AddConnectorAnchorCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            visualStateId,
            anchorId,
            side,
            role,
            0));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
    }

    private static async ValueTask CreateFlowAsync(
        EditingSession session,
        DocumentCanvasComposition composition,
        SemanticElementId flowId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId)
    {
        var result = await session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            flowId,
            new VisualStateId($"{flowId.Value}:visual"),
            sourceId,
            targetId,
            sourceAnchorId,
            targetAnchorId));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class FixedIdentityProvider(DocumentCreationIdentity identity) :
        IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => identity;
    }
}
