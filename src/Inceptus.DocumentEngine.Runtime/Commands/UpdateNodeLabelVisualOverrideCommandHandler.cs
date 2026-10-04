using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateNodeLabelVisualOverrideCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not UpdateNodeLabelVisualOverrideCommand update)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.HandlerFailure,
                    $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                    command.TypeId.Value),
            ]));
        }

        if (!document.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateNotFound,
                    $"Visual state '{update.TargetVisualStateId}' does not exist.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        if (!document.SemanticModel.TryGetElement(existing.SemanticElementId, out _))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes
                        .VisualStateDoesNotSupportNodeLabelVisualOverride,
                    $"Visual state '{update.TargetVisualStateId}' does not represent a semantic element with node-label appearance.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        if (update.TargetOverride is { } targetOverride &&
            !IsWithinDocumentOrigin(document, existing, targetOverride))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                    $"The node-label override for visual state '{update.TargetVisualStateId}' would cross the Document boundary.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        var hasExistingOverride = NodeLabelVisualOverride.TryRead(
            existing.Properties,
            out var existingOverride);
        if (hasExistingOverride == (update.TargetOverride is not null) &&
            existingOverride == update.TargetOverride)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.NodeLabelVisualOverrideUnchanged,
                    $"Visual state '{update.TargetVisualStateId}' already has the requested node-label visual override.",
                    update.TargetVisualStateId.Value),
            ]));
        }

        var replacement = new VisualStateSnapshot(
            existing.Id,
            existing.SemanticElementId,
            existing.Position,
            existing.Size,
            existing.PlacementMode,
            existing.Route,
            NodeLabelVisualOverride.UpdateProperties(
                existing.Properties,
                update.TargetOverride),
            existing.ConnectorAnchors,
            existing.SourceAnchorId,
            existing.TargetAnchorId,
            existing.BoundaryAttachment);
        var proposedVisualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Select(visualState =>
                visualState.Id == replacement.Id ? replacement : visualState),
            document.VisualModel.ProfileElementPresentations,
            document.VisualModel.RoutingScopes);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                document.SemanticModel,
                proposedVisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: PipelineInvalidation.Scene));
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);

    private static bool IsWithinDocumentOrigin(DocumentSnapshot document, VisualStateSnapshot visual,
        NodeLabelVisualOverride value)
    {
        foreach (var scope in document.VisualModel.RoutingScopes ?? [])
        {
            var node = scope.Geometry.Nodes.FirstOrDefault(node => node.VisualStateId == visual.Id);
            if (node is null) continue;
            var bounds = value.ResolveBounds(node.LocalBounds);
            var region = scope.Geometry.Regions.FirstOrDefault(region => region.Id == node.RegionId);
            if (region?.LocalToScopeTransform is { } transform)
                bounds = bounds.Translate(new VectorD(transform.OffsetX, transform.OffsetY));
            return DocumentGeometryBoundary.Contains(bounds);
        }
        return !VisualStatePersistentGeometry.TryResolveAuthoritativeNodeBounds(visual, out var owner) ||
            DocumentGeometryBoundary.Contains(value.ResolveBounds(owner));
    }
}
