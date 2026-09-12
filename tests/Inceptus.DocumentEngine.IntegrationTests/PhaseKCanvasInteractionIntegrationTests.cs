using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseKCanvasInteractionIntegrationTests
{
    [Fact]
    public async Task PointerSelectionAndHoverUseOnlySceneRebuildAndPreserveAuthoritativeState()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var document = composition.Document;
        var persistentBefore = document.CaptureSnapshot();
        var events = new RecordingSubscriber();
        var commandProbe = new CountingCommandValidator();
        var source = composition.Configuration;
        var commandProbeRegistration = new CommandValidatorRegistration(
            MoveVisualStateCommand.KnownTypeId,
            new CommandValidatorId("test:phase-k:no-command"),
            commandProbe);
        var configuration = new EditingSessionConfiguration(
            source.ProjectionEngine,
            source.LayoutEngine,
            source.LayoutAlgorithmId,
            source.RoutingEngine,
            source.RoutingAlgorithmId,
            source.SceneBuilder,
            source.ProjectionContext,
            source.LayoutContext,
            source.RoutingContext,
            source.InitialEditorState,
            source.CommandHandlers,
            source.CommandValidators.Append(commandProbeRegistration),
            source.HistoryPolicies,
            [events]);
        Assert.Contains(commandProbeRegistration, configuration.CommandValidators);
        var execution = new RecordingRenderExecution();
        var renderer = new Canvas2DRenderer(
            execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "demo:font:dejavu",
                        "2.37",
                        "DejaVu Sans",
                        "/fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync(
            "phase-k-canvas",
            new Canvas2DSurfaceSize(900d, 600d, 2d))).Succeeded);

        var attachment = await EditingSession.AttachAsync(document, renderer, configuration);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var ownedSession = session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialScene = Assert.IsType<Canvas2D.Scene.Canvas2DScene>(initial.CurrentScene);
        var initialGraph = initial.ProjectedGraph;
        var initialLayout = initial.LayoutResult;
        var initialRouting = initial.RoutingResult;
        var historyBefore = initial.HistoryStatus;
        var initialProjectionCount = composition.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = composition.Counters.LayoutInvocationCount;
        var initialRoutingCount = composition.Counters.RoutingInvocationCount;
        var initialBuildCount = composition.Counters.SceneContributionInvocationCount;
        var initialRenderCount = execution.RenderCount;
        var betaLabel = FindTopmostLabel(initialScene, "demo:beta");
        var betaDocumentPoint = Center(betaLabel.Bounds);
        var betaCssPoint = initialScene.ViewportTransform.TransformPoint(betaDocumentPoint);

        var directHit = new Canvas2DSceneHitTestService().HitTest(
            initialScene,
            Canvas2DRenderer.ConvertCssToDocument(initialScene, betaCssPoint));

        Assert.NotNull(directHit);
        Assert.Equal(betaLabel.Id, directHit.SceneObjectId);
        Assert.Equal(new SemanticElementId("demo:beta"), directHit.Origin.SemanticElementId);
        Assert.NotNull(directHit.Origin.VisualStateId);
        Assert.Equal(betaLabel.Origin.VisualStateId, directHit.Origin.VisualStateId);

        var selection = await interaction.PointerActivatedAsync(betaCssPoint);
        var afterSelection = session.CaptureState();

        Assert.Equal(Canvas2DInteractionStatus.Updated, selection.Status);
        Assert.Equal(betaLabel.Id, selection.TargetId);
        Assert.Equal(betaLabel.Origin.VisualStateId, Assert.Single(afterSelection.EditorState.Selection));
        Assert.Same(initialGraph, afterSelection.ProjectedGraph);
        Assert.Same(initialLayout, afterSelection.LayoutResult);
        Assert.Same(initialRouting, afterSelection.RoutingResult);
        Assert.Equal(initialProjectionCount, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, composition.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, composition.Counters.RoutingInvocationCount);
        Assert.Equal(initialBuildCount + 1, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 1, execution.RenderCount);
        var betaSelectionTarget = afterSelection.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == betaLabel.Origin.VisualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        Assert.Contains(afterSelection.CurrentScene!.Items, item =>
            item.Origin.StableSourceKey == $"selection:{betaSelectionTarget.Id.Value}" &&
            item.Origin.RelatedSceneObjectIds.Contains(betaSelectionTarget.Id));
        AssertAuthoritativeStateUnchanged(
            persistentBefore,
            document,
            historyBefore,
            afterSelection,
            events);

        var gammaLabel = FindTopmostLabel(afterSelection.CurrentScene, "demo:gamma");
        var gammaCssPoint = afterSelection.CurrentScene.ViewportTransform.TransformPoint(
            Center(gammaLabel.Bounds));
        var beforeHoverBuildCount = composition.Counters.SceneContributionInvocationCount;
        var beforeHoverRenderCount = execution.RenderCount;

        var hover = await interaction.PointerMovedAsync(gammaCssPoint);
        var afterHover = session.CaptureState();

        Assert.Equal(Canvas2DInteractionStatus.Updated, hover.Status);
        Assert.Equal(gammaLabel.Id, afterHover.EditorState.HoveredObjectId);
        Assert.Equal(betaLabel.Origin.VisualStateId, Assert.Single(afterHover.EditorState.Selection));
        Assert.Same(initialGraph, afterHover.ProjectedGraph);
        Assert.Same(initialLayout, afterHover.LayoutResult);
        Assert.Same(initialRouting, afterHover.RoutingResult);
        Assert.Equal(initialProjectionCount, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, composition.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, composition.Counters.RoutingInvocationCount);
        Assert.Equal(beforeHoverBuildCount + 1, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(beforeHoverRenderCount + 1, execution.RenderCount);
        Assert.Contains(afterHover.CurrentScene!.Items, item =>
            item.Origin.StableSourceKey == $"hover:{gammaLabel.Id.Value}" &&
            item.Origin.RelatedSceneObjectIds.Contains(gammaLabel.Id));
        AssertAuthoritativeStateUnchanged(
            persistentBefore,
            document,
            historyBefore,
            afterHover,
            events);

        var unchangedBuildCount = composition.Counters.SceneContributionInvocationCount;
        var unchangedRenderCount = execution.RenderCount;
        var unchangedScene = afterHover.CurrentScene;
        var unchanged = await interaction.PointerMovedAsync(gammaCssPoint);

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, unchanged.Status);
        Assert.Same(unchangedScene, session.CaptureState().CurrentScene);
        Assert.Equal(unchangedBuildCount, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(unchangedRenderCount, execution.RenderCount);
        Assert.Equal(initialProjectionCount, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, composition.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, composition.Counters.RoutingInvocationCount);
        AssertAuthoritativeStateUnchanged(
            persistentBefore,
            document,
            historyBefore,
            session.CaptureState(),
            events);
        Assert.Equal(0, commandProbe.InvocationCount);
    }

    private static Canvas2DSceneItem FindTopmostLabel(
        Canvas2D.Scene.Canvas2DScene scene,
        string semanticElementId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId == new SemanticElementId(semanticElementId));

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertAuthoritativeStateUnchanged(
        Contracts.Documents.DocumentSnapshot persistentBefore,
        Runtime.Documents.Document document,
        Contracts.History.HistoryStatus historyBefore,
        EditingSessionState state,
        RecordingSubscriber events)
    {
        Assert.Equal(persistentBefore, document.CaptureSnapshot());
        Assert.Equal(persistentBefore.Revision, document.Revision);
        Assert.Equal(historyBefore, state.HistoryStatus);
        Assert.Empty(events.Events);
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingCommandValidator : ICommandValidator
    {
        private int _invocationCount;

        internal int InvocationCount => Volatile.Read(ref _invocationCount);

        public ImmutableArray<Diagnostic> Validate(
            ICommand command,
            DocumentSnapshot document)
        {
            _ = command;
            _ = document;
            Interlocked.Increment(ref _invocationCount);
            return [];
        }
    }

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        private int _renderCount;

        internal int RenderCount => Volatile.Read(ref _renderCount);

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(
            Canvas2DRenderFrame frame)
        {
            Interlocked.Increment(ref _renderCount);
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request) =>
            ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = 80d,
                Ascent = 10d,
                Descent = 3d,
                LineHeight = 18d,
                BoundingX = 0d,
                BoundingY = -10d,
                BoundingWidth = 80d,
                BoundingHeight = 13d,
                ResolvedFontIdentity = "demo:font:dejavu@2.37",
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
