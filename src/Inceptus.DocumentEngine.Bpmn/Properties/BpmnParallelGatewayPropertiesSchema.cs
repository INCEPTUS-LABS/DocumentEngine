using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnParallelGatewayPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.ParallelGateway,
        BpmnGatewayPropertiesSchemaFields.Create(SemanticPropertyMutationKind.Name));
}

internal static class BpmnGatewayPropertiesSchemaFields
{
    internal static ElementPropertyFieldDefinition[] Create(
        SemanticPropertyMutationKind nameMutationKind) =>
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
                nameMutationKind,
                isEditable: true,
                order: 1),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("description"),
                "Description",
                BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 2),
        ];
}
