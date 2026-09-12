using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Stable identities and loading entry point for the reference host's BPMN sample.
/// </summary>
internal static class BpmnDemoPipeline
{
    internal static DocumentId DemoDocumentId { get; } = new("demo:bpmn-document");

    internal static SemanticElementId StartEventId { get; } = new("demo:bpmn:start-event");

    internal static SemanticElementId TaskId { get; } = new("demo:bpmn:review-order");

    internal static SemanticElementId ExclusiveGatewayId { get; } =
        new("demo:bpmn:approved-gateway");

    internal static SemanticElementId ApprovedTaskId { get; } =
        new("demo:bpmn:approved-task");

    internal static SemanticElementId RejectedTaskId { get; } =
        new("demo:bpmn:rejected-task");

    internal static SemanticElementId ParallelSplitGatewayId { get; } =
        new("demo:bpmn:parallel-split");

    internal static SemanticElementId PrepareShipmentTaskId { get; } =
        new("demo:bpmn:prepare-shipment");

    internal static SemanticElementId NotifyCustomerTaskId { get; } =
        new("demo:bpmn:notify-customer");

    internal static SemanticElementId ParallelJoinGatewayId { get; } =
        new("demo:bpmn:parallel-join");

    internal static SemanticElementId InclusiveSplitGatewayId { get; } =
        new("demo:bpmn:inclusive-split");

    internal static SemanticElementId AddInsuranceTaskId { get; } =
        new("demo:bpmn:add-insurance");

    internal static SemanticElementId AddGiftWrapTaskId { get; } =
        new("demo:bpmn:add-gift-wrap");

    internal static SemanticElementId InclusiveJoinGatewayId { get; } =
        new("demo:bpmn:inclusive-join");

    internal static SemanticElementId EndEventId { get; } = new("demo:bpmn:end-event");

    internal static SemanticElementId EventBasedGatewayId { get; } =
        new("demo:bpmn:event-based-gateway");

    internal static SemanticElementId MessageCatchEventId { get; } =
        new("demo:bpmn:message-catch-event");

    internal static SemanticElementId TimerCatchEventId { get; } =
        new("demo:bpmn:timer-catch-event");

    internal static SemanticElementId AwaitEventTaskId { get; } =
        new("demo:bpmn:await-event-task");

    internal static SemanticElementId ProcessMessageTaskId { get; } =
        new("demo:bpmn:process-message-task");

    internal static SemanticElementId HandleTimeoutTaskId { get; } =
        new("demo:bpmn:handle-timeout-task");

    internal static SemanticElementId ProcessOrderSubProcessId { get; } =
        new("demo:bpmn:process-order-subprocess");

    internal static DocumentScopeId ProcessOrderScopeId { get; } =
        new("demo:bpmn:scope:process-order");

    internal static SemanticElementId ProcessOrderStartEventId { get; } =
        new("demo:bpmn:process-order:start-event");

    internal static SemanticElementId ProcessOrderTaskId { get; } =
        new("demo:bpmn:process-order:task");

    internal static SemanticElementId ProcessOrderEndEventId { get; } =
        new("demo:bpmn:process-order:end-event");

    internal static SemanticElementId ProcessOrderReviewTimeoutEventId { get; } =
        new("demo:bpmn:process-order:review-timeout");

    internal static SemanticElementId ProcessOrderHandleTimeoutTaskId { get; } =
        new("demo:bpmn:process-order:handle-timeout");

    internal static SemanticElementId ProcessOrderFirstFlowId { get; } =
        new("demo:bpmn:process-order:flow:start-to-task");

    internal static SemanticElementId ProcessOrderSecondFlowId { get; } =
        new("demo:bpmn:process-order:flow:task-to-end");

    internal static SemanticElementId ProcessOrderTimeoutFlowId { get; } =
        new("demo:bpmn:process-order:flow:timeout-to-handler");

    internal static SemanticElementId ProcessOrderTimeoutHandlerFlowId { get; } =
        new("demo:bpmn:process-order:flow:handler-to-end");

    internal static SemanticElementId FirstSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:start-to-task");

    internal static SemanticElementId SecondSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:task-to-gateway");

    internal static SemanticElementId ThirdSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:gateway-to-approved");

    internal static SemanticElementId FourthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:gateway-to-rejected");

    internal static SemanticElementId FifthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:approved-to-parallel-split");

    internal static SemanticElementId SixthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:rejected-to-end");

    internal static SemanticElementId SeventhSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:parallel-split-to-prepare-shipment");

    internal static SemanticElementId EighthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:parallel-split-to-notify-customer");

    internal static SemanticElementId NinthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:prepare-shipment-to-parallel-join");

    internal static SemanticElementId TenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:notify-customer-to-parallel-join");

    internal static SemanticElementId EleventhSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:parallel-join-to-inclusive-split");

    internal static SemanticElementId TwelfthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:inclusive-split-to-add-insurance");

    internal static SemanticElementId ThirteenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:inclusive-split-to-add-gift-wrap");

    internal static SemanticElementId FourteenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:add-insurance-to-inclusive-join");

    internal static SemanticElementId FifteenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:add-gift-wrap-to-inclusive-join");

    internal static SemanticElementId SixteenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:inclusive-join-to-end");

    internal static SemanticElementId SeventeenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:task-to-event-based-gateway");

    internal static SemanticElementId EighteenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:event-based-gateway-to-message");

    internal static SemanticElementId NineteenthSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:event-based-gateway-to-timer");

    internal static SemanticElementId TwentiethSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:message-to-approved-task");

    internal static SemanticElementId TwentyFirstSequenceFlowId { get; } =
        new("demo:bpmn:sequence-flow:timer-to-rejected-task");

    internal static VisualStateId StartEventVisualId { get; } =
        new("demo:bpmn:visual:start-event");

    internal static VisualStateId TaskVisualId { get; } =
        new("demo:bpmn:visual:review-order");

    internal static VisualStateId ExclusiveGatewayVisualId { get; } =
        new("demo:bpmn:visual:approved-gateway");

    internal static VisualStateId ApprovedTaskVisualId { get; } =
        new("demo:bpmn:visual:approved-task");

    internal static VisualStateId RejectedTaskVisualId { get; } =
        new("demo:bpmn:visual:rejected-task");

    internal static VisualStateId ParallelSplitGatewayVisualId { get; } =
        new("demo:bpmn:visual:parallel-split");

    internal static VisualStateId PrepareShipmentTaskVisualId { get; } =
        new("demo:bpmn:visual:prepare-shipment");

    internal static VisualStateId NotifyCustomerTaskVisualId { get; } =
        new("demo:bpmn:visual:notify-customer");

    internal static VisualStateId ParallelJoinGatewayVisualId { get; } =
        new("demo:bpmn:visual:parallel-join");

    internal static VisualStateId InclusiveSplitGatewayVisualId { get; } =
        new("demo:bpmn:visual:inclusive-split");

    internal static VisualStateId AddInsuranceTaskVisualId { get; } =
        new("demo:bpmn:visual:add-insurance");

    internal static VisualStateId AddGiftWrapTaskVisualId { get; } =
        new("demo:bpmn:visual:add-gift-wrap");

    internal static VisualStateId InclusiveJoinGatewayVisualId { get; } =
        new("demo:bpmn:visual:inclusive-join");

    internal static VisualStateId EndEventVisualId { get; } =
        new("demo:bpmn:visual:end-event");

    internal static VisualStateId EventBasedGatewayVisualId { get; } =
        new("demo:bpmn:visual:event-based-gateway");

    internal static VisualStateId MessageCatchEventVisualId { get; } =
        new("demo:bpmn:visual:message-catch-event");

    internal static VisualStateId TimerCatchEventVisualId { get; } =
        new("demo:bpmn:visual:timer-catch-event");

    internal static VisualStateId AwaitEventTaskVisualId { get; } =
        new("demo:bpmn:visual:await-event-task");

    internal static VisualStateId ProcessMessageTaskVisualId { get; } =
        new("demo:bpmn:visual:process-message-task");

    internal static VisualStateId HandleTimeoutTaskVisualId { get; } =
        new("demo:bpmn:visual:handle-timeout-task");

    internal static VisualStateId ProcessOrderSubProcessVisualId { get; } =
        new("demo:bpmn:visual:process-order-subprocess");

    internal static VisualStateId ProcessOrderStartEventVisualId { get; } =
        new("demo:bpmn:visual:process-order:start-event");

    internal static VisualStateId ProcessOrderTaskVisualId { get; } =
        new("demo:bpmn:visual:process-order:task");

    internal static VisualStateId ProcessOrderEndEventVisualId { get; } =
        new("demo:bpmn:visual:process-order:end-event");

    internal static VisualStateId ProcessOrderReviewTimeoutEventVisualId { get; } =
        new("demo:bpmn:visual:process-order:review-timeout");

    internal static VisualStateId ProcessOrderHandleTimeoutTaskVisualId { get; } =
        new("demo:bpmn:visual:process-order:handle-timeout");

    internal static VisualStateId ProcessOrderFirstFlowVisualId { get; } =
        new("demo:bpmn:visual:process-order:flow:start-to-task");

    internal static VisualStateId ProcessOrderSecondFlowVisualId { get; } =
        new("demo:bpmn:visual:process-order:flow:task-to-end");

    internal static VisualStateId ProcessOrderTimeoutFlowVisualId { get; } =
        new("demo:bpmn:visual:process-order:flow:timeout-to-handler");

    internal static VisualStateId ProcessOrderTimeoutHandlerFlowVisualId { get; } =
        new("demo:bpmn:visual:process-order:flow:handler-to-end");

    internal static VisualStateId FirstSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:start-to-task");

    internal static VisualStateId SecondSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:task-to-gateway");

    internal static VisualStateId ThirdSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:gateway-to-approved");

    internal static VisualStateId FourthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:gateway-to-rejected");

    internal static VisualStateId FifthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:approved-to-parallel-split");

    internal static VisualStateId SixthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:rejected-to-end");

    internal static VisualStateId SeventhSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:parallel-split-to-prepare-shipment");

    internal static VisualStateId EighthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:parallel-split-to-notify-customer");

    internal static VisualStateId NinthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:prepare-shipment-to-parallel-join");

    internal static VisualStateId TenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:notify-customer-to-parallel-join");

    internal static VisualStateId EleventhSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:parallel-join-to-inclusive-split");

    internal static VisualStateId TwelfthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:inclusive-split-to-add-insurance");

    internal static VisualStateId ThirteenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:inclusive-split-to-add-gift-wrap");

    internal static VisualStateId FourteenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:add-insurance-to-inclusive-join");

    internal static VisualStateId FifteenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:add-gift-wrap-to-inclusive-join");

    internal static VisualStateId SixteenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:inclusive-join-to-end");

    internal static VisualStateId SeventeenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:task-to-event-based-gateway");

    internal static VisualStateId EighteenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:event-based-gateway-to-message");

    internal static VisualStateId NineteenthSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:event-based-gateway-to-timer");

    internal static VisualStateId TwentiethSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:message-to-approved-task");

    internal static VisualStateId TwentyFirstSequenceFlowVisualId { get; } =
        new("demo:bpmn:visual:sequence-flow:timer-to-rejected-task");

    internal static ConnectorAnchorId StartEventSourceAnchorId { get; } =
        new("demo:bpmn:anchor:start-event:right:source");

    internal static ConnectorAnchorId TaskTargetAnchorId { get; } =
        new("demo:bpmn:anchor:review-order:left:target");

    internal static ConnectorAnchorId TaskSourceAnchorId { get; } =
        new("demo:bpmn:anchor:review-order:right:source");

    internal static ConnectorAnchorId ExclusiveGatewayTargetAnchorId { get; } =
        new("demo:bpmn:anchor:approved-gateway:left:target");

    internal static ConnectorAnchorId ExclusiveGatewayApprovedSourceAnchorId { get; } =
        new("demo:bpmn:anchor:approved-gateway:right:approved-source");

    internal static ConnectorAnchorId ExclusiveGatewayRejectedSourceAnchorId { get; } =
        new("demo:bpmn:anchor:approved-gateway:right:rejected-source");

    internal static ConnectorAnchorId ApprovedTaskTargetAnchorId { get; } =
        new("demo:bpmn:anchor:approved-task:left:target");

    internal static ConnectorAnchorId ApprovedTaskSourceAnchorId { get; } =
        new("demo:bpmn:anchor:approved-task:right:source");

    internal static ConnectorAnchorId ApprovedTaskReconnectTargetAnchorId { get; } =
        new("demo:bpmn:anchor:approved-task:top:reconnect-target");

    internal static ConnectorAnchorId RejectedTaskTargetAnchorId { get; } =
        new("demo:bpmn:anchor:rejected-task:left:target");

    internal static ConnectorAnchorId RejectedTaskSourceAnchorId { get; } =
        new("demo:bpmn:anchor:rejected-task:right:source");

    internal static ConnectorAnchorId RejectedTaskReconnectSourceAnchorId { get; } =
        new("demo:bpmn:anchor:rejected-task:bottom:reconnect-source");

    internal static ConnectorAnchorId ParallelSplitTargetAnchorId { get; } =
        new("demo:bpmn:anchor:parallel-split:left:target");

    internal static ConnectorAnchorId ParallelSplitPrepareSourceAnchorId { get; } =
        new("demo:bpmn:anchor:parallel-split:right:prepare-source");

    internal static ConnectorAnchorId ParallelSplitNotifySourceAnchorId { get; } =
        new("demo:bpmn:anchor:parallel-split:right:notify-source");

    internal static ConnectorAnchorId PrepareShipmentTargetAnchorId { get; } =
        new("demo:bpmn:anchor:prepare-shipment:left:target");

    internal static ConnectorAnchorId PrepareShipmentSourceAnchorId { get; } =
        new("demo:bpmn:anchor:prepare-shipment:right:source");

    internal static ConnectorAnchorId NotifyCustomerTargetAnchorId { get; } =
        new("demo:bpmn:anchor:notify-customer:left:target");

    internal static ConnectorAnchorId NotifyCustomerSourceAnchorId { get; } =
        new("demo:bpmn:anchor:notify-customer:right:source");

    internal static ConnectorAnchorId ParallelJoinPrepareTargetAnchorId { get; } =
        new("demo:bpmn:anchor:parallel-join:left:prepare-target");

    internal static ConnectorAnchorId ParallelJoinNotifyTargetAnchorId { get; } =
        new("demo:bpmn:anchor:parallel-join:left:notify-target");

    internal static ConnectorAnchorId ParallelJoinSourceAnchorId { get; } =
        new("demo:bpmn:anchor:parallel-join:right:source");

    internal static ConnectorAnchorId InclusiveSplitTargetAnchorId { get; } =
        new("demo:bpmn:anchor:inclusive-split:left:target");

    internal static ConnectorAnchorId InclusiveSplitInsuranceSourceAnchorId { get; } =
        new("demo:bpmn:anchor:inclusive-split:right:insurance-source");

    internal static ConnectorAnchorId InclusiveSplitGiftWrapSourceAnchorId { get; } =
        new("demo:bpmn:anchor:inclusive-split:right:gift-wrap-source");

    internal static ConnectorAnchorId AddInsuranceTargetAnchorId { get; } =
        new("demo:bpmn:anchor:add-insurance:left:target");

    internal static ConnectorAnchorId AddInsuranceSourceAnchorId { get; } =
        new("demo:bpmn:anchor:add-insurance:right:source");

    internal static ConnectorAnchorId AddGiftWrapTargetAnchorId { get; } =
        new("demo:bpmn:anchor:add-gift-wrap:left:target");

    internal static ConnectorAnchorId AddGiftWrapSourceAnchorId { get; } =
        new("demo:bpmn:anchor:add-gift-wrap:right:source");

    internal static ConnectorAnchorId InclusiveJoinInsuranceTargetAnchorId { get; } =
        new("demo:bpmn:anchor:inclusive-join:left:insurance-target");

    internal static ConnectorAnchorId InclusiveJoinGiftWrapTargetAnchorId { get; } =
        new("demo:bpmn:anchor:inclusive-join:left:gift-wrap-target");

    internal static ConnectorAnchorId InclusiveJoinSourceAnchorId { get; } =
        new("demo:bpmn:anchor:inclusive-join:right:source");

    internal static ConnectorAnchorId EndEventInclusiveTargetAnchorId { get; } =
        new("demo:bpmn:anchor:end-event:left:inclusive-target");

    internal static ConnectorAnchorId EndEventRejectedTargetAnchorId { get; } =
        new("demo:bpmn:anchor:end-event:left:rejected-target");

    internal static ConnectorAnchorId AwaitEventTaskSourceAnchorId { get; } =
        new("demo:bpmn:anchor:await-event-task:right:source");

    internal static ConnectorAnchorId EventBasedGatewayTargetAnchorId { get; } =
        new("demo:bpmn:anchor:event-based-gateway:left:target");

    internal static ConnectorAnchorId EventBasedGatewayMessageSourceAnchorId { get; } =
        new("demo:bpmn:anchor:event-based-gateway:right:message-source");

    internal static ConnectorAnchorId EventBasedGatewayTimerSourceAnchorId { get; } =
        new("demo:bpmn:anchor:event-based-gateway:right:timer-source");

    internal static ConnectorAnchorId MessageCatchEventTargetAnchorId { get; } =
        new("demo:bpmn:anchor:message-catch-event:left:target");

    internal static ConnectorAnchorId MessageCatchEventSourceAnchorId { get; } =
        new("demo:bpmn:anchor:message-catch-event:right:source");

    internal static ConnectorAnchorId TimerCatchEventTargetAnchorId { get; } =
        new("demo:bpmn:anchor:timer-catch-event:left:target");

    internal static ConnectorAnchorId TimerCatchEventSourceAnchorId { get; } =
        new("demo:bpmn:anchor:timer-catch-event:right:source");

    internal static ConnectorAnchorId ProcessMessageTaskTargetAnchorId { get; } =
        new("demo:bpmn:anchor:process-message-task:left:target");

    internal static ConnectorAnchorId HandleTimeoutTaskTargetAnchorId { get; } =
        new("demo:bpmn:anchor:handle-timeout-task:left:target");

    internal static ConnectorAnchorId ProcessOrderStartSourceAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:start:right:source");

    internal static ConnectorAnchorId ProcessOrderTaskTargetAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:task:left:target");

    internal static ConnectorAnchorId ProcessOrderTaskSourceAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:task:right:source");

    internal static ConnectorAnchorId ProcessOrderEndTargetAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:end:left:target");

    internal static ConnectorAnchorId ProcessOrderReviewTimeoutSourceAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:review-timeout:bottom:source");

    internal static ConnectorAnchorId ProcessOrderHandleTimeoutTargetAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:handle-timeout:top:target");

    internal static ConnectorAnchorId ProcessOrderHandleTimeoutSourceAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:handle-timeout:right:source");

    internal static ConnectorAnchorId ProcessOrderTimeoutEndTargetAnchorId { get; } =
        new("demo:bpmn:anchor:process-order:end:bottom:target");

    internal static ValueTask<Document> CreateDocumentAsync(
        CancellationToken cancellationToken = default) =>
        BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken);
}
