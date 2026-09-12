using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Profiles;

/// <summary>
/// Stores sparse persistent model configuration for optional profiles. Absence means
/// unavailable, preserving legacy Documents without a migration payload.
/// </summary>
public sealed class ModelProfileStateSnapshot : IEquatable<ModelProfileStateSnapshot>
{
    public ModelProfileStateSnapshot(IEnumerable<ModelProfileId>? availableProfileIds = null)
    {
        var copy = availableProfileIds?.ToArray() ?? [];
        if (Array.Exists(copy, static profileId => profileId is null))
        {
            throw new ArgumentException(
                "The collection cannot contain null profile identities.",
                nameof(availableProfileIds));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1] == copy[index])
            {
                throw new ArgumentException(
                    $"Duplicate model profile identity '{copy[index]}'.",
                    nameof(availableProfileIds));
            }
        }

        AvailableProfileIds = [.. copy];
    }

    public static ModelProfileStateSnapshot Empty { get; } = new();

    public ImmutableArray<ModelProfileId> AvailableProfileIds { get; }

    public bool IsAvailable(ModelProfileId profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return AvailableProfileIds.BinarySearch(
            profileId,
            Comparer<ModelProfileId>.Create(static (left, right) =>
                StringComparer.Ordinal.Compare(left.Value, right.Value))) >= 0;
    }

    public ModelProfileStateSnapshot WithAvailability(ModelProfileId profileId, bool isAvailable)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        var currentlyAvailable = IsAvailable(profileId);
        if (currentlyAvailable == isAvailable)
        {
            return this;
        }

        return new ModelProfileStateSnapshot(isAvailable
            ? AvailableProfileIds.Append(profileId)
            : AvailableProfileIds.Where(existing => existing != profileId));
    }

    public bool Equals(ModelProfileStateSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        AvailableProfileIds.AsSpan().SequenceEqual(other.AvailableProfileIds.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ModelProfileStateSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var profileId in AvailableProfileIds)
        {
            hash.Add(profileId);
        }

        return hash.ToHashCode();
    }
}
