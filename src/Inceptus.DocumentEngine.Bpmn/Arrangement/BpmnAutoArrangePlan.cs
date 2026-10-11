using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Arrangement;

/// <summary>An immutable, revision-bound presentation proposal. Creation never modifies its input.</summary>
public sealed class BpmnAutoArrangePlan
{
    internal BpmnAutoArrangePlan(DocumentSnapshot basis, DocumentSnapshot proposal,
        IEnumerable<SpatialRegionHeightIntent> heights, IEnumerable<SpatialScopeWidthIntent> widths,
        IEnumerable<VisualStateId> changed)
    {
        Basis = basis; Proposal = proposal; Heights = [.. heights]; Widths = [.. widths];
        ChangedVisualStateIds = [.. changed.OrderBy(id => id.Value, StringComparer.Ordinal)];
    }
    internal DocumentSnapshot Basis { get; }
    internal DocumentSnapshot Proposal { get; }
    internal ImmutableArray<SpatialRegionHeightIntent> Heights { get; }
    internal ImmutableArray<SpatialScopeWidthIntent> Widths { get; }
    public DocumentId DocumentId => Basis.DocumentId;
    public DocumentRevision ExpectedRevision => Basis.Revision;
    public ImmutableArray<VisualStateId> ChangedVisualStateIds { get; }
    public bool IsNoChange => ChangedVisualStateIds.IsEmpty && Heights.IsEmpty && Widths.IsEmpty;
    public ImmutableDictionary<VisualStateId, PointD> Positions => Proposal.VisualModel.VisualStates
        .Where(visual => ChangedVisualStateIds.Contains(visual.Id)).ToImmutableDictionary(visual => visual.Id, visual => visual.Position);
}
