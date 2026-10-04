using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class ConnectorRoutingStatePreparer
{
    // The existing label-position command is Scene-only. Update its saved caption
    // basis before the single installation without reclassifying any route or region.
    private static bool TryPrepareLabelTranslation(ConnectorRoutingStatePreparationRequest request,
        out ImmutableArray<ScopeRoutingSnapshot> result)
    {
        result = [];
        var before = request.Before;
        var after = request.ProposedDocument;
        var semantic = before.SemanticModel;
        var proposed = after.SemanticModel;
        if (request.Purpose != ConnectorRoutingPreparationPurpose.Mutation || request.IsHistoryReplay ||
            !request.RoutingIntents.IsEmpty || !request.SpatialHeightIntents.IsEmpty ||
            !request.SpatialWidthIntents.IsEmpty || request.NodeGeometryImpact is not null ||
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
            return false;

        var replacements = new Dictionary<VisualStateId, NodeLabelVisualOverride>();
        for (var index = 0; index < before.VisualModel.VisualStates.Length; index++)
        {
            var old = before.VisualModel.VisualStates[index];
            var next = after.VisualModel.VisualStates[index];
            if (old.Equals(next)) continue;
            if (!NodeLabelVisualOverride.TryRead(next.Properties, out var value) ||
                !new VisualStateSnapshot(next.Id, next.SemanticElementId, next.Position, next.Size,
                    next.PlacementMode, next.Route, old.Properties, next.ConnectorAnchors,
                    next.SourceAnchorId, next.TargetAnchorId, next.BoundaryAttachment).Equals(old) ||
                !NodeLabelVisualOverride.UpdateProperties(old.Properties, value).Equals(next.Properties))
                return false;
            replacements.Add(next.Id, value!);
        }
        if (replacements.Count == 0) return false;
        var covered = new HashSet<VisualStateId>();
        var updated = ImmutableArray.CreateBuilder<ScopeRoutingSnapshot>(scopes.Length);
        foreach (var scope in scopes)
        {
            var geometry = scope.Geometry;
            var captions = geometry.Captions.ToArray();
            var changed = false;
            for (var index = 0; index < captions.Length; index++)
            {
                var caption = captions[index];
                if (!replacements.TryGetValue(caption.OwnerVisualStateId, out var value)) continue;
                // Only a translation of the already measured complete box is safe here.
                // Resizing, text changes and automatic-layout reset keep the full fallback.
                if (value.Width != caption.PlacementBounds.Width || value.Height != caption.PlacementBounds.Height ||
                    caption.ContentBounds != caption.PlacementBounds) return false;
                var node = geometry.Nodes.Single(item => item.VisualStateId == caption.OwnerVisualStateId);
                var bounds = value.ResolveBounds(node.LocalBounds);
                captions[index] = new ScopeNodeCaptionSnapshot(caption.OwnerVisualStateId, caption.LabelId,
                    caption.Placement, value, bounds, bounds, caption.Transform, caption.Lines);
                covered.Add(caption.OwnerVisualStateId);
                changed = true;
            }
            updated.Add(changed ? new ScopeRoutingSnapshot(scope.ScopeId,
                new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion, geometry.LayoutAlgorithmId,
                    geometry.Configuration, geometry.TextConfiguration, geometry.Contributors, geometry.Nodes,
                    geometry.Regions, captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors) : scope);
        }
        if (!covered.SetEquals(replacements.Keys)) return false;
        result = updated.MoveToImmutable();
        return true;
    }
}
