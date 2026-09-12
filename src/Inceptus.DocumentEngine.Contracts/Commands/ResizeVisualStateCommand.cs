using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one persistent Visual Model bounds replacement in document coordinates.
/// </summary>
public sealed class ResizeVisualStateCommand : IEquatable<ResizeVisualStateCommand>, ICommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/resize-visual-state");

    public ResizeVisualStateCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        RectD targetBounds,
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
        TargetBounds = targetBounds;
        RequestedPlacementMode = requestedPlacementMode;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    public VisualStateId TargetVisualStateId { get; }

    public RectD TargetBounds { get; }

    /// <summary>
    /// Gets the requested persistent placement mode, or <see langword="null"/> to preserve it.
    /// </summary>
    public VisualPlacementMode? RequestedPlacementMode { get; }

    public bool Equals(ResizeVisualStateCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        TargetBounds == other.TargetBounds &&
        RequestedPlacementMode == other.RequestedPlacementMode;

    public override bool Equals(object? obj) => Equals(obj as ResizeVisualStateCommand);

    public override int GetHashCode() =>
        HashCode.Combine(
            TargetDocumentId,
            ExpectedRevision,
            TargetVisualStateId,
            TargetBounds,
            RequestedPlacementMode);

    public static bool operator ==(
        ResizeVisualStateCommand? left,
        ResizeVisualStateCommand? right) =>
        EqualityComparer<ResizeVisualStateCommand>.Default.Equals(left, right);

    public static bool operator !=(
        ResizeVisualStateCommand? left,
        ResizeVisualStateCommand? right) =>
        !(left == right);
}
