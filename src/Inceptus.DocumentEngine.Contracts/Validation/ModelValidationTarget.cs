using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Validation;

/// <summary>
/// Identifies the authoritative model object addressed by a validation issue.
/// </summary>
public sealed record ModelValidationTarget
{
    private ModelValidationTarget(
        SemanticElementId? semanticElementId,
        VisualStateId? visualStateId)
    {
        if (visualStateId is not null && semanticElementId is null)
        {
            throw new ArgumentException(
                "A visual validation target must identify its semantic object.",
                nameof(visualStateId));
        }

        SemanticElementId = semanticElementId;
        VisualStateId = visualStateId;
    }

    public static ModelValidationTarget Document { get; } = new(null, null);

    public SemanticElementId? SemanticElementId { get; }

    public VisualStateId? VisualStateId { get; }

    public bool IsDocument => SemanticElementId is null;

    public static ModelValidationTarget ForSemanticElement(SemanticElementId semanticElementId)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        return new ModelValidationTarget(semanticElementId, null);
    }

    public static ModelValidationTarget ForVisualState(
        SemanticElementId semanticElementId,
        VisualStateId visualStateId)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        return new ModelValidationTarget(semanticElementId, visualStateId);
    }
}
