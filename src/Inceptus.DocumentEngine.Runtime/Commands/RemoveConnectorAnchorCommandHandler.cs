using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class RemoveConnectorAnchorCommandHandler : ICommandHandler
{
    private readonly IElementConnectorAnchorPolicyProvider _policyProvider;

    internal RemoveConnectorAnchorCommandHandler(
        IElementConnectorAnchorPolicyProvider? policyProvider = null) =>
        _policyProvider = policyProvider ?? ElementConnectorAnchorPolicyRegistry.Default;

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not RemoveConnectorAnchorCommand remove)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.HandlerFailure,
                $"Command type '{command.TypeId}' does not use the expected immutable request shape.");
        }

        if (!document.VisualModel.TryGetVisualState(
                remove.TargetVisualStateId,
                out var existing) ||
            existing is null)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.VisualStateNotFound,
                $"Visual state '{remove.TargetVisualStateId}' does not exist.");
        }

        if (!document.SemanticModel.TryGetElement(
                existing.SemanticElementId,
                out var semanticElement) ||
            semanticElement is null)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportConnectorAnchors,
                $"Visual state '{remove.TargetVisualStateId}' does not represent a semantic element that can own connector anchors.");
        }

        var removed = existing.ConnectorAnchors.SingleOrDefault(anchor =>
            anchor.Id == remove.AnchorId);
        if (removed is null)
        {
            var policy = _policyProvider.Resolve(semanticElement.TypeId);
            if (IsPredefinedIdentity(existing, policy, remove.AnchorId))
            {
                return Failure(
                    command,
                    CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation,
                    $"Predefined connector anchor '{remove.AnchorId}' is supplied by element type '{semanticElement.TypeId}' and cannot be removed.");
            }

            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorNotFound,
                $"Connector anchor '{remove.AnchorId}' does not exist on visual state '{remove.TargetVisualStateId}'.");
        }


        var elementPolicy = _policyProvider.Resolve(semanticElement.TypeId);
        if (!ElementConnectorAnchorPolicyEvaluator.CanRemove(
                elementPolicy,
                existing,
                remove.AnchorId))
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation,
                $"Element type '{semanticElement.TypeId}' does not permit removing connector anchor '{remove.AnchorId}'.");
        }

        if (ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, remove.AnchorId))
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorInUse,
                $"Connector anchor '{remove.AnchorId}' is referenced by a connector and cannot be removed.");
        }

        var anchors = existing.ConnectorAnchors
            .Where(anchor => anchor.Id != remove.AnchorId)
            .Select(anchor =>
                anchor.Side == removed.Side && anchor.Order > removed.Order
                    ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order - 1)
                    : anchor);
        var replacement = new VisualStateSnapshot(
            existing.Id,
            existing.SemanticElementId,
            existing.Position,
            existing.Size,
            existing.PlacementMode,
            existing.Route,
            existing.Properties,
            anchors,
            existing.SourceAnchorId,
            existing.TargetAnchorId,
            existing.BoundaryAttachment);
        var proposedVisualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Select(visualState =>
                visualState.Id == replacement.Id ? replacement : visualState),
            document.VisualModel.ProfileElementPresentations);
        return ValueTask.FromResult(CommandHandlerResult.Success(new DocumentSnapshot(
            document.SemanticModel,
            proposedVisualModel,
            document.Metadata,
            document.Publication)));
    }

    private static bool IsPredefinedIdentity(
        VisualStateSnapshot visualState,
        ElementConnectorAnchorPolicy policy,
        ConnectorAnchorId anchorId) =>
        Enum.GetValues<ConnectorAnchorSide>()
            .Select(policy.ForSide)
            .Where(static edge => edge.Mode == ConnectorAnchorPolicyMode.Predefined)
            .SelectMany(static edge => edge.PredefinedAnchors)
            .Any(definition =>
                ConnectorAnchorReferenceIdentity.ForPredefined(
                    visualState.Id,
                    definition.Id) == anchorId);

    private static ValueTask<CommandHandlerResult> Failure(
        ICommand command,
        string code,
        string message) =>
        ValueTask.FromResult(CommandHandlerResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, command.TypeId.Value),
        ]));
}
