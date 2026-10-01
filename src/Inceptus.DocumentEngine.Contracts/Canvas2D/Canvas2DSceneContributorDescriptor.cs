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
        : this(contributorId, version, Canvas2DScenePanDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency)
        : this(contributorId, version, panDependency, Canvas2DSceneMoveGestureDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency,
        Canvas2DSceneMoveGestureDependency moveGestureDependency)
        : this(contributorId, version, panDependency, moveGestureDependency,
            Canvas2DSceneTransientDependency.Unknown, Canvas2DSceneTransientDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency,
        Canvas2DSceneMoveGestureDependency moveGestureDependency,
        Canvas2DSceneTransientDependency hoverDependency,
        Canvas2DSceneTransientDependency visualSelectionDependency)
    {
        ArgumentNullException.ThrowIfNull(contributorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!Enum.IsDefined(panDependency))
        {
            throw new ArgumentOutOfRangeException(nameof(panDependency), panDependency,
                "The Pan dependency must be defined.");
        }

        ContributorId = contributorId;
        if (!Enum.IsDefined(moveGestureDependency))
        {
            throw new ArgumentOutOfRangeException(nameof(moveGestureDependency),
                moveGestureDependency, "The move gesture dependency must be defined.");
        }

        MoveGestureDependency = moveGestureDependency;
        if (!Enum.IsDefined(hoverDependency))
        {
            throw new ArgumentOutOfRangeException(nameof(hoverDependency));
        }
        if (!Enum.IsDefined(visualSelectionDependency))
        {
            throw new ArgumentOutOfRangeException(nameof(visualSelectionDependency));
        }
        HoverDependency = hoverDependency;
        VisualSelectionDependency = visualSelectionDependency;
        Version = version;
        PanDependency = panDependency;
    }

    public Canvas2DSceneContributorId ContributorId { get; }

    public string Version { get; }

    public Canvas2DScenePanDependency PanDependency { get; }

    public Canvas2DSceneMoveGestureDependency MoveGestureDependency { get; }

    /// <summary>Dependency on HoveredObjectId only; gestures and all other inputs stay identical.</summary>
    public Canvas2DSceneTransientDependency HoverDependency { get; }

    /// <summary>Dependency on zero-or-one VisualState selection, not SemanticSceneSelection.</summary>
    public Canvas2DSceneTransientDependency VisualSelectionDependency { get; }

    public bool Equals(Canvas2DSceneContributorDescriptor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ContributorId == other.ContributorId &&
        StringComparer.Ordinal.Equals(Version, other.Version) &&
        PanDependency == other.PanDependency &&
        MoveGestureDependency == other.MoveGestureDependency &&
        HoverDependency == other.HoverDependency &&
        VisualSelectionDependency == other.VisualSelectionDependency;

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DSceneContributorDescriptor);

    public override int GetHashCode() => HashCode.Combine(
        ContributorId,
        StringComparer.Ordinal.GetHashCode(Version),
        PanDependency,
        MoveGestureDependency,
        HoverDependency,
        VisualSelectionDependency);
}
