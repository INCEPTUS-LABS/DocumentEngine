using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed partial class Canvas2DInteractionController
{
    private bool? ConnectionSourceHoverEligibility(EditingSessionState state, Canvas2DSceneItem? item)
    {
        var selected = ConnectionToolState.GetSelection(state.EditorState);
        if (selected is null || item is null || !_connectionCreationCatalog.TryGetRegistration(selected, out var registration) ||
            !_session.TryCaptureDocumentSnapshot(out var document) || document.DocumentId != state.DocumentId ||
            document.Revision != state.DocumentRevision) return null;
        var sourceItem = item;
        ConnectorAnchorId? requiredAnchor = null;
        if (TryResolveConnectorAnchorHandle(state.CurrentScene!, state.EditorState, item, out var owner, out var anchor,
                out _, out _, out _, out var role, out _, requireSelectedOwner: false))
        {
            if (role != ConnectorAnchorRole.Source) return false;
            sourceItem = owner;
            requiredAnchor = anchor;
        }
        if (sourceItem.Origin.SemanticElementId is not { } semanticId || sourceItem.Origin.VisualStateId is not { } visualId ||
            !document.SemanticModel.TryGetElement(semanticId, out var element) || element is null ||
            !document.VisualModel.TryGetVisualState(visualId, out var visual) || visual is null) return null;
#pragma warning disable CA1031 // A source whose registered capability fails cannot be offered.
        try
        {
            return ElementConnectorAnchorResolver.Resolve(visual, element.TypeId, _session.ConnectorAnchorPolicyProvider)
                .Where(candidate => candidate.Allows(ConnectorAnchorRole.Source) &&
                    (requiredAnchor is null || candidate.Id == requiredAnchor) &&
                    !ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, candidate.Id))
                .Any(candidate => registration.CommandFactory.CanStart(new AnchorConnectionCreationSourceRequest(
                    state.PresentationContext, state.PresentationPlan, document, document.Revision, semanticId, visualId, candidate.Id)));
        }
        catch (Exception exception) when (IsNonFatal(exception)) { return false; }
#pragma warning restore CA1031
    }

    private static ImmutableArray<KeyValuePair<string, PropertyValue>> CaptureConnectionCandidates(
        EditingSessionState state, DocumentSnapshot document, AnchorConnectionCreationSourceRequest source,
        AnchorConnectionCreationRegistration registration)
    {
        if (registration.TargetEligibility is not { } eligibility) return [];
        var candidates = ImmutableArray.CreateBuilder<VisualStateId>();
        foreach (var node in state.ProjectedGraph!.Nodes)
        {
            if (node.Source.VisualStateId is not { } visualId) continue;
#pragma warning disable CA1031 // Candidate failures must not expose an invalid target.
            try
            {
                if (eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(state.PresentationContext, state.PresentationPlan,
                        document, document.Revision, source.SourceSemanticElementId, source.SourceVisualStateId, source.SourceAnchorId,
                        node.Source.SemanticElementId, visualId))) candidates.Add(visualId);
            }
            catch (Exception exception) when (IsNonFatal(exception)) { }
#pragma warning restore CA1031
        }
        return ConnectionCandidateMetadata.EncodeAllowedVisuals(candidates);
    }
}
