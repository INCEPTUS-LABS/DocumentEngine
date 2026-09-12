using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN90ActivityAttachmentScopeIntegrationTests
{
    private static readonly SizeD BoundarySize = new(36d, 36d);

    [Fact]
    public async Task ChildAttachmentRetainedGeometryAndInactiveScopeHistoryStayExact()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            Renderer("phase-n90-child-attachment-cache"),
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        AssertNotProjected(session, BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var initial = CaptureAttachmentPresentation(
            session,
            BpmnDemoPipeline.ProcessOrderTaskId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowId);
        var initialPlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        Assert.Equal(
            initialPlacement.ResolveBounds(initial.Owner.Bounds, BoundarySize),
            initial.Boundary.Bounds);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                initial.Boundary.Bounds,
                ConnectorAnchorSide.Bottom,
                0,
                1),
            initial.Route.SourceAnchor);

        await NavigateAsync(session, rootScopeId);
        AssertNotProjected(session, BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId);
        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(
            initial,
            CaptureAttachmentPresentation(
                session,
                BpmnDemoPipeline.ProcessOrderTaskId,
                BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
                BpmnDemoPipeline.ProcessOrderTimeoutFlowId));

        await NavigateAsync(session, rootScopeId);
        var movedPlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.25d);
        await ExecuteAsync(session, new UpdateBoundaryAttachmentCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId,
            movedPlacement,
            initial.Owner.Bounds));
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        AssertNotProjected(session, BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId);

        var undoAttachment = await session.UndoAsync();
        Assert.True(undoAttachment.IsCommitted, Diagnostics(undoAttachment.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(
            initialPlacement,
            BoundaryVisual(composition).BoundaryAttachment);

        var redoAttachment = await session.RedoAsync();
        Assert.True(redoAttachment.IsCommitted, Diagnostics(redoAttachment.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(movedPlacement, BoundaryVisual(composition).BoundaryAttachment);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var afterInactiveAttachment = CaptureAttachmentPresentation(
            session,
            BpmnDemoPipeline.ProcessOrderTaskId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowId);
        Assert.Equal(initial.Owner, afterInactiveAttachment.Owner);
        Assert.Equal(
            movedPlacement.ResolveBounds(initial.Owner.Bounds, BoundarySize),
            afterInactiveAttachment.Boundary.Bounds);
        Assert.NotEqual(initial.Boundary.Bounds, afterInactiveAttachment.Boundary.Bounds);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                afterInactiveAttachment.Boundary.Bounds,
                ConnectorAnchorSide.Bottom,
                0,
                1),
            afterInactiveAttachment.Route.SourceAnchor);

        var unchangedHandler = NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId);
        await NavigateAsync(session, rootScopeId);
        var movedOwnerBounds = new RectD(
            initial.Owner.Bounds.X + 47d,
            initial.Owner.Bounds.Y + 29d,
            initial.Owner.Bounds.Width,
            initial.Owner.Bounds.Height);
        await ExecuteAsync(session, new MoveVisualStateCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            BpmnDemoPipeline.ProcessOrderTaskVisualId,
            movedOwnerBounds.TopLeft,
            VisualPlacementMode.Pinned));
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var afterInactiveMove = CaptureAttachmentPresentation(
            session,
            BpmnDemoPipeline.ProcessOrderTaskId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowId);
        Assert.Equal(movedOwnerBounds, afterInactiveMove.Owner.Bounds);
        Assert.Equal(
            movedPlacement.ResolveBounds(movedOwnerBounds, BoundarySize),
            afterInactiveMove.Boundary.Bounds);
        Assert.Equal(unchangedHandler, NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId));

        await NavigateAsync(session, rootScopeId);
        var resizedOwnerBounds = new RectD(
            movedOwnerBounds.X,
            movedOwnerBounds.Y,
            movedOwnerBounds.Width + 53d,
            movedOwnerBounds.Height + 31d);
        await ExecuteAsync(session, new ResizeVisualStateCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            BpmnDemoPipeline.ProcessOrderTaskVisualId,
            resizedOwnerBounds,
            VisualPlacementMode.Pinned));
        Assert.True((await session.UndoAsync()).IsCommitted);
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var afterInactiveResize = CaptureAttachmentPresentation(
            session,
            BpmnDemoPipeline.ProcessOrderTaskId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowId);
        Assert.Equal(resizedOwnerBounds, afterInactiveResize.Owner.Bounds);
        Assert.Equal(
            movedPlacement.ResolveBounds(resizedOwnerBounds, BoundarySize),
            afterInactiveResize.Boundary.Bounds);
        Assert.Equal(unchangedHandler, NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId));
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                afterInactiveResize.Boundary.Bounds,
                ConnectorAnchorSide.Bottom,
                0,
                1),
            afterInactiveResize.Route.SourceAnchor);
    }

    [Fact]
    public async Task NestedAttachmentAndOutgoingRouteRemainScopeLocalAcrossNavigation()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            Renderer("phase-n90-nested-attachment-cache"),
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var nestedSubProcessId = new SemanticElementId("bpmn:n9:scope:nested-subprocess");
        var nestedSubProcessVisualId = new VisualStateId(
            "bpmn:n9:scope:nested-subprocess:visual");
        var nestedScopeId = new DocumentScopeId("bpmn:n9:scope:nested");
        var ownerId = new SemanticElementId("bpmn:n9:scope:nested:owner");
        var ownerVisualId = new VisualStateId("bpmn:n9:scope:nested:owner:visual");
        var boundaryId = new SemanticElementId("bpmn:n9:scope:nested:boundary");
        var boundaryVisualId = new VisualStateId(
            "bpmn:n9:scope:nested:boundary:visual");
        var handlerId = new SemanticElementId("bpmn:n9:scope:nested:handler");
        var handlerVisualId = new VisualStateId("bpmn:n9:scope:nested:handler:visual");
        var flowId = new SemanticElementId("bpmn:n9:scope:nested:flow");
        var flowVisualId = new VisualStateId("bpmn:n9:scope:nested:flow:visual");
        var sourceAnchorId = new ConnectorAnchorId(
            "bpmn:n9:scope:nested:boundary:right:source");
        var targetAnchorId = new ConnectorAnchorId(
            "bpmn:n9:scope:nested:handler:left:target");
        var ownerBounds = new RectD(150d, 150d, 160d, 90d);
        var boundaryPlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.5d);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            nestedSubProcessId,
            nestedSubProcessVisualId,
            BpmnDemoPipeline.ProcessOrderScopeId,
            nestedScopeId,
            new PointD(620d, 450d),
            new SizeD(130d, 84d),
            "NESTED_N9",
            "Nested N9",
            VisualPlacementMode.Pinned));
        await NavigateAsync(session, nestedScopeId);
        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerId,
            ownerVisualId,
            ownerBounds.TopLeft,
            ownerBounds.Size,
            "NESTED_SERVICE",
            "Nested service",
            901,
            VisualPlacementMode.Pinned,
            taskTypeId: BpmnSemanticTypes.ServiceTask,
            targetScopeId: nestedScopeId));
        await ExecuteAsync(session, new CreateBpmnTimerBoundaryEventCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            boundaryId,
            boundaryVisualId,
            ownerId,
            boundaryPlacement.Side,
            boundaryPlacement.PositionOnSide,
            ownerBounds,
            "Nested timeout",
            timerDefinition: "PT2M",
            targetScopeId: nestedScopeId));
        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            handlerId,
            handlerVisualId,
            new PointD(430d, 155d),
            new SizeD(150d, 80d),
            "NESTED_HANDLER",
            "Nested handler",
            902,
            VisualPlacementMode.Pinned,
            targetScopeId: nestedScopeId));
        await ExecuteAsync(session, new AddConnectorAnchorCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            boundaryVisualId,
            sourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        await ExecuteAsync(session, new AddConnectorAnchorCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            handlerVisualId,
            targetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0));
        await ExecuteAsync(session, new CreateBpmnSequenceFlowCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            flowId,
            flowVisualId,
            boundaryId,
            handlerId,
            sourceAnchorId,
            targetAnchorId));

        Assert.Equal(
            nestedScopeId,
            composition.Document.SemanticModel.GetScope(boundaryId).Id);
        var nested = CaptureAttachmentPresentation(session, ownerId, boundaryId, flowId);
        Assert.Equal(ownerBounds, nested.Owner.Bounds);
        Assert.Equal(
            boundaryPlacement.ResolveBounds(ownerBounds, BoundarySize),
            nested.Boundary.Bounds);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                nested.Boundary.Bounds,
                ConnectorAnchorSide.Right,
                0,
                1),
            nested.Route.SourceAnchor);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        AssertProjected(session, nestedSubProcessId);
        AssertNotProjected(session, ownerId);
        AssertNotProjected(session, boundaryId);

        await NavigateAsync(session, rootScopeId);
        AssertNotProjected(session, nestedSubProcessId);
        AssertNotProjected(session, boundaryId);

        await NavigateAsync(session, nestedScopeId);
        Assert.Equal(
            nested,
            CaptureAttachmentPresentation(session, ownerId, boundaryId, flowId));
    }

    [Fact]
    public async Task NavigationAndAttachmentMutationUndoRedoInOneExactChronology()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            Renderer("phase-n90-mixed-scope-history"),
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var boundaryId = new SemanticElementId("bpmn:n9:scope:mixed:boundary");
        var boundaryVisualId = new VisualStateId("bpmn:n9:scope:mixed:boundary:visual");
        var placement = new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Left, 0.5d);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var ownerBounds = NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId).Bounds;
        await ExecuteAsync(session, new CreateBpmnTimerBoundaryEventCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            boundaryId,
            boundaryVisualId,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId,
            placement.Side,
            placement.PositionOnSide,
            ownerBounds,
            "Mixed-history timeout",
            targetScopeId: BpmnDemoPipeline.ProcessOrderScopeId));
        var createdElement = Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == boundaryId);
        var createdVisual = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == boundaryVisualId);
        var createdBounds = NodeGeometry(session, boundaryId).Bounds;
        Assert.Equal(placement.ResolveBounds(ownerBounds, BoundarySize), createdBounds);

        await NavigateAsync(session, rootScopeId);
        AssertNotProjected(session, boundaryId);

        var undoReturn = await session.UndoAsync();
        Assert.True(undoReturn.IsApplied, Diagnostics(undoReturn.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, session.CaptureState().ActiveScopeId);
        AssertProjected(session, boundaryId);

        var undoCreation = await session.UndoAsync();
        Assert.True(undoCreation.IsCommitted, Diagnostics(undoCreation.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, session.CaptureState().ActiveScopeId);
        AssertNotProjected(session, boundaryId);
        Assert.False(composition.Document.SemanticModel.TryGetElement(boundaryId, out _));

        var undoOpen = await session.UndoAsync();
        Assert.True(undoOpen.IsApplied, Diagnostics(undoOpen.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        var redoOpen = await session.RedoAsync();
        Assert.True(redoOpen.IsApplied, Diagnostics(redoOpen.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, session.CaptureState().ActiveScopeId);
        AssertNotProjected(session, boundaryId);

        var redoCreation = await session.RedoAsync();
        Assert.True(redoCreation.IsCommitted, Diagnostics(redoCreation.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(createdElement, Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == boundaryId));
        Assert.Equal(createdVisual, Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == boundaryVisualId));
        Assert.Equal(createdBounds, NodeGeometry(session, boundaryId).Bounds);

        var redoReturn = await session.RedoAsync();
        Assert.True(redoReturn.IsApplied, Diagnostics(redoReturn.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        AssertNotProjected(session, boundaryId);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(createdBounds, NodeGeometry(session, boundaryId).Bounds);
    }

    private static VisualStateSnapshot BoundaryVisual(DocumentCanvasComposition composition) =>
        Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId);

    private static AttachmentPresentation CaptureAttachmentPresentation(
        EditingSession session,
        SemanticElementId ownerId,
        SemanticElementId boundaryId,
        SemanticElementId flowId)
    {
        var state = session.CaptureState();
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var routing = Assert.IsType<RoutingResult>(state.RoutingResult);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
        return new AttachmentPresentation(
            NodeGeometry(session, ownerId),
            NodeGeometry(session, boundaryId),
            Assert.Single(routing.Routes, route => route.ProjectedEdgeId == edge.Id));
    }

    private static LayoutNodeGeometry NodeGeometry(
        EditingSession session,
        SemanticElementId semanticElementId)
    {
        var state = session.CaptureState();
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<LayoutResult>(state.LayoutResult);
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == semanticElementId);
        return Assert.Single(layout.Nodes, geometry =>
            geometry.ProjectedObjectId == node.Id);
    }

    private static void AssertProjected(
        EditingSession session,
        SemanticElementId semanticElementId) =>
        Assert.Contains(
            Assert.IsType<ProjectedGraph>(session.CaptureState().ProjectedGraph).Nodes,
            node => node.Source.SemanticElementId == semanticElementId);

    private static void AssertNotProjected(
        EditingSession session,
        SemanticElementId semanticElementId) =>
        Assert.DoesNotContain(
            Assert.IsType<ProjectedGraph>(session.CaptureState().ProjectedGraph).Nodes,
            node => node.Source.SemanticElementId == semanticElementId);

    private static async ValueTask ExecuteAsync(
        EditingSession session,
        ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await WaitForIdleAsync(session);
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
    }

    private static async ValueTask NavigateAsync(
        EditingSession session,
        DocumentScopeId scopeId)
    {
        var result = await session.NavigateToScopeAsync(scopeId);
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        await WaitForIdleAsync(session);
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(scopeId, state.ActiveScopeId);
    }

    private static async ValueTask WaitForIdleAsync(EditingSession session) =>
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

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

    private sealed record AttachmentPresentation(
        LayoutNodeGeometry Owner,
        LayoutNodeGeometry Boundary,
        RoutedConnectorGeometry Route);
}
