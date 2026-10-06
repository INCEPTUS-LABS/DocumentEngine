using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class ConnectorRoutingStatePreparer
{
    private static ImmutableArray<ScopeRoutingSnapshot>? TryPreserveConnectorLabelRouting(
        ConnectorRoutingStatePreparationRequest request)
    {
        var before = request.Before;
        var after = request.ProposedDocument;
        var semantic = before.SemanticModel;
        var proposed = after.SemanticModel;
        // This is a content proof, not a trust in a command's name or invalidation flag.
        // It also covers History replay and removal of an explicit label placement.
        if (request.Purpose != ConnectorRoutingPreparationPurpose.Mutation ||
            !request.RoutingIntents.IsEmpty || !request.SpatialHeightIntents.IsEmpty ||
            !request.SpatialWidthIntents.IsEmpty || request.NodeGeometryImpact is not null ||
            before.DocumentId != after.DocumentId ||
            before.VisualModel.RoutingScopes is not { } scopes ||
            after.VisualModel.RoutingScopes is not { } proposedScopes ||
            !scopes.AsSpan().SequenceEqual(proposedScopes.AsSpan()) ||
            !semantic.Elements.AsSpan().SequenceEqual(proposed.Elements.AsSpan()) ||
            !semantic.Relationships.AsSpan().SequenceEqual(proposed.Relationships.AsSpan()) ||
            !semantic.NestedScopes.AsSpan().SequenceEqual(proposed.NestedScopes.AsSpan()) ||
            !semantic.ScopeMemberships.AsSpan().SequenceEqual(proposed.ScopeMemberships.AsSpan()) ||
            !semantic.ModelProfiles.Equals(proposed.ModelProfiles) ||
            !semantic.ProfileAssignments.AsSpan().SequenceEqual(proposed.ProfileAssignments.AsSpan()) ||
            !before.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(after.VisualModel.ProfileElementPresentations.AsSpan()) ||
            !before.Metadata.SystemManagedProperties.Equals(after.Metadata.SystemManagedProperties) ||
            !before.Metadata.ExtensionProperties.Equals(after.Metadata.ExtensionProperties) ||
            !Equals(before.Publication, after.Publication) ||
            before.VisualModel.VisualStates.Length != after.VisualModel.VisualStates.Length)
            return null;

        var changed = false;
        for (var index = 0; index < before.VisualModel.VisualStates.Length; index++)
        {
            var old = before.VisualModel.VisualStates[index];
            var next = after.VisualModel.VisualStates[index];
            if (old.Equals(next)) continue;
            if (!semantic.TryGetRelationship(old.SemanticElementId, out _) ||
                !scopes.Any(scope => scope.Connectors.Any(record => record.VisualStateId == old.Id)) ||
                !new VisualStateSnapshot(next.Id, next.SemanticElementId, next.Position, next.Size,
                    next.PlacementMode, next.Route, old.Properties, next.ConnectorAnchors,
                    next.SourceAnchorId, next.TargetAnchorId, next.BoundaryAttachment).Equals(old))
                return null;
            ConnectorLabelPlacement.TryRead(next.Properties, out var placement);
            if (!ConnectorLabelPlacement.UpdateProperties(old.Properties, placement).Equals(next.Properties))
                return null;
            changed = true;
        }
        return changed ? scopes : null;
    }
}
