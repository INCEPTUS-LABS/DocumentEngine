using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Provides immutable traceability from a projected object to its authoritative semantic source.
/// </summary>
public sealed class ProjectionSourceTrace : IEquatable<ProjectionSourceTrace>
{
    public ProjectionSourceTrace(
        DocumentId documentId,
        ProjectionRuleId ruleId,
        ProjectionSourceKind sourceKind,
        SemanticElementId semanticElementId,
        SemanticTypeId semanticTypeId,
        string localKey,
        VisualStateId? visualStateId = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localKey);

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "The projection source kind must be defined.");
        }

        DocumentId = documentId;
        RuleId = ruleId;
        SourceKind = sourceKind;
        SemanticElementId = semanticElementId;
        SemanticTypeId = semanticTypeId;
        VisualStateId = visualStateId;
        LocalKey = localKey;
    }

    public DocumentId DocumentId { get; }

    public ProjectionRuleId RuleId { get; }

    public ProjectionSourceKind SourceKind { get; }

    public SemanticElementId SemanticElementId { get; }

    /// <summary>
    /// Gets the stable semantic type copied from the authoritative source.
    /// It does not participate in projected identity.
    /// </summary>
    public SemanticTypeId SemanticTypeId { get; }

    /// <summary>
    /// Gets optional visual-source traceability. It does not participate in projected identity.
    /// </summary>
    public VisualStateId? VisualStateId { get; }

    public string LocalKey { get; }

    public bool Equals(ProjectionSourceTrace? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        RuleId == other.RuleId &&
        SourceKind == other.SourceKind &&
        SemanticElementId == other.SemanticElementId &&
        SemanticTypeId == other.SemanticTypeId &&
        VisualStateId == other.VisualStateId &&
        string.Equals(LocalKey, other.LocalKey, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ProjectionSourceTrace);

    public override int GetHashCode() => HashCode.Combine(
        DocumentId,
        RuleId,
        SourceKind,
        SemanticElementId,
        SemanticTypeId,
        VisualStateId,
        StringComparer.Ordinal.GetHashCode(LocalKey));
}
