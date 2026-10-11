using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Runtime.Projection;

public sealed partial class ProjectionEngine
{
    private readonly Dictionary<SemanticTypeId, PresentationViewProviderRegistration> _presentationProviders;

    public ImmutableArray<DocumentPresentationContext> GetDocumentContexts(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. _presentationProviders.Values.OrderBy(static entry => entry.OwnerTypeId.Value, StringComparer.Ordinal)
            .SelectMany(entry => entry.Provider.GetContexts(document)).Distinct()
            .OrderBy(static context => context.OwnerId.Value, StringComparer.Ordinal)];
    }

    public PresentationViewResolution ResolvePresentation(DocumentSnapshot document, PresentationViewContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        if (context is ProcessScopePresentationContext process)
        {
            if (!document.SemanticModel.TryGetScope(process.ScopeId, out _)) return Invalid("The selected process scope does not exist.");
            var ids = document.SemanticModel.Elements.Where(element => IsInScope(document, element.Id, process.ScopeId))
                .Select(static element => element.Id).ToHashSet();
            var scopePlan = new PresentationViewPlan(document.DocumentId, document.Revision, context,
                ids, document.SemanticModel.Relationships.Where(edge => ids.Contains(edge.SourceId) && ids.Contains(edge.TargetId))
                    .Select(static edge => edge.Id), [new PresentationScopePlacement(process.ScopeId, new VectorD(0, 0))]);
            foreach (var provider in _presentationProviders.Values.OrderBy(static entry => entry.OwnerTypeId.Value, StringComparer.Ordinal)
                .Select(static entry => entry.Provider).OfType<IProcessPresentationViewProvider>())
                scopePlan = provider.IncludeDocumentElements(document, process, scopePlan);
            return ValidatePlan(document, context, scopePlan);
        }

        if (context is not DocumentPresentationContext documentContext ||
            !document.SemanticModel.TryGetElement(documentContext.OwnerId, out var owner) || owner is null ||
            owner.ContainmentKind != SemanticElementContainmentKind.Document ||
            !_presentationProviders.TryGetValue(owner.TypeId, out var registration))
            return Invalid("The selected document presentation owner has no registered view provider.");
        var result = registration.Provider.Resolve(document, documentContext);
        if (!result.Succeeded || result.Plan is not { } plan) return result;
        return ValidatePlan(document, context, plan);
    }

    public ProjectionResult Project(DocumentSnapshot document, PresentationViewContext context,
        ProjectionContext? projectionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is ProcessScopePresentationContext process)
            return Project(document, process.ScopeId, projectionContext, cancellationToken);
        var resolution = ResolvePresentation(document, context);
        return resolution.Plan is { } plan ? Project(document, plan, projectionContext, cancellationToken)
            : ProjectionResult.Failure(document.DocumentId, document.Revision, resolution.Diagnostics);
    }

    public ProjectionResult Project(DocumentSnapshot document, PresentationViewPlan presentation,
        ProjectionContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(presentation);
        var validation = ValidatePlan(document, presentation.Context, presentation);
        if (!validation.Succeeded)
            return ProjectionResult.Failure(document.DocumentId, document.Revision, validation.Diagnostics);
        var authoritative = ResolvePresentation(document, presentation.Context);
        if (authoritative.Plan is not { } resolved ||
            !presentation.ElementIds.SequenceEqual(resolved.ElementIds) ||
            !presentation.RelationshipIds.SequenceEqual(resolved.RelationshipIds) ||
            !presentation.ScopePlacements.SequenceEqual(resolved.ScopePlacements))
            return ProjectionResult.Failure(document.DocumentId, document.Revision,
                authoritative.Succeeded ? Invalid("Presentation membership must match its registered provider.").Diagnostics : authoritative.Diagnostics);
        try
        {
            return ProjectCore(document, presentation.Context, context ?? ProjectionContext.Empty,
                cancellationToken, presentation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(document);
        }
    }

    private static PresentationViewResolution ValidatePlan(DocumentSnapshot document, PresentationViewContext context, PresentationViewPlan plan)
    {
        if (plan.DocumentId != document.DocumentId || plan.SourceRevision != document.Revision || plan.Context != context)
            return Invalid("The presentation membership does not match the current document revision and context.");
        var elements = plan.ElementIds.ToHashSet();
        if (plan.ElementIds.Any(id => !document.SemanticModel.TryGetElement(id, out _)) ||
            plan.RelationshipIds.Any(id => !document.SemanticModel.TryGetRelationship(id, out var edge) || edge is null ||
                !elements.Contains(edge.SourceId) || !elements.Contains(edge.TargetId)))
            return Invalid("Presentation membership contains missing elements or incomplete relationship endpoints.");
        var scopes = plan.ScopePlacements.Select(static placement => placement.ScopeId).ToHashSet();
        if (scopes.Any(id => !document.SemanticModel.TryGetScope(id, out _)) ||
            plan.ElementIds.Any(id => document.SemanticModel.TryGetScope(id, out var scope) && !scopes.Contains(scope!.Id)))
            return Invalid("Every included process element requires exactly one explicit scope placement.");
        return PresentationViewResolution.Success(plan);
    }

    private static PresentationViewResolution Invalid(string message) => PresentationViewResolution.Failure(
        [new Diagnostic("PRESENTATION_CONTEXT_INVALID", DiagnosticSeverity.Error, message)]);
}
