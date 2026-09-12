using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Properties;

/// <summary>
/// Identifies the generic editor used to present one scalar Semantic property.
/// </summary>
#pragma warning disable CA1720 // The approved editor vocabulary distinguishes integer input.
public enum ElementPropertyEditorKind
{
    SingleLineText,
    Integer,
    MultilineText,
    Boolean,
}
#pragma warning restore CA1720

/// <summary>
/// Selects one established generic Semantic-property mutation path.
/// </summary>
public enum SemanticPropertyMutationKind
{
    Name,
    Property,
}

/// <summary>
/// Immutable, data-only presentation metadata for one field in an element
/// Properties schema.
/// </summary>
public sealed class ElementPropertyFieldDefinition :
    IEquatable<ElementPropertyFieldDefinition>
{
    public ElementPropertyFieldDefinition(
        ElementPropertyFieldId fieldId,
        string displayName,
        string semanticPropertyKey,
        ElementPropertyEditorKind editorKind,
        SemanticPropertyMutationKind mutationKind,
        bool isEditable,
        int order)
    {
        ArgumentNullException.ThrowIfNull(fieldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticPropertyKey);
        if (!Enum.IsDefined(editorKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(editorKind),
                editorKind,
                "The element-property editor kind must be defined.");
        }

        if (!Enum.IsDefined(mutationKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mutationKind),
                mutationKind,
                "The Semantic-property mutation kind must be defined.");
        }

        if (mutationKind == SemanticPropertyMutationKind.Name &&
            editorKind != ElementPropertyEditorKind.SingleLineText)
        {
            throw new ArgumentException(
                "The generic Semantic Name mutation requires a single-line text editor.",
                nameof(mutationKind));
        }

        FieldId = fieldId;
        DisplayName = displayName;
        SemanticPropertyKey = semanticPropertyKey;
        EditorKind = editorKind;
        MutationKind = mutationKind;
        IsEditable = isEditable;
        Order = order;
    }

    public ElementPropertyFieldId FieldId { get; }

    public string DisplayName { get; }

    public string SemanticPropertyKey { get; }

    public ElementPropertyEditorKind EditorKind { get; }

    public SemanticPropertyMutationKind MutationKind { get; }

    public bool IsEditable { get; }

    public int Order { get; }

    public bool Equals(ElementPropertyFieldDefinition? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        FieldId == other.FieldId &&
        StringComparer.Ordinal.Equals(DisplayName, other.DisplayName) &&
        StringComparer.Ordinal.Equals(SemanticPropertyKey, other.SemanticPropertyKey) &&
        EditorKind == other.EditorKind &&
        MutationKind == other.MutationKind &&
        IsEditable == other.IsEditable &&
        Order == other.Order;

    public override bool Equals(object? obj) =>
        Equals(obj as ElementPropertyFieldDefinition);

    public override int GetHashCode() => HashCode.Combine(
        FieldId,
        StringComparer.Ordinal.GetHashCode(DisplayName),
        StringComparer.Ordinal.GetHashCode(SemanticPropertyKey),
        EditorKind,
        MutationKind,
        IsEditable,
        Order);
}
