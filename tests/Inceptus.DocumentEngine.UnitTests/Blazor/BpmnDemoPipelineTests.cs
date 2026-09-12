using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class BpmnDemoPipelineTests
{
    [Fact]
    public async Task EmbeddedNativeSamplePreservesTheAcceptedBranchingDemoModel()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var snapshot = composition.Document.CaptureSnapshot();

        Assert.Equal(BpmnDemoPipeline.DemoDocumentId, snapshot.DocumentId);
        Assert.Equal(new DocumentRevision(103), snapshot.Revision);
        Assert.Equal(26, snapshot.SemanticModel.ElementCount);
        Assert.Equal(25, snapshot.SemanticModel.RelationshipCount);
        Assert.Equal(51, snapshot.VisualModel.Count);

        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.StartEventId,
            out var startEvent));
        Assert.Equal(BpmnSemanticTypes.StartEvent, startEvent!.TypeId);
        Assert.True(snapshot.SemanticModel.TryGetElement(BpmnDemoPipeline.TaskId, out var task));
        Assert.Equal(BpmnSemanticTypes.UserTask, task!.TypeId);
        Assert.Equal("REVIEW_ORDER", task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Review order", task.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            20L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.Equal(
            "Review the incoming customer order.\nCheck completeness before approval.",
            task.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ExclusiveGatewayId,
            out var gateway));
        Assert.Equal(BpmnSemanticTypes.ExclusiveGateway, gateway!.TypeId);
        Assert.Equal("APPROVED", gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Approved?", gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "Route the process according to the approval decision.",
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ApprovedTaskId,
            out var approvedTask));
        Assert.Equal(30L, approvedTask!.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.RejectedTaskId,
            out var rejectedTask));
        Assert.Equal(40L, rejectedTask!.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
        AssertParallelGateway(
            snapshot,
            BpmnDemoPipeline.ParallelSplitGatewayId,
            "PARALLEL_SPLIT",
            "Process in parallel",
            "Start both parallel branches.");
        AssertParallelGateway(
            snapshot,
            BpmnDemoPipeline.ParallelJoinGatewayId,
            "PARALLEL_JOIN",
            "Synchronise",
            "Join the parallel branches.");
        AssertInclusiveGateway(
            snapshot,
            BpmnDemoPipeline.InclusiveSplitGatewayId,
            "OPTIONAL_SERVICES",
            "Optional services",
            "Select one or more optional process branches.");
        AssertInclusiveGateway(
            snapshot,
            BpmnDemoPipeline.InclusiveJoinGatewayId,
            "OPTIONAL_SERVICES_JOIN",
            "Continue after selected services",
            "Merge the selected optional branches.");
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.PrepareShipmentTaskId,
            out var prepareShipment));
        Assert.Equal(BpmnSemanticTypes.ManualTask, prepareShipment!.TypeId);
        Assert.Equal(50L, prepareShipment!.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.NotifyCustomerTaskId,
            out var notifyCustomer));
        Assert.Equal(BpmnSemanticTypes.SendTask, notifyCustomer!.TypeId);
        Assert.Equal(60L, notifyCustomer!.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.AddInsuranceTaskId,
            out var addInsurance));
        Assert.Equal(70L, addInsurance!.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.AddGiftWrapTaskId,
            out var addGiftWrap));
        Assert.Equal(80L, addGiftWrap!.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.EndEventId,
            out var endEvent));
        Assert.Equal(BpmnSemanticTypes.EndEvent, endEvent!.TypeId);

        Assert.Equal(
            52,
            snapshot.VisualModel.VisualStates.Sum(static visual =>
                visual.ConnectorAnchors.Length));
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.StartEventVisualId,
            BpmnDemoPipeline.StartEventSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.TaskVisualId,
            BpmnDemoPipeline.TaskTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.TaskVisualId,
            BpmnDemoPipeline.TaskSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            BpmnDemoPipeline.ExclusiveGatewayTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            BpmnDemoPipeline.ExclusiveGatewayApprovedSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            BpmnDemoPipeline.ExclusiveGatewayRejectedSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            1);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ApprovedTaskVisualId,
            BpmnDemoPipeline.ApprovedTaskTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ApprovedTaskVisualId,
            BpmnDemoPipeline.ApprovedTaskSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ApprovedTaskVisualId,
            BpmnDemoPipeline.ApprovedTaskReconnectTargetAnchorId,
            ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.RejectedTaskVisualId,
            BpmnDemoPipeline.RejectedTaskTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.RejectedTaskVisualId,
            BpmnDemoPipeline.RejectedTaskSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.RejectedTaskVisualId,
            BpmnDemoPipeline.RejectedTaskReconnectSourceAnchorId,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            BpmnDemoPipeline.ParallelSplitTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            BpmnDemoPipeline.ParallelSplitPrepareSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            BpmnDemoPipeline.ParallelSplitNotifySourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            1);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.PrepareShipmentTaskVisualId,
            BpmnDemoPipeline.PrepareShipmentTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.PrepareShipmentTaskVisualId,
            BpmnDemoPipeline.PrepareShipmentSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.NotifyCustomerTaskVisualId,
            BpmnDemoPipeline.NotifyCustomerTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.NotifyCustomerTaskVisualId,
            BpmnDemoPipeline.NotifyCustomerSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ParallelJoinGatewayVisualId,
            BpmnDemoPipeline.ParallelJoinPrepareTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ParallelJoinGatewayVisualId,
            BpmnDemoPipeline.ParallelJoinNotifyTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            1);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ParallelJoinGatewayVisualId,
            BpmnDemoPipeline.ParallelJoinSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            BpmnDemoPipeline.InclusiveSplitTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            BpmnDemoPipeline.InclusiveSplitInsuranceSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            BpmnDemoPipeline.InclusiveSplitGiftWrapSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            1);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.AddInsuranceTaskVisualId,
            BpmnDemoPipeline.AddInsuranceTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.AddInsuranceTaskVisualId,
            BpmnDemoPipeline.AddInsuranceSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.AddGiftWrapTaskVisualId,
            BpmnDemoPipeline.AddGiftWrapTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.AddGiftWrapTaskVisualId,
            BpmnDemoPipeline.AddGiftWrapSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.InclusiveJoinGatewayVisualId,
            BpmnDemoPipeline.InclusiveJoinInsuranceTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.InclusiveJoinGatewayVisualId,
            BpmnDemoPipeline.InclusiveJoinGiftWrapTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            1);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.InclusiveJoinGatewayVisualId,
            BpmnDemoPipeline.InclusiveJoinSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.EndEventVisualId,
            BpmnDemoPipeline.EndEventInclusiveTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.EndEventVisualId,
            BpmnDemoPipeline.EndEventRejectedTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            1);

        AssertFlow(
            snapshot,
            BpmnDemoPipeline.FirstSequenceFlowId,
            BpmnDemoPipeline.FirstSequenceFlowVisualId,
            BpmnDemoPipeline.StartEventId,
            BpmnDemoPipeline.TaskId,
            BpmnDemoPipeline.StartEventSourceAnchorId,
            BpmnDemoPipeline.TaskTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.SecondSequenceFlowId,
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            BpmnDemoPipeline.TaskId,
            BpmnDemoPipeline.ExclusiveGatewayId,
            BpmnDemoPipeline.TaskSourceAnchorId,
            BpmnDemoPipeline.ExclusiveGatewayTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.ThirdSequenceFlowId,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId,
            BpmnDemoPipeline.ExclusiveGatewayId,
            BpmnDemoPipeline.ApprovedTaskId,
            BpmnDemoPipeline.ExclusiveGatewayApprovedSourceAnchorId,
            BpmnDemoPipeline.ApprovedTaskTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.FourthSequenceFlowId,
            BpmnDemoPipeline.FourthSequenceFlowVisualId,
            BpmnDemoPipeline.ExclusiveGatewayId,
            BpmnDemoPipeline.RejectedTaskId,
            BpmnDemoPipeline.ExclusiveGatewayRejectedSourceAnchorId,
            BpmnDemoPipeline.RejectedTaskTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.FifthSequenceFlowId,
            BpmnDemoPipeline.FifthSequenceFlowVisualId,
            BpmnDemoPipeline.ApprovedTaskId,
            BpmnDemoPipeline.ParallelSplitGatewayId,
            BpmnDemoPipeline.ApprovedTaskSourceAnchorId,
            BpmnDemoPipeline.ParallelSplitTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.SixthSequenceFlowId,
            BpmnDemoPipeline.SixthSequenceFlowVisualId,
            BpmnDemoPipeline.RejectedTaskId,
            BpmnDemoPipeline.EndEventId,
            BpmnDemoPipeline.RejectedTaskSourceAnchorId,
            BpmnDemoPipeline.EndEventRejectedTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.SeventhSequenceFlowId,
            BpmnDemoPipeline.SeventhSequenceFlowVisualId,
            BpmnDemoPipeline.ParallelSplitGatewayId,
            BpmnDemoPipeline.PrepareShipmentTaskId,
            BpmnDemoPipeline.ParallelSplitPrepareSourceAnchorId,
            BpmnDemoPipeline.PrepareShipmentTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.EighthSequenceFlowId,
            BpmnDemoPipeline.EighthSequenceFlowVisualId,
            BpmnDemoPipeline.ParallelSplitGatewayId,
            BpmnDemoPipeline.NotifyCustomerTaskId,
            BpmnDemoPipeline.ParallelSplitNotifySourceAnchorId,
            BpmnDemoPipeline.NotifyCustomerTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.NinthSequenceFlowId,
            BpmnDemoPipeline.NinthSequenceFlowVisualId,
            BpmnDemoPipeline.PrepareShipmentTaskId,
            BpmnDemoPipeline.ParallelJoinGatewayId,
            BpmnDemoPipeline.PrepareShipmentSourceAnchorId,
            BpmnDemoPipeline.ParallelJoinPrepareTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.TenthSequenceFlowId,
            BpmnDemoPipeline.TenthSequenceFlowVisualId,
            BpmnDemoPipeline.NotifyCustomerTaskId,
            BpmnDemoPipeline.ParallelJoinGatewayId,
            BpmnDemoPipeline.NotifyCustomerSourceAnchorId,
            BpmnDemoPipeline.ParallelJoinNotifyTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.EleventhSequenceFlowId,
            BpmnDemoPipeline.EleventhSequenceFlowVisualId,
            BpmnDemoPipeline.ParallelJoinGatewayId,
            BpmnDemoPipeline.InclusiveSplitGatewayId,
            BpmnDemoPipeline.ParallelJoinSourceAnchorId,
            BpmnDemoPipeline.InclusiveSplitTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.TwelfthSequenceFlowId,
            BpmnDemoPipeline.TwelfthSequenceFlowVisualId,
            BpmnDemoPipeline.InclusiveSplitGatewayId,
            BpmnDemoPipeline.AddInsuranceTaskId,
            BpmnDemoPipeline.InclusiveSplitInsuranceSourceAnchorId,
            BpmnDemoPipeline.AddInsuranceTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.ThirteenthSequenceFlowId,
            BpmnDemoPipeline.ThirteenthSequenceFlowVisualId,
            BpmnDemoPipeline.InclusiveSplitGatewayId,
            BpmnDemoPipeline.AddGiftWrapTaskId,
            BpmnDemoPipeline.InclusiveSplitGiftWrapSourceAnchorId,
            BpmnDemoPipeline.AddGiftWrapTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.FourteenthSequenceFlowId,
            BpmnDemoPipeline.FourteenthSequenceFlowVisualId,
            BpmnDemoPipeline.AddInsuranceTaskId,
            BpmnDemoPipeline.InclusiveJoinGatewayId,
            BpmnDemoPipeline.AddInsuranceSourceAnchorId,
            BpmnDemoPipeline.InclusiveJoinInsuranceTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.FifteenthSequenceFlowId,
            BpmnDemoPipeline.FifteenthSequenceFlowVisualId,
            BpmnDemoPipeline.AddGiftWrapTaskId,
            BpmnDemoPipeline.InclusiveJoinGatewayId,
            BpmnDemoPipeline.AddGiftWrapSourceAnchorId,
            BpmnDemoPipeline.InclusiveJoinGiftWrapTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.SixteenthSequenceFlowId,
            BpmnDemoPipeline.SixteenthSequenceFlowVisualId,
            BpmnDemoPipeline.InclusiveJoinGatewayId,
            BpmnDemoPipeline.EndEventId,
            BpmnDemoPipeline.InclusiveJoinSourceAnchorId,
            BpmnDemoPipeline.EndEventInclusiveTargetAnchorId);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ProcessOrderSubProcessId,
            out var processOrder));
        Assert.Equal(BpmnSemanticTypes.SubProcess, processOrder!.TypeId);
        Assert.Equal(
            "Process order",
            processOrder.Properties[BpmnSemanticProperties.Name].TextValue);
        var processOrderScope = Assert.Single(snapshot.SemanticModel.NestedScopes, scope =>
            scope.Id == BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Equal(snapshot.SemanticModel.RootScopeId, processOrderScope.ParentScopeId);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderSubProcessId,
            processOrderScope.OwnerSemanticElementId);
        Assert.Equal(
            new[]
            {
                BpmnDemoPipeline.ProcessOrderStartEventId,
                BpmnDemoPipeline.ProcessOrderTaskId,
                BpmnDemoPipeline.ProcessOrderEndEventId,
                BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
                BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId,
            }.OrderBy(static id => id.Value, StringComparer.Ordinal),
            snapshot.SemanticModel.ScopeMemberships
                .Where(membership =>
                    membership.ScopeId == BpmnDemoPipeline.ProcessOrderScopeId)
                .Select(static membership => membership.SemanticElementId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal));
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.ProcessOrderFirstFlowId,
            BpmnDemoPipeline.ProcessOrderFirstFlowVisualId,
            BpmnDemoPipeline.ProcessOrderStartEventId,
            BpmnDemoPipeline.ProcessOrderTaskId,
            BpmnDemoPipeline.ProcessOrderStartSourceAnchorId,
            BpmnDemoPipeline.ProcessOrderTaskTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.ProcessOrderSecondFlowId,
            BpmnDemoPipeline.ProcessOrderSecondFlowVisualId,
            BpmnDemoPipeline.ProcessOrderTaskId,
            BpmnDemoPipeline.ProcessOrderEndEventId,
            BpmnDemoPipeline.ProcessOrderTaskSourceAnchorId,
            BpmnDemoPipeline.ProcessOrderEndTargetAnchorId);
        Assert.True(snapshot.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
            out var reviewTimeout));
        Assert.Equal(BpmnSemanticTypes.TimerBoundaryEvent, reviewTimeout!.TypeId);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderTaskId, reviewTimeout.AttachedToElementId);
        Assert.Equal(
            "Review timeout",
            reviewTimeout.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "PT15M",
            reviewTimeout.Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
        Assert.True(
            reviewTimeout.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.True(snapshot.VisualModel.TryGetVisualState(
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId,
            out var reviewTimeoutVisual));
        Assert.Equal(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d),
            reviewTimeoutVisual!.BoundaryAttachment);
        Assert.Equal(new SizeD(36d, 36d), reviewTimeoutVisual.Size);
        AssertAnchor(
            snapshot,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutSourceAnchorId,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Source,
            0);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowId,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowVisualId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutSourceAnchorId,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTargetAnchorId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.ProcessOrderTimeoutHandlerFlowId,
            BpmnDemoPipeline.ProcessOrderTimeoutHandlerFlowVisualId,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId,
            BpmnDemoPipeline.ProcessOrderEndEventId,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutSourceAnchorId,
            BpmnDemoPipeline.ProcessOrderTimeoutEndTargetAnchorId);
        var expectedVisualIds = new[]
        {
            BpmnDemoPipeline.StartEventVisualId,
            BpmnDemoPipeline.TaskVisualId,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            BpmnDemoPipeline.ApprovedTaskVisualId,
            BpmnDemoPipeline.RejectedTaskVisualId,
            BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            BpmnDemoPipeline.PrepareShipmentTaskVisualId,
            BpmnDemoPipeline.NotifyCustomerTaskVisualId,
            BpmnDemoPipeline.ParallelJoinGatewayVisualId,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            BpmnDemoPipeline.AddInsuranceTaskVisualId,
            BpmnDemoPipeline.AddGiftWrapTaskVisualId,
            BpmnDemoPipeline.InclusiveJoinGatewayVisualId,
            BpmnDemoPipeline.EndEventVisualId,
            BpmnDemoPipeline.AwaitEventTaskVisualId,
            BpmnDemoPipeline.ProcessMessageTaskVisualId,
            BpmnDemoPipeline.HandleTimeoutTaskVisualId,
            BpmnDemoPipeline.EventBasedGatewayVisualId,
            BpmnDemoPipeline.MessageCatchEventVisualId,
            BpmnDemoPipeline.TimerCatchEventVisualId,
            BpmnDemoPipeline.FirstSequenceFlowVisualId,
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId,
            BpmnDemoPipeline.FourthSequenceFlowVisualId,
            BpmnDemoPipeline.FifthSequenceFlowVisualId,
            BpmnDemoPipeline.SixthSequenceFlowVisualId,
            BpmnDemoPipeline.SeventhSequenceFlowVisualId,
            BpmnDemoPipeline.EighthSequenceFlowVisualId,
            BpmnDemoPipeline.NinthSequenceFlowVisualId,
            BpmnDemoPipeline.TenthSequenceFlowVisualId,
            BpmnDemoPipeline.EleventhSequenceFlowVisualId,
            BpmnDemoPipeline.TwelfthSequenceFlowVisualId,
            BpmnDemoPipeline.ThirteenthSequenceFlowVisualId,
            BpmnDemoPipeline.FourteenthSequenceFlowVisualId,
            BpmnDemoPipeline.FifteenthSequenceFlowVisualId,
            BpmnDemoPipeline.SixteenthSequenceFlowVisualId,
            BpmnDemoPipeline.SeventeenthSequenceFlowVisualId,
            BpmnDemoPipeline.EighteenthSequenceFlowVisualId,
            BpmnDemoPipeline.NineteenthSequenceFlowVisualId,
            BpmnDemoPipeline.TwentiethSequenceFlowVisualId,
            BpmnDemoPipeline.TwentyFirstSequenceFlowVisualId,
            BpmnDemoPipeline.ProcessOrderSubProcessVisualId,
            BpmnDemoPipeline.ProcessOrderStartEventVisualId,
            BpmnDemoPipeline.ProcessOrderTaskVisualId,
            BpmnDemoPipeline.ProcessOrderEndEventVisualId,
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId,
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskVisualId,
            BpmnDemoPipeline.ProcessOrderFirstFlowVisualId,
            BpmnDemoPipeline.ProcessOrderSecondFlowVisualId,
            BpmnDemoPipeline.ProcessOrderTimeoutFlowVisualId,
            BpmnDemoPipeline.ProcessOrderTimeoutHandlerFlowVisualId,
        }.OrderBy(static id => id.Value, StringComparer.Ordinal);
        Assert.Equal(
            expectedVisualIds,
            snapshot.VisualModel.VisualStates.Select(static visual => visual.Id));
    }

    [Fact]
    public async Task CompositionCombinesBpmnAndOrganizationalRegistrationsAndCompletesOneRootPipeline()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var configuration = composition.Configuration;
        var snapshot = composition.Document.CaptureSnapshot();

        Assert.Equal(BpmnAlgorithmIds.DefaultLayout, configuration.LayoutAlgorithmId);
        Assert.Equal(BpmnAlgorithmIds.DefaultRouting, configuration.RoutingAlgorithmId);
        Assert.Equal(ViewportSnapshot.Default,
            configuration.InitialEditorState.Viewport);
        Assert.Equal(
            BpmnPluginRegistration.N100.CommandHandlers.AddRange(OrganizationalRegistration().CommandHandlers)
                .Select(static item => item.TypeId),
            configuration.CommandHandlers.Select(static item => item.TypeId));
        Assert.Equal(
            BpmnPluginRegistration.N100.CommandValidators.AddRange(OrganizationalRegistration().CommandValidators)
                .Select(static item => item.ValidatorId),
            configuration.CommandValidators.Select(static item => item.ValidatorId));
        Assert.Equal(
            BpmnPluginRegistration.N100.HistoryPolicies.AddRange(OrganizationalRegistration().HistoryPolicies)
                .Select(static item => item.TypeId),
            configuration.HistoryPolicies.Select(static item => item.TypeId));
        Assert.Equal(
            BpmnPluginRegistration.N100.PropertiesSchemas.AddRange(OrganizationalRegistration().PropertiesSchemas)
                .OrderBy(static schema => schema.SemanticTypeId.Value, StringComparer.Ordinal),
            composition.PropertiesSchemaCatalog.Schemas.AsEnumerable());
        Assert.Equal(
            BpmnPluginRegistration.N100.ConnectorEndpointReconnectionRegistrations.AsEnumerable(),
            composition.EndpointReconnectionCatalog.Registrations.AsEnumerable());
        Assert.Equal(
            BpmnPluginRegistration.N100.DiagramDeletionRegistrations
                .AddRange(OrganizationalRegistration().DiagramDeletionRegistrations)
                .Select(static item => item.DeletionId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal),
            composition.DeletionCatalog.Registrations.Select(static item => item.DeletionId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal));
        Assert.Equal(
            BpmnPluginRegistration.N100.ScopeNavigationRegistrations.AsEnumerable(),
            composition.ScopeNavigationCatalog.Registrations.AsEnumerable());
        Assert.Equal(
            BpmnPluginRegistration.N100.ModelProfileDefinitions.AsEnumerable(),
            configuration.ModelProfileCatalog.Definitions.AsEnumerable());
        Assert.Equal(
            OrganizationalRegistration().BackgroundActions.AsEnumerable(),
            composition.BackgroundActionCatalog.Definitions.AsEnumerable());
        Assert.Equal(
            OrganizationalRegistration().SemanticSceneCommandActions.AsEnumerable(),
            composition.SemanticSceneCommandActionCatalog.Definitions.AsEnumerable());
        Assert.Equal(
            OrganizationalRegistration().SemanticSceneViewActions
                .OrderBy(static definition => definition.Order)
                .ThenBy(static definition => definition.Id.Value, StringComparer.Ordinal),
            composition.SemanticSceneViewActionCatalog.Definitions.AsEnumerable());

        var expectedToolbox = new ToolboxCatalog(
            BpmnPluginRegistration.N100.ToolboxContributions);
        Assert.Equal(
            expectedToolbox.Sections.Select(static section => section.SectionId),
            BpmnModelerCompositionFactory.ToolboxCatalog.Sections.Select(
                static section => section.SectionId));
        Assert.Equal(
            expectedToolbox.Groups.Select(static group => group.GroupId),
            BpmnModelerCompositionFactory.ToolboxCatalog.Groups.Select(
                static group => group.GroupId));
        Assert.Equal(
            expectedToolbox.Items.Select(static item => item.ItemId),
            BpmnModelerCompositionFactory.ToolboxCatalog.Items.Select(
                static item => item.ItemId));
        Assert.Equal(
            BpmnPluginRegistration.N100.ToolboxPlacementRegistrations
                .Select(static registration => registration.ToolboxItemId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal),
            composition.ToolboxPlacementCatalog.Registrations
                .Select(static registration => registration.ToolboxItemId));
        Assert.Equal(
            BpmnPluginRegistration.N100.AnchorConnectionCreationRegistrations
                .Select(static registration => registration.CreationId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal),
            composition.AnchorConnectionCreationCatalog.Registrations
                .Select(static registration => registration.CreationId));
        Assert.Equal(
            BpmnPluginRegistration.N100.ModelValidationRules
                .AddRange(OrganizationalRegistration().ModelValidationRules)
                .Select(static rule => rule.RuleId)
                .OrderBy(static id => id.Value, StringComparer.Ordinal),
            composition.ModelValidationCatalog.Rules.Select(static rule => rule.RuleId));

        var projectionExecution = configuration.ProjectionEngine.Project(snapshot);
        Assert.True(projectionExecution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(projectionExecution.Graph);
        var layoutExecution = configuration.LayoutEngine.Layout(
            graph,
            configuration.LayoutAlgorithmId);
        Assert.True(layoutExecution.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = configuration.RoutingEngine.Route(
            graph,
            layout,
            configuration.RoutingAlgorithmId);
        Assert.True(routingExecution.IsSuccessful);
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = configuration.SceneBuilder.Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            configuration.InitialEditorState);

        Assert.True(sceneExecution.Succeeded);
        Assert.Equal(21, graph.NodeCount);
        Assert.Equal(21, graph.EdgeCount);
        Assert.Equal(44, graph.Ports.Length);
        Assert.Equal(
            [
                "Add gift wrap",
                "Add insurance",
                "Approved task",
                "Approved?",
                "Await customer event",
                "Await customer event",
                "Continue after selected services",
                "Customer message",
                "Handle timeout",
                "Notify customer",
                "Optional services",
                "Prepare shipment",
                "Process in parallel",
                "Process message",
                "Process order",
                "Rejected task",
                "Response timeout",
                "Review order",
                "Synchronise",
            ],
            graph.Labels.Select(static label => label.Text).Order(StringComparer.Ordinal));
        var gatewayNode = Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        var gatewayLabel = Assert.Single(graph.Labels, label => label.OwnerId == gatewayNode.Id);
        var gatewayPlacement = Assert.IsType<NodeLabelPlacement>(gatewayLabel.NodePlacement);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, gatewayPlacement.Kind);
        Assert.Equal(8d, gatewayPlacement.Gap);
        Assert.Equal(160d, gatewayPlacement.MaximumWidth);
        Assert.Equal(
            NodeLabelInteractionPolicy.MoveAndResize,
            gatewayLabel.NodeInteractionPolicy);
        var parallelGatewayLabels = graph.Labels.Where(label =>
            label.Source.SemanticTypeId == BpmnSemanticTypes.ParallelGateway).ToArray();
        Assert.Equal(2, parallelGatewayLabels.Length);
        Assert.All(parallelGatewayLabels, static label =>
        {
            var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
            Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
            Assert.Equal(8d, placement.Gap);
            Assert.Equal(160d, placement.MaximumWidth);
            Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize, label.NodeInteractionPolicy);
        });
        var inclusiveGatewayLabels = graph.Labels.Where(label =>
            label.Source.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway).ToArray();
        Assert.Equal(2, inclusiveGatewayLabels.Length);
        Assert.All(inclusiveGatewayLabels, static label =>
        {
            var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
            Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
            Assert.Equal(8d, placement.Gap);
            Assert.Equal(160d, placement.MaximumWidth);
            Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize, label.NodeInteractionPolicy);
        });
        var taskLabels = graph.Labels.Where(label =>
            BpmnTaskSemanticTypes.IsTask(label.Source.SemanticTypeId)).ToArray();
        Assert.Equal(10, taskLabels.Length);
        Assert.All(taskLabels, static label =>
        {
            Assert.Null(label.NodePlacement);
            Assert.Equal(NodeLabelInteractionPolicy.Fixed, label.NodeInteractionPolicy);
        });
        Assert.DoesNotContain(graph.Labels, label =>
            label.Source.SemanticTypeId == BpmnSemanticTypes.StartEvent ||
            label.Source.SemanticTypeId == BpmnSemanticTypes.EndEvent);
        Assert.Equal(21, layout.NodeCount);
        Assert.Equal(21, routing.RouteCount);
        Assert.NotNull(sceneExecution.Scene);
    }

    [Fact]
    public async Task FactoriesRemainIndependentAndNeutralDemoRemainsSelectable()
    {
        var first = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var second = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var neutral = await BpmnModelerTestComposition.NeutralFactory.CreateAsync();

        Assert.NotSame(first.Document, second.Document);
        Assert.Equal(first.Document.CaptureSnapshot(), second.Document.CaptureSnapshot());
        Assert.NotEqual(
            first.Document.DocumentId,
            neutral.Document.DocumentId);
        Assert.NotNull(neutral.Counters);
        Assert.Equal(3, neutral.Document.SemanticModel.ElementCount);
        Assert.Equal(2, neutral.Document.SemanticModel.RelationshipCount);
    }

    [Fact]
    public async Task FactoryHonorsCancellationBeforeCreatingTheDocument()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await BpmnModelerTestComposition.DemoFactory.CreateAsync(cancellation.Token));
    }

    private static void AssertFlow(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        SemanticElementId expectedId,
        VisualStateId expectedVisualId,
        SemanticElementId expectedSourceId,
        SemanticElementId expectedTargetId,
        ConnectorAnchorId expectedSourceAnchorId,
        ConnectorAnchorId expectedTargetAnchorId)
    {
        Assert.True(snapshot.SemanticModel.TryGetRelationship(expectedId, out var flow));
        Assert.NotNull(flow);
        Assert.Equal(expectedId, flow.Id);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, flow.TypeId);
        Assert.Equal(expectedSourceId, flow.SourceId);
        Assert.Equal(expectedTargetId, flow.TargetId);
        Assert.True(snapshot.VisualModel.TryGetVisualState(expectedVisualId, out var visual));
        Assert.Equal(expectedSourceAnchorId, visual!.SourceAnchorId);
        Assert.Equal(expectedTargetAnchorId, visual.TargetAnchorId);
    }

    private static void AssertParallelGateway(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        SemanticElementId id,
        string code,
        string name,
        string description)
    {
        Assert.True(snapshot.SemanticModel.TryGetElement(id, out var gateway));
        Assert.Equal(BpmnSemanticTypes.ParallelGateway, gateway!.TypeId);
        Assert.Equal(code, gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(name, gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            description,
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
    }

    private static void AssertInclusiveGateway(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        SemanticElementId id,
        string code,
        string name,
        string description)
    {
        Assert.True(snapshot.SemanticModel.TryGetElement(id, out var gateway));
        Assert.Equal(BpmnSemanticTypes.InclusiveGateway, gateway!.TypeId);
        Assert.Equal(code, gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(name, gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            description,
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
    }

    private static OrganizationalPluginRegistration OrganizationalRegistration() =>
        CreateOrganizationalRegistration();

    private static OrganizationalPluginRegistration CreateOrganizationalRegistration()
    {
        var eligibilityPolicy = new OrganizationalElementEligibilityPolicy(
            BpmnSemanticTypes.IsFlowNode);
        return OrganizationalPluginRegistration.Create(
            eligibilityPolicy,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibilityPolicy));
    }

    private static void AssertAnchor(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int order)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(visualStateId, out var visual));
        var anchor = Assert.Single(visual!.ConnectorAnchors, candidate =>
            candidate.Id == anchorId);
        Assert.Equal(side, anchor.Side);
        Assert.Equal(role, anchor.Role);
        Assert.Equal(order, anchor.Order);
    }
}
