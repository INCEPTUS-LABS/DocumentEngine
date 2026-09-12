using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Profiles;

/// <summary>
/// Stores sparse transient view preferences. Absence means preferred visible.
/// Effective visibility additionally requires persistent model availability.
/// </summary>
public sealed class ModelProfileViewStateSnapshot : IEquatable<ModelProfileViewStateSnapshot>
{
    public ModelProfileViewStateSnapshot(IEnumerable<ModelProfileId>? hiddenProfileIds = null)
    {
        var copy = hiddenProfileIds?.ToArray() ?? [];
        if (Array.Exists(copy, static profileId => profileId is null))
        {
            throw new ArgumentException(
                "The collection cannot contain null profile identities.",
                nameof(hiddenProfileIds));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1] == copy[index])
            {
                throw new ArgumentException(
                    $"Duplicate model profile identity '{copy[index]}'.",
                    nameof(hiddenProfileIds));
            }
        }

        HiddenProfileIds = [.. copy];
    }

    public static ModelProfileViewStateSnapshot Empty { get; } = new();

    public ImmutableArray<ModelProfileId> HiddenProfileIds { get; }

    public bool IsPreferredVisible(ModelProfileId profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return !HiddenProfileIds.Any(existing => existing == profileId);
    }

    public bool IsEffectivelyVisible(
        ModelProfileId profileId,
        ModelProfileStateSnapshot modelState)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        return modelState.IsAvailable(profileId) && IsPreferredVisible(profileId);
    }

    public ModelProfileViewStateSnapshot WithPreferredVisibility(
        ModelProfileId profileId,
        bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        if (IsPreferredVisible(profileId) == isVisible)
        {
            return this;
        }

        return new ModelProfileViewStateSnapshot(isVisible
            ? HiddenProfileIds.Where(existing => existing != profileId)
            : HiddenProfileIds.Append(profileId));
    }

    public bool Equals(ModelProfileViewStateSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        HiddenProfileIds.AsSpan().SequenceEqual(other.HiddenProfileIds.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ModelProfileViewStateSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var profileId in HiddenProfileIds)
        {
            hash.Add(profileId);
        }

        return hash.ToHashCode();
    }
}
