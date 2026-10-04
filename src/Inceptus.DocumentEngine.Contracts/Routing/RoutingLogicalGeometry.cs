using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Explicit expanded scope coordinates; the layout is derived from the saved basis.</summary>
public sealed record RoutingLogicalGeometry
{
    public RoutingLogicalGeometry(DocumentScopeId scopeId, ScopeGeometrySnapshot geometry, LayoutResult layout)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(layout);
        ScopeId = scopeId;
        Geometry = geometry;
        Layout = layout;
    }

    public DocumentScopeId ScopeId { get; }
    public ScopeGeometrySnapshot Geometry { get; }
    public LayoutResult Layout { get; }
}
