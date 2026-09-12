using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>
/// Provides typed access to BPMN Participant structural references stored in the
/// plugin-owned semantic property map.
/// </summary>
public static class BpmnCollaborationSemantics
{
    public static bool TryGetCollaborationId(
        SemanticElementSnapshot participant,
        out SemanticElementId? collaborationId)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (participant.TypeId != BpmnSemanticTypes.Participant ||
            !TryReadReference(
                participant,
                BpmnSemanticProperties.CollaborationId,
                out var value))
        {
            collaborationId = null;
            return false;
        }

        collaborationId = new SemanticElementId(value);
        return true;
    }

    public static bool TryGetProcessScopeId(
        SemanticElementSnapshot participant,
        out DocumentScopeId? processScopeId)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (participant.TypeId != BpmnSemanticTypes.Participant ||
            !participant.Properties.TryGetValue(
                BpmnSemanticProperties.ProcessScopeId,
                out var property))
        {
            processScopeId = null;
            return participant.TypeId == BpmnSemanticTypes.Participant;
        }

        if (property.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(property.TextValue))
        {
            processScopeId = null;
            return false;
        }

        processScopeId = new DocumentScopeId(property.TextValue);
        return true;
    }

    private static bool TryReadReference(
        SemanticElementSnapshot element,
        string propertyKey,
        out string value)
    {
        if (element.Properties.TryGetValue(propertyKey, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
