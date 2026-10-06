using System.Collections.Immutable;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Routing;
using Xunit.Abstractions;
using Fixture = Inceptus.DocumentEngine.IntegrationTests.PhaseA122PanSceneReuseIntegrationTests.Fixture;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class ConnectorLabelMovePerformanceIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(ConnectorRoutingType.Automatic, false)]
    [InlineData(ConnectorRoutingType.Straight, false)]
    [InlineData(ConnectorRoutingType.Manual, false)]
    [InlineData(ConnectorRoutingType.Manual, true)]
    public async Task HundredConnectorLabelMovesAvoidUnrelatedDiagramWork(ConnectorRoutingType mode, bool pools)
    {
        var work = new RoutingWork();
        var (test, document) = await CreateAsync(work);
        await using var lifetime = test;
        if (pools) await test.EnablePoolsAsync();
        var label = test.State.CurrentScene!.Items.First(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.IsVisible && item.Metadata.ContainsKey(Canvas2DLabelGestureMetadata.LabelMoveCapable));
        var owner = label.Origin.VisualStateId!;
        if (mode != ConnectorRoutingType.Automatic)
            await test.ExecuteAsync(new SetConnectorRoutingTypeCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, owner, mode));
        if (mode == ConnectorRoutingType.Manual)
        {
            var route = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner);
            await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, owner,
                [route.Path[0], route.Path[0] + new VectorD(40, 20), route.Path[^1] + new VectorD(-40, 20), route.Path[^1]]));
        }
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [owner]));
        label = test.State.CurrentScene!.Items.Single(item => item.Id == label.Id);
        var start = new PointD(label.Bounds.X + label.Bounds.Width / 2, label.Bounds.Y + label.Bounds.Height / 2);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(start));
        await controller.PointerPressedAsync(Pointer(start, 1));
        Assert.Equal(Canvas2DLabelGestureMetadata.Kind, test.State.EditorState.ActiveGesture?.Kind);
        var originalGesture = test.State.EditorState.ActiveGesture!;
        var snapshot = test.Snapshot;
        var layout = test.State.LayoutResult!.Computation;
        var routing = test.State.RoutingResult!.Computation;
        output.WriteLine("TARGET " + JsonSerializer.Serialize(new
        {
            label = label.Id.Value,
            owner = owner.Value,
            routingMode = snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
                .Single(record => record.VisualStateId == owner).RoutingType.ToString(),
        }));
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        var before = Capture();
        var notifications = 0;
        var updates = 0;
        test.Session.StateChanged += (_, _) => Interlocked.Increment(ref notifications);
        var recreatedNodes = 0;
        var recreatedConnectors = 0;
        var recreatedLabels = 0;
        for (var index = 1; index <= 100; index++)
        {
            var previous = test.State.CurrentScene!.Items.ToDictionary(item => item.Id);
            var priorGesture = test.State.EditorState.ActiveGesture;
            var delta = new VectorD(index, index * 0.4);
            await controller.PointerMovedAsync(Pointer(start + delta, 1));
            var current = test.State;
            if (!ReferenceEquals(priorGesture, current.EditorState.ActiveGesture)) updates++;
            Assert.Equal(originalGesture.Id, current.EditorState.ActiveGesture!.Id);
            Assert.Equal(originalGesture.Properties, current.EditorState.ActiveGesture.Properties);
            var preview = Assert.Single(current.CurrentScene!.Items, item => item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.RelatedSceneObjectIds.Contains(label.Id) &&
                item.Origin.StableSourceKey?.StartsWith("connector-label-preview:", StringComparison.Ordinal) == true);
            var expectedBounds = label.Bounds.Translate(delta);
            Assert.Equal(expectedBounds.X, preview.Bounds.X, 8);
            Assert.Equal(expectedBounds.Y, preview.Bounds.Y, 8);
            Assert.Equal(expectedBounds.Width, preview.Bounds.Width, 8);
            Assert.Equal(expectedBounds.Height, preview.Bounds.Height, 8);
            foreach (var item in current.CurrentScene.Items.Where(item => item.Origin.VisualStateId != owner &&
                         previous.TryGetValue(item.Id, out var old) && !ReferenceEquals(item, old)))
            {
                if (item.Layer == Canvas2DSceneLayer.Content) recreatedNodes++;
                if (item.Layer == Canvas2DSceneLayer.Connector) recreatedConnectors++;
                if (item.Layer == Canvas2DSceneLayer.Label) recreatedLabels++;
            }
            Assert.Same(snapshot, test.Snapshot);
            Assert.Equal(history, current.HistoryStatus);
            Assert.Equal(events, test.Events.Count);
            Assert.Same(layout, current.LayoutResult!.Computation);
            Assert.Same(routing, current.RoutingResult!.Computation);
            Assert.False(current.CurrentScene.Items.Single(item => item.Id == label.Id).IsVisible);
        }
        var previewCounts = Capture();
        Assert.Equal(100, notifications);
        Assert.Equal(100, previewCounts["boundedLabelReuses"] - before["boundedLabelReuses"]);
        foreach (var counter in new[] { "fullPipelineRuns", "sceneRebuilds", "contributorCalls", "spatialPreparation",
                     "presentationRoutes", "layout", "routeSearch", "assessments", "repairs", "browserTextMetricMisses" })
            Assert.Equal(before[counter], previewCounts[counter]);
        output.WriteLine("PREVIEW " + JsonSerializer.Serialize(new
        {
            controllerInputs = 100,
            gestureUpdates = updates,
            notifications,
            revisions = 0,
            historyEntries = 0,
            committedEvents = 0,
            delta = Delta(before, previewCounts),
            recreatedNodes,
            recreatedConnectors,
            recreatedLabels,
        }));
        var result = await controller.PointerReleasedAsync(Pointer(start + new VectorD(100, 40)));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
        output.WriteLine("COMMIT " + JsonSerializer.Serialize(Delta(previewCounts, Capture())));
        Assert.Equal(Canvas2DInteractionStatus.Committed, result.Status);
        Assert.Equal(snapshot.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors),
            test.Snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors));
        Assert.Equal(0, previewCounts["fullUploads"] - before["fullUploads"]);
        Assert.Equal(0, recreatedNodes + recreatedConnectors + recreatedLabels);
        Assert.Equal(before["assessments"], Capture()["assessments"]);
        var committed = test.Snapshot;
        AssertInvariants();
        Assert.Equal(events + 1, test.Events.Count);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await Idle();
        Assert.Equal(snapshot.VisualModel.VisualStates.AsEnumerable(), test.Snapshot.VisualModel.VisualStates);
        AssertInvariants();
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await Idle();
        Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(), test.Snapshot.VisualModel.VisualStates);
        AssertInvariants();
        Assert.Equal(events + 3, test.Events.Count);

        // Native v2 retains the same placement and route; the new session owns empty History.
        var bytes = NativeDocumentSerializer.Export(test.Snapshot);
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = await Fixture.CreateAsync(BpmnModelerComposition.Create(opened.Document!));
        Assert.Equal(test.Snapshot, reopened.Snapshot);
        Assert.Equal(new HistoryStatus(0, false, false), reopened.State.HistoryStatus);
        await reopened.ExecuteAsync(new MoveLabelCommand(reopened.Snapshot.DocumentId, reopened.Snapshot.Revision, owner,
            new ConnectorLabelPlacement(0.25, new VectorD(13, 19))));
        Assert.True((await reopened.Session.UndoAsync()).IsCommitted);
        await reopened.Session.WaitForIdleAsync();
        Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(), reopened.Snapshot.VisualModel.VisualStates);

        async Task Idle()
        {
            await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
            await test.Session.WaitForIdleAsync();
        }
        void AssertInvariants()
        {
            Assert.Same(layout, test.State.LayoutResult!.Computation);
            Assert.Same(routing, test.State.RoutingResult!.Computation);
            Assert.Equal(snapshot.SemanticModel.Relationships.AsEnumerable(), test.Snapshot.SemanticModel.Relationships);
            Assert.Equal(snapshot.VisualModel.RoutingScopes!.Value.AsEnumerable(), test.Snapshot.VisualModel.RoutingScopes!.Value);
            foreach (var original in snapshot.VisualModel.VisualStates)
            {
                var current = test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id);
                Assert.Equal(original, original.Id == owner
                    ? new VisualStateSnapshot(current.Id, current.SemanticElementId, current.Position, current.Size,
                        current.PlacementMode, current.Route, original.Properties, current.ConnectorAnchors,
                        current.SourceAnchorId, current.TargetAnchorId, current.BoundaryAttachment) : current);
            }
            Assert.Equal(snapshot.VisualModel.VisualStates.Select(item => item.Id), test.Snapshot.VisualModel.VisualStates.Select(item => item.Id));
            foreach (var counter in new[] { "spatialPreparation", "presentationRoutes", "layout", "routeSearch", "assessments", "repairs" })
                Assert.Equal(before[counter], Capture()[counter]);
        }

        Canvas2DPointerInput Pointer(PointD point, int buttons = 0) =>
            new(96, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
        Dictionary<string, int> Capture() => new()
        {
            ["fullPipelineRuns"] = test.Pipeline.FullRuns,
            ["sceneRebuilds"] = test.Pipeline.Rebuilds,
            ["boundedLabelReuses"] = test.Pipeline.ConnectorLabelMoveReuses,
            ["contributorCalls"] = test.Contributions.Sum(item => item.Calls),
            ["spatialPreparation"] = test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
                .Sum(item => item.BasePreparations + item.PresentationPreparations),
            ["presentationRoutes"] = test.Contributions.Sum(item => item.Routes),
            ["layout"] = work.Layouts,
            ["routeSearch"] = work.Routes,
            ["assessments"] = work.Assessments,
            ["repairs"] = work.Repairs,
            ["rendererFrames"] = test.Execution.RenderCount,
            ["fullUploads"] = test.Execution.FullUploadCount,
            ["browserTextMetricMisses"] = test.Execution.MeasurementRequests.Count,
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoOpAndStaleLabelGesturesAreAtomicAndLeaveSessionUsable(bool stale)
    {
        var work = new RoutingWork();
        var (test, document) = await CreateAsync(work);
        await using var lifetime = test;
        var label = test.State.CurrentScene!.Items.First(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.IsVisible && item.Metadata.ContainsKey(Canvas2DLabelGestureMetadata.LabelMoveCapable));
        var owner = label.Origin.VisualStateId!;
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [owner]));
        var start = new PointD(label.Bounds.X + label.Bounds.Width / 2, label.Bounds.Y + label.Bounds.Height / 2);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(start));
        await controller.PointerPressedAsync(Pointer(start, 1));
        Assert.Equal(Canvas2DLabelGestureMetadata.Kind, test.State.EditorState.ActiveGesture?.Kind);
        var capturedRevision = test.Snapshot.Revision;
        await controller.PointerMovedAsync(Pointer(start + new VectorD(50, 20), 1));
        if (stale)
            await test.ExecuteAsync(new MoveLabelCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, owner,
                new ConnectorLabelPlacement(0.75, new VectorD(8, 9))));
        var expected = test.Snapshot;
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        var counts = (work.Layouts, work.Routes, work.Assessments, work.Repairs,
            test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
                .Sum(item => item.BasePreparations + item.PresentationPreparations));
        var release = await controller.PointerReleasedAsync(Pointer(stale ? start + new VectorD(50, 20) : start));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, release.Status);
        if (stale)
        {
            var result = await test.Session.ExecuteAsync(new MoveLabelCommand(expected.DocumentId, capturedRevision,
                owner, new ConnectorLabelPlacement(0.1, new VectorD(10, 10))));
            Assert.False(result.IsCommitted);
        }
        var invalid = await test.Session.ExecuteAsync(new MoveLabelCommand(expected.DocumentId, expected.Revision,
            new VisualStateId("absent:connector"), new ConnectorLabelPlacement(0.5, default)));
        Assert.False(invalid.IsCommitted);
        Assert.Same(expected, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        Assert.Equal(counts, (work.Layouts, work.Routes, work.Assessments, work.Repairs,
            test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
                .Sum(item => item.BasePreparations + item.PresentationPreparations)));
        await test.ExecuteAsync(new MoveLabelCommand(expected.DocumentId, expected.Revision, owner,
            new ConnectorLabelPlacement(0.4, new VectorD(15, 12))));
        Assert.Equal(events + 1, test.Events.Count);

        Canvas2DPointerInput Pointer(PointD point, int buttons = 0) =>
            new(98, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
    }

    [Fact]
    public async Task CombinedNodeAndLabelEditStillPreparesAffectedRouting()
    {
        var work = new RoutingWork();
        var (test, _) = await CreateAsync(work);
        await using var lifetime = test;
        var label = test.State.CurrentScene!.Items.First(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.IsVisible && item.Metadata.ContainsKey(Canvas2DLabelGestureMetadata.LabelMoveCapable));
        var node = test.Snapshot.VisualModel.VisualStates.First(visual =>
            test.Snapshot.SemanticModel.TryGetElement(visual.SemanticElementId, out _));
        var before = work.Assessments;
        await test.ExecuteAsync(new CompoundDocumentCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
        [
            new MoveLabelCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, label.Origin.VisualStateId!,
                new ConnectorLabelPlacement(0.25, new VectorD(12, 15))),
            new MoveVisualStateCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, node.Id,
                node.Position + new VectorD(15, 10)),
        ]));
        Assert.True(work.Assessments > before);
    }

    [Fact]
    public async Task ConnectorLabelMoveFullCompositionMeasurementProbe()
    {
        await using var test = await Fixture.CreateAsync();
        var label = test.State.CurrentScene!.Items.First(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.IsVisible && item.Metadata.ContainsKey(Canvas2DLabelGestureMetadata.LabelMoveCapable));
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [label.Origin.VisualStateId!]));
        label = test.State.CurrentScene!.Items.Single(item => item.Id == label.Id);
        var start = new PointD(label.Bounds.X + label.Bounds.Width / 2, label.Bounds.Y + label.Bounds.Height / 2);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(start));
        await controller.PointerPressedAsync(Pointer(start, 1));
        Assert.Equal(Canvas2DLabelGestureMetadata.Kind, test.State.EditorState.ActiveGesture?.Kind);
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
            OrganizationalPluginRegistration.Create(eligibility,
                OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
        var builder = new Canvas2DSceneBuilder(contributors: registrations);
        var metrics = new MetricsProbe(test.Renderer);
        var graph = test.State.ProjectedGraph!;
        var nodes = graph.Nodes.Select(node => node.Id).ToHashSet();
        var edges = graph.Edges.Select(edge => edge.Id).ToHashSet();
        output.WriteLine("LABEL_LAYOUT_INPUTS " + JsonSerializer.Serialize(new
        {
            active = graph.Labels.Single(item => item.Id == label.Origin.ProjectedObjectId).Text,
            nodeLabelsPerComposition = graph.Labels.Count(item => nodes.Contains(item.OwnerId)),
            connectorLabelsPerComposition = graph.Labels.Count(item => edges.Contains(item.OwnerId)),
        }));
        for (var index = 1; index <= 100; index++)
        {
            await controller.PointerMovedAsync(Pointer(start + new VectorD(index, index * 0.4), 1));
            var state = test.State;
            var independent = await builder.BuildMeasuredAsync(test.Snapshot, state.ActiveScopeId,
                state.ModelProfileViewState, state.ModelProfileElementViewState, state.ProjectedGraph!,
                state.LayoutResult!, state.RoutingResult!, test.Snapshot.VisualModel, state.EditorState,
                metrics, test.Renderer.CreateTextMeasurementRequest, CancellationToken.None);
            Assert.Equal(state.CurrentScene, independent.Scene);
        }
        output.WriteLine("FULL_COMPOSITION_MEASUREMENTS " + JsonSerializer.Serialize(new
        {
            calls = metrics.Requests.Count,
            byText = metrics.Requests.GroupBy(item => item.Text)
                .ToDictionary(group => group.Key, group => group.Count()),
        }));

        Canvas2DPointerInput Pointer(PointD point, int buttons = 0) =>
            new(97, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
    }

    private sealed class MetricsProbe(ITextMetricsService inner) : ITextMetricsService
    {
        internal List<TextMeasurementRequest> Requests { get; } = [];
        public ValueTask<TextMeasurementResult> MeasureAsync(TextMeasurementRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.MeasureAsync(request, cancellationToken);
        }
    }

    private static Dictionary<string, int> Delta(Dictionary<string, int> before, Dictionary<string, int> after) =>
        before.ToDictionary(item => item.Key, item => after[item.Key] - item.Value);

    private static async Task<(Fixture Test, Document Document)> CreateAsync(RoutingWork work)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var source = composition.Configuration;
        var configuration = new EditingSessionConfiguration(source.ProjectionEngine,
            new LayoutEngine([new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, work)]), source.LayoutAlgorithmId,
            new RoutingEngine([new RoutingAlgorithmRegistration(source.RoutingAlgorithmId, work)]), source.RoutingAlgorithmId,
            source.SceneBuilder, source.ProjectionContext, source.LayoutContext, source.RoutingContext,
            source.InitialEditorState, source.CommandHandlers, source.CommandValidators, source.HistoryPolicies,
            source.DocumentChangedSubscribers, source.ConnectorAnchorPolicyProvider, source.ModelProfileCatalog,
            source.InitialModelProfileViewState, source.RoutingInputPreparer);
        return (await Fixture.CreateAsync(new DocumentCanvasComposition(composition.Document, configuration,
            composition.PropertiesSchemaCatalog, spatialEditPlanners: composition.SpatialEditPlanners)), composition.Document);
    }

    private sealed class RoutingWork : ILayoutAlgorithm, IRoutingAlgorithm, IStableConnectorRoutingPolicy
    {
        private readonly BpmnLayoutAlgorithm _layout = new();
        private readonly BpmnRoutingAlgorithm _routing = new();
        internal int Layouts, Routes, Assessments, Repairs;
        public LayoutAlgorithmResult Compute(ProjectedGraph graph, LayoutContext context, CancellationToken cancellationToken)
        { Layouts++; return _layout.Compute(graph, context, cancellationToken); }
        public RoutingAlgorithmResult Route(ProjectedGraph graph, LayoutResult layout, RoutingContext context, CancellationToken cancellationToken)
        { Routes++; return _routing.Route(graph, layout, context, cancellationToken); }
        public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate, CancellationToken cancellationToken = default)
        { Assessments++; return _routing.Assess(graph, logicalLayout, context, edgeId, candidate, cancellationToken); }
        public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ImmutableArray<ProjectedObjectId> orderedEdgeIds, CancellationToken cancellationToken = default)
        { Repairs++; return _routing.RouteAffected(graph, logicalLayout, context, orderedEdgeIds, cancellationToken); }
    }
}
