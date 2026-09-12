namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Declares the stable identity and version of one deterministic scene contributor.
/// </summary>
public sealed class Canvas2DSceneContributorDescriptor :
    IEquatable<Canvas2DSceneContributorDescriptor>
{
    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version)
    {
        ArgumentNullException.ThrowIfNull(contributorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        ContributorId = contributorId;
        Version = version;
    }

    public Canvas2DSceneContributorId ContributorId { get; }

    public string Version { get; }

    public bool Equals(Canvas2DSceneContributorDescriptor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ContributorId == other.ContributorId &&
        StringComparer.Ordinal.Equals(Version, other.Version);

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DSceneContributorDescriptor);

    public override int GetHashCode() => HashCode.Combine(
        ContributorId,
        StringComparer.Ordinal.GetHashCode(Version));
}
