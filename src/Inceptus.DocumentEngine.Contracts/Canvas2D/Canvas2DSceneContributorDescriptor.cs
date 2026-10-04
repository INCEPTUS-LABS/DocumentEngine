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
        : this(contributorId, version, panDependency, moveGestureDependency,
            hoverDependency, visualSelectionDependency, Canvas2DScenePlacementDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency,
        Canvas2DSceneMoveGestureDependency moveGestureDependency,
        Canvas2DSceneTransientDependency hoverDependency,
        Canvas2DSceneTransientDependency visualSelectionDependency,
        Canvas2DScenePlacementDependency placementDependency)
        : this(contributorId, version, panDependency, moveGestureDependency, hoverDependency,
            visualSelectionDependency, placementDependency, Canvas2DSceneTransientDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency,
        Canvas2DSceneMoveGestureDependency moveGestureDependency,
        Canvas2DSceneTransientDependency hoverDependency,
        Canvas2DSceneTransientDependency visualSelectionDependency,
        Canvas2DScenePlacementDependency placementDependency,
        Canvas2DSceneTransientDependency regionResizeDependency)
        : this(contributorId, version, panDependency, moveGestureDependency, hoverDependency,
            visualSelectionDependency, placementDependency, regionResizeDependency,
            Canvas2DSceneTransientDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency,
        Canvas2DSceneMoveGestureDependency moveGestureDependency,
        Canvas2DSceneTransientDependency hoverDependency,
        Canvas2DSceneTransientDependency visualSelectionDependency,
        Canvas2DScenePlacementDependency placementDependency,
        Canvas2DSceneTransientDependency regionResizeDependency,
        Canvas2DSceneTransientDependency nodeLabelMoveDependency)
        : this(contributorId, version, panDependency, moveGestureDependency, hoverDependency,
            visualSelectionDependency, placementDependency, regionResizeDependency, nodeLabelMoveDependency,
            Canvas2DSceneTransientDependency.Unknown)
    {
    }

    public Canvas2DSceneContributorDescriptor(
        Canvas2DSceneContributorId contributorId,
        string version,
        Canvas2DScenePanDependency panDependency,
        Canvas2DSceneMoveGestureDependency moveGestureDependency,
        Canvas2DSceneTransientDependency hoverDependency,
        Canvas2DSceneTransientDependency visualSelectionDependency,
        Canvas2DScenePlacementDependency placementDependency,
        Canvas2DSceneTransientDependency regionResizeDependency,
        Canvas2DSceneTransientDependency nodeLabelMoveDependency,
        Canvas2DSceneTransientDependency routeBendDependency)
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
        if (!Enum.IsDefined(placementDependency))
        {
            throw new ArgumentOutOfRangeException(nameof(placementDependency));
        }
        HoverDependency = hoverDependency;
        VisualSelectionDependency = visualSelectionDependency;
        PlacementDependency = placementDependency;
        if (!Enum.IsDefined(regionResizeDependency))
            throw new ArgumentOutOfRangeException(nameof(regionResizeDependency));
        RegionResizeDependency = regionResizeDependency;
        if (!Enum.IsDefined(nodeLabelMoveDependency))
            throw new ArgumentOutOfRangeException(nameof(nodeLabelMoveDependency));
        NodeLabelMoveDependency = nodeLabelMoveDependency;
        if (!Enum.IsDefined(routeBendDependency))
            throw new ArgumentOutOfRangeException(nameof(routeBendDependency));
        RouteBendDependency = routeBendDependency;
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

    /// <summary>Dependency on the transient placement feedback family only.</summary>
    public Canvas2DScenePlacementDependency PlacementDependency { get; }

    /// <summary>Dependency on spatial resize feedback and its gesture lifecycle, with other inputs fixed.</summary>
    public Canvas2DSceneTransientDependency RegionResizeDependency { get; }

    /// <summary>Dependency on node-label move gesture position, with every other input fixed.</summary>
    public Canvas2DSceneTransientDependency NodeLabelMoveDependency { get; }

    /// <summary>Dependency on Manual route-bend gesture position, with every other input fixed.</summary>
    public Canvas2DSceneTransientDependency RouteBendDependency { get; }

    public bool Equals(Canvas2DSceneContributorDescriptor? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ContributorId == other.ContributorId &&
        StringComparer.Ordinal.Equals(Version, other.Version) &&
        PanDependency == other.PanDependency &&
        MoveGestureDependency == other.MoveGestureDependency &&
        HoverDependency == other.HoverDependency &&
        VisualSelectionDependency == other.VisualSelectionDependency &&
        PlacementDependency == other.PlacementDependency &&
        RegionResizeDependency == other.RegionResizeDependency &&
        NodeLabelMoveDependency == other.NodeLabelMoveDependency &&
        RouteBendDependency == other.RouteBendDependency;

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DSceneContributorDescriptor);

    public override int GetHashCode() => HashCode.Combine(HashCode.Combine(
        ContributorId,
        StringComparer.Ordinal.GetHashCode(Version),
        PanDependency,
        MoveGestureDependency,
        HoverDependency,
        VisualSelectionDependency,
        PlacementDependency,
        RegionResizeDependency), NodeLabelMoveDependency, RouteBendDependency);
}
