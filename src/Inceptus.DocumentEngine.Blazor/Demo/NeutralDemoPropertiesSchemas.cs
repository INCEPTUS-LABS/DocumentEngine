using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Application-only Properties schemas for the neutral demonstration model.
/// </summary>
internal static class NeutralDemoPropertiesSchemas
{
    internal static ElementPropertyFieldId NameFieldId { get; } = new("name");

    internal static ElementPropertyFieldId ElementNumberFieldId { get; } =
        new("element-number");

    internal static ElementPropertyFieldId DescriptionFieldId { get; } =
        new("description");

    internal static ImmutableArray<ElementPropertiesSchema> Schemas { get; } =
    [
        CreateNodeSchema(NeutralDemoPipeline.NeutralNodeTypeId),
        CreateNodeSchema(NeutralDemoPipeline.NeutralMixedPolicyNodeTypeId),
        new ElementPropertiesSchema(
            NeutralDemoPipeline.NeutralEdgeTypeId,
            [
                new ElementPropertyFieldDefinition(
                    NameFieldId,
                    "Name",
                    NeutralDemoPipeline.LabelPropertyKey,
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 0),
                new ElementPropertyFieldDefinition(
                    DescriptionFieldId,
                    "Description",
                    NeutralDemoPipeline.DescriptionPropertyKey,
                    ElementPropertyEditorKind.MultilineText,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 1),
            ]),
    ];

    internal static ElementPropertiesSchemaCatalog Catalog { get; } = new(Schemas);

    private static ElementPropertiesSchema CreateNodeSchema(SemanticTypeId typeId) =>
        new(
            typeId,
            [
                new ElementPropertyFieldDefinition(
                    NameFieldId,
                    "Name",
                    NeutralDemoPipeline.LabelPropertyKey,
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Name,
                    isEditable: true,
                    order: 0),
                new ElementPropertyFieldDefinition(
                    ElementNumberFieldId,
                    "Element number",
                    NeutralDemoPipeline.ElementNumberPropertyKey,
                    ElementPropertyEditorKind.Integer,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 1),
                new ElementPropertyFieldDefinition(
                    DescriptionFieldId,
                    "Description",
                    NeutralDemoPipeline.DescriptionPropertyKey,
                    ElementPropertyEditorKind.MultilineText,
                    SemanticPropertyMutationKind.Property,
                    isEditable: true,
                    order: 2),
            ]);
}
