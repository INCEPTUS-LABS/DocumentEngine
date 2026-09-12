using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Provides notation-neutral queries over persistent connector endpoint references.
/// </summary>
public static class ConnectorAnchorOccupancy
{
    public static bool IsOccupied(
        IVisualModelView visualModel,
        ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(anchorId);

        return EnumerateEndpointReferences(visualModel).Any(candidate => candidate == anchorId);
    }

    public static int CountEndpointReferences(
        IVisualModelView visualModel,
        ConnectorAnchorId anchorId)
    {
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(anchorId);

        return EnumerateEndpointReferences(visualModel).Count(candidate => candidate == anchorId);
    }

    /// <summary>
    /// Determines whether an anchor is referenced by an endpoint other than one exact
    /// connector endpoint that is being edited.
    /// </summary>
    public static bool IsOccupiedByOtherEndpoint(
        IVisualModelView visualModel,
        ConnectorAnchorId anchorId,
        VisualStateId excludedConnectorVisualStateId,
        ConnectorEndpointKind excludedEndpointKind)
    {
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(anchorId);
        ArgumentNullException.ThrowIfNull(excludedConnectorVisualStateId);
        if (!Enum.IsDefined(excludedEndpointKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(excludedEndpointKind),
                excludedEndpointKind,
                "The excluded connector endpoint kind must be defined.");
        }

        foreach (var visualState in visualModel.VisualStates)
        {
            var isExcludedConnector = visualState.Id == excludedConnectorVisualStateId;
            if (visualState.SourceAnchorId == anchorId &&
                (!isExcludedConnector || excludedEndpointKind != ConnectorEndpointKind.Source))
            {
                return true;
            }

            if (visualState.TargetAnchorId == anchorId &&
                (!isExcludedConnector || excludedEndpointKind != ConnectorEndpointKind.Target))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<ConnectorAnchorId> EnumerateEndpointReferences(
        IVisualModelView visualModel)
    {
        ArgumentNullException.ThrowIfNull(visualModel);

        foreach (var visualState in visualModel.VisualStates)
        {
            if (visualState.SourceAnchorId is { } sourceAnchorId)
            {
                yield return sourceAnchorId;
            }

            if (visualState.TargetAnchorId is { } targetAnchorId)
            {
                yield return targetAnchorId;
            }
        }
    }
}
