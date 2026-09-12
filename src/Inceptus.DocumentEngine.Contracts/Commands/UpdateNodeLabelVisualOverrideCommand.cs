using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one atomic persistent update of a node-owned label's manual visual box.
/// </summary>
public sealed class UpdateNodeLabelVisualOverrideCommand :
    IEquatable<UpdateNodeLabelVisualOverrideCommand>,
    ICommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/update-node-label-visual-override");

    public UpdateNodeLabelVisualOverrideCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        NodeLabelVisualOverride? targetOverride)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        TargetOverride = targetOverride;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    public VisualStateId TargetVisualStateId { get; }

    /// <summary>
    /// Gets the complete manual visual override to store. Null removes the override
    /// and restores notation-derived automatic placement.
    /// </summary>
    public NodeLabelVisualOverride? TargetOverride { get; }

    public bool Equals(UpdateNodeLabelVisualOverrideCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        TargetOverride == other.TargetOverride;

    public override bool Equals(object? obj) =>
        Equals(obj as UpdateNodeLabelVisualOverrideCommand);

    public override int GetHashCode() => HashCode.Combine(
        TargetDocumentId,
        ExpectedRevision,
        TargetVisualStateId,
        TargetOverride);

    public static bool operator ==(
        UpdateNodeLabelVisualOverrideCommand? left,
        UpdateNodeLabelVisualOverrideCommand? right) =>
        EqualityComparer<UpdateNodeLabelVisualOverrideCommand>.Default.Equals(left, right);

    public static bool operator !=(
        UpdateNodeLabelVisualOverrideCommand? left,
        UpdateNodeLabelVisualOverrideCommand? right) =>
        !(left == right);
}
