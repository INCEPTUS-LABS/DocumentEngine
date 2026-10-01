using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// BPMN application adapter for the optional SequenceFlow Name field. The generic
/// property command retains its existing replacement-only contract.
/// </summary>
internal static class ModelerPropertyEditing
{
    internal static bool CanEditMissingValue(
        SemanticTypeId typeId, ElementPropertyFieldDefinition field) =>
        typeId == BpmnSemanticTypes.SequenceFlow &&
        field.SemanticPropertyKey == BpmnSemanticProperties.Name;

    internal static ICommand? CreateCommand(
        DocumentCanvasPropertySnapshot target,
        ElementPropertyFieldDefinition field,
        PropertyValue value) =>
        CanEditMissingValue(target.TypeId, field)
            ? new UpdateBpmnSequenceFlowNameCommand(target.DocumentId, target.Revision,
                target.SemanticId, value.TextValue.Length == 0 ? null : value.TextValue)
            : null;
}
