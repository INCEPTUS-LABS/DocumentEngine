using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Immutable, normalized input for one applicable Projection rule.
/// </summary>
public abstract class ProjectionRuleInput
{
    protected ProjectionRuleInput(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        ProjectionContext context,
        IEnumerable<VisualStateSnapshot>? visualStates)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(context);

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        Context = context;
        VisualStates = CopyAndOrderVisualStates(visualStates);
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public ProjectionContext Context { get; }

    public ImmutableArray<VisualStateSnapshot> VisualStates { get; }

    public abstract ProjectionSourceKind SourceKind { get; }

    public abstract SemanticElementId SemanticId { get; }

    public abstract SemanticTypeId SemanticTypeId { get; }

    protected void ValidateVisualAssociations(string parameterName)
    {
        if (VisualStates.Any(visualState => visualState.SemanticElementId != SemanticId))
        {
            throw new ArgumentException(
                "Every visual state supplied to a Projection rule must reference its semantic source.",
                parameterName);
        }
    }

    private static ImmutableArray<VisualStateSnapshot> CopyAndOrderVisualStates(
        IEnumerable<VisualStateSnapshot>? visualStates)
    {
        if (visualStates is null)
        {
            return [];
        }

        var copy = visualStates.ToArray();
        if (Array.Exists(copy, static visualState => visualState is null))
        {
            throw new ArgumentException(
                "Projection rule visual states cannot contain null values.",
                nameof(visualStates));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].Id == copy[index].Id)
            {
                throw new ArgumentException(
                    $"Duplicate Projection visual state ID '{copy[index].Id}'.",
                    nameof(visualStates));
            }
        }

        return [.. copy];
    }
}

public sealed class ElementProjectionRuleInput : ProjectionRuleInput
{
    public ElementProjectionRuleInput(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        ProjectionContext context,
        SemanticElementSnapshot element,
        IEnumerable<VisualStateSnapshot>? visualStates = null)
        : base(documentId, sourceRevision, context, visualStates)
    {
        ArgumentNullException.ThrowIfNull(element);
        Element = element;
        ValidateVisualAssociations(nameof(visualStates));
    }

    public SemanticElementSnapshot Element { get; }

    public override ProjectionSourceKind SourceKind => ProjectionSourceKind.SemanticElement;

    public override SemanticElementId SemanticId => Element.Id;

    public override SemanticTypeId SemanticTypeId => Element.TypeId;
}

public sealed class RelationshipProjectionRuleInput : ProjectionRuleInput
{
    public RelationshipProjectionRuleInput(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        ProjectionContext context,
        SemanticRelationshipSnapshot relationship,
        SemanticElementSnapshot sourceElement,
        SemanticElementSnapshot targetElement,
        IEnumerable<VisualStateSnapshot>? visualStates = null)
        : base(documentId, sourceRevision, context, visualStates)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(sourceElement);
        ArgumentNullException.ThrowIfNull(targetElement);
        if (relationship.SourceId != sourceElement.Id ||
            relationship.TargetId != targetElement.Id)
        {
            throw new ArgumentException(
                "Resolved Projection endpoints must match the semantic relationship.",
                nameof(relationship));
        }

        Relationship = relationship;
        SourceElement = sourceElement;
        TargetElement = targetElement;
        ValidateVisualAssociations(nameof(visualStates));
    }

    public SemanticRelationshipSnapshot Relationship { get; }

    public SemanticElementSnapshot SourceElement { get; }

    public SemanticElementSnapshot TargetElement { get; }

    public override ProjectionSourceKind SourceKind => ProjectionSourceKind.SemanticRelationship;

    public override SemanticElementId SemanticId => Relationship.Id;

    public override SemanticTypeId SemanticTypeId => Relationship.TypeId;
}
