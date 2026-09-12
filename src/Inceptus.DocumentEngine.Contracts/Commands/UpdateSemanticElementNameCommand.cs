using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one persistent replacement of an existing text property that represents
/// a Semantic element's name.
/// </summary>
public sealed class UpdateSemanticElementNameCommand :
    IEquatable<UpdateSemanticElementNameCommand>,
    ICommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/update-semantic-element-name");

    public UpdateSemanticElementNameCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId targetSemanticElementId,
        string namePropertyKey,
        string targetName)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePropertyKey);
        ArgumentNullException.ThrowIfNull(targetName);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetSemanticElementId = targetSemanticElementId;
        NamePropertyKey = namePropertyKey;
        TargetName = targetName;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Semantic;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public SemanticElementId TargetSemanticElementId { get; }

    /// <summary>
    /// Gets the domain-owned property key whose existing text value represents the name.
    /// </summary>
    public string NamePropertyKey { get; }

    public string TargetName { get; }

    public bool Equals(UpdateSemanticElementNameCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetSemanticElementId == other.TargetSemanticElementId &&
        string.Equals(NamePropertyKey, other.NamePropertyKey, StringComparison.Ordinal) &&
        string.Equals(TargetName, other.TargetName, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        Equals(obj as UpdateSemanticElementNameCommand);

    public override int GetHashCode() =>
        HashCode.Combine(
            TargetDocumentId,
            ExpectedRevision,
            TargetSemanticElementId,
            StringComparer.Ordinal.GetHashCode(NamePropertyKey),
            StringComparer.Ordinal.GetHashCode(TargetName));

    public static bool operator ==(
        UpdateSemanticElementNameCommand? left,
        UpdateSemanticElementNameCommand? right) =>
        EqualityComparer<UpdateSemanticElementNameCommand>.Default.Equals(left, right);

    public static bool operator !=(
        UpdateSemanticElementNameCommand? left,
        UpdateSemanticElementNameCommand? right) =>
        !(left == right);
}
