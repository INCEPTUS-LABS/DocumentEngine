using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnOrganizationalCommandHandler<TCommand> : ICommandHandler
    where TCommand : BpmnOrganizationalCommand
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not TCommand)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
                BpmnOrganizationalCommandSupport.InvalidShape(command)));
        }

        if (!BpmnOrganizationalCommandSupport.TryCreateProposedModel(
                command,
                document,
                out var semanticModel,
                out var diagnostics) ||
            semanticModel is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                semanticModel,
                document.VisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: PipelineInvalidation.Scene));
    }
}

internal sealed class BpmnOrganizationalCommandValidator<TCommand> : ICommandValidator
    where TCommand : BpmnOrganizationalCommand
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not TCommand)
        {
            return BpmnOrganizationalCommandSupport.InvalidShape(command);
        }

        _ = BpmnOrganizationalCommandSupport.TryCreateProposedModel(
            command,
            document,
            out _,
            out var diagnostics);
        return diagnostics;
    }
}

internal sealed class BpmnOrganizationalGenericUpdateGuard : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        var targetId = command switch
        {
            UpdateSemanticElementNameCommand update => update.TargetSemanticElementId,
            UpdateSemanticElementPropertyCommand update => update.TargetSemanticElementId,
            _ => null,
        };
        if (targetId is null ||
            !document.SemanticModel.TryGetElement(targetId, out var target) ||
            target is null ||
            !BpmnSemanticTypes.IsOrganizationalElement(target.TypeId))
        {
            return [];
        }

        if (!document.SemanticModel.ModelProfiles.IsAvailable(
                BpmnModelProfiles.OrganizationalId))
        {
            return
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.OrganizationalProfileUnavailable,
                    "BPMN Organizational data cannot be edited while the Organizational profile is unavailable.",
                    targetId.Value),
            ];
        }

        var structuralPropertyKey = command switch
        {
            UpdateSemanticElementNameCommand nameUpdate => nameUpdate.NamePropertyKey,
            UpdateSemanticElementPropertyCommand propertyUpdate =>
                propertyUpdate.PropertyKey,
            _ => null,
        };
        if (StringComparer.Ordinal.Equals(
                structuralPropertyKey,
                BpmnSemanticProperties.CollaborationId) ||
            StringComparer.Ordinal.Equals(
                structuralPropertyKey,
                BpmnSemanticProperties.ProcessScopeId))
        {
            return
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.OrganizationalStructuralReferenceInvalid,
                    "Participant structural references require the typed BPMN Participant update command.",
                    targetId.Value),
            ];
        }

        return [];
    }
}

internal static class BpmnOrganizationalCommandSupport
{
    internal static bool TryCreateProposedModel(
        ICommand command,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        semanticModel = null;
        if (command is not BpmnOrganizationalCommand)
        {
            diagnostics = InvalidShape(command);
            return false;
        }

        if (!document.SemanticModel.ModelProfiles.IsAvailable(
                BpmnModelProfiles.OrganizationalId))
        {
            diagnostics =
            [
                Error(
                    BpmnCommandDiagnosticCodes.OrganizationalProfileUnavailable,
                    "BPMN Organizational data cannot be edited while the Organizational profile is unavailable.",
                    command.TypeId.Value),
            ];
            return false;
        }

        return command switch
        {
            CreateBpmnCollaborationCommand creation =>
                TryCreateCollaboration(creation, document, out semanticModel, out diagnostics),
            CreateBpmnParticipantCommand creation =>
                TryCreateParticipant(creation, document, out semanticModel, out diagnostics),
            UpdateBpmnCollaborationCommand update =>
                TryUpdateCollaboration(update, document, out semanticModel, out diagnostics),
            UpdateBpmnParticipantCommand update =>
                TryUpdateParticipant(update, document, out semanticModel, out diagnostics),
            DeleteBpmnParticipantCommand deletion =>
                TryDeleteParticipant(deletion, document, out semanticModel, out diagnostics),
            DeleteBpmnCollaborationCommand deletion =>
                TryDeleteCollaboration(deletion, document, out semanticModel, out diagnostics),
            _ => Fail(
                InvalidShape(command),
                out semanticModel,
                out diagnostics),
        };
    }

    internal static ImmutableArray<Diagnostic> InvalidShape(ICommand command) =>
    [
        Error(
            BpmnCommandDiagnosticCodes.InvalidCommand,
            "The BPMN Organizational command has an invalid immutable request shape.",
            command.TypeId.Value),
    ];

    private static bool TryCreateCollaboration(
        CreateBpmnCollaborationCommand creation,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var textDiagnostic = ValidateOptionalText(
            creation.Name,
            creation.Description,
            creation.CollaborationId);
        if (textDiagnostic is not null)
        {
            return Fail([textDiagnostic], out semanticModel, out diagnostics);
        }

        if (SemanticIdentityExists(document, creation.CollaborationId))
        {
            return Fail(
                [Error(
                    BpmnCommandDiagnosticCodes.DuplicateSemanticId,
                    $"Semantic identity '{creation.CollaborationId}' already exists.",
                    creation.CollaborationId.Value)],
                out semanticModel,
                out diagnostics);
        }

        var collaboration = BpmnSemanticFactory.CreateCollaboration(
            creation.CollaborationId,
            creation.Name,
            creation.Description);
        semanticModel = ReplaceElements(
            document,
            document.SemanticModel.Elements.Append(collaboration));
        diagnostics = [];
        return true;
    }

    private static bool TryCreateParticipant(
        CreateBpmnParticipantCommand creation,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var textDiagnostic = ValidateOptionalText(
            creation.Name,
            creation.Description,
            creation.ParticipantId);
        if (textDiagnostic is not null)
        {
            return Fail([textDiagnostic], out semanticModel, out diagnostics);
        }

        if (SemanticIdentityExists(document, creation.ParticipantId))
        {
            return Fail(
                [Error(
                    BpmnCommandDiagnosticCodes.DuplicateSemanticId,
                    $"Semantic identity '{creation.ParticipantId}' already exists.",
                    creation.ParticipantId.Value)],
                out semanticModel,
                out diagnostics);
        }

        var referenceDiagnostic = ValidateParticipantReferences(
            creation.CollaborationId,
            creation.ProcessScopeId,
            creation.ParticipantId,
            document);
        if (referenceDiagnostic is not null)
        {
            return Fail([referenceDiagnostic], out semanticModel, out diagnostics);
        }

        var participant = BpmnSemanticFactory.CreateParticipant(
            creation.ParticipantId,
            creation.CollaborationId,
            creation.ProcessScopeId,
            creation.Name,
            creation.Description);
        semanticModel = ReplaceElements(
            document,
            document.SemanticModel.Elements.Append(participant));
        diagnostics = [];
        return true;
    }

    private static bool TryUpdateCollaboration(
        UpdateBpmnCollaborationCommand update,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetOrganizationalElement(
                document,
                update.CollaborationId,
                BpmnSemanticTypes.Collaboration,
                out var existing,
                out var targetDiagnostic))
        {
            return Fail([targetDiagnostic!], out semanticModel, out diagnostics);
        }

        var textDiagnostic = ValidateOptionalText(
            update.Name,
            update.Description,
            update.CollaborationId);
        if (textDiagnostic is not null)
        {
            return Fail([textDiagnostic], out semanticModel, out diagnostics);
        }

        var replacement = ReplaceProperties(
            existing!,
            update.Name,
            update.Description,
            collaborationId: null,
            processScopeId: null);
        return CompleteUpdate(
            document,
            existing!,
            replacement,
            out semanticModel,
            out diagnostics);
    }

    private static bool TryUpdateParticipant(
        UpdateBpmnParticipantCommand update,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetOrganizationalElement(
                document,
                update.ParticipantId,
                BpmnSemanticTypes.Participant,
                out var existing,
                out var targetDiagnostic))
        {
            return Fail([targetDiagnostic!], out semanticModel, out diagnostics);
        }

        var textDiagnostic = ValidateOptionalText(
            update.Name,
            update.Description,
            update.ParticipantId);
        if (textDiagnostic is not null)
        {
            return Fail([textDiagnostic], out semanticModel, out diagnostics);
        }

        var referenceDiagnostic = ValidateParticipantReferences(
            update.CollaborationId,
            update.ProcessScopeId,
            update.ParticipantId,
            document);
        if (referenceDiagnostic is not null)
        {
            return Fail([referenceDiagnostic], out semanticModel, out diagnostics);
        }

        var replacement = ReplaceProperties(
            existing!,
            update.Name,
            update.Description,
            update.CollaborationId,
            update.ProcessScopeId);
        return CompleteUpdate(
            document,
            existing!,
            replacement,
            out semanticModel,
            out diagnostics);
    }

    private static bool TryDeleteParticipant(
        DeleteBpmnParticipantCommand deletion,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetOrganizationalElement(
                document,
                deletion.ParticipantId,
                BpmnSemanticTypes.Participant,
                out _,
                out var targetDiagnostic))
        {
            return Fail([targetDiagnostic!], out semanticModel, out diagnostics);
        }

        return TryDeleteElements(
            document,
            [deletion.ParticipantId],
            out semanticModel,
            out diagnostics);
    }

    private static bool TryDeleteCollaboration(
        DeleteBpmnCollaborationCommand deletion,
        DocumentSnapshot document,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (!TryGetOrganizationalElement(
                document,
                deletion.CollaborationId,
                BpmnSemanticTypes.Collaboration,
                out _,
                out var targetDiagnostic))
        {
            return Fail([targetDiagnostic!], out semanticModel, out diagnostics);
        }

        var removedIds = document.SemanticModel.Elements
            .Where(element =>
                element.Id == deletion.CollaborationId ||
                (element.TypeId == BpmnSemanticTypes.Participant &&
                    BpmnCollaborationSemantics.TryGetCollaborationId(
                        element,
                        out var collaborationId) &&
                    collaborationId == deletion.CollaborationId))
            .Select(static element => element.Id)
            .ToImmutableHashSet();
        return TryDeleteElements(
            document,
            removedIds,
            out semanticModel,
            out diagnostics);
    }

    private static bool TryDeleteElements(
        DocumentSnapshot document,
        ImmutableHashSet<SemanticElementId> removedIds,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (document.SemanticModel.Relationships.Any(relationship =>
                removedIds.Contains(relationship.SourceId) ||
                removedIds.Contains(relationship.TargetId)) ||
            document.VisualModel.VisualStates.Any(visual =>
                removedIds.Contains(visual.SemanticElementId)) ||
            document.SemanticModel.ScopeMemberships.Any(membership =>
                removedIds.Contains(membership.SemanticElementId)))
        {
            return Fail(
                [Error(
                    BpmnCommandDiagnosticCodes.OrganizationalSemanticInvalid,
                    "The BPMN Organizational element has unsupported relationship, Visual State, or Process-membership state.",
                    removedIds.OrderBy(static id => id.Value, StringComparer.Ordinal)
                        .First().Value)],
                out semanticModel,
                out diagnostics);
        }

        semanticModel = ReplaceElements(
            document,
            document.SemanticModel.Elements.Where(element =>
                !removedIds.Contains(element.Id)));
        diagnostics = [];
        return true;
    }

    private static bool CompleteUpdate(
        DocumentSnapshot document,
        SemanticElementSnapshot existing,
        SemanticElementSnapshot replacement,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        if (replacement.Equals(existing))
        {
            return Fail(
                [Error(
                    BpmnCommandDiagnosticCodes.OrganizationalSemanticUnchanged,
                    $"BPMN Organizational element '{existing.Id}' already has the requested state.",
                    existing.Id.Value)],
                out semanticModel,
                out diagnostics);
        }

        semanticModel = ReplaceElements(
            document,
            document.SemanticModel.Elements.Select(element =>
                element.Id == replacement.Id ? replacement : element));
        diagnostics = [];
        return true;
    }

    private static bool TryGetOrganizationalElement(
        DocumentSnapshot document,
        SemanticElementId elementId,
        SemanticTypeId expectedTypeId,
        out SemanticElementSnapshot? element,
        out Diagnostic? diagnostic)
    {
        if (document.SemanticModel.TryGetElement(elementId, out element) &&
            element is not null &&
            element.TypeId == expectedTypeId &&
            element.ContainmentKind == SemanticElementContainmentKind.Document &&
            element.AttachedToElementId is null)
        {
            diagnostic = null;
            return true;
        }

        diagnostic = Error(
            BpmnCommandDiagnosticCodes.OrganizationalSemanticInvalid,
            $"Semantic element '{elementId}' is not one current document-contained BPMN '{expectedTypeId}'.",
            elementId.Value);
        return false;
    }

    private static Diagnostic? ValidateParticipantReferences(
        SemanticElementId collaborationId,
        DocumentScopeId? processScopeId,
        SemanticElementId participantId,
        DocumentSnapshot document)
    {
        if (!document.SemanticModel.TryGetElement(collaborationId, out var collaboration) ||
            collaboration is null ||
            collaboration.TypeId != BpmnSemanticTypes.Collaboration ||
            collaboration.ContainmentKind != SemanticElementContainmentKind.Document)
        {
            return Error(
                BpmnCommandDiagnosticCodes.ParticipantCollaborationInvalid,
                $"BPMN Participant '{participantId}' must reference an existing document-contained Collaboration.",
                participantId.Value);
        }

        if (processScopeId is not null &&
            (!document.SemanticModel.TryGetScope(processScopeId, out _) ||
                !document.SemanticModel.IsTopLevelScope(processScopeId)))
        {
            return Error(
                BpmnCommandDiagnosticCodes.ParticipantProcessScopeInvalid,
                $"BPMN Participant '{participantId}' may reference only Main or an explicit peer top-level Process scope.",
                participantId.Value);
        }

        return null;
    }

    private static Diagnostic? ValidateOptionalText(
        string? name,
        string? description,
        SemanticElementId sourceId)
    {
        if (name is not null && string.IsNullOrWhiteSpace(name))
        {
            return Error(
                BpmnCommandDiagnosticCodes.OrganizationalSemanticInvalid,
                "A supplied BPMN Organizational Name must contain non-whitespace text.",
                sourceId.Value);
        }

        return description is not null && string.IsNullOrWhiteSpace(description)
            ? Error(
                BpmnCommandDiagnosticCodes.OrganizationalSemanticInvalid,
                "A supplied BPMN Organizational Description must contain non-whitespace text.",
                sourceId.Value)
            : null;
    }

    private static SemanticElementSnapshot ReplaceProperties(
        SemanticElementSnapshot existing,
        string? name,
        string? description,
        SemanticElementId? collaborationId,
        DocumentScopeId? processScopeId)
    {
        var properties = existing.Properties.Where(entry =>
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.Name) &&
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.Description) &&
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.CollaborationId) &&
            !StringComparer.Ordinal.Equals(entry.Key, BpmnSemanticProperties.ProcessScopeId))
            .ToList();
        AddOptionalText(properties, BpmnSemanticProperties.Name, name);
        AddOptionalText(properties, BpmnSemanticProperties.Description, description);
        if (collaborationId is not null)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.CollaborationId,
                PropertyValue.FromText(collaborationId.Value)));
        }

        if (processScopeId is not null)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                BpmnSemanticProperties.ProcessScopeId,
                PropertyValue.FromText(processScopeId.Value)));
        }

        return new SemanticElementSnapshot(
            existing.Id,
            existing.TypeId,
            properties,
            existing.AttachedToElementId,
            existing.ContainmentKind);
    }

    private static void AddOptionalText(
        List<KeyValuePair<string, PropertyValue>> properties,
        string key,
        string? value)
    {
        if (value is not null)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                key,
                PropertyValue.FromText(value)));
        }
    }

    private static bool SemanticIdentityExists(
        DocumentSnapshot document,
        SemanticElementId semanticElementId) =>
        document.SemanticModel.TryGetElement(semanticElementId, out _) ||
        document.SemanticModel.TryGetRelationship(semanticElementId, out _);

    private static SemanticModelSnapshot ReplaceElements(
        DocumentSnapshot document,
        IEnumerable<SemanticElementSnapshot> elements) =>
        new(
            document.DocumentId,
            document.Revision,
            elements,
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments);

    private static bool Fail(
        ImmutableArray<Diagnostic> failure,
        out SemanticModelSnapshot? semanticModel,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        semanticModel = null;
        diagnostics = failure;
        return false;
    }

    private static Diagnostic Error(string code, string message, string source) =>
        BpmnDiagnostics.Error(code, message, source);
}
