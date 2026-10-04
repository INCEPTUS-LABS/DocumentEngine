using System.Text.Json;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DPlacementPreviewTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.75d)]
    public async Task ActivationMovementValidityAndRetirementEqualFreshFullComposition(double zoom)
    {
        var fixture = new Fixture();
        var viewport = new ViewportSnapshot(zoom, new VectorD(-15, 20));
        var initial = State(null, viewport);
        var scene = fixture.Build(initial);
        var content = scene.RenderContent;
        var selected = scene.BoundedPresentation!.Items.ToArray();
        var stages = new EditorStateSnapshot[]
        {
            State(Feedback(500, 350), viewport),
            State(Feedback(600, 375, allowed: false), viewport),
            State(Feedback(600, 375), viewport),
            State(Feedback(200, 650, outsideLabel: true), viewport),
            State(Feedback(200, 850, canonicalY: 20, outsideLabel: true), viewport),
            State(null, viewport),
        };
        var hit = new Canvas2DSceneHitTestService();
        foreach (var state in stages)
        {
            var invariantCalls = fixture.Stable.Calls;
            scene = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(scene, state));
            Assert.Equal(invariantCalls, fixture.Stable.Calls);
            var oracle = fixture.Build(state, fixture.FreshBuilder());
            Assert.Equal(oracle, scene);
            Assert.True(content == scene.RenderContent);
            Assert.All(content, item => Assert.Same(item, scene.Items.Single(current => current.Id == item.Id)));
            Assert.All(selected, item => Assert.Same(item, scene.Items.Single(current => current.Id == item.Id)));
            Assert.Equal(scene.Items.Length, scene.Items.Select(static item => item.Id).Distinct().Count());
            Assert.DoesNotContain(content, IsPlacementItem);
            Assert.All(scene.Items.Where(IsPlacementItem), item =>
            {
                Assert.Null(item.Origin.SemanticElementId);
                Assert.Null(item.Origin.VisualStateId);
                Assert.Null(item.Origin.ProjectedObjectId);
                Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
            });
            if (state.TemporaryFeedback.FirstOrDefault() is { Bounds: { } bounds })
            {
                var center = new PointD(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                Assert.Null(hit.HitTest(scene, center));
                Assert.Null(hit.HitTestForHover(scene, center));
            }
        }
        Assert.DoesNotContain(scene.Items, IsPlacementItem);
        Assert.Empty(scene.BoundedPresentation!.PlacementItems);
    }

    [Theory]
    [InlineData(Canvas2DScenePlacementDependency.Unknown)]
    [InlineData(Canvas2DScenePlacementDependency.Dependent)]
    public async Task MissingPlacementProofForcesIndependentCompleteComposition(Canvas2DScenePlacementDependency dependency)
    {
        var fixture = new Fixture(dependency: dependency);
        var before = fixture.Build(State(null));
        var state = State(Feedback(500, 350));
        Assert.Null(await fixture.ReuseAsync(before, state));
        var source = EditingSessionTestHarness.Configuration();
        var configuration = new EditingSessionConfiguration(source.ProjectionEngine, source.LayoutEngine,
            source.LayoutAlgorithmId, source.RoutingEngine, source.RoutingAlgorithmId, fixture.Builder);
        var pipeline = new EditingSessionPipeline(configuration);
        var artifacts = new EditingSessionPipelineArtifacts(fixture.Document.SemanticModel.RootScopeId,
            fixture.Inputs.Graph, fixture.Inputs.Layout, fixture.Inputs.Routing);
        var result = await pipeline.RebuildSceneForTransientPresentationAsync(before, fixture.Document,
            artifacts, state, ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        Assert.False(result.ReusedPlacementContent);
        Assert.Equal(fixture.Build(state, fixture.FreshBuilder()), result.Scene);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("content")]
    [InlineData("hit")]
    [InlineData("appearance")]
    [InlineData("owner")]
    [InlineData("oversized")]
    [InlineData("overrides")]
    [InlineData("spatial")]
    public async Task FalseBoundedDeclarationCannotInstallUnboundedOrInteractiveOutput(string invalid)
    {
        var fixture = new Fixture(invalid: invalid);
        var before = fixture.Build(State(null));
        var state = State(Feedback(500, 350));
        Assert.Null(await fixture.ReuseAsync(before, state));
        var full = fixture.Builder.Build(fixture.Document, fixture.Document.SemanticModel.RootScopeId,
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
            fixture.Inputs.Graph, fixture.Inputs.Layout, fixture.Inputs.Routing, fixture.Document.VisualModel, state);
        Assert.False(full.Succeeded);
        Assert.Contains(full.Diagnostics, diagnostic => diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidContribution);
    }

    [Fact]
    public async Task BoundedContributorCannotRetainItsLastCandidateAfterRetirement()
    {
        var fixture = new Fixture(invalid: "phantom");
        var before = fixture.Build(State(null));
        var active = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(before, State(Feedback(500, 350))));
        Assert.Null(await fixture.ReuseAsync(active, State(null)));
        Assert.Null(await fixture.ReuseAsync(active, new(selection: [new VisualStateId("test:visual:b")])));
        var full = fixture.Builder.Build(fixture.Document, fixture.Document.SemanticModel.RootScopeId,
            ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
            fixture.Inputs.Graph, fixture.Inputs.Layout, fixture.Inputs.Routing, fixture.Document.VisualModel, State(null));
        Assert.False(full.Succeeded);
        Assert.Contains(full.Diagnostics, diagnostic => diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidContribution);
    }

    [Theory]
    [InlineData(Canvas2DSceneTransientDependency.Invariant)]
    [InlineData(Canvas2DSceneTransientDependency.Unknown)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent)]
    public async Task SelectionAfterCommitRetiresPreviewWithBothContributorProofs(
        Canvas2DSceneTransientDependency selectionDependency)
    {
        var fixture = new Fixture(annotateSelection: true, selectionDependency: selectionDependency);
        var before = fixture.Build(State(Feedback(500, 350)));
        var selected = new EditorStateSnapshot(selection: [new VisualStateId("test:visual:b")]);
        var configuration = EditingSessionTestHarness.Configuration();
        var pipeline = new EditingSessionPipeline(new EditingSessionConfiguration(configuration.ProjectionEngine,
            configuration.LayoutEngine, configuration.LayoutAlgorithmId, configuration.RoutingEngine,
            configuration.RoutingAlgorithmId, fixture.Builder));
        var calls = fixture.Stable.Calls;
        var result = await pipeline.RebuildSceneForTransientPresentationAsync(before, fixture.Document,
            EditingSessionTestHarness.CreateArtifacts(fixture.Inputs, fixture.Document.SemanticModel.RootScopeId),
            selected, ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        var eligible = selectionDependency == Canvas2DSceneTransientDependency.Invariant;
        Assert.Equal(eligible, result.ReusedPlacementContent);
        Assert.Equal(eligible, result.ReusedSelectionContent);
        Assert.Equal(calls + (eligible ? 0 : 1), fixture.Stable.Calls);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        Assert.Equal(fixture.Build(selected, fixture.FreshBuilder()), scene);
        Assert.DoesNotContain(scene.Items, IsPlacementItem);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        await renderer.RenderAsync(before);
        await renderer.RenderAsync(scene);
        Assert.Equal(eligible ? 1 : 2, execution.Calls.Count(static call => call == "render"));
    }

    [Theory]
    [InlineData("hover")]
    [InlineData("selection")]
    [InlineData("pan")]
    public async Task UnrelatedBoundedPathsWithActivePreviewUseCompleteComposition(string change)
    {
        var fixture = new Fixture(annotateSelection: true);
        var feedback = Feedback(500, 350);
        var before = fixture.Build(State(feedback));
        var hover = before.Items.First(item => item.Origin.VisualStateId?.Value == "test:visual:b" &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item)).Id;
        var state = new EditorStateSnapshot(
            selection: [new VisualStateId(change == "selection" ? "test:visual:b" : "test:visual:a")],
            hoveredObjectId: change == "hover" ? hover : null,
            viewport: change == "pan" ? new ViewportSnapshot(1, new VectorD(20, 40)) : null,
            temporaryFeedback: [feedback]);
        var configuration = EditingSessionTestHarness.Configuration();
        var pipeline = new EditingSessionPipeline(new EditingSessionConfiguration(configuration.ProjectionEngine,
            configuration.LayoutEngine, configuration.LayoutAlgorithmId, configuration.RoutingEngine,
            configuration.RoutingAlgorithmId, fixture.Builder));
        var artifacts = EditingSessionTestHarness.CreateArtifacts(fixture.Inputs, fixture.Document.SemanticModel.RootScopeId);
        var result = change == "pan"
            ? await pipeline.RebuildSceneForPanAsync(before, fixture.Document, artifacts, state,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default)
            : await pipeline.RebuildSceneForTransientPresentationAsync(before, fixture.Document, artifacts, state,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        Assert.False(result.ReusedPlacementContent || result.ReusedSelectionContent || result.ReusedMoveContent || result.ReusedPanContent);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        Assert.Equal(fixture.Build(state, fixture.FreshBuilder()), scene);
        Assert.Equal(before.Items.Where(IsPlacementItem), scene.Items.Where(IsPlacementItem));
    }

    [Fact]
    public async Task PreviewOnlyReportsPlacementProofWithoutSelectionProof()
    {
        var fixture = new Fixture(annotateSelection: true);
        var configuration = EditingSessionTestHarness.Configuration();
        var pipeline = new EditingSessionPipeline(new EditingSessionConfiguration(configuration.ProjectionEngine,
            configuration.LayoutEngine, configuration.LayoutAlgorithmId, configuration.RoutingEngine,
            configuration.RoutingAlgorithmId, fixture.Builder));
        var result = await pipeline.RebuildSceneForTransientPresentationAsync(fixture.Build(State(null)), fixture.Document,
            EditingSessionTestHarness.CreateArtifacts(fixture.Inputs, fixture.Document.SemanticModel.RootScopeId),
            State(Feedback(500, 350)), ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty, default);
        Assert.True(result.ReusedPlacementContent);
        Assert.False(result.ReusedSelectionContent);
        Assert.False(result.ReusedMoveContent);
    }

    [Fact]
    public async Task BoundedContributorReceivesOnlyTypedFeedbackWithoutGlobalPresentationItems()
    {
        var fixture = new Fixture(500);
        var before = fixture.Build(State(null));
        AssertRestricted(fixture.Preview.LastContext!);
        var unrelated = new EditorFeedbackSnapshot("other", "other", new RectD(30, 40, 10, 10));
        var initial = new EditorStateSnapshot(selection: [new("test:visual:a")], temporaryFeedback: [unrelated],
            activeToolId: "unchanged-tool", viewport: new(1.5, new VectorD(10, 20)));
        before = fixture.Build(initial);
        var state = new EditorStateSnapshot(selection: initial.Selection, activeToolId: initial.ActiveToolId,
            viewport: initial.Viewport, temporaryFeedback: [unrelated, Feedback(500, 350)]);
        Assert.NotNull(await fixture.ReuseAsync(before, state));
        AssertRestricted(fixture.Preview.LastContext!);
        Assert.Single(fixture.Preview.LastContext!.EditorState.TemporaryFeedback);

        static void AssertRestricted(Canvas2DSceneContributionContext context)
        {
            Assert.Empty(context.EditorState.Selection);
            Assert.Null(context.EditorState.ActiveToolId);
            Assert.Null(context.EditorState.HoveredObjectId);
            Assert.Null(context.EditorState.ActiveGesture);
            Assert.Equal(ViewportSnapshot.Default, context.EditorState.Viewport);
            Assert.All(context.EditorState.TemporaryFeedback, feedback => Assert.NotNull(feedback.PlacementPreview));
            Assert.Empty(context.Presentation!.BaseSceneItems);
        }
    }

    [Fact]
    public async Task PlacementCannotReuseAfterUnrelatedStateOrProvenanceChanges()
    {
        var fixture = new Fixture();
        var before = fixture.Build(State(null));
        var feedback = Feedback(500, 350);
        Assert.Null(await fixture.ReuseAsync(before, new(temporaryFeedback: [feedback], activeToolId: "changed")));
        Assert.Null(await fixture.ReuseAsync(before, new(temporaryFeedback: [feedback], viewport: new(2, default))));
        Assert.Null(await fixture.ReuseAsync(before, new(temporaryFeedback: [feedback]))); // Selection differs.
        Assert.Null(await fixture.ReuseAsync(before, State(null))); // No effective transition.
        Assert.Null((await fixture.FreshBuilder().TryReuseForPlacementPreviewAsync(before, fixture.Document,
            fixture.Document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty, fixture.Inputs.Graph, fixture.Inputs.Layout,
            fixture.Inputs.Routing, State(feedback), null, null, default)).Scene);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fixture.ReuseAsync(before, State(feedback), cancellationToken: new(true)));
    }

    [Fact]
    public async Task CandidateLabelPreparationIsLocalAndReusedAcrossPositionAndValidityUpdates()
    {
        var fixture = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = await fixture.BuildMeasuredAsync(State(null), renderer);
        var state = State(Feedback(500, 350, outsideLabel: true));
        scene = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(scene, state, renderer));
        var measurements = execution.Calls.Count(static call => call == "measure");
        var prepared = Assert.Single(scene.BoundedPresentation!.PlacementLabelLayouts);
        foreach (var feedback in new[]
        {
            Feedback(550, 375, outsideLabel: true),
            Feedback(550, 375, allowed: false, outsideLabel: true),
            Feedback(100, 775, canonicalY: 20, outsideLabel: true),
        })
        {
            state = State(feedback);
            scene = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(scene, state, renderer));
            Assert.Equal(measurements, execution.Calls.Count(static call => call == "measure"));
            Assert.Same(prepared, Assert.Single(scene.BoundedPresentation!.PlacementLabelLayouts));
            var oracle = await fixture.BuildMeasuredAsync(state, renderer, fixture.FreshBuilder());
            Assert.Equal(oracle, scene);
            var body = scene.Items.Single(item => item.Origin.StableSourceKey == "feedback:placement:body");
            var label = scene.Items.Single(item => item.Origin.StableSourceKey == "feedback:placement:label:0");
            Assert.Equal(body.Bounds.Bottom + 8, label.Bounds.Top);
        }
    }

    [Fact]
    public async Task BoundedTransportRetiresWholeFamilyAndRecoversAfterFailureAndSurfaceReplacement()
    {
        var fixture = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var before = fixture.Build(State(null));
        Assert.True((await renderer.RenderAsync(before)).Succeeded);
        var token = execution.LastFrame!.ContentVersion;
        var active = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(before, State(Feedback(500, 350))));
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        Assert.Equal(token, execution.LastViewportFrame!.ContentVersion);
        Assert.NotEmpty(execution.LastViewportFrame.PresentationItems!);
        Assert.Single(execution.Calls, static call => call == "render");
        var retired = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(active, State(null)));
        Assert.True((await renderer.RenderAsync(retired)).Succeeded);
        Assert.Equal(before.BoundedPresentation!.Items.Length, execution.LastViewportFrame.PresentationItems!.Length);
        execution.RenderResult = Canvas2DRendererTestExecution.Failure("test:failure", "test");
        Assert.False((await renderer.RenderAsync(active)).Succeeded);
        Assert.False(renderer.HasAcknowledgedContent(active));
        execution.RenderResult = Canvas2DRendererTestExecution.Success();
        Assert.True((await renderer.RenderAsync(retired)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.True((await renderer.ResizeAsync(new Canvas2DSurfaceSize(901, 601, 2))).Succeeded);
        Assert.False(renderer.HasAcknowledgedContent(retired));
        Assert.True((await renderer.RenderAsync(retired)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.DoesNotContain(execution.LastFrame!.PresentationItems, item => item.Item.Id.Contains("placement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DelayedPlacementAcknowledgementCannotResurrectRetiredFamilyOrAuthorizeReplacement()
    {
        var fixture = new Fixture();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var before = fixture.Build(State(null));
        Assert.True((await renderer.RenderAsync(before)).Succeeded);
        var active = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(before, State(Feedback(500, 350))));
        var retired = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(active, State(null)));
        execution.RenderEnteredSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = renderer.RenderAsync(active).AsTask();
        await execution.RenderEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var retiring = renderer.RenderAsync(retired).AsTask();
        Assert.False(retiring.IsCompleted);
        execution.RenderReleaseSignal.TrySetResult();
        Assert.True((await pending).Succeeded);
        Assert.True((await retiring).Succeeded);
        Assert.DoesNotContain(execution.LastViewportFrame!.PresentationItems!, item =>
            item.Item.Id.Contains("placement", StringComparison.Ordinal));
        var replacement = new Fixture().Build(State(null));
        Assert.False(renderer.HasAcknowledgedContent(replacement));
        Assert.True((await renderer.RenderAsync(replacement)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.DoesNotContain(execution.LastFrame!.PresentationItems, item =>
            item.Item.Id.Contains("placement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectedNegativeBodyKeepsOutsideCaptionMovingWithTheCompleteFamily()
    {
        var fixture = new Fixture();
        var scene = fixture.Build(State(null));
        foreach (var feedback in new[]
        {
            Feedback(-100, -60, allowed: false, outsideLabel: true),
            Feedback(-150, -90, allowed: false, outsideLabel: true),
        })
        {
            scene = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(scene, State(feedback)));
            Assert.Equal(fixture.Build(State(feedback), fixture.FreshBuilder()), scene);
            var body = scene.Items.Single(item => item.Origin.StableSourceKey == "feedback:placement:body");
            var label = scene.Items.Single(item => item.Origin.StableSourceKey == "feedback:placement:label:0");
            Assert.Equal(body.Bounds.X + body.Bounds.Width / 2, label.Bounds.X + label.Bounds.Width / 2);
            Assert.Equal(body.Bounds.Bottom + 8, label.Bounds.Top);
            Assert.True(label.Bounds.Left < 0);
        }
    }

    [Fact]
    public async Task BoundedPlacementPayloadAndContributorWorkStayLocalAtRepresentativeSizes()
    {
        var sizes = new List<int>();
        foreach (var count in new[] { 100, 250, 500 })
        {
            var fixture = new Fixture(count);
            var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            await using var lifetime = renderer;
            var before = fixture.Build(State(null));
            await renderer.RenderAsync(before);
            var active = Assert.IsType<Canvas2DScene>(await fixture.ReuseAsync(before, State(Feedback(500, 350))));
            Assert.Equal(1, fixture.Stable.Calls);
            Assert.Equal(3, active.BoundedPresentation!.PlacementItems.Length);
            Assert.Equal(fixture.Build(State(Feedback(500, 350)), fixture.FreshBuilder()), active);
            await renderer.RenderAsync(active);
            Assert.Single(execution.Calls, static call => call == "render");
            Assert.Contains(active.BoundedPresentation.BeforeContentIndices, index => index < active.RenderContent.Length);
            sizes.Add(JsonSerializer.SerializeToUtf8Bytes(execution.LastViewportFrame).Length);
        }
        Assert.InRange(sizes.Max() - sizes.Min(), 0, 40);
    }

    private static bool IsPlacementItem(Canvas2DSceneItem item) =>
        item.Origin.StableSourceKey?.StartsWith("feedback:placement:", StringComparison.Ordinal) == true;

    private static EditorStateSnapshot State(EditorFeedbackSnapshot? feedback, ViewportSnapshot? viewport = null) =>
        new(selection: [new VisualStateId("test:visual:a")],
            viewport: viewport, temporaryFeedback: feedback is null ? [] : [feedback]);

    private static EditorFeedbackSnapshot Feedback(double x, double y, bool allowed = true,
        double? canonicalY = null, bool outsideLabel = false)
    {
        var canonical = new RectD(x, canonicalY ?? y, 120, 80);
        var preview = new ToolboxPlacementPreview(new("test:tool"), new("test:type"), canonical,
            new PointD(canonical.X + 60, canonical.Y + 40), "Prospective node",
            outsideLabel ? new(NodeLabelPlacementKind.OutsideBelow, gap: 8, maximumWidth: 180) : null,
            isAllowed: allowed, diagnostics: allowed ? [] :
                [new Diagnostic("test:rejected", DiagnosticSeverity.Warning, "Rejected")]);
        return new("placement", new RectD(x, y, 120, 80), preview);
    }

    private sealed class Fixture
    {
        private readonly int _count;
        private readonly Canvas2DScenePlacementDependency _dependency;
        private readonly string? _invalid;
        private readonly Canvas2DSceneTransientDependency _selectionDependency;
        internal Canvas2DSceneTestData Inputs { get; } = Canvas2DSceneTestData.CreateWithNodeLabels();
        internal DocumentSnapshot Document { get; }
        internal StableContributor Stable { get; }
        internal PreviewContributor Preview { get; }
        internal Canvas2DSceneBuilder Builder { get; }
        internal Fixture(int count = 0, Canvas2DScenePlacementDependency dependency = Canvas2DScenePlacementDependency.Invariant,
            string? invalid = null, bool annotateSelection = false,
            Canvas2DSceneTransientDependency selectionDependency = Canvas2DSceneTransientDependency.Invariant)
        {
            _count = count;
            _dependency = dependency;
            _invalid = invalid;
            _selectionDependency = selectionDependency;
            if (annotateSelection)
            {
                var graph = Inputs.Graph;
                Inputs = Inputs.WithGraph(new ProjectedGraph(graph.DocumentId, graph.SourceRevision,
                    graph.Nodes.Select(node => new ProjectedNode(node.Source, node.PlacementHint, node.SemanticProperties,
                        node.ProjectedProperties.Append(Canvas2DTransientInteractionMetadata.BoundedSelectionEnabled),
                        node.LayoutHints, node.RoutingHints, node.AlgorithmMetadata, node.GeometryInteractionPolicy)),
                    graph.Edges, graph.Groups, graph.Ports, graph.Labels));
            }
            Stable = new(count);
            Preview = new(invalid);
            Builder = CreateBuilder(Stable, Preview);
            Document = EditingSessionTestHarness.CreateDocument(Inputs).CaptureSnapshot();
        }
        internal Canvas2DSceneBuilder FreshBuilder() => CreateBuilder(new(_count), new(_invalid));
        private Canvas2DSceneBuilder CreateBuilder(StableContributor stable, PreviewContributor preview) => new(contributors:
        [
            new(Descriptor("test:stable", _dependency, _selectionDependency), stable),
            new(Descriptor("test:placement", Canvas2DScenePlacementDependency.BoundedFeedbackOnly), preview),
        ]);
        internal Canvas2DScene Build(EditorStateSnapshot state, Canvas2DSceneBuilder? builder = null) =>
            Assert.IsType<Canvas2DScene>((builder ?? Builder).Build(Document, Document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                Inputs.Graph, Inputs.Layout, Inputs.Routing, Document.VisualModel, state).Scene);
        internal async Task<Canvas2DScene> BuildMeasuredAsync(EditorStateSnapshot state, Canvas2DRenderer renderer,
            Canvas2DSceneBuilder? builder = null) => Assert.IsType<Canvas2DScene>((await (builder ?? Builder).BuildMeasuredAsync(
                Document, Document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty, Inputs.Graph, Inputs.Layout, Inputs.Routing,
                Document.VisualModel, state, renderer, renderer.CreateTextMeasurementRequest)).Scene);
        internal async ValueTask<Canvas2DScene?> ReuseAsync(Canvas2DScene scene, EditorStateSnapshot state,
            Canvas2DRenderer? renderer = null, CancellationToken cancellationToken = default) =>
            (await Builder.TryReuseForPlacementPreviewAsync(scene, Document, Document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty, ModelProfileElementViewStateSnapshot.Empty,
                Inputs.Graph, Inputs.Layout, Inputs.Routing, state, renderer,
                renderer is null ? null : renderer.CreateTextMeasurementRequest, cancellationToken)).Scene;
    }

    private static Canvas2DSceneContributorDescriptor Descriptor(string id, Canvas2DScenePlacementDependency dependency,
        Canvas2DSceneTransientDependency selectionDependency = Canvas2DSceneTransientDependency.Invariant) =>
        new(new(id), "1", Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, selectionDependency, dependency);

    private sealed class StableContributor(int count) : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
                Enumerable.Range(0, count).Select(index => new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(context.Contributor.ContributorId, $"unrelated:{index:D4}"),
                    Canvas2DSceneLayer.Overlay, index % 2 == 0 ? 2000 : 7000,
                    Canvas2DSceneGeometry.Rectangle(new RectD(1000 + index, 80, 10, 20)),
                    new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.RegisteredExtension,
                        stableSourceKey: $"unrelated:{index:D4}")))));
        }
    }

    private sealed class PreviewContributor(string? invalid) : ICanvas2DSceneContributor
    {
        private EditorFeedbackSnapshot? _lastFeedback;
        internal Canvas2DSceneContributionContext? LastContext { get; private set; }

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            LastContext = context;
            var feedback = context.EditorState.TemporaryFeedback.FirstOrDefault(static value => value.PlacementPreview is not null);
            if (feedback is not null)
            {
                _lastFeedback = feedback;
            }
            else if (invalid == "phantom")
            {
                feedback = _lastFeedback;
            }
            if (feedback is null)
            {
                return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution());
            }
            var bounds = feedback.Bounds!.Value;
            var count = invalid == "oversized" ? 129 : 2;
            var items = Enumerable.Range(0, count).Select(index =>
            {
                var key = $"feedback:{feedback.Id}:{(index == 0 ? "body" : $"marker:{index}")}";
                return new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForExtension(context.Contributor.ContributorId, key),
                    invalid == "content" ? Canvas2DSceneLayer.Content : Canvas2DSceneLayer.Overlay, 5000 + index,
                    Canvas2DSceneGeometry.Ellipse(index == 0 ? bounds : new RectD(bounds.X + 5, bounds.Y + 5, 12, 12)),
                    new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.EditorState | Canvas2DSceneOriginCategory.RegisteredExtension |
                        (invalid == "owner" ? Canvas2DSceneOriginCategory.SemanticElement : Canvas2DSceneOriginCategory.None),
                        semanticElementId: invalid == "owner" ? new("test:element:a") : null, stableSourceKey: key),
                    style: new Canvas2DSceneStyle(fill: "white", stroke: feedback.PlacementPreview!.IsAllowed ? "green" : "red"),
                    hitTestPolicy: invalid == "hit" ? new(Canvas2DHitTestMode.Bounds) : Canvas2DHitTestPolicy.None,
                    persistentAppearance: invalid == "appearance" ? [new("test", PropertyValue.FromBoolean(true))] : null);
            });
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(items,
                metadata: invalid == "metadata" ? [new("test", PropertyValue.FromBoolean(true))] : null,
                canonicalItemVisualOverrides: invalid == "overrides" ?
                    [new(Canvas2DSceneObjectIdentity.ForProjected(context.ProjectedGraph.Nodes[0].Id, "node"),
                        Canvas2DSceneGeometry.Ellipse(bounds), new Canvas2DSceneStyle())] : null,
                spatialPresentationPlan: invalid == "spatial" ? new([], [], Matrix2D.Identity) : null));
        }
    }
}
