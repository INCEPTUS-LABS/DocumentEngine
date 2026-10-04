using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Fact]
    public async Task A1214WholeBodyContainmentRejectsCentreOnlyPlacementHeadersGapsAndNegativeCanonicalOrigin()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var original = fixture.Document;
        var history = fixture.State.HistoryStatus;
        var region = Destination(fixture, Fixture.PoolB);
        var outside = new PointD(region.Bounds.Right - 30d, region.MapLocalToScene(new PointD(0d, 60d)).Y);
        Assert.True(region.Bounds.Contains(outside));
        await ShowPlacementAsync(fixture, outside);
        Assert.False(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);
        Assert.False(region.Bounds.Contains(PlacementFeedback(fixture).Bounds!.Value));
        var rejected = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(outside));
        Assert.False(rejected.IsCommitted);
        Assert.Same(original, fixture.Document);
        Assert.Equal(history, fixture.State.HistoryStatus);

        var touching = new PointD(region.Bounds.Right - 60d, outside.Y);
        await ShowPlacementAsync(fixture, touching);
        Assert.True(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);
        Assert.Equal(region.Bounds.Right, PlacementFeedback(fixture).Bounds!.Value.Right);
        foreach (var invalid in new[]
                 {
                     new PointD(region.Bounds.Left - 10d, region.Bounds.Top + 60d),
                     new PointD(region.Bounds.Left + 200d, region.Bounds.Bottom + 20d),
                     region.MapLocalToScene(new PointD(59d, 39d)),
                 })
        {
            await ShowPlacementAsync(fixture, invalid);
            var feedback = PlacementFeedback(fixture);
            Assert.False(feedback.PlacementPreview!.IsAllowed);
            Assert.Equal(invalid, Center(feedback.Bounds!.Value));
        }
        Assert.Same(original, fixture.Document);
        Assert.Equal(history, fixture.State.HistoryStatus);
    }

    [Fact]
    public async Task A1214PoolCrossingUsesEachActualOriginAndKeepsBodyLabelAndMarkerInOneFeedbackFamily()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:manual-task"));
        var original = fixture.Document;
        var origins = new List<PointD>();
        foreach (var containerId in new SemanticElementId?[] { Fixture.PoolB, null, Fixture.PoolA, Fixture.PoolB })
        {
            var region = Destination(fixture, containerId);
            var point = region.MapLocalToScene(new PointD(340d, 60d));
            origins.Add(region.MapLocalToScene(default(PointD)));
            await ShowPlacementAsync(fixture, point);
            var feedback = PlacementFeedback(fixture);
            Assert.True(feedback.PlacementPreview!.IsAllowed);
            Assert.Equal(region.MapLocalToScene(feedback.PlacementPreview.Bounds), feedback.Bounds);
            Assert.Equal(point, Center(feedback.Bounds!.Value));
            Assert.Contains("Manual Task", feedback.PlacementPreview.Label, StringComparison.Ordinal);
            var family = fixture.State.CurrentScene!.Items.Where(item =>
                item.Origin.StableSourceKey?.StartsWith($"feedback:{feedback.Id}:", StringComparison.Ordinal) == true).ToArray();
            Assert.NotEmpty(family);
            Assert.All(family, item => Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode));
        }
        Assert.True(origins.Distinct().Count() == 3);
        Assert.Same(original, fixture.Document);

        var secondPoolNode = fixture.Node(Fixture.RegionNodes[2]);
        await ShowPlacementAsync(fixture, Center(secondPoolNode.Bounds));
        Assert.False(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);
        Assert.Contains(PlacementFeedback(fixture).PlacementPreview!.Diagnostics,
            diagnostic => diagnostic.Code == "TOOLBOX_PLACEMENT_BLOCKED");
    }

    [Fact]
    public async Task A1214CollapsedRegionsRejectAndHiddenDecorationRetainsTheSamePlacementAuthority()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        Assert.True((await fixture.Session.UpdateModelProfileViewStateAsync(
            fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false))).Succeeded);
        var region = Destination(fixture, Fixture.PoolB);
        await ShowPlacementAsync(fixture, region.MapLocalToScene(new PointD(340d, 60d)));
        Assert.True(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);
        await ShowPlacementAsync(fixture, new PointD(region.Bounds.Left - 10d, region.Bounds.Top + 40d));
        Assert.False(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);

        Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
            fixture.State.ModelProfileElementViewState.WithCollapsed(OrganizationalModelProfile.Id, Fixture.PoolB, true))).Succeeded);
        region = Destination(fixture, Fixture.PoolB);
        await ShowPlacementAsync(fixture, Center(region.Bounds));
        Assert.False(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);
        var before = fixture.Document;
        var result = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(Center(region.Bounds)));
        Assert.False(result.IsCommitted);
        Assert.Same(before, fixture.Document);
        Assert.True(fixture.Placement.IsPlacementActive);
        var unassigned = Destination(fixture, null);
        await ShowPlacementAsync(fixture, unassigned.MapLocalToScene(new PointD(340d, 60d)));
        Assert.True(PlacementFeedback(fixture).PlacementPreview!.IsAllowed);
    }

    private static async Task ShowPlacementAsync(Fixture fixture, PointD scenePoint)
    {
        await fixture.Placement.UpdatePreviewAtCssPointAsync(fixture.Session, fixture.Css(scenePoint));
        fixture.AssertReady();
    }

    private static EditorFeedbackSnapshot PlacementFeedback(Fixture fixture) =>
        Assert.Single(fixture.State.EditorState.TemporaryFeedback, feedback => feedback.PlacementPreview is not null);
}
