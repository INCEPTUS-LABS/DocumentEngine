using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnStartEndEventPropertiesSchemas
{
    internal static ElementPropertiesSchema Start { get; } = new(BpmnSemanticTypes.StartEvent, [Name()]);
    internal static ElementPropertiesSchema End { get; } = new(BpmnSemanticTypes.EndEvent, [Name()]);

    private static ElementPropertyFieldDefinition Name() => new(
        new ElementPropertyFieldId("name"), "Name", BpmnSemanticProperties.Name,
        ElementPropertyEditorKind.SingleLineText, SemanticPropertyMutationKind.Property,
        isEditable: true, order: 0);
}
