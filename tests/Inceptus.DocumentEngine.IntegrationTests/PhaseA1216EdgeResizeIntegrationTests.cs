using System.Diagnostics;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Xunit.Abstractions;
using Fixture = Inceptus.DocumentEngine.IntegrationTests.PhaseA122PanSceneReuseIntegrationTests.Fixture;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA1216EdgeResizeIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100, Canvas2DSpatialResizeEdge.Bottom)]
    [InlineData(250, Canvas2DSpatialResizeEdge.Bottom)]
    [InlineData(500, Canvas2DSpatialResizeEdge.Bottom)]
    [InlineData(100, Canvas2DSpatialResizeEdge.Right)]
    [InlineData(250, Canvas2DSpatialResizeEdge.Right)]
    [InlineData(500, Canvas2DSpatialResizeEdge.Right)]
    public async Task ResizePreviewKeepsConnectedDiagramAndPersistentPipelineIdleUntilOneRelease(
        int nodeCount, Canvas2DSpatialResizeEdge edge)
    {
        var source = await BpmnModelerTestComposition.CreateDemoAsync();
        var composition = new DocumentCanvasComposition(CreateDocument(nodeCount), source.Configuration,
            source.PropertiesSchemaCatalog, source.Counters, source.ToolboxPlacementCatalog,
            spatialEditPlanners: source.SpatialEditPlanners);
        await using var test = await Fixture.CreateAsync(composition);
        await using var controller = test.CreateInteractionController();
        Assert.Equal(nodeCount, test.State.ProjectedGraph!.NodeCount);
        Assert.Equal(nodeCount / 2, test.State.ProjectedGraph.EdgeCount);
        var samples = new List<object>();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var initial = test.State;
            var snapshot = test.Snapshot;
            var routes = snapshot.VisualModel.RoutingScopes!.Value.Single().Connectors;
            var target = initial.CurrentScene!.SpatialPresentationPlan!.ResizeTargets.First(target => target.Edge == edge);
            var origin = edge == Canvas2DSpatialResizeEdge.Bottom
                ? new PointD(target.PaintedBounds.Left + 12d, target.PaintedBounds.Bottom)
                : new PointD(target.PaintedBounds.Right, target.PaintedBounds.Top + 12d);
            PointD Point(double displacement) => origin + (edge == Canvas2DSpatialResizeEdge.Bottom
                ? new VectorD(0d, displacement) : new VectorD(displacement, 0d));
            Canvas2DPointerInput Input(double displacement, bool held = true) => new(1,
                test.State.CurrentScene!.ViewportTransform.TransformPoint(Point(displacement)), buttons: held ? 1 : 0);

            async Task Measure(string operation, Func<ValueTask<Canvas2DInteractionResult>> action,
                bool commit = false, bool? allowed = null)
            {
                var state = test.State;
                var full = test.Pipeline.FullRuns;
                var scenes = test.Pipeline.Rebuilds;
                var contributions = test.Contributions.Sum(item => item.Calls + item.Routes);
                var bounded = test.Pipeline.SpatialResizeReuses;
                var uploads = test.Execution.FullUploadCount;
                var replacements = test.Execution.ViewportRenderCount;
                var events = test.Events.Count;
                var allocated = GC.GetTotalAllocatedBytes();
                var started = Stopwatch.GetTimestamp();
                var result = await action();
                await test.Session.WaitForIdleAsync();
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var allocation = GC.GetTotalAllocatedBytes() - allocated;
                var current = test.State;
                Assert.True(current.IsCurrentScenePresented);
                if (allowed is { } expected)
                    Assert.Equal(expected, Assert.Single(current.EditorState.TemporaryFeedback,
                        feedback => feedback.SpatialResize is not null).SpatialResize!.IsAllowed);
                if (!commit)
                {
                    Assert.Same(snapshot, test.Snapshot);
                    Assert.Equal(state.HistoryStatus, current.HistoryStatus);
                    Assert.Equal(events, test.Events.Count);
                    Assert.Equal(full, test.Pipeline.FullRuns);
                    Assert.Equal(scenes, test.Pipeline.Rebuilds);
                    Assert.Equal(contributions, test.Contributions.Sum(item => item.Calls + item.Routes));
                    Assert.Equal(uploads, test.Execution.FullUploadCount);
                    Assert.Same(initial.ProjectedGraph, current.ProjectedGraph);
                    Assert.Same(initial.LayoutResult, current.LayoutResult);
                    Assert.Same(initial.RoutingResult, current.RoutingResult);
                }
                else
                {
                    Assert.True(result.Status == Canvas2DInteractionStatus.Committed,
                        string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
                    Assert.Equal(state.DocumentRevision.Increment(), current.DocumentRevision);
                    Assert.Equal(state.HistoryStatus.EntryCount + 1, current.HistoryStatus.EntryCount);
                    Assert.Equal(events + 1, test.Events.Count);
                    Assert.Null(current.EditorState.ActiveGesture);
                    Assert.Empty(current.EditorState.TemporaryFeedback);
                    if (edge == Canvas2DSpatialResizeEdge.Right)
                        Assert.Equal(routes.AsEnumerable(), test.Snapshot.VisualModel.RoutingScopes!.Value.Single().Connectors.AsEnumerable());
                }
                samples.Add(new
                {
                    repeat,
                    operation,
                    elapsedMs = elapsed,
                    allocatedBytes = allocation,
                    commandsCommitted = test.Events.Count - events,
                    revisions = current.DocumentRevision.Value - state.DocumentRevision.Value,
                    history = current.HistoryStatus.EntryCount - state.HistoryStatus.EntryCount,
                    persistentPipeline = test.Pipeline.FullRuns - full,
                    completeScenes = test.Pipeline.Rebuilds - scenes,
                    contributorCalls = test.Contributions.Sum(item => item.Calls + item.Routes) - contributions,
                    boundedCompositions = test.Pipeline.SpatialResizeReuses - bounded,
                    fullUploads = test.Execution.FullUploadCount - uploads,
                    boundedUploads = test.Execution.ViewportRenderCount - replacements,
                    payloadBytes = test.Execution.FullUploadCount > uploads
                        ? JsonSerializer.SerializeToUtf8Bytes(test.Execution.LastContent).Length
                        : test.Execution.ViewportRenderCount > replacements
                            ? JsonSerializer.SerializeToUtf8Bytes(test.Execution.LastViewport).Length : 0,
                });
            }

            await Measure("hover", () => controller.PointerMovedAsync(Input(0d, false)), allowed: true);
            await Measure("acquisition", () => controller.PointerPressedAsync(Input(0d)), allowed: true);
            await Measure("first-preview", () => controller.PointerMovedAsync(Input(10d)), allowed: true);
            for (var index = 0; index < 20; index++)
                await Measure("steady-valid", () => controller.PointerMovedAsync(Input(11d + index)), allowed: true);
            await Measure("valid-to-invalid", () => controller.PointerMovedAsync(Input(target.Constraints.Minimum - target.AuthoredExtent - 1d)), allowed: false);
            await Measure("invalid-to-valid", () => controller.PointerMovedAsync(Input(25d)), allowed: true);
            await Measure("cancel", () => controller.CancelActiveGestureAsync());
            await controller.PointerPressedAsync(Input(0d));
            await controller.PointerMovedAsync(Input(25d));
            await Measure("release-ready", () => controller.PointerReleasedAsync(Input(30d, false)), commit: true);
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            nodeCount,
            connectorCount = nodeCount / 2,
            axis = edge.ToString(),
            boundary = "Managed input through renderer acknowledgement; recording renderer, no browser pixel timing.",
            allocationScope = "Process-wide allocation delta; includes any concurrent test activity.",
            samples,
        }));
    }

    private static Document CreateDocument(int count)
    {
        var id = new DocumentId($"a1216:resize-measure:{count}");
        var elements = new List<SemanticElementSnapshot>();
        var visuals = new List<VisualStateSnapshot>();
        var flows = new List<SemanticRelationshipSnapshot>();
        var assignments = new List<ModelProfileElementAssignmentSnapshot>();
        var pools = new[] { new SemanticElementId("a1216:resize:pool:a"), new SemanticElementId("a1216:resize:pool:b") };
        elements.AddRange(pools.Select(pool => OrganizationalSemanticFactory.CreatePool(pool, pool.Value)));
        for (var index = 0; index < count; index++)
        {
            var semantic = new SemanticElementId($"a1216:resize:task:{index:D4}");
            elements.Add(BpmnSemanticFactory.CreateTask(semantic, $"TASK_{index}", $"Task {index}", index));
            visuals.Add(new VisualStateSnapshot(new VisualStateId(semantic.Value + ":visual"), semantic,
                new PointD(100d + index % 10 * 180d, 100d + index / 10 * 160d), new SizeD(120d, 80d), VisualPlacementMode.Pinned));
            assignments.Add(new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, semantic, pools[index / 10 % 2]));
            if (index % 2 == 1)
            {
                var flow = new SemanticElementId($"a1216:resize:flow:{index:D4}");
                flows.Add(BpmnSemanticFactory.CreateSequenceFlow(flow, new SemanticElementId($"a1216:resize:task:{index - 1:D4}"), semantic));
                visuals.Add(new VisualStateSnapshot(new VisualStateId(flow.Value + ":visual"), flow,
                    new PointD(0d, 0d), new SizeD(0d, 0d), VisualPlacementMode.Automatic));
            }
        }
        return Assert.IsType<Document>(DocumentFactory.Create(new DocumentSnapshot(
            new SemanticModelSnapshot(id, DocumentRevision.Zero, elements, flows,
                modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]), profileAssignments: assignments),
            new VisualModelSnapshot(id, DocumentRevision.Zero, visuals), new DocumentMetadataSnapshot(id, DocumentRevision.Zero))).Document);
    }
}
