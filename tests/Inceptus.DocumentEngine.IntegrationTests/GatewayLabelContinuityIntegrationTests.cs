using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Fixture = Inceptus.DocumentEngine.IntegrationTests.PhaseA122PanSceneReuseIntegrationTests.Fixture;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class GatewayLabelContinuityIntegrationTests
{
    private static readonly double[] Zooms = [1d, 1.75d];
    public static IEnumerable<object[]> GatewaysAndZooms =>
        new[] { BpmnSemanticTypes.ExclusiveGateway, BpmnSemanticTypes.ParallelGateway,
            BpmnSemanticTypes.InclusiveGateway, BpmnSemanticTypes.EventBasedGateway }
        .SelectMany(type => Zooms.Select(zoom => new object[] { type.Value, zoom }));

    [Theory]
    [MemberData(nameof(GatewaysAndZooms))]
    public async Task BodyEdgesAndCornersShareContinuousBoundaryWithoutPersistentEffects(string type, double zoom)
    {
        await using var test = await Fixture.CreateAsync();
        var label = await SelectLabelAsync(test, type, zoom);
        var before = test.State;
        var snapshot = test.Snapshot;
        var events = test.Events.Count;
        await using var controller = test.CreateInteractionController();
        var center = Center(label.Bounds);
        foreach (var direction in PhaseM323BpmnManualGatewayLabelIntegrationTests.ResizeCases)
        {
            var role = (string)direction[0];
            var cursor = (string)direction[1];
            var zone = Zone(test, label, role);
            var edge = Center(zone.Bounds);
            // Sample the complete approach, including the overlap between body and zones.
            for (var step = 0; step <= 16; step++)
            {
                var point = center + new VectorD((edge.X - center.X) * step / 16,
                    (edge.Y - center.Y) * step / 16);
                Assert.True(label.Bounds.Contains(point) || zone.Bounds.Contains(point));
                var result = await controller.PointerMovedAsync(Pointer(test, point));
                AssertBoundary(test, label, $"{role}, step {step}, point {point}");
                if (step == 0) Assert.Equal("grab", result.CssCursor);
                if (step == 16)
                {
                    Assert.Equal(cursor, result.CssCursor);
                    Assert.Equal(zone.Id, test.State.EditorState.HoveredObjectId);
                }
            }

            // The activation area intentionally extends beyond the painted boundary.
            var outside = edge + new VectorD(Math.Sign(edge.X - center.X) * 2,
                Math.Sign(edge.Y - center.Y) * 2);
            Assert.False(label.Bounds.Contains(outside));
            Assert.True(zone.Bounds.Contains(outside));
            var outsideHover = await controller.PointerMovedAsync(Pointer(test, outside));
            Assert.Equal(cursor, outsideHover.CssCursor);
            Assert.Equal(zone.Id, test.State.EditorState.HoveredObjectId);
            AssertBoundary(test, label);
            Assert.DoesNotContain(test.State.CurrentScene!.Items,
                item => item.Origin.StableSourceKey == $"hover:{zone.Id.Value}");
        }

        await controller.PointerMovedAsync(Pointer(test, new PointD(5000, 5000)));
        Assert.DoesNotContain(test.State.CurrentScene!.Items,
            item => item.Origin.StableSourceKey == $"hover:{label.Id.Value}");
        Assert.Same(snapshot, test.Snapshot);
        Assert.Equal(before.DocumentRevision, test.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        Assert.Same(before.LayoutResult, test.State.LayoutResult);
        Assert.Same(before.RoutingResult, test.State.RoutingResult);
    }

    [Theory]
    [MemberData(nameof(GatewaysAndZooms))]
    public async Task BodyMovesAndZoneResizesWithVisiblePreviewAndOneExactHistoryCommit(string type, double zoom)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var test = await Fixture.CreateAsync(composition);
        var label = await SelectLabelAsync(test, type, zoom);
        var before = test.State;
        var snapshot = test.Snapshot;
        var events = test.Events.Count;
        var original = snapshot.VisualModel.VisualStates.Single(item => item.Id == label.Origin.VisualStateId);
        await using var controller = test.CreateInteractionController();

        await controller.PointerMovedAsync(Pointer(test, Center(label.Bounds)));
        await controller.PointerPressedAsync(Pointer(test, Center(label.Bounds), 1));
        await controller.PointerMovedAsync(Pointer(test, Center(label.Bounds) + new VectorD(20, 15), 1));
        Assert.Equal(Canvas2DNodeLabelGestureMetadata.MoveOperation,
            test.State.EditorState.ActiveGesture!.Properties[Canvas2DNodeLabelGestureMetadata.Operation].TextValue);
        Assert.Equal(label.Bounds.Translate(new VectorD(20, 15)), Preview(test, label).Bounds);
        await controller.CancelActiveGestureAsync();
        Assert.Same(snapshot, test.Snapshot);

        var start = Center(Zone(test, label, "southeast").Bounds);
        var end = start + new VectorD(32, 24);
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        foreach (var delta in new[] { new VectorD(12, 8), new VectorD(32, 24) })
        {
            var move = await controller.PointerMovedAsync(Pointer(test, start + delta, 1));
            Assert.Equal("nwse-resize", move.CssCursor);
            Assert.Equal(Canvas2DNodeLabelGestureMetadata.ResizeOperation,
                test.State.EditorState.ActiveGesture!.Properties[Canvas2DNodeLabelGestureMetadata.Operation].TextValue);
            var preview = Preview(test, label);
            Assert.True(preview.IsVisible);
            AssertBoundary(test, label);
            AssertBounds(new RectD(label.Bounds.X, label.Bounds.Y,
                label.Bounds.Width + delta.X, label.Bounds.Height + delta.Y), preview.Bounds);
            Assert.Same(snapshot, test.Snapshot);
            Assert.Equal(before.DocumentRevision, test.State.DocumentRevision);
            Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
            Assert.Equal(events, test.Events.Count);
        }

        var released = await controller.PointerReleasedAsync(Pointer(test, end));
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(snapshot.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(events + 1, test.Events.Count);
        var committed = test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id);
        Assert.True(NodeLabelVisualOverride.TryRead(committed.Properties, out _));
        Assert.Equal(original.Position, committed.Position);
        Assert.Equal(original.Size, committed.Size);
        Assert.Equal(original.ConnectorAnchors.ToArray(), committed.ConnectorAnchors.ToArray());
        var committedBounds = CurrentLabel(test, label.Origin.SemanticElementId!).Bounds;
        AssertBounds(new RectD(label.Bounds.X, label.Bounds.Y,
            label.Bounds.Width + 32, label.Bounds.Height + 24), committedBounds);

        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(original, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id));
        Assert.Equal(label.Bounds, CurrentLabel(test, label.Origin.SemanticElementId!).Bounds);
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(committed, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id));
        Assert.Equal(committedBounds, CurrentLabel(test, label.Origin.SemanticElementId!).Bounds);
    }

    private static async Task<Canvas2DSceneItem> SelectLabelAsync(Fixture test, string type, double zoom)
    {
        // Use the named split examples: the demo's Parallel Join label overlaps
        // the next Gateway label, which legitimately has its own hover ownership.
        var id = type switch
        {
            "BPMN.ExclusiveGateway" => BpmnDemoPipeline.ExclusiveGatewayId,
            "BPMN.ParallelGateway" => BpmnDemoPipeline.ParallelSplitGatewayId,
            "BPMN.InclusiveGateway" => BpmnDemoPipeline.InclusiveSplitGatewayId,
            "BPMN.EventBasedGateway" => BpmnDemoPipeline.EventBasedGatewayId,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        var visual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == id);
        Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, zoom == 1 ? 1 : 2))).Succeeded);
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [visual.Id], viewport: new ViewportSnapshot(zoom, new VectorD(35, 19))))).Succeeded);
        return CurrentLabel(test, id);
    }

    private static Canvas2DSceneItem CurrentLabel(Fixture test, SemanticElementId id) =>
        test.State.CurrentScene!.Items.Single(item => item.Origin.SemanticElementId == id &&
            item.Layer == Canvas2DSceneLayer.Label && item.IsVisible &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));

    private static Canvas2DSceneItem Zone(Fixture test, Canvas2DSceneItem label, string role) =>
        test.State.CurrentScene!.Items.Single(item =>
            item.Origin.StableSourceKey == $"node-label-resize-zone:{role}:{label.Id.Value}");

    private static Canvas2DSceneItem Preview(Fixture test, Canvas2DSceneItem label) =>
        Assert.Single(test.State.CurrentScene!.Items, item =>
            item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId &&
            item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);

    private static void AssertBounds(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Width, actual.Width, 8);
        Assert.Equal(expected.Height, actual.Height, 8);
    }

    private static void AssertBoundary(Fixture test, Canvas2DSceneItem label, string sample = "outside")
    {
        Assert.True(test.State.CurrentScene!.Items.Any(item => item.Origin.StableSourceKey == $"hover:{label.Id.Value}"),
            $"{sample}; label={label.Id} bounds={label.Bounds}; hover={test.State.EditorState.HoveredObjectId}");
        var boundary = Assert.Single(test.State.CurrentScene!.Items,
            item => item.Origin.StableSourceKey == $"hover:{label.Id.Value}");
        Assert.True(boundary.IsVisible);
        Assert.True(boundary.Style.Opacity > 0);
        Assert.True(boundary.Style.StrokeWidth > 0);
        Assert.NotNull(boundary.Style.Stroke);
        var resizePreview = test.State.CurrentScene.Items.SingleOrDefault(item =>
            item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId &&
            item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        Assert.Equal(resizePreview?.Bounds ?? label.Bounds, boundary.Bounds);
        Assert.Equal(Canvas2DHitTestMode.None, boundary.HitTestPolicy.Mode);
        Assert.Contains(label.Id, boundary.Origin.RelatedSceneObjectIds);
    }

    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static Canvas2DPointerInput Pointer(Fixture test, PointD point, int buttons = 0) =>
        new(42, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
}
