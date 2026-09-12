using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.EditorState;

/// <summary>
/// Captures transient interaction state independently of the authoritative Document.
/// </summary>
public sealed class EditorStateSnapshot : IEquatable<EditorStateSnapshot>
{
    public EditorStateSnapshot(
        IEnumerable<VisualStateId>? selection = null,
        SceneObjectId? hoveredObjectId = null,
        string? activeToolId = null,
        string? focusTargetId = null,
        ViewportSnapshot? viewport = null,
        EditorGestureSnapshot? activeGesture = null,
        IEnumerable<EditorFeedbackSnapshot>? temporaryFeedback = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? toolState = null,
        SemanticElementId? semanticSceneSelection = null)
    {
        Selection = CopySelection(selection);
        if (semanticSceneSelection is not null && !Selection.IsEmpty)
        {
            throw new ArgumentException(
                "Visual State selection and semantic-only Scene selection cannot coexist.",
                nameof(semanticSceneSelection));
        }

        SemanticSceneSelection = semanticSceneSelection;
        HoveredObjectId = hoveredObjectId;
        ActiveToolId = ValidateOptionalIdentity(activeToolId, nameof(activeToolId));
        FocusTargetId = ValidateOptionalIdentity(focusTargetId, nameof(focusTargetId));
        Viewport = viewport ?? ViewportSnapshot.Default;
        ActiveGesture = activeGesture;
        TemporaryFeedback = CopyFeedback(temporaryFeedback);
        ToolState = new PropertyMap(toolState);
    }

    public static EditorStateSnapshot Empty { get; } = new();

    /// <summary>
    /// Canonically ordered logical selection targets.
    /// </summary>
    public ImmutableArray<VisualStateId> Selection { get; }

    /// <summary>
    /// Gets the single transient semantic target selected from an interactive Scene item that
    /// intentionally has no Visual State. It is mutually exclusive with <see cref="Selection"/>.
    /// </summary>
    public SemanticElementId? SemanticSceneSelection { get; }

    public SceneObjectId? HoveredObjectId { get; }

    public string? ActiveToolId { get; }

    public string? FocusTargetId { get; }

    public ViewportSnapshot Viewport { get; }

    public EditorGestureSnapshot? ActiveGesture { get; }

    public ImmutableArray<EditorFeedbackSnapshot> TemporaryFeedback { get; }

    public PropertyMap ToolState { get; }

    public bool Equals(EditorStateSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Selection.AsSpan().SequenceEqual(other.Selection.AsSpan()) &&
        SemanticSceneSelection == other.SemanticSceneSelection &&
        HoveredObjectId == other.HoveredObjectId &&
        StringComparer.Ordinal.Equals(ActiveToolId, other.ActiveToolId) &&
        StringComparer.Ordinal.Equals(FocusTargetId, other.FocusTargetId) &&
        Viewport.Equals(other.Viewport) &&
        Equals(ActiveGesture, other.ActiveGesture) &&
        TemporaryFeedback.AsSpan().SequenceEqual(other.TemporaryFeedback.AsSpan()) &&
        ToolState.Equals(other.ToolState);

    public override bool Equals(object? obj) => Equals(obj as EditorStateSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var selected in Selection)
        {
            hash.Add(selected);
        }

        hash.Add(SemanticSceneSelection);

        hash.Add(HoveredObjectId);
        hash.Add(ActiveToolId, StringComparer.Ordinal);
        hash.Add(FocusTargetId, StringComparer.Ordinal);
        hash.Add(Viewport);
        hash.Add(ActiveGesture);
        foreach (var feedback in TemporaryFeedback)
        {
            hash.Add(feedback);
        }

        hash.Add(ToolState);
        return hash.ToHashCode();
    }

    private static ImmutableArray<VisualStateId> CopySelection(
        IEnumerable<VisualStateId>? selection)
    {
        if (selection is null)
        {
            return [];
        }

        var copy = selection.ToArray();
        if (Array.Exists(copy, static item => item is null))
        {
            throw new ArgumentException("Selection cannot contain null values.", nameof(selection));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1] == copy[index])
            {
                throw new ArgumentException(
                    $"Selection contains duplicate Visual State ID '{copy[index]}'.",
                    nameof(selection));
            }
        }

        return [.. copy];
    }

    private static ImmutableArray<EditorFeedbackSnapshot> CopyFeedback(
        IEnumerable<EditorFeedbackSnapshot>? feedback)
    {
        if (feedback is null)
        {
            return [];
        }

        var copy = feedback.ToArray();
        if (Array.Exists(copy, static item => item is null))
        {
            throw new ArgumentException(
                "Temporary feedback cannot contain null values.",
                nameof(feedback));
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in copy)
        {
            if (!identities.Add(item.Id))
            {
                throw new ArgumentException(
                    $"Temporary feedback contains duplicate ID '{item.Id}'.",
                    nameof(feedback));
            }
        }

        return [.. copy];
    }

    private static string? ValidateOptionalIdentity(string? value, string parameterName)
    {
        if (value is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        }

        return value;
    }
}
