using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes removal of one unused persistent connector anchor from a Visual State.
/// </summary>
public sealed class RemoveConnectorAnchorCommand :
    IEquatable<RemoveConnectorAnchorCommand>,
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/remove-connector-anchor");

    public RemoveConnectorAnchorCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(anchorId);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        AnchorId = anchorId;
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

    public ConnectorAnchorId AnchorId { get; }

    public bool Equals(RemoveConnectorAnchorCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        AnchorId == other.AnchorId;

    public override bool Equals(object? obj) => Equals(obj as RemoveConnectorAnchorCommand);

    public override int GetHashCode() => HashCode.Combine(
        TargetDocumentId,
        ExpectedRevision,
        TargetVisualStateId,
        AnchorId);

    public static bool operator ==(
        RemoveConnectorAnchorCommand? left,
        RemoveConnectorAnchorCommand? right) =>
        EqualityComparer<RemoveConnectorAnchorCommand>.Default.Equals(left, right);

    public static bool operator !=(
        RemoveConnectorAnchorCommand? left,
        RemoveConnectorAnchorCommand? right) =>
        !(left == right);
}
