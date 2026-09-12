using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Semantics;

public sealed class SemanticElementSnapshot : IEquatable<SemanticElementSnapshot>
{
    public SemanticElementSnapshot(
        SemanticElementId id,
        SemanticTypeId typeId,
        IEnumerable<KeyValuePair<string, PropertyValue>>? properties = null,
        SemanticElementId? attachedToElementId = null,
        SemanticElementContainmentKind containmentKind = SemanticElementContainmentKind.Scope)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(typeId);

        Id = id;
        TypeId = typeId;
        Properties = new PropertyMap(properties);
        AttachedToElementId = attachedToElementId;
        if (!Enum.IsDefined(containmentKind))
        {
            throw new ArgumentOutOfRangeException(nameof(containmentKind));
        }

        ContainmentKind = containmentKind;
    }

    public SemanticElementId Id { get; }

    public SemanticTypeId TypeId { get; }

    public PropertyMap Properties { get; }

    /// <summary>
    /// Gets the optional structural owner to whose node boundary this element is attached.
    /// This is semantic structure and is not an editable free-form property.
    /// </summary>
    public SemanticElementId? AttachedToElementId { get; }

    /// <summary>
    /// Gets the element's authoritative containment. The default preserves legacy
    /// scope-contained semantics.
    /// </summary>
    public SemanticElementContainmentKind ContainmentKind { get; }

    public bool Equals(SemanticElementSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        TypeId == other.TypeId &&
        Properties.Equals(other.Properties) &&
        AttachedToElementId == other.AttachedToElementId &&
        ContainmentKind == other.ContainmentKind;

    public override bool Equals(object? obj) => Equals(obj as SemanticElementSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(Id, TypeId, Properties, AttachedToElementId, ContainmentKind);

    public static bool operator ==(SemanticElementSnapshot? left, SemanticElementSnapshot? right) =>
        EqualityComparer<SemanticElementSnapshot>.Default.Equals(left, right);

    public static bool operator !=(SemanticElementSnapshot? left, SemanticElementSnapshot? right) =>
        !(left == right);
}
