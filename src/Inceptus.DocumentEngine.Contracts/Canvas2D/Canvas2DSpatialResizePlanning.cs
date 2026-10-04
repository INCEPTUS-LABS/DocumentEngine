using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public sealed record Canvas2DSpatialResizeRequest
{
    public Canvas2DSpatialResizeRequest(DocumentSnapshot document, DocumentScopeId activeScopeId,
        Canvas2DSpatialResizeTarget target, double requestedExtent)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(target);
        Document = document;
        ActiveScopeId = activeScopeId;
        Target = target;
        RequestedExtent = requestedExtent;
    }
    public DocumentSnapshot Document { get; }
    public DocumentScopeId ActiveScopeId { get; }
    public Canvas2DSpatialResizeTarget Target { get; }
    public double RequestedExtent { get; }
}

/// <summary>Optional pure dimension planning on the existing profile registration.</summary>
public interface ICanvas2DSpatialResizePlanner
{
    bool TryGetConstraints(DocumentSnapshot document, DocumentScopeId scopeId, Canvas2DSpatialResizeEdge edge,
        Canvas2DSpatialRegionId? authorityRegionId, out Canvas2DSpatialDimensionConstraints? constraints,
        out ImmutableArray<Diagnostic> diagnostics);
    Canvas2DSpatialEditPlanResult PlanResize(Canvas2DSpatialResizeRequest request);
}

public sealed partial class Canvas2DSpatialEditPlannerCatalog
{
    public bool TryGetConstraints(ModelProfileId profileId, DocumentSnapshot document, DocumentScopeId scopeId,
        Canvas2DSpatialResizeEdge edge, Canvas2DSpatialRegionId? authorityRegionId,
        out Canvas2DSpatialDimensionConstraints? constraints, out ImmutableArray<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        if (_planners.TryGetValue(profileId, out var planner) && planner is ICanvas2DSpatialResizePlanner resize)
            return resize.TryGetConstraints(document, scopeId, edge, authorityRegionId, out constraints, out diagnostics);
        constraints = null;
        diagnostics = [MissingResizePlanner(profileId)];
        return false;
    }

    public Canvas2DSpatialEditPlanResult PlanResize(Canvas2DSpatialResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _planners.TryGetValue(request.Target.ProfileId, out var planner) && planner is ICanvas2DSpatialResizePlanner resize
            ? resize.PlanResize(request)
            : Canvas2DSpatialEditPlanResult.Failure([MissingResizePlanner(request.Target.ProfileId)]);
    }

    private static Diagnostic MissingResizePlanner(ModelProfileId profileId) => new(
        "INCEPTUS.SPATIAL.RESIZE.PLANNER.MISSING", DiagnosticSeverity.Error,
        "The spatial profile has no dimension edit policy.", profileId.Value);
}
