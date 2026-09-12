using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>Builds proposals through the existing handlers; owns no Document or History store.</summary>
internal sealed class CompoundDocumentCommandHandler(
    CommandHandlerRegistry handlers,
    CommandValidationService validation,
    IElementConnectorAnchorPolicyProvider anchorPolicies) : ICommandHandler, ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command) => command is CompoundDocumentCommand
        ? [] : [Error("The compound edit envelope is invalid.")];

    public async ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        if (command is not CompoundDocumentCommand compound)
        {
            return CommandHandlerResult.Failure([Error("The compound edit envelope is invalid.")]);
        }

        var proposed = document;
        var diagnostics = new List<Diagnostic>();
        var invalidation = PipelineInvalidation.None;
        var changedNodes = new HashSet<VisualStateId>();
        foreach (var child in compound.Commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            handlers.TryGet(child.TypeId, out var registration);
            var envelope = validation.ValidateEnvelope(child, proposed, registration);
            diagnostics.AddRange(envelope.Diagnostics);
            if (!envelope.IsValid)
            {
                return CommandHandlerResult.Failure(diagnostics);
            }

            var delegated = validation.ValidateDelegated(child, proposed, cancellationToken);
            diagnostics.AddRange(delegated.Diagnostics);
            if (!delegated.IsValid)
            {
                return CommandHandlerResult.Failure(diagnostics);
            }

            var result = await registration!.Handler.HandleAsync(child, proposed, cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded || result.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            {
                return CommandHandlerResult.Failure(diagnostics);
            }

            var next = result.ProposedDocument!;
            var actual = ChangedComponents(proposed, next);
            if (next.DocumentId != document.DocumentId || next.Revision != document.Revision ||
                (actual & ~child.AffectedComponents) != AuthoritativeDocumentComponent.None)
            {
                return CommandHandlerResult.Failure([Error("A child proposal exceeded its Document, revision, or component authority.")]);
            }

            diagnostics.AddRange(DocumentInvariantValidator.Validate(next, anchorPolicies));
            if (diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            {
                return CommandHandlerResult.Failure(diagnostics);
            }

            invalidation |= result.PipelineInvalidation ?? CommandPipelineInvalidation.Resolve(child);
            if (result.NodeGeometryImpact is { } impact)
            {
                changedNodes.UnionWith(impact.ChangedVisualStateIds);
                // This bounded creation/movement composition does not merge historical/deletion
                // geometry directives. Those operations retain their existing dedicated commands.
                if (impact.HasRemovedVisualStates || impact.HistoricalSourceRevision is not null)
                {
                    return CommandHandlerResult.Failure([Error("Compound spatial edits cannot contain historical or removal geometry directives.")]);
                }
            }
            proposed = next;
        }

        return CommandHandlerResult.Success(proposed, diagnostics, invalidation,
            (invalidation & PipelineInvalidation.NodeLayout) != 0 ? null : changedNodes.Count > 0
                ? NodeGeometryPipelineImpact.ForChangedVisualStates(changedNodes)
                : NodeGeometryPipelineImpact.PreserveAll);
    }

    private static AuthoritativeDocumentComponent ChangedComponents(DocumentSnapshot before, DocumentSnapshot after) =>
        (before.SemanticModel.Equals(after.SemanticModel) ? AuthoritativeDocumentComponent.None : AuthoritativeDocumentComponent.SemanticModel) |
        (before.VisualModel.Equals(after.VisualModel) ? AuthoritativeDocumentComponent.None : AuthoritativeDocumentComponent.VisualModel) |
        (before.Metadata.Equals(after.Metadata) ? AuthoritativeDocumentComponent.None : AuthoritativeDocumentComponent.Metadata) |
        (Equals(before.Publication, after.Publication) ? AuthoritativeDocumentComponent.None : AuthoritativeDocumentComponent.Publication);

    private static Diagnostic Error(string message) => new(
        "INCEPTUS.COMPOUND.INVALID", DiagnosticSeverity.Error, message, CompoundDocumentCommand.KnownTypeId.Value);
}
