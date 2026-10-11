using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.ConnectionCreation;

internal static class BpmnElementConnectionCreation
{
    internal static ImmutableArray<Diagnostic> Source(ElementConnectionSourceRequest request, bool message)
    {
        if (!Member(request.Document, request.Context, request.PresentationPlan, request.SemanticId, request.VisualId) ||
            !request.Document.SemanticModel.TryGetElement(request.SemanticId, out var element) || element is null ||
            (message ? !BpmnEffectiveParticipant.TryResolve(request.Document, element.Id, out _, out _) ||
                !BpmnMessageFlowSemantics.CanBeSource(element.TypeId) || !BpmnMessageFlowSemantics.ValidateDirectPool(element).IsEmpty :
                !BpmnSemanticTypes.IsFlowNode(element.TypeId) || element.TypeId == BpmnSemanticTypes.EndEvent))
            return [new("BPMN_ELEMENT_FLOW_SOURCE_INVALID", DiagnosticSeverity.Error, "Select an eligible source element for the selected Flow tool.")];
        return [];
    }

    internal static ImmutableArray<Diagnostic> Endpoints(ElementConnectionCreationRequest request, bool message)
    {
        var diagnostics = Source(new(request.Document, request.Context, request.PresentationPlan, request.Source.SemanticId, request.Source.VisualId), message);
        if (!diagnostics.IsEmpty) return diagnostics;
        if (!Member(request.Document, request.Context, request.PresentationPlan, request.Target.SemanticId, request.Target.VisualId))
            return [new("BPMN_ELEMENT_FLOW_TARGET_INVALID", DiagnosticSeverity.Error, "The target is not a current member of this presentation.")];
        return message ? BpmnMessageFlowSemantics.ValidateEndpoints(request.Document, request.Source.SemanticId, request.Target.SemanticId)
            : BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(request.Document, request.Target.SemanticId,
                request.Source.SemanticId, request.Target.SemanticId);
    }

    private static bool Member(DocumentSnapshot document, PresentationViewContext context, PresentationViewPlan? plan,
        SemanticElementId id, VisualStateId? visualId) =>
        (visualId is not null ? document.VisualModel.TryGetVisualState(visualId, out var visual) && visual?.SemanticElementId == id :
            document.SemanticModel.TryGetElement(id, out var element) && element is not null &&
            (OrganizationalSemantics.IsPool(element) || element.TypeId == BpmnSemanticTypes.Participant)) &&
        (context is ProcessScopePresentationContext process
            ? document.SemanticModel.TryGetScope(id, out var scope) && scope?.Id == process.ScopeId ||
                plan is not null && plan.Context == context && plan.SourceRevision == document.Revision && plan.Contains(id)
            : plan is not null && plan.DocumentId == document.DocumentId && plan.SourceRevision == document.Revision &&
                plan.Context == context && plan.Contains(id));

    internal static async ValueTask<ImmutableArray<ElementConnectionCandidate>> Candidates(ElementConnectionCreationRequest request,
        bool message, CancellationToken cancellationToken)
    {
        if (!Endpoints(request, message).IsEmpty) return [];
        request = request with
        {
            Source = request.Source with { VisualId = request.Source.VisualId ?? request.IdentityProvider.CreateIdentity().VisualStateId },
            Target = request.Target with { VisualId = request.Target.VisualId ?? request.IdentityProvider.CreateIdentity().VisualStateId },
        };
        return await Candidates(request.Document, request.Source.SemanticId, request.Source.VisualId!,
            request.Target.SemanticId, request.Target.VisualId!, request.IdentityProvider, message, cancellationToken).ConfigureAwait(false);
    }

    // Authoring planners can use actual semantic/visual identities without manufacturing Scene targets.
    internal static async ValueTask<ImmutableArray<ElementConnectionCandidate>> Candidates(DocumentSnapshot document,
        SemanticElementId sourceId, VisualStateId sourceVisualId, SemanticElementId targetId, VisualStateId targetVisualId,
        IDocumentCreationIdentityProvider identityProvider, bool message, CancellationToken cancellationToken)
    {
        var source = Anchors(document, sourceId, sourceVisualId, identityProvider, ConnectorAnchorRole.Source);
        var target = Anchors(document, targetId, targetVisualId, identityProvider, ConnectorAnchorRole.Target);
        var identity = identityProvider.CreateIdentity();
        var result = ImmutableArray.CreateBuilder<ElementConnectionCandidate>();
        foreach (var first in source)
            foreach (var second in target)
            {
                var command = new CreateBpmnElementFlowCommand(document.DocumentId, document.Revision,
                    message ? BpmnSemanticTypes.MessageFlow : BpmnSemanticTypes.SequenceFlow, identity.SemanticElementId,
                    identity.VisualStateId, sourceId, targetId, sourceVisualId,
                    targetVisualId, first.Id, second.Id, first.Added, second.Added);
                var proposal = await new BpmnElementFlowCommandHandler().HandleAsync(command, document, cancellationToken).ConfigureAwait(false);
                if (proposal.Succeeded) result.Add(new(new(command, identity.SemanticElementId, identity.VisualStateId), proposal));
            }
        return result.ToImmutable();
    }

    private static ImmutableArray<(ConnectorAnchorId Id, ConnectorAnchor? Added)> Anchors(DocumentSnapshot document,
        SemanticElementId semanticId, VisualStateId visualId, IDocumentCreationIdentityProvider identityProvider, ConnectorAnchorRole role)
    {
        document.VisualModel.TryGetVisualState(visualId, out var visual);
        visual ??= new VisualStateSnapshot(visualId, semanticId, default, default, VisualPlacementMode.Derived);
        document.SemanticModel.TryGetElement(semanticId, out var element);
        var policy = BpmnConnectorAnchorPolicies.Provider.Resolve(element!.TypeId);
        var free = ElementConnectorAnchorResolver.Resolve(visual!, policy).Where(anchor => anchor.Allows(role) &&
            !ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchor.Id)).OrderBy(anchor => anchor.Id.Value, StringComparer.Ordinal).ToArray();
        if (free.Length > 0) return [.. free.Select(anchor => (anchor.Id, (ConnectorAnchor?)null))];
        var id = identityProvider.CreateConnectorAnchorId();
        var result = ImmutableArray.CreateBuilder<(ConnectorAnchorId, ConnectorAnchor?)>();
        foreach (var side in CandidateSides(visual, element.TypeId, role))
        {
            var anchors = visual!.ConnectorAnchors.Where(anchor => anchor.Side == side).ToArray();
            var positions = anchors.Select(anchor => anchor.NormalizedPosition ??
                ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order, anchors.Length)).Append(0d).Append(1d).Order().ToArray();
            // Every available interval is a boundary candidate; existing authored points never move.
            for (var index = 1; index < positions.Length; index++)
            {
                var position = (positions[index - 1] + positions[index]) / 2;
                if (position > positions[index - 1] && position < positions[index])
                    result.Add((id, new(id, side, role, anchors.Length, position)));
            }
        }
        return result.ToImmutable();
    }

    // Both new connections and retained-anchor arrangement use the registered side/role authority.
    internal static IEnumerable<ConnectorAnchorSide> CandidateSides(VisualStateSnapshot visual,
        SemanticTypeId type, ConnectorAnchorRole role) => Enum.GetValues<ConnectorAnchorSide>()
        .Where(side => ElementConnectorAnchorPolicyEvaluator.CanAdd(BpmnConnectorAnchorPolicies.Provider.Resolve(type), visual, side, role));
}

internal sealed partial class BpmnSequenceFlowConnectionCreationCommandFactory : IElementConnectionCreationCommandFactory
{
    public ImmutableArray<Diagnostic> ValidateSource(ElementConnectionSourceRequest request) => BpmnElementConnectionCreation.Source(request, false);
    public ImmutableArray<Diagnostic> ValidateEndpoints(ElementConnectionCreationRequest request) => BpmnElementConnectionCreation.Endpoints(request, false);
    public ValueTask<ImmutableArray<ElementConnectionCandidate>> CreateCandidatesAsync(ElementConnectionCreationRequest request, CancellationToken cancellationToken) =>
        BpmnElementConnectionCreation.Candidates(request, false, cancellationToken);
}

internal sealed partial class BpmnMessageFlowConnectionCreationCommandFactory : IElementConnectionCreationCommandFactory
{
    public ImmutableArray<Diagnostic> ValidateSource(ElementConnectionSourceRequest request) => BpmnElementConnectionCreation.Source(request, true);
    public ImmutableArray<Diagnostic> ValidateEndpoints(ElementConnectionCreationRequest request) => BpmnElementConnectionCreation.Endpoints(request, true);
    public ValueTask<ImmutableArray<ElementConnectionCandidate>> CreateCandidatesAsync(ElementConnectionCreationRequest request, CancellationToken cancellationToken) =>
        BpmnElementConnectionCreation.Candidates(request, true, cancellationToken);
}
