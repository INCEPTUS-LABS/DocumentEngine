using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSpatialMoveEvaluationTests
{
    private static readonly DocumentId DocumentId = new("test:move-evaluation");
    private static readonly Canvas2DSpatialRegionId PoolId = new("test:pool");
    private static readonly Canvas2DSpatialRegionId UnassignedId = new("test:unassigned");
    private static readonly ModelProfileId ProfileId = new("test:profile");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RigidGroupUsesDisplayedUnassignedBottomAndAcceptsEquality(bool compact)
    {
        var plan = Plan(compact);
        var offset = compact ? -300 : 0;
        var fixture = Fixture(plan, [new(30, 520 + offset, 120, 80), new(280, 600 + offset, 120, 80)]);
        using var scene = fixture.Scene;

        var result = Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, fixture.Bodies, new VectorD(0, 200));

        Assert.True(result.Succeeded, result.Rejection);
        Assert.Equal(new VectorD(0, 120), result.Translation);
        Assert.Equal(plan.Regions.Single(region => region.Id == UnassignedId).Bounds.Bottom,
            result.Destinations.Max(destination => destination.Bounds.Bottom));
        Assert.Equal(80, result.Destinations[1].Bounds.Top - result.Destinations[0].Bounds.Top);
        var moves = result.Destinations.Select(destination => new VisualStateMove(destination.Body.VisualStateId,
            destination.Region.MapSceneToLocal(destination.Bounds).TopLeft, VisualPlacementMode.Pinned));
        Assert.True(result.MatchesCommand(fixture.Document,
            new MoveVisualStatesCommand(DocumentId, DocumentRevision.Zero, moves)));
    }

    [Fact]
    public void SourcePoolBottomDoesNotLimitCrossRegionMovement()
    {
        var fixture = Fixture(Plan(false), [new(30, 100, 120, 80)]);
        using var scene = fixture.Scene;
        var result = Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, fixture.Bodies, new VectorD(0, 450));
        Assert.True(result.Succeeded, result.Rejection);
        var destination = Assert.Single(result.Destinations);
        Assert.Equal(UnassignedId, destination.Region.Id);
        Assert.Equal(550, destination.Bounds.Top);
        Assert.Equal(new PointD(30, 50), destination.Region.MapSceneToLocal(destination.Bounds).TopLeft);
    }

    [Fact]
    public void OneBodyCrossingItsDestinationRejectsTheWholeGroup()
    {
        var fixture = Fixture(Plan(false), [new(30, 520, 120, 80), new(430, 600, 120, 80)]);
        using var scene = fixture.Scene;
        var result = Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, fixture.Bodies, new VectorD(0, 200));
        Assert.False(result.Succeeded);
        Assert.Empty(result.Destinations);
        Assert.Equal(120, result.Translation.Y);
    }

    [Fact]
    public void AttachedFollowerParticipatesInClampAndInheritsHostDestination()
    {
        var fixture = Fixture(Plan(false), [new(30, 100, 120, 80), new(80, 162, 36, 36)], follower: true);
        using var scene = fixture.Scene;
        var result = Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, fixture.Bodies, new VectorD(0, 1000));
        Assert.True(result.Succeeded, result.Rejection);
        Assert.Equal(602, result.Translation.Y);
        Assert.All(result.Destinations, destination => Assert.Equal(UnassignedId, destination.Region.Id));
        Assert.Equal(800, result.Destinations.Single(destination => destination.Body.IsPreviewOnly).Bounds.Bottom);
        Assert.Equal(782, result.Destinations.Single(destination => !destination.Body.IsPreviewOnly).Bounds.Bottom);
    }

    [Fact]
    public void UnrelatedCaptionAndConnectorOverflowDoNotAffectBodyClamp()
    {
        var fixture = Fixture(Plan(false), [new(30, 600, 120, 80)], includeOverflow: true);
        using var scene = fixture.Scene;
        var result = Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, fixture.Bodies, new VectorD(0, 200));
        Assert.True(result.Succeeded, result.Rejection);
        Assert.Equal(120, result.Translation.Y);
    }

    [Fact]
    public void OldBodiesAndUnevaluatedCommandCoordinatesCannotPassAcceptance()
    {
        var fixture = Fixture(Plan(false), [new(30, 600, 120, 80)]);
        using var scene = fixture.Scene;
        var result = Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, fixture.Bodies, new VectorD(0, 200));
        Assert.False(result.MatchesCommand(fixture.Document, new MoveVisualStateCommand(DocumentId,
            DocumentRevision.Zero, fixture.Bodies[0].VisualStateId, new PointD(30, 300), VisualPlacementMode.Pinned)));
        var stale = fixture.Bodies.SetItem(0, fixture.Bodies[0] with { OriginalBounds = new RectD(30, 599, 120, 80) });
        Assert.False(Canvas2DSpatialMoveEvaluator.Evaluate(scene, fixture.Document, stale, result.Translation).Succeeded);
    }

    [Fact]
    public void MissingBottomCapabilityPreservesExistingUnpartitionedClampBehavior()
    {
        var plan = Plan(false);
        var compatible = new Canvas2DSpatialPresentationPlan(plan.Regions, [], Matrix2D.Identity);
        Assert.Equal(new VectorD(0, 1000), Canvas2DSpatialMoveEvaluator.ClampTranslation(compatible,
            [new RectD(30, 600, 120, 80)], new VectorD(0, 1000)));
    }

    private static Canvas2DSpatialPresentationPlan Plan(bool compact)
    {
        var unassignedTop = compact ? 200 : 500;
        return new([
            new Canvas2DSpatialRegion(PoolId, ProfileId, new SemanticElementId("test:pool-element"), Matrix2D.Identity,
                new RectD(0, 0, 500, compact ? 100 : 400)),
            new Canvas2DSpatialRegion(UnassignedId, ProfileId, null, Matrix2D.CreateTranslation(0, unassignedTop),
                new RectD(0, unassignedTop, 500, 300))], [], Matrix2D.Identity,
            compact ? new Canvas2DSpatialCoordinateMap([
                new(PoolId, 0, 400, 0, 100), new(UnassignedId, 500, 800, 200, 500)]) : Canvas2DSpatialCoordinateMap.Identity,
            UnassignedId);
    }

    private static (Canvas2DScene Scene, DocumentSnapshot Document, ImmutableArray<Canvas2DSpatialMoveBody> Bodies)
        Fixture(Canvas2DSpatialPresentationPlan plan, RectD[] bounds, bool follower = false, bool includeOverflow = false)
    {
        var elements = bounds.Select((_, index) => new SemanticElementSnapshot(new SemanticElementId($"test:node:{index}"),
            new SemanticTypeId("test:node"), attachedToElementId: follower && index == 1 ? new SemanticElementId("test:node:0") : null)).ToArray();
        var visuals = bounds.Select((rect, index) => new VisualStateSnapshot(new VisualStateId($"test:visual:{index}"),
            elements[index].Id, rect.TopLeft, rect.Size, VisualPlacementMode.Pinned)).ToArray();
        var items = bounds.Select((rect, index) => new Canvas2DSceneItem(new SceneObjectId($"test:body:{index}"),
            Canvas2DSceneLayer.Content, 0, Canvas2DSceneGeometry.Rectangle(rect),
            new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.SemanticElement | Canvas2DSceneOriginCategory.VisualState,
                elements[index].Id, visuals[index].Id))).ToList();
        var bodies = items.Select((item, index) => new Canvas2DSpatialMoveBody(item.Id, visuals[index].Id,
            item.Bounds, follower && index == 1)).ToImmutableArray();
        if (includeOverflow)
        {
            items.Add(new Canvas2DSceneItem(new SceneObjectId("test:caption"), Canvas2DSceneLayer.Label, 0,
                Canvas2DSceneGeometry.Rectangle(new RectD(30, 1000, 120, 20)), items[0].Origin));
            items.Add(new Canvas2DSceneItem(new SceneObjectId("test:connector"), Canvas2DSceneLayer.Connector, 0,
                Canvas2DSceneGeometry.Path([new PointD(40, 2000), new PointD(300, 3000)]), items[0].Origin));
        }
        var scene = new Canvas2DScene(DocumentId, DocumentRevision.Zero, new AlgorithmId("test:layout"),
            new AlgorithmId("test:routing"), Canvas2DSceneConfiguration.Default, [], new ViewportSnapshot(1, new VectorD(0, 0)),
            Matrix2D.Identity, null, null, PropertyMap.Empty, PropertyMap.Empty, items, spatialPresentationPlan: plan);
        var document = new DocumentSnapshot(new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero, elements),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals), new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        return (scene, document, bodies);
    }
}
