using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class SetConnectorRoutingTypeCommandHandler : ICommandHandler, ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command) => command is SetConnectorRoutingTypeCommand
        ? [] : [Error("A routing-type request must use its immutable typed command.")];

    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not SetConnectorRoutingTypeCommand change ||
            !document.VisualModel.TryGetVisualState(change.TargetVisualStateId, out var visual) ||
            visual is null || !document.SemanticModel.TryGetRelationship(visual.SemanticElementId, out _))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure([Error("Routing type requires a current connector visual.")]));
        }
        // The ordered finalizer decides equality: an earlier compound child may have changed mode.
        return ValueTask.FromResult(CommandHandlerResult.SuccessWithPreparation(document,
            [ConnectorRoutingIntent.SetType(change.TargetVisualStateId, change.RoutingType)], [],
            pipelineInvalidation: CommandPipelineInvalidation.ConnectorOnly));
    }
    private static Diagnostic Error(string message) => new("INCEPTUS.ROUTING.TYPE.INVALID", DiagnosticSeverity.Error, message);
}
