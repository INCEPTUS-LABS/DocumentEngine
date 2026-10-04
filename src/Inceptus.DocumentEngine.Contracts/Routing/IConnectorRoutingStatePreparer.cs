using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Contracts.Routing;

public enum ConnectorRoutingPreparationPurpose
{
    Mutation,
    InitialConstruction,
    ValidateSavedState,
    RecoverSavedState,
}

/// <summary>Prepares only the saved geometry/routing subtree before the single immutable installation.</summary>
public interface IConnectorRoutingStatePreparer
{
    ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(
        ConnectorRoutingStatePreparationRequest request,
        CancellationToken cancellationToken);
}

public sealed class ConnectorRoutingStatePreparationRequest
{
    public ConnectorRoutingStatePreparationRequest(
        DocumentSnapshot before,
        DocumentSnapshot proposedDocument,
        IEnumerable<ConnectorRoutingIntent> routingIntents,
        IEnumerable<SpatialRegionHeightIntent> spatialHeightIntents,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        bool isHistoryReplay)
        : this(before, proposedDocument, routingIntents, spatialHeightIntents,
            nodeGeometryImpact, isHistoryReplay, ConnectorRoutingPreparationPurpose.Mutation)
    {
    }

    public ConnectorRoutingStatePreparationRequest(
        DocumentSnapshot before,
        DocumentSnapshot proposedDocument,
        IEnumerable<ConnectorRoutingIntent> routingIntents,
        IEnumerable<SpatialRegionHeightIntent> spatialHeightIntents,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        bool isHistoryReplay,
        ConnectorRoutingPreparationPurpose purpose)
        : this(before, proposedDocument, routingIntents, spatialHeightIntents, [],
            nodeGeometryImpact, isHistoryReplay, purpose)
    {
    }

    public ConnectorRoutingStatePreparationRequest(
        DocumentSnapshot before,
        DocumentSnapshot proposedDocument,
        IEnumerable<ConnectorRoutingIntent> routingIntents,
        IEnumerable<SpatialRegionHeightIntent> spatialHeightIntents,
        IEnumerable<SpatialScopeWidthIntent> spatialWidthIntents,
        NodeGeometryPipelineImpact? nodeGeometryImpact,
        bool isHistoryReplay,
        ConnectorRoutingPreparationPurpose purpose = ConnectorRoutingPreparationPurpose.Mutation)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(proposedDocument);
        if (before.DocumentId != proposedDocument.DocumentId)
            throw new ArgumentException("Preparation cannot replace document identity.", nameof(proposedDocument));
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        Before = before;
        ProposedDocument = proposedDocument;
        RoutingIntents = RoutingStateCollection.Copy(routingIntents, nameof(routingIntents));
        SpatialHeightIntents = RoutingStateCollection.Copy(spatialHeightIntents, nameof(spatialHeightIntents));
        SpatialWidthIntents = RoutingStateCollection.Copy(spatialWidthIntents, nameof(spatialWidthIntents));
        NodeGeometryImpact = nodeGeometryImpact;
        IsHistoryReplay = isHistoryReplay;
        Purpose = purpose;
    }

    public DocumentSnapshot Before { get; }
    public DocumentSnapshot ProposedDocument { get; }
    public ImmutableArray<ConnectorRoutingIntent> RoutingIntents { get; }
    public ImmutableArray<SpatialRegionHeightIntent> SpatialHeightIntents { get; }
    public ImmutableArray<SpatialScopeWidthIntent> SpatialWidthIntents { get; }
    public NodeGeometryPipelineImpact? NodeGeometryImpact { get; }
    public bool IsHistoryReplay { get; }
    public ConnectorRoutingPreparationPurpose Purpose { get; }
}

public sealed class ConnectorRoutingStatePreparationResult
{
    private ConnectorRoutingStatePreparationResult(
        bool succeeded,
        IEnumerable<ScopeRoutingSnapshot> routingScopes,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Succeeded = succeeded;
        RoutingScopes = RoutingStateCollection.Unique(routingScopes,
            static scope => scope.ScopeId.Value, nameof(routingScopes));
        Diagnostics = RoutingDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        if (succeeded && Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new ArgumentException("Successful preparation cannot contain errors.", nameof(diagnostics));
        if (!succeeded && !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new ArgumentException("Failed preparation requires an error diagnostic.", nameof(diagnostics));
    }

    public bool Succeeded { get; }
    public ImmutableArray<ScopeRoutingSnapshot> RoutingScopes { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static ConnectorRoutingStatePreparationResult Success(
        IEnumerable<ScopeRoutingSnapshot> routingScopes, IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, routingScopes, diagnostics);

    public static ConnectorRoutingStatePreparationResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(false, [], diagnostics);
}
