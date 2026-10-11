using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Presentation;

/// <summary>Includes referenced Participants in the ordinary Pool diagram without a duplicate Collaboration view.</summary>
public sealed class BpmnCollaborationViewProvider : IPresentationViewProvider, IProcessPresentationViewProvider
{
    public const string InvalidView = "BPMN_COLLABORATION_VIEW_INVALID";
    public const string ParticipantVisualMissing = "BPMN_PARTICIPANT_VISUAL_MISSING";
    public ImmutableArray<DocumentPresentationContext> GetContexts(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [];
    }
    public PresentationViewResolution Resolve(DocumentSnapshot document, DocumentPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        return PresentationViewResolution.Failure([new Diagnostic(InvalidView, DiagnosticSeverity.Error,
            "Participants are presented through their assigned Pools in the normal diagram.", context.OwnerId.Value)]);
    }
    public PresentationViewPlan IncludeDocumentElements(DocumentSnapshot document, ProcessScopePresentationContext context, PresentationViewPlan scopePlan)
    {
        if (!document.SemanticModel.ModelProfiles.IsAvailable(Inceptus.DocumentEngine.Organizational.Profiles.OrganizationalModelProfile.Id))
            return scopePlan;
        var ids = scopePlan.ElementIds.ToHashSet();
        foreach (var pool in OrganizationalSemantics.GetPoolsInScope(document, context.ScopeId))
            if (OrganizationalSemantics.GetParticipantId(pool) is { } id &&
                document.SemanticModel.TryGetElement(id, out var participant) && participant?.TypeId == BpmnSemanticTypes.Participant)
                ids.Add(id);
        return new(document.DocumentId, document.Revision, context, ids,
            document.SemanticModel.Relationships.Where(flow => ids.Contains(flow.SourceId) && ids.Contains(flow.TargetId)).Select(static flow => flow.Id),
            scopePlan.ScopePlacements);
    }
}
