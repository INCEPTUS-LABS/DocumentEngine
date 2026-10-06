using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorLabelMoveTests
{
    [Fact]
    public void ExistingConstructorsRemainConservativeAndDependencyIsPartOfIdentity()
    {
        var legacy = new Canvas2DSceneContributorDescriptor(new("test:connector-label"), "1",
            Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DScenePlacementDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant);
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown, legacy.ConnectorLabelMoveDependency);
        Assert.Equal(legacy, Descriptor(Canvas2DSceneTransientDependency.Unknown));
        Assert.NotEqual(legacy, Descriptor(Canvas2DSceneTransientDependency.Invariant));
        Assert.Equal(Descriptor(Canvas2DSceneTransientDependency.Invariant), Descriptor(Canvas2DSceneTransientDependency.Invariant));
        Assert.Throws<ArgumentOutOfRangeException>(() => Descriptor((Canvas2DSceneTransientDependency)99));
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown,
            new Canvas2DSceneContributorDescriptor(new("test:old"), "1").ConnectorLabelMoveDependency);
    }

    [Theory]
    [InlineData(Canvas2DSceneTransientDependency.Unknown)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent)]
    [InlineData(Canvas2DSceneTransientDependency.Invariant)]
    public async Task OnlyExplicitConnectorLabelInvarianceAllowsExactMeasuredSceneReuse(Canvas2DSceneTransientDependency dependency)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var edge = inputs.Graph.Edges[0];
        var label = new ProjectedLabel(edge.Source, edge.Id, "Connector caption");
        var graph = new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, inputs.Graph.Edges, inputs.Graph.Groups, inputs.Graph.Ports, [label]);
        var contributor = new Probe(dependency);
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
        var target = initial.Items.First(item => item.Layer == Canvas2DSceneLayer.Label && item.Origin.ProjectedObjectId == label.Id);
        var connector = initial.Items.Single(item => item.Origin.ProjectedObjectId == edge.Id &&
            item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount));
        EditorStateSnapshot Moving(PointD point, string id = "label:test", double zoom = 1) => new(
            selection: [edge.Source.VisualStateId!], hoveredObjectId: target.Id, viewport: new ViewportSnapshot(zoom, default),
            activeGesture: new EditorGestureSnapshot(id, Canvas2DLabelGestureMetadata.Kind, default, point,
                [new(Canvas2DLabelGestureMetadata.TargetConnectorSceneObjectId, PropertyValue.FromText(connector.Id.Value)),
                 new(Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId, PropertyValue.FromText(label.Id.Value)),
                 new(Canvas2DLabelGestureMetadata.TargetVisualStateId, PropertyValue.FromText(edge.Source.VisualStateId!.Value))]));
        var active = await Build(Moving(default));
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        var version = execution.LastFrame!.ContentVersion;
        foreach (var point in new[] { new PointD(70, 8), new PointD(-13, 32), new PointD(0, 0) })
        {
            var state = Moving(point);
            var calls = contributor.Calls;
            var next = Reuse(active, state);
            Assert.Equal(calls, contributor.Calls);
            if (dependency != Canvas2DSceneTransientDependency.Invariant)
            {
                Assert.Null(next);
                Assert.Null(active.BoundedPresentation?.ConnectorLabelMove);
                active = await Build(state);
                Assert.Equal(calls + 1, contributor.Calls);
                continue;
            }
            Assert.NotNull(next);
            Assert.Equal(await Build(state), next);
            Assert.Same(active.Items.Single(item => item.Id == connector.Id), next.Items.Single(item => item.Id == connector.Id));
            Assert.DoesNotContain(next.Items, item => item.Layer == Canvas2DSceneLayer.Label &&
                item.Origin.ProjectedObjectId == label.Id && item.IsVisible);
            Assert.Contains(next.Items, item => item.IsVisible && item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.StableSourceKey?.StartsWith("connector-label-preview:", StringComparison.Ordinal) == true);
            var hover = next.Items.Single(item => item.Origin.StableSourceKey == $"hover:{target.Id.Value}");
            var originalHover = (await Build(Moving(default))).Items.Single(item => item.Id == hover.Id);
            Assert.Equal(originalHover.Bounds.Translate(new VectorD(point.X, point.Y)), hover.Bounds);
            Assert.True((await renderer.RenderAsync(next)).Succeeded);
            Assert.Equal("renderViewport", execution.Calls[^1]);
            Assert.Equal(version, execution.LastViewportFrame!.ContentVersion);
            active = next;
        }
        Assert.Null(Reuse(active, EditorStateSnapshot.Empty));
        Assert.Null(Reuse(active, Moving(new(3, 4), id: "different")));
        Assert.Null(Reuse(active, Moving(new(3, 4), zoom: 1.75)));
        Assert.Null(builder.TryReuseForConnectorLabelMove(active, document, new DocumentScopeId("other"),
            view, elements, graph, inputs.Layout, inputs.Routing, Moving(new(3, 4)), CancellationToken.None));
        Assert.Null(builder.TryReuseForConnectorLabelMove(active,
            EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot(), document.SemanticModel.RootScopeId,
            view, elements, graph, inputs.Layout, inputs.Routing, Moving(new(3, 4)), CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => builder.TryReuseForConnectorLabelMove(active, document,
            document.SemanticModel.RootScopeId, view, elements, graph, inputs.Layout, inputs.Routing,
            Moving(new(3, 4)), new CancellationToken(true)));

        Canvas2DScene? Reuse(Canvas2DScene prior, EditorStateSnapshot state) => builder.TryReuseForConnectorLabelMove(
            prior, document, document.SemanticModel.RootScopeId, view, elements, graph, inputs.Layout, inputs.Routing,
            state, CancellationToken.None);
    }

    private static Canvas2DSceneContributorDescriptor Descriptor(Canvas2DSceneTransientDependency dependency) =>
        new(new("test:connector-label"), "1", Canvas2DScenePanDependency.Invariant,
            Canvas2DSceneMoveGestureDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DScenePlacementDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, dependency);

    private sealed class Probe(Canvas2DSceneTransientDependency dependency) : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution([], metadata:
                dependency == Canvas2DSceneTransientDependency.Invariant ? null :
                [new("position", PropertyValue.FromNumber(context.EditorState.ActiveGesture?.Current.X ?? 0))]));
        }
    }
}
