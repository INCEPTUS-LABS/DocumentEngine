using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Harness = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.PlacementHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA1214ToolboxPlacementIntegrationTests
{
    [Fact]
    public async Task PreviewAndRejectedClickAllocateNothingAndValidClickCommitsExactBodyWithUndoRedo()
    {
        await using var harness = await Harness.CreateAsync();
        var identities = new CountingIdentityProvider();
        var controller = Controller(harness, identities);
        harness.Selection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var before = harness.Composition.Document.CaptureSnapshot();
        var beforeState = harness.Session.CaptureState();
        var beforeEvents = harness.Events.Events.Count;
        var exported = NativeDocumentSerializer.Export(harness.Composition.Document);
        var free = new PointD(1100d, 400d);

        var update = await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, free));
        Assert.Empty(update.Diagnostics);
        var green = Preview(harness);
        Assert.True(green.PlacementPreview!.IsAllowed);
        Assert.Equal(new RectD(1040d, 360d, 120d, 80d), green.Bounds);
        Assert.Equal(0, identities.Count);
        Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforeState.HistoryStatus, harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(beforeEvents, harness.Events.Events.Count);
        Assert.Equal(exported.AsEnumerable(), NativeDocumentSerializer.Export(harness.Composition.Document).AsEnumerable());

        var unchanged = harness.Session.CaptureState();
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, free));
        Assert.Same(unchanged.CurrentScene, harness.Session.CaptureState().CurrentScene);
        Assert.Equal(unchanged.Generation, harness.Session.CaptureState().Generation);

        var obstacle = Bodies(harness).First();
        var redClick = await controller.TryPlaceAtCssPointAsync(harness.Session, Css(harness, Center(obstacle.Bounds)));
        Assert.False(redClick.IsCommitted);
        Assert.Contains(redClick.Diagnostics, diagnostic => diagnostic.Code == "TOOLBOX_PLACEMENT_BLOCKED");
        Assert.False(Preview(harness).PlacementPreview!.IsAllowed);
        Assert.True(controller.IsPlacementActive);
        Assert.Equal(0, identities.Count);
        Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforeState.HistoryStatus, harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(beforeEvents, harness.Events.Events.Count);

        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, free));
        var expected = Preview(harness).Bounds;
        var placed = await controller.TryPlaceAtCssPointAsync(harness.Session, Css(harness, free));
        await harness.WaitForIdleAsync();
        Assert.True(placed.IsCommitted, Diagnostics(placed));
        Assert.Equal(1, identities.Count);
        var after = harness.Composition.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(before.SemanticModel.ElementCount + 1, after.SemanticModel.ElementCount);
        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1, harness.Session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(beforeEvents + 1, harness.Events.Events.Count);
        Assert.Equal(expected, Bodies(harness).Single(item => item.Origin.VisualStateId == placed.CreatedVisualStateId).Bounds);
        Assert.Equal(placed.CreatedVisualStateId, Assert.Single(harness.Session.CaptureState().EditorState.Selection));
        Assert.DoesNotContain(harness.Session.CaptureState().EditorState.TemporaryFeedback, feedback => feedback.PlacementPreview is not null);
        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.WaitForIdleAsync();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), harness.Composition.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.WaitForIdleAsync();
        Assert.Equal(after.SemanticModel.Elements.AsEnumerable(), harness.Composition.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
        Assert.Equal(after.VisualModel.VisualStates.AsEnumerable(), harness.Composition.Document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task FinalClickRechecksNewObstacleAndCurrentViewportInsteadOfPriorGreenFeedback()
    {
        await using var harness = await Harness.CreateAsync();
        var identities = new CountingIdentityProvider();
        var controller = Controller(harness, identities);
        harness.Selection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var point = new PointD(1000d, 500d);
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, point));
        Assert.True(Preview(harness).PlacementPreview!.IsAllowed);
        var state = harness.Session.CaptureState();
        var obstacle = new CreateBpmnTaskCommand(state.DocumentId, state.DocumentRevision,
            new SemanticElementId("a1214:late-obstacle"), new VisualStateId("a1214:late-obstacle:visual"),
            new PointD(940d, 460d), new SizeD(120d, 80d), "LATE", "Late obstacle", 2001L, VisualPlacementMode.Pinned);
        Assert.True((await harness.Session.ExecuteAsync(obstacle)).IsCommitted);
        await harness.WaitForIdleAsync();
        Assert.True((await harness.Session.UpdateViewportAsync(new ViewportSnapshot(1.5d, new VectorD(70d, -20d)))).Succeeded);
        var before = harness.Session.CaptureState();
        var result = await controller.TryPlaceAtCssPointAsync(harness.Session, Css(harness, point));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TOOLBOX_PLACEMENT_BLOCKED");
        Assert.Equal(before.DocumentRevision, harness.Session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(0, identities.Count);
        var bounds = Assert.IsType<RectD>(Preview(harness).Bounds);
        Assert.Equal(940d, bounds.X, 10);
        Assert.Equal(460d, bounds.Y, 10);
        Assert.Equal(new SizeD(120d, 80d), bounds.Size);
    }

    [Fact]
    public async Task BoundaryContactIsAllowedButPositiveAreaOverlapAndNegativeOriginAreRejected()
    {
        await using var harness = await Harness.CreateAsync();
        var controller = Controller(harness, new CountingIdentityProvider());
        harness.Selection.Select(new ToolboxItemId("bpmn:toolbox:start-event"));
        var state = harness.Session.CaptureState();
        Assert.True((await harness.Session.ExecuteAsync(new CreateBpmnTaskCommand(state.DocumentId, state.DocumentRevision,
            new SemanticElementId("a1214:isolated"), new VisualStateId("a1214:isolated:visual"),
            new PointD(1000d, 500d), new SizeD(120d, 80d), "ISOLATED", "Isolated", 2002L, VisualPlacementMode.Pinned))).IsCommitted);
        await harness.WaitForIdleAsync();
        Assert.True((await harness.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [new VisualStateId("a1214:isolated:visual")],
            viewport: harness.Session.CaptureState().EditorState.Viewport))).Succeeded);
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, new PointD(1138d, 540d)));
        Assert.True(Preview(harness).PlacementPreview!.IsAllowed);
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, new PointD(1137.999d, 540d)));
        Assert.False(Preview(harness).PlacementPreview!.IsAllowed);
        Assert.Contains(Preview(harness).PlacementPreview!.Diagnostics, diagnostic => diagnostic.Code == "TOOLBOX_PLACEMENT_BLOCKED");
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, new PointD(17d, 17d)));
        Assert.False(Preview(harness).PlacementPreview!.IsAllowed);
        Assert.Equal(new RectD(-1d, -1d, 36d, 36d), Preview(harness).Bounds);
    }

    [Fact]
    public async Task CaptionOutsideNodeBodyDoesNotBecomeAnExtraPlacementObstacle()
    {
        await using var harness = await Harness.CreateAsync();
        var controller = Controller(harness, new CountingIdentityProvider());
        var semanticId = new SemanticElementId("a1214:caption-owner");
        var state = harness.Session.CaptureState();
        Assert.True((await harness.Session.ExecuteAsync(new CreateBpmnExclusiveGatewayCommand(state.DocumentId, state.DocumentRevision,
            semanticId, new VisualStateId("a1214:caption-owner:visual"), new PointD(1000d, 500d),
            new SizeD(48d, 48d), "CAPTION", "Gateway caption outside body",
            VisualPlacementMode.Pinned))).IsCommitted);
        await harness.WaitForIdleAsync();
        var body = Bodies(harness).Single(item => item.Origin.SemanticElementId == semanticId);
        var caption = harness.Session.CaptureState().CurrentScene!.Items.Where(item => item.Origin.SemanticElementId == semanticId &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text && item.Bounds.Top >= body.Bounds.Bottom)
            .OrderByDescending(item => item.Bounds.Bottom).First();
        var point = new PointD(caption.Bounds.X + caption.Bounds.Width / 2d, caption.Bounds.Bottom + 10d);
        var candidate = new RectD(point.X - 18d, point.Y - 18d, 36d, 36d);
        Assert.True(candidate.Intersects(caption.Bounds));
        Assert.False(candidate.Intersects(body.Bounds));
        harness.Selection.Select(new ToolboxItemId("bpmn:toolbox:start-event"));
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, point));
        Assert.True(Preview(harness).PlacementPreview!.IsAllowed);
    }

    [Fact]
    public async Task ConnectorPathsAndSelectionDoNotBlockAndCandidateNeverBecomesAnObstacle()
    {
        await using var harness = await Harness.CreateAsync();
        var controller = Controller(harness, new CountingIdentityProvider());
        harness.Selection.Select(new ToolboxItemId("bpmn:toolbox:start-event"));
        var bodies = Bodies(harness).ToArray();
        var scene = harness.Session.CaptureState().CurrentScene!;
        var candidates = scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Connector && item.Geometry.Points.Length >= 2)
            .SelectMany(item => item.Geometry.Points.Zip(item.Geometry.Points.Skip(1),
                (left, right) => item.Transform.TransformPoint(new PointD((left.X + right.X) / 2d, (left.Y + right.Y) / 2d))))
            .Where(point => point.X >= 18d && point.Y >= 18d &&
                !bodies.Any(body => body.Bounds.Intersects(new RectD(point.X - 18d, point.Y - 18d, 36d, 36d))))
            .ToArray();
        Assert.NotEmpty(candidates);
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, candidates[0]));
        Assert.True(Preview(harness).PlacementPreview!.IsAllowed);
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, candidates[0] + new VectorD(0.1d, 0d)));
        Assert.True(Preview(harness).PlacementPreview!.IsAllowed);
        Assert.DoesNotContain(Bodies(harness), item => (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0);
        Assert.True(controller.Cancel());
        Assert.True(await ToolboxPlacementController.ClearPreviewAsync(harness.Session));
        Assert.DoesNotContain(harness.Session.CaptureState().EditorState.TemporaryFeedback, feedback => feedback.PlacementPreview is not null);
    }

    [Fact]
    public async Task BoundaryEventRequiresAttachmentButHostOverlapIsAllowedAndFinalGeometryIsExact()
    {
        await using var harness = await Harness.CreateAsync();
        var identities = new CountingIdentityProvider();
        var controller = Controller(harness, identities);
        harness.Selection.Select(new ToolboxItemId("bpmn:toolbox:timer-boundary-event"));
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, new PointD(1100d, 600d)));
        Assert.False(Preview(harness).PlacementPreview!.IsAllowed);
        var invalid = await controller.TryPlaceAtCssPointAsync(harness.Session, Css(harness, new PointD(1100d, 600d)));
        Assert.False(invalid.IsCommitted);
        Assert.Equal(0, identities.Count);
        var host = Bodies(harness).First(item => harness.Composition.Document.CaptureSnapshot().SemanticModel.Elements.Any(element =>
            element.Id == item.Origin.SemanticElementId && BpmnActivitySemanticTypes.IsActivity(element.TypeId)));
        var attachmentPoint = new PointD(host.Bounds.Left + host.Bounds.Width / 2d, host.Bounds.Bottom);
        await controller.UpdatePreviewAtCssPointAsync(harness.Session, Css(harness, attachmentPoint));
        var preview = Preview(harness);
        Assert.True(preview.PlacementPreview!.IsAllowed, string.Join("; ", preview.PlacementPreview.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(host.Origin.VisualStateId, preview.PlacementPreview.AttachmentCandidate!.Target.VisualStateId);
        Assert.True(host.Bounds.Intersects(preview.Bounds!.Value));
        var result = await controller.TryPlaceAtCssPointAsync(harness.Session, Css(harness, attachmentPoint));
        await harness.WaitForIdleAsync();
        Assert.True(result.IsCommitted, Diagnostics(result));
        Assert.Equal(preview.Bounds, Bodies(harness).Single(item => item.Origin.VisualStateId == result.CreatedVisualStateId).Bounds);
    }

    private static ToolboxPlacementController Controller(Harness harness, CountingIdentityProvider identities) =>
        new(harness.Composition.ToolboxPlacementCatalog, harness.Selection, identities, harness.Composition.SpatialEditPlanners);

    private static EditorFeedbackSnapshot Preview(Harness harness) =>
        Assert.Single(harness.Session.CaptureState().EditorState.TemporaryFeedback, feedback => feedback.PlacementPreview is not null);

    private static IEnumerable<Canvas2DSceneItem> Bodies(Harness harness) =>
        harness.Session.CaptureState().CurrentScene!.Items.Where(item => item.IsVisible &&
            item.Origin.ProjectedObjectId is { } id && item.Id == Canvas2DSceneObjectIdentity.ForProjected(id, "node"));

    private static PointD Css(Harness harness, PointD point) =>
        harness.Session.CaptureState().CurrentScene!.ViewportTransform.TransformPoint(point);

    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2d, bounds.Y + bounds.Height / 2d);

    private static string Diagnostics(ToolboxPlacementControllerResult result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class CountingIdentityProvider : IDocumentCreationIdentityProvider
    {
        internal int Count { get; private set; }

        public DocumentCreationIdentity CreateIdentity()
        {
            Count++;
            return new DocumentCreationIdentity(new SemanticElementId($"a1214:created:{Count}"),
                new VisualStateId($"a1214:created:{Count}:visual"));
        }
    }
}
