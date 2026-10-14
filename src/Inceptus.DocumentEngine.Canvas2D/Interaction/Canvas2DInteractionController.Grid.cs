using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed partial class Canvas2DInteractionController
{
    private GridSettings? ResolveMoveGrid(Canvas2DSceneItem[] targets)
    {
        if (_session.GridSettings is not { SnapToGrid: true } grid || _gridSnapEligibility is null ||
            !_session.TryCaptureDocumentSnapshot(out var document)) return null;
        return targets.All(target => target.Origin.SemanticElementId is { } id &&
            document.SemanticModel.TryGetElement(id, out var element) && _gridSnapEligibility(element!.TypeId))
            ? grid : null;
    }

    private static VectorD ConstrainMoveTranslation(Canvas2DScene scene, ImmutableArray<MoveGestureTarget> targets,
        RectD initiator, VectorD requested, GridSettings? grid)
    {
        VectorD Clamp(VectorD delta) => scene.SpatialPresentationPlan is { } plan
            ? Canvas2DSpatialMoveEvaluator.ClampTranslation(plan, targets.Select(static target => target.OriginalBounds), delta)
            : DocumentGeometryBoundary.ClampTranslation(targets.Select(static target =>
                target.SpatialRegion?.MapSceneToLocal(target.BoundaryBounds) ??
                target.ProcessPlacement?.MapViewToLocal(target.BoundaryBounds) ?? target.BoundaryBounds), delta);

        if (grid is null) return Clamp(requested);
        var center = new PointD(initiator.X + initiator.Width / 2, initiator.Y + initiator.Height / 2);
        var snapped = GridGeometry.Snap(center + requested, grid) - center;
        var legal = Clamp(snapped);
        // At an existing geometry boundary, choose the nearest feasible grid coordinate.
        // Every target, including attached dependents, receives this same translation.
        double Constrain(double origin, double value, double constrained)
        {
            if (value == constrained) return value;
            var quotient = (origin + constrained) / grid.PointDistance;
            if (!double.IsFinite(quotient)) return constrained;
            var result = grid.PointDistance * (constrained > value ? Math.Ceiling(quotient) : Math.Floor(quotient)) - origin;
            return double.IsFinite(result) ? result : constrained;
        }
        return new(Constrain(center.X, snapped.X, legal.X), Constrain(center.Y, snapped.Y, legal.Y));
    }
}
