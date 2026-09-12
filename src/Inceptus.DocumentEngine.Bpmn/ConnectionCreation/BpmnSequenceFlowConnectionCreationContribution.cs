using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.ConnectionCreation;

internal static class BpmnSequenceFlowConnectionCreationContribution
{
    internal static AnchorConnectionCreationId SequenceFlowCreationId { get; } =
        new("bpmn:connection-creation/sequence-flow");

    internal static ImmutableArray<AnchorConnectionCreationRegistration>
        Registrations
    { get; } =
    [
        new(
            SequenceFlowCreationId,
            new BpmnSequenceFlowConnectionCreationCommandFactory()),
    ];

    internal static ImmutableArray<AnchorConnectionCreationRegistration>
        N31Registrations
    { get; } = CreateN31Registrations();

    private static ImmutableArray<AnchorConnectionCreationRegistration>
        CreateN31Registrations()
    {
        var factory = new BpmnSequenceFlowConnectionCreationCommandFactory();
        return [new(SequenceFlowCreationId, factory, factory)];
    }
}

internal sealed class BpmnSequenceFlowConnectionCreationCommandFactory :
    IAnchorConnectionCreationCommandFactory,
    IAnchorConnectionTargetEligibility
{
    public bool CanStart(AnchorConnectionCreationSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ConnectorAnchorOccupancy.IsOccupied(
                request.Document.VisualModel,
                request.SourceAnchorId) ||
            !request.Document.VisualModel.TryGetVisualState(
                request.SourceVisualStateId,
                out var sourceVisual) ||
            sourceVisual is null ||
            sourceVisual.SemanticElementId != request.SourceSemanticElementId ||
            !request.Document.SemanticModel.TryGetElement(
                request.SourceSemanticElementId,
                out var sourceElement) ||
            sourceElement is null ||
            !BpmnSemanticTypes.IsFlowNode(sourceElement.TypeId))
        {
            return false;
        }

        try
        {
            return ElementConnectorAnchorResolver.Resolve(
                    sourceVisual,
                    sourceElement.TypeId,
                    BpmnConnectorAnchorPolicies.Provider)
                .Count(anchor =>
                    anchor.Id == request.SourceAnchorId &&
                    anchor.Allows(ConnectorAnchorRole.Source)) == 1;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool CanAcquire(AnchorConnectionTargetEligibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CanStart(new AnchorConnectionCreationSourceRequest(
                request.Document,
                request.ExpectedRevision,
                request.SourceSemanticElementId,
                request.SourceVisualStateId,
                request.SourceAnchorId)) &&
            BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(
                request.Document,
                request.TargetSemanticElementId,
                request.SourceSemanticElementId,
                request.TargetSemanticElementId).IsEmpty &&
            request.Document.VisualModel.TryGetVisualState(
                request.TargetVisualStateId,
                out var targetVisual) &&
            targetVisual is not null &&
            targetVisual.SemanticElementId == request.TargetSemanticElementId;
    }

    public AnchorConnectionCreationPlanResult CreatePlan(
        AnchorConnectionCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identity = request.IdentityProvider.CreateIdentity();
        ArgumentNullException.ThrowIfNull(identity);
        ICommand command;
        ImmutableArray<Diagnostic> diagnostics;
        if (request.TargetAcquisition is
            {
                Kind: TargetAnchorAcquisitionKind.Proposed,
                Side: { } side,
                InsertionIndex: { } insertionIndex,
            })
        {
            var atomic = new CreateBpmnSequenceFlowWithTargetAnchorCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                identity.SemanticElementId,
                identity.VisualStateId,
                request.SourceSemanticElementId,
                request.TargetSemanticElementId,
                request.SourceAnchorId,
                request.TargetVisualStateId,
                request.TargetAnchorId,
                side,
                insertionIndex);
            command = atomic;
            diagnostics = BpmnSequenceFlowWithTargetAnchorCreationValidation.Validate(
                atomic,
                request.Document);
        }
        else
        {
            var existing = new CreateBpmnSequenceFlowCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                identity.SemanticElementId,
                identity.VisualStateId,
                request.SourceSemanticElementId,
                request.TargetSemanticElementId,
                request.SourceAnchorId,
                request.TargetAnchorId);
            command = existing;
            diagnostics = BpmnSequenceFlowCreationValidation.Validate(
                existing,
                request.Document);
        }

        return diagnostics.IsEmpty
            ? AnchorConnectionCreationPlanResult.Success(
                new AnchorConnectionCreationPlan(
                    command,
                    identity.SemanticElementId,
                    identity.VisualStateId))
            : AnchorConnectionCreationPlanResult.Failure(diagnostics);
    }
}
