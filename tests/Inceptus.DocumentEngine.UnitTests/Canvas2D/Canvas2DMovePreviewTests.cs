using System.Text.Json;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DMovePreviewTests
{
    [Fact]
    public void LegacyContributorDeclarationIsConservativeAndMoveDependencyIsPartOfIdentity()
    {
        var id = new Canvas2DSceneContributorId("test:move");
        var legacy = new Canvas2DSceneContributorDescriptor(id, "1", Canvas2DScenePanDependency.Invariant);
        Assert.Equal(Canvas2DSceneMoveGestureDependency.Unknown, legacy.MoveGestureDependency);
        Assert.NotEqual(legacy, new(id, "1", Canvas2DScenePanDependency.Invariant,
            Canvas2DSceneMoveGestureDependency.Invariant));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSceneContributorDescriptor(id, "1",
            Canvas2DScenePanDependency.Invariant, (Canvas2DSceneMoveGestureDependency)99));
    }

    [Theory]
    [InlineData(1d, false)]
    [InlineData(1.75d, false)]
    [InlineData(0.8d, true)]
    public void ActivationRepeatedReversalAndRetirementEqualCompleteSceneIncludingHits(double zoom, bool selected)
    {
        var test = new Fixture();
        var viewport = new ViewportSnapshot(zoom, default, new RectD(0, 0, 900, 600));
        var resting = new EditorStateSnapshot(selection: selected ? [Fixture.Target] : [], viewport: viewport);
        var scene = test.Build(resting);
        var content = scene.RenderContent;
        Assert.NotNull(scene.BoundedPresentation);
        Canvas2DScene? preceding = null;
        foreach (var delta in new[] { new VectorD(12, 8), new VectorD(36, 19), new VectorD(2, 1) })
        {
            var state = Moving(viewport, delta);
            var next = Assert.IsType<Canvas2DScene>(test.Reuse(scene, state));
            Assert.Equal(test.Build(state), next);
            Assert.True(content == next.RenderContent);
            Assert.All(content, item => Assert.Same(item, next.Items.Single(value => value.Id == item.Id)));
            Assert.InRange(next.BoundedPresentation!.Items.Length, 1, Canvas2DBoundedPresentation.MaximumItems);
            var hit = new Canvas2DSceneHitTestService();
            var reference = test.Build(state);
            foreach (var item in next.Items)
            {
                var expected = hit.HitTest(reference, item.Bounds.TopLeft);
                var actual = hit.HitTest(next, item.Bounds.TopLeft);
                Assert.Equal(expected?.SceneObjectId, actual?.SceneObjectId);
                Assert.Equal(expected?.Origin, actual?.Origin);
                Assert.Equal(expected?.DocumentPoint, actual?.DocumentPoint);
                Assert.Equal(expected?.PresentedScopePoint, actual?.PresentedScopePoint);
                Assert.Equal(expected?.SpatialRegion, actual?.SpatialRegion);
            }
            if (preceding is not null)
            {
                Assert.All(next.BoundedPresentation.Items.Where(item => item.Origin.StableSourceKey!.StartsWith("resize-", StringComparison.Ordinal)),
                    item => Assert.Same(item, preceding.Items.Single(old => old.Id == item.Id)));
            }
            preceding = next;
            scene = next;
        }
        var cleared = new EditorStateSnapshot(selection: [Fixture.Target], viewport: viewport);
        var retired = Assert.IsType<Canvas2DScene>(test.Reuse(scene, cleared));
        Assert.Equal(test.Build(cleared), retired);
        Assert.DoesNotContain(retired.Items, item => item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
        Assert.NotNull(retired.PanReuseSource);
    }

    [Theory]
    [InlineData("zoom")]
    [InlineData("surface")]
    [InlineData("tool")]
    [InlineData("focus")]
    [InlineData("hover")]
    [InlineData("selection")]
    [InlineData("feedback")]
    [InlineData("gesture")]
    [InlineData("gesture-id")]
    [InlineData("origin")]
    [InlineData("targets")]
    public void UnsupportedEditorChangesRejectReuse(string change)
    {
        var test = new Fixture();
        var state = Moving(ViewportSnapshot.Default, new VectorD(10, 10));
        var scene = test.Build(state);
        var viewport = change switch
        {
            "zoom" => new ViewportSnapshot(2, default),
            "surface" => new ViewportSnapshot(1, default, new RectD(0, 0, 17, 19)),
            _ => state.Viewport,
        };
        var gesture = new EditorGestureSnapshot(change == "gesture-id" ? "other" : "move:test",
            change == "gesture" ? "unsupported" : Canvas2DMoveGestureMetadata.Kind,
            change == "origin" ? new PointD(1, 2) : default, new PointD(20, 20),
            [new(Canvas2DMoveGestureMetadata.TargetVisualStateId, PropertyValue.FromText(
                change == "targets" ? "test:visual:b" : Fixture.Target.Value))]);
        var updated = new EditorStateSnapshot(
            selection: change == "selection" ? [Fixture.Target, new VisualStateId("test:visual:b")] : [Fixture.Target],
            hoveredObjectId: change == "hover" ? scene.Items[0].Id : null,
            activeToolId: change == "tool" ? "changed" : null,
            focusTargetId: change == "focus" ? "changed" : null,
            viewport: viewport, activeGesture: gesture,
            temporaryFeedback: change == "feedback" ? [new EditorFeedbackSnapshot("feedback", "feedback")] : []);
        Assert.Null(test.Reuse(scene, updated));
    }

    [Theory]
    [InlineData(Canvas2DSceneMoveGestureDependency.Unknown)]
    [InlineData(Canvas2DSceneMoveGestureDependency.Dependent)]
    [InlineData(Canvas2DSceneMoveGestureDependency.Invariant)]
    public async Task GestureDependentAndUnknownContributorsUseCompletePipeline(Canvas2DSceneMoveGestureDependency dependency)
    {
        var contributor = new GestureContributor(dependency);
        var test = new Fixture(new Canvas2DSceneBuilder(contributors:
            [new(new(contributor.Id, "1", Canvas2DScenePanDependency.Invariant, dependency), contributor)]));
        var source = EditingSessionTestHarness.Configuration();
        var pipeline = new EditingSessionPipeline(new EditingSessionConfiguration(source.ProjectionEngine,
            source.LayoutEngine, source.LayoutAlgorithmId, source.RoutingEngine, source.RoutingAlgorithmId, test.Builder));
        var artifacts = EditingSessionTestHarness.CreateArtifacts(test.Inputs, test.Document.SemanticModel.RootScopeId);
        var before = test.Build(EditorStateSnapshot.Empty);
        var state = Moving(ViewportSnapshot.Default, new VectorD(10, 10));
        var calls = contributor.Calls;
        var after = await pipeline.RebuildSceneForTransientPresentationAsync(before, test.Document, artifacts, state,
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        Assert.Equal(dependency == Canvas2DSceneMoveGestureDependency.Invariant, after.ReusedMoveContent);
        Assert.Equal(calls + (after.ReusedMoveContent ? 0 : 1), contributor.Calls);
        Assert.Equal(test.Build(state), after.Scene);
    }

    [Fact]
    public async Task RendererRetainsBaseTokenUsesBoundedPrimitivesAndRecoversAfterFailureAndResize()
    {
        var test = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var before = test.Build(EditorStateSnapshot.Empty);
        Assert.True((await renderer.RenderAsync(before)).Succeeded);
        var version = execution.LastFrame!.ContentVersion;
        var active = Assert.IsType<Canvas2DScene>(test.Reuse(before, Moving(ViewportSnapshot.Default, new VectorD(10, 10))));
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        Assert.Equal("renderViewport", execution.Calls[^1]);
        Assert.Equal(version, execution.LastViewportFrame!.ContentVersion);
        Assert.Equal(active.BoundedPresentation!.Items.Length, execution.LastViewportFrame.PresentationItems!.Length);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(execution.LastViewportFrame).Length, 1, 20000);
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        Assert.Null(execution.LastViewportFrame.PresentationItems);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(execution.LastViewportFrame).Length, 1, 500);
        execution.RenderResult = Canvas2DRendererTestExecution.Failure("test:failure", "test");
        Assert.False((await renderer.RenderAsync(active)).Succeeded);
        execution.RenderResult = Canvas2DRendererTestExecution.Success();
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.True(execution.LastFrame!.ContentVersion > version);
        Assert.True((await renderer.ResizeAsync(new Canvas2DSurfaceSize(901, 601, 2))).Succeeded);
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
    }

    [Fact]
    public void CancellationIsObservedBeforeAnyReuse()
    {
        var test = new Fixture();
        var before = test.Build(EditorStateSnapshot.Empty);
        Assert.Throws<OperationCanceledException>(() => test.Reuse(before,
            Moving(ViewportSnapshot.Default, new VectorD(10, 10)), new CancellationToken(true)));
    }

    [Fact]
    public async Task BoundedPayloadIsIndependentOfUnrelatedSceneSizeAndPreservesContributorOrdering()
    {
        var sizes = new List<int>();
        foreach (var count in new[] { 100, 250, 500 })
        {
            var contributor = new StableItemsContributor(count);
            var test = new Fixture(new Canvas2DSceneBuilder(contributors:
                [new(new(new("test:stable-items"), "1", Canvas2DScenePanDependency.Invariant,
                    Canvas2DSceneMoveGestureDependency.Invariant), contributor)]));
            var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            await using var lifetime = renderer;
            var before = test.Build(EditorStateSnapshot.Empty);
            Assert.True((await renderer.RenderAsync(before)).Succeeded);
            var moving = Moving(ViewportSnapshot.Default, new VectorD(10, 10));
            var active = Assert.IsType<Canvas2DScene>(test.Reuse(before, moving));
            Assert.Equal(test.Build(moving), active);
            Assert.True((await renderer.RenderAsync(active)).Succeeded);
            Assert.Single(execution.Calls, call => call == "render");
            Assert.Single(execution.Calls, call => call == "renderViewport");
            sizes.Add(JsonSerializer.SerializeToUtf8Bytes(execution.LastViewportFrame).Length);
            Assert.Equal(before.RenderContent.Length, execution.LastFrame!.Items.Length);
            Assert.Contains(active.BoundedPresentation!.BeforeContentIndices, index => index < active.RenderContent.Length);
        }
        Assert.InRange(sizes.Max() - sizes.Min(), 0, 30); // Only insertion-index digit lengths can differ.
    }

    [Fact]
    public async Task DelayedBoundedAcknowledgementCannotAuthorizeNewContentOrSurviveDisposal()
    {
        var test = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var before = test.Build(EditorStateSnapshot.Empty);
        await renderer.RenderAsync(before);
        var active = Assert.IsType<Canvas2DScene>(test.Reuse(before, Moving(ViewportSnapshot.Default, new VectorD(10, 10))));
        execution.RenderEnteredSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = renderer.RenderAsync(active).AsTask();
        await execution.RenderEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var replacement = new Fixture().Build(EditorStateSnapshot.Empty);
        var newer = renderer.RenderAsync(replacement).AsTask();
        Assert.False(newer.IsCompleted);
        execution.RenderReleaseSignal.TrySetResult();
        Assert.True((await pending).Succeeded);
        Assert.True((await newer).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.Empty(execution.LastFrame!.PresentationItems);
        await renderer.DisposeAsync();
        Assert.False((await renderer.RenderAsync(active)).Succeeded);
    }

    [Fact]
    public void MoveReuseAndTransportRemainPrivateImplementationDetails()
    {
        Assert.False(typeof(Canvas2DBoundedPresentation).IsPublic);
        Assert.False(typeof(Canvas2DBoundedPresentationSource).IsPublic);
        Assert.False(typeof(Inceptus.DocumentEngine.Canvas2D.Rendering.Interop.Canvas2DBoundedRenderItem).IsPublic);
        Assert.Equal(["Unknown", "Invariant", "Dependent"], Enum.GetNames<Canvas2DSceneMoveGestureDependency>());
    }

    [Fact]
    public void FixedNodeHoverRetainsExactOverlayAndConnectorHoverFallsBack()
    {
        var test = new Fixture();
        var initial = test.Build(EditorStateSnapshot.Empty);
        foreach (var item in initial.Items.Where(item => item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Connector))
        {
            var resting = new EditorStateSnapshot(hoveredObjectId: item.Id);
            var moving = Moving(ViewportSnapshot.Default, new VectorD(10, 10));
            var active = new EditorStateSnapshot(selection: moving.Selection, hoveredObjectId: item.Id,
                viewport: moving.Viewport, activeGesture: moving.ActiveGesture);
            var next = test.Reuse(test.Build(resting), active);
            if (item.Layer == Canvas2DSceneLayer.Connector) { Assert.Null(next); }
            else { Assert.Equal(test.Build(active), Assert.IsType<Canvas2DScene>(next)); }
        }
    }

    [Theory]
    [InlineData("document")]
    [InlineData("graph")]
    [InlineData("layout")]
    [InlineData("routing")]
    [InlineData("scope")]
    [InlineData("profile")]
    [InlineData("collapse")]
    [InlineData("builder")]
    public void ChangedProvenanceCannotAuthorizeStableContent(string change)
    {
        var test = new Fixture();
        var scene = test.Build(EditorStateSnapshot.Empty);
        var other = new Fixture();
        var builder = change == "builder" ? other.Builder : test.Builder;
        Assert.Null(builder.TryReuseForMovePreview(scene,
            change == "document" ? other.Document : test.Document,
            change == "scope" ? new DocumentScopeId("test:other") : test.Document.SemanticModel.RootScopeId,
            change == "profile" ? new ModelProfileViewStateSnapshot([]) : ModelProfileViewStateSnapshot.Empty,
            change == "collapse" ? new ModelProfileElementViewStateSnapshot([]) : ModelProfileElementViewStateSnapshot.Empty,
            change == "graph" ? other.Inputs.Graph : test.Inputs.Graph,
            change == "layout" ? other.Inputs.Layout : test.Inputs.Layout,
            change == "routing" ? other.Inputs.Routing : test.Inputs.Routing,
            Moving(ViewportSnapshot.Default, new VectorD(10, 10)), default));
    }

    private static EditorStateSnapshot Moving(ViewportSnapshot viewport, VectorD delta) => new(
        selection: [Fixture.Target], viewport: viewport,
        activeGesture: new EditorGestureSnapshot("move:test", Canvas2DMoveGestureMetadata.Kind,
            default, new PointD(delta.X, delta.Y),
            [new(Canvas2DMoveGestureMetadata.TargetVisualStateId, PropertyValue.FromText(Fixture.Target.Value))]));

    private sealed class Fixture
    {
        internal static readonly VisualStateId Target = new("test:visual:a");
        internal Canvas2DSceneTestData Inputs { get; } = Canvas2DSceneTestData.Create();
        internal DocumentSnapshot Document { get; }
        internal Canvas2DSceneBuilder Builder { get; }
        internal Fixture(Canvas2DSceneBuilder? builder = null)
        {
            Builder = builder ?? new();
            Document = EditingSessionTestHarness.CreateDocument(Inputs).CaptureSnapshot();
        }
        internal Canvas2DScene Build(EditorStateSnapshot state) => Assert.IsType<Canvas2DScene>(Builder.Build(
            Document, Document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty, Inputs.Graph, Inputs.Layout, Inputs.Routing,
            Document.VisualModel, state).Scene);
        internal Canvas2DScene? Reuse(Canvas2DScene before, EditorStateSnapshot state, CancellationToken token = default) =>
            Builder.TryReuseForMovePreview(before, Document, Document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                Inputs.Graph, Inputs.Layout, Inputs.Routing, state, token);
    }

    private sealed class GestureContributor(Canvas2DSceneMoveGestureDependency dependency) : ICanvas2DSceneContributor
    {
        internal Canvas2DSceneContributorId Id { get; } = new("test:gesture-dependent");
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(metadata:
                [new("active", PropertyValue.FromBoolean(dependency != Canvas2DSceneMoveGestureDependency.Invariant &&
                    context.EditorState.ActiveGesture is not null))]));
        }
    }

    private sealed class StableItemsContributor(int count) : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
                Enumerable.Range(0, count).Select(index => new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(new("test:stable-items"), $"test:unrelated:{index:D4}"), Canvas2DSceneLayer.Overlay,
                    index % 2 == 0 ? 2000 : 6000, Canvas2DSceneGeometry.Rectangle(new RectD(400 + index, 80, 10, 20)),
                    new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.RegisteredExtension,
                        stableSourceKey: $"test:unrelated:{index:D4}")))));
    }
}
