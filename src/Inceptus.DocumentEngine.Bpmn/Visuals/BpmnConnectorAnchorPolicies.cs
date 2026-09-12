using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Visuals;

/// <summary>
/// Supplies the immutable connector-anchor policies for the currently supported BPMN flow nodes.
/// Policy evaluation and anchor persistence remain generic framework responsibilities.
/// </summary>
internal static class BpmnConnectorAnchorPolicies
{
    private static readonly ElementConnectorAnchorPolicy SourceOnly = AllEdges(
        ConnectorAnchorRoleCapability.Source);

    private static readonly ElementConnectorAnchorPolicy TargetOnly = AllEdges(
        ConnectorAnchorRoleCapability.Target);

    private static readonly ElementConnectorAnchorPolicy SourceOrTarget = AllEdges(
        ConnectorAnchorRoleCapability.SourceOrTarget);

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        Registrations
    { get; } =
    [
        new(BpmnSemanticTypes.StartEvent, SourceOnly),
        new(BpmnSemanticTypes.EndEvent, TargetOnly),
        new(BpmnSemanticTypes.Task, SourceOrTarget),
        new(BpmnSemanticTypes.ExclusiveGateway, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        M33Registrations
    { get; } =
    [
        .. Registrations,
        new(BpmnSemanticTypes.ParallelGateway, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        M34Registrations
    { get; } =
    [
        .. M33Registrations,
        new(BpmnSemanticTypes.InclusiveGateway, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        N4Registrations
    { get; } =
    [
        .. M34Registrations,
        new(BpmnSemanticTypes.MessageCatchEvent, SourceOrTarget),
        new(BpmnSemanticTypes.TimerCatchEvent, SourceOrTarget),
        new(BpmnSemanticTypes.EventBasedGateway, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        N6Registrations
    { get; } =
    [
        .. N4Registrations,
        new(BpmnSemanticTypes.UserTask, SourceOrTarget),
        new(BpmnSemanticTypes.ManualTask, SourceOrTarget),
        new(BpmnSemanticTypes.ServiceTask, SourceOrTarget),
        new(BpmnSemanticTypes.SendTask, SourceOrTarget),
        new(BpmnSemanticTypes.ReceiveTask, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        N7Registrations
    { get; } =
    [
        .. N6Registrations,
        new(BpmnSemanticTypes.MessageThrowEvent, SourceOrTarget),
        new(BpmnSemanticTypes.SignalCatchEvent, SourceOrTarget),
        new(BpmnSemanticTypes.SignalThrowEvent, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        N81Registrations
    { get; } =
    [
        .. N7Registrations,
        new(BpmnSemanticTypes.SubProcess, SourceOrTarget),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        N90Registrations
    { get; } =
    [
        .. N81Registrations,
        new(BpmnSemanticTypes.TimerBoundaryEvent, SourceOnly),
    ];

    internal static ImmutableArray<ElementConnectorAnchorPolicyRegistration>
        N91Registrations
    { get; } =
    [
        .. N90Registrations,
        new(BpmnSemanticTypes.MessageBoundaryEvent, SourceOnly),
        new(BpmnSemanticTypes.SignalBoundaryEvent, SourceOnly),
    ];

    internal static IElementConnectorAnchorPolicyProvider Provider { get; } =
        new ElementConnectorAnchorPolicyRegistry(N91Registrations);

    private static ElementConnectorAnchorPolicy AllEdges(
        ConnectorAnchorRoleCapability roleCapability) =>
        new(
            EdgeConnectorAnchorPolicy.DynamicUnlimited(roleCapability),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(roleCapability),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(roleCapability),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(roleCapability));
}
