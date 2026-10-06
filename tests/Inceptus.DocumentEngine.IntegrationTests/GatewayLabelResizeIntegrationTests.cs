using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Routing;
using Xunit.Abstractions;
using Fixture = Inceptus.DocumentEngine.IntegrationTests.PhaseA122PanSceneReuseIntegrationTests.Fixture;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class GatewayLabelResizeIntegrationTests(ITestOutputHelper output)
{
    private static readonly double[] Zooms = [1, 1.75];
    public static IEnumerable<object[]> MinimumCases => Enumerable.Range(0, 4).SelectMany(family => Zooms.SelectMany(zoom =>
        PhaseM323BpmnManualGatewayLabelIntegrationTests.ResizeCases.Select(direction => new object[] { family, zoom, direction[0], direction[1] })));

    [Theory]
    [MemberData(nameof(MinimumCases))]
    public async Task AllGatewayDirectionsClampPreviewCommitAnchorsAndHistory(int family, double zoom, string role, string cursor)
    {
        await using var test = await Fixture.CreateAsync();
        var label = await SelectAsync(test, family, zoom);
        await SetBoundsAsync(test, label, new RectD(label.Bounds.X, label.Bounds.Y, 80, 40));
        label = Label(test);
        var before = test.Snapshot;
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        var original = before.VisualModel.VisualStates.Single(item => item.Id == label.Origin.VisualStateId);
        var west = role.Contains("west", StringComparison.Ordinal);
        var east = role.Contains("east", StringComparison.Ordinal);
        var north = role.Contains("north", StringComparison.Ordinal);
        var south = role.Contains("south", StringComparison.Ordinal);
        var delta = new VectorD(west ? 200 : east ? -200 : 0, north ? 200 : south ? -200 : 0);
        var expected = new RectD(west ? label.Bounds.Right - 20 : label.Bounds.Left,
            north ? label.Bounds.Bottom - 20 : label.Bounds.Top, west || east ? 20 : 80, north || south ? 20 : 40);
        var start = Center(Zone(test, label, role).Bounds);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        var preview = await controller.PointerMovedAsync(Pointer(test, start + delta, 1));
        Assert.Equal(cursor, preview.CssCursor);
        Assert.Equal(expected, Preview(test, label).Bounds);
        Assert.Equal(expected, Assert.Single(test.State.CurrentScene!.Items,
            item => item.Origin.StableSourceKey == $"hover:{label.Id.Value}").Bounds);
        Assert.Same(before, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, start + delta))).Status);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(expected, Label(test).Bounds);
        Assert.Equal(before.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        var committed = test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(original, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id));
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(committed, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id));
        Assert.Equal(expected, Label(test).Bounds);

        var noOp = test.Snapshot;
        var noOpHistory = test.State.HistoryStatus;
        var noOpEvents = test.Events.Count;
        start = Center(Zone(test, Label(test), role).Bounds);
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        await controller.PointerMovedAsync(Pointer(test, start + delta, 1));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, start + delta))).Status);
        Assert.Same(noOp, test.Snapshot);
        Assert.Equal(noOpHistory, test.State.HistoryStatus);
        Assert.Equal(noOpEvents, test.Events.Count);
    }

    [Fact]
    public async Task ResizePreviewAndCommitKeepUsableLogicalMinimum()
    {
        await using var test = await Fixture.CreateAsync();
        var label = await SelectAsync(test);
        await using var controller = test.CreateInteractionController();
        var start = Center(Zone(test, label, "southeast").Bounds);
        var end = new PointD(label.Bounds.Left - 100, label.Bounds.Top - 100);
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        await controller.PointerMovedAsync(Pointer(test, end, 1));
        var preview = test.State.CurrentScene!.Items.Single(item =>
            item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId &&
            item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        output.WriteLine($"Minimum preview: {preview.Bounds.Width} x {preview.Bounds.Height}");
        Assert.True(preview.Bounds.Width >= 20 && preview.Bounds.Height >= 20,
            $"Resize shrank below the full activation-zone minimum: {preview.Bounds.Width} x {preview.Bounds.Height}");
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, end))).Status);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(preview.Bounds, Label(test).Bounds);
        var saved = test.Snapshot;
        var reopened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(saved).AsMemory());
        Assert.True(reopened.Succeeded);
        await using var opened = await Fixture.CreateAsync(BpmnModelerComposition.Create(reopened.Document!));
        Assert.Equal(saved, opened.Snapshot);
        Assert.Equal(0, opened.State.HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(0, 1d, false)]
    [InlineData(1, 1d, false)]
    [InlineData(2, 1d, false)]
    [InlineData(3, 1d, false)]
    [InlineData(0, 1.75d, false)]
    [InlineData(1, 1.75d, false)]
    [InlineData(2, 1.75d, false)]
    [InlineData(3, 1.75d, false)]
    [InlineData(0, 1.75d, true)]
    public async Task HundredResizeMovesAndCommitAvoidDiagramProcessing(int family, double zoom, bool pools)
    {
        var (test, layout, routing, document) = await CreateCountedAsync();
        await using var lifetime = test;
        if (pools) await test.EnablePoolsAsync();
        var label = await SelectAsync(test, family, zoom);
        var snapshot = test.Snapshot;
        var state = test.State;
        var events = test.Events.Count;
        var before = Counts(test, layout, routing);
        var declarationsBefore = test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
            .Sum(item => item.BasePreparations);
        await using var controller = test.CreateInteractionController();
        var start = Center(Zone(test, label, "southeast").Bounds);
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        for (var index = 1; index <= 100; index++)
        {
            await controller.PointerMovedAsync(Pointer(test, start + new VectorD(index, index / 2d), 1));
            Assert.Same(snapshot, test.Snapshot);
            Assert.Equal(state.HistoryStatus, test.State.HistoryStatus);
            Assert.Equal(events, test.Events.Count);
            Assert.Equal(Preview(test, label).Bounds, Assert.Single(test.State.CurrentScene!.Items,
                item => item.Origin.StableSourceKey == $"hover:{label.Id.Value}").Bounds);
        }
        var preview = Counts(test, layout, routing);
        output.WriteLine($"Before={before}; after 100 moves={preview}");
        Assert.Equal(before, preview);
        Assert.Equal(declarationsBefore, test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
            .Sum(item => item.BasePreparations));
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, start + new VectorD(100, 50)))).Status);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
        var committed = Counts(test, layout, routing);
        output.WriteLine($"After commit={committed}; delta={committed - before}");
        Assert.Equal(snapshot.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(state.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(before, committed);
        var declarationsAfter = test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
            .Sum(item => item.BasePreparations);
        output.WriteLine($"Base text-declaration query delta: {declarationsAfter - declarationsBefore}");
        Assert.Equal(2, declarationsAfter - declarationsBefore);
        Assert.Same(state.LayoutResult!.Computation, test.State.LayoutResult!.Computation);
        Assert.Same(state.RoutingResult!.Computation, test.State.RoutingResult!.Computation);
        AssertInvariantGeometry(snapshot, test.Snapshot, label.Origin.VisualStateId!);
        var validation = await new ConnectorRoutingStatePreparer((BpmnModelerComposition.Create(document)).Configuration,
            test.Renderer).PrepareAsync(new ConnectorRoutingStatePreparationRequest(test.Snapshot, test.Snapshot,
                [], [], null, false, ConnectorRoutingPreparationPurpose.ValidateSavedState), CancellationToken.None);
        Assert.True(validation.Succeeded, string.Join("; ", validation.Diagnostics.Select(item => item.Message)));
        if (family == 0 && !pools)
        {
            var resized = Label(test).Bounds;
            Assert.Contains(test.State.LayoutResult.Nodes, node => node.Bounds.Left < resized.Right &&
                node.Bounds.Right > resized.Left && node.Bounds.Top < resized.Bottom && node.Bounds.Bottom > resized.Top);
        }
    }

    [Fact]
    public async Task UndersizedNativeLabelOpensUnchangedAndFirstResizeLiftsBothDimensions()
    {
        await using var original = await Fixture.CreateAsync();
        var label = await SelectAsync(original);
        await SetBoundsAsync(original, label, new RectD(label.Bounds.X, label.Bounds.Y, 2, 3));
        var saved = original.Snapshot;
        var bytes = NativeDocumentSerializer.Export(saved);
        Assert.Contains("\"formatVersion\":2", System.Text.Encoding.UTF8.GetString(bytes.AsSpan()).Replace(" ", "", StringComparison.Ordinal));
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(opened.Succeeded);
        await using var test = await Fixture.CreateAsync(BpmnModelerComposition.Create(opened.Document!));
        Assert.Equal(saved, test.Snapshot);
        Assert.Equal(0, test.State.HistoryStatus.EntryCount);
        Assert.Equal(0, test.Events.Count);
        label = await SelectAsync(test);
        Assert.Equal(2, label.Bounds.Width);
        Assert.Equal(3, label.Bounds.Height);
        await using var controller = test.CreateInteractionController();
        var start = Center(Zone(test, label, "southeast").Bounds);
        var end = start + new VectorD(8, 8);
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        await controller.PointerMovedAsync(Pointer(test, end, 1));
        Assert.Equal(20, Preview(test, label).Bounds.Width);
        Assert.Equal(20, Preview(test, label).Bounds.Height);
        Assert.Equal(Canvas2DInteractionStatus.Committed, (await controller.PointerReleasedAsync(Pointer(test, end))).Status);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(20, Label(test).Bounds.Width);
        Assert.Equal(20, Label(test).Bounds.Height);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(2, Label(test).Bounds.Width);
        Assert.Equal(3, Label(test).Bounds.Height);
    }

    [Fact]
    public async Task StaleAndRejectedResizeLeaveDocumentAndProcessingUnchanged()
    {
        var (test, layout, routing, document) = await CreateCountedAsync();
        await using var lifetime = test;
        var label = await SelectAsync(test);
        await using var controller = test.CreateInteractionController();
        var start = Center(Zone(test, label, "east").Bounds);
        await controller.PointerPressedAsync(Pointer(test, start, 1));
        await controller.PointerMovedAsync(Pointer(test, start + new VectorD(25, 0), 1));
        Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1001, 700, 2))).Succeeded);
        var before = test.Snapshot;
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        var work = Counts(test, layout, routing);
        Assert.NotEqual(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, start + new VectorD(35, 0)))).Status);
        var rejected = await test.Session.ExecuteAsync(new UpdateNodeLabelVisualOverrideCommand(before.DocumentId,
            before.Revision, new VisualStateId("test:missing-label"), new NodeLabelVisualOverride(0, 0, 20, 20)));
        Assert.False(rejected.IsCommitted);
        Assert.NotEmpty(rejected.Diagnostics);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(before, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        Assert.Equal(work, Counts(test, layout, routing));
        Assert.Null(test.State.EditorState.ActiveGesture);
        await SetBoundsAsync(test, Label(test), new RectD(label.Bounds.X, label.Bounds.Y, 80, 40));
        Assert.Equal(before.Revision.Increment(), test.Snapshot.Revision);
    }

    private static void AssertInvariantGeometry(DocumentSnapshot before, DocumentSnapshot after, VisualStateId labelOwner)
    {
        Assert.Equal(before.SemanticModel.Elements.ToArray(), after.SemanticModel.Elements.ToArray());
        Assert.Equal(before.SemanticModel.Relationships.ToArray(), after.SemanticModel.Relationships.ToArray());
        Assert.Equal(before.VisualModel.VisualStates.Select(item => item.Id), after.VisualModel.VisualStates.Select(item => item.Id));
        foreach (var old in before.VisualModel.VisualStates)
        {
            var next = after.VisualModel.VisualStates.Single(item => item.Id == old.Id);
            if (old.Id != labelOwner) Assert.Equal(old, next);
            else Assert.Equal(old, new VisualStateSnapshot(next.Id, next.SemanticElementId, next.Position, next.Size,
                next.PlacementMode, next.Route, old.Properties, next.ConnectorAnchors, next.SourceAnchorId, next.TargetAnchorId, next.BoundaryAttachment));
        }
        foreach (var old in before.VisualModel.RoutingScopes!.Value)
        {
            var next = after.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == old.ScopeId);
            Assert.Equal(old.Connectors.ToArray(), next.Connectors.ToArray());
            Assert.Equal(old.Geometry.Nodes.ToArray(), next.Geometry.Nodes.ToArray());
            Assert.Equal(old.Geometry.Regions.ToArray(), next.Geometry.Regions.ToArray());
            Assert.Equal(old.Geometry.SpatialWidths.ToArray(), next.Geometry.SpatialWidths.ToArray());
        }
    }

    private static async Task SetBoundsAsync(Fixture test, Canvas2DSceneItem label, RectD bounds)
    {
        var body = test.State.LayoutResult!.Nodes.Single(item => item.ProjectedObjectId ==
            test.State.ProjectedGraph!.Nodes.Single(node => node.Source.VisualStateId == label.Origin.VisualStateId).Id);
        await test.ExecuteAsync(new UpdateNodeLabelVisualOverrideCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, label.Origin.VisualStateId!, NodeLabelVisualOverride.FromBounds(body.Bounds, bounds)));
    }

    private static Canvas2DSceneItem Preview(Fixture test, Canvas2DSceneItem label) => test.State.CurrentScene!.Items.Single(item =>
        item.Origin.ProjectedObjectId == label.Origin.ProjectedObjectId &&
        item.Origin.StableSourceKey?.StartsWith("resize-preview:", StringComparison.Ordinal) == true &&
        item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);

    private static WorkCounts Counts(Fixture test, LayoutProbe layout, RoutingProbe routing) =>
        new(layout.Calls, routing.Routes, routing.Assessments, routing.Repairs,
            test.Contributions.OfType<PhaseA122PanSceneReuseIntegrationTests.GeometryContributionProbe>()
                .Sum(probe => probe.PresentationPreparations),
            test.Contributions.Sum(probe => probe.Routes));

    private sealed record WorkCounts(int Layout, int Routes, int Assessments, int Repairs, int ScopeGeometry, int PresentationRoutes)
    {
        public static WorkCounts operator -(WorkCounts a, WorkCounts b) =>
            new(a.Layout - b.Layout, a.Routes - b.Routes, a.Assessments - b.Assessments,
                a.Repairs - b.Repairs, a.ScopeGeometry - b.ScopeGeometry, a.PresentationRoutes - b.PresentationRoutes);
    }

    private static async Task<(Fixture Test, LayoutProbe Layout, RoutingProbe Routing, Document Document)> CreateCountedAsync()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var source = composition.Configuration;
        var layout = new LayoutProbe();
        var routing = new RoutingProbe();
        var configuration = new EditingSessionConfiguration(source.ProjectionEngine,
            new LayoutEngine([new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, layout)]), source.LayoutAlgorithmId,
            new RoutingEngine([new RoutingAlgorithmRegistration(source.RoutingAlgorithmId, routing)]), source.RoutingAlgorithmId,
            source.SceneBuilder, source.ProjectionContext, source.LayoutContext, source.RoutingContext,
            source.InitialEditorState, source.CommandHandlers, source.CommandValidators, source.HistoryPolicies,
            source.DocumentChangedSubscribers, source.ConnectorAnchorPolicyProvider, source.ModelProfileCatalog,
            source.InitialModelProfileViewState, source.RoutingInputPreparer);
        var test = await Fixture.CreateAsync(new DocumentCanvasComposition(composition.Document, configuration,
            composition.PropertiesSchemaCatalog, spatialEditPlanners: composition.SpatialEditPlanners));
        return (test, layout, routing, composition.Document);
    }

    private sealed class LayoutProbe : ILayoutAlgorithm
    {
        private readonly BpmnLayoutAlgorithm _inner = new();
        internal int Calls { get; private set; }
        public LayoutAlgorithmResult Compute(ProjectedGraph graph, LayoutContext context, CancellationToken cancellationToken)
        { Calls++; return _inner.Compute(graph, context, cancellationToken); }
    }

    private sealed class RoutingProbe : IRoutingAlgorithm, IStableConnectorRoutingPolicy
    {
        private readonly BpmnRoutingAlgorithm _inner = new();
        internal int Routes { get; private set; }
        internal int Assessments { get; private set; }
        internal int Repairs { get; private set; }
        public RoutingAlgorithmResult Route(ProjectedGraph graph, LayoutResult layout, RoutingContext context, CancellationToken cancellationToken)
        { Routes++; return _inner.Route(graph, layout, context, cancellationToken); }
        public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate, CancellationToken cancellationToken = default)
        { Assessments++; return _inner.Assess(graph, logicalLayout, context, edgeId, candidate, cancellationToken); }
        public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ImmutableArray<ProjectedObjectId> orderedEdgeIds, CancellationToken cancellationToken = default)
        { Repairs++; return _inner.RouteAffected(graph, logicalLayout, context, orderedEdgeIds, cancellationToken); }
    }

    private static async Task<Canvas2DSceneItem> SelectAsync(Fixture test, int family = 0, double zoom = 1)
    {
        var visual = family switch
        {
            0 => BpmnDemoPipeline.ExclusiveGatewayVisualId,
            1 => BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            2 => BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            3 => BpmnDemoPipeline.EventBasedGatewayVisualId,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
        Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, zoom == 1 ? 1 : 2))).Succeeded);
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [visual], viewport: new ViewportSnapshot(zoom, new VectorD(35, 19))))).Succeeded);
        return Label(test);
    }

    private static Canvas2DSceneItem Label(Fixture test) => test.State.CurrentScene!.Items.Single(item =>
        item.Origin.VisualStateId == test.State.EditorState.Selection.Single() && item.Layer == Canvas2DSceneLayer.Label &&
        item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));
    private static Canvas2DSceneItem Zone(Fixture test, Canvas2DSceneItem label, string role) =>
        test.State.CurrentScene!.Items.Single(item => item.Origin.StableSourceKey == $"node-label-resize-zone:{role}:{label.Id.Value}");
    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static Canvas2DPointerInput Pointer(Fixture test, PointD point, int buttons = 0) =>
        new(43, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: buttons);
}
