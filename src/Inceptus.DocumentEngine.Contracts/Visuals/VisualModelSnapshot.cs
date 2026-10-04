using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public sealed class VisualModelSnapshot : IVisualModelView, IEquatable<VisualModelSnapshot>
{
    public VisualModelSnapshot(
        DocumentId documentId,
        DocumentRevision revision,
        IEnumerable<VisualStateSnapshot>? visualStates = null,
        IEnumerable<ModelProfileElementPresentationSnapshot>? profileElementPresentations = null)
        : this(documentId, revision, visualStates, profileElementPresentations, null)
    {
    }

    public VisualModelSnapshot(
        DocumentId documentId,
        DocumentRevision revision,
        IEnumerable<VisualStateSnapshot>? visualStates,
        IEnumerable<ModelProfileElementPresentationSnapshot>? profileElementPresentations,
        IEnumerable<ScopeRoutingSnapshot>? routingScopes)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        DocumentId = documentId;
        Revision = revision;
        VisualStates = CopyAndOrder(visualStates);
        ProfileElementPresentations = CopyAndOrderPresentations(profileElementPresentations);
        RoutingScopes = routingScopes is null ? null : RoutingStateCollection.Unique(
            routingScopes, static scope => scope.ScopeId.Value, nameof(routingScopes));
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

    /// <summary>
    /// Gets complete saved expanded geometry and ordered connector records. Null identifies
    /// fresh in-memory construction awaiting preparation, never a supported native file.
    /// </summary>
    public ImmutableArray<ScopeRoutingSnapshot>? RoutingScopes { get; }

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
         ProfileElementPresentations.AsSpan().SequenceEqual(other.ProfileElementPresentations.AsSpan()) &&
         RoutingScopes.HasValue == other.RoutingScopes.HasValue &&
         RoutingScopes.GetValueOrDefault().AsSpan().SequenceEqual(
             other.RoutingScopes.GetValueOrDefault().AsSpan()));

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

        hash.Add(RoutingScopes.HasValue);
        if (RoutingScopes is { } scopes)
        {
            foreach (var scope in scopes)
            {
                hash.Add(scope);
            }
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
