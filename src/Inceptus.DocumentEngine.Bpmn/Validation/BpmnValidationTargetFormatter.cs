using System.Globalization;
using System.Text;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Validation;

/// <summary>
/// Produces consistent user-facing references for the supported BPMN semantic subset.
/// </summary>
internal static class BpmnValidationTargetFormatter
{
    internal static string FormatElementReference(SemanticElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var reference = new StringBuilder(DisplayName(element.TypeId));
        if (BpmnTaskSemanticTypes.IsTask(element.TypeId) &&
            TryReadInteger(element.Properties, BpmnSemanticProperties.ElementNumber,
                out var elementNumber))
        {
            reference.Append(' ');
            reference.Append(elementNumber.ToString(CultureInfo.InvariantCulture));
        }

        if (TryReadNonEmptyText(element.Properties, BpmnSemanticProperties.Name,
                out var name))
        {
            reference.Append(" \"");
            reference.Append(name);
            reference.Append('"');
        }

        reference.Append(" [");
        if (SupportsCode(element.TypeId) &&
            TryReadNonEmptyText(element.Properties, BpmnSemanticProperties.Code,
                out var code))
        {
            reference.Append("Code: ");
            reference.Append(code);
            reference.Append(", ");
        }

        reference.Append("ID: ");
        reference.Append(element.Id.Value);
        reference.Append(']');
        return reference.ToString();
    }

    internal static string FormatSequenceFlowReference(
        SemanticRelationshipSnapshot sequenceFlow)
    {
        ArgumentNullException.ThrowIfNull(sequenceFlow);
        if (sequenceFlow.TypeId != BpmnSemanticTypes.SequenceFlow)
        {
            throw new ArgumentException(
                $"Semantic relationship '{sequenceFlow.Id}' is not a BPMN Sequence Flow.",
                nameof(sequenceFlow));
        }

        var reference = new StringBuilder(DisplayName(sequenceFlow.TypeId));
        if (TryReadNonEmptyText(sequenceFlow.Properties, BpmnSemanticProperties.Name,
                out var name))
        {
            reference.Append(" \"");
            reference.Append(name);
            reference.Append('"');
        }

        reference.Append(" [ID: ");
        reference.Append(sequenceFlow.Id.Value);
        reference.Append(']');
        return reference.ToString();
    }

    internal static string FormatUnknownElementReference(SemanticElementId elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        return $"BPMN Element [ID: {elementId.Value}]";
    }

    private static string DisplayName(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (typeId == BpmnSemanticTypes.StartEvent)
        {
            return "Start Event";
        }

        if (typeId == BpmnSemanticTypes.EndEvent)
        {
            return "End Event";
        }

        if (BpmnTaskSemanticTypes.IsTask(typeId))
        {
            return BpmnTaskSemanticTypes.DisplayName(typeId);
        }

        if (typeId == BpmnSemanticTypes.SubProcess)
        {
            return "SubProcess";
        }

        if (typeId == BpmnSemanticTypes.ExclusiveGateway)
        {
            return "Exclusive Gateway";
        }

        if (typeId == BpmnSemanticTypes.ParallelGateway)
        {
            return "Parallel Gateway";
        }

        if (typeId == BpmnSemanticTypes.InclusiveGateway)
        {
            return "Inclusive Gateway";
        }

        if (typeId == BpmnSemanticTypes.EventBasedGateway)
        {
            return "Event-Based Gateway";
        }

        if (BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId))
        {
            return BpmnIntermediateEventSemanticTypes.DisplayName(typeId);
        }

        if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(typeId))
        {
            return BpmnBoundaryEventSemanticTypes.DisplayName(typeId);
        }

        if (typeId == BpmnSemanticTypes.SequenceFlow)
        {
            return "Sequence Flow";
        }

        throw new ArgumentException(
            $"Semantic type '{typeId}' has no supported BPMN display name.",
            nameof(typeId));
    }

    private static bool SupportsCode(SemanticTypeId typeId) =>
        BpmnActivitySemanticTypes.IsActivity(typeId) ||
        BpmnSemanticTypes.IsGateway(typeId);

    private static bool TryReadNonEmptyText(
        PropertyMap properties,
        string key,
        out string value)
    {
        if (properties.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadInteger(
        PropertyMap properties,
        string key,
        out long value)
    {
        if (properties.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Integer)
        {
            value = property.IntegerValue;
            return true;
        }

        value = default;
        return false;
    }
}
