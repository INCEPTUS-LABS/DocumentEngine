using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Immutable current and candidate endpoint inputs supplied to a notation-owned
/// endpoint-reconnection factory.
/// </summary>
public sealed class ConnectorEndpointReconnectionRequest
{
    public ConnectorEndpointReconnectionRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId,
        SemanticElementId candidateSemanticElementId,
        VisualStateId candidateVisualStateId,
        ConnectorAnchorId candidateAnchorId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);
        ArgumentNullException.ThrowIfNull(currentSemanticElementId);
        ArgumentNullException.ThrowIfNull(currentAnchorId);
        ArgumentNullException.ThrowIfNull(candidateSemanticElementId);
        ArgumentNullException.ThrowIfNull(candidateVisualStateId);
        ArgumentNullException.ThrowIfNull(candidateAnchorId);
        ConnectorEndpointReconnectionStartRequest.ValidateEndpointKind(
            endpointKind,
            nameof(endpointKind));
        ConnectorEndpointReconnectionStartRequest.ValidateRevision(
            document,
            expectedRevision,
            nameof(expectedRevision));

        Document = document;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        ConnectorVisualStateId = connectorVisualStateId;
        EndpointKind = endpointKind;
        CurrentSemanticElementId = currentSemanticElementId;
        CurrentAnchorId = currentAnchorId;
        CandidateSemanticElementId = candidateSemanticElementId;
        CandidateVisualStateId = candidateVisualStateId;
        CandidateAnchorId = candidateAnchorId;
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public SemanticElementId RelationshipId { get; }

    public VisualStateId ConnectorVisualStateId { get; }

    public ConnectorEndpointKind EndpointKind { get; }

    public SemanticElementId CurrentSemanticElementId { get; }

    public ConnectorAnchorId CurrentAnchorId { get; }

    public SemanticElementId CandidateSemanticElementId { get; }

    public VisualStateId CandidateVisualStateId { get; }

    public ConnectorAnchorId CandidateAnchorId { get; }
}
