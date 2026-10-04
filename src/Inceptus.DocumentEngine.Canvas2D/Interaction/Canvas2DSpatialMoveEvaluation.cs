using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal sealed record Canvas2DSpatialMoveBody(
    SceneObjectId SceneObjectId, VisualStateId VisualStateId, RectD OriginalBounds, bool IsPreviewOnly);

internal sealed record Canvas2DSpatialMoveDestination(
    Canvas2DSpatialMoveBody Body, Canvas2DSpatialRegion Region, RectD Bounds);

/// <summary>One immutable evaluated move, rechecked against the same current Scene at session acceptance.</summary>
internal sealed record Canvas2DSpatialMoveEvaluation(
    Canvas2DSpatialPresentationPlan Plan,
    ImmutableArray<Canvas2DSpatialMoveBody> Bodies,
    VectorD Translation,
    ImmutableArray<Canvas2DSpatialMoveDestination> Destinations,
    string? Rejection)
{
    internal bool Succeeded => Rejection is null;

    internal bool MatchesCommand(DocumentSnapshot document, ICommand command)
    {
        var expected = Destinations.Where(static destination => !destination.Body.IsPreviewOnly)
            .Select(static destination => new VisualStateMove(destination.Body.VisualStateId,
                destination.Region.MapSceneToLocal(destination.Bounds).TopLeft, VisualPlacementMode.Pinned))
            .ToDictionary(static move => move.VisualStateId);
        var commands = command is CompoundDocumentCommand compound ? compound.Commands : [command];
        var actual = commands.SelectMany(static child => child switch
        {
            MoveVisualStateCommand move => (IEnumerable<VisualStateMove>)[new(move.TargetVisualStateId,
                move.TargetPosition, move.RequestedPlacementMode)],
            MoveVisualStatesCommand moves => moves.Moves,
            _ => [],
        }).ToArray();
        if (actual.Select(static move => move.VisualStateId).Distinct().Count() != actual.Length ||
            actual.Any(move => !expected.TryGetValue(move.VisualStateId, out var target) ||
                move.TargetPosition != target.TargetPosition || move.RequestedPlacementMode != target.RequestedPlacementMode))
            return false;
        var actualIds = actual.Select(static move => move.VisualStateId).ToHashSet();
        return expected.Values.All(move => actualIds.Contains(move.VisualStateId) ||
            (document.VisualModel.TryGetVisualState(move.VisualStateId, out var current) && current is not null &&
                current.Position == move.TargetPosition && current.PlacementMode == move.RequestedPlacementMode));
    }
}

internal static class Canvas2DSpatialMoveEvaluator
{
    internal static VectorD ClampTranslation(Canvas2DSpatialPresentationPlan plan,
        IEnumerable<RectD> bodies, VectorD requested)
    {
        if (plan.MovementBottomBoundaryRegionId is not { } boundaryId) return requested;
        var boundary = plan.Regions.Single(region => region.Id == boundaryId);
        var array = bodies.ToArray();
        return array.Length == 0 ? requested : new VectorD(requested.X,
            Math.Min(requested.Y, boundary.Bounds.Bottom - array.Max(static body => body.Bottom)));
    }

    internal static Canvas2DSpatialMoveEvaluation Evaluate(Canvas2DScene scene, DocumentSnapshot document,
        ImmutableArray<Canvas2DSpatialMoveBody> bodies, VectorD requested)
    {
        var plan = scene.SpatialPresentationPlan!;
        var translation = ClampTranslation(plan, bodies.Select(static body => body.OriginalBounds), requested);
        var destinations = ImmutableArray.CreateBuilder<Canvas2DSpatialMoveDestination>();
        Canvas2DSpatialMoveEvaluation Reject(string reason) => new(plan, bodies, translation, [], reason);
        foreach (var body in bodies)
        {
            if (!scene.Items.Any(item => item.Id == body.SceneObjectId && item.IsVisible &&
                    item.Origin.VisualStateId == body.VisualStateId && item.Bounds == body.OriginalBounds &&
                    (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0) ||
                !document.VisualModel.TryGetVisualState(body.VisualStateId, out _))
                return Reject("A moving node body no longer matches the current Scene and Document.");
        }
        foreach (var body in bodies.Where(static body => !body.IsPreviewOnly))
        {
            var bounds = body.OriginalBounds.Translate(translation);
            var center = new PointD(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            var regions = plan.Regions.Where(region => region.Bounds.Contains(center)).ToArray();
            if (regions.Length != 1) return Reject("Move every node into one recognized spatial destination.");
            destinations.Add(new(body, regions[0], bounds));
        }
        foreach (var body in bodies.Where(static body => body.IsPreviewOnly))
        {
            document.VisualModel.TryGetVisualState(body.VisualStateId, out var visual);
            var hostId = visual is not null && document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element)
                ? element?.AttachedToElementId : null;
            var hosts = destinations.Where(destination =>
                document.VisualModel.TryGetVisualState(destination.Body.VisualStateId, out var candidate) &&
                candidate?.SemanticElementId == hostId).ToArray();
            if (hosts.Length != 1) return Reject("An attached follower requires one unambiguous moving host.");
            destinations.Add(new(body, hosts[0].Region, body.OriginalBounds.Translate(translation)));
        }
        foreach (var destination in destinations)
        {
            if (!destination.Region.Bounds.Contains(destination.Bounds) ||
                !DocumentGeometryBoundary.Contains(destination.Region.MapSceneToLocal(destination.Bounds)) ||
                scene.Items.Any(item => item.IsVisible && Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item) &&
                    item.Bounds.Intersects(destination.Bounds)))
                return Reject("Every complete moving body must fit its current destination outside collapsed rows.");
        }
        return new(plan, bodies, translation, destinations.ToImmutable(), null);
    }
}
