using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public sealed class VisualModelSnapshot : IVisualModelView, IEquatable<VisualModelSnapshot>
{
    public VisualModelSnapshot(
        DocumentId documentId,
        DocumentRevision revision,
        IEnumerable<VisualStateSnapshot>? visualStates = null,
        IEnumerable<ModelProfileElementPresentationSnapshot>? profileElementPresentations = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        DocumentId = documentId;
        Revision = revision;
        VisualStates = CopyAndOrder(visualStates);
        ProfileElementPresentations = CopyAndOrderPresentations(profileElementPresentations);
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision Revision { get; }

    public int Count => VisualStates.Length;

    public ImmutableArray<VisualStateSnapshot> VisualStates { get; }

    /// <summary>
    /// Gets geometry-free persistent profile presentation records. These records do
    /// not create Visual States or contribute to <see cref="Count"/>.
    /// </summary>
    public ImmutableArray<ModelProfileElementPresentationSnapshot> ProfileElementPresentations { get; }

    public bool TryGetVisualState(VisualStateId id, out VisualStateSnapshot? visualState)
    {
        ArgumentNullException.ThrowIfNull(id);

        foreach (var candidate in VisualStates)
        {
            if (candidate.Id == id)
            {
                visualState = candidate;
                return true;
            }
        }

        visualState = null;
        return false;
    }

    public bool Equals(VisualModelSnapshot? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
         DocumentId == other.DocumentId &&
         Revision == other.Revision &&
         VisualStates.AsSpan().SequenceEqual(other.VisualStates.AsSpan()) &&
         ProfileElementPresentations.AsSpan().SequenceEqual(other.ProfileElementPresentations.AsSpan()));

    public override bool Equals(object? obj) => Equals(obj as VisualModelSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(Revision);

        foreach (var visualState in VisualStates)
        {
            hash.Add(visualState);
        }

        foreach (var presentation in ProfileElementPresentations)
        {
            hash.Add(presentation);
        }

        return hash.ToHashCode();
    }

    private static ImmutableArray<ModelProfileElementPresentationSnapshot> CopyAndOrderPresentations(
        IEnumerable<ModelProfileElementPresentationSnapshot>? profileElementPresentations)
    {
        var copy = profileElementPresentations?.ToArray() ?? [];
        if (Array.Exists(copy, static presentation => presentation is null))
        {
            throw new ArgumentException(
                "Profile presentation records cannot contain null values.",
                nameof(profileElementPresentations));
        }

        Array.Sort(copy, static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(
                left.ProfileId.Value,
                right.ProfileId.Value);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(
                left.SemanticElementId.Value,
                right.SemanticElementId.Value);
        });
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ProfileId == copy[index].ProfileId &&
                copy[index - 1].SemanticElementId == copy[index].SemanticElementId)
            {
                throw new ArgumentException(
                    "A semantic element cannot have multiple presentation records in one profile.",
                    nameof(profileElementPresentations));
            }
        }

        return [.. copy];
    }

    private static ImmutableArray<VisualStateSnapshot> CopyAndOrder(
        IEnumerable<VisualStateSnapshot>? visualStates)
    {
        if (visualStates is null)
        {
            return [];
        }

        var ordered = visualStates
            .Select(visualState => visualState ??
                throw new ArgumentException("Visual states cannot contain null values.", nameof(visualStates)))
            .OrderBy(visualState => visualState.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].Id == ordered[index].Id)
            {
                throw new ArgumentException(
                    $"Duplicate visual state ID '{ordered[index].Id}'.",
                    nameof(visualStates));
            }
        }

        return ordered;
    }
}
