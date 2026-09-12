using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Properties;

public static class OrganizationalPoolPropertiesSchema
{
    public static ElementPropertiesSchema Definition { get; } =
        new(
            OrganizationalSemanticTypes.Pool,
            [
                new ElementPropertyFieldDefinition(
                    new ElementPropertyFieldId("name"),
                    "Name",
                    OrganizationalSemanticProperties.Name,
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Name,
                    isEditable: true,
                    order: 0),
                new ElementPropertyFieldDefinition(
                    new ElementPropertyFieldId("description"),
                    "Description",
                    OrganizationalSemanticProperties.Description,
                    ElementPropertyEditorKind.MultilineText,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 1),
            ]);
}
