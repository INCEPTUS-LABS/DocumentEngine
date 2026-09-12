using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnCommandEnvelopeValidator<TCommand> : ICommandEnvelopeValidator
    where TCommand : class, ICommand
{
    public ImmutableArray<Diagnostic> Validate(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is TCommand
            ? []
            : [BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                $"Command type '{command.TypeId}' does not use the registered BPMN request shape.",
                command.TypeId.Value)];
    }
}

internal sealed class BpmnElementCreationValidator<TCommand> : ICommandValidator
    where TCommand : BpmnElementCreationCommand
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not TCommand creation)
        {
            return [BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "The BPMN element creation request has an invalid shape.",
                command.TypeId.Value)];
        }

        var diagnostics = new List<Diagnostic>();
        if (document.SemanticModel.TryGetElement(creation.ElementId, out _) ||
            document.SemanticModel.TryGetRelationship(creation.ElementId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateSemanticId,
                $"Semantic identity '{creation.ElementId}' already exists.",
                creation.ElementId.Value));
        }

        if (document.VisualModel.TryGetVisualState(creation.VisualStateId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateVisualId,
                $"Visual identity '{creation.VisualStateId}' already exists.",
                creation.VisualStateId.Value));
        }

        if (creation.TargetScopeId is { } targetScopeId &&
            targetScopeId != document.SemanticModel.RootScopeId &&
            !document.SemanticModel.NestedScopes.Any(scope => scope.Id == targetScopeId))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.ElementTargetScopeInvalid,
                $"Target scope '{targetScopeId}' does not exist in the current Document.",
                targetScopeId.Value));
        }

        if (creation is CreateBpmnTaskCommand task &&
            (!BpmnTaskSemanticTypes.IsTask(task.TaskTypeId) ||
                string.IsNullOrWhiteSpace(task.Code) ||
                string.IsNullOrWhiteSpace(task.Name)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidTask,
                "A supported BPMN Task requires nonblank text Code and Name properties.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnExclusiveGatewayCommand gateway &&
            (string.IsNullOrWhiteSpace(gateway.Code) ||
                string.IsNullOrWhiteSpace(gateway.Name)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidExclusiveGateway,
                "A BPMN Exclusive Gateway requires nonblank text Code and Name properties.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnParallelGatewayCommand parallelGateway &&
            (string.IsNullOrWhiteSpace(parallelGateway.Code) ||
                string.IsNullOrWhiteSpace(parallelGateway.Name)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidParallelGateway,
                "A BPMN Parallel Gateway requires nonblank text Code and Name properties.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnInclusiveGatewayCommand inclusiveGateway &&
            (string.IsNullOrWhiteSpace(inclusiveGateway.Code) ||
                string.IsNullOrWhiteSpace(inclusiveGateway.Name)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidInclusiveGateway,
                "A BPMN Inclusive Gateway requires nonblank text Code and Name properties.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnEventBasedGatewayCommand eventBasedGateway &&
            (string.IsNullOrWhiteSpace(eventBasedGateway.Code) ||
                string.IsNullOrWhiteSpace(eventBasedGateway.Name)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidEventBasedGateway,
                "A BPMN Event-Based Gateway requires nonblank text Code and Name properties.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnMessageCatchEventCommand messageCatchEvent &&
            string.IsNullOrWhiteSpace(messageCatchEvent.Name))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCatchEvent,
                "A BPMN Message Catch Event requires a nonblank text Name property.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnTimerCatchEventCommand timerCatchEvent &&
            string.IsNullOrWhiteSpace(timerCatchEvent.Name))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCatchEvent,
                "A BPMN Timer Catch Event requires a nonblank text Name property.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnMessageThrowEventCommand messageThrowEvent &&
            string.IsNullOrWhiteSpace(messageThrowEvent.Name))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidIntermediateEvent,
                "A BPMN Message Throw Event requires a nonblank text Name property.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnSignalCatchEventCommand signalCatchEvent &&
            string.IsNullOrWhiteSpace(signalCatchEvent.Name))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidIntermediateEvent,
                "A BPMN Signal Catch Event requires a nonblank text Name property.",
                creation.ElementId.Value));
        }

        if (creation is CreateBpmnSignalThrowEventCommand signalThrowEvent &&
            string.IsNullOrWhiteSpace(signalThrowEvent.Name))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidIntermediateEvent,
                "A BPMN Signal Throw Event requires a nonblank text Name property.",
                creation.ElementId.Value));
        }

        ValidateOptionalText(creation, diagnostics);
        return [.. diagnostics];
    }

    private static void ValidateOptionalText(
        BpmnElementCreationCommand creation,
        List<Diagnostic> diagnostics)
    {
        var values = creation switch
        {
            CreateBpmnStartEventCommand start => new[] { start.Name, start.Description },
            CreateBpmnTaskCommand task => new[] { task.Description },
            CreateBpmnExclusiveGatewayCommand gateway => new[] { gateway.Description },
            CreateBpmnParallelGatewayCommand gateway => new[] { gateway.Description },
            CreateBpmnInclusiveGatewayCommand gateway => new[] { gateway.Description },
            CreateBpmnEventBasedGatewayCommand gateway => new[] { gateway.Description },
            CreateBpmnMessageCatchEventCommand message => new[] { message.Description },
            CreateBpmnMessageThrowEventCommand message => new[] { message.Description },
            CreateBpmnTimerCatchEventCommand timer =>
                new[] { timer.TimerDefinition, timer.Description },
            CreateBpmnSignalCatchEventCommand signal => new[] { signal.Description },
            CreateBpmnSignalThrowEventCommand signal => new[] { signal.Description },
            CreateBpmnEndEventCommand end => new[] { end.Name, end.Description },
            _ => [],
        };
        if (values.Any(static value => value is not null && string.IsNullOrWhiteSpace(value)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "Optional BPMN text properties must be nonblank when supplied.",
                creation.ElementId.Value));
        }
    }
}

internal sealed class BpmnSubProcessCreationValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        return command is CreateBpmnSubProcessCommand creation
            ? BpmnSubProcessCreationValidation.Validate(creation, document)
            :
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN SubProcess creation request has an invalid shape.",
                    command.TypeId.Value),
            ];
    }
}

internal abstract class BpmnBoundaryEventCreationValidator<TCommand> : ICommandValidator
    where TCommand : class, ICommand
{
    private readonly SemanticTypeId _semanticTypeId;

    protected BpmnBoundaryEventCreationValidator(SemanticTypeId semanticTypeId)
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

    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        return command is TCommand &&
            BpmnBoundaryEventCreationCommandData.TryResolve(command, out var creation) &&
            creation is not null &&
            creation.SemanticTypeId == _semanticTypeId
            ? BpmnBoundaryEventCreationValidation.Validate(creation, document)
            :
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    $"The BPMN {BpmnBoundaryEventSemanticTypes.DisplayName(_semanticTypeId)} creation request has an invalid shape.",
                    command.TypeId.Value),
            ];
    }
}

internal sealed class BpmnMessageBoundaryEventCreationValidator :
    BpmnBoundaryEventCreationValidator<CreateBpmnMessageBoundaryEventCommand>
{
    internal BpmnMessageBoundaryEventCreationValidator()
        : base(BpmnSemanticTypes.MessageBoundaryEvent)
    {
    }
}

internal sealed class BpmnTimerBoundaryEventCreationValidator :
    BpmnBoundaryEventCreationValidator<CreateBpmnTimerBoundaryEventCommand>
{
    internal BpmnTimerBoundaryEventCreationValidator()
        : base(BpmnSemanticTypes.TimerBoundaryEvent)
    {
    }
}

internal sealed class BpmnSignalBoundaryEventCreationValidator :
    BpmnBoundaryEventCreationValidator<CreateBpmnSignalBoundaryEventCommand>
{
    internal BpmnSignalBoundaryEventCreationValidator()
        : base(BpmnSemanticTypes.SignalBoundaryEvent)
    {
    }
}

internal static class BpmnBoundaryEventCreationValidation
{
    internal static ImmutableArray<Diagnostic> Validate(
        BpmnBoundaryEventCreationCommandData creation,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<Diagnostic>();
        if (document.SemanticModel.TryGetElement(creation.ElementId, out _) ||
            document.SemanticModel.TryGetRelationship(creation.ElementId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateSemanticId,
                $"Semantic identity '{creation.ElementId}' already exists.",
                creation.ElementId.Value));
        }

        if (document.VisualModel.TryGetVisualState(creation.VisualStateId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateVisualId,
                $"Visual identity '{creation.VisualStateId}' already exists.",
                creation.VisualStateId.Value));
        }

        if (!document.SemanticModel.TryGetElement(
                creation.AttachedToActivityId,
                out var owner) ||
            owner is null ||
            !BpmnActivitySemanticTypes.IsActivity(owner.TypeId) ||
            owner.ContainmentKind != SemanticElementContainmentKind.Scope ||
            !document.SemanticModel.TryGetScope(owner.Id, out var ownerScope) ||
            ownerScope is null)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid,
                $"A BPMN {BpmnBoundaryEventSemanticTypes.DisplayName(creation.SemanticTypeId)} must attach to one existing scope-contained BPMN Activity.",
                creation.ElementId.Value));
            ValidateText(creation, diagnostics);
            return [.. diagnostics];
        }

        var ownerScopeId = ownerScope.Id;
        if (creation.TargetScopeId is { } targetScopeId &&
            targetScopeId != ownerScopeId)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentScopeMismatch,
                $"A BPMN {BpmnBoundaryEventSemanticTypes.DisplayName(creation.SemanticTypeId)} must use its attached Activity scope '{ownerScopeId}', not '{targetScopeId}'.",
                creation.ElementId.Value));
        }

        var ownerVisuals = document.VisualModel.VisualStates
            .Where(visual => visual.SemanticElementId == owner.Id)
            .ToArray();
        if (ownerVisuals.Length != 1)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid,
                $"Attached BPMN Activity '{owner.Id}' must have exactly one Visual State.",
                creation.ElementId.Value));
        }
        else
        {
            var ownerVisual = ownerVisuals[0];
            var persistentOwnerBounds = new RectD(
                ownerVisual.Position.X,
                ownerVisual.Position.Y,
                ownerVisual.Size.Width,
                ownerVisual.Size.Height);
            if (ownerVisual.PlacementMode == VisualPlacementMode.Pinned &&
                creation.EffectiveOwnerBounds != persistentOwnerBounds)
            {
                diagnostics.Add(BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid,
                    "The supplied effective Activity bounds must match a pinned Activity's persistent bounds.",
                    creation.ElementId.Value));
            }

            var attachedBounds = creation.BoundaryAttachment.ResolveBounds(
                creation.EffectiveOwnerBounds,
                BpmnNodeLogicalSizePolicy.EventSize);
            if (creation.EffectiveOwnerBounds.IsEmpty ||
                !DocumentGeometryBoundary.Contains(creation.EffectiveOwnerBounds) ||
                !DocumentGeometryBoundary.Contains(attachedBounds))
            {
                diagnostics.Add(BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid,
                    $"The effective Activity bounds and derived {BpmnBoundaryEventSemanticTypes.DisplayName(creation.SemanticTypeId)} bounds must remain inside the non-negative Document boundary.",
                    creation.ElementId.Value));
            }
        }

        ValidateText(creation, diagnostics);
        return [.. diagnostics];
    }

    private static void ValidateText(
        BpmnBoundaryEventCreationCommandData creation,
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(creation.Name) ||
            creation.TimerDefinition is not null &&
                string.IsNullOrWhiteSpace(creation.TimerDefinition) ||
            creation.Description is not null &&
                string.IsNullOrWhiteSpace(creation.Description))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidBoundaryEvent,
                $"A BPMN {BpmnBoundaryEventSemanticTypes.DisplayName(creation.SemanticTypeId)} requires a nonblank Name, and optional text properties must be nonblank when supplied.",
                creation.ElementId.Value));
        }
    }
}

internal static class BpmnSubProcessCreationValidation
{
    internal static ImmutableArray<Diagnostic> Validate(
        CreateBpmnSubProcessCommand creation,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<Diagnostic>();
        if (document.SemanticModel.TryGetElement(creation.ElementId, out _) ||
            document.SemanticModel.TryGetRelationship(creation.ElementId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateSemanticId,
                $"Semantic identity '{creation.ElementId}' already exists.",
                creation.ElementId.Value));
        }

        if (document.VisualModel.TryGetVisualState(creation.VisualStateId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateVisualId,
                $"Visual identity '{creation.VisualStateId}' already exists.",
                creation.VisualStateId.Value));
        }

        if (string.IsNullOrWhiteSpace(creation.Code) ||
            string.IsNullOrWhiteSpace(creation.Name) ||
            creation.Description is not null &&
            string.IsNullOrWhiteSpace(creation.Description))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidSubProcess,
                "A BPMN SubProcess requires nonblank Code and Name properties, and Description must be nonblank when supplied.",
                creation.ElementId.Value));
        }

        if (!IsKnownScope(document, creation.ParentScopeId))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SubProcessParentScopeInvalid,
                $"Parent scope '{creation.ParentScopeId}' does not exist in the current Document.",
                creation.ParentScopeId.Value));
        }

        var childScopeCollides =
            creation.ChildScopeId == document.SemanticModel.RootScopeId ||
            document.SemanticModel.NestedScopes.Any(scope =>
                scope.Id == creation.ChildScopeId);
        var ownerAlreadyHasChild = document.SemanticModel.NestedScopes.Any(scope =>
            scope.OwnerSemanticElementId == creation.ElementId);
        if (childScopeCollides || ownerAlreadyHasChild)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SubProcessChildScopeInvalid,
                childScopeCollides
                    ? $"Child scope identity '{creation.ChildScopeId}' collides with an existing or canonical root scope identity."
                    : $"Semantic element '{creation.ElementId}' already owns a child scope.",
                creation.ChildScopeId.Value));
        }

        if (creation.Size.Width <= 0d ||
            creation.Size.Height <= 0d ||
            !DocumentGeometryBoundary.Contains(creation.Position))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                $"BPMN SubProcess Visual State '{creation.VisualStateId}' contains invalid persistent geometry.",
                creation.VisualStateId.Value));
        }

        return [.. diagnostics];
    }

    private static bool IsKnownScope(
        DocumentSnapshot document,
        DocumentScopeId scopeId) =>
        scopeId == document.SemanticModel.RootScopeId ||
        document.SemanticModel.NestedScopes.Any(scope => scope.Id == scopeId);
}

internal sealed class BpmnSequenceFlowCreationValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not CreateBpmnSequenceFlowCommand flow)
        {
            return [BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "The BPMN Sequence Flow creation request has an invalid shape.",
                command.TypeId.Value)];
        }

        return BpmnSequenceFlowCreationValidation.Validate(flow, document);
    }
}

internal static class BpmnSequenceFlowCreationValidation
{
    internal static ImmutableArray<Diagnostic> Validate(
        CreateBpmnSequenceFlowCommand flow,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<Diagnostic>();
        if (document.SemanticModel.TryGetElement(flow.RelationshipId, out _) ||
            document.SemanticModel.TryGetRelationship(flow.RelationshipId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateSemanticId,
                $"Semantic identity '{flow.RelationshipId}' already exists.",
                flow.RelationshipId.Value));
        }

        if (document.VisualModel.TryGetVisualState(flow.VisualStateId, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.DuplicateVisualId,
                $"Visual identity '{flow.VisualStateId}' already exists.",
                flow.VisualStateId.Value));
        }

        diagnostics.AddRange(ValidateEndpointSemantics(
            document,
            flow.RelationshipId,
            flow.SourceId,
            flow.TargetId));
        if (!document.SemanticModel.TryGetElement(flow.SourceId, out _) ||
            !document.SemanticModel.TryGetElement(flow.TargetId, out _))
        {
            return [.. diagnostics];
        }

        if ((flow.Name is not null && string.IsNullOrWhiteSpace(flow.Name)) ||
            (flow.Description is not null && string.IsNullOrWhiteSpace(flow.Description)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "Optional BPMN text properties must be nonblank when supplied.",
                flow.RelationshipId.Value));
        }

        if (flow.Route.Any(static point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "A BPMN Sequence Flow persistent route must contain finite points.",
                flow.RelationshipId.Value));
        }

        ValidateAnchorBinding(
            flow,
            document,
            flow.SourceAnchorId,
            ConnectorAnchorRole.Source,
            flow.SourceId,
            diagnostics);
        ValidateAnchorBinding(
            flow,
            document,
            flow.TargetAnchorId,
            ConnectorAnchorRole.Target,
            flow.TargetId,
            diagnostics);

        return [.. diagnostics];
    }

    internal static ImmutableArray<Diagnostic> ValidateEndpointSemantics(
        DocumentSnapshot document,
        SemanticElementId diagnosticSubjectId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        SemanticElementId? replacedRelationshipId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnosticSubjectId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        var diagnostics = new List<Diagnostic>();
        document.SemanticModel.TryGetElement(sourceId, out var source);
        document.SemanticModel.TryGetElement(targetId, out var target);
        if (source is null || target is null)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.EndpointMissing,
                "A BPMN Sequence Flow requires existing source and target elements.",
                diagnosticSubjectId.Value));
            return [.. diagnostics];
        }

        if (!BpmnSemanticTypes.IsFlowNode(source.TypeId) ||
            !BpmnSemanticTypes.IsFlowNode(target.TypeId) ||
            source.ContainmentKind != SemanticElementContainmentKind.Scope ||
            target.ContainmentKind != SemanticElementContainmentKind.Scope)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.EndpointTypeInvalid,
                "A BPMN Sequence Flow endpoint must be a supported scope-contained BPMN flow node.",
                diagnosticSubjectId.Value));
        }

        if (target.TypeId == BpmnSemanticTypes.StartEvent)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.IncomingStartEvent,
                "A BPMN Start Event cannot have an incoming Sequence Flow.",
                diagnosticSubjectId.Value));
        }

        if (source.TypeId == BpmnSemanticTypes.EndEvent)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.OutgoingEndEvent,
                "A BPMN End Event cannot have an outgoing Sequence Flow.",
                diagnosticSubjectId.Value));
        }

        foreach (var violation in BpmnSequenceFlowConfigurationRules.AnalyzeCandidate(
                     document,
                     diagnosticSubjectId,
                     sourceId,
                     targetId,
                     replacedRelationshipId))
        {
            diagnostics.Add(ConfigurationDiagnostic(violation, diagnosticSubjectId));
        }

        return [.. diagnostics];
    }

    private static Diagnostic ConfigurationDiagnostic(
        BpmnSequenceFlowConfigurationViolation violation,
        SemanticElementId diagnosticSubjectId) => violation.Kind switch
        {
            BpmnSequenceFlowConfigurationViolationKind.SequenceFlowCrossesScope =>
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope,
                    "A BPMN Sequence Flow cannot cross process-scope boundaries; its source and target must belong to the same process scope.",
                    diagnosticSubjectId.Value),
            BpmnSequenceFlowConfigurationViolationKind
                .BoundaryEventIncomingSequenceFlow =>
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.IncomingBoundaryEvent,
                    "A BPMN Boundary Event cannot have an incoming Sequence Flow.",
                    diagnosticSubjectId.Value),
            BpmnSequenceFlowConfigurationViolationKind.EventBasedGatewayTargetInvalid =>
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid,
                    "A BPMN Event-Based Gateway Sequence Flow must target a Message Catch Event, Timer Catch Event, Signal Catch Event, or Receive Task.",
                    diagnosticSubjectId.Value),
            BpmnSequenceFlowConfigurationViolationKind
                .EventBasedGatewayMixedMessageReceptionModes =>
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes
                        .EventBasedGatewayMixedMessageReceptionModes,
                    "A BPMN Event-Based Gateway cannot mix Message Catch Event and Receive Task branches.",
                    diagnosticSubjectId.Value),
            BpmnSequenceFlowConfigurationViolationKind
                .EventBasedTargetAdditionalIncoming =>
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.EventBasedTargetAdditionalIncoming,
                    "A target in an Event-Based Gateway configuration cannot have another incoming Sequence Flow.",
                    diagnosticSubjectId.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(violation)),
        };

    private static void ValidateAnchorBinding(
        CreateBpmnSequenceFlowCommand flow,
        DocumentSnapshot document,
        ConnectorAnchorId anchorId,
        ConnectorAnchorRole expectedRole,
        SemanticElementId expectedOwnerSemanticId,
        List<Diagnostic> diagnostics)
    {
        var matches = new List<(VisualStateSnapshot VisualState, ResolvedConnectorAnchor Anchor)>();
        var policyInvalid = false;
        foreach (var visualState in document.VisualModel.VisualStates)
        {
            if (!document.SemanticModel.TryGetElement(
                    visualState.SemanticElementId,
                    out var element) ||
                element is null)
            {
                continue;
            }

            try
            {
                matches.AddRange(ElementConnectorAnchorResolver.Resolve(
                        visualState,
                        element.TypeId,
                        BpmnConnectorAnchorPolicies.Provider)
                    .Where(anchor => anchor.Id == anchorId)
                    .Select(anchor => (visualState, anchor)));
            }
            catch (InvalidOperationException)
            {
                policyInvalid |= visualState.ConnectorAnchors.Any(anchor =>
                    anchor.Id == anchorId);
            }
        }

        if (policyInvalid || matches.Count != 1)
        {
            diagnostics.Add(InvalidAnchor(
                flow,
                anchorId,
                expectedRole,
                "does not resolve uniquely under the active BPMN connector-anchor policy"));
            return;
        }

        var match = matches[0];
        if (!match.Anchor.Allows(expectedRole))
        {
            diagnostics.Add(InvalidAnchor(
                flow,
                anchorId,
                expectedRole,
                $"does not allow the required {expectedRole} endpoint role"));
        }

        if (match.VisualState.SemanticElementId != expectedOwnerSemanticId)
        {
            diagnostics.Add(InvalidAnchor(
                flow,
                anchorId,
                expectedRole,
                $"is not owned by the endpoint semantic element '{expectedOwnerSemanticId}'"));
        }

        if (ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchorId))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                CommandExecutionDiagnosticCodes.ConnectorAnchorInUse,
                $"Connector anchor '{anchorId}' is already referenced by a connector endpoint.",
                flow.RelationshipId.Value));
        }
    }

    private static Diagnostic InvalidAnchor(
        CreateBpmnSequenceFlowCommand flow,
        ConnectorAnchorId anchorId,
        ConnectorAnchorRole expectedRole,
        string detail) =>
        BpmnDiagnostics.Error(
            BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
            $"BPMN Sequence Flow {expectedRole} anchor '{anchorId}' {detail}.",
            flow.RelationshipId.Value);
}

internal sealed class BpmnSequenceFlowWithTargetAnchorCreationValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        return command is CreateBpmnSequenceFlowWithTargetAnchorCommand creation
            ? BpmnSequenceFlowWithTargetAnchorCreationValidation.Validate(creation, document)
            : [BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "The atomic BPMN Sequence Flow creation request has an invalid shape.",
                command.TypeId.Value)];
    }
}

internal static class BpmnSequenceFlowWithTargetAnchorCreationValidation
{
    internal static ImmutableArray<Diagnostic> Validate(
        CreateBpmnSequenceFlowWithTargetAnchorCommand creation,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<Diagnostic>();
        if (!document.VisualModel.TryGetVisualState(
                creation.TargetVisualStateId,
                out var targetVisual) ||
            targetVisual is null ||
            targetVisual.SemanticElementId != creation.TargetId ||
            !document.SemanticModel.TryGetElement(creation.TargetId, out var targetElement) ||
            targetElement is null)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
                "The proposed Target anchor owner does not match an existing BPMN target node.",
                creation.RelationshipId.Value));
            return [.. diagnostics];
        }

        diagnostics.AddRange(BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(
            document,
            creation.RelationshipId,
            creation.SourceId,
            creation.TargetId));

        var policy = BpmnConnectorAnchorPolicies.Provider.Resolve(targetElement.TypeId);
        if (!ElementConnectorAnchorPolicyEvaluator.CanAdd(
                policy,
                targetVisual,
                creation.TargetSide,
                ConnectorAnchorRole.Target))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
                $"The selected target edge '{creation.TargetSide}' cannot add a Target anchor.",
                creation.RelationshipId.Value));
        }

        var sideCount = targetVisual.ConnectorAnchors.Count(anchor =>
            anchor.Side == creation.TargetSide);
        if (creation.TargetInsertionIndex > sideCount)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
                "The proposed Target anchor insertion index is no longer valid.",
                creation.RelationshipId.Value));
        }

        if (ConnectorAnchorReferenceIdentity.IsPredefinedReference(creation.TargetAnchorId) ||
            ResolvesAnchorIdentity(document, creation.TargetAnchorId))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
                $"Connector-anchor identity '{creation.TargetAnchorId}' is reserved or already exists.",
                creation.RelationshipId.Value));
        }

        if (diagnostics.Count > 0)
        {
            return [.. diagnostics];
        }

        var replacement = ConnectorAnchorInsertion.Insert(
            targetVisual,
            creation.TargetAnchorId,
            creation.TargetSide,
            ConnectorAnchorRole.Target,
            creation.TargetInsertionIndex);
        var interim = BpmnDocumentReplacement.ReplaceVisual(document, replacement);
        var sequenceFlow = new CreateBpmnSequenceFlowCommand(
            creation.TargetDocumentId,
            creation.ExpectedRevision,
            creation.RelationshipId,
            creation.VisualStateId,
            creation.SourceId,
            creation.TargetId,
            creation.SourceAnchorId,
            creation.TargetAnchorId,
            creation.Route,
            creation.Name,
            creation.Description);
        return BpmnSequenceFlowCreationValidation.Validate(sequenceFlow, interim);
    }

    private static bool ResolvesAnchorIdentity(
        DocumentSnapshot document,
        ConnectorAnchorId anchorId)
    {
        foreach (var visual in document.VisualModel.VisualStates)
        {
            if (!document.SemanticModel.TryGetElement(
                    visual.SemanticElementId,
                    out var element) ||
                element is null)
            {
                continue;
            }

            try
            {
                if (ElementConnectorAnchorResolver.Resolve(
                        visual,
                        element.TypeId,
                        BpmnConnectorAnchorPolicies.Provider)
                    .Any(anchor => anchor.Id == anchorId))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                if (visual.ConnectorAnchors.Any(anchor => anchor.Id == anchorId))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

internal sealed class BpmnExclusiveGatewayCodeUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(update.PropertyKey, BpmnSemanticProperties.Code) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element?.TypeId != BpmnSemanticTypes.ExclusiveGateway)
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(update.TargetValue.TextValue)
                ? []
                : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidExclusiveGateway,
                    "A BPMN Exclusive Gateway Code must remain nonblank text.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal sealed class BpmnParallelGatewayCodeUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(update.PropertyKey, BpmnSemanticProperties.Code) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element?.TypeId != BpmnSemanticTypes.ParallelGateway)
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(update.TargetValue.TextValue)
                ? []
                : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidParallelGateway,
                    "A BPMN Parallel Gateway Code must remain nonblank text.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal sealed class BpmnInclusiveGatewayCodeUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(update.PropertyKey, BpmnSemanticProperties.Code) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element?.TypeId != BpmnSemanticTypes.InclusiveGateway)
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(update.TargetValue.TextValue)
                ? []
                : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidInclusiveGateway,
                    "A BPMN Inclusive Gateway Code must remain nonblank text.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal sealed class BpmnEventBasedGatewayCodeUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(update.PropertyKey, BpmnSemanticProperties.Code) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element?.TypeId != BpmnSemanticTypes.EventBasedGateway)
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(update.TargetValue.TextValue)
                ? []
                : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidEventBasedGateway,
                    "A BPMN Event-Based Gateway Code must remain nonblank text.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal sealed class BpmnSubProcessCodeUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(update.PropertyKey, BpmnSemanticProperties.Code) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element?.TypeId != BpmnSemanticTypes.SubProcess)
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(update.TargetValue.TextValue)
                ? []
                : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidSubProcess,
                    "A BPMN SubProcess Code must remain nonblank text.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal sealed class BpmnTaskCodeUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(update.PropertyKey, BpmnSemanticProperties.Code) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element is null ||
            !BpmnTaskSemanticTypes.IsTask(element.TypeId))
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(update.TargetValue.TextValue)
                ? []
                : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidTask,
                    "A BPMN Task Code must remain nonblank text.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal sealed class BpmnTaskElementNumberUpdateValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateSemanticElementPropertyCommand update ||
            !StringComparer.Ordinal.Equals(
                update.PropertyKey,
                BpmnSemanticProperties.ElementNumber) ||
            !document.SemanticModel.TryGetElement(update.TargetSemanticElementId, out var element) ||
            element is null ||
            !BpmnTaskSemanticTypes.IsTask(element.TypeId))
        {
            return [];
        }

        return update.TargetValue.Kind == PropertyValueKind.Integer
            ? []
            : [BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidTask,
                    "A BPMN Task Element number must remain an integer.",
                    update.TargetSemanticElementId.Value)];
    }
}

internal static class BpmnDiagnostics
{
    internal static Diagnostic Error(string code, string message, string source) =>
        new(code, DiagnosticSeverity.Error, message, source);
}
