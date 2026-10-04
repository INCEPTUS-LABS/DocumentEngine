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
        var routingIntents = new List<ConnectorRoutingIntent>();
        var heightIntents = new List<SpatialRegionHeightIntent>();
        var widthIntents = new List<SpatialScopeWidthIntent>();
        var hasProposal = false;
        var requiresStructuralGeometryComparison = false;
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

            if (result.IsNoChange) continue;
            hasProposal = true;

            var next = result.ProposedDocument!;
            var actual = ChangedComponents(proposed, next);
            if (next.DocumentId != document.DocumentId || next.Revision != document.Revision ||
                ((!result.RoutingIntents.IsEmpty || !result.SpatialHeightIntents.IsEmpty || !result.SpatialWidthIntents.IsEmpty) &&
                    (child.AffectedComponents & AuthoritativeDocumentComponent.VisualModel) == 0) ||
                (actual & ~child.AffectedComponents) != AuthoritativeDocumentComponent.None)
            {
                return CommandHandlerResult.Failure([Error("A child proposal exceeded its Document, revision, or component authority.")]);
            }

            diagnostics.AddRange(DocumentInvariantValidator.Validate(CommandProcessor.WithoutRoutingScopes(next), anchorPolicies));
            if (diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error))
            {
                return CommandHandlerResult.Failure(diagnostics);
            }

            invalidation |= result.PipelineInvalidation ?? CommandPipelineInvalidation.Resolve(child);
            routingIntents.AddRange(result.RoutingIntents);
            heightIntents.AddRange(result.SpatialHeightIntents);
            widthIntents.AddRange(result.SpatialWidthIntents);
            if (result.NodeGeometryImpact is { } impact)
            {
                changedNodes.UnionWith(impact.ChangedVisualStateIds);
                if (impact.HasRemovedVisualStates || impact.HistoricalSourceRevision is not null)
                {
                    if (document.VisualModel.RoutingScopes is null)
                        return CommandHandlerResult.Failure([Error("Unprepared compound spatial edits cannot contain historical or removal geometry directives.")]);
                    // The saved-state preparer compares the final structure once; a single legacy
                    // changed/removed hint cannot describe a mixed compound's complete effect.
                    requiresStructuralGeometryComparison = true;
                }
            }
            proposed = next;
        }

        if (!hasProposal) return CommandHandlerResult.NoChange(diagnostics);
        return CommandHandlerResult.SuccessWithPreparation(proposed, routingIntents, heightIntents, widthIntents, diagnostics, invalidation,
            (invalidation & PipelineInvalidation.NodeLayout) != 0 || requiresStructuralGeometryComparison ? null : changedNodes.Count > 0
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
