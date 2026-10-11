using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Organizational.Projection;

/// <summary>Projects region-backed endpoint identity and anchors without owning a second rectangle.</summary>
public static class OrganizationalContainerProjection
{
    public static ProjectionRuleId PoolRuleId { get; } = new("inceptus:organizational/projection/pool-container");
    public static Canvas2DSpatialRegionId PoolRegionId(SemanticElementId id) => new($"inceptus:organizational/pool-region:{id.Value}");
    public static ProjectionRuleResult Project(ElementProjectionRuleInput input, ProjectionRuleId rule,
        IEnumerable<SemanticElementId> pools, bool sharedHeader)
    {
        ArgumentNullException.ThrowIfNull(input);
        var visual = input.VisualStates.SingleOrDefault();
        var envelope = new ProjectedRegionEnvelope(pools.Select(PoolRegionId), sharedHeader ? 76d : 38d, sharedHeader ? 38d : null);
        ProjectionSourceTrace Trace(string key) => new(input.DocumentId, rule, input.SourceKind, input.SemanticId, input.SemanticTypeId, key, visual?.Id);
        var node = new ProjectedNode(Trace("node"), visual is null ? null : new(default, default, VisualPlacementMode.Derived),
            input.Element.Properties, [Canvas2DNodePresentationMetadata.BoundaryContainer], null, null, null,
            NodeGeometryInteractionPolicy.DerivedRegion, envelope);
        var anchors = visual is null ? [] : ElementConnectorAnchorResolver.Resolve(visual, input.SemanticTypeId);
        return ProjectionRuleResult.Success(new ProjectionRuleContribution(nodes: [node], ports: anchors.Select(anchor =>
            new ProjectedPort(Trace($"connector-anchor:{anchor.Id.Value}"), node.Id,
                routingHints: ProjectedConnectorAnchorMetadata.Encode(ProjectedConnectorAnchor.FromResolved(anchor,
                    anchors.Count(candidate => candidate.Side == anchor.Side)))))));
    }
}
