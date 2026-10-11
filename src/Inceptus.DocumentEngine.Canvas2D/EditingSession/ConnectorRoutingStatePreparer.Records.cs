using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class ConnectorRoutingStatePreparer
{
    private static ImmutableArray<ConnectorRoutingRecord> PrepareRecords(
        ConnectorRoutingStatePreparationRequest request,
        ProjectedGraph graph, LayoutResult layout, RoutingContext context,
        ImmutableArray<ConnectorRoutingRecord> current, ImmutableArray<ConnectorRoutingRecord> proposedRecords,
        IEnumerable<ConnectorRoutingRecord> beforeRecords, IStableConnectorRoutingPolicy policy,
        List<Diagnostic> diagnostics, bool strict, CancellationToken cancellationToken,
        ScopeGeometrySnapshot? currentGeometry = null, bool unchangedDocumentGeometry = false)
    {
        var edges = graph.Edges.ToDictionary(edge => edge.Source.VisualStateId
            ?? throw new InvalidOperationException("Saved connectors require Visual State identity."));
        // Focused replay can seed an exact earlier route and priority. It is still assessed against current geometry below.
        var currentById = current.ToDictionary(record => record.VisualStateId);
        var hasReplaySeeds = request.IsHistoryReplay && request.RoutingIntents.IsEmpty &&
            (request.NodeGeometryImpact is { HasExplicitChanges: true } || !request.SpatialHeightIntents.IsEmpty || !request.SpatialWidthIntents.IsEmpty) &&
            SemanticDefinitionsUnchanged(request.Before, request.ProposedDocument) &&
            current.Length == proposedRecords.Length && proposedRecords.All(record =>
                currentById.TryGetValue(record.VisualStateId, out var prior) && prior.RoutingType == record.RoutingType &&
                prior.ManualDefinition.HasValue == record.ManualDefinition.HasValue &&
                (!prior.ManualDefinition.HasValue || prior.ManualDefinition.Value.AsSpan().SequenceEqual(record.ManualDefinition!.Value.AsSpan()))) &&
            !current.AsSpan().SequenceEqual(proposedRecords.AsSpan());
        var replaySeeds = hasReplaySeeds ? proposedRecords.ToDictionary(record => record.VisualStateId) : [];
        var survivors = (hasReplaySeeds ? proposedRecords.Concat(current.Where(record => !replaySeeds.ContainsKey(record.VisualStateId))) : current)
            .Where(record => edges.ContainsKey(record.VisualStateId)).ToArray();
        var survivorIds = survivors.Select(static record => record.VisualStateId).ToHashSet();
        var allCurrent = beforeRecords.ToDictionary(static record => record.VisualStateId);
        var proposed = proposedRecords.ToDictionary(static record => record.VisualStateId);
        var newIds = request.RoutingIntents.Where(static intent => intent.Kind == ConnectorRoutingIntentKind.Initialize)
            .Select(static intent => intent.VisualStateId)
            .Concat(proposedRecords.Select(static record => record.VisualStateId))
            .Concat(edges.Keys.OrderBy(static id => id.Value, StringComparer.Ordinal))
            .Where(id => edges.ContainsKey(id) && !survivorIds.Contains(id)).Distinct().ToArray();
        var order = survivors.Select(static record => record.VisualStateId).Concat(newIds).ToArray();
        var results = new Dictionary<VisualStateId, ConnectorRoutingRecord>();
        var comparisons = new Dictionary<VisualStateId, ConnectorRoutingRecord>();
        var pending = new List<ProjectedObjectId>();
        var pendingDefinitions = new Dictionary<VisualStateId, ImmutableArray<PointD>?>();
        var beforeOwners = request.Before.VisualModel.VisualStates.GroupBy(static visual => visual.SemanticElementId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var afterOwners = request.ProposedDocument.VisualModel.VisualStates.GroupBy(static visual => visual.SemanticElementId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        foreach (var id in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edge = edges[id];
            var original = replaySeeds.GetValueOrDefault(id) ?? allCurrent.GetValueOrDefault(id) ?? proposed.GetValueOrDefault(id);
            var candidate = original;
            var mode = candidate?.RoutingType ?? ConnectorRoutingType.Automatic;
            var definition = candidate?.ManualDefinition;
            var forceSearch = false;
            var assessment = policy.Assess(graph, layout, context, edge.Id, candidate, cancellationToken);
            var assessedCandidate = candidate;
            // NoRoute observations describe the candidate. Publish them only if that
            // outcome survives this operation; repaired or explicit paths replace it.
            if (candidate?.Outcome != ConnectorRoutingOutcome.NoRoute)
                diagnostics.AddRange(assessment.Diagnostics);
            if (assessment.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException("Current connector endpoint assessment failed.");
            if (!strict && candidate?.RoutingType == ConnectorRoutingType.Automatic &&
                candidate.Outcome == ConnectorRoutingOutcome.Path && currentGeometry is not null &&
                context.PreparedInput?.LogicalGeometry is { } logical &&
                TryTransportFrame(candidate, currentGeometry, logical.Geometry, assessment, out var transported))
            {
                candidate = transported;
                assessment = policy.Assess(graph, layout, context, edge.Id, candidate, cancellationToken);
                assessedCandidate = candidate;
                if (assessment.IsValidAutomaticPath) comparisons[id] = candidate;
            }
            foreach (var intent in request.RoutingIntents.Where(intent => intent.VisualStateId == id))
            {
                switch (intent.Kind)
                {
                    case ConnectorRoutingIntentKind.Initialize:
                        if (allCurrent.ContainsKey(id))
                            throw new InvalidOperationException("Initialize cannot replace a surviving connector's mode.");
                        goto case ConnectorRoutingIntentKind.SetType;
                    case ConnectorRoutingIntentKind.SetType:
                        if (intent.RoutingType == ConnectorRoutingType.Manual && definition is null &&
                            mode == ConnectorRoutingType.Automatic)
                        {
                            if (!ReferenceEquals(candidate, assessedCandidate))
                                assessment = policy.Assess(graph, layout, context, edge.Id, candidate, cancellationToken);
                            if (forceSearch || !assessment.IsValidAutomaticPath && !IsUnchangedNoRoute(candidate, assessment))
                            {
                                var intermediate = policy.RouteAffected(graph, layout, context, [edge.Id], cancellationToken);
                                diagnostics.AddRange(intermediate.Diagnostics.Where(static diagnostic =>
                                    diagnostic.Severity == DiagnosticSeverity.Error));
                                if (intermediate.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
                                    intermediate.Records.Length != 1 || intermediate.Records[0].VisualStateId != id)
                                    throw new InvalidOperationException("Intermediate automatic routing returned an invalid result.");
                                candidate = intermediate.Records[0];
                            }
                        }
                        mode = intent.RoutingType!.Value;
                        if (mode == ConnectorRoutingType.Manual)
                        {
                            definition ??= candidate?.Outcome == ConnectorRoutingOutcome.Path
                                ? [.. candidate.Path.Skip(1).Take(candidate.Path.Length - 2)] : [];
                            candidate = ExplicitRecord(id, mode, definition, assessment);
                        }
                        else if (mode == ConnectorRoutingType.Straight)
                            candidate = ExplicitRecord(id, mode, definition, assessment);
                        forceSearch = mode == ConnectorRoutingType.Automatic && forceSearch;
                        break;
                    case ConnectorRoutingIntentKind.ReplaceManualDefinition:
                        if (mode != ConnectorRoutingType.Manual)
                            throw new InvalidOperationException("Connector points can be edited only in Manual mode.");
                        if (intent.SourcePoint is { } source && (source != assessment.Source.Point || intent.TargetPoint != assessment.Target.Point))
                            throw new InvalidOperationException("The manual path endpoints no longer match current attachment authority.");
                        definition = intent.ManualDefinition;
                        candidate = ExplicitRecord(id, mode, definition, assessment);
                        break;
                    case ConnectorRoutingIntentKind.Recalculate:
                        if (mode == ConnectorRoutingType.Straight)
                            throw new InvalidOperationException("Straight connectors have no route recalculation or point reset.");
                        if (mode == ConnectorRoutingType.Manual)
                        {
                            definition = [];
                            candidate = ExplicitRecord(id, mode, definition, assessment);
                        }
                        else forceSearch = true;
                        break;
                    default: throw new InvalidOperationException("Unknown routing intent.");
                }
            }
            if (mode != ConnectorRoutingType.Automatic)
            {
                results.Add(id, ExplicitRecord(id, mode, definition, assessment));
                continue;
            }
            if (!ReferenceEquals(candidate, assessedCandidate))
                assessment = policy.Assess(graph, layout, context, edge.Id, candidate, cancellationToken);
            if (!forceSearch && assessment.IsValidAutomaticPath && candidate?.Outcome == ConnectorRoutingOutcome.Path)
            {
                results.Add(id, new ConnectorRoutingRecord(id, mode, ConnectorRoutingOutcome.Path,
                    candidate.Path, definition, assessment.AcceptedProof));
                continue;
            }
            var unchangedNoRoute = IsUnchangedNoRoute(candidate, assessment);
            if (!forceSearch && unchangedNoRoute)
            {
                diagnostics.AddRange(assessment.Diagnostics);
                results.Add(id, candidate!);
                continue;
            }
            if (strict) throw new InvalidOperationException("A saved automatic path or its policy proof is no longer valid.");
            pending.Add(edge.Id);
            pendingDefinitions.Add(id, definition);
        }
        bool IsUnchangedNoRoute(ConnectorRoutingRecord? record, ConnectorRoutingAssessment assessment) =>
            record?.Outcome == ConnectorRoutingOutcome.NoRoute &&
            record.AutomaticProof == assessment.AcceptedProof &&
            (strict || unchangedDocumentGeometry ||
                currentGeometry is not null && context.PreparedInput?.LogicalGeometry is { } logicalBasis &&
                currentGeometry.Nodes.AsSpan().SequenceEqual(logicalBasis.Geometry.Nodes.AsSpan()) &&
                currentGeometry.Regions.AsSpan().SequenceEqual(logicalBasis.Geometry.Regions.AsSpan()));
        if (pending.Count > 0)
        {
            var repaired = policy.RouteAffected(graph, layout, context, [.. pending], cancellationToken);
            diagnostics.AddRange(repaired.Diagnostics);
            if (repaired.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
                repaired.Records.Length != pending.Count || repaired.Records.Any(record => !pendingDefinitions.ContainsKey(record.VisualStateId)))
                throw new InvalidOperationException("The selected routing batch returned incomplete or conflicting results.");
            foreach (var record in repaired.Records)
                results.Add(record.VisualStateId, new ConnectorRoutingRecord(record.VisualStateId,
                    ConnectorRoutingType.Automatic, record.Outcome, record.Path, pendingDefinitions[record.VisualStateId],
                    record.AutomaticProof, record.NoRouteReason));
        }
        // Translating existing region frames changes routing observations, not connector priority.
        // Repairs still use the same affected-route assessment above, including cross-frame endpoints.
        if (request.RoutingIntents.IsEmpty && currentGeometry is not null &&
            context.PreparedInput?.LogicalGeometry is { } prepared &&
            request.Before.VisualModel.VisualStates.AsSpan().SequenceEqual(request.ProposedDocument.VisualModel.VisualStates.AsSpan()) &&
            IsRegionFrameTranslation(currentGeometry, prepared.Geometry))
            return [.. order.Select(id => results[id])];
        if (hasReplaySeeds) return [.. order.Select(id => results[id])];
        var unchanged = new List<ConnectorRoutingRecord>();
        var changed = new List<ConnectorRoutingRecord>();
        foreach (var record in survivors)
        {
            var result = results[record.VisualStateId];
            var comparison = comparisons.GetValueOrDefault(record.VisualStateId) ?? record;
            var equal = comparison.Outcome == result.Outcome && comparison.Path.AsSpan().SequenceEqual(result.Path.AsSpan()) &&
                SameBinding(comparison, result) && SamePersistentBindings(request.Before, request.ProposedDocument,
                    beforeOwners, afterOwners, record.VisualStateId);
            (equal ? unchanged : changed).Add(result);
        }
        return [.. unchanged, .. changed, .. newIds.Select(id => results[id])];
    }

    private static bool IsRegionFrameTranslation(ScopeGeometrySnapshot before, ScopeGeometrySnapshot after)
    {
        if (!before.Nodes.AsSpan().SequenceEqual(after.Nodes.AsSpan()) || before.Regions.Length != after.Regions.Length)
            return false;
        var changed = false;
        for (var index = 0; index < before.Regions.Length; index++)
        {
            var previous = before.Regions[index];
            var current = after.Regions[index];
            if (previous == current) continue;
            if (previous.Id != current.Id || previous.ProfileId != current.ProfileId ||
                previous.ContainerSemanticElementId != current.ContainerSemanticElementId || previous.ExpandedHeight != current.ExpandedHeight ||
                previous.ContentBounds is not { } oldBounds || current.ContentBounds is not { } bounds || oldBounds.Size != bounds.Size ||
                previous.LocalToScopeTransform is not { } oldTransform || current.LocalToScopeTransform is not { } transform ||
                oldTransform != Matrix2D.CreateTranslation(oldTransform.OffsetX, oldTransform.OffsetY) ||
                transform != Matrix2D.CreateTranslation(oldTransform.OffsetX + bounds.X - oldBounds.X, oldTransform.OffsetY + bounds.Y - oldBounds.Y))
                return false;
            changed = true;
        }
        return changed;
    }

    private static ConnectorRoutingRecord ExplicitRecord(VisualStateId id, ConnectorRoutingType mode,
        ImmutableArray<PointD>? definition, ConnectorRoutingAssessment assessment)
    {
        if (definition is { } points && points.Any(static point => point.X < 0d || point.Y < 0d))
            throw new InvalidOperationException("Manual points cannot lie outside the document origin.");
        var path = mode == ConnectorRoutingType.Straight
            ? ImmutableArray.Create(assessment.Source.Point, assessment.Target.Point)
            : [assessment.Source.Point, .. definition.GetValueOrDefault(), assessment.Target.Point];
        return new ConnectorRoutingRecord(id, mode, ConnectorRoutingOutcome.Path, path,
            mode == ConnectorRoutingType.Manual ? definition ?? [] : definition);
    }

    private static bool SameBinding(ConnectorRoutingRecord left, ConnectorRoutingRecord right) =>
        left.AutomaticProof is not { } before || right.AutomaticProof is not { } after ||
        SameEndpointObservation(before.Source, after.Source) && SameEndpointObservation(before.Target, after.Target);

    private static bool SameEndpointObservation(ConnectorRoutingEndpointObservation before, ConnectorRoutingEndpointObservation after) =>
        before.SemanticElementId == after.SemanticElementId && before.VisualStateId == after.VisualStateId &&
        before.AnchorId == after.AnchorId && before.Role == after.Role && before.Side == after.Side &&
        before.Point == after.Point && before.Direction == after.Direction;

    private static bool SamePersistentBindings(DocumentSnapshot before, DocumentSnapshot after,
        Dictionary<SemanticElementId, VisualStateSnapshot[]> beforeOwners,
        Dictionary<SemanticElementId, VisualStateSnapshot[]> afterOwners, VisualStateId id)
    {
        if (!before.VisualModel.TryGetVisualState(id, out var oldVisual) ||
            !after.VisualModel.TryGetVisualState(id, out var newVisual) ||
            !before.SemanticModel.TryGetRelationship(oldVisual!.SemanticElementId, out var oldRelationship) ||
            !after.SemanticModel.TryGetRelationship(newVisual!.SemanticElementId, out var newRelationship)) return false;
        if (oldRelationship!.SourceId != newRelationship!.SourceId || oldRelationship.TargetId != newRelationship.TargetId ||
            oldVisual.SourceAnchorId != newVisual.SourceAnchorId || oldVisual.TargetAnchorId != newVisual.TargetAnchorId) return false;
        return SameEndpoint(oldRelationship.SourceId, oldVisual.SourceAnchorId) &&
            SameEndpoint(oldRelationship.TargetId, oldVisual.TargetAnchorId);

        bool SameEndpoint(SemanticElementId semanticId, ConnectorAnchorId? anchorId)
        {
            var oldOwners = beforeOwners.GetValueOrDefault(semanticId) ?? [];
            var newOwners = afterOwners.GetValueOrDefault(semanticId) ?? [];
            if (oldOwners.Length != 1 || newOwners.Length != 1 || oldOwners[0].Id != newOwners[0].Id) return false;
            if (anchorId is null) return true;
            var oldAnchor = oldOwners[0].ConnectorAnchors.FirstOrDefault(anchor => anchor.Id == anchorId);
            var newAnchor = newOwners[0].ConnectorAnchors.FirstOrDefault(anchor => anchor.Id == anchorId);
            return oldAnchor is not null && newAnchor is not null && oldAnchor.Id == newAnchor.Id &&
                oldAnchor.Role == newAnchor.Role && oldAnchor.Side == newAnchor.Side &&
                (oldAnchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(oldAnchor.Order,
                    oldOwners[0].ConnectorAnchors.Count(anchor => anchor.Side == oldAnchor.Side))) ==
                (newAnchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(newAnchor.Order,
                    newOwners[0].ConnectorAnchors.Count(anchor => anchor.Side == newAnchor.Side)));
        }
    }

    private static bool TryTransportFrame(ConnectorRoutingRecord candidate, ScopeGeometrySnapshot before,
        ScopeGeometrySnapshot after, ConnectorRoutingAssessment endpoints, out ConnectorRoutingRecord result)
    {
        result = candidate;
        var proof = candidate.AutomaticProof!;
        var delta = endpoints.Source.Point - proof.Source.Point;
        if (delta == default || endpoints.Target.Point - proof.Target.Point != delta) return false;
        bool HasFrameDelta(VisualStateId id)
        {
            var previous = before.Nodes.FirstOrDefault(node => node.VisualStateId == id);
            var current = after.Nodes.FirstOrDefault(node => node.VisualStateId == id);
            if (previous is null || current is null || previous.RegionId != current.RegionId ||
                previous.LocalBounds != current.LocalBounds || previous.Transform != current.Transform || current.RegionId is null)
                return false;
            var oldRegion = before.Regions.Single(region => region.Id == current.RegionId);
            var newRegion = after.Regions.Single(region => region.Id == current.RegionId);
            return oldRegion.LocalToScopeTransform is { } oldTransform && newRegion.LocalToScopeTransform is { } newTransform &&
                new VectorD(newTransform.OffsetX - oldTransform.OffsetX, newTransform.OffsetY - oldTransform.OffsetY) == delta;
        }
        if (!HasFrameDelta(endpoints.Source.VisualStateId) || !HasFrameDelta(endpoints.Target.VisualStateId)) return false;
        result = new ConnectorRoutingRecord(candidate.VisualStateId, candidate.RoutingType, candidate.Outcome,
            candidate.Path.Select(point => point + delta), candidate.ManualDefinition,
            new ConnectorAutomaticRouteProof(proof.AlgorithmId, proof.PolicyVersion, proof.ObstacleClearance,
                proof.EndpointLead, endpoints.Source, endpoints.Target));
        return true;
    }
}
