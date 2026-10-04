using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.Runtime.Commands;

public sealed partial class CommandProcessor
{
    private static bool OnlySavedCaptionsChanged(ImmutableArray<ScopeRoutingSnapshot>? before,
        ImmutableArray<ScopeRoutingSnapshot>? after)
    {
        if (before is not { } oldScopes || after is not { } newScopes || oldScopes.Length != newScopes.Length)
            return false;
        for (var index = 0; index < oldScopes.Length; index++)
        {
            var old = oldScopes[index];
            var next = newScopes[index];
            var previous = old.Geometry;
            var geometry = next.Geometry;
            if (old.ScopeId != next.ScopeId || !old.Connectors.AsSpan().SequenceEqual(next.Connectors.AsSpan()) ||
                previous.PolicyId != geometry.PolicyId || previous.PolicyVersion != geometry.PolicyVersion ||
                previous.LayoutAlgorithmId != geometry.LayoutAlgorithmId || !previous.Configuration.Equals(geometry.Configuration) ||
                !previous.TextConfiguration.Equals(geometry.TextConfiguration) ||
                !previous.Contributors.AsSpan().SequenceEqual(geometry.Contributors.AsSpan()) ||
                !previous.Nodes.AsSpan().SequenceEqual(geometry.Nodes.AsSpan()) ||
                !previous.Regions.AsSpan().SequenceEqual(geometry.Regions.AsSpan()) ||
                !previous.TextMeasurements.AsSpan().SequenceEqual(geometry.TextMeasurements.AsSpan()) ||
                !previous.SpatialWidths.AsSpan().SequenceEqual(geometry.SpatialWidths.AsSpan()))
                return false;
        }
        return true;
    }

    private (CommandExecutionResult Result, bool StartDispatch, DocumentDispatchWorkItem? WorkItem)
        CompleteNoChangeUnderGate(Document document, ICommand command, CommandTransaction transaction,
            HistoryStore? historyStore, PreparedHistoryMutation? preparedHistoryMutation,
            CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(document.CaptureState(), transaction.BaseState) ||
            (preparedHistoryMutation is not null &&
                (historyStore is null || !historyStore.IsCurrent(preparedHistoryMutation))))
        {
            transaction.MarkAborted();
            return (Failure(transaction.DocumentId, command, CommandExecutionStatus.InternalFailure,
                transaction.BaseRevision, Error(HistoryDiagnosticCodes.InvalidPreparation,
                    "The unchanged operation no longer matches the current Document and History state.",
                    command.TypeId.Value)), false, null);
        }

        var result = CommandExecutionResult.CreateNoChange(transaction.DocumentId, command.TypeId,
            transaction.BaseRevision, transaction.Diagnostics);
        transaction.MarkPrepared();
        _executionCheckpointObserver?.Invoke(CommandExecutionCheckpoint.BeforeFinalCancellationCheck);
        cancellationToken.ThrowIfCancellationRequested();
        _executionCheckpointObserver?.Invoke(CommandExecutionCheckpoint.AfterFinalCancellationCheck);
        // A successful replay consumes its prepared cursor even when its owned value already holds.
        // No Document replacement or committed event accompanies that cursor-only transition.
        if (preparedHistoryMutation is not null) historyStore!.InstallPrepared(preparedHistoryMutation);
        transaction.MarkCommitted();
        return (result, false, null);
    }

    private static ImmutableArray<ConnectorRoutingIntent> MergeReplayIntents(
        ImmutableArray<ConnectorRoutingIntent> handlerIntents,
        ImmutableArray<ConnectorRoutingIntent> replayIntents)
    {
        if (replayIntents.IsEmpty) return handlerIntents;
        var result = handlerIntents.ToBuilder();
        foreach (var intent in replayIntents)
        {
            if (!Equals(result.LastOrDefault(candidate => candidate.VisualStateId == intent.VisualStateId), intent))
                result.Add(intent);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<SpatialRegionHeightIntent> MergeReplayHeightIntents(
        ImmutableArray<SpatialRegionHeightIntent> handlerIntents,
        ImmutableArray<SpatialRegionHeightIntent> replayIntents)
    {
        if (replayIntents.IsEmpty) return handlerIntents;
        var result = handlerIntents.ToBuilder();
        foreach (var intent in replayIntents)
        {
            if (result.LastOrDefault(candidate => candidate.ScopeId == intent.ScopeId &&
                    candidate.RegionId == intent.RegionId) != intent)
                result.Add(intent);
        }
        return result.ToImmutable();
    }

    private static bool IsRoutingNoChange(DocumentSnapshot before, DocumentSnapshot proposed,
        ImmutableArray<ConnectorRoutingIntent> routingIntents,
        ImmutableArray<SpatialRegionHeightIntent> heightIntents,
        ImmutableArray<SpatialScopeWidthIntent> widthIntents)
    {
        if (before.VisualModel.RoutingScopes is not { } scopes ||
            (routingIntents.IsEmpty && heightIntents.IsEmpty && widthIntents.IsEmpty) ||
            !ContentEqualsIgnoringRevision(WithoutRoutingScopes(before), WithoutRoutingScopes(proposed)))
            return false;
        foreach (var intent in routingIntents)
        {
            var record = scopes.SelectMany(static scope => scope.Connectors)
                .FirstOrDefault(candidate => candidate.VisualStateId == intent.VisualStateId);
            if (record is null) return false;
            if (intent.Kind == ConnectorRoutingIntentKind.SetType && record.RoutingType == intent.RoutingType)
                continue;
            if (intent.Kind == ConnectorRoutingIntentKind.ReplaceManualDefinition &&
                record.RoutingType == ConnectorRoutingType.Manual && record.ManualDefinition is { } definition &&
                definition.AsSpan().SequenceEqual(intent.ManualDefinition.AsSpan()) &&
                (intent.SourcePoint is null || (record.Path.Length >= 2 &&
                    record.Path[0] == intent.SourcePoint && record.Path[^1] == intent.TargetPoint))) continue;
            return false;
        }
        foreach (var intent in heightIntents)
        {
            var region = scopes.FirstOrDefault(scope => scope.ScopeId == intent.ScopeId)?.Geometry.Regions
                .FirstOrDefault(candidate => candidate.Id == intent.RegionId);
            if (region is null || region.ExpandedHeight != intent.ExpandedHeight) return false;
        }
        foreach (var intent in widthIntents)
        {
            var width = scopes.FirstOrDefault(scope => scope.ScopeId == intent.ScopeId)?.Geometry.SpatialWidths
                .FirstOrDefault(candidate => candidate.ProfileId == intent.ProfileId);
            if (width is null || width.OuterWidth != intent.OuterWidth) return false;
        }
        return true;
    }

    private async ValueTask<ConnectorRoutingStatePreparationResult> PrepareRoutingStateAsync(
        DocumentSnapshot before, DocumentSnapshot proposed,
        ImmutableArray<ConnectorRoutingIntent> routingIntents,
        ImmutableArray<SpatialRegionHeightIntent> heightIntents,
        ImmutableArray<SpatialScopeWidthIntent> widthIntents,
        NodeGeometryPipelineImpact? nodeGeometryImpact, bool isHistoryReplay,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _routingStatePreparer!.PrepareAsync(new ConnectorRoutingStatePreparationRequest(
                before, proposed, routingIntents, heightIntents, widthIntents, nodeGeometryImpact, isHistoryReplay),
                cancellationToken).ConfigureAwait(false) ?? ConnectorRoutingStatePreparationResult.Failure([
                    Error("INCEPTUS.ROUTING.PREPARER.INVALID", "Routing preparation returned no result.", before.DocumentId.Value)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return ConnectorRoutingStatePreparationResult.Failure([Error("INCEPTUS.ROUTING.PREPARER.FAILED",
                "Routing preparation failed before installation.", before.DocumentId.Value,
                new KeyValuePair<string, string>("ExceptionType", ExceptionType(exception)))]);
        }
    }

    internal static DocumentSnapshot WithoutRoutingScopes(DocumentSnapshot document) =>
        ReplaceRoutingScopes(document, null);

    private static DocumentSnapshot ApplyReplayNodeGeometrySeeds(DocumentSnapshot document,
        ImmutableArray<NodeGeometryHistorySeed> seeds)
    {
        if (seeds.IsEmpty || document.VisualModel.RoutingScopes is not { } scopes) return document;
        return ReplaceRoutingScopes(document, scopes.Select(scope =>
        {
            var owned = seeds.Where(seed => seed.ScopeId == scope.ScopeId &&
                document.VisualModel.TryGetVisualState(seed.Geometry.VisualStateId, out _))
                .ToDictionary(seed => seed.Geometry.VisualStateId, seed => seed.Geometry);
            if (owned.Count == 0) return scope;
            var geometry = scope.Geometry;
            return new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(
                geometry.PolicyId, geometry.PolicyVersion, geometry.LayoutAlgorithmId,
                geometry.Configuration, geometry.TextConfiguration, geometry.Contributors,
                geometry.Nodes.Select(node => owned.GetValueOrDefault(node.VisualStateId, node)),
                geometry.Regions, geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors);
        }));
    }

    private static DocumentSnapshot ReplaceRoutingScopes(DocumentSnapshot document,
        IEnumerable<ScopeRoutingSnapshot>? scopes) => new(document.SemanticModel,
            new VisualModelSnapshot(document.DocumentId, document.Revision, document.VisualModel.VisualStates,
                document.VisualModel.ProfileElementPresentations, scopes), document.Metadata, document.Publication);

    internal static bool ContentEqualsIgnoringRevision(DocumentSnapshot before, DocumentSnapshot after) =>
        DocumentSnapshotCloner.CloneAtRevision(before, after.Revision).Equals(after);

    private static ImmutableArray<Diagnostic> ValidateRoutingOwnership(DocumentSnapshot before,
        DocumentSnapshot prepared, ImmutableArray<ConnectorRoutingIntent> routingIntents,
        ImmutableArray<SpatialRegionHeightIntent> heightIntents,
        ImmutableArray<SpatialScopeWidthIntent> widthIntents)
    {
        if (before.VisualModel.RoutingScopes is not { } beforeScopes) return [];
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var afterRecords = prepared.VisualModel.RoutingScopes.GetValueOrDefault()
            .SelectMany(static scope => scope.Connectors).ToDictionary(static record => record.VisualStateId);
        foreach (var record in beforeScopes.SelectMany(static scope => scope.Connectors))
        {
            if (!afterRecords.TryGetValue(record.VisualStateId, out var after)) continue;
            var intents = routingIntents.Where(intent => intent.VisualStateId == record.VisualStateId).ToArray();
            var ownedType = intents.LastOrDefault(static intent => intent.Kind == ConnectorRoutingIntentKind.SetType)?.RoutingType;
            if (after.RoutingType != (ownedType ?? record.RoutingType))
                diagnostics.Add(Error("INCEPTUS.ROUTING.TYPE.AUTHORITY",
                    "Routing preparation changed a surviving connector's type without its explicit intent.", record.VisualStateId.Value));
            var expectedDefinition = record.ManualDefinition;
            var orderedMode = record.RoutingType;
            var capturesDefinition = false;
            foreach (var intent in intents)
            {
                if (intent.Kind == ConnectorRoutingIntentKind.SetType)
                {
                    orderedMode = intent.RoutingType!.Value;
                    if (orderedMode == ConnectorRoutingType.Manual && expectedDefinition is null)
                        capturesDefinition = true;
                }
                else if (intent.Kind == ConnectorRoutingIntentKind.ReplaceManualDefinition && orderedMode == ConnectorRoutingType.Manual)
                {
                    expectedDefinition = intent.ManualDefinition;
                    capturesDefinition = false;
                }
                else if (intent.Kind == ConnectorRoutingIntentKind.Recalculate && orderedMode == ConnectorRoutingType.Manual)
                {
                    expectedDefinition = [];
                    capturesDefinition = false;
                }
            }
            if (!capturesDefinition && (expectedDefinition.HasValue != after.ManualDefinition.HasValue ||
                !expectedDefinition.GetValueOrDefault().AsSpan().SequenceEqual(after.ManualDefinition.GetValueOrDefault().AsSpan())))
                diagnostics.Add(Error("INCEPTUS.ROUTING.MANUAL.AUTHORITY",
                    "Routing preparation did not preserve or apply the explicitly owned manual definition.", record.VisualStateId.Value));
        }
        foreach (var scope in beforeScopes)
        {
            var afterScope = prepared.VisualModel.RoutingScopes.GetValueOrDefault()
                .FirstOrDefault(candidate => candidate.ScopeId == scope.ScopeId);
            if (afterScope is null) continue;
            foreach (var width in scope.Geometry.SpatialWidths)
            {
                var after = afterScope.Geometry.SpatialWidths.FirstOrDefault(candidate => candidate.ProfileId == width.ProfileId);
                var ownedWidth = widthIntents.LastOrDefault(intent => intent.ScopeId == scope.ScopeId &&
                    intent.ProfileId == width.ProfileId)?.OuterWidth;
                if (after is null || after.OuterWidth != (ownedWidth ?? width.OuterWidth))
                    diagnostics.Add(Error("INCEPTUS.ROUTING.WIDTH.AUTHORITY",
                        "Routing preparation changed a saved scope width without its explicit intent.", scope.ScopeId.Value));
            }
            foreach (var region in scope.Geometry.Regions)
            {
                var after = afterScope.Geometry.Regions.FirstOrDefault(candidate => candidate.Id == region.Id);
                if (after is null) continue;
                var ownedHeight = heightIntents.LastOrDefault(intent => intent.ScopeId == scope.ScopeId &&
                    intent.RegionId == region.Id)?.ExpandedHeight;
                if (after.ExpandedHeight != (ownedHeight ?? region.ExpandedHeight))
                    diagnostics.Add(Error("INCEPTUS.ROUTING.HEIGHT.AUTHORITY",
                        "Routing preparation changed a saved region height without its explicit intent.", region.Id.Value));
            }
        }
        return diagnostics.ToImmutable();
    }
}
