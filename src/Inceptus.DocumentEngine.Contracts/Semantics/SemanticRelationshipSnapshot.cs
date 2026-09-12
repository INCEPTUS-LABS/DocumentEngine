using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Semantics;

public sealed class SemanticRelationshipSnapshot : IEquatable<SemanticRelationshipSnapshot>
{
    public SemanticRelationshipSnapshot(
        SemanticElementId id,
        SemanticTypeId typeId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        IEnumerable<KeyValuePair<string, PropertyValue>>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);

        Id = id;
        TypeId = typeId;
        SourceId = sourceId;
        TargetId = targetId;
        Properties = new PropertyMap(properties);
    }

    public SemanticElementId Id { get; }

    public SemanticTypeId TypeId { get; }

    public SemanticElementId SourceId { get; }

    public SemanticElementId TargetId { get; }

    public PropertyMap Properties { get; }

    public bool Equals(SemanticRelationshipSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        TypeId == other.TypeId &&
        SourceId == other.SourceId &&
        TargetId == other.TargetId &&
        Properties.Equals(other.Properties);

    public override bool Equals(object? obj) => Equals(obj as SemanticRelationshipSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(Id, TypeId, SourceId, TargetId, Properties);

    public static bool operator ==(
        SemanticRelationshipSnapshot? left,
        SemanticRelationshipSnapshot? right) =>
        EqualityComparer<SemanticRelationshipSnapshot>.Default.Equals(left, right);

    public static bool operator !=(
        SemanticRelationshipSnapshot? left,
        SemanticRelationshipSnapshot? right) =>
        !(left == right);
}
