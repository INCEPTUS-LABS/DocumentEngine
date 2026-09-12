using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Properties;

internal static class BpmnMessageCatchEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateNameAndDescriptionFields());
}

internal static class BpmnMessageThrowEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateNameAndDescriptionFields());
}

internal static class BpmnMessageBoundaryEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateBoundaryEventFields());
}

internal static class BpmnTimerCatchEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.TimerCatchEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateTimerFields());
}

internal static class BpmnTimerBoundaryEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.TimerBoundaryEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateBoundaryTimerFields());
}

internal static class BpmnSignalCatchEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.SignalCatchEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateNameAndDescriptionFields());
}

internal static class BpmnSignalThrowEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.SignalThrowEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateNameAndDescriptionFields());
}

internal static class BpmnSignalBoundaryEventPropertiesSchema
{
    internal static ElementPropertiesSchema Definition { get; } = new(
        BpmnSemanticTypes.SignalBoundaryEvent,
        BpmnIntermediateEventPropertiesSchemaFields.CreateBoundaryEventFields());
}

internal static class BpmnIntermediateEventPropertiesSchemaFields
{
    internal static ElementPropertyFieldDefinition[] CreateNameAndDescriptionFields() =>
        [
            Name(order: 0),
            Description(order: 1),
        ];

    internal static ElementPropertyFieldDefinition[] CreateTimerFields() =>
        [
            Name(order: 0),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("timer-definition"),
                "Timer definition",
                BpmnSemanticProperties.TimerDefinition,
                ElementPropertyEditorKind.MultilineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 1),
            Description(order: 2),
        ];

    internal static ElementPropertyFieldDefinition[] CreateBoundaryTimerFields() =>
        [
            Name(order: 0),
            new ElementPropertyFieldDefinition(
                new ElementPropertyFieldId("timer-definition"),
                "Timer definition",
                BpmnSemanticProperties.TimerDefinition,
                ElementPropertyEditorKind.MultilineText,
                SemanticPropertyMutationKind.Property,
                isEditable: true,
                order: 1),
            Interrupting(order: 2),
            Description(order: 3),
        ];

    internal static ElementPropertyFieldDefinition[] CreateBoundaryEventFields() =>
        [
            Name(order: 0),
            Interrupting(order: 1),
            Description(order: 2),
        ];

    private static ElementPropertyFieldDefinition Name(int order) => new(
        new ElementPropertyFieldId("name"),
        "Name",
        BpmnSemanticProperties.Name,
        ElementPropertyEditorKind.SingleLineText,
        SemanticPropertyMutationKind.Property,
        isEditable: true,
        order: order);

    private static ElementPropertyFieldDefinition Description(int order) => new(
        new ElementPropertyFieldId("description"),
        "Description",
        BpmnSemanticProperties.Description,
        ElementPropertyEditorKind.MultilineText,
        SemanticPropertyMutationKind.Property,
        isEditable: true,
        order: order);

    private static ElementPropertyFieldDefinition Interrupting(int order) => new(
        new ElementPropertyFieldId("interrupting"),
        "Interrupting",
        BpmnSemanticProperties.CancelActivity,
        ElementPropertyEditorKind.Boolean,
        SemanticPropertyMutationKind.Property,
        isEditable: true,
        order: order);
}
