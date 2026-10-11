using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Presentation;

/// <summary>Notation-resolved membership for one exact document revision.</summary>
public sealed class PresentationViewPlan : IEquatable<PresentationViewPlan>
{
    public PresentationViewPlan(DocumentId documentId, DocumentRevision sourceRevision,
        PresentationViewContext context, IEnumerable<SemanticElementId> elementIds,
        IEnumerable<SemanticElementId> relationshipIds,
        IEnumerable<PresentationScopePlacement>? scopePlacements = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(elementIds);
        ArgumentNullException.ThrowIfNull(relationshipIds);
        DocumentId = documentId;
        SourceRevision = sourceRevision;
        Context = context;
        ElementIds = CopyIdentities(elementIds, nameof(elementIds));
        RelationshipIds = CopyIdentities(relationshipIds, nameof(relationshipIds));
        var placements = scopePlacements?.ToArray() ?? [];
        if (Array.Exists(placements, static placement => placement is null) ||
            placements.Select(static placement => placement.ScopeId).Distinct().Count() != placements.Length)
            throw new ArgumentException("Each process scope must have exactly one view placement.", nameof(scopePlacements));
        ScopePlacements = [.. placements.OrderBy(static placement => placement.ScopeId.Value, StringComparer.Ordinal)];
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public PresentationViewContext Context { get; }

    public ImmutableArray<SemanticElementId> ElementIds { get; }

    public ImmutableArray<SemanticElementId> RelationshipIds { get; }

    public ImmutableArray<PresentationScopePlacement> ScopePlacements { get; }

    public bool Contains(SemanticElementId id) => ElementIds.Contains(id) || RelationshipIds.Contains(id);

    public bool Equals(PresentationViewPlan? other) => ReferenceEquals(this, other) || other is not null &&
        DocumentId == other.DocumentId && SourceRevision == other.SourceRevision && Context == other.Context &&
        ElementIds.SequenceEqual(other.ElementIds) && RelationshipIds.SequenceEqual(other.RelationshipIds) &&
        ScopePlacements.SequenceEqual(other.ScopePlacements);

    public override bool Equals(object? obj) => Equals(obj as PresentationViewPlan);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(Context);
        foreach (var id in ElementIds) hash.Add(id);
        foreach (var id in RelationshipIds) hash.Add(id);
        foreach (var placement in ScopePlacements) hash.Add(placement);
        return hash.ToHashCode();
    }

    private static ImmutableArray<SemanticElementId> CopyIdentities(IEnumerable<SemanticElementId> source, string parameter)
    {
        var values = source.ToArray();
        if (Array.Exists(values, static value => value is null) || values.Distinct().Count() != values.Length)
            throw new ArgumentException("Presentation identities must be non-null and unique.", parameter);
        return [.. values.OrderBy(static value => value.Value, StringComparer.Ordinal)];
    }
}
