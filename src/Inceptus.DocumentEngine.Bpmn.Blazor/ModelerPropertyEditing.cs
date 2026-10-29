using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// BPMN application adapters for concrete type switching and optional properties.
/// The generic property command retains its replacement-only contract.
/// </summary>
internal static class ModelerPropertyEditing
{
    internal const string AppearanceInvalidDiagnosticCode = "BPMN.APPEARANCE.INVALID";

    internal static ICommand CreateAppearanceCommand(DocumentId documentId, DocumentRevision revision,
        IEnumerable<KeyValuePair<string, PropertyValue?>> changes) =>
        new SetBpmnDocumentAppearanceCommand(documentId, revision, changes);

    internal static bool SupportsConnectorActions(DocumentSnapshot document, SemanticElementId semanticId) =>
        document.SemanticModel.Relationships.Any(flow => flow.Id == semanticId &&
            (flow.TypeId == BpmnSemanticTypes.SequenceFlow || flow.TypeId == BpmnSemanticTypes.MessageFlow));

    internal static ImmutableArray<KeyValuePair<SemanticElementId, string>> CaptureParticipants(DocumentSnapshot document) =>
        [.. document.SemanticModel.Elements.Where(element => element.TypeId == BpmnSemanticTypes.Participant)
            .Select(element => KeyValuePair.Create(element.Id,
                element.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) && name.Kind == PropertyValueKind.Text
                    ? name.TextValue : string.Empty))
            .OrderBy(option => option.Value, StringComparer.Ordinal).ThenBy(option => option.Key.Value, StringComparer.Ordinal)];

    internal static ICommand CreateTypeCommand(
        DocumentCanvasPropertySnapshot target, SemanticTypeId targetType) =>
        new ChangeBpmnElementTypeCommand(target.DocumentId, target.Revision,
            target.SemanticId, targetType);

    internal static bool CanEditMissingValue(
        SemanticTypeId typeId, ElementPropertyFieldDefinition field) =>
        (typeId == BpmnSemanticTypes.SequenceFlow || typeId == BpmnSemanticTypes.MessageFlow || SupportsEventName(typeId)) &&
        field.SemanticPropertyKey == BpmnSemanticProperties.Name || IsParticipantField(typeId, field) ||
        IsDescriptionField(typeId, field);

    internal static bool SupportsEventName(SemanticTypeId typeId) =>
        BpmnSemanticTypes.IsEvent(typeId);

    internal static bool IsVisibleDataField(SemanticTypeId typeId, ElementPropertyFieldDefinition field) =>
        !BpmnSemanticTypes.IsEvent(typeId) || field.SemanticPropertyKey == BpmnSemanticProperties.Name;

    private static bool IsDescriptionField(SemanticTypeId typeId, ElementPropertyFieldDefinition field) =>
        (BpmnActivitySemanticTypes.IsActivity(typeId) || BpmnGatewaySemanticTypes.IsGateway(typeId)) &&
        field.SemanticPropertyKey == BpmnSemanticProperties.Description;

    internal static bool IsParticipantField(SemanticTypeId typeId, ElementPropertyFieldDefinition field) =>
        typeId == OrganizationalSemanticTypes.Pool && field.SemanticPropertyKey == OrganizationalSemanticProperties.ParticipantId;

    internal static ICommand? CreateCommand(
        DocumentCanvasPropertySnapshot target,
        ElementPropertyFieldDefinition field,
        PropertyValue value)
    {
        if (IsDescriptionField(target.TypeId, field))
            return new UpdateBpmnElementDescriptionCommand(target.DocumentId, target.Revision, target.SemanticId,
                value.TextValue);
        if (IsParticipantField(target.TypeId, field))
            return new SetBpmnPoolParticipantCommand(target.DocumentId, target.Revision, target.SemanticId,
                string.IsNullOrWhiteSpace(value.TextValue) ? null : new SemanticElementId(value.TextValue));
        if (!CanEditMissingValue(target.TypeId, field)) return null;

        if (SupportsEventName(target.TypeId))
            return new UpdateBpmnEventNameCommand(target.DocumentId, target.Revision, target.SemanticId,
                string.IsNullOrWhiteSpace(value.TextValue) ? null : value.TextValue);

        var name = value.TextValue.Length == 0 ? null : value.TextValue;
        return target.TypeId == BpmnSemanticTypes.MessageFlow
            ? new UpdateBpmnMessageFlowNameCommand(target.DocumentId, target.Revision, target.SemanticId, name)
            : new UpdateBpmnSequenceFlowNameCommand(target.DocumentId, target.Revision, target.SemanticId, name);
    }
}
