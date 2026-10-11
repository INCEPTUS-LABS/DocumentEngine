using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class ToolboxPlacementController
{
    private static bool TryResolveCurrentPlacementTarget(
        EditingSessionState state, DocumentSnapshot document, PointD scenePoint,
        out PointD localPoint, out DocumentScopeId targetScope,
        out Canvas2DSpatialRegion? spatialRegion, out PresentationScopePlacement? placement,
        out RectD? boundary, out bool allowed)
    {
        placement = null;
        boundary = null;
        spatialRegion = null;
        if (state.PresentationContext is ProcessScopePresentationContext process)
        {
            allowed = TryResolvePlacementTarget(state.CurrentScene!, process.ScopeId, scenePoint,
                out localPoint, out targetScope, out spatialRegion);
            return true;
        }

        localPoint = default;
        targetScope = null!;
        allowed = false;
        if (state.PresentationPlan is not { } plan || plan.Context != state.PresentationContext ||
            plan.DocumentId != document.DocumentId || plan.SourceRevision != document.Revision)
            return false;

        var candidates = plan.ScopePlacements.Select(candidate => (Placement: candidate,
                Visual: document.VisualModel.VisualStates.SingleOrDefault(visual =>
                    visual.Id == candidate.BoundaryVisualStateId)))
            .Where(candidate => candidate.Visual is { } visual &&
                new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height)
                    .Contains(scenePoint)).Take(2).ToArray();
        if (candidates.Length != 1)
            return false;

        placement = candidates[0].Placement;
        var boundaryVisual = candidates[0].Visual!;
        boundary = new RectD(boundaryVisual.Position.X, boundaryVisual.Position.Y,
            boundaryVisual.Size.Width, boundaryVisual.Size.Height);
        targetScope = placement.ScopeId;
        localPoint = placement.MapViewToLocal(scenePoint);
        allowed = true;
        return true;
    }
}
