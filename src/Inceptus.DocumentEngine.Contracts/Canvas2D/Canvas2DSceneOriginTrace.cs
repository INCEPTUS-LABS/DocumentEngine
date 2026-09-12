using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public sealed class Canvas2DSceneOriginTrace : IEquatable<Canvas2DSceneOriginTrace>
{
    private const Canvas2DSceneOriginCategory AllCategories =
        Canvas2DSceneOriginCategory.SemanticElement |
        Canvas2DSceneOriginCategory.VisualState |
        Canvas2DSceneOriginCategory.ProjectedRuntimeObject |
        Canvas2DSceneOriginCategory.EditorState |
        Canvas2DSceneOriginCategory.Configuration |
        Canvas2DSceneOriginCategory.RegisteredExtension;

    public Canvas2DSceneOriginTrace(
        Canvas2DSceneOriginCategory categories,
        SemanticElementId? semanticElementId = null,
        VisualStateId? visualStateId = null,
        ProjectedObjectId? projectedObjectId = null,
        string? stableSourceKey = null,
        IEnumerable<ProjectedObjectId>? relatedProjectedObjectIds = null,
        IEnumerable<SceneObjectId>? relatedSceneObjectIds = null)
    {
        if (categories == Canvas2DSceneOriginCategory.None || (categories & ~AllCategories) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categories), categories, "At least one defined origin category is required.");
        }

        if (stableSourceKey is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stableSourceKey);
        }

        Categories = categories;
        SemanticElementId = semanticElementId;
        VisualStateId = visualStateId;
        ProjectedObjectId = projectedObjectId;
        StableSourceKey = stableSourceKey;
        RelatedProjectedObjectIds = CopyAndOrder(
            relatedProjectedObjectIds,
            static id => id.Value,
            nameof(relatedProjectedObjectIds));
        RelatedSceneObjectIds = CopyAndOrder(
            relatedSceneObjectIds,
            static id => id.Value,
            nameof(relatedSceneObjectIds));

        ValidateConsistency();
    }

    public Canvas2DSceneOriginCategory Categories { get; }

    public SemanticElementId? SemanticElementId { get; }

    public VisualStateId? VisualStateId { get; }

    public ProjectedObjectId? ProjectedObjectId { get; }

    public string? StableSourceKey { get; }

    public ImmutableArray<ProjectedObjectId> RelatedProjectedObjectIds { get; }

    public ImmutableArray<SceneObjectId> RelatedSceneObjectIds { get; }

    public bool Equals(Canvas2DSceneOriginTrace? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Categories == other.Categories &&
        SemanticElementId == other.SemanticElementId &&
        VisualStateId == other.VisualStateId &&
        ProjectedObjectId == other.ProjectedObjectId &&
        StringComparer.Ordinal.Equals(StableSourceKey, other.StableSourceKey) &&
        RelatedProjectedObjectIds.AsSpan().SequenceEqual(other.RelatedProjectedObjectIds.AsSpan()) &&
        RelatedSceneObjectIds.AsSpan().SequenceEqual(other.RelatedSceneObjectIds.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneOriginTrace);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Categories);
        hash.Add(SemanticElementId);
        hash.Add(VisualStateId);
        hash.Add(ProjectedObjectId);
        hash.Add(StableSourceKey, StringComparer.Ordinal);
        foreach (var id in RelatedProjectedObjectIds)
        {
            hash.Add(id);
        }

        foreach (var id in RelatedSceneObjectIds)
        {
            hash.Add(id);
        }

        return hash.ToHashCode();
    }

    private void ValidateConsistency()
    {
        RequireCategory(SemanticElementId is not null, Canvas2DSceneOriginCategory.SemanticElement, nameof(SemanticElementId));
        RequireCategory(VisualStateId is not null, Canvas2DSceneOriginCategory.VisualState, nameof(VisualStateId));
        RequireCategory(
            ProjectedObjectId is not null || !RelatedProjectedObjectIds.IsEmpty,
            Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
            nameof(ProjectedObjectId));

        var runtimeOnlyCategories = Canvas2DSceneOriginCategory.EditorState |
            Canvas2DSceneOriginCategory.Configuration |
            Canvas2DSceneOriginCategory.RegisteredExtension;
        if ((Categories & runtimeOnlyCategories) != 0 && StableSourceKey is null)
        {
            throw new ArgumentException(
                "Editor State, configuration, and extension origins require a stable source key.",
                nameof(StableSourceKey));
        }

        if (ProjectedObjectId is not null && RelatedProjectedObjectIds.Contains(ProjectedObjectId))
        {
            throw new ArgumentException(
                "The primary projected object ID cannot also be a related projected object ID.",
                nameof(RelatedProjectedObjectIds));
        }
    }

    private void RequireCategory(
        bool identityPresent,
        Canvas2DSceneOriginCategory category,
        string identityName)
    {
        var categoryPresent = (Categories & category) != 0;
        if (identityPresent != categoryPresent)
        {
            throw new ArgumentException(
                $"Origin category '{category}' and identity '{identityName}' must be declared together.",
                identityName);
        }
    }

    private static ImmutableArray<T> CopyAndOrder<T>(
        IEnumerable<T>? values,
        Func<T, string> getValue,
        string parameterName)
        where T : class
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException("Related identities cannot contain null values.", parameterName);
        }

        Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(getValue(left), getValue(right)));
        for (var index = 1; index < copy.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(getValue(copy[index - 1]), getValue(copy[index])))
            {
                throw new ArgumentException("Related identities cannot contain duplicates.", parameterName);
            }
        }

        return [.. copy];
    }
}
