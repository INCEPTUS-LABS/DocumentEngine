using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Profiles;

/// <summary>
/// Identifies one model-profile element whose preferred presentation is collapsed.
/// </summary>
public sealed record ModelProfileElementViewStateKey
{
    public ModelProfileElementViewStateKey(
        ModelProfileId profileId,
        SemanticElementId semanticElementId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ProfileId = profileId;
        SemanticElementId = semanticElementId;
    }

    public ModelProfileId ProfileId { get; }

    public SemanticElementId SemanticElementId { get; }
}

/// <summary>
/// Stores sparse, session-only presentation preferences for semantic elements contributed by
/// model profiles. Absence means expanded; only collapsed keys are retained.
/// </summary>
public sealed class ModelProfileElementViewStateSnapshot :
    IEquatable<ModelProfileElementViewStateSnapshot>
{
    public ModelProfileElementViewStateSnapshot(
        IEnumerable<ModelProfileElementViewStateKey>? collapsedElements = null)
    {
        var copy = collapsedElements?.ToArray() ?? [];
        if (Array.Exists(copy, static entry => entry is null))
        {
            throw new ArgumentException(
                "Collapsed profile-element state cannot contain null entries.",
                nameof(collapsedElements));
        }

        Array.Sort(copy, Compare);
        for (var index = 1; index < copy.Length; index++)
        {
            if (Compare(copy[index - 1], copy[index]) == 0)
            {
                throw new ArgumentException(
                    "Collapsed profile-element state cannot contain duplicate keys.",
                    nameof(collapsedElements));
            }
        }

        CollapsedElements = [.. copy];
    }

    public static ModelProfileElementViewStateSnapshot Empty { get; } = new();

    public ImmutableArray<ModelProfileElementViewStateKey> CollapsedElements { get; }

    public bool IsCollapsed(
        ModelProfileId profileId,
        SemanticElementId semanticElementId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        return CollapsedElements.Any(entry =>
            entry.ProfileId == profileId && entry.SemanticElementId == semanticElementId);
    }

    public ModelProfileElementViewStateSnapshot WithCollapsed(
        ModelProfileId profileId,
        SemanticElementId semanticElementId,
        bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        if (IsCollapsed(profileId, semanticElementId) == isCollapsed)
        {
            return this;
        }

        return new ModelProfileElementViewStateSnapshot(isCollapsed
            ? CollapsedElements.Append(
                new ModelProfileElementViewStateKey(profileId, semanticElementId))
            : CollapsedElements.Where(entry =>
                entry.ProfileId != profileId ||
                entry.SemanticElementId != semanticElementId));
    }

    public ModelProfileElementViewStateSnapshot RetainSemanticElements(
        IEnumerable<SemanticElementId> semanticElementIds)
    {
        ArgumentNullException.ThrowIfNull(semanticElementIds);
        var copy = semanticElementIds.ToArray();
        if (Array.Exists(copy, static semanticElementId => semanticElementId is null))
        {
            throw new ArgumentException(
                "Retained semantic-element identities cannot contain null values.",
                nameof(semanticElementIds));
        }

        var retained = copy.ToHashSet();
        var filtered = CollapsedElements
            .Where(entry => retained.Contains(entry.SemanticElementId))
            .ToArray();
        return filtered.Length == CollapsedElements.Length
            ? this
            : new ModelProfileElementViewStateSnapshot(filtered);
    }

    public bool Equals(ModelProfileElementViewStateSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        CollapsedElements.AsSpan().SequenceEqual(other.CollapsedElements.AsSpan());

    public override bool Equals(object? obj) =>
        Equals(obj as ModelProfileElementViewStateSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in CollapsedElements)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    private static int Compare(
        ModelProfileElementViewStateKey left,
        ModelProfileElementViewStateKey right)
    {
        var profile = StringComparer.Ordinal.Compare(
            left.ProfileId.Value,
            right.ProfileId.Value);
        return profile != 0
            ? profile
            : StringComparer.Ordinal.Compare(
                left.SemanticElementId.Value,
                right.SemanticElementId.Value);
    }
}
