using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes one persistent replacement of an existing typed property on a Semantic element
/// or relationship.
/// </summary>
public sealed class UpdateSemanticElementPropertyCommand :
    IEquatable<UpdateSemanticElementPropertyCommand>,
    ICommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/update-semantic-element-property");

    public UpdateSemanticElementPropertyCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId targetSemanticElementId,
        string propertyKey,
        PropertyValue targetValue)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);
        ArgumentNullException.ThrowIfNull(targetValue);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetSemanticElementId = targetSemanticElementId;
        PropertyKey = propertyKey;
        TargetValue = targetValue;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Semantic;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public SemanticElementId TargetSemanticElementId { get; }

    /// <summary>
    /// Gets the domain-owned key of the existing typed property to replace.
    /// </summary>
    public string PropertyKey { get; }

    public PropertyValue TargetValue { get; }

    public bool Equals(UpdateSemanticElementPropertyCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetSemanticElementId == other.TargetSemanticElementId &&
        string.Equals(PropertyKey, other.PropertyKey, StringComparison.Ordinal) &&
        TargetValue.Equals(other.TargetValue);

    public override bool Equals(object? obj) =>
        Equals(obj as UpdateSemanticElementPropertyCommand);

    public override int GetHashCode() =>
        HashCode.Combine(
            TargetDocumentId,
            ExpectedRevision,
            TargetSemanticElementId,
            StringComparer.Ordinal.GetHashCode(PropertyKey),
            TargetValue);

    public static bool operator ==(
        UpdateSemanticElementPropertyCommand? left,
        UpdateSemanticElementPropertyCommand? right) =>
        EqualityComparer<UpdateSemanticElementPropertyCommand>.Default.Equals(left, right);

    public static bool operator !=(
        UpdateSemanticElementPropertyCommand? left,
        UpdateSemanticElementPropertyCommand? right) =>
        !(left == right);
}
