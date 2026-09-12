using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed partial class Canvas2DInteractionController
{
    private bool TryPlanSpatialMove(
        PersistentGestureState gesture,
        EditingSessionState state,
        PointD finalPoint,
        out ICommand? command,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        command = null;
        diagnostics = [];
        if (!_session.TryCaptureDocumentSnapshot(out var document) || document is null ||
            document.Revision != gesture.DocumentRevision ||
            state.CurrentScene?.SpatialPresentationPlan is not { } plan)
        {
            return Reject("The spatial edit no longer has a current Document and presentation plan.", out diagnostics);
        }

        var destinations = new List<(MoveGestureTarget Target, Canvas2DSpatialRegion Region, VisualStateMove Move)>();
        var delta = finalPoint - gesture.StartDocumentPoint;
        foreach (var target in gesture.MoveTargets.Where(static target => !target.IsPreviewOnly))
        {
            var bounds = target.OriginalBounds.Translate(delta);
            if (state.CurrentScene.Items.Any(item => item.IsVisible &&
                    Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item) &&
                    item.Bounds.Contains(Center(bounds))))
            {
                return Reject("The destination is a collapsed or otherwise unavailable spatial region.", out diagnostics);
            }
            var regions = plan.Regions.Where(region => region.Bounds.Contains(Center(bounds))).ToArray();
            if (regions.Length != 1)
            {
                return Reject("Move the node into one recognized spatial destination region.", out diagnostics);
            }
            var region = regions[0];
            var canonical = region.MapSceneToLocal(bounds);
            if (!DocumentGeometryBoundary.Contains(canonical) ||
                !DocumentGeometryBoundary.Contains(region.MapSceneToLocal(target.BoundaryBounds.Translate(delta))))
            {
                return Reject("The destination would place canonical Process geometry outside its valid boundary.", out diagnostics);
            }
            destinations.Add((target, region,
                new VisualStateMove(target.VisualStateId, canonical.TopLeft, VisualPlacementMode.Pinned)));
        }

        var changedMoves = destinations.Select(static target => target.Move).Where(move =>
            !document.VisualModel.TryGetVisualState(move.VisualStateId, out var visual) || visual is null ||
            visual.Position != move.TargetPosition || visual.PlacementMode != move.RequestedPlacementMode).ToArray();
        var planningMoves = changedMoves.Length > 0 ? changedMoves : destinations.Select(static target => target.Move).ToArray();
        if (planningMoves.Length == 0)
        {
            return true;
        }
        ICommand baseCommand = planningMoves.Length == 1
            ? new MoveVisualStateCommand(document.DocumentId, document.Revision,
                planningMoves[0].VisualStateId, planningMoves[0].TargetPosition, planningMoves[0].RequestedPlacementMode)
            : new MoveVisualStatesCommand(document.DocumentId, document.Revision, planningMoves);
        var commands = new List<ICommand>();
        if (changedMoves.Length > 0)
        {
            commands.Add(baseCommand);
        }
        foreach (var destination in destinations)
        {
            if (!document.VisualModel.TryGetVisualState(destination.Target.VisualStateId, out var visual) || visual is null)
            {
                return Reject("The moved visual no longer exists.", out diagnostics);
            }
            var planned = _spatialEditPlanners.Plan(new Canvas2DSpatialEditRequest(
                document, state.ActiveScopeId, Canvas2DSpatialEditKind.Move, baseCommand,
                visual.SemanticElementId, destination.Region));
            if (!planned.Succeeded)
            {
                diagnostics = planned.Diagnostics;
                return false;
            }
            if (ReferenceEquals(planned.Command, baseCommand))
            {
                continue;
            }
            if (planned.Command is not CompoundDocumentCommand compound ||
                !ReferenceEquals(compound.Commands[0], baseCommand))
            {
                return Reject("A spatial policy must preserve the existing movement operation in its compound plan.", out diagnostics);
            }
            commands.AddRange(compound.Commands.Skip(1));
        }
        if (commands.Count > 32)
        {
            return Reject("This spatial edit exceeds the bounded atomic operation size; move fewer nodes at once.", out diagnostics);
        }
        command = commands.Count switch
        {
            0 => null,
            1 => commands[0],
            _ => new CompoundDocumentCommand(document.DocumentId, document.Revision, commands),
        };
        return true;
    }

    private static bool Reject(string message, out ImmutableArray<Diagnostic> diagnostics)
    {
        diagnostics = [new Diagnostic("INCEPTUS.SPATIAL.MOVE.REJECTED", DiagnosticSeverity.Error, message)];
        return false;
    }
}
