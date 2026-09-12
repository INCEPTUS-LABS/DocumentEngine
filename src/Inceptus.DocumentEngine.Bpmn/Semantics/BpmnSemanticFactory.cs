using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

public static class BpmnSemanticFactory
{
    public static SemanticElementSnapshot CreateCollaboration(
        SemanticElementId id,
        string? name = null,
        string? description = null) =>
        new(
            id,
            BpmnSemanticTypes.Collaboration,
            OptionalProperties(name, description),
            containmentKind: SemanticElementContainmentKind.Document);

    public static SemanticElementSnapshot CreateParticipant(
        SemanticElementId id,
        SemanticElementId collaborationId,
        DocumentScopeId? processScopeId = null,
        string? name = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(collaborationId);
        var properties = OptionalProperties(name, description);
        properties.Add(new KeyValuePair<string, PropertyValue>(
            BpmnSemanticProperties.CollaborationId,
            PropertyValue.FromText(collaborationId.Value)));
        if (processScopeId is not null)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.ProcessScopeId,
                PropertyValue.FromText(processScopeId.Value)));
        }

        return new SemanticElementSnapshot(
            id,
            BpmnSemanticTypes.Participant,
            properties,
            containmentKind: SemanticElementContainmentKind.Document);
    }

    public static SemanticElementSnapshot CreateStartEvent(
        SemanticElementId id,
        string? name = null,
        string? description = null) =>
        new(id, BpmnSemanticTypes.StartEvent, OptionalProperties(name, description));

    public static SemanticElementSnapshot CreateTask(
        SemanticElementId id,
        string code,
        string name,
        long elementNumber,
        string? description = null) =>
        CreateTask(
            id,
            BpmnSemanticTypes.Task,
            code,
            name,
            elementNumber,
            description);

    public static SemanticElementSnapshot CreateTask(
        SemanticElementId id,
        SemanticTypeId taskTypeId,
        string code,
        string name,
        long elementNumber,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(taskTypeId);
        if (!BpmnTaskSemanticTypes.IsTask(taskTypeId))
        {
            throw new ArgumentException(
                $"Semantic type '{taskTypeId}' is not a supported BPMN Task.",
                nameof(taskTypeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(BpmnSemanticProperties.Code, PropertyValue.FromText(code)),
            new(BpmnSemanticProperties.Name, PropertyValue.FromText(name)),
            new(
                BpmnSemanticProperties.ElementNumber,
                PropertyValue.FromInteger(elementNumber)),
        };
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return new SemanticElementSnapshot(id, taskTypeId, properties);
    }

    public static SemanticElementSnapshot CreateSubProcess(
        SemanticElementId id,
        string code,
        string name,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(BpmnSemanticProperties.Code, PropertyValue.FromText(code)),
            new(BpmnSemanticProperties.Name, PropertyValue.FromText(name)),
        };
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return new SemanticElementSnapshot(id, BpmnSemanticTypes.SubProcess, properties);
    }

    public static SemanticElementSnapshot CreateExclusiveGateway(
        SemanticElementId id,
        string code,
        string name,
        string? description = null) =>
        CreateGateway(
            id,
            BpmnSemanticTypes.ExclusiveGateway,
            code,
            name,
            description);

    public static SemanticElementSnapshot CreateParallelGateway(
        SemanticElementId id,
        string code,
        string name,
        string? description = null) =>
        CreateGateway(
            id,
            BpmnSemanticTypes.ParallelGateway,
            code,
            name,
            description);

    public static SemanticElementSnapshot CreateInclusiveGateway(
        SemanticElementId id,
        string code,
        string name,
        string? description = null) =>
        CreateGateway(
            id,
            BpmnSemanticTypes.InclusiveGateway,
            code,
            name,
            description);

    public static SemanticElementSnapshot CreateEventBasedGateway(
        SemanticElementId id,
        string code,
        string name,
        string? description = null) =>
        CreateGateway(
            id,
            BpmnSemanticTypes.EventBasedGateway,
            code,
            name,
            description);

    public static SemanticElementSnapshot CreateMessageCatchEvent(
        SemanticElementId id,
        string name,
        string? description = null) =>
        CreateIntermediateEvent(
            id,
            BpmnSemanticTypes.MessageCatchEvent,
            name,
            description);

    public static SemanticElementSnapshot CreateMessageThrowEvent(
        SemanticElementId id,
        string name,
        string? description = null) =>
        CreateIntermediateEvent(
            id,
            BpmnSemanticTypes.MessageThrowEvent,
            name,
            description);

    public static SemanticElementSnapshot CreateTimerCatchEvent(
        SemanticElementId id,
        string name,
        string? timerDefinition = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (timerDefinition is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(timerDefinition);
        }

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(BpmnSemanticProperties.Name, PropertyValue.FromText(name)),
            new(
                BpmnSemanticProperties.TimerDefinition,
                PropertyValue.FromText(timerDefinition ?? string.Empty)),
        };
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return new SemanticElementSnapshot(id, BpmnSemanticTypes.TimerCatchEvent, properties);
    }

    public static SemanticElementSnapshot CreateTimerBoundaryEvent(
        SemanticElementId id,
        SemanticElementId attachedToActivityId,
        string name,
        string? timerDefinition = null,
        bool cancelActivity = true,
        string? description = null)
        => CreateBoundaryEvent(
            id,
            BpmnSemanticTypes.TimerBoundaryEvent,
            attachedToActivityId,
            name,
            timerDefinition,
            cancelActivity,
            description);

    public static SemanticElementSnapshot CreateMessageBoundaryEvent(
        SemanticElementId id,
        SemanticElementId attachedToActivityId,
        string name,
        bool cancelActivity = true,
        string? description = null) =>
        CreateBoundaryEvent(
            id,
            BpmnSemanticTypes.MessageBoundaryEvent,
            attachedToActivityId,
            name,
            timerDefinition: null,
            cancelActivity,
            description);

    public static SemanticElementSnapshot CreateSignalBoundaryEvent(
        SemanticElementId id,
        SemanticElementId attachedToActivityId,
        string name,
        bool cancelActivity = true,
        string? description = null) =>
        CreateBoundaryEvent(
            id,
            BpmnSemanticTypes.SignalBoundaryEvent,
            attachedToActivityId,
            name,
            timerDefinition: null,
            cancelActivity,
            description);

    public static SemanticElementSnapshot CreateSignalCatchEvent(
        SemanticElementId id,
        string name,
        string? description = null) =>
        CreateIntermediateEvent(
            id,
            BpmnSemanticTypes.SignalCatchEvent,
            name,
            description);

    public static SemanticElementSnapshot CreateSignalThrowEvent(
        SemanticElementId id,
        string name,
        string? description = null) =>
        CreateIntermediateEvent(
            id,
            BpmnSemanticTypes.SignalThrowEvent,
            name,
            description);

    private static SemanticElementSnapshot CreateGateway(
        SemanticElementId id,
        SemanticTypeId typeId,
        string code,
        string name,
        string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(BpmnSemanticProperties.Code, PropertyValue.FromText(code)),
            new(BpmnSemanticProperties.Name, PropertyValue.FromText(name)),
        };
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return new SemanticElementSnapshot(id, typeId, properties);
    }

    private static SemanticElementSnapshot CreateIntermediateEvent(
        SemanticElementId id,
        SemanticTypeId typeId,
        string name,
        string? description)
    {
        if (!BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId) ||
            typeId == BpmnSemanticTypes.TimerCatchEvent)
        {
            throw new ArgumentException(
                $"Semantic type '{typeId}' does not use the shared Name/Description intermediate-event representation.",
                nameof(typeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(BpmnSemanticProperties.Name, PropertyValue.FromText(name)),
        };
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return new SemanticElementSnapshot(id, typeId, properties);
    }

    internal static SemanticElementSnapshot CreateBoundaryEvent(
        SemanticElementId id,
        SemanticTypeId typeId,
        SemanticElementId attachedToActivityId,
        string name,
        string? timerDefinition,
        bool cancelActivity,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(attachedToActivityId);
        if (!BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(typeId))
        {
            throw new ArgumentException(
                $"Semantic type '{typeId}' is not a supported BPMN Boundary Event.",
                nameof(typeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (typeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            if (timerDefinition is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(timerDefinition);
            }
        }
        else if (timerDefinition is not null)
        {
            throw new ArgumentException(
                $"Semantic type '{typeId}' does not support a Timer definition.",
                nameof(timerDefinition));
        }

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(BpmnSemanticProperties.Name, PropertyValue.FromText(name)),
        };
        if (typeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.TimerDefinition,
                PropertyValue.FromText(timerDefinition ?? string.Empty)));
        }

        properties.Add(new KeyValuePair<string, PropertyValue>(
            BpmnSemanticProperties.CancelActivity,
            PropertyValue.FromBoolean(cancelActivity)));
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return new SemanticElementSnapshot(
            id,
            typeId,
            properties,
            attachedToElementId: attachedToActivityId);
    }

    public static SemanticElementSnapshot CreateEndEvent(
        SemanticElementId id,
        string? name = null,
        string? description = null) =>
        new(id, BpmnSemanticTypes.EndEvent, OptionalProperties(name, description));

    public static SemanticRelationshipSnapshot CreateSequenceFlow(
        SemanticElementId id,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        string? name = null,
        string? description = null) =>
        new(
            id,
            BpmnSemanticTypes.SequenceFlow,
            sourceId,
            targetId,
            OptionalProperties(name, description));

    private static List<KeyValuePair<string, PropertyValue>> OptionalProperties(
        string? name,
        string? description)
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>();
        AddOptionalText(properties, BpmnSemanticProperties.Name, name);
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        return properties;
    }

    private static void AddOptionalText(
        List<KeyValuePair<string, PropertyValue>> properties,
        string key,
        string? value)
    {
        if (value is null)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        properties.Add(new(key, PropertyValue.FromText(value)));
    }
}
