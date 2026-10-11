using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnParticipantPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(BpmnSemanticTypes.Participant,
        [new ElementPropertyFieldDefinition(new ElementPropertyFieldId("name"), "Name", BpmnSemanticProperties.Name,
            ElementPropertyEditorKind.SingleLineText, SemanticPropertyMutationKind.Name, isEditable: true, order: 0)]);
}
