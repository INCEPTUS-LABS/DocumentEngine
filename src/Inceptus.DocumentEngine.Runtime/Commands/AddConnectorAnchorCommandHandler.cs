using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class AddConnectorAnchorCommandHandler : ICommandHandler
{
    private readonly IElementConnectorAnchorPolicyProvider _policyProvider;

    internal AddConnectorAnchorCommandHandler(
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

        if (command is not AddConnectorAnchorCommand add)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.HandlerFailure,
                $"Command type '{command.TypeId}' does not use the expected immutable request shape.");
        }

        if (!document.VisualModel.TryGetVisualState(add.TargetVisualStateId, out var existing) ||
            existing is null)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.VisualStateNotFound,
                $"Visual state '{add.TargetVisualStateId}' does not exist.");
        }

        if (!document.SemanticModel.TryGetElement(
                existing.SemanticElementId,
                out var semanticElement) ||
            semanticElement is null)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportConnectorAnchors,
                $"Visual state '{add.TargetVisualStateId}' does not represent a semantic element that can own connector anchors.");
        }

        var policy = _policyProvider.Resolve(semanticElement.TypeId);
        if (!ElementConnectorAnchorPolicyEvaluator.CanAdd(
                policy,
                existing,
                add.Side,
                add.Role))
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation,
                $"Element type '{semanticElement.TypeId}' does not permit adding a {add.Role} connector anchor on side '{add.Side}'.");
        }

        if (ConnectorAnchorReferenceIdentity.IsPredefinedReference(add.AnchorId))
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation,
                $"Dynamic connector-anchor ID '{add.AnchorId}' uses the reserved predefined-reference identity namespace.");
        }

        if (document.VisualModel.VisualStates.Any(visualState =>
                visualState.ConnectorAnchors.Any(anchor => anchor.Id == add.AnchorId)) ||
            IsPredefinedIdentity(document, add.AnchorId))
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorIdentityDuplicate,
                $"Connector-anchor ID '{add.AnchorId}' already exists in the Document.");
        }

        var sideCount = existing.ConnectorAnchors.Count(anchor => anchor.Side == add.Side);
        if (add.InsertionIndex > sideCount)
        {
            return Failure(
                command,
                CommandExecutionDiagnosticCodes.ConnectorAnchorInsertionIndexInvalid,
                $"Connector-anchor insertion index '{add.InsertionIndex}' is outside side '{add.Side}' of visual state '{add.TargetVisualStateId}'.");
        }

        var replacement = ConnectorAnchorInsertion.Insert(
            existing,
            add.AnchorId,
            add.Side,
            add.Role,
            add.InsertionIndex);
        return ValueTask.FromResult(CommandHandlerResult.Success(Replace(
            document,
            replacement)));
    }

    private bool IsPredefinedIdentity(
        DocumentSnapshot document,
        ConnectorAnchorId anchorId)
    {
        foreach (var visualState in document.VisualModel.VisualStates)
        {
            if (!document.SemanticModel.TryGetElement(
                    visualState.SemanticElementId,
                    out var element) ||
                element is null)
            {
                continue;
            }

            var policy = _policyProvider.Resolve(element.TypeId);
            foreach (var edge in Enum.GetValues<ConnectorAnchorSide>()
                         .Select(policy.ForSide)
                         .Where(static edge =>
                             edge.Mode == ConnectorAnchorPolicyMode.Predefined))
            {
                if (edge.PredefinedAnchors.Any(definition =>
                        ConnectorAnchorReferenceIdentity.ForPredefined(
                            visualState.Id,
                            definition.Id) == anchorId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DocumentSnapshot Replace(
        DocumentSnapshot document,
        VisualStateSnapshot replacement) =>
        new(
            document.SemanticModel,
            new VisualModelSnapshot(
                document.DocumentId,
                document.Revision,
                document.VisualModel.VisualStates.Select(visualState =>
                    visualState.Id == replacement.Id ? replacement : visualState),
                document.VisualModel.ProfileElementPresentations),
            document.Metadata,
            document.Publication);

    private static ValueTask<CommandHandlerResult> Failure(
        ICommand command,
        string code,
        string message) =>
        ValueTask.FromResult(CommandHandlerResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, command.TypeId.Value),
        ]));
}
