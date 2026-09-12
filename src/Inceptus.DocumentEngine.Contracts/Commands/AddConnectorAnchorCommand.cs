using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one persistent connector-anchor insertion into a Visual State's
/// ordered edge-local collection.
/// </summary>
public sealed class AddConnectorAnchorCommand :
    IEquatable<AddConnectorAnchorCommand>,
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/add-connector-anchor");

    public AddConnectorAnchorCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(anchorId);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(side),
                side,
                "The connector-anchor side must be defined.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "The connector-anchor role must be defined.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(insertionIndex);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        AnchorId = anchorId;
        Side = side;
        Role = role;
        InsertionIndex = insertionIndex;
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

    public ConnectorAnchorSide Side { get; }

    public ConnectorAnchorRole Role { get; }

    public int InsertionIndex { get; }

    public bool Equals(AddConnectorAnchorCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        AnchorId == other.AnchorId &&
        Side == other.Side &&
        Role == other.Role &&
        InsertionIndex == other.InsertionIndex;

    public override bool Equals(object? obj) => Equals(obj as AddConnectorAnchorCommand);

    public override int GetHashCode() => HashCode.Combine(
        TargetDocumentId,
        ExpectedRevision,
        TargetVisualStateId,
        AnchorId,
        Side,
        Role,
        InsertionIndex);

    public static bool operator ==(
        AddConnectorAnchorCommand? left,
        AddConnectorAnchorCommand? right) =>
        EqualityComparer<AddConnectorAnchorCommand>.Default.Equals(left, right);

    public static bool operator !=(
        AddConnectorAnchorCommand? left,
        AddConnectorAnchorCommand? right) =>
        !(left == right);
}
