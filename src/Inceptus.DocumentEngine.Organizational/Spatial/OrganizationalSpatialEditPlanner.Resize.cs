using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Scene;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

public sealed partial class OrganizationalSpatialEditPlanner
{
    public bool TryGetConstraints(DocumentSnapshot document, DocumentScopeId scopeId, Canvas2DSpatialResizeEdge edge,
        Canvas2DSpatialRegionId? authorityRegionId, out Canvas2DSpatialDimensionConstraints? constraints,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scopeId);
        constraints = null;
        var geometry = document.VisualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == scopeId)?.Geometry;
        if (geometry is not null && document.SemanticModel.ModelProfiles.IsAvailable(OrganizationalModelProfile.Id))
            constraints = OrganizationalPoolSceneContributor.SavedDimensionConstraints(geometry,
                document.SemanticModel.Elements, edge, authorityRegionId);
        diagnostics = constraints is null
            ? [new Diagnostic("ORGANIZATIONAL_RESIZE_TARGET_UNAVAILABLE", DiagnosticSeverity.Error,
                "The requested dimension has no active saved Organizational region.", scopeId.Value)] : [];
        return constraints is not null;
    }

    public Canvas2DSpatialEditPlanResult PlanResize(Canvas2DSpatialResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target.ProfileId != OrganizationalModelProfile.Id ||
            !TryGetConstraints(request.Document, request.ActiveScopeId, request.Target.Edge, request.Target.AuthorityRegionId,
                out var constraints, out var diagnostics))
            return Failure("The acquired spatial dimension is no longer available.", request.ActiveScopeId.Value);
        diagnostics = constraints!.Validate(request.RequestedExtent);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return Canvas2DSpatialEditPlanResult.Failure(diagnostics);
        return Canvas2DSpatialEditPlanResult.Success(request.Target.Edge == Canvas2DSpatialResizeEdge.Bottom
            ? new SetOrganizationalRegionExpandedHeightCommand(request.Document.DocumentId, request.Document.Revision,
                request.ActiveScopeId, request.Target.AuthorityRegionId!, request.RequestedExtent)
            : new SetOrganizationalScopeWidthCommand(request.Document.DocumentId, request.Document.Revision,
                request.ActiveScopeId, request.RequestedExtent));
    }
}
