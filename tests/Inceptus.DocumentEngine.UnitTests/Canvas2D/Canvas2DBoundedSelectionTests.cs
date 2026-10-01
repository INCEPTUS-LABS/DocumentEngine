using System.Text.Json;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DBoundedSelectionTests
{
    [Fact]
    public void LegacyDescriptorsAreConservativeAndBothDimensionsParticipateInIdentity()
    {
        var id = new Canvas2DSceneContributorId("test:selection");
        var legacy = new Canvas2DSceneContributorDescriptor(id, "1", Canvas2DScenePanDependency.Invariant,
            Canvas2DSceneMoveGestureDependency.Invariant);
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown, legacy.HoverDependency);
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown, legacy.VisualSelectionDependency);
        var hover = new Canvas2DSceneContributorDescriptor(id, "1", legacy.PanDependency, legacy.MoveGestureDependency,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Unknown);
        var selection = new Canvas2DSceneContributorDescriptor(id, "1", legacy.PanDependency, legacy.MoveGestureDependency,
            Canvas2DSceneTransientDependency.Unknown, Canvas2DSceneTransientDependency.Invariant);
        Assert.Equal(3, new[] { legacy, hover, selection }.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSceneContributorDescriptor(id, "1",
            legacy.PanDependency, legacy.MoveGestureDependency, (Canvas2DSceneTransientDependency)99,
            Canvas2DSceneTransientDependency.Invariant));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSceneContributorDescriptor(id, "1",
            legacy.PanDependency, legacy.MoveGestureDependency, Canvas2DSceneTransientDependency.Invariant,
            (Canvas2DSceneTransientDependency)99));
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.75d)]
    public void HoverSelectSwitchClearAndDeselectEqualIndependentFullComposition(double zoom)
    {
        var test = new Fixture();
        var viewport = new ViewportSnapshot(zoom, default, new RectD(0, 0, 900, 600));
        var scene = test.Build(new(viewport: viewport));
        var content = scene.RenderContent;
        var a = Body(scene, Fixture.A).Id;
        var b = Body(scene, Fixture.B).Id;
        var states = new EditorStateSnapshot[]
        {
            new(hoveredObjectId: a, viewport: viewport),
            new(selection: [Fixture.A], hoveredObjectId: a, viewport: viewport),
            new(selection: [Fixture.A], hoveredObjectId: b, viewport: viewport),
            new(selection: [Fixture.B], hoveredObjectId: b, viewport: viewport),
            new(selection: [Fixture.B], viewport: viewport),
            new(viewport: viewport),
            new(selection: [Fixture.A], viewport: viewport),
            new(viewport: viewport),
        };
        var hit = new Canvas2DSceneHitTestService();
        foreach (var state in states)
        {
            scene = Assert.IsType<Canvas2DScene>(test.Reuse(scene, state));
            var reference = test.Build(state, new Canvas2DSceneBuilder());
            Assert.Equal(reference, scene);
            Assert.True(content == scene.RenderContent);
            Assert.All(content, item => Assert.Same(item, scene.Items.Single(value => value.Id == item.Id)));
            Assert.DoesNotContain(content, item => item.Origin.StableSourceKey?.StartsWith("selection", StringComparison.Ordinal) == true);
            foreach (var item in reference.Items)
            {
                var point = new PointD(item.Bounds.X + item.Bounds.Width / 2, item.Bounds.Y + item.Bounds.Height / 2);
                Assert.Equal(hit.HitTest(reference, point)?.SceneObjectId, hit.HitTest(scene, point)?.SceneObjectId);
                Assert.Equal(hit.HitTestForHover(reference, point)?.SceneObjectId, hit.HitTestForHover(scene, point)?.SceneObjectId);
            }
            if (state.Selection.IsEmpty && state.HoveredObjectId is null)
            {
                Assert.Empty(scene.BoundedPresentation!.Items);
                Assert.True(content.AsSpan().SequenceEqual(scene.Items.AsSpan()));
            }
        }
    }

    [Theory]
    [InlineData(Canvas2DSceneTransientDependency.Unknown, true)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent, true)]
    [InlineData(Canvas2DSceneTransientDependency.Invariant, true)]
    [InlineData(Canvas2DSceneTransientDependency.Unknown, false)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent, false)]
    [InlineData(Canvas2DSceneTransientDependency.Invariant, false)]
    public async Task ChangedDimensionRequiresExplicitContributorProof(Canvas2DSceneTransientDependency dependency, bool hover)
    {
        var contributor = new StateContributor(dependency);
        var builder = new Canvas2DSceneBuilder(contributors: [new(new(new("test:state"), "1",
            Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
            hover ? dependency : Canvas2DSceneTransientDependency.Invariant,
            hover ? Canvas2DSceneTransientDependency.Invariant : dependency), contributor)]);
        var test = new Fixture(builder);
        var before = test.Build(EditorStateSnapshot.Empty);
        var state = hover ? new EditorStateSnapshot(hoveredObjectId: Body(before, Fixture.A).Id) :
            new EditorStateSnapshot(selection: [Fixture.A]);
        var source = EditingSessionTestHarness.Configuration();
        var pipeline = new EditingSessionPipeline(new EditingSessionConfiguration(source.ProjectionEngine,
            source.LayoutEngine, source.LayoutAlgorithmId, source.RoutingEngine, source.RoutingAlgorithmId, builder));
        var calls = contributor.Calls;
        var result = await pipeline.RebuildSceneForTransientPresentationAsync(before, test.Document,
            EditingSessionTestHarness.CreateArtifacts(test.Inputs, test.Document.SemanticModel.RootScopeId), state,
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        Assert.Equal(dependency == Canvas2DSceneTransientDependency.Invariant, result.ReusedSelectionContent);
        Assert.False(result.ReusedMoveContent);
        Assert.Equal(calls + (result.ReusedSelectionContent ? 0 : 1), contributor.Calls);
        Assert.Equal(test.Build(state), result.Scene);
    }

    [Theory]
    [InlineData("multiple")]
    [InlineData("connector")]
    [InlineData("unknown")]
    [InlineData("feedback")]
    [InlineData("gesture")]
    [InlineData("tool")]
    [InlineData("focus")]
    [InlineData("zoom")]
    [InlineData("surface")]
    [InlineData("label-hover")]
    [InlineData("no-capability")]
    public void UnsupportedStatesFallBackWithoutAlteringPriorScene(string change)
    {
        var test = new Fixture(annotate: change != "no-capability");
        var before = test.Build(EditorStateSnapshot.Empty);
        var state = new EditorStateSnapshot(
            selection: change switch
            {
                "multiple" => [Fixture.A, Fixture.B],
                "connector" => [new("test:visual:ab")],
                "unknown" => [new("missing")],
                _ => [Fixture.A],
            },
            hoveredObjectId: change == "label-hover" ? before.Items.First(item => item.Layer == Canvas2DSceneLayer.Label).Id : null,
            activeToolId: change == "tool" ? "tool" : null,
            focusTargetId: change == "focus" ? "focus" : null,
            viewport: change switch
            {
                "zoom" => new ViewportSnapshot(2, default),
                "surface" => new ViewportSnapshot(1, default, new RectD(0, 0, 10, 10)),
                _ => ViewportSnapshot.Default,
            },
            activeGesture: change == "gesture" ? new EditorGestureSnapshot("test", "test", default, default) : null,
            temporaryFeedback: change == "feedback" ? [new EditorFeedbackSnapshot("test", "test")] : []);
        Assert.Null(test.Reuse(before, state));
        Assert.Equal(test.Build(EditorStateSnapshot.Empty), before);
    }

    [Fact]
    public void BuilderAndImmutableInputProvenanceAndCancellationAreRequired()
    {
        var test = new Fixture();
        var before = test.Build(EditorStateSnapshot.Empty);
        var state = new EditorStateSnapshot(selection: [Fixture.A]);
        Assert.Null(new Fixture().Reuse(before, state));
        Assert.Null(new Canvas2DSceneBuilder().TryReuseForSelection(before, test.Document, test.Document.SemanticModel.RootScopeId,
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
            test.Inputs.Graph, test.Inputs.Layout, test.Inputs.Routing, state, default));
        Assert.Throws<OperationCanceledException>(() => test.Reuse(before, state, new CancellationToken(true)));
    }

    [Fact]
    public async Task SelectionTransportRetainsBaseAndEmptyReplacementRetiresAllAffordances()
    {
        var test = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = test.Build(EditorStateSnapshot.Empty);
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        var token = execution.LastFrame!.ContentVersion;
        Assert.True(renderer.HasAcknowledgedContent(scene));
        scene = Assert.IsType<Canvas2DScene>(test.Reuse(scene, new(selection: [Fixture.A])));
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.Equal(token, execution.LastViewportFrame!.ContentVersion);
        Assert.NotEmpty(execution.LastViewportFrame.PresentationItems!);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(execution.LastViewportFrame).Length, 1, 20000);
        scene = Assert.IsType<Canvas2DScene>(test.Reuse(scene, EditorStateSnapshot.Empty));
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.Empty(execution.LastViewportFrame.PresentationItems!);
        Assert.Equal(token, execution.LastViewportFrame.ContentVersion);
        Assert.Single(execution.Calls, item => item == "render");
        execution.RenderResult = Canvas2DRendererTestExecution.Failure("test:failure", "test");
        Assert.False((await renderer.RenderAsync(scene)).Succeeded);
        Assert.False(renderer.HasAcknowledgedContent(scene));
        execution.RenderResult = Canvas2DRendererTestExecution.Success();
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
    }

    private static Canvas2DSceneItem Body(Canvas2DScene scene, VisualStateId id) => scene.Items.First(item =>
        item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Content);

    [Fact]
    public async Task BoundedPayloadAndContributorWorkDoNotGrowWithUnrelatedSceneContent()
    {
        var sizes = new List<int>();
        foreach (var count in new[] { 100, 250, 500 })
        {
            var contributor = new StableContributor(count);
            var test = new Fixture(new Canvas2DSceneBuilder(contributors: [new(new(new("test:stable"), "1",
                Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
                Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant), contributor)]));
            var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            await using var lifetime = renderer;
            var before = test.Build(EditorStateSnapshot.Empty);
            await renderer.RenderAsync(before);
            var selected = Assert.IsType<Canvas2DScene>(test.Reuse(before, new(selection: [Fixture.A])));
            Assert.Equal(1, contributor.Calls);
            Assert.Equal(test.Build(new(selection: [Fixture.A])), selected);
            await renderer.RenderAsync(selected);
            Assert.Equal(9, selected.BoundedPresentation!.Items.Length);
            Assert.Contains(selected.BoundedPresentation.BeforeContentIndices, index => index < selected.RenderContent.Length);
            Assert.Equal(execution.LastFrame!.ContentVersion, execution.LastViewportFrame!.ContentVersion);
            Assert.Single(execution.Calls, item => item == "render");
            sizes.Add(JsonSerializer.SerializeToUtf8Bytes(execution.LastViewportFrame).Length);
        }
        Assert.InRange(sizes.Max() - sizes.Min(), 0, 30);
    }

    private sealed class Fixture
    {
        internal static readonly VisualStateId A = new("test:visual:a");
        internal static readonly VisualStateId B = new("test:visual:b");
        internal Canvas2DSceneTestData Inputs { get; }
        internal DocumentSnapshot Document { get; }
        private Canvas2DSceneBuilder Builder { get; }
        internal Fixture(Canvas2DSceneBuilder? builder = null, bool annotate = true)
        {
            Builder = builder ?? new();
            var inputs = Canvas2DSceneTestData.CreateWithNodeLabels();
            var graph = inputs.Graph;
            Inputs = annotate ? inputs.WithGraph(new ProjectedGraph(graph.DocumentId, graph.SourceRevision,
                graph.Nodes.Select(node => new ProjectedNode(node.Source, node.PlacementHint, node.SemanticProperties,
                    node.ProjectedProperties.Append(Canvas2DTransientInteractionMetadata.BoundedSelectionEnabled),
                    node.LayoutHints, node.RoutingHints, node.AlgorithmMetadata, node.GeometryInteractionPolicy)),
                graph.Edges, graph.Groups, graph.Ports, graph.Labels)) : inputs;
            Document = EditingSessionTestHarness.CreateDocument(Inputs).CaptureSnapshot();
        }
        internal Canvas2DScene Build(EditorStateSnapshot state, Canvas2DSceneBuilder? builder = null) =>
            Assert.IsType<Canvas2DScene>((builder ?? Builder).Build(Document, Document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                Inputs.Graph, Inputs.Layout, Inputs.Routing, Document.VisualModel, state).Scene);
        internal Canvas2DScene? Reuse(Canvas2DScene scene, EditorStateSnapshot state, CancellationToken token = default) =>
            Builder.TryReuseForSelection(scene, Document, Document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                Inputs.Graph, Inputs.Layout, Inputs.Routing, state, token);
    }

    private sealed class StateContributor(Canvas2DSceneTransientDependency dependency) : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(metadata:
                [new("state", PropertyValue.FromBoolean(dependency != Canvas2DSceneTransientDependency.Invariant &&
                    (context.EditorState.HoveredObjectId is not null || !context.EditorState.Selection.IsEmpty)))]));
        }
    }

    private sealed class StableContributor(int count) : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
                Enumerable.Range(0, count).Select(index => new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(new("test:stable"), $"unrelated:{index:D4}"),
                    Canvas2DSceneLayer.Overlay, index % 2 == 0 ? 2000 : 6000,
                    Canvas2DSceneGeometry.Rectangle(new RectD(400 + index, 80, 10, 20)),
                    new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.RegisteredExtension,
                        stableSourceKey: $"unrelated:{index:D4}")))));
        }
    }
}
