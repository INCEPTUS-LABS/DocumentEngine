using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Validation;

/// <summary>
/// Transient validation findings for one exact authoritative Document revision.
/// </summary>
public sealed class ValidationSnapshot
{
    public ValidationSnapshot(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        IEnumerable<ModelValidationIssue>? issues = null,
        long? sourceGeneration = null)
        : this(
            documentId,
            ResolveRootScopeId(documentId),
            sourceRevision,
            issues,
            sourceGeneration)
    {
    }

    public ValidationSnapshot(
        DocumentId documentId,
        DocumentScopeId scopeId,
        DocumentRevision sourceRevision,
        IEnumerable<ModelValidationIssue>? issues = null,
        long? sourceGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(scopeId);
        if (sourceGeneration < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceGeneration),
                sourceGeneration,
                "The optional runtime generation cannot be negative.");
        }

        DocumentId = documentId;
        ScopeId = scopeId;
        SourceRevision = sourceRevision;
        SourceGeneration = sourceGeneration;
        Issues = ModelValidationIssueCollection.CopyAndOrder(issues, nameof(issues));
    }

    public DocumentId DocumentId { get; }

    public DocumentScopeId ScopeId { get; }

    public DocumentRevision SourceRevision { get; }

    public long? SourceGeneration { get; }

    public ImmutableArray<ModelValidationIssue> Issues { get; }

    public int ErrorCount => Issues.Count(static issue =>
        issue.Severity == ModelValidationSeverity.Error);

    public int WarningCount => Issues.Count(static issue =>
        issue.Severity == ModelValidationSeverity.Warning);

    public int InfoCount => Issues.Count(static issue =>
        issue.Severity == ModelValidationSeverity.Info);

    private static DocumentScopeId ResolveRootScopeId(DocumentId documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        return new DocumentScopeId(documentId.Value);
    }
}

internal static class ModelValidationIssueCollection
{
    internal static ImmutableArray<ModelValidationIssue> CopyAndOrder(
        IEnumerable<ModelValidationIssue>? issues,
        string parameterName)
    {
        var copy = issues?.ToArray() ?? [];
        if (Array.Exists(copy, static issue => issue is null))
        {
            throw new ArgumentException(
                "Validation issue collections cannot contain null findings.",
                parameterName);
        }

        Array.Sort(copy, Compare);
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].Id == copy[index].Id)
            {
                throw new ArgumentException(
                    $"Duplicate model-validation issue identity '{copy[index].Id}'.",
                    parameterName);
            }
        }

        return [.. copy];
    }

    private static int Compare(ModelValidationIssue left, ModelValidationIssue right)
    {
        var comparison = left.Severity.CompareTo(right.Severity);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(
            left.Target.SemanticElementId?.Value,
            right.Target.SemanticElementId?.Value);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(
            left.Target.VisualStateId?.Value,
            right.Target.VisualStateId?.Value);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(left.Discriminator, right.Discriminator);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.Message, right.Message);
    }
}
