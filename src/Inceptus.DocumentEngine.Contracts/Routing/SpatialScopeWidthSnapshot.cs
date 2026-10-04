using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>The single authored outer width of a profile's spatial stack in its enclosing scope.</summary>
public sealed record SpatialScopeWidthSnapshot
{
    public SpatialScopeWidthSnapshot(ModelProfileId profileId, double outerWidth)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        if (!double.IsFinite(outerWidth) || outerWidth <= 0d)
            throw new ArgumentOutOfRangeException(nameof(outerWidth));
        ProfileId = profileId;
        OuterWidth = outerWidth;
    }

    public ModelProfileId ProfileId { get; }
    public double OuterWidth { get; }
}
