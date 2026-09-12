using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN91BoundaryEventScopeIntegrationTests
{
    private static readonly SizeD BoundarySize = new(36d, 36d);

    public static TheoryData<SemanticTypeId> BoundaryTypes => new()
    {
        BpmnSemanticTypes.TimerBoundaryEvent,
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.SignalBoundaryEvent,
    };

    [Fact]
    public async Task ChildMessageSignalCreationNavigationAndHistoryStayExact()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            Renderer("phase-n91-message-signal-scope-history"),
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var messageId = new SemanticElementId("bpmn:n9.1:scope:message");
        var messageVisualId = new VisualStateId(
            "bpmn:n9.1:scope:message:visual");
        var signalId = new SemanticElementId("bpmn:n9.1:scope:signal");
        var signalVisualId = new VisualStateId(
            "bpmn:n9.1:scope:signal:visual");
        var messagePlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Left,
            0.25d);
        var signalPlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.75d);

        AssertNotProjected(session, messageId);
        AssertNotProjected(session, signalId);

        // Exact global chronology begins: Open -> Message -> Signal -> Return.
        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        var ownerBounds = NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderTaskId).Bounds;
        var seededTimerBefore = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id ==
                BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId);
        var seededTimerGeometryBefore = NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId);

        await ExecuteAsync(session, new CreateBpmnMessageBoundaryEventCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            messageId,
            messageVisualId,
            BpmnDemoPipeline.ProcessOrderTaskId,
            messagePlacement.Side,
            messagePlacement.PositionOnSide,
            ownerBounds,
            "Child message",
            cancelActivity: true,
            description: "Child-scope Message Boundary Event.",
            targetScopeId: BpmnDemoPipeline.ProcessOrderScopeId));
        var createdMessageElement = Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == messageId);
        var createdMessageVisual = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == messageVisualId);
        var createdMessageGeometry = AssertBoundaryProjection(
            session,
            messageId,
            messagePlacement.ResolveBounds(ownerBounds, BoundarySize));
        Assert.Equal(BpmnSemanticTypes.MessageBoundaryEvent, createdMessageElement.TypeId);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderTaskId,
            createdMessageElement.AttachedToElementId);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            composition.Document.SemanticModel.GetScope(messageId).Id);

        await ExecuteAsync(session, new CreateBpmnSignalBoundaryEventCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            signalId,
            signalVisualId,
            BpmnDemoPipeline.ProcessOrderTaskId,
            signalPlacement.Side,
            signalPlacement.PositionOnSide,
            ownerBounds,
            "Child signal",
            cancelActivity: false,
            description: "Child-scope non-interrupting Signal Boundary Event.",
            targetScopeId: BpmnDemoPipeline.ProcessOrderScopeId));
        var createdSignalElement = Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == signalId);
        var createdSignalVisual = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == signalVisualId);
        var createdSignalGeometry = AssertBoundaryProjection(
            session,
            signalId,
            signalPlacement.ResolveBounds(ownerBounds, BoundarySize));
        Assert.Equal(BpmnSemanticTypes.SignalBoundaryEvent, createdSignalElement.TypeId);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderTaskId,
            createdSignalElement.AttachedToElementId);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            composition.Document.SemanticModel.GetScope(signalId).Id);
        Assert.Equal(
            createdMessageGeometry,
            NodeGeometry(session, messageId));
        Assert.Equal(
            seededTimerBefore,
            Assert.Single(
                composition.Document.VisualModel.VisualStates,
                visual => visual.Id ==
                    BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId));
        Assert.Equal(
            seededTimerGeometryBefore,
            NodeGeometry(session, BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId));

        await NavigateAsync(session, rootScopeId);
        AssertNotProjected(session, messageId);
        AssertNotProjected(session, signalId);

        var undoReturn = await session.UndoAsync();
        Assert.True(undoReturn.IsApplied, Diagnostics(undoReturn.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            session.CaptureState().ActiveScopeId);
        Assert.Equal(createdMessageGeometry, NodeGeometry(session, messageId));
        Assert.Equal(createdSignalGeometry, NodeGeometry(session, signalId));

        var undoSignal = await session.UndoAsync();
        Assert.True(undoSignal.IsCommitted, Diagnostics(undoSignal.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.False(composition.Document.SemanticModel.TryGetElement(signalId, out _));
        Assert.False(composition.Document.VisualModel.TryGetVisualState(
            signalVisualId,
            out _));
        AssertNotProjected(session, signalId);
        Assert.Equal(createdMessageElement, Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == messageId));
        Assert.Equal(createdMessageVisual, Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == messageVisualId));
        Assert.Equal(createdMessageGeometry, NodeGeometry(session, messageId));

        var undoMessage = await session.UndoAsync();
        Assert.True(undoMessage.IsCommitted, Diagnostics(undoMessage.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.False(composition.Document.SemanticModel.TryGetElement(messageId, out _));
        Assert.False(composition.Document.VisualModel.TryGetVisualState(
            messageVisualId,
            out _));
        AssertNotProjected(session, messageId);
        Assert.Equal(seededTimerGeometryBefore, NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId));

        var undoOpen = await session.UndoAsync();
        Assert.True(undoOpen.IsApplied, Diagnostics(undoOpen.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        var redoOpen = await session.RedoAsync();
        Assert.True(redoOpen.IsApplied, Diagnostics(redoOpen.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            session.CaptureState().ActiveScopeId);
        AssertNotProjected(session, messageId);
        AssertNotProjected(session, signalId);

        var redoMessage = await session.RedoAsync();
        Assert.True(redoMessage.IsCommitted, Diagnostics(redoMessage.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(createdMessageElement, Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == messageId));
        Assert.Equal(createdMessageVisual, Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == messageVisualId));
        Assert.Equal(createdMessageGeometry, AssertBoundaryProjection(
            session,
            messageId,
            messagePlacement.ResolveBounds(ownerBounds, BoundarySize)));

        var redoSignal = await session.RedoAsync();
        Assert.True(redoSignal.IsCommitted, Diagnostics(redoSignal.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(createdSignalElement, Assert.Single(
            composition.Document.SemanticModel.Elements,
            element => element.Id == signalId));
        Assert.Equal(createdSignalVisual, Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == signalVisualId));
        Assert.Equal(createdSignalGeometry, AssertBoundaryProjection(
            session,
            signalId,
            signalPlacement.ResolveBounds(ownerBounds, BoundarySize)));
        Assert.Equal(createdMessageGeometry, NodeGeometry(session, messageId));

        var redoReturn = await session.RedoAsync();
        Assert.True(redoReturn.IsApplied, Diagnostics(redoReturn.Diagnostics));
        await WaitForIdleAsync(session);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        AssertNotProjected(session, messageId);
        AssertNotProjected(session, signalId);

        await NavigateAsync(session, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(createdMessageGeometry, AssertBoundaryProjection(
            session,
            messageId,
            messagePlacement.ResolveBounds(ownerBounds, BoundarySize)));
        Assert.Equal(createdSignalGeometry, AssertBoundaryProjection(
            session,
            signalId,
            signalPlacement.ResolveBounds(ownerBounds, BoundarySize)));
        Assert.Equal(seededTimerGeometryBefore, NodeGeometry(
            session,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId));
    }

    [Theory]
    [MemberData(nameof(BoundaryTypes))]
    public async Task NewlyCreatedBoundaryBodyEdgeOffersSourceOnlyAnchorContext(
        SemanticTypeId boundaryType)
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            Renderer($"phase-n91-first-anchor-{boundaryType.Value}"),
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var elementId = new SemanticElementId(
            $"bpmn:n9.1:first-anchor:{boundaryType.Value}");
        var visualStateId = new VisualStateId($"{elementId.Value}:visual");
        var ownerBounds = NodeGeometry(session, BpmnDemoPipeline.TaskId).Bounds;
        var placement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.25d);
        await ExecuteAsync(
            session,
            CreateBoundaryCommand(
                boundaryType,
                composition.Document.DocumentId,
                composition.Document.Revision,
                elementId,
                visualStateId,
                ownerBounds,
                placement));

        var created = Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == visualStateId);
        Assert.Empty(created.ConnectorAnchors);

        var state = session.CaptureState();
        var selected = await session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [visualStateId],
            viewport: state.EditorState.Viewport));
        Assert.True(selected.Succeeded, Diagnostics(selected.Diagnostics));
        await WaitForIdleAsync(session);

        state = session.CaptureState();
        var boundaryGeometry = NodeGeometry(session, elementId);
        var before = composition.Document.CaptureSnapshot();
        var beforeHistoryCount = state.HistoryStatus.EntryCount;
        await using var interaction = new Canvas2DInteractionController(session);
        var result = await interaction.PointerContextMenuAsync(new PointD(
            boundaryGeometry.Bounds.Right,
            boundaryGeometry.Bounds.Top + (boundaryGeometry.Bounds.Height / 2d)));

        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            result.ConnectorAnchorContextAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
        Assert.Equal(visualStateId, action.TargetVisualStateId);
        Assert.Equal(ConnectorAnchorSide.Right, action.Side);
        Assert.True(action.CanAdd(ConnectorAnchorRole.Source));
        Assert.False(action.CanAdd(ConnectorAnchorRole.Target));
        Assert.Equal(action.SourceSceneObjectId, action.TargetSceneObjectId);
        Assert.Equal(before.Revision, composition.Document.Revision);
        Assert.Equal(
            beforeHistoryCount,
            session.CaptureState().HistoryStatus.EntryCount);
        Assert.Empty(Assert.Single(
            composition.Document.VisualModel.VisualStates,
            visual => visual.Id == visualStateId).ConnectorAnchors);
    }

    private static LayoutNodeGeometry AssertBoundaryProjection(
        EditingSession session,
        SemanticElementId semanticElementId,
        RectD expectedBounds)
    {
        var state = session.CaptureState();
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == semanticElementId);
        Assert.Equal(
            NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize,
            node.GeometryInteractionPolicy);
        var label = Assert.Single(graph.Labels, candidate => candidate.OwnerId == node.Id);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, label.NodePlacement?.Kind);
        Assert.Equal(
            NodeLabelInteractionPolicy.MoveAndResize,
            label.NodeInteractionPolicy);

        var geometry = NodeGeometry(session, semanticElementId);
        Assert.Equal(expectedBounds, geometry.Bounds);
        Assert.Equal(BoundarySize, geometry.Bounds.Size);
        return geometry;
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
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
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

    private static ICommand CreateBoundaryCommand(
        SemanticTypeId boundaryType,
        DocumentId documentId,
        DocumentRevision revision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        RectD ownerBounds,
        BoundaryAttachmentPlacement placement) => boundaryType.Value switch
        {
            "BPMN.TimerBoundaryEvent" => new CreateBpmnTimerBoundaryEventCommand(
                documentId,
                revision,
                elementId,
                visualStateId,
                BpmnDemoPipeline.TaskId,
                placement.Side,
                placement.PositionOnSide,
                ownerBounds,
                "First-anchor Timer Boundary Event"),
            "BPMN.MessageBoundaryEvent" => new CreateBpmnMessageBoundaryEventCommand(
                documentId,
                revision,
                elementId,
                visualStateId,
                BpmnDemoPipeline.TaskId,
                placement.Side,
                placement.PositionOnSide,
                ownerBounds,
                "First-anchor Message Boundary Event"),
            "BPMN.SignalBoundaryEvent" => new CreateBpmnSignalBoundaryEventCommand(
                documentId,
                revision,
                elementId,
                visualStateId,
                BpmnDemoPipeline.TaskId,
                placement.Side,
                placement.PositionOnSide,
                ownerBounds,
                "First-anchor Signal Boundary Event"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(boundaryType),
                boundaryType,
                "The Boundary Event type must be supported by N9.1."),
        };

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
            new Canvas2DSurfaceSize(1000d, 700d, 1.25d)).AsTask().GetAwaiter()
            .GetResult();
        Assert.True(
            initialization.Succeeded,
            Diagnostics(initialization.Diagnostics));
        return renderer;
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));
}
