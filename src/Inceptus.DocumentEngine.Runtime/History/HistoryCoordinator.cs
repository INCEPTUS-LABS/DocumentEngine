using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Runtime.History;

/// <summary>
/// Prepares History mutations. It neither executes Commands nor installs Document state.
/// </summary>
internal sealed class HistoryCoordinator
{
    private readonly HistoryPolicyRegistry _policies;

    internal HistoryCoordinator(IEnumerable<CommandHistoryPolicyRegistration> registrations) =>
        _policies = new HistoryPolicyRegistry(registrations);

    internal HistoryMutationPreparationResult PrepareRecord(
        HistoryStore store,
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (before.DocumentId != committed.DocumentId ||
            command.TargetDocumentId != before.DocumentId ||
            command.ExpectedRevision != before.Revision ||
            committed.Revision != before.Revision.Increment())
        {
            return Failure(
                HistoryDiagnosticCodes.InvalidPreparation,
                "History preparation requires coherent before and committed revisions.",
                command.TypeId.Value);
        }

        if (!_policies.TryGet(command.TypeId, out var policy))
        {
            return Failure(
                HistoryDiagnosticCodes.MissingPolicy,
                $"Command type '{command.TypeId}' has no explicit History policy.",
                command.TypeId.Value);
        }

        CommandHistoryPreparationResult preparation;
        try
        {
            preparation = policy!.Prepare(command, before, committed);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return Failure(
                HistoryDiagnosticCodes.PolicyFailure,
                "The Command History policy failed during commit preparation.",
                command.TypeId.Value,
                exception);
        }

        if (preparation is null || !preparation.Succeeded)
        {
            var diagnostics = preparation?.Diagnostics ?? [];
            return HistoryMutationPreparationResult.Failure(
                diagnostics.Add(new Diagnostic(
                    HistoryDiagnosticCodes.PolicyFailure,
                    DiagnosticSeverity.Error,
                    "The Command History policy did not prepare a valid result.",
                    command.TypeId.Value)));
        }

        if (preparation.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return HistoryMutationPreparationResult.Failure(preparation.Diagnostics);
        }

        if (preparation.Behavior == HistoryRecordingBehavior.PreserveExistingHistory)
        {
            return HistoryMutationPreparationResult.Success(diagnostics: preparation.Diagnostics);
        }

        if (preparation.Behavior == HistoryRecordingBehavior.NotUndoable)
        {
            var discardRedo = store.PrepareNotUndoable();
            return !discardRedo.Succeeded || preparation.Diagnostics.IsEmpty
                ? discardRedo
                : HistoryMutationPreparationResult.Success(
                    discardRedo.Mutation,
                    preparation.Diagnostics.Concat(discardRedo.Diagnostics));
        }

        if (preparation.Behavior != HistoryRecordingBehavior.Undoable ||
            preparation.UndoFactory is null ||
            preparation.RedoFactory is null)
        {
            return Failure(
                HistoryDiagnosticCodes.InvalidPreparation,
                "An undoable History policy must supply inverse and redo Command factories.",
                command.TypeId.Value);
        }

        var entry = new HistoryEntry(
            command.TypeId,
            preparation.UndoFactory,
            preparation.RedoFactory,
            preparation.RoutingTypeDeltas,
            preparation.SpatialHeightDeltas,
            CaptureNodeGeometryDeltas(before, committed), preparation.SpatialWidthDeltas);
        var record = store.PrepareRecord(entry);
        return !record.Succeeded || preparation.Diagnostics.IsEmpty
            ? record
            : HistoryMutationPreparationResult.Success(
                record.Mutation,
                preparation.Diagnostics.Concat(record.Diagnostics));
    }

    private static ImmutableArray<NodeGeometryHistoryDelta> CaptureNodeGeometryDeltas(
        DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (before.VisualModel.RoutingScopes is not { } beforeScopes ||
            committed.VisualModel.RoutingScopes is not { } afterScopes) return [];
        var beforeNodes = beforeScopes.SelectMany(scope => scope.Geometry.Nodes.Select(node =>
            new NodeGeometryHistorySeed(scope.ScopeId, node))).ToDictionary(seed => seed.Geometry.VisualStateId);
        var afterNodes = afterScopes.SelectMany(scope => scope.Geometry.Nodes.Select(node =>
            new NodeGeometryHistorySeed(scope.ScopeId, node))).ToDictionary(seed => seed.Geometry.VisualStateId);
        var deltas = ImmutableArray.CreateBuilder<NodeGeometryHistoryDelta>();
        foreach (var visual in committed.VisualModel.VisualStates)
        {
            if (!before.VisualModel.TryGetVisualState(visual.Id, out var prior) ||
                !beforeNodes.TryGetValue(visual.Id, out var oldNode) ||
                !afterNodes.TryGetValue(visual.Id, out var newNode) || oldNode == newNode) continue;
            if (prior!.Position != visual.Position || prior.Size != visual.Size ||
                prior.PlacementMode != visual.PlacementMode || !Equals(prior.BoundaryAttachment, visual.BoundaryAttachment))
                deltas.Add(new NodeGeometryHistoryDelta(oldNode, newNode));
        }
        return deltas.ToImmutable();
    }

    private static HistoryMutationPreparationResult Failure(
        string code,
        string message,
        string sourceIdentity,
        Exception? exception = null)
    {
        var context = exception is null
            ? Array.Empty<KeyValuePair<string, string>>()
            :
            [
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name),
            ];
        return HistoryMutationPreparationResult.Failure(
        [
            new Diagnostic(
                code,
                DiagnosticSeverity.Error,
                message,
                sourceIdentity,
                context),
        ]);
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}
