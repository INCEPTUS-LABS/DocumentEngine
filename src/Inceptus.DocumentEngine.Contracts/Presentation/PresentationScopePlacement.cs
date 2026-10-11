using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Presentation;

/// <summary>A reversible view translation of existing process geometry.</summary>
public sealed record PresentationScopePlacement
{
    public PresentationScopePlacement(DocumentScopeId scopeId, VectorD translation,
        VisualStateId? boundaryVisualStateId = null)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ScopeId = scopeId;
        Translation = translation;
        BoundaryVisualStateId = boundaryVisualStateId;
    }

    public DocumentScopeId ScopeId { get; }

    public VectorD Translation { get; }

    public VisualStateId? BoundaryVisualStateId { get; }

    public PointD MapLocalToView(PointD point) => point + Translation;

    public PointD MapViewToLocal(PointD point) => point - Translation;

    public RectD MapLocalToView(RectD bounds) => bounds.Translate(Translation);

    public RectD MapViewToLocal(RectD bounds) => bounds.Translate(new VectorD(-Translation.X, -Translation.Y));
}
