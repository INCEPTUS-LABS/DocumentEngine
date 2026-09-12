using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN5ModelValidationIntegrationTests
{
    [Fact]
    public async Task ReadyHostValidationIsReadOnlySelectableAndInvalidatedByMutation()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document;
        var beforeDocument = document.CaptureSnapshot();
        var beforeState = harness.State;
        var beforeScene = beforeState.CurrentScene;
        var beforeGraph = beforeState.ProjectedGraph;
        var beforeLayout = beforeState.LayoutResult;
        var beforeRouting = beforeState.RoutingResult;
        var beforeRenderCount = harness.Execution.RenderCount;

        var validation = Assert.IsType<ValidationSnapshot>(
            await harness.Host.ValidateAsync());

        Assert.Equal(13, validation.Issues.Length);
        Assert.Contains(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart &&
            issue.Message ==
                "Receive Task 90 \"Await customer event\" " +
                "[Code: AWAIT_CUSTOMER_EVENT, ID: demo:bpmn:await-event-task] " +
                "is not reachable from any Start Event.");
        Assert.Contains(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeCannotReachEnd &&
            issue.Message ==
                "Message Catch Event \"Customer message\" " +
                "[ID: demo:bpmn:message-catch-event] cannot reach any End Event.");
        Assert.Contains(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart &&
            issue.Message ==
                "Event-Based Gateway \"Await customer event\" " +
                "[Code: AWAIT_EVENT, ID: demo:bpmn:event-based-gateway] " +
                "is not reachable from any Start Event.");
        var afterValidation = harness.State;
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(beforeState.DocumentRevision, afterValidation.DocumentRevision);
        Assert.Equal(beforeState.Generation, afterValidation.Generation);
        Assert.Equal(beforeState.HistoryStatus, afterValidation.HistoryStatus);
        Assert.Equal(beforeState.EditorState, afterValidation.EditorState);
        Assert.Same(beforeScene, afterValidation.CurrentScene);
        Assert.Same(beforeGraph, afterValidation.ProjectedGraph);
        Assert.Same(beforeLayout, afterValidation.LayoutResult);
        Assert.Same(beforeRouting, afterValidation.RoutingResult);
        Assert.Equal(beforeRenderCount, harness.Execution.RenderCount);

        var targeted = validation.Issues.First(issue => issue.Target.VisualStateId is not null);
        var selection = await harness.Host.SelectValidationIssueAsync(targeted.Id);
        Assert.Equal(Canvas2DInteractionStatus.Updated, selection?.Status);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(targeted.Target.VisualStateId,
            Assert.Single(harness.State.EditorState.Selection));
        Assert.Equal(beforeState.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Same(validation, harness.Host.CaptureState().ValidationSnapshot);

        var movedVisual = beforeDocument.VisualModel.VisualStates.First(visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);
        var mutation = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            beforeDocument.DocumentId,
            beforeDocument.Revision,
            movedVisual.Id,
            movedVisual.Position + new VectorD(2d, 1d),
            VisualPlacementMode.Pinned));
        Assert.True(mutation.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Null(harness.Host.CaptureState().ValidationSnapshot);

        var refreshed = Assert.IsType<ValidationSnapshot>(
            await harness.Host.ValidateAsync());
        Assert.Equal(document.Revision, refreshed.SourceRevision);
        Assert.NotEqual(validation.SourceRevision, refreshed.SourceRevision);
    }
}
