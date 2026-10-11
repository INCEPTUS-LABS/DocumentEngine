using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal static partial class DocumentInvariantValidator
{
    internal const string RoutingStateInvalidCode = "DOC_ROUTING_STATE_INVALID";
    internal const string RoutingStateMissingCode = "DOC_ROUTING_STATE_MISSING";

    /// <summary>Validates persisted structure and authority references without invoking a policy or solver.</summary>
    internal static ImmutableArray<Diagnostic> ValidateRoutingState(DocumentSnapshot snapshot, bool requirePrepared,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.VisualModel.RoutingScopes is not { } scopes)
        {
            return requirePrepared
                ? [Error(RoutingStateMissingCode, "Native v2 requires complete prepared routing scopes.", snapshot.DocumentId.Value)]
                : [];
        }

        var diagnostics = new List<Diagnostic>();
        var semantic = snapshot.SemanticModel;
        var expectedScopes = semantic.NestedScopes.Select(static scope => scope.Id)
            .Append(semantic.RootScopeId).ToHashSet();
        if (!expectedScopes.SetEquals(scopes.Select(static scope => scope.ScopeId)))
            Add("Prepared routing must cover every semantic scope exactly once.", snapshot.DocumentId.Value);

        var allNodeIds = new HashSet<VisualStateId>();
        var allConnectorIds = new HashSet<VisualStateId>();
        var documentConnectorIds = snapshot.VisualModel.DocumentRoutingStates
            .SelectMany(static state => state.Connectors).Select(static connector => connector.VisualStateId).ToHashSet();
        foreach (var scope in scopes)
        {
            if (!semantic.TryGetScope(scope.ScopeId, out _))
            {
                Add("Saved routing references a missing semantic scope.", scope.ScopeId.Value);
                continue;
            }
            var expectedNodes = snapshot.VisualModel.VisualStates.Where(visual =>
                visual.PlacementMode != VisualPlacementMode.Derived &&
                semantic.TryGetElement(visual.SemanticElementId, out _) &&
                semantic.TryGetScope(visual.SemanticElementId, out var owner) && owner?.Id == scope.ScopeId)
                .Select(static visual => visual.Id).ToHashSet();
            var expectedConnectors = snapshot.VisualModel.VisualStates.Where(visual =>
                !documentConnectorIds.Contains(visual.Id) &&
                semantic.TryGetRelationship(visual.SemanticElementId, out _) &&
                (semantic.TryGetScope(visual.SemanticElementId, out var owner) ? owner?.Id == scope.ScopeId :
                    scope.Connectors.Any(connector => connector.VisualStateId == visual.Id)))
                .Select(static visual => visual.Id).ToHashSet();
            if (!expectedNodes.SetEquals(scope.Geometry.Nodes.Select(static node => node.VisualStateId)))
                Add("Saved node geometry must cover exactly the node visuals in its scope.", scope.ScopeId.Value);
            if (!expectedConnectors.SetEquals(scope.Connectors.Select(static connector => connector.VisualStateId)))
                Add("Saved routing must cover exactly the relationship visuals in its scope.", scope.ScopeId.Value);

            foreach (var node in scope.Geometry.Nodes)
            {
                if (!allNodeIds.Add(node.VisualStateId)) Add("A node occurs in more than one saved scope.", node.VisualStateId.Value);
                if (node.LocalBounds.Width <= 0d || node.LocalBounds.Height <= 0d)
                    Add("Saved node bounds must have positive dimensions.", node.VisualStateId.Value);
            }

            var regionOwners = new HashSet<(ModelProfileId, SemanticElementId?)>();
            if (!scope.Geometry.Regions.Select(static region => region.ProfileId).ToHashSet()
                    .SetEquals(scope.Geometry.SpatialWidths.Select(static width => width.ProfileId)))
                Add("Corrected native v2 requires exactly one authored width for each saved spatial profile.", scope.ScopeId.Value);
            foreach (var region in scope.Geometry.Regions)
            {
                if (!regionOwners.Add((region.ProfileId, region.ContainerSemanticElementId)))
                    Add("A profile container can have only one saved region in a scope.", region.Id.Value);
                if (region.ContainerSemanticElementId is { } container &&
                    !semantic.TryGetElement(container, out _))
                    Add("A saved region references a missing semantic container.", region.Id.Value);
            }

            foreach (var connector in scope.Connectors)
            {
                if (!allConnectorIds.Add(connector.VisualStateId))
                    Add("A connector occurs in more than one saved scope.", connector.VisualStateId.Value);
                if (!snapshot.VisualModel.TryGetVisualState(connector.VisualStateId, out var visual) || visual is null ||
                    !semantic.TryGetRelationship(visual.SemanticElementId, out var relationship) || relationship is null)
                {
                    Add("A saved connector must refer to an existing relationship visual.", connector.VisualStateId.Value);
                    continue;
                }
                bool Member(SemanticElementId id) => semantic.TryGetScope(id, out var endpointScope)
                    ? endpointScope?.Id == scope.ScopeId
                    : semantic.TryGetElement(id, out var endpoint) && endpoint?.ContainmentKind == SemanticElementContainmentKind.Document &&
                        snapshot.VisualModel.VisualStates.Any(owner => owner.SemanticElementId == id && owner.PlacementMode == VisualPlacementMode.Derived);
                // Document members are checked against the registered presentation plan at preparation/import.
                // Every scope-contained endpoint still has to belong to this exact Process.
                if (!Member(relationship.SourceId) || !Member(relationship.TargetId))
                    Add("A saved connector cannot contain an endpoint from another Process scope.", connector.VisualStateId.Value);
                if (connector.AutomaticProof is { } proof)
                {
                    ValidateObservation(proof.Source, relationship.SourceId, visual.SourceAnchorId, connector.VisualStateId);
                    ValidateObservation(proof.Target, relationship.TargetId, visual.TargetAnchorId, connector.VisualStateId);
                }
            }
        }
        foreach (var state in snapshot.VisualModel.DocumentRoutingStates)
        {
            if (state.Connectors.IsEmpty)
                Add("Document routing requires at least one document-owned connector.", state.OwnerId.Value);
            if (!semantic.TryGetElement(state.OwnerId, out var owner) ||
                owner?.ContainmentKind != SemanticElementContainmentKind.Document)
                Add("Document routing must belong to an existing document-contained semantic owner.", state.OwnerId.Value);
            foreach (var connector in state.Connectors)
            {
                if (!allConnectorIds.Add(connector.VisualStateId))
                    Add("A connector can have only one saved routing owner.", connector.VisualStateId.Value);
                if (!snapshot.VisualModel.TryGetVisualState(connector.VisualStateId, out var visual) || visual is null ||
                    !semantic.TryGetRelationship(visual.SemanticElementId, out var relationship) || relationship is null)
                {
                    Add("A saved connector must refer to an existing relationship visual.", connector.VisualStateId.Value);
                    continue;
                }
                if (semantic.TryGetScope(relationship.SourceId, out var sourceScope) && sourceScope is not null &&
                    semantic.TryGetScope(relationship.TargetId, out var targetScope) && targetScope?.Id == sourceScope.Id)
                    Add("A connector whose endpoints share a Process scope must retain that scope's routing authority.", connector.VisualStateId.Value);
                if (connector.AutomaticProof is { } proof)
                {
                    ValidateObservation(proof.Source, relationship.SourceId, visual.SourceAnchorId, connector.VisualStateId);
                    ValidateObservation(proof.Target, relationship.TargetId, visual.TargetAnchorId, connector.VisualStateId);
                }
            }
        }
        var expectedConnectorIds = snapshot.VisualModel.VisualStates.Where(visual =>
            semantic.TryGetRelationship(visual.SemanticElementId, out _)).Select(static visual => visual.Id);
        if (!allConnectorIds.SetEquals(expectedConnectorIds))
            Add("Saved routing must cover every relationship visual exactly once.", snapshot.DocumentId.Value);
        if (snapshot.VisualModel.VisualStates.Any(static visual => !visual.Route.IsEmpty))
            Add("Prepared routing cannot coexist with untyped VisualState route guidance.", snapshot.DocumentId.Value);
        return [.. diagnostics];

        void Add(string message, string source) => diagnostics.Add(Error(RoutingStateInvalidCode, message, source));

        void ValidateObservation(ConnectorRoutingEndpointObservation observation, SemanticElementId expectedElement,
            ConnectorAnchorId? expectedAnchor, VisualStateId connectorId)
        {
            if (observation.SemanticElementId != expectedElement || observation.AnchorId != expectedAnchor ||
                !snapshot.VisualModel.TryGetVisualState(observation.VisualStateId, out var owner) || owner is null ||
                owner.SemanticElementId != expectedElement)
            {
                Add("Saved endpoint observations conflict with authoritative semantic/visual bindings.", connectorId.Value);
                return;
            }
            if (expectedAnchor is not null)
            {
                // Export remains notation-neutral. The import/materialization boundary supplies
                // the registered policy needed to resolve predefined anchor definitions.
                if (connectorAnchorPolicyProvider is null && ConnectorAnchorReferenceIdentity.IsPredefinedReference(expectedAnchor))
                    return;
                if (!semantic.TryGetElement(expectedElement, out var element) || element is null) return;
                ImmutableArray<ResolvedConnectorAnchor> anchors;
                try
                {
                    anchors = ElementConnectorAnchorResolver.Resolve(owner, element.TypeId, connectorAnchorPolicyProvider);
                }
                catch (InvalidOperationException)
                {
                    Add("Saved endpoint observations require a valid current connector anchor policy.", connectorId.Value);
                    return;
                }
                var anchor = anchors.FirstOrDefault(candidate => candidate.Id == expectedAnchor);
                if (anchor is null || !anchor.Allows(observation.Role) || anchor.Side != observation.Side ||
                    anchor.Order != observation.Order || anchors.Count(candidate => candidate.Side == anchor.Side) != observation.SideCount)
                    Add("Saved endpoint observations conflict with authoritative connector anchor data.", connectorId.Value);
            }
        }
    }
}
