using System.Globalization;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Placement;

/// <summary>Shared read-only defaults for prospective and committed Toolbox creation.</summary>
internal sealed record BpmnToolboxCreationDefaults(
    SemanticTypeId SemanticTypeId,
    SizeD Size,
    string? Name,
    string? Code,
    long ElementNumber,
    string? Description)
{
    internal RectD BoundsAt(PointD hotspot) => new(
        hotspot.X - (Size.Width / 2d), hotspot.Y - (Size.Height / 2d), Size.Width, Size.Height);

    internal static BpmnToolboxCreationDefaults Resolve(
        DocumentSnapshot document, SemanticTypeId type, out Diagnostic? failure)
    {
        failure = null;
        var size = BpmnNodeLogicalSizePolicy.Resolve(type);
        if (type == BpmnSemanticTypes.StartEvent || type == BpmnSemanticTypes.EndEvent)
        {
            return new(type, size, null, null, 0L, null);
        }

        long number = 0L;
        string displayName;
        string? code = null;
        // Leave optional descriptions absent so new elements display an empty field.
        if (BpmnTaskSemanticTypes.IsTask(type))
        {
            var maximum = 0L;
            foreach (var element in document.SemanticModel.Elements)
            {
                if (BpmnTaskSemanticTypes.IsTask(element.TypeId) &&
                    element.Properties.TryGetValue(BpmnSemanticProperties.ElementNumber, out var value) &&
                    value.Kind == PropertyValueKind.Integer)
                {
                    maximum = Math.Max(maximum, value.IntegerValue);
                }
            }

            displayName = BpmnTaskSemanticTypes.DisplayName(type);
            if (maximum == long.MaxValue)
            {
                failure = new Diagnostic(BpmnToolboxPlacementDiagnosticCodes.TaskElementNumberExhausted,
                    DiagnosticSeverity.Error,
                    "A BPMN Task cannot be placed because its next Element number would exceed Int64.MaxValue.");
                return new(type, size, displayName, null, 0L, null);
            }

            number = maximum + 1L;
            var text = number.ToString(CultureInfo.InvariantCulture);
            var name = string.Concat(displayName, " ", text);
            return new(type, size, name,
                string.Concat(BpmnTaskSemanticTypes.DefaultCodePrefix(type), "_", text),
                number, null);
        }

        if (BpmnSemanticTypes.IsGateway(type))
        {
            (displayName, code) = type == BpmnSemanticTypes.ExclusiveGateway
                ? ("Exclusive Gateway", "EXCLUSIVE_GATEWAY")
                : type == BpmnSemanticTypes.ParallelGateway
                    ? ("Parallel Gateway", "PARALLEL_GATEWAY")
                    : type == BpmnSemanticTypes.InclusiveGateway
                        ? ("Inclusive Gateway", "INCLUSIVE_GATEWAY")
                        : ("Event-Based Gateway", "EVENT_BASED_GATEWAY");
            return new(type, size, displayName, code, 0L, null);
        }

        number = checked(document.SemanticModel.Elements.LongCount(element => element.TypeId == type) + 1L);
        var numberText = number.ToString(CultureInfo.InvariantCulture);
        if (type == BpmnSemanticTypes.SubProcess)
        {
            displayName = "SubProcess";
            code = string.Concat("SUBPROCESS_", numberText);
        }
        else if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type))
        {
            displayName = BpmnBoundaryEventSemanticTypes.DisplayName(type);
        }
        else
        {
            displayName = type == BpmnSemanticTypes.MessageCatchEvent ? "Message Event"
                : type == BpmnSemanticTypes.TimerCatchEvent ? "Timer Event"
                : type == BpmnSemanticTypes.MessageThrowEvent ? "Message Throw Event"
                : type == BpmnSemanticTypes.SignalCatchEvent ? "Signal Catch Event"
                : "Signal Throw Event";
        }

        return new(type, size, string.Concat(displayName, " ", numberText), code, number, null);
    }
}
