using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Bpmn.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

public abstract class BpmnElementCreationCommand : ICommand, ICommandPipelineInvalidation
{
    protected BpmnElementCreationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        VisualPlacementMode placementMode,
        DocumentScopeId? targetScopeId = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(elementId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(nameof(placementMode));
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ElementId = elementId;
        VisualStateId = visualStateId;
        Position = position;
        Size = size;
        PlacementMode = placementMode;
        TargetScopeId = targetScopeId;
    }

    public abstract CommandTypeId TypeId { get; }

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId ElementId { get; }

    public VisualStateId VisualStateId { get; }

    public PointD Position { get; }

    public SizeD Size { get; }

    public VisualPlacementMode PlacementMode { get; }

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PlacementMode == VisualPlacementMode.Pinned
            ? CommandPipelineInvalidation.WithoutNodeLayout
            : CommandPipelineInvalidation.Full;

    internal NodeGeometryPipelineImpact? NodeGeometryImpact =>
        PlacementMode == VisualPlacementMode.Pinned
            ? NodeGeometryPipelineImpact.ForChangedVisualStates([VisualStateId])
            : null;

    /// <summary>
    /// Gets the explicit process scope targeted by this creation, or <see langword="null"/>
    /// when the command uses the canonical root-scope compatibility path.
    /// </summary>
    public DocumentScopeId? TargetScopeId { get; }
}

public sealed class CreateBpmnStartEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-start-event");

    public CreateBpmnStartEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? name = null,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string? Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnTaskCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-task");

    public CreateBpmnTaskCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string code,
        string name,
        long elementNumber,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        SemanticTypeId? taskTypeId = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        Code = code;
        Name = name;
        ElementNumber = elementNumber;
        Description = description;
        TaskTypeId = taskTypeId ?? BpmnSemanticTypes.Task;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Code { get; }

    public string Name { get; }

    public long ElementNumber { get; }

    public string? Description { get; }

    public SemanticTypeId TaskTypeId { get; }
}

/// <summary>
/// Atomically creates one compact BPMN SubProcess and its owned child scope.
/// Both scope identities are explicit so command replay never allocates or
/// regenerates containment identity.
/// </summary>
public sealed class CreateBpmnSubProcessCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-sub-process");

    public CreateBpmnSubProcessCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        DocumentScopeId parentScopeId,
        DocumentScopeId childScopeId,
        PointD position,
        SizeD size,
        string code,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            parentScopeId)
    {
        ArgumentNullException.ThrowIfNull(parentScopeId);
        ArgumentNullException.ThrowIfNull(childScopeId);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);

        ParentScopeId = parentScopeId;
        ChildScopeId = childScopeId;
        Code = code;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public DocumentScopeId ParentScopeId { get; }

    public DocumentScopeId ChildScopeId { get; }

    public string Code { get; }

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnExclusiveGatewayCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-exclusive-gateway");

    public CreateBpmnExclusiveGatewayCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string code,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        Code = code;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Code { get; }

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnParallelGatewayCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-parallel-gateway");

    public CreateBpmnParallelGatewayCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string code,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        Code = code;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Code { get; }

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnInclusiveGatewayCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-inclusive-gateway");

    public CreateBpmnInclusiveGatewayCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string code,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        Code = code;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Code { get; }

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnEventBasedGatewayCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-event-based-gateway");

    public CreateBpmnEventBasedGatewayCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string code,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        Code = code;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Code { get; }

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnMessageCatchEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-message-catch-event");

    public CreateBpmnMessageCatchEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnMessageThrowEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-message-throw-event");

    public CreateBpmnMessageThrowEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnTimerCatchEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-timer-catch-event");

    public CreateBpmnTimerCatchEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? timerDefinition = null,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        TimerDefinition = timerDefinition;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Name { get; }

    public string? TimerDefinition { get; }

    public string? Description { get; }
}

/// <summary>
/// Atomically creates a Message Boundary Event attached to one existing BPMN Activity.
/// Boundary-relative placement is the sole editable placement authority; Position and
/// Size are derived by the handler from the current Activity bounds.
/// </summary>
public sealed class CreateBpmnMessageBoundaryEventCommand : ICommand
{
    private readonly BpmnBoundaryEventCreationCommandState _state;

    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-message-boundary-event");

    public CreateBpmnMessageBoundaryEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticElementId attachedToActivityId,
        BoundaryAttachmentSide side,
        double positionOnSide,
        RectD effectiveOwnerBounds,
        string name,
        bool cancelActivity = true,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
    {
        _state = new BpmnBoundaryEventCreationCommandState(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            attachedToActivityId,
            side,
            positionOnSide,
            effectiveOwnerBounds,
            name,
            cancelActivity,
            description,
            targetScopeId);
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId => _state.TargetDocumentId;

    public DocumentRevision ExpectedRevision => _state.ExpectedRevision;

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId ElementId => _state.ElementId;

    public VisualStateId VisualStateId => _state.VisualStateId;

    public SemanticElementId AttachedToActivityId => _state.AttachedToActivityId;

    public BoundaryAttachmentPlacement BoundaryAttachment => _state.BoundaryAttachment;

    public BoundaryAttachmentSide Side => BoundaryAttachment.Side;

    public double PositionOnSide => BoundaryAttachment.PositionOnSide;

    public RectD EffectiveOwnerBounds => _state.EffectiveOwnerBounds;

    public string Name => _state.Name;

    public bool CancelActivity => _state.CancelActivity;

    public string? Description => _state.Description;

    public DocumentScopeId? TargetScopeId => _state.TargetScopeId;

    internal BpmnBoundaryEventCreationCommandState State => _state;
}

/// <summary>
/// Atomically creates a Timer Boundary Event attached to one existing BPMN Activity.
/// Boundary-relative placement is the sole editable placement authority; Position and
/// Size are derived by the handler from the current Activity bounds.
/// </summary>
public sealed class CreateBpmnTimerBoundaryEventCommand : ICommand
{
    private readonly BpmnBoundaryEventCreationCommandState _state;

    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-timer-boundary-event");

    public CreateBpmnTimerBoundaryEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticElementId attachedToActivityId,
        BoundaryAttachmentSide side,
        double positionOnSide,
        RectD effectiveOwnerBounds,
        string name,
        string? timerDefinition = null,
        bool cancelActivity = true,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
    {
        _state = new BpmnBoundaryEventCreationCommandState(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            attachedToActivityId,
            side,
            positionOnSide,
            effectiveOwnerBounds,
            name,
            cancelActivity,
            description,
            targetScopeId);
        TimerDefinition = timerDefinition;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId => _state.TargetDocumentId;

    public DocumentRevision ExpectedRevision => _state.ExpectedRevision;

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId ElementId => _state.ElementId;

    public VisualStateId VisualStateId => _state.VisualStateId;

    public SemanticElementId AttachedToActivityId => _state.AttachedToActivityId;

    public BoundaryAttachmentPlacement BoundaryAttachment => _state.BoundaryAttachment;

    public BoundaryAttachmentSide Side => BoundaryAttachment.Side;

    public double PositionOnSide => BoundaryAttachment.PositionOnSide;

    /// <summary>
    /// Gets the effective Layout bounds of the Activity used to resolve initial
    /// boundary-event geometry. Persistent Activity geometry remains unchanged.
    /// </summary>
    public RectD EffectiveOwnerBounds => _state.EffectiveOwnerBounds;

    public string Name => _state.Name;

    public string? TimerDefinition { get; }

    public bool CancelActivity => _state.CancelActivity;

    public string? Description => _state.Description;

    /// <summary>
    /// Compatibility input for callers that already carry active scope identity. When
    /// supplied it must equal the Activity scope; membership is always derived from the
    /// Activity itself.
    /// </summary>
    public DocumentScopeId? TargetScopeId => _state.TargetScopeId;

    internal BpmnBoundaryEventCreationCommandState State => _state;
}

/// <summary>
/// Atomically creates a Signal Boundary Event attached to one existing BPMN Activity.
/// Boundary-relative placement is the sole editable placement authority; Position and
/// Size are derived by the handler from the current Activity bounds.
/// </summary>
public sealed class CreateBpmnSignalBoundaryEventCommand : ICommand
{
    private readonly BpmnBoundaryEventCreationCommandState _state;

    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-signal-boundary-event");

    public CreateBpmnSignalBoundaryEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticElementId attachedToActivityId,
        BoundaryAttachmentSide side,
        double positionOnSide,
        RectD effectiveOwnerBounds,
        string name,
        bool cancelActivity = true,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
    {
        _state = new BpmnBoundaryEventCreationCommandState(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            attachedToActivityId,
            side,
            positionOnSide,
            effectiveOwnerBounds,
            name,
            cancelActivity,
            description,
            targetScopeId);
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId => _state.TargetDocumentId;

    public DocumentRevision ExpectedRevision => _state.ExpectedRevision;

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public SemanticElementId ElementId => _state.ElementId;

    public VisualStateId VisualStateId => _state.VisualStateId;

    public SemanticElementId AttachedToActivityId => _state.AttachedToActivityId;

    public BoundaryAttachmentPlacement BoundaryAttachment => _state.BoundaryAttachment;

    public BoundaryAttachmentSide Side => BoundaryAttachment.Side;

    public double PositionOnSide => BoundaryAttachment.PositionOnSide;

    public RectD EffectiveOwnerBounds => _state.EffectiveOwnerBounds;

    public string Name => _state.Name;

    public bool CancelActivity => _state.CancelActivity;

    public string? Description => _state.Description;

    public DocumentScopeId? TargetScopeId => _state.TargetScopeId;

    internal BpmnBoundaryEventCreationCommandState State => _state;
}

internal sealed class BpmnBoundaryEventCreationCommandState
{
    internal BpmnBoundaryEventCreationCommandState(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticElementId attachedToActivityId,
        BoundaryAttachmentSide side,
        double positionOnSide,
        RectD effectiveOwnerBounds,
        string name,
        bool cancelActivity,
        string? description,
        DocumentScopeId? targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(elementId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(attachedToActivityId);
        ArgumentNullException.ThrowIfNull(name);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ElementId = elementId;
        VisualStateId = visualStateId;
        AttachedToActivityId = attachedToActivityId;
        BoundaryAttachment = new BoundaryAttachmentPlacement(side, positionOnSide);
        EffectiveOwnerBounds = effectiveOwnerBounds;
        Name = name;
        CancelActivity = cancelActivity;
        Description = description;
        TargetScopeId = targetScopeId;
    }

    internal DocumentId TargetDocumentId { get; }

    internal DocumentRevision ExpectedRevision { get; }

    internal SemanticElementId ElementId { get; }

    internal VisualStateId VisualStateId { get; }

    internal SemanticElementId AttachedToActivityId { get; }

    internal BoundaryAttachmentPlacement BoundaryAttachment { get; }

    internal RectD EffectiveOwnerBounds { get; }

    internal string Name { get; }

    internal bool CancelActivity { get; }

    internal string? Description { get; }

    internal DocumentScopeId? TargetScopeId { get; }
}

internal sealed record BpmnBoundaryEventCreationCommandData(
    SemanticTypeId SemanticTypeId,
    BpmnBoundaryEventCreationCommandState State,
    string? TimerDefinition)
{
    internal SemanticElementId ElementId => State.ElementId;

    internal VisualStateId VisualStateId => State.VisualStateId;

    internal SemanticElementId AttachedToActivityId => State.AttachedToActivityId;

    internal BoundaryAttachmentPlacement BoundaryAttachment => State.BoundaryAttachment;

    internal RectD EffectiveOwnerBounds => State.EffectiveOwnerBounds;

    internal string Name => State.Name;

    internal bool CancelActivity => State.CancelActivity;

    internal string? Description => State.Description;

    internal DocumentScopeId? TargetScopeId => State.TargetScopeId;

    internal static bool TryResolve(
        ICommand command,
        out BpmnBoundaryEventCreationCommandData? creation)
    {
        ArgumentNullException.ThrowIfNull(command);
        creation = command switch
        {
            CreateBpmnMessageBoundaryEventCommand message => new(
                BpmnSemanticTypes.MessageBoundaryEvent,
                message.State,
                TimerDefinition: null),
            CreateBpmnTimerBoundaryEventCommand timer => new(
                BpmnSemanticTypes.TimerBoundaryEvent,
                timer.State,
                timer.TimerDefinition),
            CreateBpmnSignalBoundaryEventCommand signal => new(
                BpmnSemanticTypes.SignalBoundaryEvent,
                signal.State,
                TimerDefinition: null),
            _ => null,
        };
        return creation is not null;
    }
}

public sealed class CreateBpmnSignalCatchEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-signal-catch-event");

    public CreateBpmnSignalCatchEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnSignalThrowEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-signal-throw-event");

    public CreateBpmnSignalThrowEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        string name,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnEndEventCommand : BpmnElementCreationCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-end-event");

    public CreateBpmnEndEventCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        PointD position,
        SizeD size,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual,
        string? name = null,
        string? description = null,
        DocumentScopeId? targetScopeId = null)
        : base(
            targetDocumentId,
            expectedRevision,
            elementId,
            visualStateId,
            position,
            size,
            placementMode,
            targetScopeId)
    {
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public string? Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnSequenceFlowCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-sequence-flow");

    public CreateBpmnSequenceFlowCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId,
        IEnumerable<PointD>? route = null,
        string? name = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(targetAnchorId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        VisualStateId = visualStateId;
        SourceId = sourceId;
        TargetId = targetId;
        SourceAnchorId = sourceAnchorId;
        TargetAnchorId = targetAnchorId;
        Route = route?.ToImmutableArray() ?? [];
        Name = name;
        Description = description;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.ConnectorOnly;

    public SemanticElementId RelationshipId { get; }

    public VisualStateId VisualStateId { get; }

    public SemanticElementId SourceId { get; }

    public SemanticElementId TargetId { get; }

    public ConnectorAnchorId SourceAnchorId { get; }

    public ConnectorAnchorId TargetAnchorId { get; }

    public ImmutableArray<PointD> Route { get; }

    public string? Name { get; }

    public string? Description { get; }
}

/// <summary>
/// Atomically creates a BPMN Sequence Flow and the persistent Target anchor required by it.
/// </summary>
public sealed class CreateBpmnSequenceFlowWithTargetAnchorCommand :
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-sequence-flow-with-target-anchor");

    public CreateBpmnSequenceFlowWithTargetAnchorCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId targetAnchorId,
        ConnectorAnchorSide targetSide,
        int targetInsertionIndex,
        IEnumerable<PointD>? route = null,
        string? name = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetAnchorId);
        if (!Enum.IsDefined(targetSide))
        {
            throw new ArgumentOutOfRangeException(nameof(targetSide));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(targetInsertionIndex);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        VisualStateId = visualStateId;
        SourceId = sourceId;
        TargetId = targetId;
        SourceAnchorId = sourceAnchorId;
        TargetVisualStateId = targetVisualStateId;
        TargetAnchorId = targetAnchorId;
        TargetSide = targetSide;
        TargetInsertionIndex = targetInsertionIndex;
        Route = route?.ToImmutableArray() ?? [];
        Name = name;
        Description = description;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public CommandCategory Category => CommandCategory.Document;
    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;
    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.ConnectorOnly;
    public SemanticElementId RelationshipId { get; }
    public VisualStateId VisualStateId { get; }
    public SemanticElementId SourceId { get; }
    public SemanticElementId TargetId { get; }
    public ConnectorAnchorId SourceAnchorId { get; }
    public VisualStateId TargetVisualStateId { get; }
    public ConnectorAnchorId TargetAnchorId { get; }
    public ConnectorAnchorSide TargetSide { get; }
    public int TargetInsertionIndex { get; }
    public ImmutableArray<PointD> Route { get; }
    public string? Name { get; }
    public string? Description { get; }
}
