using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Identifies one visible projected node that a notation-owned placement strategy may target.
/// </summary>
public sealed record ToolboxPlacementTarget
{
    public ToolboxPlacementTarget(
        SemanticElementId semanticElementId,
        SemanticTypeId semanticTypeId,
        VisualStateId visualStateId,
        ProjectedObjectId projectedObjectId,
        RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(projectedObjectId);
        if (bounds.IsEmpty)
        {
            throw new ArgumentException(
                "A Toolbox placement target requires non-empty effective bounds.",
                nameof(bounds));
        }

        SemanticElementId = semanticElementId;
        SemanticTypeId = semanticTypeId;
        VisualStateId = visualStateId;
        ProjectedObjectId = projectedObjectId;
        Bounds = bounds;
    }

    public SemanticElementId SemanticElementId { get; }

    public SemanticTypeId SemanticTypeId { get; }

    public VisualStateId VisualStateId { get; }

    public ProjectedObjectId ProjectedObjectId { get; }

    public RectD Bounds { get; }
}
