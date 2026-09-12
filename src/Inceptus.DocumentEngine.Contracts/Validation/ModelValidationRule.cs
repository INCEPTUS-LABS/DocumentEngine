using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

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
        ActiveScopeId = activeScopeId;
    }

    public DocumentSnapshot Document { get; }

    public DocumentScopeId ActiveScopeId { get; }

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
