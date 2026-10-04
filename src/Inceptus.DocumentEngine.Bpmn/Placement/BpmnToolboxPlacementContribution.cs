using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Placement;

internal static class BpmnToolboxPlacementContribution
{
    internal static ImmutableArray<ToolboxPlacementRegistration> Registrations { get; } =
    [
        Registration(BpmnToolboxContribution.StartEventItemId, BpmnPlacementNodeKind.StartEvent),
        Registration(BpmnToolboxContribution.TaskItemId, BpmnPlacementNodeKind.Task),
        Registration(
            BpmnToolboxContribution.ExclusiveGatewayItemId,
            BpmnPlacementNodeKind.ExclusiveGateway),
        Registration(
            BpmnToolboxContribution.ParallelGatewayItemId,
            BpmnPlacementNodeKind.ParallelGateway),
        Registration(
            BpmnToolboxContribution.InclusiveGatewayItemId,
            BpmnPlacementNodeKind.InclusiveGateway),
        Registration(BpmnToolboxContribution.EndEventItemId, BpmnPlacementNodeKind.EndEvent),
    ];

    internal static ImmutableArray<ToolboxPlacementRegistration> N4Registrations { get; } =
    [
        .. Registrations,
        Registration(
            BpmnToolboxContribution.MessageCatchEventItemId,
            BpmnPlacementNodeKind.MessageCatchEvent),
        Registration(
            BpmnToolboxContribution.TimerCatchEventItemId,
            BpmnPlacementNodeKind.TimerCatchEvent),
        Registration(
            BpmnToolboxContribution.EventBasedGatewayItemId,
            BpmnPlacementNodeKind.EventBasedGateway),
    ];

    internal static ImmutableArray<ToolboxPlacementRegistration> N6Registrations { get; } =
    [
        .. N4Registrations,
        Registration(BpmnToolboxContribution.UserTaskItemId, BpmnPlacementNodeKind.UserTask),
        Registration(
            BpmnToolboxContribution.ManualTaskItemId,
            BpmnPlacementNodeKind.ManualTask),
        Registration(
            BpmnToolboxContribution.ServiceTaskItemId,
            BpmnPlacementNodeKind.ServiceTask),
        Registration(BpmnToolboxContribution.SendTaskItemId, BpmnPlacementNodeKind.SendTask),
        Registration(
            BpmnToolboxContribution.ReceiveTaskItemId,
            BpmnPlacementNodeKind.ReceiveTask),
    ];

    internal static ImmutableArray<ToolboxPlacementRegistration> N7Registrations { get; } =
    [
        .. N6Registrations,
        Registration(
            BpmnToolboxContribution.MessageThrowEventItemId,
            BpmnPlacementNodeKind.MessageThrowEvent),
        Registration(
            BpmnToolboxContribution.SignalCatchEventItemId,
            BpmnPlacementNodeKind.SignalCatchEvent),
        Registration(
            BpmnToolboxContribution.SignalThrowEventItemId,
            BpmnPlacementNodeKind.SignalThrowEvent),
    ];

    internal static ImmutableArray<ToolboxPlacementRegistration> N81Registrations { get; } =
    [
        .. N7Registrations,
        Registration(
            BpmnToolboxContribution.SubProcessItemId,
            BpmnPlacementNodeKind.SubProcess),
    ];

    internal static ImmutableArray<ToolboxPlacementRegistration> N90Registrations { get; } =
    [
        .. N81Registrations,
        new ToolboxPlacementRegistration(
            BpmnToolboxContribution.TimerBoundaryEventItemId,
            new BpmnTimerBoundaryEventToolboxPlacementCommandFactory(),
            new BpmnTimerBoundaryEventPlacementCandidateProvider(),
            new BpmnToolboxPlacementPreviewProvider(BpmnToolboxContribution.TimerBoundaryEventItemId,
                BpmnSemanticTypes.TimerBoundaryEvent, BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind)),
    ];

    internal static ImmutableArray<ToolboxPlacementRegistration> N91Registrations { get; } =
    [
        .. N90Registrations,
        new ToolboxPlacementRegistration(
            BpmnToolboxContribution.MessageBoundaryEventItemId,
            new BpmnMessageBoundaryEventToolboxPlacementCommandFactory(),
            new BpmnMessageBoundaryEventPlacementCandidateProvider(),
            new BpmnToolboxPlacementPreviewProvider(BpmnToolboxContribution.MessageBoundaryEventItemId,
                BpmnSemanticTypes.MessageBoundaryEvent, BpmnMessageBoundaryEventSceneFeedback.AttachmentCandidateKind)),
        new ToolboxPlacementRegistration(
            BpmnToolboxContribution.SignalBoundaryEventItemId,
            new BpmnSignalBoundaryEventToolboxPlacementCommandFactory(),
            new BpmnSignalBoundaryEventPlacementCandidateProvider(),
            new BpmnToolboxPlacementPreviewProvider(BpmnToolboxContribution.SignalBoundaryEventItemId,
                BpmnSemanticTypes.SignalBoundaryEvent, BpmnSignalBoundaryEventSceneFeedback.AttachmentCandidateKind)),
    ];

    private static ToolboxPlacementRegistration Registration(
        ToolboxItemId toolboxItemId,
        BpmnPlacementNodeKind nodeKind) =>
        new(
            toolboxItemId,
            new BpmnToolboxPlacementCommandFactory(toolboxItemId, nodeKind),
            candidateProvider: null,
            new BpmnToolboxPlacementPreviewProvider(toolboxItemId,
                BpmnToolboxPlacementCommandFactory.SemanticTypeId(nodeKind)));
}

internal sealed class BpmnTimerBoundaryEventPlacementCandidateProvider :
    IToolboxPlacementCandidateProvider
{
    private readonly BpmnBoundaryEventPlacementCandidateProvider _inner = new(
        BpmnSemanticTypes.TimerBoundaryEvent,
        BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind);

    internal const double BoundaryHitTolerance =
        BpmnBoundaryEventPlacementCandidateProvider.BoundaryHitTolerance;

    internal const string AttachmentSideProperty =
        BpmnBoundaryEventPlacementCandidateProvider.AttachmentSideProperty;

    internal const string PositionOnSideProperty =
        BpmnBoundaryEventPlacementCandidateProvider.PositionOnSideProperty;

    public ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request) =>
        _inner.ResolveCandidate(request);
}

internal sealed class BpmnMessageBoundaryEventPlacementCandidateProvider :
    IToolboxPlacementCandidateProvider
{
    private readonly BpmnBoundaryEventPlacementCandidateProvider _inner = new(
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnMessageBoundaryEventSceneFeedback.AttachmentCandidateKind);

    public ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request) =>
        _inner.ResolveCandidate(request);
}

internal sealed class BpmnSignalBoundaryEventPlacementCandidateProvider :
    IToolboxPlacementCandidateProvider
{
    private readonly BpmnBoundaryEventPlacementCandidateProvider _inner = new(
        BpmnSemanticTypes.SignalBoundaryEvent,
        BpmnSignalBoundaryEventSceneFeedback.AttachmentCandidateKind);

    public ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request) =>
        _inner.ResolveCandidate(request);
}

internal sealed class BpmnBoundaryEventPlacementCandidateProvider :
    IToolboxPlacementCandidateProvider
{
    private readonly SemanticTypeId _semanticTypeId;
    private readonly string _feedbackKind;

    internal BpmnBoundaryEventPlacementCandidateProvider(
        SemanticTypeId semanticTypeId,
        string feedbackKind)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedbackKind);
        if (!BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            throw new ArgumentException(
                $"Semantic type '{semanticTypeId}' is not a supported BPMN Boundary Event.",
                nameof(semanticTypeId));
        }

        _semanticTypeId = semanticTypeId;
        _feedbackKind = feedbackKind;
    }

    // The event radius is a stable logical-coordinate tolerance. It keeps deep
    // Activity interiors ineligible while making the complete preview body usable.
    internal const double BoundaryHitTolerance = 18d;

    internal const string AttachmentSideProperty =
        "bpmn:toolbox:boundary-attachment-side";

    internal const string PositionOnSideProperty =
        "bpmn:toolbox:boundary-position-on-side";

    public ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveCandidate(new ToolboxPlacementPreviewRequest(request.ToolboxItemId,
            request.Document, request.DocumentPoint, request.TargetScopeId, request.VisibleTargets));
    }

    internal ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var eventSize = BpmnNodeLogicalSizePolicy.Resolve(_semanticTypeId);
        ToolboxPlacementTarget? bestTarget = null;
        BoundaryAttachmentPlacement? bestPlacement = null;
        var bestDistanceSquared = double.PositiveInfinity;
        var toleranceSquared = BoundaryHitTolerance * BoundaryHitTolerance;

        foreach (var target in request.VisibleTargets)
        {
            if (!BpmnActivitySemanticTypes.IsActivity(target.SemanticTypeId))
            {
                continue;
            }

            var semantic = request.Document.SemanticModel.Elements.FirstOrDefault(
                element => element.Id == target.SemanticElementId);
            if (semantic is null ||
                semantic.TypeId != target.SemanticTypeId ||
                !BpmnActivitySemanticTypes.IsActivity(semantic.TypeId) ||
                request.Document.SemanticModel.GetScope(semantic.Id).Id !=
                request.TargetScopeId ||
                !BoundaryAttachmentPlacement.TryProjectToBoundary(
                    target.Bounds,
                    request.DocumentPoint,
                    eventSize,
                    out var placement))
            {
                continue;
            }

            var center = placement!.ResolveCenter(target.Bounds);
            var deltaX = request.DocumentPoint.X - center.X;
            var deltaY = request.DocumentPoint.Y - center.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            if (distanceSquared > toleranceSquared || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestTarget = target;
            bestPlacement = placement;
            bestDistanceSquared = distanceSquared;
        }

        if (bestTarget is null || bestPlacement is null)
        {
            return null;
        }

        return new ToolboxPlacementCandidate(
            bestTarget,
            _feedbackKind,
            bestPlacement.ResolveBounds(bestTarget.Bounds, eventSize),
            new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
            {
                [AttachmentSideProperty] = PropertyValue.FromInteger(
                    (long)bestPlacement.Side),
                [PositionOnSideProperty] = PropertyValue.FromNumber(
                    bestPlacement.PositionOnSide),
                [BpmnSemanticProperties.CancelActivity] =
                    PropertyValue.FromBoolean(true),
            },
            feedbackPresentationMode: EditorFeedbackPresentationMode.ContributorOnly);
    }
}

internal sealed class BpmnTimerBoundaryEventToolboxPlacementCommandFactory :
    IToolboxPlacementCommandFactory
{
    private readonly BpmnBoundaryEventToolboxPlacementCommandFactory _inner = new(
        BpmnToolboxContribution.TimerBoundaryEventItemId,
        BpmnSemanticTypes.TimerBoundaryEvent,
        BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind);

    public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request) =>
        _inner.CreatePlan(request);
}

internal sealed class BpmnMessageBoundaryEventToolboxPlacementCommandFactory :
    IToolboxPlacementCommandFactory
{
    private readonly BpmnBoundaryEventToolboxPlacementCommandFactory _inner = new(
        BpmnToolboxContribution.MessageBoundaryEventItemId,
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnMessageBoundaryEventSceneFeedback.AttachmentCandidateKind);

    public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request) =>
        _inner.CreatePlan(request);
}

internal sealed class BpmnSignalBoundaryEventToolboxPlacementCommandFactory :
    IToolboxPlacementCommandFactory
{
    private readonly BpmnBoundaryEventToolboxPlacementCommandFactory _inner = new(
        BpmnToolboxContribution.SignalBoundaryEventItemId,
        BpmnSemanticTypes.SignalBoundaryEvent,
        BpmnSignalBoundaryEventSceneFeedback.AttachmentCandidateKind);

    public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request) =>
        _inner.CreatePlan(request);
}

internal sealed class BpmnBoundaryEventToolboxPlacementCommandFactory :
    IToolboxPlacementCommandFactory
{
    private readonly ToolboxItemId _toolboxItemId;
    private readonly SemanticTypeId _semanticTypeId;
    private readonly string _feedbackKind;
    private readonly string _displayName;

    internal BpmnBoundaryEventToolboxPlacementCommandFactory(
        ToolboxItemId toolboxItemId,
        SemanticTypeId semanticTypeId,
        string feedbackKind)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedbackKind);
        if (!BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            throw new ArgumentException(
                $"Semantic type '{semanticTypeId}' is not a supported BPMN Boundary Event.",
                nameof(semanticTypeId));
        }

        _toolboxItemId = toolboxItemId;
        _semanticTypeId = semanticTypeId;
        _feedbackKind = feedbackKind;
        _displayName = BpmnBoundaryEventSemanticTypes.DisplayName(semanticTypeId);
    }

    public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ToolboxItemId != _toolboxItemId)
        {
            return Failure(
                BpmnToolboxPlacementDiagnosticCodes.ToolboxItemMismatch,
                $"The {_displayName} placement factory cannot create a plan for '{request.ToolboxItemId}'.",
                request.ToolboxItemId.Value);
        }

        if (request.Candidate is not { } candidate)
        {
            return Failure(
                BpmnToolboxPlacementDiagnosticCodes.AttachmentCandidateRequired,
                $"A {_displayName} can only be placed on or near a visible Activity boundary.",
                request.ToolboxItemId.Value);
        }

        var target = candidate.Target;
        var semantic = request.Document.SemanticModel.Elements.FirstOrDefault(
            element => element.Id == target.SemanticElementId);
        if (semantic is null ||
            !BpmnActivitySemanticTypes.IsActivity(semantic.TypeId) ||
            semantic.TypeId != target.SemanticTypeId ||
            request.Document.SemanticModel.GetScope(semantic.Id).Id != request.TargetScopeId ||
            candidate.FeedbackKind != _feedbackKind ||
            !TryReadPlacement(candidate, out var placement) ||
            !placement!.ResolveBounds(
                    target.Bounds,
                    BpmnNodeLogicalSizePolicy.Resolve(_semanticTypeId))
                .Equals(candidate.PreviewBounds) ||
            !DocumentGeometryBoundary.Contains(candidate.PreviewBounds))
        {
            return Failure(
                BpmnToolboxPlacementDiagnosticCodes.AttachmentCandidateInvalid,
                $"The {_displayName} attachment candidate is no longer valid in the active scope.",
                target.SemanticElementId.Value);
        }

        var identity = request.IdentityProvider.CreateIdentity();
        ArgumentNullException.ThrowIfNull(identity);
        var defaults = BpmnToolboxCreationDefaults.Resolve(request.Document, _semanticTypeId, out _);
        var command = CreateCommand(
            request.Document.DocumentId,
            request.ExpectedRevision,
            identity.SemanticElementId,
            identity.VisualStateId,
            target.SemanticElementId,
            placement.Side,
            placement.PositionOnSide,
            target.Bounds,
            defaults.Name!,
            defaults.Description!,
            request.TargetScopeId);

        return ToolboxPlacementPlanResult.Success(new ToolboxPlacementPlan(
            command,
            identity.SemanticElementId,
            identity.VisualStateId));
    }

    private static bool TryReadPlacement(
        ToolboxPlacementCandidate candidate,
        out BoundaryAttachmentPlacement? placement)
    {
        placement = null;
        if (!candidate.Properties.TryGetValue(
                BpmnTimerBoundaryEventPlacementCandidateProvider.AttachmentSideProperty,
                out var sideValue) ||
            sideValue.Kind != PropertyValueKind.Integer ||
            sideValue.IntegerValue < 0L ||
            sideValue.IntegerValue > (long)BoundaryAttachmentSide.Left ||
            !candidate.Properties.TryGetValue(
                BpmnTimerBoundaryEventPlacementCandidateProvider.PositionOnSideProperty,
                out var positionValue) ||
            positionValue.Kind != PropertyValueKind.Number)
        {
            return false;
        }

        var position = positionValue.NumberValue;
        if (position is < 0d or > 1d)
        {
            return false;
        }

        placement = new BoundaryAttachmentPlacement(
            (BoundaryAttachmentSide)sideValue.IntegerValue,
            position);
        return true;
    }

    private ICommand CreateCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticElementId attachedToActivityId,
        BoundaryAttachmentSide side,
        double positionOnSide,
        RectD effectiveOwnerBounds,
        string name,
        string description,
        DocumentScopeId targetScopeId)
    {
        if (_semanticTypeId == BpmnSemanticTypes.MessageBoundaryEvent)
        {
            return new CreateBpmnMessageBoundaryEventCommand(
                targetDocumentId,
                expectedRevision,
                elementId,
                visualStateId,
                attachedToActivityId,
                side,
                positionOnSide,
                effectiveOwnerBounds,
                name,
                description: description,
                targetScopeId: targetScopeId);
        }

        if (_semanticTypeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            return new CreateBpmnTimerBoundaryEventCommand(
                targetDocumentId,
                expectedRevision,
                elementId,
                visualStateId,
                attachedToActivityId,
                side,
                positionOnSide,
                effectiveOwnerBounds,
                name,
                description: description,
                targetScopeId: targetScopeId);
        }

        return new CreateBpmnSignalBoundaryEventCommand(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            attachedToActivityId,
            side,
            positionOnSide,
            effectiveOwnerBounds,
            name,
            description: description,
            targetScopeId: targetScopeId);
    }

    private static ToolboxPlacementPlanResult Failure(
        string code,
        string message,
        string source) =>
        ToolboxPlacementPlanResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, source),
        ]);
}

internal enum BpmnPlacementNodeKind
{
    StartEvent,
    Task,
    UserTask,
    ManualTask,
    ServiceTask,
    SendTask,
    ReceiveTask,
    SubProcess,
    ExclusiveGateway,
    ParallelGateway,
    InclusiveGateway,
    EventBasedGateway,
    MessageCatchEvent,
    MessageThrowEvent,
    TimerCatchEvent,
    SignalCatchEvent,
    SignalThrowEvent,
    EndEvent,
}

internal sealed class BpmnToolboxPlacementCommandFactory :
    IToolboxPlacementCommandFactory
{
    private readonly ToolboxItemId _toolboxItemId;
    private readonly BpmnPlacementNodeKind _nodeKind;

    internal BpmnToolboxPlacementCommandFactory(
        ToolboxItemId toolboxItemId,
        BpmnPlacementNodeKind nodeKind)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        _toolboxItemId = toolboxItemId;
        _nodeKind = nodeKind;
    }

    public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ToolboxItemId != _toolboxItemId)
        {
            return ToolboxPlacementPlanResult.Failure(
            [
                Error(
                    BpmnToolboxPlacementDiagnosticCodes.ToolboxItemMismatch,
                    $"BPMN placement factory for '{_toolboxItemId}' cannot create a plan for '{request.ToolboxItemId}'.",
                    request.ToolboxItemId.Value),
            ]);
        }

        var defaults = BpmnToolboxCreationDefaults.Resolve(request.Document, SemanticTypeId(_nodeKind), out var defaultsFailure);
        if (defaultsFailure is not null)
        {
            return ToolboxPlacementPlanResult.Failure([new Diagnostic(defaultsFailure.Code,
                defaultsFailure.Severity, defaultsFailure.Message, request.ToolboxItemId.Value)]);
        }

        var size = defaults.Size;
        var bounds = defaults.BoundsAt(request.DocumentPoint);
        var position = bounds.TopLeft;
        if (!DocumentGeometryBoundary.Contains(bounds))
        {
            return ToolboxPlacementPlanResult.Failure(
            [
                Error(CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"BPMN Toolbox item '{request.ToolboxItemId}' cannot be placed because its final bounds would cross the Document boundary.",
                    request.ToolboxItemId.Value),
            ]);
        }
        var identity = request.IdentityProvider.CreateIdentity();
        ArgumentNullException.ThrowIfNull(identity);
        var command = CreateCommand(
            request,
            identity,
            position,
            size,
            defaults);

        return ToolboxPlacementPlanResult.Success(
            new ToolboxPlacementPlan(
                command,
                identity.SemanticElementId,
                identity.VisualStateId));
    }

    private ICommand CreateCommand(
        ToolboxPlacementRequest request,
        DocumentCreationIdentity identity,
        PointD position,
        SizeD size,
        BpmnToolboxCreationDefaults defaults) => _nodeKind switch
        {
            BpmnPlacementNodeKind.StartEvent => new CreateBpmnStartEventCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                identity.SemanticElementId,
                identity.VisualStateId,
                position,
                size,
                VisualPlacementMode.Pinned,
                targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.Task or
            BpmnPlacementNodeKind.UserTask or
            BpmnPlacementNodeKind.ManualTask or
            BpmnPlacementNodeKind.ServiceTask or
            BpmnPlacementNodeKind.SendTask or
            BpmnPlacementNodeKind.ReceiveTask => CreateTaskCommand(
                request,
                identity,
                position,
                size,
                SemanticTypeId(_nodeKind),
                defaults),
            BpmnPlacementNodeKind.SubProcess => new CreateBpmnSubProcessCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                identity.SemanticElementId,
                identity.VisualStateId,
                request.TargetScopeId,
                new DocumentScopeId($"bpmn:scope:{identity.SemanticElementId.Value}"),
                position,
                size,
                defaults.Code!,
                defaults.Name!,
                VisualPlacementMode.Pinned,
                description: defaults.Description),
            BpmnPlacementNodeKind.ExclusiveGateway =>
                new CreateBpmnExclusiveGatewayCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Code!,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.ParallelGateway =>
                new CreateBpmnParallelGatewayCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Code!,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.InclusiveGateway =>
                new CreateBpmnInclusiveGatewayCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Code!,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.EventBasedGateway =>
                new CreateBpmnEventBasedGatewayCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Code!,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.MessageCatchEvent =>
                new CreateBpmnMessageCatchEventCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.MessageThrowEvent =>
                new CreateBpmnMessageThrowEventCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.TimerCatchEvent =>
                new CreateBpmnTimerCatchEventCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.SignalCatchEvent =>
                new CreateBpmnSignalCatchEventCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.SignalThrowEvent =>
                new CreateBpmnSignalThrowEventCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    identity.SemanticElementId,
                    identity.VisualStateId,
                    position,
                    size,
                    defaults.Name!,
                    VisualPlacementMode.Pinned,
                    description: defaults.Description,
                    targetScopeId: request.TargetScopeId),
            BpmnPlacementNodeKind.EndEvent => new CreateBpmnEndEventCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                identity.SemanticElementId,
                identity.VisualStateId,
                position,
                size,
                VisualPlacementMode.Pinned,
                targetScopeId: request.TargetScopeId),
            _ => throw new InvalidOperationException(
                $"Unsupported BPMN Toolbox placement node kind '{_nodeKind}'."),
        };

    private static CreateBpmnTaskCommand CreateTaskCommand(
        ToolboxPlacementRequest request,
        DocumentCreationIdentity identity,
        PointD position,
        SizeD size,
        SemanticTypeId semanticTypeId,
        BpmnToolboxCreationDefaults defaults)
    {
        return new CreateBpmnTaskCommand(
            request.Document.DocumentId,
            request.ExpectedRevision,
            identity.SemanticElementId,
            identity.VisualStateId,
            position,
            size,
            defaults.Code!,
            defaults.Name!,
            defaults.ElementNumber,
            VisualPlacementMode.Pinned,
            description: defaults.Description,
            taskTypeId: semanticTypeId,
            targetScopeId: request.TargetScopeId);
    }

    internal static SemanticTypeId SemanticTypeId(BpmnPlacementNodeKind nodeKind) =>
        nodeKind switch
        {
            BpmnPlacementNodeKind.StartEvent => BpmnSemanticTypes.StartEvent,
            BpmnPlacementNodeKind.Task => BpmnSemanticTypes.Task,
            BpmnPlacementNodeKind.UserTask => BpmnSemanticTypes.UserTask,
            BpmnPlacementNodeKind.ManualTask => BpmnSemanticTypes.ManualTask,
            BpmnPlacementNodeKind.ServiceTask => BpmnSemanticTypes.ServiceTask,
            BpmnPlacementNodeKind.SendTask => BpmnSemanticTypes.SendTask,
            BpmnPlacementNodeKind.ReceiveTask => BpmnSemanticTypes.ReceiveTask,
            BpmnPlacementNodeKind.SubProcess => BpmnSemanticTypes.SubProcess,
            BpmnPlacementNodeKind.ExclusiveGateway =>
                BpmnSemanticTypes.ExclusiveGateway,
            BpmnPlacementNodeKind.ParallelGateway => BpmnSemanticTypes.ParallelGateway,
            BpmnPlacementNodeKind.InclusiveGateway => BpmnSemanticTypes.InclusiveGateway,
            BpmnPlacementNodeKind.EventBasedGateway => BpmnSemanticTypes.EventBasedGateway,
            BpmnPlacementNodeKind.MessageCatchEvent => BpmnSemanticTypes.MessageCatchEvent,
            BpmnPlacementNodeKind.MessageThrowEvent => BpmnSemanticTypes.MessageThrowEvent,
            BpmnPlacementNodeKind.TimerCatchEvent => BpmnSemanticTypes.TimerCatchEvent,
            BpmnPlacementNodeKind.SignalCatchEvent => BpmnSemanticTypes.SignalCatchEvent,
            BpmnPlacementNodeKind.SignalThrowEvent => BpmnSemanticTypes.SignalThrowEvent,
            BpmnPlacementNodeKind.EndEvent => BpmnSemanticTypes.EndEvent,
            _ => throw new ArgumentOutOfRangeException(nameof(nodeKind), nodeKind, null),
        };

    private static Diagnostic Error(string code, string message, string source) =>
        new(code, DiagnosticSeverity.Error, message, source);
}
