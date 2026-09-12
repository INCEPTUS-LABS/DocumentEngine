using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Immutable current-endpoint inputs used to match one endpoint-reconnection capability.
/// </summary>
public sealed class ConnectorEndpointReconnectionStartRequest
{
    public ConnectorEndpointReconnectionStartRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);
        ArgumentNullException.ThrowIfNull(currentSemanticElementId);
        ArgumentNullException.ThrowIfNull(currentAnchorId);
        ValidateEndpointKind(endpointKind, nameof(endpointKind));
        ValidateRevision(document, expectedRevision, nameof(expectedRevision));

        Document = document;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        ConnectorVisualStateId = connectorVisualStateId;
        EndpointKind = endpointKind;
        CurrentSemanticElementId = currentSemanticElementId;
        CurrentAnchorId = currentAnchorId;
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public SemanticElementId RelationshipId { get; }

    public VisualStateId ConnectorVisualStateId { get; }

    public ConnectorEndpointKind EndpointKind { get; }

    public SemanticElementId CurrentSemanticElementId { get; }

    public ConnectorAnchorId CurrentAnchorId { get; }

    internal static void ValidateEndpointKind(
        ConnectorEndpointKind endpointKind,
        string parameterName)
    {
        if (!Enum.IsDefined(endpointKind))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                endpointKind,
                "The connector endpoint kind must be defined.");
        }
    }

    internal static void ValidateRevision(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        string parameterName)
    {
        if (expectedRevision != document.Revision)
        {
            throw new ArgumentException(
                "The expected endpoint-reconnection revision must match the supplied Document snapshot.",
                parameterName);
        }
    }
}
