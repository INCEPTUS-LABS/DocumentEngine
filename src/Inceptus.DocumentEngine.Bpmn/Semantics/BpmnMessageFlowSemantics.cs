using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>The single BPMN authority for Message Flow endpoint direction and ownership.</summary>
public static class BpmnMessageFlowSemantics
{
    public const string EndpointMissing = "BPMN_MESSAGE_FLOW_ENDPOINT_MISSING";
    public const string SourceInvalid = "BPMN_MESSAGE_FLOW_SOURCE_INVALID";
    public const string TargetInvalid = "BPMN_MESSAGE_FLOW_TARGET_INVALID";
    public const string SameParticipant = "BPMN_MESSAGE_FLOW_SAME_PARTICIPANT";
    public const string DifferentCollaborations = "BPMN_MESSAGE_FLOW_DIFFERENT_COLLABORATIONS";
    public const string BoundaryAttachmentInvalid = "BPMN_MESSAGE_FLOW_BOUNDARY_ATTACHMENT_INVALID";
    public const string PresentationUnavailable = "BPMN_MESSAGE_FLOW_PRESENTATION_UNAVAILABLE";
    public const string AnchorInvalid = "BPMN_MESSAGE_FLOW_ANCHOR_INVALID";
    public const string StateMismatch = "BPMN_MESSAGE_FLOW_STATE_MISMATCH";
    public const string InvalidCommand = "BPMN_MESSAGE_FLOW_COMMAND_INVALID";
    public const string TypeChangeInvalid = "BPMN_MESSAGE_FLOW_TYPE_CHANGE_INVALID";

    public static bool CanBeSource(SemanticTypeId type) => type == BpmnSemanticTypes.Participant || type == OrganizationalSemanticTypes.Pool ||
        type == BpmnSemanticTypes.MessageThrowEvent ||
        BpmnActivitySemanticTypes.IsActivity(type) && type != BpmnSemanticTypes.ReceiveTask;

    public static bool CanBeTarget(SemanticTypeId type) => type == BpmnSemanticTypes.Participant || type == OrganizationalSemanticTypes.Pool ||
        type == BpmnSemanticTypes.MessageCatchEvent || type == BpmnSemanticTypes.MessageBoundaryEvent ||
        BpmnActivitySemanticTypes.IsActivity(type) && type != BpmnSemanticTypes.SendTask;

    public static ImmutableArray<Diagnostic> ValidateEndpoints(DocumentSnapshot document,
        SemanticElementId sourceId, SemanticElementId targetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        if (!document.SemanticModel.TryGetElement(sourceId, out var source) || source is null ||
            !document.SemanticModel.TryGetElement(targetId, out var target) || target is null)
            return [Error(EndpointMissing, "Message Flow endpoints must be existing semantic elements, not relationships.", sourceId)];

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (!CanBeSource(source.TypeId))
            diagnostics.Add(Error(SourceInvalid, "This element type cannot send a Message Flow.", source.Id));
        if (!CanBeTarget(target.TypeId))
            diagnostics.Add(Error(TargetInvalid, "This element type cannot receive a Message Flow.", target.Id));
        if (target.TypeId == BpmnSemanticTypes.MessageBoundaryEvent && !HasValidBoundaryAttachment(document, target))
            diagnostics.Add(Error(BoundaryAttachmentInvalid, "A Message Boundary Event must remain attached to an Activity in the same Process scope.", target.Id));

        diagnostics.AddRange(ValidateDirectPool(source));
        diagnostics.AddRange(ValidateDirectPool(target));
        var sourceValid = BpmnEffectiveParticipant.TryResolve(document, sourceId, out var sender, out var sourceFailure);
        var targetValid = BpmnEffectiveParticipant.TryResolve(document, targetId, out var receiver, out var targetFailure);
        if (!sourceValid) diagnostics.Add(sourceFailure!);
        if (!targetValid) diagnostics.Add(targetFailure!);
        if (sourceValid && targetValid)
        {
            if (sender == receiver)
                diagnostics.Add(Error(SameParticipant, "A Message Flow must connect two different Participants.", sourceId));
        }
        return diagnostics.ToImmutable();
    }

    internal static ImmutableArray<Diagnostic> ValidatePresentation(DocumentSnapshot document,
        SemanticElementId sourceId, SemanticElementId targetId)
    {
        var diagnostics = ValidateEndpointContext(document, sourceId, targetId);
        if (!diagnostics.IsEmpty) return diagnostics;
        if (document.VisualModel.VisualStates.Count(visual => visual.SemanticElementId == sourceId) != 1 ||
            document.VisualModel.VisualStates.Count(visual => visual.SemanticElementId == targetId) != 1)
            return [Error(PresentationUnavailable, "Both Message Flow endpoints require unique visual anchor ownership in the current diagram.", sourceId)];
        return [];
    }

    internal static ImmutableArray<Diagnostic> ValidateEndpointContext(DocumentSnapshot document,
        SemanticElementId sourceId, SemanticElementId targetId)
    {
        var diagnostics = ValidateEndpoints(document, sourceId, targetId);
        if (!diagnostics.IsEmpty) return diagnostics;
        var sourceScopes = EndpointScopes(sourceId);
        var targetScopes = EndpointScopes(targetId);
        if (sourceScopes.Intersect(targetScopes).Count() != 1)
            return [Error(PresentationUnavailable, "Message Flow endpoints must belong to one unambiguous normal Pool diagram.", sourceId)];
        return [];

        IEnumerable<DocumentScopeId> EndpointScopes(SemanticElementId id)
        {
            if (document.SemanticModel.TryGetScope(id, out var scope)) return [scope!.Id];
            return document.SemanticModel.Elements.Where(pool => OrganizationalSemantics.IsPool(pool) &&
                    OrganizationalSemantics.GetParticipantId(pool) == id)
                .Select(pool => document.SemanticModel.GetScope(pool.Id).Id).Distinct();
        }
    }

    internal static ImmutableArray<Diagnostic> ValidateExisting(DocumentSnapshot document) =>
        [.. document.SemanticModel.Relationships.Where(flow => flow.TypeId == BpmnSemanticTypes.MessageFlow)
            .SelectMany(flow => ValidatePresentation(document, flow.SourceId, flow.TargetId))];

    internal static ImmutableArray<Diagnostic> ValidateDirectPool(SemanticElementSnapshot element) =>
        OrganizationalSemantics.IsPool(element) && OrganizationalSemantics.GetParticipantId(element) is not null
            ? [Error(BpmnEffectiveParticipant.LaneEndpointInvalid,
                "Select the shared Participant label or an eligible element; a Lane-mode Pool is not a direct endpoint.", element.Id)] : [];

    private static bool HasValidBoundaryAttachment(DocumentSnapshot document, SemanticElementSnapshot target) =>
        target.AttachedToElementId is { } attachedId &&
        document.SemanticModel.TryGetElement(attachedId, out var activity) && activity is not null &&
        BpmnActivitySemanticTypes.IsActivity(activity.TypeId) &&
        document.SemanticModel.TryGetScope(target.Id, out var eventScope) &&
        document.SemanticModel.TryGetScope(activity.Id, out var activityScope) && eventScope?.Id == activityScope?.Id &&
        document.VisualModel.VisualStates.Count(visual => visual.SemanticElementId == target.Id && visual.BoundaryAttachment is not null) == 1 &&
        document.VisualModel.VisualStates.Count(visual => visual.SemanticElementId == target.Id) == 1;

    internal static Diagnostic Error(string code, string message, SemanticElementId id) =>
        new(code, DiagnosticSeverity.Error, message, id.Value);
}
