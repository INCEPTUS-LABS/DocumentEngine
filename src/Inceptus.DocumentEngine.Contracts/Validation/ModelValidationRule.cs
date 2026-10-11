using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Contracts.Validation;

public sealed class ModelValidationContext
{
    public ModelValidationContext(DocumentSnapshot document)
        : this(document, ResolveRootScopeId(document))
    {
    }

    public ModelValidationContext(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        if (activeScopeId != document.SemanticModel.RootScopeId &&
            !document.SemanticModel.NestedScopes.Any(scope => scope.Id == activeScopeId))
        {
            throw new ArgumentException(
                $"Document scope '{activeScopeId}' does not exist in the validation Document.",
                nameof(activeScopeId));
        }

        Document = document;
        PresentationContext = new ProcessScopePresentationContext(activeScopeId);
    }

    public ModelValidationContext(DocumentSnapshot document, PresentationViewPlan presentationPlan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(presentationPlan);
        if (presentationPlan.DocumentId != document.DocumentId ||
            presentationPlan.SourceRevision != document.Revision)
        {
            throw new ArgumentException("The presentation plan must describe the exact validation Document revision.", nameof(presentationPlan));
        }

        var semantic = document.SemanticModel;
        var validContext = presentationPlan.Context switch
        {
            ProcessScopePresentationContext process => ScopeExists(document, process.ScopeId),
            DocumentPresentationContext context => semantic.TryGetElement(context.OwnerId, out var owner) &&
                owner!.ContainmentKind == SemanticElementContainmentKind.Document,
            _ => false,
        };
        if (!validContext ||
            presentationPlan.ScopePlacements.Any(placement => !ScopeExists(document, placement.ScopeId)) ||
            presentationPlan.ElementIds.Any(id => !semantic.TryGetElement(id, out _)) ||
            presentationPlan.RelationshipIds.Any(id => !semantic.TryGetRelationship(id, out _)))
        {
            throw new ArgumentException("The presentation plan must refer to existing validation Document membership.", nameof(presentationPlan));
        }

        Document = document;
        PresentationContext = presentationPlan.Context;
        PresentationPlan = presentationPlan;
    }

    public DocumentSnapshot Document { get; }

    public PresentationViewContext PresentationContext { get; }

    public PresentationViewPlan? PresentationPlan { get; }

    public DocumentScopeId ActiveScopeId => PresentationContext is ProcessScopePresentationContext process
        ? process.ScopeId
        : throw new InvalidOperationException("Document presentation validation has no active Process scope.");

    public bool IncludesScope(DocumentScopeId scopeId) => PresentationContext is ProcessScopePresentationContext process
        ? process.ScopeId == scopeId
        : PresentationPlan!.ScopePlacements.Any(placement => placement.ScopeId == scopeId);

    public bool Contains(SemanticElementId semanticElementId) => PresentationPlan is { } plan
        ? plan.Contains(semanticElementId)
        : Document.SemanticModel.TryGetScope(semanticElementId, out var scope) && scope is not null && IncludesScope(scope.Id);

    private static bool ScopeExists(DocumentSnapshot document, DocumentScopeId scopeId) =>
        scopeId == document.SemanticModel.RootScopeId ||
        document.SemanticModel.NestedScopes.Any(scope => scope.Id == scopeId);

    private static DocumentScopeId ResolveRootScopeId(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.SemanticModel.RootScopeId;
    }
}

public interface IModelValidationRule
{
    ModelValidationRuleId RuleId { get; }

    ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context);
}
