using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnSequenceFlowNameCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateBpmnSequenceFlowNameCommand update ||
            !document.SemanticModel.TryGetRelationship(update.RelationshipId, out var relationship) ||
            relationship?.TypeId != BpmnSemanticTypes.SequenceFlow)
        {
            return [BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.InvalidCommand,
                "Name editing requires an existing BPMN Sequence Flow.", command.TypeId.Value)];
        }

        relationship.Properties.TryGetValue(BpmnSemanticProperties.Name, out var previous);
        if (previous is not null && previous.Kind != PropertyValueKind.Text)
        {
            return [BpmnDiagnostics.Error(CommandExecutionDiagnosticCodes.SemanticPropertyUnsupported,
                "The Sequence Flow Name must be absent or text.", update.RelationshipId.Value)];
        }

        if (StringComparer.Ordinal.Equals(previous?.TextValue, update.TargetName))
        {
            return [BpmnDiagnostics.Error(CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged,
                "The Sequence Flow already has the requested Name state.", update.RelationshipId.Value)];
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

        var update = (UpdateBpmnSequenceFlowNameCommand)command;
        document.SemanticModel.TryGetRelationship(update.RelationshipId, out var relationship);
        var properties = relationship!.Properties.Where(static entry =>
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.Name));
        if (update.TargetName is not null)
        {
            properties = properties.Append(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.Name, PropertyValue.FromText(update.TargetName)));
        }

        var replacement = new SemanticRelationshipSnapshot(
            relationship.Id, relationship.TypeId, relationship.SourceId, relationship.TargetId, properties);
        var semantic = document.SemanticModel;
        var proposed = new SemanticModelSnapshot(
            document.DocumentId, document.Revision, semantic.Elements,
            semantic.Relationships.Select(item => item.Id == replacement.Id ? replacement : item),
            semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(proposed, document.VisualModel, document.Metadata, document.Publication)));
    }
}
