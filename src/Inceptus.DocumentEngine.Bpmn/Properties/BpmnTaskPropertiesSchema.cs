using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnTaskPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } =
        Create(BpmnSemanticTypes.Task);

    internal static ImmutableArray<ElementPropertiesSchema> SpecializedDefinitions
    { get; } = BpmnTaskSemanticTypes.All
        .Where(static typeId => typeId != BpmnSemanticTypes.Task)
        .Select(Create)
        .ToImmutableArray();

    private static ElementPropertiesSchema Create(SemanticTypeId semanticTypeId) =>
        new(
        semanticTypeId,
        [
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("code"),
                "Code",
                BpmnSemanticProperties.Code,
                ElementPropertyEditorKind.SingleLineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 0),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("name"),
                "Name",
                BpmnSemanticProperties.Name,
                ElementPropertyEditorKind.SingleLineText,
                SemanticPropertyMutationKind.Name,
                isEditable: true,
                order: 1),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("element-number"),
                "Element number",
                BpmnSemanticProperties.ElementNumber,
                ElementPropertyEditorKind.Integer,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 2),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("description"),
                "Description",
                BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 3),
        ]);
}
