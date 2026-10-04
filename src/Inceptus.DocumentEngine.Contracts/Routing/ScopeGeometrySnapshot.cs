using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Contracts.Routing;

public sealed record ScopeGeometryContributorSnapshot
{
    public ScopeGeometryContributorSnapshot(
        Canvas2DSceneContributorDescriptor descriptor,
        Canvas2DSceneContributionStage stage)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        Descriptor = descriptor;
        Stage = stage;
    }

    public Canvas2DSceneContributorDescriptor Descriptor { get; }
    public Canvas2DSceneContributionStage Stage { get; }
}

/// <summary>The installed expanded node/frame basis for one semantic scope.</summary>
public sealed class ScopeGeometrySnapshot : IEquatable<ScopeGeometrySnapshot>
{
    public ScopeGeometrySnapshot(
        string policyId,
        string policyVersion,
        AlgorithmId layoutAlgorithmId,
        Canvas2DSceneConfiguration configuration,
        TextMeasurementRequest textConfiguration,
        IEnumerable<ScopeGeometryContributorSnapshot> contributors,
        IEnumerable<ScopeNodeGeometrySnapshot> nodes,
        IEnumerable<SpatialRegionGeometrySnapshot> regions,
        IEnumerable<ScopeNodeCaptionSnapshot> captions)
        : this(policyId, policyVersion, layoutAlgorithmId, configuration, textConfiguration,
            contributors, nodes, regions, captions, [])
    {
    }

    public ScopeGeometrySnapshot(
        string policyId,
        string policyVersion,
        AlgorithmId layoutAlgorithmId,
        Canvas2DSceneConfiguration configuration,
        TextMeasurementRequest textConfiguration,
        IEnumerable<ScopeGeometryContributorSnapshot> contributors,
        IEnumerable<ScopeNodeGeometrySnapshot> nodes,
        IEnumerable<SpatialRegionGeometrySnapshot> regions,
        IEnumerable<ScopeNodeCaptionSnapshot> captions,
        IEnumerable<ScopeTextMeasurementSnapshot> textMeasurements)
        : this(policyId, policyVersion, layoutAlgorithmId, configuration, textConfiguration,
            contributors, nodes, regions, captions, textMeasurements, [])
    {
    }

    public ScopeGeometrySnapshot(
        string policyId,
        string policyVersion,
        AlgorithmId layoutAlgorithmId,
        Canvas2DSceneConfiguration configuration,
        TextMeasurementRequest textConfiguration,
        IEnumerable<ScopeGeometryContributorSnapshot> contributors,
        IEnumerable<ScopeNodeGeometrySnapshot> nodes,
        IEnumerable<SpatialRegionGeometrySnapshot> regions,
        IEnumerable<ScopeNodeCaptionSnapshot> captions,
        IEnumerable<ScopeTextMeasurementSnapshot> textMeasurements,
        IEnumerable<SpatialScopeWidthSnapshot> spatialWidths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentNullException.ThrowIfNull(layoutAlgorithmId);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(textConfiguration);
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        LayoutAlgorithmId = layoutAlgorithmId;
        Configuration = configuration;
        TextConfiguration = textConfiguration;
        Contributors = RoutingStateCollection.Unique(contributors,
            static item => item.Descriptor.ContributorId.Value, nameof(contributors), order: false);
        Nodes = RoutingStateCollection.Unique(nodes, static item => item.VisualStateId.Value, nameof(nodes));
        Regions = RoutingStateCollection.Unique(regions, static item => item.Id.Value, nameof(regions));
        SpatialWidths = RoutingStateCollection.Unique(spatialWidths, static item => item.ProfileId.Value, nameof(spatialWidths));
        Captions = RoutingStateCollection.Unique(captions, static item => item.LabelId.Value, nameof(captions));
        TextMeasurements = RoutingStateCollection.Copy(textMeasurements, nameof(textMeasurements));
        if (TextMeasurements.Select(static item => item.Request).Distinct().Count() != TextMeasurements.Length)
            throw new ArgumentException("Each saved text request must have one measurement.", nameof(textMeasurements));
        var activeRegionIds = Regions.Where(static region => region.IsActive)
            .Select(static region => region.Id).ToHashSet();
        if (Nodes.Any(node => node.RegionId is not null && !activeRegionIds.Contains(node.RegionId)))
            throw new ArgumentException("Node membership must refer to an active saved region.", nameof(nodes));
        var owners = Nodes.Select(static node => node.VisualStateId).ToHashSet();
        if (Captions.Any(caption => !owners.Contains(caption.OwnerVisualStateId)))
            throw new ArgumentException("Saved captions must belong to a saved node.", nameof(captions));
    }

    public string PolicyId { get; }
    public string PolicyVersion { get; }
    public AlgorithmId LayoutAlgorithmId { get; }
    public Canvas2DSceneConfiguration Configuration { get; }
    public TextMeasurementRequest TextConfiguration { get; }
    public ImmutableArray<ScopeGeometryContributorSnapshot> Contributors { get; }
    public ImmutableArray<ScopeNodeGeometrySnapshot> Nodes { get; }
    public ImmutableArray<SpatialRegionGeometrySnapshot> Regions { get; }
    public ImmutableArray<SpatialScopeWidthSnapshot> SpatialWidths { get; }
    public ImmutableArray<ScopeNodeCaptionSnapshot> Captions { get; }
    public ImmutableArray<ScopeTextMeasurementSnapshot> TextMeasurements { get; }

    public bool Equals(ScopeGeometrySnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null && StringComparer.Ordinal.Equals(PolicyId, other.PolicyId) &&
        StringComparer.Ordinal.Equals(PolicyVersion, other.PolicyVersion) &&
        LayoutAlgorithmId == other.LayoutAlgorithmId && Configuration.Equals(other.Configuration) &&
        TextConfiguration.Equals(other.TextConfiguration) &&
        Contributors.AsSpan().SequenceEqual(other.Contributors.AsSpan()) &&
        Nodes.AsSpan().SequenceEqual(other.Nodes.AsSpan()) &&
        Regions.AsSpan().SequenceEqual(other.Regions.AsSpan()) &&
        SpatialWidths.AsSpan().SequenceEqual(other.SpatialWidths.AsSpan()) &&
        Captions.AsSpan().SequenceEqual(other.Captions.AsSpan()) &&
        TextMeasurements.AsSpan().SequenceEqual(other.TextMeasurements.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ScopeGeometrySnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PolicyId, StringComparer.Ordinal);
        hash.Add(PolicyVersion, StringComparer.Ordinal);
        hash.Add(LayoutAlgorithmId);
        hash.Add(Configuration);
        hash.Add(TextConfiguration);
        foreach (var item in Contributors) hash.Add(item);
        foreach (var item in Nodes) hash.Add(item);
        foreach (var item in Regions) hash.Add(item);
        foreach (var item in SpatialWidths) hash.Add(item);
        foreach (var item in Captions) hash.Add(item);
        foreach (var item in TextMeasurements) hash.Add(item);
        return hash.ToHashCode();
    }
}
