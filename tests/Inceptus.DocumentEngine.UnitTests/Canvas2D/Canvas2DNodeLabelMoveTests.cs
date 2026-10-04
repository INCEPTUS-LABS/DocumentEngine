using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DNodeLabelMoveTests
{
    [Fact]
    public void LabelDependencyIsExplicitConservativeAndPartOfDescriptorIdentity()
    {
        var old = new Canvas2DSceneContributorDescriptor(new("test:label-dependency"), "1",
            Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DScenePlacementDependency.Invariant, Canvas2DSceneTransientDependency.Invariant);
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown, old.NodeLabelMoveDependency);
        var invariant = Descriptor(Canvas2DSceneTransientDependency.Invariant);
        Assert.NotEqual(old, invariant);
        Assert.Equal(invariant, Descriptor(Canvas2DSceneTransientDependency.Invariant));
        Assert.Throws<ArgumentOutOfRangeException>(() => Descriptor((Canvas2DSceneTransientDependency)99));
    }

    [Theory]
    [InlineData(Canvas2DSceneTransientDependency.Unknown)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent)]
    [InlineData(Canvas2DSceneTransientDependency.Invariant)]
    public async Task SteadyTranslationRequiresItsOwnCapabilityAndMatchesIndependentFullComposition(
        Canvas2DSceneTransientDependency dependency)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var node = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(node.Source, node.Id, "Gateway decision label",
            nodePlacement: new NodeLabelPlacement(NodeLabelPlacementKind.OutsideBelow, 8, 120),
            nodeInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var graph = new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, inputs.Graph.Edges, inputs.Graph.Groups, inputs.Graph.Ports, [label]);
        var contributor = new Probe();
        var builder = new Canvas2DSceneBuilder(contributors: [new(Descriptor(dependency), contributor)]);
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
        EditorStateSnapshot Moving(PointD point) => new(selection: [node.Source.VisualStateId!],
            activeGesture: new EditorGestureSnapshot("label:test", Canvas2DNodeLabelGestureMetadata.Kind,
                default, point,
                [new(Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId, PropertyValue.FromText(nodeId.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId, PropertyValue.FromText(target.Id.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId, PropertyValue.FromText(label.Id.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.TargetVisualStateId, PropertyValue.FromText(node.Source.VisualStateId!.Value)),
                 new(Canvas2DNodeLabelGestureMetadata.Operation, PropertyValue.FromText(Canvas2DNodeLabelGestureMetadata.MoveOperation))]));
        var active = await Build(Moving(new PointD(10, 15)));
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        var version = execution.LastFrame!.ContentVersion;
        foreach (var point in new[] { new PointD(70, 8), new PointD(13, 32), new PointD(0, 0) })
        {
            var state = Moving(point);
            var calls = contributor.Calls;
            var next = builder.TryReuseForNodeLabelMove(active, document, document.SemanticModel.RootScopeId,
                view, elements, graph, inputs.Layout, inputs.Routing, state, CancellationToken.None);
            Assert.Equal(calls, contributor.Calls);
            if (dependency != Canvas2DSceneTransientDependency.Invariant)
            {
                Assert.Null(next);
                Assert.Null(active.BoundedPresentation?.NodeLabelMove);
                continue;
            }
            Assert.NotNull(next);
            Assert.Equal(await Build(state), next);
            Assert.Equal(initial.Items.Single(item => item.Id == nodeId), next.Items.Single(item => item.Id == nodeId));
            Assert.DoesNotContain(next.Items, item => item.Layer == Canvas2DSceneLayer.Label &&
                item.Origin.ProjectedObjectId == label.Id && item.IsVisible);
            Assert.True((await renderer.RenderAsync(next)).Succeeded);
            Assert.Equal("renderViewport", execution.Calls[^1]);
            Assert.Equal(version, execution.LastViewportFrame!.ContentVersion);
            Assert.Equal(next.BoundedPresentation!.Items.Length, execution.LastViewportFrame.PresentationItems!.Length);
            active = next;
        }
        Assert.Null(builder.TryReuseForNodeLabelMove(active, document, document.SemanticModel.RootScopeId,
            view, elements, graph, inputs.Layout, inputs.Routing, EditorStateSnapshot.Empty, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => builder.TryReuseForNodeLabelMove(active, document,
            document.SemanticModel.RootScopeId, view, elements, graph, inputs.Layout, inputs.Routing,
            Moving(new PointD(20, 20)), new CancellationToken(true)));
    }

    private static Canvas2DSceneContributorDescriptor Descriptor(Canvas2DSceneTransientDependency dependency) =>
        new(new("test:label-dependency"), "1", Canvas2DScenePanDependency.Invariant,
            Canvas2DSceneMoveGestureDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DScenePlacementDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, dependency);

    private sealed class Probe : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution([]));
        }
    }
}
