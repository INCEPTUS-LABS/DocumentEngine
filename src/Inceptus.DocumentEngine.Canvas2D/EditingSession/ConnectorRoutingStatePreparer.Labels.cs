using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class ConnectorRoutingStatePreparer
{
    // Label overrides are Scene-only. Update their saved captions before the single
    // installation, retaining the exact node, region and connector basis.
    private async ValueTask<ImmutableArray<ScopeRoutingSnapshot>?> TryPrepareLabelUpdateAsync(
        ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken)
    {
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
            return null;

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
                return null;
            replacements.Add(next.Id, value!);
        }
        if (replacements.Count == 0) return null;
        var covered = new HashSet<VisualStateId>();
        var updated = ImmutableArray.CreateBuilder<ScopeRoutingSnapshot>(scopes.Length);
        foreach (var scope in scopes)
        {
            var geometry = scope.Geometry;
            var captions = geometry.Captions.ToArray();
            var changed = false;
            var resized = captions.Any(caption => replacements.TryGetValue(caption.OwnerVisualStateId, out var value) &&
                (value.Width != caption.PlacementBounds.Width || value.Height != caption.PlacementBounds.Height ||
                    caption.ContentBounds != caption.PlacementBounds));
            var graph = resized ? _configuration.ProjectionEngine.Project(after, scope.ScopeId,
                _configuration.ProjectionContext, cancellationToken).Graph : null;
            if (resized && graph is null) return null;
            for (var index = 0; index < captions.Length; index++)
            {
                var caption = captions[index];
                if (!replacements.TryGetValue(caption.OwnerVisualStateId, out var value)) continue;
                var node = geometry.Nodes.Single(item => item.VisualStateId == caption.OwnerVisualStateId);
                var bounds = value.ResolveBounds(node.LocalBounds);
                captions[index] = resized
                    ? await _configuration.SceneBuilder.PrepareResizedCaptionAsync(
                        graph!.Labels.Single(label => label.Id == caption.LabelId), node, value,
                        _textMetrics, _requestFactory, cancellationToken).ConfigureAwait(false)
                    : new ScopeNodeCaptionSnapshot(caption.OwnerVisualStateId, caption.LabelId,
                        caption.Placement, value, bounds, bounds, caption.Transform, caption.Lines);
                covered.Add(caption.OwnerVisualStateId);
                changed = true;
            }
            var measurements = resized
                ? _configuration.SceneBuilder.RebuildCaptionMeasurementIndex(CreateInputs(after, scope.ScopeId),
                    graph!, RestoreLocalLayout(graph!, geometry), geometry, captions)
                : geometry.TextMeasurements;
            updated.Add(changed ? new ScopeRoutingSnapshot(scope.ScopeId,
                new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion, geometry.LayoutAlgorithmId,
                    geometry.Configuration, geometry.TextConfiguration, geometry.Contributors, geometry.Nodes,
                    geometry.Regions, captions, measurements, geometry.SpatialWidths), scope.Connectors) : scope);
        }
        if (!covered.SetEquals(replacements.Keys)) return null;
        return updated.MoveToImmutable();
    }
}
