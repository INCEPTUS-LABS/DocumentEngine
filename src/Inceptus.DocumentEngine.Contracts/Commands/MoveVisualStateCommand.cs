using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed class MoveVisualStateCommand : IEquatable<MoveVisualStateCommand>, ICommand
{
    /// <summary>
    /// Gets the stable type identity used to register validators before a request exists.
    /// </summary>
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/move-visual-state");

    public MoveVisualStateCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        PointD targetPosition,
        VisualPlacementMode? requestedPlacementMode = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);

        if (requestedPlacementMode is { } placementMode && !Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedPlacementMode),
                requestedPlacementMode,
                "The requested placement mode must be defined.");
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        TargetPosition = targetPosition;
        RequestedPlacementMode = requestedPlacementMode;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    public VisualStateId TargetVisualStateId { get; }

    public PointD TargetPosition { get; }

    /// <summary>
    /// Gets the requested persistent placement mode, or <see langword="null"/> to preserve it.
    /// </summary>
    public VisualPlacementMode? RequestedPlacementMode { get; }

    public bool Equals(MoveVisualStateCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        TargetPosition == other.TargetPosition &&
        RequestedPlacementMode == other.RequestedPlacementMode;

    public override bool Equals(object? obj) => Equals(obj as MoveVisualStateCommand);

    public override int GetHashCode() =>
        HashCode.Combine(
            TargetDocumentId,
            ExpectedRevision,
            TargetVisualStateId,
            TargetPosition,
            RequestedPlacementMode);

    public static bool operator ==(MoveVisualStateCommand? left, MoveVisualStateCommand? right) =>
        EqualityComparer<MoveVisualStateCommand>.Default.Equals(left, right);

    public static bool operator !=(MoveVisualStateCommand? left, MoveVisualStateCommand? right) =>
        !(left == right);
}
