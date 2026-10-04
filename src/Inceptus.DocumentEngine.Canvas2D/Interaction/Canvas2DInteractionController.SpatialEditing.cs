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
        out ImmutableArray<Diagnostic> diagnostics,
        out Canvas2DSpatialMoveEvaluation? evaluation)
    {
        command = null;
        diagnostics = [];
        evaluation = null;
        if (!_session.TryCaptureDocumentSnapshot(out var document) || document is null ||
            document.Revision != gesture.DocumentRevision ||
            state.CurrentScene?.SpatialPresentationPlan is not { } plan)
        {
            return Reject("The spatial edit no longer has a current Document and presentation plan.", out diagnostics);
        }

        evaluation = Canvas2DSpatialMoveEvaluator.Evaluate(state.CurrentScene!, document,
            SpatialMoveBodies(gesture.MoveTargets), finalPoint - gesture.StartDocumentPoint);
        if (!evaluation.Succeeded) return Reject(evaluation.Rejection!, out diagnostics);
        var destinations = new List<(Canvas2DSpatialMoveBody Target, Canvas2DSpatialRegion Region, VisualStateMove Move)>();
        foreach (var destination in evaluation.Destinations.Where(static destination => !destination.Body.IsPreviewOnly))
        {
            var canonical = destination.Region.MapSceneToLocal(destination.Bounds);
            destinations.Add((destination.Body, destination.Region,
                new VisualStateMove(destination.Body.VisualStateId, canonical.TopLeft, VisualPlacementMode.Pinned)));
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

    private static ImmutableArray<Canvas2DSpatialMoveBody> SpatialMoveBodies(ImmutableArray<MoveGestureTarget> targets) =>
        targets.Select(static target => new Canvas2DSpatialMoveBody(target.SceneObjectId, target.VisualStateId,
            target.OriginalBounds, target.IsPreviewOnly)).ToImmutableArray();

    private string SpatialMoveCursor(EditingSessionState state, ImmutableArray<MoveGestureTarget> targets, VectorD delta)
    {
        if (state.CurrentScene?.SpatialPresentationPlan is null) return "grabbing";
        return _session.TryCaptureDocumentSnapshot(out var document) && document is not null &&
            document.Revision == state.DocumentRevision && Canvas2DSpatialMoveEvaluator.Evaluate(
                state.CurrentScene, document, SpatialMoveBodies(targets), delta).Succeeded ? "grabbing" : "not-allowed";
    }

    private static bool Reject(string message, out ImmutableArray<Diagnostic> diagnostics)
    {
        diagnostics = [new Diagnostic("INCEPTUS.SPATIAL.MOVE.REJECTED", DiagnosticSeverity.Error, message)];
        return false;
    }
}
