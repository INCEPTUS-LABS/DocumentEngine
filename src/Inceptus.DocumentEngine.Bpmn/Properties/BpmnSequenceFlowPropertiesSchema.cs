using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnSequenceFlowPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.SequenceFlow,
        [new ElementPropertyFieldDefinition(new ElementPropertyFieldId("name"), "Name",
            BpmnSemanticProperties.Name, ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Property, isEditable: true, order: 0)]);
}
