using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnElementCreationCommandHandler<TCommand> : ICommandHandler
    where TCommand : BpmnElementCreationCommand
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not TCommand creation)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN element creation request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        var element = CreateElement(creation);
        var visual = new VisualStateSnapshot(
            creation.VisualStateId,
            creation.ElementId,
            creation.Position,
            creation.Size,
            creation.PlacementMode);
        var targetScopeId = creation.TargetScopeId ?? document.SemanticModel.RootScopeId;
        SemanticElementScopeMembershipSnapshot? membership =
            targetScopeId == document.SemanticModel.RootScopeId
                ? null
                : new SemanticElementScopeMembershipSnapshot(
                    creation.ElementId,
                    targetScopeId);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Add(document, element, visual, membership),
            pipelineInvalidation: CommandPipelineInvalidation.Resolve(creation),
            nodeGeometryImpact: creation.NodeGeometryImpact));
    }

    private static SemanticElementSnapshot CreateElement(TCommand creation) => creation switch
    {
        CreateBpmnStartEventCommand start => BpmnSemanticFactory.CreateStartEvent(
            start.ElementId, start.Name, start.Description),
        CreateBpmnTaskCommand task => BpmnSemanticFactory.CreateTask(
            task.ElementId,
            task.TaskTypeId,
            task.Code,
            task.Name,
            task.ElementNumber,
            task.Description),
        CreateBpmnExclusiveGatewayCommand gateway =>
            BpmnSemanticFactory.CreateExclusiveGateway(
                gateway.ElementId,
                gateway.Code,
                gateway.Name,
                gateway.Description),
        CreateBpmnParallelGatewayCommand gateway =>
            BpmnSemanticFactory.CreateParallelGateway(
                gateway.ElementId,
                gateway.Code,
                gateway.Name,
                gateway.Description),
        CreateBpmnInclusiveGatewayCommand gateway =>
            BpmnSemanticFactory.CreateInclusiveGateway(
                gateway.ElementId,
                gateway.Code,
                gateway.Name,
                gateway.Description),
        CreateBpmnEventBasedGatewayCommand gateway =>
            BpmnSemanticFactory.CreateEventBasedGateway(
                gateway.ElementId,
                gateway.Code,
                gateway.Name,
                gateway.Description),
        CreateBpmnMessageCatchEventCommand message =>
            BpmnSemanticFactory.CreateMessageCatchEvent(
                message.ElementId,
                message.Name,
                message.Description),
        CreateBpmnMessageThrowEventCommand message =>
            BpmnSemanticFactory.CreateMessageThrowEvent(
                message.ElementId,
                message.Name,
                message.Description),
        CreateBpmnTimerCatchEventCommand timer =>
            BpmnSemanticFactory.CreateTimerCatchEvent(
                timer.ElementId,
                timer.Name,
                timer.TimerDefinition,
                timer.Description),
        CreateBpmnSignalCatchEventCommand signal =>
            BpmnSemanticFactory.CreateSignalCatchEvent(
                signal.ElementId,
                signal.Name,
                signal.Description),
        CreateBpmnSignalThrowEventCommand signal =>
            BpmnSemanticFactory.CreateSignalThrowEvent(
                signal.ElementId,
                signal.Name,
                signal.Description),
        CreateBpmnEndEventCommand end => BpmnSemanticFactory.CreateEndEvent(
            end.ElementId, end.Name, end.Description),
        _ => throw new InvalidOperationException("Unsupported BPMN element creation command."),
    };
}

internal sealed class BpmnSubProcessCreationCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not CreateBpmnSubProcessCommand creation)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN SubProcess creation request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        var diagnostics = BpmnSubProcessCreationValidation.Validate(creation, document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var element = BpmnSemanticFactory.CreateSubProcess(
            creation.ElementId,
            creation.Code,
            creation.Name,
            creation.Description);
        var visual = new VisualStateSnapshot(
            creation.VisualStateId,
            creation.ElementId,
            creation.Position,
            creation.Size,
            creation.PlacementMode);
        var childScope = new DocumentScopeSnapshot(
            creation.ChildScopeId,
            creation.ParentScopeId,
            creation.ElementId);
        SemanticElementScopeMembershipSnapshot? membership =
            creation.ParentScopeId == document.SemanticModel.RootScopeId
                ? null
                : new SemanticElementScopeMembershipSnapshot(
                    creation.ElementId,
                    creation.ParentScopeId);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.AddSubProcess(
                document,
                element,
                visual,
                childScope,
                membership),
            pipelineInvalidation: CommandPipelineInvalidation.Resolve(creation),
            nodeGeometryImpact: creation.NodeGeometryImpact));
    }
}

internal abstract class BpmnBoundaryEventCreationCommandHandler<TCommand> : ICommandHandler
    where TCommand : class, ICommand
{
    private readonly SemanticTypeId _semanticTypeId;

    protected BpmnBoundaryEventCreationCommandHandler(SemanticTypeId semanticTypeId)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        if (!BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            throw new ArgumentException(
                $"Semantic type '{semanticTypeId}' is not a supported BPMN Boundary Event.",
                nameof(semanticTypeId));
        }

        _semanticTypeId = semanticTypeId;
    }

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not TCommand ||
            !BpmnBoundaryEventCreationCommandData.TryResolve(command, out var creation) ||
            creation is null ||
            creation.SemanticTypeId != _semanticTypeId)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    $"The BPMN {BpmnBoundaryEventSemanticTypes.DisplayName(_semanticTypeId)} creation request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        var diagnostics = BpmnBoundaryEventCreationValidation.Validate(
            creation,
            document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var owner = document.SemanticModel.Elements.Single(element =>
            element.Id == creation.AttachedToActivityId);
        var attachedBounds = creation.BoundaryAttachment.ResolveBounds(
            creation.EffectiveOwnerBounds,
            BpmnNodeLogicalSizePolicy.EventSize);
        var element = BpmnSemanticFactory.CreateBoundaryEvent(
            creation.ElementId,
            creation.SemanticTypeId,
            creation.AttachedToActivityId,
            creation.Name,
            creation.TimerDefinition,
            creation.CancelActivity,
            creation.Description);
        var visual = new VisualStateSnapshot(
            creation.VisualStateId,
            creation.ElementId,
            attachedBounds.TopLeft,
            BpmnNodeLogicalSizePolicy.EventSize,
            VisualPlacementMode.Manual,
            boundaryAttachment: creation.BoundaryAttachment);
        var ownerScopeId = document.SemanticModel.GetScope(owner.Id).Id;
        SemanticElementScopeMembershipSnapshot? membership =
            ownerScopeId == document.SemanticModel.RootScopeId
                ? null
                : new SemanticElementScopeMembershipSnapshot(
                    creation.ElementId,
                    ownerScopeId);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Add(document, element, visual, membership)));
    }
}

internal sealed class BpmnMessageBoundaryEventCreationCommandHandler :
    BpmnBoundaryEventCreationCommandHandler<CreateBpmnMessageBoundaryEventCommand>
{
    internal BpmnMessageBoundaryEventCreationCommandHandler()
        : base(BpmnSemanticTypes.MessageBoundaryEvent)
    {
    }
}

internal sealed class BpmnTimerBoundaryEventCreationCommandHandler :
    BpmnBoundaryEventCreationCommandHandler<CreateBpmnTimerBoundaryEventCommand>
{
    internal BpmnTimerBoundaryEventCreationCommandHandler()
        : base(BpmnSemanticTypes.TimerBoundaryEvent)
    {
    }
}

internal sealed class BpmnSignalBoundaryEventCreationCommandHandler :
    BpmnBoundaryEventCreationCommandHandler<CreateBpmnSignalBoundaryEventCommand>
{
    internal BpmnSignalBoundaryEventCreationCommandHandler()
        : base(BpmnSemanticTypes.SignalBoundaryEvent)
    {
    }
}

internal sealed class BpmnSequenceFlowCreationCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not CreateBpmnSequenceFlowCommand flow)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN Sequence Flow creation request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        var diagnostics = BpmnSequenceFlowCreationValidation.Validate(flow, document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var relationship = BpmnSemanticFactory.CreateSequenceFlow(
            flow.RelationshipId,
            flow.SourceId,
            flow.TargetId,
            flow.Name,
            flow.Description);
        var visual = new VisualStateSnapshot(
            flow.VisualStateId,
            flow.RelationshipId,
            new PointD(0d, 0d),
            new SizeD(0d, 0d),
            VisualPlacementMode.Manual,
            flow.Route,
            sourceAnchorId: flow.SourceAnchorId,
            targetAnchorId: flow.TargetAnchorId);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Add(document, relationship, visual)));
    }
}

internal sealed class BpmnSequenceFlowWithTargetAnchorCreationCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not CreateBpmnSequenceFlowWithTargetAnchorCommand creation)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The atomic BPMN Sequence Flow creation request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        var diagnostics = BpmnSequenceFlowWithTargetAnchorCreationValidation.Validate(
            creation,
            document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var targetVisual = document.VisualModel.VisualStates.Single(visual =>
            visual.Id == creation.TargetVisualStateId);
        var targetReplacement = ConnectorAnchorInsertion.Insert(
            targetVisual,
            creation.TargetAnchorId,
            creation.TargetSide,
            ConnectorAnchorRole.Target,
            creation.TargetInsertionIndex);
        var relationship = BpmnSemanticFactory.CreateSequenceFlow(
            creation.RelationshipId,
            creation.SourceId,
            creation.TargetId,
            creation.Name,
            creation.Description);
        var connector = new VisualStateSnapshot(
            creation.VisualStateId,
            creation.RelationshipId,
            new PointD(0d, 0d),
            new SizeD(0d, 0d),
            VisualPlacementMode.Manual,
            creation.Route,
            sourceAnchorId: creation.SourceAnchorId,
            targetAnchorId: creation.TargetAnchorId);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Add(
                document,
                relationship,
                targetReplacement,
                connector)));
    }
}

internal static class BpmnDocumentReplacement
{
    internal static DocumentSnapshot Add(
        DocumentSnapshot document,
        SemanticElementSnapshot element,
        VisualStateSnapshot visual,
        SemanticElementScopeMembershipSnapshot? membership = null) =>
        Create(
            document,
            document.SemanticModel.Elements.Append(element),
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes,
            membership is null
                ? document.SemanticModel.ScopeMemberships
                : document.SemanticModel.ScopeMemberships.Append(membership),
            document.VisualModel.VisualStates.Append(visual));

    internal static DocumentSnapshot Add(
        DocumentSnapshot document,
        SemanticRelationshipSnapshot relationship,
        VisualStateSnapshot visual) =>
        Create(
            document,
            document.SemanticModel.Elements,
            document.SemanticModel.Relationships.Append(relationship),
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.VisualModel.VisualStates.Append(visual));

    internal static DocumentSnapshot AddSubProcess(
        DocumentSnapshot document,
        SemanticElementSnapshot element,
        VisualStateSnapshot visual,
        DocumentScopeSnapshot childScope,
        SemanticElementScopeMembershipSnapshot? membership) =>
        Create(
            document,
            document.SemanticModel.Elements.Append(element),
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes.Append(childScope),
            membership is null
                ? document.SemanticModel.ScopeMemberships
                : document.SemanticModel.ScopeMemberships.Append(membership),
            document.VisualModel.VisualStates.Append(visual));

    internal static DocumentSnapshot Add(
        DocumentSnapshot document,
        SemanticRelationshipSnapshot relationship,
        VisualStateSnapshot replacement,
        VisualStateSnapshot addition) =>
        Create(
            document,
            document.SemanticModel.Elements,
            document.SemanticModel.Relationships.Append(relationship),
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.VisualModel.VisualStates
                .Select(visual => visual.Id == replacement.Id ? replacement : visual)
                .Append(addition));

    internal static DocumentSnapshot ReplaceVisual(
        DocumentSnapshot document,
        VisualStateSnapshot replacement) =>
        Create(
            document,
            document.SemanticModel.Elements,
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.VisualModel.VisualStates.Select(visual =>
                visual.Id == replacement.Id ? replacement : visual));

    internal static DocumentSnapshot Replace(
        DocumentSnapshot document,
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel) =>
        new(
            new SemanticModelSnapshot(
                document.DocumentId,
                document.Revision,
                semanticModel.Elements,
                semanticModel.Relationships,
                semanticModel.NestedScopes,
                semanticModel.ScopeMemberships,
                semanticModel.ModelProfiles,
                semanticModel.ProfileAssignments),
            new VisualModelSnapshot(
                document.DocumentId,
                document.Revision,
                visualModel.VisualStates,
                visualModel.ProfileElementPresentations),
            document.Metadata,
            document.Publication);

    private static DocumentSnapshot Create(
        DocumentSnapshot document,
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> relationships,
        IEnumerable<DocumentScopeSnapshot> nestedScopes,
        IEnumerable<SemanticElementScopeMembershipSnapshot> scopeMemberships,
        IEnumerable<VisualStateSnapshot> visuals) =>
        new(
            new SemanticModelSnapshot(
                document.DocumentId,
                document.Revision,
                elements,
                relationships,
                nestedScopes,
                scopeMemberships,
                document.SemanticModel.ModelProfiles,
                document.SemanticModel.ProfileAssignments),
            new VisualModelSnapshot(
                document.DocumentId,
                document.Revision,
                visuals,
                document.VisualModel.ProfileElementPresentations),
            document.Metadata,
            document.Publication);
}
