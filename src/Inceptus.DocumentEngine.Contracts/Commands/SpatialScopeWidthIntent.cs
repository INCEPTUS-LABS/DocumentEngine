using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>Authorizes one scoped width change without authorizing changes to any region height.</summary>
public sealed record SpatialScopeWidthIntent
{
    public SpatialScopeWidthIntent(DocumentScopeId scopeId, ModelProfileId profileId, double outerWidth)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(profileId);
        if (!double.IsFinite(outerWidth) || outerWidth <= 0d)
            throw new ArgumentOutOfRangeException(nameof(outerWidth));
        ScopeId = scopeId;
        ProfileId = profileId;
        OuterWidth = outerWidth;
    }

    public DocumentScopeId ScopeId { get; }
    public ModelProfileId ProfileId { get; }
    public double OuterWidth { get; }
}
