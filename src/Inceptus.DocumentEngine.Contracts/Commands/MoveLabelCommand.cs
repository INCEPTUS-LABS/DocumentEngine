using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one persistent route-relative label-placement update for a Visual State.
/// </summary>
public sealed class MoveLabelCommand :
    IEquatable<MoveLabelCommand>,
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/move-label");

    public MoveLabelCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        ConnectorLabelPlacement? targetPlacement)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        TargetPlacement = targetPlacement;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.ConnectorOnly;

    public VisualStateId TargetVisualStateId { get; }

    /// <summary>
    /// Gets the explicit placement to store. Null removes explicit placement and restores
    /// the automatic connector-label default.
    /// </summary>
    public ConnectorLabelPlacement? TargetPlacement { get; }

    public bool Equals(MoveLabelCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        TargetPlacement == other.TargetPlacement;

    public override bool Equals(object? obj) => Equals(obj as MoveLabelCommand);

    public override int GetHashCode() => HashCode.Combine(
        TargetDocumentId,
        ExpectedRevision,
        TargetVisualStateId,
        TargetPlacement);

    public static bool operator ==(MoveLabelCommand? left, MoveLabelCommand? right) =>
        EqualityComparer<MoveLabelCommand>.Default.Equals(left, right);

    public static bool operator !=(MoveLabelCommand? left, MoveLabelCommand? right) =>
        !(left == right);
}
