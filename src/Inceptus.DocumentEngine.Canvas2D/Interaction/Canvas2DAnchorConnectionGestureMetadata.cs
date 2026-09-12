using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Runtime-only metadata for one transient, anchor-to-anchor connection-creation gesture.
/// The metadata carries identities only; plugin eligibility and persistent creation remain
/// downstream in the registered connection factory.
/// </summary>
internal static class Canvas2DAnchorConnectionGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:anchor-connection-create";
    internal const string SourceSemanticElementId =
        "inceptus.canvas2d:anchor-connection-source-semantic-element-id";
    internal const string SourceVisualStateId =
        "inceptus.canvas2d:anchor-connection-source-visual-state-id";
    internal const string SourceAnchorId =
        "inceptus.canvas2d:anchor-connection-source-anchor-id";
    internal const string ConnectionCreationId =
        "inceptus.canvas2d:anchor-connection-creation-id";
    internal const string TargetSemanticElementId =
        "inceptus.canvas2d:anchor-connection-target-semantic-element-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:anchor-connection-target-visual-state-id";
    internal const string TargetAnchorId =
        "inceptus.canvas2d:anchor-connection-target-anchor-id";
    internal const string TargetAcquisitionKind =
        "inceptus.canvas2d:anchor-connection-target-acquisition-kind";
    internal const string TargetSide =
        "inceptus.canvas2d:anchor-connection-target-side";
    internal const string TargetInsertionIndex =
        "inceptus.canvas2d:anchor-connection-target-insertion-index";
    internal const int PreviewZIndex = 3000;

    internal static PropertyMap CreateProperties(
        SemanticElementId sourceSemanticElementId,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId,
        AnchorConnectionCreationId connectionCreationId,
        SemanticElementId? targetSemanticElementId = null,
        VisualStateId? targetVisualStateId = null,
        ConnectorAnchorId? targetAnchorId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceSemanticElementId);
        ArgumentNullException.ThrowIfNull(sourceVisualStateId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(connectionCreationId);
        ValidateTargetTuple(
            targetSemanticElementId,
            targetVisualStateId,
            targetAnchorId,
            nameof(targetSemanticElementId));

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(SourceSemanticElementId, PropertyValue.FromText(sourceSemanticElementId.Value)),
            new(SourceVisualStateId, PropertyValue.FromText(sourceVisualStateId.Value)),
            new(SourceAnchorId, PropertyValue.FromText(sourceAnchorId.Value)),
            new(ConnectionCreationId, PropertyValue.FromText(connectionCreationId.Value)),
        };
        if (targetSemanticElementId is not null)
        {
            properties.Add(new(
                TargetSemanticElementId,
                PropertyValue.FromText(targetSemanticElementId.Value)));
            properties.Add(new(
                TargetVisualStateId,
                PropertyValue.FromText(targetVisualStateId!.Value)));
            properties.Add(new(
                TargetAnchorId,
                PropertyValue.FromText(targetAnchorId!.Value)));
        }

        return new PropertyMap(properties);
    }

    internal static PropertyMap CreateProperties(
        SemanticElementId sourceSemanticElementId,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId,
        AnchorConnectionCreationId connectionCreationId,
        TargetAnchorAcquisitionResult? targetAcquisition)
    {
        var properties = CreateProperties(
                sourceSemanticElementId,
                sourceVisualStateId,
                sourceAnchorId,
                connectionCreationId,
                targetAcquisition?.TargetSemanticElementId,
                targetAcquisition?.TargetVisualStateId,
                targetAcquisition?.AnchorId)
            .ToList();
        if (targetAcquisition is null)
        {
            return new PropertyMap(properties);
        }

        if (!targetAcquisition.IsAccepted || targetAcquisition.Side is not { } side)
        {
            throw new ArgumentException(
                "Transient target-acquisition metadata requires an accepted result.",
                nameof(targetAcquisition));
        }

        properties.Add(new(
            TargetAcquisitionKind,
            PropertyValue.FromText(targetAcquisition.Kind.ToString())));
        properties.Add(new(TargetSide, PropertyValue.FromText(side.ToString())));
        if (targetAcquisition.InsertionIndex is { } insertionIndex)
        {
            properties.Add(new(
                TargetInsertionIndex,
                PropertyValue.FromInteger(insertionIndex)));
        }

        return new PropertyMap(properties);
    }

    internal static bool TryRead(
        PropertyMap properties,
        out Canvas2DAnchorConnectionGestureData? data)
    {
        ArgumentNullException.ThrowIfNull(properties);
        data = null;
        if (!TryReadText(properties, SourceSemanticElementId, out var sourceSemanticId) ||
            !TryReadText(properties, SourceVisualStateId, out var sourceVisualId) ||
            !TryReadText(properties, SourceAnchorId, out var sourceAnchorId) ||
            !TryReadText(properties, ConnectionCreationId, out var connectionCreationId))
        {
            return false;
        }

        var hasTarget = properties.ContainsKey(TargetSemanticElementId);
        if (hasTarget != properties.ContainsKey(TargetVisualStateId) ||
            hasTarget != properties.ContainsKey(TargetAnchorId))
        {
            return false;
        }

        var targetSemanticId = string.Empty;
        var targetVisualId = string.Empty;
        var targetAnchorId = string.Empty;
        if (hasTarget &&
            (!TryReadText(properties, TargetSemanticElementId, out targetSemanticId) ||
             !TryReadText(properties, TargetVisualStateId, out targetVisualId) ||
             !TryReadText(properties, TargetAnchorId, out targetAnchorId)))
        {
            return false;
        }

        TargetAnchorAcquisitionKind? acquisitionKind = null;
        ConnectorAnchorSide? targetSide = null;
        int? targetInsertionIndex = null;
        var hasAcquisition = properties.ContainsKey(TargetAcquisitionKind);
        if (hasAcquisition)
        {
            if (!hasTarget ||
                !TryReadText(properties, TargetAcquisitionKind, out var kindValue) ||
                !Enum.TryParse(
                    kindValue,
                    ignoreCase: false,
                    out TargetAnchorAcquisitionKind parsedKind) ||
                parsedKind == TargetAnchorAcquisitionKind.Rejected ||
                !TryReadText(properties, TargetSide, out var sideValue) ||
                !Enum.TryParse(
                    sideValue,
                    ignoreCase: false,
                    out ConnectorAnchorSide parsedSide))
            {
                return false;
            }

            acquisitionKind = parsedKind;
            targetSide = parsedSide;
            if (parsedKind == TargetAnchorAcquisitionKind.Proposed)
            {
                if (!properties.TryGetValue(TargetInsertionIndex, out var insertion) ||
                    insertion.Kind != PropertyValueKind.Integer ||
                    insertion.IntegerValue < 0 ||
                    insertion.IntegerValue > int.MaxValue)
                {
                    return false;
                }

                targetInsertionIndex = (int)insertion.IntegerValue;
            }
        }
        else if (properties.ContainsKey(TargetSide) ||
                 properties.ContainsKey(TargetInsertionIndex))
        {
            return false;
        }

        try
        {
            data = new Canvas2DAnchorConnectionGestureData(
                new SemanticElementId(sourceSemanticId),
                new VisualStateId(sourceVisualId),
                new ConnectorAnchorId(sourceAnchorId),
                new AnchorConnectionCreationId(connectionCreationId),
                hasTarget ? new SemanticElementId(targetSemanticId) : null,
                hasTarget ? new VisualStateId(targetVisualId) : null,
                hasTarget ? new ConnectorAnchorId(targetAnchorId) : null,
                acquisitionKind,
                targetSide,
                targetInsertionIndex);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateTargetTuple(
        SemanticElementId? targetSemanticElementId,
        VisualStateId? targetVisualStateId,
        ConnectorAnchorId? targetAnchorId,
        string parameterName)
    {
        var count = (targetSemanticElementId is null ? 0 : 1) +
            (targetVisualStateId is null ? 0 : 1) +
            (targetAnchorId is null ? 0 : 1);
        if (count is not 0 and not 3)
        {
            throw new ArgumentException(
                "Target semantic, visual, and connector-anchor identities must be supplied together.",
                parameterName);
        }
    }

    private static bool TryReadText(
        PropertyMap properties,
        string key,
        out string value)
    {
        if (properties.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

/// <summary>
/// Strongly typed transient identities decoded from connection-gesture metadata.
/// </summary>
internal sealed record Canvas2DAnchorConnectionGestureData(
    SemanticElementId SourceSemanticElementId,
    VisualStateId SourceVisualStateId,
    ConnectorAnchorId SourceAnchorId,
    AnchorConnectionCreationId ConnectionCreationId,
    SemanticElementId? TargetSemanticElementId,
    VisualStateId? TargetVisualStateId,
    ConnectorAnchorId? TargetAnchorId,
    TargetAnchorAcquisitionKind? AcquisitionKind,
    ConnectorAnchorSide? TargetSide,
    int? TargetInsertionIndex)
{
    internal bool HasTarget => TargetAnchorId is not null;

    internal bool HasProposedTarget =>
        AcquisitionKind == TargetAnchorAcquisitionKind.Proposed;
}
