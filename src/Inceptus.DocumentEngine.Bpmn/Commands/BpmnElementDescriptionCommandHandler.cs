using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnElementDescriptionCommandHandler : ICommandHandler, ICommandValidator
{
    internal static bool Supports(SemanticTypeId typeId) =>
        BpmnActivitySemanticTypes.IsActivity(typeId) || BpmnGatewaySemanticTypes.IsGateway(typeId);

    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateBpmnElementDescriptionCommand update ||
            !document.SemanticModel.TryGetElement(update.ElementId, out var element) ||
            element is null || !Supports(element.TypeId))
        {
            return [BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.InvalidCommand,
                "Description editing requires an existing BPMN activity or gateway.", command.TypeId.Value)];
        }

        element.Properties.TryGetValue(BpmnSemanticProperties.Description, out var previous);
        if (previous is not null && previous.Kind != PropertyValueKind.Text)
        {
            return [BpmnDiagnostics.Error(CommandExecutionDiagnosticCodes.SemanticPropertyUnsupported,
                "The Description must be absent or text.", update.ElementId.Value)];
        }

        if (StringComparer.Ordinal.Equals(previous?.TextValue, update.TargetDescription))
        {
            return [BpmnDiagnostics.Error(CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged,
                "The element already has the requested Description state.", update.ElementId.Value)];
        }

        return [];
    }

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var update = (UpdateBpmnElementDescriptionCommand)command;
        document.SemanticModel.TryGetElement(update.ElementId, out var element);
        var properties = element!.Properties.Where(static entry =>
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.Description));
        if (update.TargetDescription is not null)
        {
            properties = properties.Append(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.Description, PropertyValue.FromText(update.TargetDescription)));
        }

        var replacement = new SemanticElementSnapshot(element.Id, element.TypeId, properties,
            element.AttachedToElementId, element.ContainmentKind);
        var semantic = document.SemanticModel;
        var proposed = new SemanticModelSnapshot(
            document.DocumentId, document.Revision,
            semantic.Elements.Select(item => item.Id == replacement.Id ? replacement : item),
            semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships,
            semantic.ModelProfiles, semantic.ProfileAssignments);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(proposed, document.VisualModel, document.Metadata, document.Publication)));
    }
}
