using System.Diagnostics;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Xunit.Abstractions;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA1214PlacementMeasurementsIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(500)]
    public async Task PlacementMovementScalesByBodyScanWhileKeepingPersistentPipelineAndUploadsIdle(int nodeCount)
    {
        var source = await BpmnModelerTestComposition.CreateDemoAsync();
        var document = CreateDocument(nodeCount);
        var composition = new DocumentCanvasComposition(document, source.Configuration, source.PropertiesSchemaCatalog,
            source.Counters, source.ToolboxPlacementCatalog, spatialEditPlanners: source.SpatialEditPlanners);
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync(composition);
        var selection = new ToolboxSelectionState();
        var controller = new ToolboxPlacementController(source.ToolboxPlacementCatalog, selection,
            new NoIdentityProvider(), source.SpatialEditPlanners);
        var before = test.State;
        var full = test.Pipeline.FullRuns;
        var rebuilds = test.Pipeline.Rebuilds;
        var uploads = test.Execution.FullUploadCount;
        var contributions = test.Contributions.Where(item => item.PlacementDependency != Canvas2DScenePlacementDependency.BoundedFeedbackOnly)
            .Sum(item => item.Calls + item.Routes);
        var regions = before.CurrentScene!.SpatialPresentationPlan!.Regions.OrderBy(region => region.Bounds.Top).ToArray();
        Assert.Equal(3, regions.Length);
        Assert.Equal(nodeCount, before.ProjectedGraph!.NodeCount);
        var samples = new List<Sample>();
        var armed = Stopwatch.GetTimestamp();
        var armingReuses = test.Pipeline.PlacementReuses;
        selection.Select(new ToolboxItemId("bpmn:toolbox:manual-task"));
        var armingMs = Stopwatch.GetElapsedTime(armed).TotalMilliseconds;
        var armingCompositions = test.Pipeline.PlacementReuses - armingReuses;

        async Task Measure(string operation, PointD point, bool allowed)
        {
            var reuses = test.Pipeline.PlacementReuses;
            var compositionElapsed = test.Pipeline.PlacementCompositionElapsed;
            var renders = test.Execution.ViewportRenderCount;
            var started = Stopwatch.GetTimestamp();
            await controller.UpdatePreviewAtCssPointAsync(test.Session, test.State.CurrentScene!.ViewportTransform.TransformPoint(point));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var preview = Assert.Single(test.State.EditorState.TemporaryFeedback, feedback => feedback.PlacementPreview is not null);
            Assert.Equal(allowed, preview.PlacementPreview!.IsAllowed);
            Assert.True(test.State.IsCurrentScenePresented);
            var metrics = Assert.IsType<ToolboxPlacementEvaluationMetrics>(controller.LastEvaluationMetrics);
            Assert.Equal(nodeCount, metrics.ApplicableBodyCount);
            var viewportRenders = test.Execution.ViewportRenderCount - renders;
            samples.Add(new(operation, elapsed, metrics.EvaluationElapsed.TotalMilliseconds,
                metrics.ProviderElapsed.TotalMilliseconds, metrics.CollisionElapsed.TotalMilliseconds,
                (test.Pipeline.PlacementCompositionElapsed - compositionElapsed).TotalMilliseconds,
                metrics.CollisionBodiesConsidered, metrics.AllocatedBytes,
                test.Pipeline.PlacementReuses - reuses, viewportRenders,
                test.State.CurrentScene!.BoundedPresentation!.Items.Length,
                viewportRenders == 0 || test.Execution.LastViewport is null ? 0 : JsonSerializer.SerializeToUtf8Bytes(test.Execution.LastViewport).Length));
        }

        await Measure("cold-preview", regions[0].MapLocalToScene(new PointD(340d, 60d)), true);
        for (var index = 0; index < 30; index++)
        {
            await Measure("valid-move", regions[0].MapLocalToScene(new PointD(341d + index, 60d)), true);
        }
        for (var index = 0; index < 20; index++)
        {
            await Measure("valid-to-invalid", regions[0].MapLocalToScene(new PointD(160d, 140d)), false);
            await Measure("invalid-to-valid", regions[0].MapLocalToScene(new PointD(340d, 60d)), true);
        }
        for (var index = 0; index < 30; index++)
        {
            await Measure("same-position", regions[0].MapLocalToScene(new PointD(340d, 60d)), true);
            Assert.Equal(0, samples[^1].BoundedReuses);
            Assert.Equal(0, samples[^1].ViewportRenders);
        }
        for (var index = 0; index < 20; index++)
        {
            await Measure("region-crossing", regions[1 + index % 2].MapLocalToScene(new PointD(340d, 60d)), true);
        }
        var retired = Stopwatch.GetTimestamp();
        var retirementReuses = test.Pipeline.PlacementReuses;
        var retirementCompositionStarted = test.Pipeline.PlacementCompositionElapsed;
        controller.Cancel();
        Assert.True(await ToolboxPlacementController.ClearPreviewAsync(test.Session));
        var retirementMs = Stopwatch.GetElapsedTime(retired).TotalMilliseconds;
        var retirementCompositions = test.Pipeline.PlacementReuses - retirementReuses;
        var retirementCompositionMs = (test.Pipeline.PlacementCompositionElapsed - retirementCompositionStarted).TotalMilliseconds;
        Assert.Equal(full, test.Pipeline.FullRuns);
        Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(contributions, test.Contributions.Where(item => item.PlacementDependency != Canvas2DScenePlacementDependency.BoundedFeedbackOnly)
            .Sum(item => item.Calls + item.Routes));
        Assert.Same(before.ProjectedGraph, test.State.ProjectedGraph);
        Assert.Same(before.LayoutResult, test.State.LayoutResult);
        Assert.Same(before.RoutingResult, test.State.RoutingResult);
        Assert.Equal(before.DocumentRevision, test.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
        Assert.Equal(0, test.Events.Count);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            nodeCount,
            armingMs,
            armingCompositions,
            retirementMs,
            retirementCompositions,
            retirementCompositionMs,
            sampleCount = samples.Count,
            boundaries = "Managed controller call to completed renderer acknowledgement; recording renderer, not browser Canvas pixels.",
            fullRuns = test.Pipeline.FullRuns - full,
            fullSceneRebuilds = test.Pipeline.Rebuilds - rebuilds,
            fullUploads = test.Execution.FullUploadCount - uploads,
            samples,
        }));
    }

    private static Document CreateDocument(int count)
    {
        var id = new DocumentId($"a1214:measure:{count}");
        var elements = new List<SemanticElementSnapshot>();
        var visuals = new List<VisualStateSnapshot>();
        var assignments = new List<ModelProfileElementAssignmentSnapshot>();
        var pools = new[] { new SemanticElementId("a1214:pool:a"), new SemanticElementId("a1214:pool:b") };
        elements.AddRange(pools.Select(pool => OrganizationalSemanticFactory.CreatePool(pool, pool.Value)));
        for (var index = 0; index < count; index++)
        {
            var semantic = new SemanticElementId($"a1214:task:{index:D4}");
            elements.Add(BpmnSemanticFactory.CreateTask(semantic, $"TASK_{index}", $"Task {index}", index));
            visuals.Add(new VisualStateSnapshot(new VisualStateId(semantic.Value + ":visual"), semantic,
                new PointD(100d + index % 25 * 180d, 100d + index / 25 * 160d), new SizeD(120d, 80d), VisualPlacementMode.Pinned));
            if (index % 3 < 2)
            {
                assignments.Add(new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, semantic, pools[index % 3]));
            }
        }
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(id, DocumentRevision.Zero, elements,
                modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]), profileAssignments: assignments),
            new VisualModelSnapshot(id, DocumentRevision.Zero, visuals), new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private sealed record Sample(string Operation, double CallbackReadyMs, double EvaluationMs, double ProviderMs,
        double CollisionMs, double BoundedCompositionMs, int BodiesConsidered, long EvaluationAllocatedBytes, int BoundedReuses, int ViewportRenders,
        int BoundedItems, int PayloadBytes);

    private sealed class NoIdentityProvider : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => throw new InvalidOperationException("Pointer-only input must not allocate persistent identities.");
    }
}
