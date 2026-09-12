using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Runtime-only identities for one transient existing-connector endpoint reconnection.
/// Persistent eligibility and command construction remain owned by the registered plugin.
/// </summary>
internal static class Canvas2DConnectorEndpointReconnectionGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:connector-endpoint-reconnect";
    internal const string RelationshipId =
        "inceptus.canvas2d:connector-endpoint-reconnect-relationship-id";
    internal const string ConnectorVisualStateId =
        "inceptus.canvas2d:connector-endpoint-reconnect-visual-state-id";
    internal const string EndpointKind =
        "inceptus.canvas2d:connector-endpoint-reconnect-endpoint-kind";
    internal const string OriginalSemanticEndpointId =
        "inceptus.canvas2d:connector-endpoint-reconnect-original-semantic-endpoint-id";
    internal const string OriginalAnchorId =
        "inceptus.canvas2d:connector-endpoint-reconnect-original-anchor-id";
    internal const string ReconnectionId =
        "inceptus.canvas2d:connector-endpoint-reconnect-registration-id";
    internal const string ConnectorSceneObjectId =
        "inceptus.canvas2d:connector-endpoint-reconnect-scene-object-id";
    internal const string CandidateSemanticElementId =
        "inceptus.canvas2d:connector-endpoint-reconnect-candidate-semantic-element-id";
    internal const string CandidateVisualStateId =
        "inceptus.canvas2d:connector-endpoint-reconnect-candidate-visual-state-id";
    internal const string CandidateAnchorId =
        "inceptus.canvas2d:connector-endpoint-reconnect-candidate-anchor-id";
    internal const int PreviewZIndex = 3000;

    internal static PropertyMap CreateProperties(
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId originalSemanticEndpointId,
        ConnectorAnchorId originalAnchorId,
        ConnectorEndpointReconnectionId reconnectionId,
        SceneObjectId connectorSceneObjectId,
        SemanticElementId? candidateSemanticElementId = null,
        VisualStateId? candidateVisualStateId = null,
        ConnectorAnchorId? candidateAnchorId = null)
    {
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);
        if (!Enum.IsDefined(endpointKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endpointKind),
                endpointKind,
                "The connector endpoint kind must be defined.");
        }

        ArgumentNullException.ThrowIfNull(originalSemanticEndpointId);
        ArgumentNullException.ThrowIfNull(originalAnchorId);
        ArgumentNullException.ThrowIfNull(reconnectionId);
        ArgumentNullException.ThrowIfNull(connectorSceneObjectId);
        ValidateCandidateTuple(
            candidateSemanticElementId,
            candidateVisualStateId,
            candidateAnchorId,
            nameof(candidateSemanticElementId));

        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(RelationshipId, PropertyValue.FromText(relationshipId.Value)),
            new(ConnectorVisualStateId, PropertyValue.FromText(connectorVisualStateId.Value)),
            new(EndpointKind, PropertyValue.FromInteger((long)endpointKind)),
            new(
                OriginalSemanticEndpointId,
                PropertyValue.FromText(originalSemanticEndpointId.Value)),
            new(OriginalAnchorId, PropertyValue.FromText(originalAnchorId.Value)),
            new(ReconnectionId, PropertyValue.FromText(reconnectionId.Value)),
            new(ConnectorSceneObjectId, PropertyValue.FromText(connectorSceneObjectId.Value)),
        };
        if (candidateSemanticElementId is not null)
        {
            properties.Add(new(
                CandidateSemanticElementId,
                PropertyValue.FromText(candidateSemanticElementId.Value)));
            properties.Add(new(
                CandidateVisualStateId,
                PropertyValue.FromText(candidateVisualStateId!.Value)));
            properties.Add(new(
                CandidateAnchorId,
                PropertyValue.FromText(candidateAnchorId!.Value)));
        }

        return new PropertyMap(properties);
    }

    internal static bool TryRead(
        PropertyMap properties,
        out Canvas2DConnectorEndpointReconnectionGestureData? data)
    {
        ArgumentNullException.ThrowIfNull(properties);
        data = null;
        if (!TryReadText(properties, RelationshipId, out var relationshipId) ||
            !TryReadText(properties, ConnectorVisualStateId, out var connectorVisualStateId) ||
            !TryReadEndpointKind(properties, out var endpointKind) ||
            !TryReadText(
                properties,
                OriginalSemanticEndpointId,
                out var originalSemanticEndpointId) ||
            !TryReadText(properties, OriginalAnchorId, out var originalAnchorId) ||
            !TryReadText(properties, ReconnectionId, out var reconnectionId) ||
            !TryReadText(
                properties,
                ConnectorSceneObjectId,
                out var connectorSceneObjectId))
        {
            return false;
        }

        var hasCandidate = properties.ContainsKey(CandidateSemanticElementId);
        if (hasCandidate != properties.ContainsKey(CandidateVisualStateId) ||
            hasCandidate != properties.ContainsKey(CandidateAnchorId))
        {
            return false;
        }

        var candidateSemanticElementId = string.Empty;
        var candidateVisualStateId = string.Empty;
        var candidateAnchorId = string.Empty;
        if (hasCandidate &&
            (!TryReadText(
                 properties,
                 CandidateSemanticElementId,
                 out candidateSemanticElementId) ||
             !TryReadText(
                 properties,
                 CandidateVisualStateId,
                 out candidateVisualStateId) ||
             !TryReadText(properties, CandidateAnchorId, out candidateAnchorId)))
        {
            return false;
        }

        try
        {
            data = new Canvas2DConnectorEndpointReconnectionGestureData(
                new SemanticElementId(relationshipId),
                new VisualStateId(connectorVisualStateId),
                endpointKind,
                new SemanticElementId(originalSemanticEndpointId),
                new ConnectorAnchorId(originalAnchorId),
                new ConnectorEndpointReconnectionId(reconnectionId),
                new SceneObjectId(connectorSceneObjectId),
                hasCandidate ? new SemanticElementId(candidateSemanticElementId) : null,
                hasCandidate ? new VisualStateId(candidateVisualStateId) : null,
                hasCandidate ? new ConnectorAnchorId(candidateAnchorId) : null);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateCandidateTuple(
        SemanticElementId? candidateSemanticElementId,
        VisualStateId? candidateVisualStateId,
        ConnectorAnchorId? candidateAnchorId,
        string parameterName)
    {
        var count = (candidateSemanticElementId is null ? 0 : 1) +
            (candidateVisualStateId is null ? 0 : 1) +
            (candidateAnchorId is null ? 0 : 1);
        if (count is not 0 and not 3)
        {
            throw new ArgumentException(
                "Candidate semantic, visual, and connector-anchor identities must be supplied together.",
                parameterName);
        }
    }

    private static bool TryReadEndpointKind(
        PropertyMap properties,
        out ConnectorEndpointKind endpointKind)
    {
        if (properties.TryGetValue(EndpointKind, out var property) &&
            property.Kind == PropertyValueKind.Integer &&
            property.IntegerValue is >= int.MinValue and <= int.MaxValue)
        {
            endpointKind = (ConnectorEndpointKind)(int)property.IntegerValue;
            return Enum.IsDefined(endpointKind);
        }

        endpointKind = default;
        return false;
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
/// Strongly typed transient identities decoded from endpoint-reconnection metadata.
/// </summary>
internal sealed record Canvas2DConnectorEndpointReconnectionGestureData(
    SemanticElementId RelationshipId,
    VisualStateId ConnectorVisualStateId,
    ConnectorEndpointKind EndpointKind,
    SemanticElementId OriginalSemanticEndpointId,
    ConnectorAnchorId OriginalAnchorId,
    ConnectorEndpointReconnectionId ReconnectionId,
    SceneObjectId ConnectorSceneObjectId,
    SemanticElementId? CandidateSemanticElementId,
    VisualStateId? CandidateVisualStateId,
    ConnectorAnchorId? CandidateAnchorId)
{
    internal bool HasCandidate => CandidateAnchorId is not null;
}
