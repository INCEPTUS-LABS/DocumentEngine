using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>An ordinary spatial History action owns only its changed scoped width.</summary>
public sealed record SpatialScopeWidthHistoryDelta
{
    public SpatialScopeWidthHistoryDelta(DocumentScopeId scopeId, ModelProfileId profileId,
        double beforeWidth, double afterWidth)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(profileId);
        if (!double.IsFinite(beforeWidth) || beforeWidth <= 0d)
            throw new ArgumentOutOfRangeException(nameof(beforeWidth));
        if (!double.IsFinite(afterWidth) || afterWidth <= 0d)
            throw new ArgumentOutOfRangeException(nameof(afterWidth));
        if (beforeWidth == afterWidth)
            throw new ArgumentException("A width delta requires different values.", nameof(afterWidth));
        ScopeId = scopeId;
        ProfileId = profileId;
        BeforeWidth = beforeWidth;
        AfterWidth = afterWidth;
    }

    public DocumentScopeId ScopeId { get; }
    public ModelProfileId ProfileId { get; }
    public double BeforeWidth { get; }
    public double AfterWidth { get; }
}
