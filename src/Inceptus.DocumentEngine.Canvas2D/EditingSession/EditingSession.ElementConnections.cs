using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private readonly ConnectorRoutingStatePreparer? _elementConnectionRoutingPreparer;

    internal async ValueTask<AnchorConnectionCreationPlan?> ChooseElementConnectionAsync(DocumentSnapshot document,
        ImmutableArray<ElementConnectionCandidate> candidates, CancellationToken cancellationToken)
    {
        AnchorConnectionCreationPlan? best = null;
        (int Crossings, int Bends, double Length) bestScore = (int.MaxValue, int.MaxValue, double.MaxValue);
        if (_elementConnectionRoutingPreparer is null) return null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proposal = candidate.Proposal;
            var prepared = await _elementConnectionRoutingPreparer.PrepareAsync(new(document, proposal.ProposedDocument!,
                proposal.RoutingIntents, proposal.SpatialHeightIntents, proposal.SpatialWidthIntents,
                proposal.NodeGeometryImpact, false), cancellationToken).ConfigureAwait(false);
            if (!prepared.Succeeded) continue;
            var ownerRecords = prepared.RoutingScopes.FirstOrDefault(scope => scope.Connectors.Any(route =>
                    route.VisualStateId == candidate.Plan.CreatedConnectorVisualStateId))?.Connectors ??
                prepared.DocumentRoutingStates.FirstOrDefault(owner => owner.Connectors.Any(route =>
                    route.VisualStateId == candidate.Plan.CreatedConnectorVisualStateId))?.Connectors ?? [];
            var record = ownerRecords.SingleOrDefault(route => route.VisualStateId == candidate.Plan.CreatedConnectorVisualStateId);
            if (record?.Outcome != ConnectorRoutingOutcome.Path) continue;
            var score = ConnectorPathQuality.Score(record.Path, ownerRecords
                .Where(existing => existing.VisualStateId != record.VisualStateId).Select(existing => existing.Path));
            // Candidate order is canonical (anchor identity, side, edge parameter); ties keep that order.
            if (score.CompareTo(bestScore) < 0) { best = candidate.Plan; bestScore = score; }
        }
        return best;
    }

    internal async ValueTask<HistoryOperationResult> CompleteElementConnectionAsync(EditingSessionState expected,
        ElementConnectionEndpoint source, ElementConnectionEndpoint target, ICommand command, CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
            return HistoryOperationResult.CreateNotCommitted(expected.DocumentId, command.TypeId, HistoryOperationStatus.Cancelled,
                expected.DocumentRevision, expected.HistoryStatus, [CancelledDiagnostic(expected.DocumentId.Value)]);
        try
        {
            lock (_sync)
            {
                var state = CaptureStateUnderLock();
                var document = AttachedDocument.CaptureSnapshot();
                if (_closing || _closed || state.Status != EditingSessionStatus.Ready || state.Generation != expected.Generation ||
                    !ReferenceEquals(state.CurrentScene, expected.CurrentScene) || !ReferenceEquals(state.EditorState, expected.EditorState) ||
                    state.PresentationContext != expected.PresentationContext || command.TargetDocumentId != document.DocumentId ||
                    command.ExpectedRevision != document.Revision || ConnectionToolState.GetPendingSource(state.EditorState)?.Source != source ||
                    !Current(source) || !Current(target))
                    return InteractionCommandRejected(command, state, Canvas2DInteractionDiagnosticCodes.StaleScene,
                        "The acquired elements no longer belong to the exact current document presentation.");

                bool Current(ElementConnectionEndpoint endpoint)
                {
                    var item = state.CurrentScene!.Items.FirstOrDefault(item => item.Id == endpoint.SceneId);
                    document.SemanticModel.TryGetScope(endpoint.SemanticId, out var scope);
                    return item?.Origin.SemanticElementId == endpoint.SemanticId && item.Origin.VisualStateId == endpoint.VisualId &&
                        item.Origin.ProjectedObjectId == endpoint.ProjectedId && scope?.Id == endpoint.OriginatingScopeId &&
                        IsCurrentSceneTarget(document, state.CurrentScene, state.PresentationContext, endpoint.SceneId, item.SpatialRegion);
                }
            }
            var result = await History.ExecuteAsync(_commandProcessor, command, cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally { _commandGate.Release(); }
    }
}
