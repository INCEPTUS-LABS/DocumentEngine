using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one atomic persistent position replacement for one or more Visual States.
/// </summary>
public sealed class MoveVisualStatesCommand : IEquatable<MoveVisualStatesCommand>, ICommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/move-visual-states");

    public MoveVisualStatesCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        IEnumerable<VisualStateMove> moves)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(moves);

        var ordered = moves
            .Select(move => move ?? throw new ArgumentException(
                "Visual State moves cannot contain null values.",
                nameof(moves)))
            .OrderBy(move => move.VisualStateId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (ordered.IsEmpty)
        {
            throw new ArgumentException(
                "An atomic Visual State move must contain at least one target.",
                nameof(moves));
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].VisualStateId == ordered[index].VisualStateId)
            {
                throw new ArgumentException(
                    $"Duplicate Visual State move target '{ordered[index].VisualStateId}'.",
                    nameof(moves));
            }
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        Moves = ordered;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    public ImmutableArray<VisualStateMove> Moves { get; }

    public bool Equals(MoveVisualStatesCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        Moves.AsSpan().SequenceEqual(other.Moves.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as MoveVisualStatesCommand);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TargetDocumentId);
        hash.Add(ExpectedRevision);
        foreach (var move in Moves)
        {
            hash.Add(move);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(
        MoveVisualStatesCommand? left,
        MoveVisualStatesCommand? right) =>
        EqualityComparer<MoveVisualStatesCommand>.Default.Equals(left, right);

    public static bool operator !=(
        MoveVisualStatesCommand? left,
        MoveVisualStatesCommand? right) =>
        !(left == right);
}

/// <summary>
/// Describes one target in an atomic persistent Visual State move.
/// </summary>
public sealed class VisualStateMove : IEquatable<VisualStateMove>
{
    public VisualStateMove(
        VisualStateId visualStateId,
        PointD targetPosition,
        VisualPlacementMode? requestedPlacementMode = null)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        if (requestedPlacementMode is { } placementMode && !Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedPlacementMode),
                requestedPlacementMode,
                "The requested placement mode must be defined.");
        }

        VisualStateId = visualStateId;
        TargetPosition = targetPosition;
        RequestedPlacementMode = requestedPlacementMode;
    }

    public VisualStateId VisualStateId { get; }

    public PointD TargetPosition { get; }

    /// <summary>
    /// Gets the requested persistent placement mode, or <see langword="null"/> to preserve it.
    /// </summary>
    public VisualPlacementMode? RequestedPlacementMode { get; }

    public bool Equals(VisualStateMove? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        VisualStateId == other.VisualStateId &&
        TargetPosition == other.TargetPosition &&
        RequestedPlacementMode == other.RequestedPlacementMode;

    public override bool Equals(object? obj) => Equals(obj as VisualStateMove);

    public override int GetHashCode() =>
        HashCode.Combine(VisualStateId, TargetPosition, RequestedPlacementMode);

    public static bool operator ==(VisualStateMove? left, VisualStateMove? right) =>
        EqualityComparer<VisualStateMove>.Default.Equals(left, right);

    public static bool operator !=(VisualStateMove? left, VisualStateMove? right) =>
        !(left == right);
}
