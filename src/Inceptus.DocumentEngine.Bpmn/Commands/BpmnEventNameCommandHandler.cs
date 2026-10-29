using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnEventNameCommandHandler : ICommandHandler, ICommandValidator
{
    internal static bool Supports(SemanticTypeId typeId) =>
        BpmnSemanticTypes.IsEvent(typeId);

    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateBpmnEventNameCommand update ||
            !document.SemanticModel.TryGetElement(update.ElementId, out var element) ||
            element is null || !Supports(element.TypeId))
        {
            return [BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.InvalidCommand,
                "Name editing requires an existing BPMN Event.", command.TypeId.Value)];
        }

        element.Properties.TryGetValue(BpmnSemanticProperties.Name, out var previous);
        if (previous is not null && previous.Kind != PropertyValueKind.Text)
        {
            return [BpmnDiagnostics.Error(CommandExecutionDiagnosticCodes.SemanticPropertyUnsupported,
                "The Name must be absent or text.", update.ElementId.Value)];
        }

        if (StringComparer.Ordinal.Equals(previous?.TextValue, update.TargetName))
        {
            return [BpmnDiagnostics.Error(CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged,
                "The element already has the requested Name state.", update.ElementId.Value)];
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

        var update = (UpdateBpmnEventNameCommand)command;
        document.SemanticModel.TryGetElement(update.ElementId, out var element);
        var properties = element!.Properties.Where(static entry =>
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.Name));
        if (update.TargetName is not null)
        {
            properties = properties.Append(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.Name, PropertyValue.FromText(update.TargetName)));
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
