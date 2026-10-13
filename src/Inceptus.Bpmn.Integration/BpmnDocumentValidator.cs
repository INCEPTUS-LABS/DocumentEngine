using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.Bpmn.Integration;

/// <summary>Runs the existing document invariants and notation rules independently of an editor.</summary>
public sealed class BpmnDocumentValidator
{
    private readonly ModelValidationEngine _engine;

    public BpmnDocumentValidator() : this(BpmnIntegrationPolicy.ValidationRules) { }

    public BpmnDocumentValidator(ModelValidationCatalog rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _engine = new ModelValidationEngine(rules);
    }

    public BpmnDocumentValidationResult Validate(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var reconstruction = DocumentReconstructor.Reconstruct(document, BpmnIntegrationPolicy.ConnectorAnchors);
        if (!reconstruction.Succeeded)
            return new(reconstruction.Diagnostics, []);

        var scopes = new[] { document.SemanticModel.RootScopeId }
            .Concat(document.SemanticModel.NestedScopes.Select(static scope => scope.Id))
            .OrderBy(static scope => scope.Value, StringComparer.Ordinal);
        return new([], scopes.Select(scope => _engine.Validate(new ModelValidationContext(document, scope)))
            .ToImmutableArray());
    }

    /// <summary>Preserves the existing view-specific report, including supplied routing findings.</summary>
    public ValidationSnapshot Validate(ModelValidationContext context,
        IEnumerable<ModelValidationIssue>? routingIssues = null, long? sourceGeneration = null) =>
        _engine.Validate(context, routingIssues, sourceGeneration);
}

public sealed record BpmnDocumentValidationResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<ValidationSnapshot> Reports)
{
    public bool IsStructurallyValid => !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    public ImmutableArray<ModelValidationIssue> Issues => Reports.SelectMany(static report => report.Issues)
        .DistinctBy(static issue => issue.Id).ToImmutableArray();
    public bool IsValid => IsStructurallyValid && !Issues.Any(static issue => issue.Severity == ModelValidationSeverity.Error);
}
