using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable additive Scene items, canonical visual overrides, and an optional single-scope
/// spatial presentation plan returned by one registered contributor.
/// </summary>
public sealed class Canvas2DSceneContribution : IEquatable<Canvas2DSceneContribution>
{
    public Canvas2DSceneContribution(
        IEnumerable<Canvas2DSceneItem>? items = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null,
        IEnumerable<Canvas2DCanonicalSceneItemVisualOverride>?
            canonicalItemVisualOverrides = null,
        Canvas2DSpatialPresentationPlan? spatialPresentationPlan = null)
    {
        Items = CopyAndOrderItems(items);
        Metadata = new PropertyMap(metadata);
        CanonicalItemVisualOverrides = CopyAndOrderVisualOverrides(
            canonicalItemVisualOverrides);
        SpatialPresentationPlan = spatialPresentationPlan;
    }

    public ImmutableArray<Canvas2DSceneItem> Items { get; }

    public PropertyMap Metadata { get; }

    /// <summary>
    /// Gets deterministic visual-only replacements for framework-composed canonical items.
    /// </summary>
    public ImmutableArray<Canvas2DCanonicalSceneItemVisualOverride>
        CanonicalItemVisualOverrides
    { get; }

    /// <summary>
    /// Gets the optional single-scope spatial presentation plan. Only one presentation-stage
    /// contributor may supply this authority for a Scene.
    /// </summary>
    public Canvas2DSpatialPresentationPlan? SpatialPresentationPlan { get; }

    public bool Equals(Canvas2DSceneContribution? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Items.AsSpan().SequenceEqual(other.Items.AsSpan()) &&
        Metadata.Equals(other.Metadata) &&
        CanonicalItemVisualOverrides.AsSpan().SequenceEqual(
            other.CanonicalItemVisualOverrides.AsSpan()) &&
        Equals(SpatialPresentationPlan, other.SpatialPresentationPlan);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneContribution);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        hash.Add(Metadata);
        foreach (var visualOverride in CanonicalItemVisualOverrides)
        {
            hash.Add(visualOverride);
        }

        hash.Add(SpatialPresentationPlan);

        return hash.ToHashCode();
    }

    private static ImmutableArray<Canvas2DSceneItem> CopyAndOrderItems(
        IEnumerable<Canvas2DSceneItem>? items)
    {
        if (items is null)
        {
            return [];
        }

        var copy = items.ToArray();
        if (Array.Exists(copy, static item => item is null))
        {
            throw new ArgumentException(
                "Scene contributions cannot contain null items.",
                nameof(items));
        }

        Array.Sort(copy, CompareItems);
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].Id == copy[index].Id)
            {
                throw new ArgumentException(
                    $"Scene object ID '{copy[index].Id}' occurs more than once in one contribution.",
                    nameof(items));
            }
        }

        return [.. copy];
    }

    private static int CompareItems(Canvas2DSceneItem left, Canvas2DSceneItem right)
    {
        var comparison = left.Layer.CompareTo(right.Layer);
        comparison = comparison != 0 ? comparison : left.ZIndex.CompareTo(right.ZIndex);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value);
    }

    private static ImmutableArray<Canvas2DCanonicalSceneItemVisualOverride>
        CopyAndOrderVisualOverrides(
            IEnumerable<Canvas2DCanonicalSceneItemVisualOverride>? visualOverrides)
    {
        if (visualOverrides is null)
        {
            return [];
        }

        var copy = visualOverrides.ToArray();
        if (Array.Exists(copy, static visualOverride => visualOverride is null))
        {
            throw new ArgumentException(
                "Canonical Scene-item visual overrides cannot contain null values.",
                nameof(visualOverrides));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.TargetSceneObjectId.Value,
                right.TargetSceneObjectId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].TargetSceneObjectId == copy[index].TargetSceneObjectId)
            {
                throw new ArgumentException(
                    $"Canonical Scene object ID '{copy[index].TargetSceneObjectId}' is targeted " +
                    "more than once in one contribution.",
                    nameof(visualOverrides));
            }
        }

        return [.. copy];
    }
}
