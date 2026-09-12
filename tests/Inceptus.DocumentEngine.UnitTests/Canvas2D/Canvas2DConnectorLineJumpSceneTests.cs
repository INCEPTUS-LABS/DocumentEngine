using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorLineJumpSceneTests
{
    [Fact]
    public void HorizontalSegmentOwnsSampledPositiveYLineJump()
    {
        PointD[] horizontal = [new(0d, 10d), new(60d, 10d)];
        PointD[] vertical = [new(30d, 0d), new(30d, 20d)];

        var horizontalDisplay = Canvas2DConnectorLineJumpGeometry.Create(
            horizontal,
            [vertical]);
        var verticalDisplay = Canvas2DConnectorLineJumpGeometry.Create(
            vertical,
            [horizontal]);

        Assert.Equal(5d, Canvas2DConnectorLineJumpGeometry.HalfWidth);
        Assert.Equal(4d, Canvas2DConnectorLineJumpGeometry.Height);
        Assert.Equal(horizontal[0], horizontalDisplay[0]);
        Assert.Equal(horizontal[^1], horizontalDisplay[^1]);
        Assert.Contains(new PointD(25d, 10d), horizontalDisplay);
        Assert.Contains(new PointD(30d, 14d), horizontalDisplay);
        Assert.Contains(new PointD(35d, 10d), horizontalDisplay);
        Assert.All(horizontalDisplay, point => Assert.InRange(point.Y, 10d, 14d));
        Assert.Equal(vertical, verticalDisplay);
    }

    [Fact]
    public void OnlyStrictInteriorHorizontalVerticalCrossingsProduceJumps()
    {
        PointD[] horizontal = [new(0d, 0d), new(40d, 0d)];
        IReadOnlyList<PointD>[] otherPaths =
        [
            [new PointD(20d, -10d), new PointD(20d, 10d)],
            [new PointD(0d, -10d), new PointD(0d, 10d)],
            [new PointD(40d, -10d), new PointD(40d, 10d)],
            [new PointD(10d, 0d), new PointD(10d, 10d)],
            [new PointD(30d, -1e-10), new PointD(30d, 10d)],
            [new PointD(15d, -10d), new PointD(16d, 10d)],
            [new PointD(5d, 5d), new PointD(35d, 5d)],
        ];

        var display = Canvas2DConnectorLineJumpGeometry.Create(horizontal, otherPaths);

        var apex = Assert.Single(display, point =>
            point.Y.Equals(Canvas2DConnectorLineJumpGeometry.Height));
        Assert.Equal(20d, apex.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistinctCrowdedCrossingsAreDeduplicatedSortedAndShrunkToFit(bool reversed)
    {
        PointD[] horizontal = reversed
            ? [new PointD(30d, 0d), new PointD(0d, 0d)]
            : [new PointD(0d, 0d), new PointD(30d, 0d)];
        IReadOnlyList<PointD>[] otherPaths =
        [
            Vertical(28d),
            Vertical(7d),
            Vertical(2d),
            Vertical(8d),
            Vertical(7d),
        ];

        var display = Canvas2DConnectorLineJumpGeometry.Create(horizontal, otherPaths);
        var apexes = display
            .Where(point => point.Y.Equals(Canvas2DConnectorLineJumpGeometry.Height))
            .Select(static point => point.X)
            .ToArray();

        Assert.Equal(
            reversed ? [28d, 8d, 7d, 2d] : [2d, 7d, 8d, 28d],
            apexes);
        Assert.All(display, point =>
        {
            Assert.InRange(point.X, 0d, 30d);
            Assert.InRange(point.Y, 0d, Canvas2DConnectorLineJumpGeometry.Height);
            if (point.Y > 0d)
            {
                Assert.InRange(point.X, double.Epsilon, Math.BitDecrement(30d));
            }
        });
    }

    [Fact]
    public void PathMetadataRoundTripsLogicalAndEditablePathsAndFallsBackToDisplayGeometry()
    {
        PointD[] logical = [new(0d, 0d), new(20d, 0d), new(40d, 0d)];
        PointD[] editable = [new(0d, 0d), new(25d, 0d), new(40d, 0d)];
        PointD[] display =
        [
            new(0d, 0d),
            new(15d, 0d),
            new(20d, 4d),
            new(25d, 0d),
            new(40d, 0d),
        ];
        var item = Item(
            "metadata",
            display,
            Canvas2DConnectorPathMetadata.CreateProperties(logical, editable));

        Assert.Equal(logical, Canvas2DConnectorPathMetadata.Resolve(item));
        Assert.Equal(editable, Canvas2DConnectorPathMetadata.ResolveEditable(item));

        var fallback = Item("fallback", display);
        Assert.Equal(display, Canvas2DConnectorPathMetadata.Resolve(fallback));
        Assert.Equal(display, Canvas2DConnectorPathMetadata.ResolveEditable(fallback));

        var incomplete = Item(
            "incomplete",
            display,
            new PropertyMap(
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DConnectorPathMetadata.LogicalPathPointCount,
                    PropertyValue.FromInteger(3)),
            ]));
        Assert.Equal(display, Canvas2DConnectorPathMetadata.Resolve(incomplete));
    }

    [Fact]
    public void SceneUsesBridgedDisplayPathWhileMetadataAndRoutingRemainLogical()
    {
        var source = Canvas2DSceneTestData.Create();
        var existingEdge = Assert.Single(source.Graph.Edges);
        PointD[] persistentRoute =
        [
            new(109d, 45d),
            new(160d, 45d),
            new(211d, 45d),
        ];
        var horizontalEdge = new ProjectedEdge(
            existingEdge.Source,
            existingEdge.SourceNodeId,
            existingEdge.TargetNodeId,
            existingEdge.SourcePortId,
            existingEdge.TargetPortId,
            persistentRoute,
            existingEdge.SemanticProperties,
            existingEdge.ProjectedProperties,
            existingEdge.LayoutHints,
            existingEdge.RoutingHints,
            existingEdge.AlgorithmMetadata);
        var verticalEdge = new ProjectedEdge(
            new ProjectionSourceTrace(
                source.Graph.DocumentId,
                existingEdge.Source.RuleId,
                ProjectionSourceKind.SemanticRelationship,
                new SemanticElementId("test:semantic:line-jump-vertical"),
                existingEdge.Source.SemanticTypeId,
                "line-jump-vertical"),
            existingEdge.SourceNodeId,
            existingEdge.TargetNodeId);
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            [horizontalEdge, verticalEdge],
            source.Graph.Groups,
            source.Graph.Ports,
            source.Graph.Labels);
        var horizontalRoute = new RoutedConnectorGeometry(
            horizontalEdge.Id,
            new PointD(110d, 45d),
            new PointD(210d, 45d),
            [new PointD(130d, 45d), new PointD(190d, 45d)],
            metadata:
            [
                new KeyValuePair<string, PropertyValue>(
                    "test:route-marker",
                    PropertyValue.FromText("preserved")),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DConnectorPathMetadata.LogicalPathPointCount,
                    PropertyValue.FromInteger(999)),
            ]);
        var verticalRoute = new RoutedConnectorGeometry(
            verticalEdge.Id,
            new PointD(60d, 70d),
            new PointD(260d, 20d),
            [new PointD(160d, 70d), new PointD(160d, 20d)]);
        var routing = new RoutingResult(
            source.Routing.DocumentId,
            source.Routing.SourceRevision,
            source.Routing.LayoutAlgorithmId,
            source.Routing.RoutingAlgorithmId,
            new RoutingComputation([horizontalRoute, verticalRoute]));
        var logicalBefore = horizontalRoute.Path.ToArray();
        var verticalBefore = verticalRoute.Path.ToArray();
        var persistentBefore = horizontalEdge.PersistentRoute.ToArray();

        var result = new Canvas2DSceneBuilder().Build(
            graph,
            source.Layout,
            routing,
            source.VisualModel,
            source.EditorState);

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var horizontalItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(horizontalEdge.Id, "connector"));
        var verticalItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(verticalEdge.Id, "connector"));
        Assert.Contains(new PointD(155d, 45d), horizontalItem.Geometry.Points);
        Assert.Contains(new PointD(160d, 49d), horizontalItem.Geometry.Points);
        Assert.Contains(new PointD(165d, 45d), horizontalItem.Geometry.Points);
        Assert.Equal(new RectD(110d, 45d, 100d, 4d), horizontalItem.Bounds);
        Assert.Equal(
            verticalRoute.Path.AsEnumerable(),
            verticalItem.Geometry.Points.AsEnumerable());
        Assert.Equal(
            horizontalRoute.Path.AsEnumerable(),
            Canvas2DConnectorPathMetadata.Resolve(horizontalItem).AsEnumerable());
        PointD[] expectedEditable =
        [
            horizontalRoute.SourceAnchor,
            persistentRoute[1],
            horizontalRoute.DestinationAnchor,
        ];
        Assert.Equal(
            expectedEditable.AsEnumerable(),
            Canvas2DConnectorPathMetadata.ResolveEditable(horizontalItem).AsEnumerable());
        Assert.True(horizontalItem.Metadata[Canvas2DRouteGestureMetadata.RouteEditable].BooleanValue);
        Assert.Equal("preserved", horizontalItem.Metadata["test:route-marker"].TextValue);
        Assert.Equal(logicalBefore, horizontalRoute.Path);
        Assert.Equal(verticalBefore, verticalRoute.Path);
        Assert.Equal(persistentBefore, horizontalEdge.PersistentRoute);
        Assert.Same(horizontalRoute, routing.Routes.Single(route =>
            route.ProjectedEdgeId == horizontalEdge.Id));
    }

    [Theory]
    [InlineData("aa", -1)]
    [InlineData("zz", 1)]
    public void BridgePresentationDrawAndHitOwnershipIsStableAcrossIdsAndInteraction(
        string verticalIdentity,
        int expectedVerticalIdOrder)
    {
        var input = CreateCrossingInput(verticalIdentity);
        var horizontalId = Canvas2DSceneObjectIdentity.ForProjected(
            input.HorizontalEdge.Id,
            "connector");
        var verticalId = Canvas2DSceneObjectIdentity.ForProjected(
            input.VerticalEdge.Id,
            "connector");
        Assert.Equal(expectedVerticalIdOrder, Math.Sign(StringComparer.Ordinal.Compare(
            verticalId.Value,
            horizontalId.Value)));

        var idle = Build(input, EditorStateSnapshot.Empty);
        var horizontal = idle.Items.Single(item => item.Id == horizontalId);
        var vertical = idle.Items.Single(item => item.Id == verticalId);
        var mask = idle.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(
                input.HorizontalEdge.Id,
                "connector-line-jump-mask:0"));
        var jump = idle.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(
                input.HorizontalEdge.Id,
                "connector-line-jump:0"));
        var arrow = idle.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(
                input.HorizontalEdge.Id,
                "connector-target-arrow"));
        var apex = new PointD(180d, 49d);

        Assert.Equal(Canvas2DSceneLayer.Connector, mask.Layer);
        Assert.Equal(Canvas2DHitTestMode.None, mask.HitTestPolicy.Mode);
        Assert.Contains(horizontalId, mask.Origin.RelatedSceneObjectIds);
        Assert.True((mask.Origin.Categories & Canvas2DSceneOriginCategory.Configuration) != 0);
        Assert.Equal("connector-line-jump-mask:0", mask.Origin.StableSourceKey);
        Assert.Equal("#ffffff", mask.Style.Stroke);
        Assert.Equal(3d, mask.Style.StrokeWidth);
        Assert.Equal(horizontal.ZIndex, vertical.ZIndex);
        Assert.True(horizontal.ZIndex < mask.ZIndex);
        Assert.True(vertical.ZIndex < mask.ZIndex);
        Assert.True(arrow.ZIndex < mask.ZIndex);
        Assert.True(mask.ZIndex < jump.ZIndex);
        Assert.True(IndexOf(idle, verticalId) < IndexOf(idle, mask.Id));
        Assert.True(IndexOf(idle, horizontalId) < IndexOf(idle, mask.Id));
        Assert.True(IndexOf(idle, arrow.Id) < IndexOf(idle, mask.Id));
        Assert.True(IndexOf(idle, mask.Id) < IndexOf(idle, jump.Id));
        AssertHit(idle, apex, horizontalId);

        var verticalSelected = Build(
            input,
            new EditorStateSnapshot(selection: [input.VerticalVisualStateId]));
        var verticalSelection = Overlay(verticalSelected, "selection", verticalId);
        Assert.Equal(Canvas2DSceneLayer.Connector, verticalSelection.Layer);
        Assert.True(vertical.ZIndex < verticalSelection.ZIndex);
        Assert.True(verticalSelection.ZIndex < mask.ZIndex);
        AssertHit(verticalSelected, apex, horizontalId);

        var verticalHovered = Build(
            input,
            new EditorStateSnapshot(hoveredObjectId: verticalId));
        var verticalHover = Overlay(verticalHovered, "hover", verticalId);
        Assert.Equal(Canvas2DSceneLayer.Connector, verticalHover.Layer);
        Assert.True(vertical.ZIndex < verticalHover.ZIndex);
        Assert.True(verticalHover.ZIndex < mask.ZIndex);
        AssertHit(verticalHovered, apex, horizontalId);

        var horizontalSelected = Build(
            input,
            new EditorStateSnapshot(selection: [input.HorizontalVisualStateId]));
        var horizontalSelection = Overlay(
            horizontalSelected,
            "selection",
            horizontalId);
        var horizontalJumpSelection = Overlay(
            horizontalSelected,
            "selection",
            jump.Id);
        var horizontalArrowSelection = Overlay(
            horizontalSelected,
            "selection",
            arrow.Id);
        Assert.Equal(Canvas2DSceneLayer.Connector, horizontalSelection.Layer);
        Assert.Equal("#2563eb", horizontalSelection.Style.Stroke);
        Assert.True(horizontal.ZIndex < horizontalSelection.ZIndex);
        Assert.True(horizontalSelection.ZIndex < mask.ZIndex);
        Assert.True(jump.ZIndex < horizontalJumpSelection.ZIndex);
        Assert.Equal("#2563eb", horizontalJumpSelection.Style.Stroke);
        Assert.True(arrow.ZIndex < horizontalArrowSelection.ZIndex);
        Assert.True(horizontalArrowSelection.ZIndex < mask.ZIndex);
        Assert.Equal("#2563eb", horizontalArrowSelection.Style.Fill);
        Assert.Equal("#2563eb", horizontalArrowSelection.Style.Stroke);
        AssertHit(horizontalSelected, apex, horizontalId);

        var horizontalHovered = Build(
            input,
            new EditorStateSnapshot(hoveredObjectId: horizontalId));
        var horizontalHover = Overlay(horizontalHovered, "hover", horizontalId);
        var horizontalJumpHover = Overlay(
            horizontalHovered,
            "hover",
            jump.Id);
        var horizontalArrowHover = Overlay(
            horizontalHovered,
            "hover",
            arrow.Id);
        Assert.Equal(Canvas2DSceneLayer.Connector, horizontalHover.Layer);
        Assert.Equal("#f59e0b", horizontalHover.Style.Stroke);
        Assert.True(horizontal.ZIndex < horizontalHover.ZIndex);
        Assert.True(horizontalHover.ZIndex < mask.ZIndex);
        Assert.True(jump.ZIndex < horizontalJumpHover.ZIndex);
        Assert.Equal("#f59e0b", horizontalJumpHover.Style.Stroke);
        Assert.True(arrow.ZIndex < horizontalArrowHover.ZIndex);
        Assert.True(horizontalArrowHover.ZIndex < mask.ZIndex);
        Assert.Equal("#f59e0b", horizontalArrowHover.Style.Fill);
        Assert.Equal("#f59e0b", horizontalArrowHover.Style.Stroke);
        AssertHit(horizontalHovered, apex, horizontalId);

        var targetArrowHovered = Build(
            input,
            new EditorStateSnapshot(hoveredObjectId: arrow.Id));
        Assert.Equal(
            "#f59e0b",
            Overlay(targetArrowHovered, "hover", horizontalId).Style.Stroke);
        Assert.Equal(
            "#f59e0b",
            Overlay(targetArrowHovered, "hover", jump.Id).Style.Stroke);
        var arrowHoverFromArrowHit = Overlay(
            targetArrowHovered,
            "hover",
            arrow.Id);
        Assert.Equal("#f59e0b", arrowHoverFromArrowHit.Style.Fill);
        Assert.Equal("#f59e0b", arrowHoverFromArrowHit.Style.Stroke);
        AssertHit(targetArrowHovered, apex, horizontalId);

        Assert.Equal(input.HorizontalPathBefore, input.HorizontalRoute.Path);
        Assert.Equal(input.VerticalPathBefore, input.VerticalRoute.Path);
        Assert.Equal(input.VisualStatesBefore, input.VisualModel.VisualStates);
        Assert.All(input.VisualModel.VisualStates, visual => Assert.Empty(visual.Route));
    }

    [Theory]
    [InlineData("aa")]
    [InlineData("zz")]
    public void PerJumpProxyPreservesOwnershipWhenCrossedConnectorOwnsAnotherJump(
        string multiOwnerIdentity)
    {
        var source = CreateCrossingInput(multiOwnerIdentity);
        var thirdSemanticId = new SemanticElementId("test:semantic:multi-owner-third");
        var thirdVisualStateId = new VisualStateId("test:visual:multi-owner-third");
        var thirdEdge = new ProjectedEdge(
            new ProjectionSourceTrace(
                source.Graph.DocumentId,
                source.HorizontalEdge.Source.RuleId,
                ProjectionSourceKind.SemanticRelationship,
                thirdSemanticId,
                source.HorizontalEdge.Source.SemanticTypeId,
                "multi-owner-third",
                thirdVisualStateId),
            source.HorizontalEdge.SourceNodeId,
            source.HorizontalEdge.TargetNodeId);
        var multiOwnerRoute = source.VerticalRoute;
        var thirdRoute = new RoutedConnectorGeometry(
            thirdEdge.Id,
            new PointD(60d, 70d),
            new PointD(260d, 20d),
            [
                new PointD(60d, 100d),
                new PointD(120d, 100d),
                new PointD(120d, 10d),
                new PointD(260d, 10d),
            ]);
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            [source.HorizontalEdge, source.VerticalEdge, thirdEdge],
            source.Graph.Groups,
            source.Graph.Ports,
            source.Graph.Labels);
        var routing = new RoutingResult(
            source.Routing.DocumentId,
            source.Routing.SourceRevision,
            source.Routing.LayoutAlgorithmId,
            source.Routing.RoutingAlgorithmId,
            new RoutingComputation(
                [source.HorizontalRoute, multiOwnerRoute, thirdRoute]));
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Append(new VisualStateSnapshot(
                thirdVisualStateId,
                thirdSemanticId,
                default,
                default,
                VisualPlacementMode.Automatic)));
        var input = source with
        {
            Graph = graph,
            Routing = routing,
            VisualModel = visualModel,
            VerticalRoute = multiOwnerRoute,
            VerticalPathBefore = multiOwnerRoute.Path,
            VisualStatesBefore = visualModel.VisualStates,
        };
        var horizontalId = Canvas2DSceneObjectIdentity.ForProjected(
            input.HorizontalEdge.Id,
            "connector");
        var multiOwnerId = Canvas2DSceneObjectIdentity.ForProjected(
            input.VerticalEdge.Id,
            "connector");
        var horizontalMaskId = Canvas2DSceneObjectIdentity.ForProjected(
            input.HorizontalEdge.Id,
            "connector-line-jump-mask:0");
        var horizontalJumpId = Canvas2DSceneObjectIdentity.ForProjected(
            input.HorizontalEdge.Id,
            "connector-line-jump:0");
        var multiOwnerJumpId = Canvas2DSceneObjectIdentity.ForProjected(
            input.VerticalEdge.Id,
            "connector-line-jump:0");
        var horizontalApex = new PointD(180d, 49d);
        var multiOwnerApex = new PointD(120d, 74d);

        var idle = Build(input, EditorStateSnapshot.Empty);

        Assert.Equal(0, idle.Items.Single(item => item.Id == horizontalId).ZIndex);
        Assert.Equal(0, idle.Items.Single(item => item.Id == multiOwnerId).ZIndex);
        Assert.True(IndexOf(idle, multiOwnerId) < IndexOf(idle, horizontalMaskId));
        Assert.True(IndexOf(idle, horizontalMaskId) < IndexOf(idle, horizontalJumpId));
        AssertHit(idle, horizontalApex, horizontalId);
        AssertHit(idle, multiOwnerApex, multiOwnerId);

        var selected = Build(
            input,
            new EditorStateSnapshot(selection: [input.VerticalVisualStateId]));
        var selection = Overlay(selected, "selection", multiOwnerId);
        var jumpSelection = Overlay(selected, "selection", multiOwnerJumpId);
        Assert.True(selection.ZIndex <
            selected.Items.Single(item => item.Id == horizontalMaskId).ZIndex);
        Assert.True(
            selected.Items.Single(item => item.Id == multiOwnerJumpId).ZIndex <
            jumpSelection.ZIndex);
        AssertHit(selected, horizontalApex, horizontalId);
        AssertHit(selected, multiOwnerApex, multiOwnerId);

        var hovered = Build(
            input,
            new EditorStateSnapshot(hoveredObjectId: multiOwnerId));
        AssertHit(hovered, horizontalApex, horizontalId);
        AssertHit(hovered, multiOwnerApex, multiOwnerId);
    }

    private static PointD[] Vertical(double x) => [new(x, -10d), new(x, 10d)];

    private static Canvas2DSceneItem Item(
        string key,
        IReadOnlyList<PointD> displayPath,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null) =>
        new(
            new SceneObjectId($"test:line-jump:{key}"),
            Canvas2DSceneLayer.Connector,
            0,
            Canvas2DSceneGeometry.Path(displayPath),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.Configuration,
                stableSourceKey: $"test:line-jump:{key}"),
            metadata: metadata);

    private static CrossingInput CreateCrossingInput(string verticalIdentity)
    {
        var source = Canvas2DSceneTestData.Create();
        var horizontalEdge = Assert.Single(source.Graph.Edges);
        var horizontalRoute = Assert.Single(source.Routing.Routes);
        var verticalSemanticId = new SemanticElementId(
            $"test:semantic:{verticalIdentity}");
        var verticalVisualStateId = new VisualStateId(
            $"test:visual:{verticalIdentity}");
        var verticalEdge = new ProjectedEdge(
            new ProjectionSourceTrace(
                source.Graph.DocumentId,
                horizontalEdge.Source.RuleId,
                ProjectionSourceKind.SemanticRelationship,
                verticalSemanticId,
                horizontalEdge.Source.SemanticTypeId,
                verticalIdentity,
                verticalVisualStateId),
            horizontalEdge.SourceNodeId,
            horizontalEdge.TargetNodeId);
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            [horizontalEdge, verticalEdge],
            source.Graph.Groups,
            source.Graph.Ports,
            source.Graph.Labels);
        var verticalRoute = new RoutedConnectorGeometry(
            verticalEdge.Id,
            new PointD(60d, 70d),
            new PointD(260d, 20d),
            [new PointD(180d, 70d), new PointD(180d, 20d)]);
        var routing = new RoutingResult(
            source.Routing.DocumentId,
            source.Routing.SourceRevision,
            source.Routing.LayoutAlgorithmId,
            source.Routing.RoutingAlgorithmId,
            new RoutingComputation([horizontalRoute, verticalRoute]));
        var verticalVisualState = new VisualStateSnapshot(
            verticalVisualStateId,
            verticalSemanticId,
            default,
            default,
            VisualPlacementMode.Automatic);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Append(verticalVisualState));

        return new CrossingInput(
            graph,
            source.Layout,
            routing,
            visualModel,
            horizontalEdge,
            verticalEdge,
            horizontalRoute,
            verticalRoute,
            horizontalEdge.Source.VisualStateId!,
            verticalVisualStateId,
            horizontalRoute.Path,
            verticalRoute.Path,
            visualModel.VisualStates);
    }

    private static Canvas2DScene Build(
        CrossingInput input,
        EditorStateSnapshot editorState)
    {
        var result = new Canvas2DSceneBuilder().Build(
            input.Graph,
            input.Layout,
            input.Routing,
            input.VisualModel,
            editorState);
        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return Assert.IsType<Canvas2DScene>(result.Scene);
    }

    private static Canvas2DSceneItem Overlay(
        Canvas2DScene scene,
        string role,
        SceneObjectId targetId) =>
        scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForEditorState(
            $"{role}:{targetId.Value}"));

    private static void AssertHit(
        Canvas2DScene scene,
        PointD point,
        SceneObjectId expectedId) =>
        Assert.Equal(
            expectedId,
            new Canvas2DSceneHitTestService().HitTest(scene, point)?.SceneObjectId);

    private static int IndexOf(Canvas2DScene scene, SceneObjectId itemId) =>
        Array.FindIndex(scene.Items.ToArray(), item => item.Id == itemId);

    private sealed record CrossingInput(
        ProjectedGraph Graph,
        Contracts.Layout.LayoutResult Layout,
        RoutingResult Routing,
        VisualModelSnapshot VisualModel,
        ProjectedEdge HorizontalEdge,
        ProjectedEdge VerticalEdge,
        RoutedConnectorGeometry HorizontalRoute,
        RoutedConnectorGeometry VerticalRoute,
        VisualStateId HorizontalVisualStateId,
        VisualStateId VerticalVisualStateId,
        ImmutableArray<PointD> HorizontalPathBefore,
        ImmutableArray<PointD> VerticalPathBefore,
        ImmutableArray<VisualStateSnapshot> VisualStatesBefore);
}
