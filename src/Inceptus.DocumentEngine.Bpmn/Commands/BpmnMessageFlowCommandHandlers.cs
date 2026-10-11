using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnMessageFlowCreationCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not CreateBpmnMessageFlowCommand creation) return BpmnMessageFlowCommandSupport.Invalid(command);
        var diagnostics = BpmnMessageFlowSemantics.ValidatePresentation(document, creation.SourceId, creation.TargetId).ToBuilder();
        if (document.SemanticModel.TryGetElement(creation.RelationshipId, out _) ||
            document.SemanticModel.TryGetRelationship(creation.RelationshipId, out _) ||
            document.VisualModel.TryGetVisualState(creation.VisualStateId, out _))
            diagnostics.Add(BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.InvalidCommand,
                "Message Flow creation requires unused semantic and visual identities.", creation.RelationshipId));
        if (creation.Route.Length == 1 || creation.Name is not null && string.IsNullOrWhiteSpace(creation.Name) ||
            creation.Description is not null && string.IsNullOrWhiteSpace(creation.Description))
            diagnostics.Add(BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.InvalidCommand,
                "Message Flow optional text must be nonblank and a supplied route must contain both endpoints.", creation.RelationshipId));
        diagnostics.AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(document, creation.SourceId,
            creation.SourceAnchorId, ConnectorAnchorRole.Source));
        diagnostics.AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(document, creation.TargetId,
            creation.TargetAnchorId, ConnectorAnchorRole.Target));
        return diagnostics.ToImmutable();
    }

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        if (!diagnostics.IsEmpty) return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        var creation = (CreateBpmnMessageFlowCommand)command;
        var relationship = BpmnSemanticFactory.CreateMessageFlow(creation.RelationshipId, creation.SourceId,
            creation.TargetId, creation.Name, creation.Description);
        var visual = new VisualStateSnapshot(creation.VisualStateId, creation.RelationshipId, default, default,
            VisualPlacementMode.Manual, sourceAnchorId: creation.SourceAnchorId, targetAnchorId: creation.TargetAnchorId);
        return ValueTask.FromResult(BpmnSequenceFlowCreationCommandHandler.PrepareRoutingCreation(
            BpmnDocumentReplacement.Add(document, relationship, visual), creation.VisualStateId, creation.RoutingType, creation.Route));
    }
}

internal sealed class BpmnMessageFlowWithTargetAnchorCreationCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not CreateBpmnMessageFlowWithTargetAnchorCommand creation) return BpmnMessageFlowCommandSupport.Invalid(command);
        if (!document.VisualModel.TryGetVisualState(creation.TargetVisualStateId, out var visual) || visual is null ||
            visual.SemanticElementId != creation.TargetId ||
            !document.SemanticModel.TryGetElement(creation.TargetId, out var element) || element is null ||
            !ElementConnectorAnchorPolicyEvaluator.CanAdd(BpmnConnectorAnchorPolicies.Provider.Resolve(element.TypeId),
                visual, creation.TargetSide, ConnectorAnchorRole.Target) ||
            creation.TargetInsertionIndex > visual.ConnectorAnchors.Count(anchor => anchor.Side == creation.TargetSide) ||
            ConnectorAnchorReferenceIdentity.IsPredefinedReference(creation.TargetAnchorId) ||
            document.VisualModel.VisualStates.SelectMany(candidate => candidate.ConnectorAnchors).Any(anchor => anchor.Id == creation.TargetAnchorId))
            return [BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.AnchorInvalid,
                "The proposed Message Flow target anchor is not a unique, addable anchor on the current target edge.", creation.TargetId)];
        var interim = BpmnDocumentReplacement.ReplaceVisual(document, Insert(creation, visual));
        return new BpmnMessageFlowCreationCommandHandler().Validate(AsCreation(creation), interim);
    }

    public async ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        if (!diagnostics.IsEmpty) return CommandHandlerResult.Failure(diagnostics);
        var creation = (CreateBpmnMessageFlowWithTargetAnchorCommand)command;
        document.VisualModel.TryGetVisualState(creation.TargetVisualStateId, out var visual);
        return await new BpmnMessageFlowCreationCommandHandler().HandleAsync(AsCreation(creation),
            BpmnDocumentReplacement.ReplaceVisual(document, Insert(creation, visual!)), cancellationToken).ConfigureAwait(false);
    }

    private static VisualStateSnapshot Insert(CreateBpmnMessageFlowWithTargetAnchorCommand creation, VisualStateSnapshot visual) =>
        ConnectorAnchorInsertion.Insert(visual, creation.TargetAnchorId, creation.TargetSide,
            ConnectorAnchorRole.Target, creation.TargetInsertionIndex);

    private static CreateBpmnMessageFlowCommand AsCreation(CreateBpmnMessageFlowWithTargetAnchorCommand creation) =>
        new(creation.TargetDocumentId, creation.ExpectedRevision, creation.RelationshipId, creation.VisualStateId,
            creation.SourceId, creation.TargetId, creation.SourceAnchorId, creation.TargetAnchorId, creation.RoutingType,
            creation.Route, creation.Name, creation.Description);
}

internal sealed class BpmnMessageFlowEndpointReconnectionValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
        command is ReconnectBpmnMessageFlowEndpointCommand reconnect
            ? BpmnMessageFlowEndpointReconnectionValidation.Validate(reconnect, document) : BpmnMessageFlowCommandSupport.Invalid(command);
}

internal static class BpmnMessageFlowEndpointReconnectionValidation
{
    internal static ImmutableArray<Diagnostic> Validate(ReconnectBpmnMessageFlowEndpointCommand command, DocumentSnapshot document)
    {
        if (!BpmnMessageFlowCommandSupport.TryGet(document, command.RelationshipId, command.ConnectorVisualStateId,
                out var relationship, out var visual)) return BpmnMessageFlowCommandSupport.Invalid(command);
        var source = command.EndpointKind == ConnectorEndpointKind.Source;
        if ((source ? relationship!.SourceId : relationship!.TargetId) != command.ExpectedCurrentSemanticElementId ||
            (source ? visual!.SourceAnchorId : visual!.TargetAnchorId) != command.ExpectedCurrentAnchorId ||
            command.ExpectedCurrentSemanticElementId == command.NewSemanticElementId && command.ExpectedCurrentAnchorId == command.NewAnchorId)
            return [BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.StateMismatch,
                "The Message Flow endpoint no longer matches the expected state or is unchanged.", command.RelationshipId)];
        var diagnostics = BpmnMessageFlowSemantics.ValidatePresentation(document,
            source ? command.NewSemanticElementId : relationship.SourceId,
            source ? relationship.TargetId : command.NewSemanticElementId).ToBuilder();
        diagnostics.AddRange(BpmnMessageFlowCommandSupport.ValidateAnchor(document, command.NewSemanticElementId,
            command.NewAnchorId, source ? ConnectorAnchorRole.Source : ConnectorAnchorRole.Target,
            command.ConnectorVisualStateId, command.EndpointKind));
        return diagnostics.ToImmutable();
    }
}

internal sealed class BpmnMessageFlowDeletionCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        if (command is not DeleteBpmnMessageFlowCommand deletion ||
            !BpmnMessageFlowCommandSupport.TryGet(document, deletion.RelationshipId, deletion.ConnectorVisualStateId,
                out var relationship, out var visual) ||
            relationship!.SourceId != deletion.ExpectedSourceId || relationship.TargetId != deletion.ExpectedTargetId ||
            visual!.SourceAnchorId != deletion.ExpectedSourceAnchorId || visual.TargetAnchorId != deletion.ExpectedTargetAnchorId)
            return BpmnMessageFlowCommandSupport.Invalid(command);
        return [];
    }

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        if (!diagnostics.IsEmpty) return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        var deletion = (DeleteBpmnMessageFlowCommand)command;
        return ValueTask.FromResult(CommandHandlerResult.Success(BpmnMessageFlowCommandSupport.Replace(document,
            document.SemanticModel.Relationships.Where(relationship => relationship.Id != deletion.RelationshipId),
            document.VisualModel.VisualStates.Where(visual => visual.Id != deletion.ConnectorVisualStateId),
            BpmnMessageFlowCommandSupport.WithoutRoutingRecord(document, deletion.ConnectorVisualStateId)),
            pipelineInvalidation: CommandPipelineInvalidation.ConnectorOnly, nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
    }
}

internal static class BpmnMessageFlowCommandSupport
{
    internal static bool TryGet(DocumentSnapshot document, SemanticElementId relationshipId, VisualStateId visualId,
        out SemanticRelationshipSnapshot? relationship, out VisualStateSnapshot? visual)
    {
        document.SemanticModel.TryGetRelationship(relationshipId, out relationship);
        document.VisualModel.TryGetVisualState(visualId, out visual);
        return relationship?.TypeId == BpmnSemanticTypes.MessageFlow && visual?.SemanticElementId == relationshipId &&
            document.VisualModel.VisualStates.Count(candidate => candidate.SemanticElementId == relationshipId) == 1;
    }

    internal static ImmutableArray<Diagnostic> Invalid(ICommand command) =>
        [new(BpmnMessageFlowSemantics.InvalidCommand, DiagnosticSeverity.Error,
            "The Message Flow command does not match the current semantic, visual, or endpoint state.", command.TypeId.Value)];

    internal static ImmutableArray<Diagnostic> ValidateAnchor(DocumentSnapshot document, SemanticElementId endpointId,
        ConnectorAnchorId anchorId, ConnectorAnchorRole role, VisualStateId? existingConnector = null,
        ConnectorEndpointKind endpointKind = ConnectorEndpointKind.Source)
    {
        var visuals = document.VisualModel.VisualStates.Where(visual => visual.SemanticElementId == endpointId).ToArray();
        if (visuals.Length != 1 || !document.SemanticModel.TryGetElement(endpointId, out var element) || element is null ||
            document.VisualModel.VisualStates.SelectMany(visual => visual.ConnectorAnchors).Count(anchor => anchor.Id == anchorId) != 1 ||
            visuals[0].ConnectorAnchors.SingleOrDefault(anchor => anchor.Id == anchorId)?.Role != role)
            return InvalidAnchor(endpointId);
        try
        {
            if (!ElementConnectorAnchorResolver.Resolve(visuals[0], element.TypeId, BpmnConnectorAnchorPolicies.Provider)
                    .Any(anchor => anchor.Id == anchorId && anchor.Allows(role))) return InvalidAnchor(endpointId);
        }
        catch (InvalidOperationException) { return InvalidAnchor(endpointId); }
        if (existingConnector is null ? ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchorId)
            : ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(document.VisualModel, anchorId, existingConnector, endpointKind))
            return [new(CommandExecutionDiagnosticCodes.ConnectorAnchorInUse, DiagnosticSeverity.Error,
                "The Message Flow anchor is already occupied by another connector endpoint.", anchorId.Value)];
        return [];
    }

    private static ImmutableArray<Diagnostic> InvalidAnchor(SemanticElementId endpointId) =>
        [BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.AnchorInvalid,
            "A Message Flow endpoint requires one persistent anchor on its exact visual owner with the required direction.", endpointId)];

    internal static IEnumerable<DocumentRoutingSnapshot> WithoutRoutingRecord(DocumentSnapshot document, VisualStateId visualId) =>
        document.VisualModel.DocumentRoutingStates.Select(state => new DocumentRoutingSnapshot(state.OwnerId,
            state.Connectors.Where(record => record.VisualStateId != visualId)));

    internal static DocumentSnapshot Replace(DocumentSnapshot document, IEnumerable<SemanticRelationshipSnapshot> relationships,
        IEnumerable<VisualStateSnapshot> visuals, IEnumerable<DocumentRoutingSnapshot>? routing = null) =>
        new(new SemanticModelSnapshot(document.DocumentId, document.Revision, document.SemanticModel.Elements, relationships,
                document.SemanticModel.NestedScopes, document.SemanticModel.ScopeMemberships,
                document.SemanticModel.ModelProfiles, document.SemanticModel.ProfileAssignments),
            new VisualModelSnapshot(document.DocumentId, document.Revision, visuals, document.VisualModel.ProfileElementPresentations,
                document.VisualModel.RoutingScopes, routing ?? document.VisualModel.DocumentRoutingStates), document.Metadata, document.Publication);
}
