using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.ConnectionCreation;

internal static class BpmnMessageFlowConnectionCreationContribution
{
    internal static AnchorConnectionCreationId MessageFlowCreationId { get; } = new("bpmn:connection-creation/message-flow");
    internal static ImmutableArray<AnchorConnectionCreationRegistration> Registrations { get; } = Create();

    private static ImmutableArray<AnchorConnectionCreationRegistration> Create()
    {
        var factory = new BpmnMessageFlowConnectionCreationCommandFactory();
        return [new(MessageFlowCreationId, factory, factory, false, BpmnMessageFlowVisuals.Descriptor)];
    }
}

internal sealed partial class BpmnMessageFlowConnectionCreationCommandFactory : IAnchorConnectionCreationCommandFactory, IAnchorConnectionTargetEligibility
{
    public bool CanStart(AnchorConnectionCreationSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PresentationPlan is not { } plan || !plan.Contains(request.SourceSemanticElementId) ||
            !BpmnEffectiveParticipant.TryResolve(request.Document, request.SourceSemanticElementId, out _, out _) ||
            ConnectorAnchorOccupancy.IsOccupied(request.Document.VisualModel, request.SourceAnchorId) ||
            !request.Document.VisualModel.TryGetVisualState(request.SourceVisualStateId, out var visual) || visual is null ||
            visual.SemanticElementId != request.SourceSemanticElementId ||
            !request.Document.SemanticModel.TryGetElement(request.SourceSemanticElementId, out var element) || element is null ||
            !BpmnMessageFlowSemantics.CanBeSource(element.TypeId) || !BpmnMessageFlowSemantics.ValidateDirectPool(element).IsEmpty) return false;
        try
        {
            return ElementConnectorAnchorResolver.Resolve(visual, element.TypeId, BpmnConnectorAnchorPolicies.Provider)
                .Count(anchor => anchor.Id == request.SourceAnchorId && anchor.Allows(ConnectorAnchorRole.Source)) == 1;
        }
        catch (InvalidOperationException) { return false; }
    }

    public bool CanAcquire(AnchorConnectionTargetEligibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CanStart(new AnchorConnectionCreationSourceRequest(request.PresentationContext, request.PresentationPlan,
                request.Document, request.ExpectedRevision, request.SourceSemanticElementId, request.SourceVisualStateId, request.SourceAnchorId)) &&
            request.PresentationPlan!.Contains(request.TargetSemanticElementId) &&
            BpmnMessageFlowSemantics.ValidateEndpoints(request.Document, request.SourceSemanticElementId, request.TargetSemanticElementId).IsEmpty &&
            request.Document.VisualModel.TryGetVisualState(request.TargetVisualStateId, out var visual) && visual is not null &&
            visual.SemanticElementId == request.TargetSemanticElementId;
    }

    public AnchorConnectionCreationPlanResult CreatePlan(AnchorConnectionCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanAcquire(new AnchorConnectionTargetEligibilityRequest(request.PresentationContext, request.PresentationPlan,
                request.Document, request.ExpectedRevision, request.SourceSemanticElementId, request.SourceVisualStateId,
                request.SourceAnchorId, request.TargetSemanticElementId, request.TargetVisualStateId)))
            return AnchorConnectionCreationPlanResult.Failure([new Diagnostic(BpmnMessageFlowSemantics.PresentationUnavailable,
                DiagnosticSeverity.Error, "The Message Flow endpoints are not eligible in the selected Collaboration view.")]);
        var identity = request.IdentityProvider.CreateIdentity();
        ArgumentNullException.ThrowIfNull(identity);
        ICommand command;
        ImmutableArray<Diagnostic> diagnostics;
        if (request.TargetAcquisition is { Kind: TargetAnchorAcquisitionKind.Proposed, Side: { } side, InsertionIndex: { } index })
        {
            var create = new CreateBpmnMessageFlowWithTargetAnchorCommand(request.Document.DocumentId, request.ExpectedRevision,
                identity.SemanticElementId, identity.VisualStateId, request.SourceSemanticElementId, request.TargetSemanticElementId,
                request.SourceAnchorId, request.TargetVisualStateId, request.TargetAnchorId, side, index);
            command = create;
            diagnostics = new BpmnMessageFlowWithTargetAnchorCreationCommandHandler().Validate(create, request.Document);
        }
        else
        {
            var create = new CreateBpmnMessageFlowCommand(request.Document.DocumentId, request.ExpectedRevision,
                identity.SemanticElementId, identity.VisualStateId, request.SourceSemanticElementId, request.TargetSemanticElementId,
                request.SourceAnchorId, request.TargetAnchorId);
            command = create;
            diagnostics = new BpmnMessageFlowCreationCommandHandler().Validate(create, request.Document);
        }
        return diagnostics.IsEmpty ? AnchorConnectionCreationPlanResult.Success(new AnchorConnectionCreationPlan(command,
            identity.SemanticElementId, identity.VisualStateId)) : AnchorConnectionCreationPlanResult.Failure(diagnostics);
    }
}
