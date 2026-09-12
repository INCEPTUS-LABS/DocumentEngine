using System.Globalization;
using System.Text;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Creates stable projected identities from their complete deterministic identity tuple.
/// </summary>
public static class ProjectedObjectIdentity
{
    public static ProjectedObjectId Create(
        ProjectionSourceTrace source,
        ProjectedObjectKind objectKind)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Create(
            source.DocumentId,
            source.RuleId,
            source.SourceKind,
            source.SemanticElementId,
            objectKind,
            source.LocalKey);
    }

    public static ProjectedObjectId Create(
        DocumentId documentId,
        ProjectionRuleId ruleId,
        ProjectionSourceKind sourceKind,
        SemanticElementId semanticElementId,
        ProjectedObjectKind objectKind,
        string localKey)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localKey);

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "The projection source kind must be defined.");
        }

        if (!Enum.IsDefined(objectKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(objectKind),
                objectKind,
                "The projected object kind must be defined.");
        }

        var builder = new StringBuilder("projected:");
        AppendSegment(builder, documentId.Value);
        AppendSegment(builder, ruleId.Value);
        AppendSegment(builder, ((int)sourceKind).ToString(CultureInfo.InvariantCulture));
        AppendSegment(builder, semanticElementId.Value);
        AppendSegment(builder, ((int)objectKind).ToString(CultureInfo.InvariantCulture));
        AppendSegment(builder, localKey);

        return new ProjectedObjectId(builder.ToString());
    }

    private static void AppendSegment(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}
