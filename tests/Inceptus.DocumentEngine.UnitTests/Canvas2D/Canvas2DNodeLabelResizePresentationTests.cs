using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DNodeLabelResizePresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResizeReevaluatesUnknownContributorsAndReusesOnlyExactlyEqualContent(bool changesDrawing)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var node = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(node.Source, node.Id, "Decision label",
            nodePlacement: new NodeLabelPlacement(NodeLabelPlacementKind.OutsideBelow, 8, 120),
            nodeInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var graph = new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, inputs.Graph.Edges, inputs.Graph.Groups, inputs.Graph.Ports, [label]);
        var contributor = new ResizeContributor(changesDrawing);
        var builder = new Canvas2DSceneBuilder(contributors: [new(new(ResizeContributor.Id, "1"), contributor)]);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var view = ModelProfileViewStateSnapshot.Empty;
        var elements = ModelProfileElementViewStateSnapshot.Empty;
        async Task<Canvas2DScene> Build(EditorStateSnapshot state) => Assert.IsType<Canvas2DScene>(
            (await builder.BuildMeasuredAsync(document, document.SemanticModel.RootScopeId, view, elements,
                graph, inputs.Layout, inputs.Routing, document.VisualModel, state, renderer,
                renderer.CreateTextMeasurementRequest, CancellationToken.None)).Scene);
        var initial = await Build(EditorStateSnapshot.Empty);
        var target = initial.Items.Single(item => item.Origin.ProjectedObjectId == label.Id &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        EditorStateSnapshot Resizing(double x) => new(selection: [node.Source.VisualStateId!],
            activeGesture: new EditorGestureSnapshot("resize:test", Canvas2DNodeLabelGestureMetadata.Kind,
                default, new PointD(x, 0),
                [new(Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId, PropertyValue.FromText(nodeId.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId, PropertyValue.FromText(target.Id.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId, PropertyValue.FromText(label.Id.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.TargetVisualStateId, PropertyValue.FromText(node.Source.VisualStateId!.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.Operation, PropertyValue.FromText(Canvas2DNodeLabelGestureMetadata.ResizeOperation)),
                 new(Canvas2DNodeLabelGestureMetadata.ResizeDirection, PropertyValue.FromText("east"))]));
        var active = await Build(Resizing(10));
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        foreach (var x in new[] { 70d, 13d, -200d })
        {
            var calls = contributor.Calls;
            var full = await Build(Resizing(x));
            Assert.Equal(calls + 1, contributor.Calls);
            var next = Canvas2DSceneBuilder.ReuseEqualNodeLabelResizeContent(active, full);
            Assert.Equal(full, next);
            Assert.Equal(full.ContributorMetadata, next.ContributorMetadata);
            Assert.NotNull(next.BoundedPresentation);
            Assert.Equal(!changesDrawing, ReferenceEquals(active.BoundedPresentation!.Source, next.BoundedPresentation.Source));
            Assert.True((await renderer.RenderAsync(next)).Succeeded);
            Assert.Equal(changesDrawing ? "render" : "renderViewport", execution.Calls[^1]);
            active = next;
        }
        var retired = await Build(EditorStateSnapshot.Empty);
        Assert.Same(retired, Canvas2DSceneBuilder.ReuseEqualNodeLabelResizeContent(active, retired));
        Assert.True(retired.Items.Single(item => item.Id == target.Id).IsVisible);

        // A fresh builder/session provenance cannot borrow the acknowledged base,
        // even when every drawing item is equal.
        var otherBuilder = new Canvas2DSceneBuilder(contributors: [new(new(ResizeContributor.Id, "1"), contributor)]);
        var other = (await otherBuilder.BuildMeasuredAsync(document, document.SemanticModel.RootScopeId, view, elements,
            graph, inputs.Layout, inputs.Routing, document.VisualModel, Resizing(-200), renderer,
            renderer.CreateTextMeasurementRequest, CancellationToken.None)).Scene!;
        Assert.Same(other, Canvas2DSceneBuilder.ReuseEqualNodeLabelResizeContent(active, other));
    }

    private sealed class ResizeContributor(bool changesDrawing) : ICanvas2DSceneContributor
    {
        internal static Canvas2DSceneContributorId Id { get; } = new("test:resize-dependent");
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            var x = context.EditorState.ActiveGesture?.Current.X ?? 0;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
                [new(Canvas2DSceneObjectIdentity.ForExtension(Id, "indicator"), Canvas2DSceneLayer.Content, 1,
                    Canvas2DSceneGeometry.Rectangle(new RectD(changesDrawing ? x : 0, 400, 10, 10)),
                    new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.RegisteredExtension, stableSourceKey: "indicator"))],
                metadata: [new("pointer-x", PropertyValue.FromNumber(x))]));
        }
    }
}
