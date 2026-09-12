using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN1ToolboxPlacementIntegrationTests
{
    private static readonly PointD PlacementCenter = new(500d, 300d);

    [Theory]
    [MemberData(nameof(BpmnNodeCases))]
    public async Task EveryBpmnNodeToolCreatesOneDisconnectedPinnedNodeThroughTheFullSession(
        SemanticTypeId semanticTypeId,
        double width,
        double height,
        Canvas2DSceneGeometryKind bodyGeometry,
        string? markerPrefix)
    {
        var identity = Identity($"all-types:{semanticTypeId.Value}");
        await using var harness = await PlacementHarness.CreateAsync(
            identities: new SequenceIdentityProvider(identity));
        var item = harness.Item(semanticTypeId);
        var beforeDocument = harness.Composition.Document.CaptureSnapshot();
        var beforeState = harness.Session.CaptureState();
        var beforeEvents = harness.Events.Events.Count;
        var beforeRenders = harness.Execution.RenderCount;
        var beforeFullRuns = harness.Pipeline.FullRunCount;
        var beforePreservingRuns = harness.Pipeline.NodeLayoutPreservingRunCount;
        var beforeSceneRuns = harness.Pipeline.SceneOnlyRunCount;
        var expectedTaskNumber = semanticTypeId == BpmnSemanticTypes.Task
            ? MaximumTaskElementNumber(beforeDocument) + 1L
            : 0L;

        Assert.True(harness.Selection.Select(item.ItemId));
        Assert.Equal(item.ItemId, harness.Controller.ActiveItemId);
        Assert.Equal(beforeDocument, harness.Composition.Document.CaptureSnapshot());
        Assert.Same(beforeState.CurrentScene, harness.Session.CaptureState().CurrentScene);
        Assert.Equal(beforeRenders, harness.Execution.RenderCount);
        Assert.Equal(beforeFullRuns, harness.Pipeline.FullRunCount);
        Assert.Equal(beforePreservingRuns, harness.Pipeline.NodeLayoutPreservingRunCount);
        Assert.Equal(beforeSceneRuns, harness.Pipeline.SceneOnlyRunCount);

        var cssPoint = Assert.IsType<Canvas2DScene>(beforeState.CurrentScene)
            .ViewportTransform.TransformPoint(PlacementCenter);
        var placement = await harness.Controller.TryPlaceAtCssPointAsync(
            harness.Session,
            cssPoint);
        await harness.WaitForIdleAsync();

        Assert.True(placement.Handled);
        Assert.True(placement.IsCommitted);
        Assert.Equal(identity.VisualStateId, placement.CreatedVisualStateId);
        Assert.Empty(placement.Diagnostics);
        Assert.False(harness.Controller.IsPlacementActive);
        Assert.Null(harness.Selection.SelectedItemId);

        var afterDocument = harness.Composition.Document.CaptureSnapshot();
        var afterState = harness.Session.CaptureState();
        var createdElement = Assert.Single(afterDocument.SemanticModel.Elements, element =>
            element.Id == identity.SemanticElementId);
        var createdVisual = Assert.Single(afterDocument.VisualModel.VisualStates, visual =>
            visual.Id == identity.VisualStateId);
        var expectedPosition = new PointD(
            PlacementCenter.X - (width / 2d),
            PlacementCenter.Y - (height / 2d));

        Assert.Equal(beforeDocument.Revision.Increment(), afterDocument.Revision);
        Assert.Equal(beforeDocument.SemanticModel.ElementCount + 1,
            afterDocument.SemanticModel.ElementCount);
        Assert.Equal(beforeDocument.VisualModel.Count + 1, afterDocument.VisualModel.Count);
        Assert.Equal(
            beforeDocument.SemanticModel.Relationships.AsEnumerable(),
            afterDocument.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(semanticTypeId, createdElement.TypeId);
        Assert.Equal(identity.SemanticElementId, createdVisual.SemanticElementId);
        Assert.Equal(expectedPosition, createdVisual.Position);
        Assert.Equal(new SizeD(width, height), createdVisual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, createdVisual.PlacementMode);
        Assert.Empty(createdVisual.ConnectorAnchors);
        Assert.Empty(createdVisual.Route);
        Assert.Null(createdVisual.SourceAnchorId);
        Assert.Null(createdVisual.TargetAnchorId);
        Assert.False(NodeLabelVisualOverride.TryRead(
            createdVisual.Properties,
            out _));

        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1,
            afterState.HistoryStatus.EntryCount);
        Assert.True(afterState.HistoryStatus.CanUndo);
        Assert.False(afterState.HistoryStatus.CanRedo);
        Assert.Equal(identity.VisualStateId,
            Assert.Single(afterState.EditorState.Selection));
        Assert.Equal(beforeEvents + 1, harness.Events.Events.Count);
        Assert.Equal(afterDocument.Revision,
            harness.Events.Events.Last().CommittedRevision);
        Assert.Equal(beforeFullRuns, harness.Pipeline.FullRunCount);
        Assert.Equal(beforePreservingRuns + 1, harness.Pipeline.NodeLayoutPreservingRunCount);
        Assert.Equal(beforeSceneRuns + 1, harness.Pipeline.SceneOnlyRunCount);
        Assert.Equal(beforeRenders + 2, harness.Execution.RenderCount);
        Assert.NotSame(beforeState.ProjectedGraph, afterState.ProjectedGraph);
        Assert.NotSame(beforeState.LayoutResult, afterState.LayoutResult);
        Assert.NotSame(beforeState.RoutingResult, afterState.RoutingResult);
        foreach (var previousNode in beforeState.LayoutResult!.Nodes)
        {
            var currentNode = Assert.Single(afterState.LayoutResult!.Nodes, node =>
                node.ProjectedObjectId == previousNode.ProjectedObjectId);
            Assert.Equal(previousNode, currentNode);
            Assert.Same(previousNode, currentNode);
        }
        Assert.Equal(beforeState.ProjectedGraph!.NodeCount + 1,
            afterState.ProjectedGraph!.NodeCount);
        Assert.Equal(beforeState.ProjectedGraph.EdgeCount,
            afterState.ProjectedGraph.EdgeCount);
        Assert.Equal(beforeState.RoutingResult!.RouteCount,
            afterState.RoutingResult!.RouteCount);

        var scene = Assert.IsType<Canvas2DScene>(afterState.CurrentScene);
        var body = NodeBody(scene, identity.VisualStateId);
        Assert.Equal(new RectD(expectedPosition.X, expectedPosition.Y, width, height),
            body.Bounds);
        Assert.Equal(bodyGeometry, body.Geometry.Kind);
        if (markerPrefix is not null)
        {
            Assert.Single(scene.Items, sceneItem =>
                sceneItem.Origin.SemanticElementId == identity.SemanticElementId &&
                sceneItem.Origin.StableSourceKey?.StartsWith(
                    markerPrefix,
                    StringComparison.Ordinal) == true);
            var label = LabelInteractionBody(scene, identity.SemanticElementId);
            Assert.True(label.Bounds.Top >= body.Bounds.Bottom);
        }
        else if (semanticTypeId == BpmnSemanticTypes.Task)
        {
            var text = scene.Items.Where(sceneItem =>
                    sceneItem.Layer == Canvas2DSceneLayer.Label &&
                    sceneItem.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
                    sceneItem.Origin.SemanticElementId == identity.SemanticElementId)
                .ToArray();
            Assert.NotEmpty(text);
            Assert.All(text, line => Assert.True(body.Bounds.Contains(Center(line.Bounds))));
        }

        AssertSemanticDefaults(
            harness.Composition,
            createdElement,
            expectedTaskNumber);
    }

    [Theory]
    [InlineData(0.75d, -120d, 85d, 1d)]
    [InlineData(1d, 0d, 0d, 1.25d)]
    [InlineData(1.5d, 210d, -95d, 2d)]
    public async Task PlacementUsesCurrentInverseAfterInteractivePanAndIgnoresDevicePixelRatio(
        double zoom,
        double panX,
        double panY,
        double devicePixelRatio)
    {
        var identity = Identity($"coordinates:{zoom}:{devicePixelRatio}");
        await using var harness = await PlacementHarness.CreateAsync(
            devicePixelRatio,
            new SequenceIdentityProvider(identity));
        var viewportResult = await harness.Session.UpdateViewportAsync(
            new ViewportSnapshot(zoom, default));
        Assert.True(viewportResult.Succeeded);
        var panResult = await harness.Session.PanViewportAsync(new VectorD(panX, panY));
        Assert.True(panResult.Succeeded);
        var state = harness.Session.CaptureState();
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var cssPoint = scene.ViewportTransform.TransformPoint(PlacementCenter);
        Assert.True(harness.Selection.Select(harness.Item(BpmnSemanticTypes.Task).ItemId));

        var placement = await harness.Controller.TryPlaceAtCssPointAsync(
            harness.Session,
            cssPoint);
        await harness.WaitForIdleAsync();

        Assert.True(placement.IsCommitted);
        var visual = Assert.Single(
            harness.Composition.Document.VisualModel.VisualStates,
            candidate => candidate.Id == identity.VisualStateId);
        Assert.Equal(new PointD(440d, 260d), visual.Position);
        Assert.Equal(new SizeD(120d, 80d), visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Equal(devicePixelRatio, harness.SurfaceSize.DevicePixelRatio);
        Assert.Equal(
            new RectD(440d, 260d, 120d, 80d),
            NodeBody(
                Assert.IsType<Canvas2DScene>(harness.Session.CaptureState().CurrentScene),
                identity.VisualStateId).Bounds);
    }

    [Theory]
    [InlineData(18d, 18d, true)]
    [InlineData(17d, 18d, false)]
    [InlineData(18d, 17d, false)]
    [InlineData(-10d, -10d, false)]
    public async Task CenteredEventPlacementEnforcesPositiveDocumentBounds(
        double centerX,
        double centerY,
        bool expectedCommit)
    {
        var identity = Identity($"boundary:{centerX}:{centerY}");
        await using var harness = await PlacementHarness.CreateAsync(
            identities: new SequenceIdentityProvider(identity));
        var before = harness.Composition.Document.CaptureSnapshot();
        var beforeHistory = harness.Session.CaptureState().HistoryStatus;
        Assert.True(harness.Selection.Select(
            harness.Item(BpmnSemanticTypes.StartEvent).ItemId));
        var scene = Assert.IsType<Canvas2DScene>(
            harness.Session.CaptureState().CurrentScene);
        var center = new PointD(centerX, centerY);

        var placement = await harness.Controller.TryPlaceAtCssPointAsync(
            harness.Session,
            scene.ViewportTransform.TransformPoint(center));
        await harness.WaitForIdleAsync();

        Assert.True(placement.Handled);
        Assert.Equal(expectedCommit, placement.IsCommitted);
        if (!expectedCommit)
        {
            Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
            Assert.Equal(beforeHistory, harness.Session.CaptureState().HistoryStatus);
            Assert.Null(placement.CreatedVisualStateId);
            return;
        }

        var created = Assert.Single(
            harness.Composition.Document.VisualModel.VisualStates,
            visual => visual.Id == identity.VisualStateId);
        Assert.Equal(new PointD(0d, 0d), created.Position);
        Assert.Equal(new SizeD(36d, 36d), created.Size);
        var after = harness.Session.CaptureState();
        Assert.Equal(
            new RectD(0d, 0d, 36d, 36d),
            NodeBody(Assert.IsType<Canvas2DScene>(after.CurrentScene), created.Id).Bounds);
        Assert.Equal(beforeHistory.EntryCount + 1, after.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task OneShotPlacementUndoRedoAndExplicitSecondTaskPreserveExactIdentitiesAndBounds()
    {
        var firstIdentity = Identity("history:first-task");
        var secondIdentity = Identity("history:second-task");
        await using var harness = await PlacementHarness.CreateAsync(
            identities: new SequenceIdentityProvider(firstIdentity, secondIdentity));
        var taskItem = harness.Item(BpmnSemanticTypes.Task);
        var baseline = harness.Composition.Document.CaptureSnapshot();
        var baselineTaskNumber = MaximumTaskElementNumber(baseline);
        var firstCenter = new PointD(500d, 300d);
        var secondCenter = new PointD(760d, 420d);

        Assert.True(harness.Selection.Select(taskItem.ItemId));
        var first = await PlaceAsync(harness, firstCenter);
        Assert.True(first.IsCommitted);
        var committedFirst = harness.Composition.Document.CaptureSnapshot();
        var firstElement = Assert.Single(committedFirst.SemanticModel.Elements, element =>
            element.Id == firstIdentity.SemanticElementId);
        var firstVisual = Assert.Single(committedFirst.VisualModel.VisualStates, visual =>
            visual.Id == firstIdentity.VisualStateId);
        var afterFirstState = harness.Session.CaptureState();
        var renderAfterFirst = harness.Execution.RenderCount;
        var fullRunsAfterFirst = harness.Pipeline.FullRunCount;
        var sceneRunsAfterFirst = harness.Pipeline.SceneOnlyRunCount;

        var secondClickWithoutReselect = await PlaceAsync(harness, secondCenter);
        Assert.False(secondClickWithoutReselect.Handled);
        Assert.False(secondClickWithoutReselect.IsCommitted);
        Assert.Equal(committedFirst, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(renderAfterFirst, harness.Execution.RenderCount);
        Assert.Equal(fullRunsAfterFirst, harness.Pipeline.FullRunCount);
        Assert.Equal(sceneRunsAfterFirst, harness.Pipeline.SceneOnlyRunCount);

        var undo = await harness.Session.UndoAsync();
        await harness.WaitForIdleAsync();
        Assert.True(undo.IsCommitted);
        var undone = harness.Composition.Document.CaptureSnapshot();
        Assert.False(undone.SemanticModel.TryGetElement(firstIdentity.SemanticElementId, out _));
        Assert.False(undone.VisualModel.TryGetVisualState(firstIdentity.VisualStateId, out _));
        Assert.Empty(harness.Session.CaptureState().EditorState.Selection);
        Assert.False(harness.Controller.IsPlacementActive);

        var redo = await harness.Session.RedoAsync();
        await harness.WaitForIdleAsync();
        Assert.True(redo.IsCommitted);
        var redone = harness.Composition.Document.CaptureSnapshot();
        Assert.Equal(firstElement, Assert.Single(redone.SemanticModel.Elements, element =>
            element.Id == firstIdentity.SemanticElementId));
        Assert.Equal(firstVisual, Assert.Single(redone.VisualModel.VisualStates, visual =>
            visual.Id == firstIdentity.VisualStateId));
        Assert.Empty(harness.Session.CaptureState().EditorState.Selection);
        Assert.False(harness.Controller.IsPlacementActive);

        Assert.True(harness.Selection.Select(taskItem.ItemId));
        var second = await PlaceAsync(harness, secondCenter);
        Assert.True(second.IsCommitted);
        var twicePlaced = harness.Composition.Document.CaptureSnapshot();
        var secondElement = Assert.Single(twicePlaced.SemanticModel.Elements, element =>
            element.Id == secondIdentity.SemanticElementId);
        var secondVisual = Assert.Single(twicePlaced.VisualModel.VisualStates, visual =>
            visual.Id == secondIdentity.VisualStateId);

        Assert.NotEqual(firstIdentity.SemanticElementId, secondIdentity.SemanticElementId);
        Assert.NotEqual(firstIdentity.VisualStateId, secondIdentity.VisualStateId);
        Assert.Equal(baselineTaskNumber + 1L,
            RequiredProperty(firstElement, BpmnSemanticProperties.ElementNumber).IntegerValue);
        Assert.Equal(baselineTaskNumber + 2L,
            RequiredProperty(secondElement, BpmnSemanticProperties.ElementNumber).IntegerValue);
        Assert.Equal(new PointD(440d, 260d), firstVisual.Position);
        Assert.Equal(new PointD(700d, 380d), secondVisual.Position);
        Assert.Equal(VisualPlacementMode.Pinned, secondVisual.PlacementMode);
        Assert.Equal(baseline.Revision.Value + 4UL, twicePlaced.Revision.Value);
        Assert.Equal(afterFirstState.HistoryStatus.EntryCount + 1,
            harness.Session.CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task InvalidFactoriesRejectedCommandAndStalePlanRetainToolWithoutPersistentWork()
    {
        await using var harness = await PlacementHarness.CreateAsync();
        var taskItem = harness.Item(BpmnSemanticTypes.Task);
        var previousSelection = harness.Composition.Document.VisualModel.VisualStates[0].Id;
        var selected = await harness.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            [previousSelection],
            viewport: harness.Session.CaptureState().EditorState.Viewport));
        Assert.True(selected.Succeeded);
        var baseline = harness.Composition.Document.CaptureSnapshot();
        var baselineState = harness.Session.CaptureState();
        var baselineRenders = harness.Execution.RenderCount;
        var baselineFullRuns = harness.Pipeline.FullRunCount;
        var baselineSceneRuns = harness.Pipeline.SceneOnlyRunCount;

        var failureSelection = new ToolboxSelectionState();
        Assert.True(failureSelection.Select(taskItem.ItemId));
        var failureController = new ToolboxPlacementController(
            Catalog(taskItem.ItemId, new FailingFactory()),
            failureSelection,
            new SequenceIdentityProvider(Identity("failure:unused")));
        var cssPoint = Assert.IsType<Canvas2DScene>(baselineState.CurrentScene)
            .ViewportTransform.TransformPoint(PlacementCenter);

        var failed = await failureController.TryPlaceAtCssPointAsync(
            harness.Session,
            cssPoint);

        Assert.True(failed.Handled);
        Assert.False(failed.IsCommitted);
        Assert.Equal("TEST_TOOLBOX_FACTORY_REJECTED", Assert.Single(failed.Diagnostics).Code);
        Assert.Equal(taskItem.ItemId, failureController.ActiveItemId);
        Assert.Equal(taskItem.ItemId, failureSelection.SelectedItemId);
        Assert.Equal(baseline, harness.Composition.Document.CaptureSnapshot());
        Assert.Same(baselineState.EditorState, harness.Session.CaptureState().EditorState);
        Assert.Equal(previousSelection,
            Assert.Single(harness.Session.CaptureState().EditorState.Selection));

        var nullSelection = new ToolboxSelectionState();
        Assert.True(nullSelection.Select(taskItem.ItemId));
        var nullController = new ToolboxPlacementController(
            Catalog(taskItem.ItemId, new NullResultFactory()),
            nullSelection,
            new SequenceIdentityProvider(Identity("failure:null-unused")));
        var invalid = await nullController.TryPlaceAtCssPointAsync(
            harness.Session,
            cssPoint);

        Assert.True(invalid.Handled);
        Assert.False(invalid.IsCommitted);
        Assert.Contains(invalid.Diagnostics, diagnostic =>
            diagnostic.Code == "TOOLBOX_PLACEMENT_FACTORY_FAILED");
        Assert.Equal(taskItem.ItemId, nullController.ActiveItemId);
        Assert.Equal(taskItem.ItemId, nullSelection.SelectedItemId);
        Assert.Equal(baseline, harness.Composition.Document.CaptureSnapshot());
        Assert.Same(baselineState.EditorState, harness.Session.CaptureState().EditorState);

        var rejectedIdentity = Identity("failure:command-rejected");
        var rejectedSelection = new ToolboxSelectionState();
        Assert.True(rejectedSelection.Select(taskItem.ItemId));
        var rejectedController = new ToolboxPlacementController(
            Catalog(taskItem.ItemId, new UnsupportedCommandFactory(rejectedIdentity)),
            rejectedSelection,
            new SequenceIdentityProvider(rejectedIdentity));
        var rejected = await rejectedController.TryPlaceAtCssPointAsync(
            harness.Session,
            cssPoint);

        Assert.True(rejected.Handled);
        Assert.False(rejected.IsCommitted);
        Assert.NotEmpty(rejected.Diagnostics);
        Assert.Equal(taskItem.ItemId, rejectedController.ActiveItemId);
        Assert.Equal(taskItem.ItemId, rejectedSelection.SelectedItemId);
        Assert.Equal(baseline, harness.Composition.Document.CaptureSnapshot());
        Assert.Same(baselineState.EditorState, harness.Session.CaptureState().EditorState);

        var staleIdentity = Identity("failure:stale");
        var staleSelection = new ToolboxSelectionState();
        Assert.True(staleSelection.Select(taskItem.ItemId));
        var staleController = new ToolboxPlacementController(
            Catalog(taskItem.ItemId, new StalePlanFactory(staleIdentity)),
            staleSelection,
            new SequenceIdentityProvider(staleIdentity));
        var stale = await staleController.TryPlaceAtCssPointAsync(
            harness.Session,
            cssPoint);

        Assert.True(stale.Handled);
        Assert.False(stale.IsCommitted);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == "TOOLBOX_PLACEMENT_STALE");
        Assert.Equal(taskItem.ItemId, staleController.ActiveItemId);
        Assert.Equal(taskItem.ItemId, staleSelection.SelectedItemId);
        Assert.Equal(baseline, harness.Composition.Document.CaptureSnapshot());
        Assert.Same(baselineState.EditorState, harness.Session.CaptureState().EditorState);
        Assert.Equal(baselineRenders, harness.Execution.RenderCount);
        Assert.Equal(baselineFullRuns, harness.Pipeline.FullRunCount);
        Assert.Equal(baselineSceneRuns, harness.Pipeline.SceneOnlyRunCount);
    }

    [Fact]
    public async Task RuntimeFaultedLastKnownGoodSceneCannotAuthorizePlacement()
    {
        await using var harness = await PlacementHarness.CreateAsync(failFullRunNumber: 2);
        var existing = harness.Composition.Document.VisualModel.VisualStates[0];
        var move = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            harness.Composition.Document.DocumentId,
            harness.Composition.Document.Revision,
            existing.Id,
            new PointD(existing.Position.X + 15d, existing.Position.Y + 10d)));
        await harness.WaitForIdleAsync();
        Assert.True(move.IsCommitted);
        var faulted = harness.Session.CaptureState();
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, faulted.Status);
        Assert.Null(faulted.CurrentScene);
        Assert.NotNull(faulted.LastKnownGoodScene);
        var beforePlacement = harness.Composition.Document.CaptureSnapshot();
        var historyBefore = faulted.HistoryStatus;
        var renderBefore = harness.Execution.RenderCount;
        var fullRunsBefore = harness.Pipeline.FullRunCount;
        var sceneRunsBefore = harness.Pipeline.SceneOnlyRunCount;
        var item = harness.Item(BpmnSemanticTypes.StartEvent);
        Assert.True(harness.Selection.Select(item.ItemId));

        var placement = await harness.Controller.TryPlaceAtCssPointAsync(
            harness.Session,
            new PointD(500d, 300d));

        Assert.True(placement.Handled);
        Assert.False(placement.IsCommitted);
        Assert.Contains(placement.Diagnostics, diagnostic =>
            diagnostic.Code == "TOOLBOX_PLACEMENT_UNAVAILABLE");
        Assert.Equal(item.ItemId, harness.Controller.ActiveItemId);
        Assert.Equal(item.ItemId, harness.Selection.SelectedItemId);
        Assert.Equal(beforePlacement, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(historyBefore, harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(renderBefore, harness.Execution.RenderCount);
        Assert.Equal(fullRunsBefore, harness.Pipeline.FullRunCount);
        Assert.Equal(sceneRunsBefore, harness.Pipeline.SceneOnlyRunCount);
    }

    [Fact]
    public async Task EmptyHistoricalPlacementCatalogKeepsToolboxSelectionOnly()
    {
        await using var harness = await PlacementHarness.CreateAsync();
        var selection = new ToolboxSelectionState();
        var taskItem = harness.Item(BpmnSemanticTypes.Task);
        Assert.True(selection.Select(taskItem.ItemId));
        var controller = new ToolboxPlacementController(
            ToolboxPlacementCatalog.Empty,
            selection,
            new SequenceIdentityProvider(Identity("historical:unused")));
        var before = harness.Composition.Document.CaptureSnapshot();
        var beforeState = harness.Session.CaptureState();
        var beforeRenders = harness.Execution.RenderCount;

        var placement = await controller.TryPlaceAtCssPointAsync(
            harness.Session,
            new PointD(-1000d, -1000d));
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var ordinaryClick = await interaction.PointerActivatedAsync(
            new PointD(-1000d, -1000d));
        await harness.WaitForIdleAsync();

        Assert.False(placement.Handled);
        Assert.False(placement.IsCommitted);
        Assert.False(controller.IsPlacementActive);
        Assert.Equal(taskItem.ItemId, selection.SelectedItemId);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, ordinaryClick.Status);
        Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforeState.HistoryStatus,
            harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(beforeRenders, harness.Execution.RenderCount);
    }

    [Fact]
    public async Task HostPrimaryDownPlacesBeforeHitTestingAndConsumesThePointerBoundary()
    {
        await using var harness = await HostHarness.CreateAsync();
        var toolboxSelection = HostToolboxSelection(harness.Host);
        var toolbox = new ToolboxCatalog(BpmnPluginRegistration.N1.ToolboxContributions);
        var taskItem = Assert.Single(toolbox.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.Task);
        var existingVisualId = harness.Composition.Document.VisualModel.VisualStates[0].Id;
        var existingBody = NodeBody(harness.Scene, existingVisualId);
        var clickCenter = Center(existingBody.Bounds);
        var selected = await harness.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            [existingVisualId],
            viewport: harness.State.EditorState.Viewport));
        Assert.True(selected.Succeeded);
        var before = harness.Composition.Document.CaptureSnapshot();
        var beforeRenders = harness.Execution.RenderCount;

        Assert.True(toolboxSelection.Select(taskItem.ItemId));
        await harness.Host.RefreshToolboxPlacementAsync();
        Assert.Equal("crosshair", harness.Host.CaptureState().CssCursor);
        Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforeRenders, harness.Execution.RenderCount);

        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.Down,
            pointerId: 8100,
            clickCenter,
            button: 0,
            buttons: 1);
        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.Up,
            pointerId: 8100,
            clickCenter,
            button: 0,
            buttons: 0);
        await harness.Session.WaitForIdleAsync();

        var after = harness.Composition.Document.CaptureSnapshot();
        var createdTask = Assert.Single(after.SemanticModel.Elements, element =>
            element.TypeId == BpmnSemanticTypes.Task &&
            !before.SemanticModel.Elements.Any(previous => previous.Id == element.Id));
        var createdVisual = Assert.Single(after.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == createdTask.Id);
        Assert.Equal(
            new PointD(clickCenter.X - 60d, clickCenter.Y - 40d),
            createdVisual.Position);
        Assert.Equal(VisualPlacementMode.Pinned, createdVisual.PlacementMode);
        Assert.Equal(createdVisual.Id,
            Assert.Single(harness.State.EditorState.Selection));
        Assert.NotEqual(existingVisualId,
            Assert.Single(harness.State.EditorState.Selection));
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal("default", harness.Host.CaptureState().CssCursor);

        var afterPlacement = harness.Composition.Document.CaptureSnapshot();
        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.Down,
            pointerId: 8101,
            new PointD(-500d, -500d),
            button: 0,
            buttons: 1);
        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.Up,
            pointerId: 8101,
            new PointD(-500d, -500d),
            button: 0,
            buttons: 0);
        Assert.Equal(afterPlacement, harness.Composition.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task HostRightClickCancelsWithoutCreatingAndReplacementToolCreatesOnce()
    {
        await using var harness = await HostHarness.CreateAsync();
        var toolboxSelection = HostToolboxSelection(harness.Host);
        var toolbox = new ToolboxCatalog(BpmnPluginRegistration.N1.ToolboxContributions);
        var taskItem = Assert.Single(toolbox.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.Task);
        var parallelItem = Assert.Single(toolbox.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.ParallelGateway);
        var before = harness.Composition.Document.CaptureSnapshot();
        var clickCenter = new PointD(730d, 530d);

        Assert.True(toolboxSelection.Select(taskItem.ItemId));
        await harness.Host.RefreshToolboxPlacementAsync();
        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.ContextMenu,
            pointerId: 8200,
            clickCenter,
            button: 2,
            buttons: 0);
        Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal("default", harness.Host.CaptureState().CssCursor);

        Assert.True(toolboxSelection.Select(parallelItem.ItemId));
        await harness.Host.RefreshToolboxPlacementAsync();
        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.Down,
            pointerId: 8201,
            clickCenter,
            button: 0,
            buttons: 1);
        await SendPointerAsync(
            harness,
            CanvasPointerEventKind.Up,
            pointerId: 8201,
            clickCenter,
            button: 0,
            buttons: 0);
        await harness.Session.WaitForIdleAsync();

        var after = harness.Composition.Document.CaptureSnapshot();
        Assert.Empty(after.SemanticModel.Elements.Where(element =>
            element.TypeId == BpmnSemanticTypes.Task &&
            !before.SemanticModel.Elements.Any(previous => previous.Id == element.Id)));
        var gateway = Assert.Single(after.SemanticModel.Elements, element =>
            element.TypeId == BpmnSemanticTypes.ParallelGateway &&
            !before.SemanticModel.Elements.Any(previous => previous.Id == element.Id));
        var visual = Assert.Single(after.VisualModel.VisualStates, candidate =>
            candidate.SemanticElementId == gateway.Id);
        Assert.Equal(new PointD(clickCenter.X - 24d, clickCenter.Y - 24d), visual.Position);
        Assert.Empty(visual.ConnectorAnchors);
        Assert.Equal(visual.Id, Assert.Single(harness.State.EditorState.Selection));
        Assert.Null(toolboxSelection.SelectedItemId);

        Assert.True(toolboxSelection.Select(taskItem.ItemId));
        await harness.Host.RefreshToolboxPlacementAsync();
        await harness.Host.CancelToolboxPlacementAsync();
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal("default", harness.Host.CaptureState().CssCursor);
        Assert.Equal(after, harness.Composition.Document.CaptureSnapshot());
    }

    public static TheoryData<SemanticTypeId, double, double, Canvas2DSceneGeometryKind, string?>
        BpmnNodeCases => new()
        {
            {
                BpmnSemanticTypes.StartEvent,
                36d,
                36d,
                Canvas2DSceneGeometryKind.Ellipse,
                null
            },
            {
                BpmnSemanticTypes.Task,
                120d,
                80d,
                Canvas2DSceneGeometryKind.Path,
                null
            },
            {
                BpmnSemanticTypes.ExclusiveGateway,
                48d,
                48d,
                Canvas2DSceneGeometryKind.Path,
                "exclusive-gateway-x:"
            },
            {
                BpmnSemanticTypes.ParallelGateway,
                48d,
                48d,
                Canvas2DSceneGeometryKind.Path,
                "parallel-gateway-plus:"
            },
            {
                BpmnSemanticTypes.InclusiveGateway,
                48d,
                48d,
                Canvas2DSceneGeometryKind.Path,
                "inclusive-gateway-o:"
            },
            {
                BpmnSemanticTypes.EndEvent,
                36d,
                36d,
                Canvas2DSceneGeometryKind.Ellipse,
                null
            },
        };

    private static async ValueTask<ToolboxPlacementControllerResult> PlaceAsync(
        PlacementHarness harness,
        PointD documentPoint)
    {
        var scene = Assert.IsType<Canvas2DScene>(harness.Session.CaptureState().CurrentScene);
        var result = await harness.Controller.TryPlaceAtCssPointAsync(
            harness.Session,
            scene.ViewportTransform.TransformPoint(documentPoint));
        await harness.WaitForIdleAsync();
        return result;
    }

    private static void AssertSemanticDefaults(
        DocumentCanvasComposition composition,
        SemanticElementSnapshot element,
        long expectedTaskNumber)
    {
        if (element.TypeId == BpmnSemanticTypes.StartEvent ||
            element.TypeId == BpmnSemanticTypes.EndEvent)
        {
            Assert.Empty(element.Properties);
            Assert.False(composition.PropertiesSchemaCatalog.TryGetSchema(
                element.TypeId,
                out _));
            return;
        }

        var code = RequiredProperty(element, BpmnSemanticProperties.Code);
        var name = RequiredProperty(element, BpmnSemanticProperties.Name);
        var description = RequiredProperty(element, BpmnSemanticProperties.Description);
        Assert.Equal(PropertyValueKind.Text, code.Kind);
        Assert.Equal(PropertyValueKind.Text, name.Kind);
        Assert.Equal(PropertyValueKind.Text, description.Kind);
        Assert.True(composition.PropertiesSchemaCatalog.TryGetSchema(
            element.TypeId,
            out var schema));
        Assert.NotNull(schema);

        if (element.TypeId == BpmnSemanticTypes.Task)
        {
            var number = RequiredProperty(element, BpmnSemanticProperties.ElementNumber);
            Assert.Equal(PropertyValueKind.Integer, number.Kind);
            Assert.Equal(expectedTaskNumber, number.IntegerValue);
            Assert.Equal($"TASK_{expectedTaskNumber}", code.TextValue);
            Assert.Equal($"Task {expectedTaskNumber}", name.TextValue);
            Assert.Equal(
                $"Task {expectedTaskNumber} created from the Toolbox.",
                description.TextValue);
            Assert.Equal(
                [
                    BpmnSemanticProperties.Code,
                    BpmnSemanticProperties.Name,
                    BpmnSemanticProperties.ElementNumber,
                    BpmnSemanticProperties.Description,
                ],
                schema.Fields.Select(field => field.SemanticPropertyKey));
            return;
        }

        Assert.False(element.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        var expectedPrefix = element.TypeId == BpmnSemanticTypes.ExclusiveGateway
            ? "EXCLUSIVE_GATEWAY"
            : element.TypeId == BpmnSemanticTypes.ParallelGateway
                ? "PARALLEL_GATEWAY"
                : "INCLUSIVE_GATEWAY";
        Assert.Equal(expectedPrefix, code.TextValue);
        Assert.Equal(expectedPrefix.Replace('_', ' ').ToLowerInvariant(),
            name.TextValue.ToLowerInvariant());
        Assert.Equal(
            $"{name.TextValue} created from the Toolbox.",
            description.TextValue);
        Assert.Equal(
            [
                BpmnSemanticProperties.Code,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.Description,
            ],
            schema.Fields.Select(field => field.SemanticPropertyKey));
    }

    private static PropertyValue RequiredProperty(
        SemanticElementSnapshot element,
        string key)
    {
        Assert.True(element.Properties.TryGetValue(key, out var value));
        return Assert.IsType<PropertyValue>(value);
    }

    private static long MaximumTaskElementNumber(DocumentSnapshot document) =>
        document.SemanticModel.Elements
            .Where(element => BpmnTaskSemanticTypes.IsTask(element.TypeId))
            .Select(element => RequiredProperty(
                element,
                BpmnSemanticProperties.ElementNumber).IntegerValue)
            .DefaultIfEmpty()
            .Max();

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedObjectId, "node"));

    private static Canvas2DSceneItem LabelInteractionBody(
        Canvas2DScene scene,
        SemanticElementId semanticElementId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.SemanticElementId == semanticElementId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                projectedObjectId,
                "label-interaction"));

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static DocumentCreationIdentity Identity(string suffix) => new(
        new SemanticElementId($"test:bpmn:n1:semantic:{suffix}"),
        new VisualStateId($"test:bpmn:n1:visual:{suffix}"));

    private static ToolboxPlacementCatalog Catalog(
        ToolboxItemId itemId,
        IToolboxPlacementCommandFactory factory) =>
        new([new ToolboxPlacementRegistration(itemId, factory)]);

    private static ToolboxSelectionState HostToolboxSelection(DocumentCanvasHost host) =>
        Assert.IsType<ToolboxSelectionState>(typeof(DocumentCanvasHost)
            .GetField("_toolboxSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host));

    private static Task SendPointerAsync(
        HostHarness harness,
        CanvasPointerEventKind kind,
        long pointerId,
        PointD documentPoint,
        int button,
        int buttons)
    {
        var css = harness.Scene.ViewportTransform.TransformPoint(documentPoint);
        return (harness.Pointer.Callback ?? throw new InvalidOperationException(
            "The pointer observer has no managed callback."))(new CanvasPointerInput(
            kind,
            pointerId,
            button,
            buttons,
            IsPrimary: true,
            ClientX: css.X + 17d,
            ClientY: css.Y + 23d,
            CanvasLeft: 17d,
            CanvasTop: 23d,
            AltKey: false,
            ControlKey: false,
            MetaKey: false,
            ShiftKey: false,
            CaptureGeneration: 0));
    }

    internal sealed class SequenceIdentityProvider : IDocumentCreationIdentityProvider
    {
        private readonly Queue<DocumentCreationIdentity> _identities;

        internal SequenceIdentityProvider(params DocumentCreationIdentity[] identities) =>
            _identities = new Queue<DocumentCreationIdentity>(identities);

        public DocumentCreationIdentity CreateIdentity() =>
            _identities.Count == 0
                ? throw new InvalidOperationException("No test placement identity remains.")
                : _identities.Dequeue();
    }

    private sealed class FailingFactory : IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ToolboxPlacementPlanResult.Failure(
            [
                new Diagnostic(
                    "TEST_TOOLBOX_FACTORY_REJECTED",
                    DiagnosticSeverity.Error,
                    "The test placement factory rejected the plan.",
                    request.ToolboxItemId.Value),
            ]);
        }
    }

    private sealed class NullResultFactory : IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return null!;
        }
    }

    private sealed class UnsupportedCommandFactory(DocumentCreationIdentity identity) :
        IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ToolboxPlacementPlanResult.Success(new ToolboxPlacementPlan(
                new StaleCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision),
                identity.SemanticElementId,
                identity.VisualStateId));
        }
    }

    private sealed class StalePlanFactory(DocumentCreationIdentity identity) :
        IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ToolboxPlacementPlanResult.Success(new ToolboxPlacementPlan(
                new StaleCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision.Increment()),
                identity.SemanticElementId,
                identity.VisualStateId));
        }
    }

    private sealed record StaleCommand(
        DocumentId TargetDocumentId,
        DocumentRevision ExpectedRevision) : ICommand
    {
        public CommandTypeId TypeId { get; } = new("test:command/stale-toolbox-placement");

        public CommandCategory Category => CommandCategory.Compound;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel;
    }

    internal sealed class PlacementHarness : IAsyncDisposable
    {
        private PlacementHarness(
            DocumentCanvasComposition composition,
            EditingSession session,
            ToolboxCatalog toolbox,
            ToolboxSelectionState selection,
            ToolboxPlacementController controller,
            RecordingSubscriber events,
            RecordingRenderExecution execution,
            PipelineProbe pipeline,
            Canvas2DSurfaceSize surfaceSize)
        {
            Composition = composition;
            Session = session;
            Toolbox = toolbox;
            Selection = selection;
            Controller = controller;
            Events = events;
            Execution = execution;
            Pipeline = pipeline;
            SurfaceSize = surfaceSize;
        }

        internal DocumentCanvasComposition Composition { get; }

        internal EditingSession Session { get; }

        internal ToolboxCatalog Toolbox { get; }

        internal ToolboxSelectionState Selection { get; }

        internal ToolboxPlacementController Controller { get; }

        internal RecordingSubscriber Events { get; }

        internal RecordingRenderExecution Execution { get; }

        internal PipelineProbe Pipeline { get; }

        internal Canvas2DSurfaceSize SurfaceSize { get; }

        internal ToolboxItemDefinition Item(SemanticTypeId semanticTypeId) =>
            Assert.Single(Toolbox.Items, item => item.ElementTypeId == semanticTypeId);

        internal static async ValueTask<PlacementHarness> CreateAsync(
            double devicePixelRatio = 1.25d,
            SequenceIdentityProvider? identities = null,
            int? failFullRunNumber = null)
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            var events = new RecordingSubscriber();
            var source = composition.Configuration;
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
                source.CommandValidators,
                source.HistoryPolicies,
                [.. source.DocumentChangedSubscribers, events],
                source.ConnectorAnchorPolicyProvider);
            var execution = new RecordingRenderExecution();
            var renderer = new Canvas2DRenderer(
                execution,
                new Canvas2DRendererConfiguration(
                    fontResources:
                    [
                        new Canvas2DFontResource(
                            "org.dejavu.DejaVuSans",
                            "2.37",
                            "DejaVu Sans",
                            "fonts/DejaVuSans-2.37.ttf"),
                    ],
                    defaultFontFamily: "DejaVu Sans"));
            var surfaceSize = new Canvas2DSurfaceSize(900d, 600d, devicePixelRatio);
            var initialized = await renderer.InitializeAsync(
                "phase-n1-placement-integration-canvas",
                surfaceSize);
            Assert.True(initialized.Succeeded);
            var pipeline = new PipelineProbe(
                new EditingSessionPipeline(configuration),
                failFullRunNumber);
            var attachment = await EditingSession.AttachAsync(
                composition.Document,
                renderer,
                configuration,
                pipeline);
            Assert.True(attachment.IsAttached);
            var session = Assert.IsType<EditingSession>(attachment.Session);
            await session.WaitForIdleAsync();
            Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
            var toolbox = new ToolboxCatalog(BpmnPluginRegistration.N1.ToolboxContributions);
            var selection = new ToolboxSelectionState();
            var identityProvider = identities ?? new SequenceIdentityProvider(
                Identity($"default:{Guid.NewGuid():N}"));
            var controller = new ToolboxPlacementController(
                composition.ToolboxPlacementCatalog,
                selection,
                identityProvider);
            return new PlacementHarness(
                composition,
                session,
                toolbox,
                selection,
                controller,
                events,
                execution,
                pipeline,
                surfaceSize);
        }

        internal async ValueTask WaitForIdleAsync()
        {
            await Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await CommandProcessor.WaitForEventDispatchIdleAsync(Composition.Document)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    internal sealed class PipelineProbe(
        ISessionPipelineProcessing inner,
        int? failFullRunNumber) : ISessionPipelineProcessing
    {
        private int _fullRunCount;
        private int _documentRunCount;
        private int _nodeLayoutPreservingRunCount;
        private int _sceneOnlyRunCount;
        private int _failNextSceneOnlyRun;

        internal int FullRunCount => Volatile.Read(ref _fullRunCount);

        internal int NodeLayoutPreservingRunCount =>
            Volatile.Read(ref _nodeLayoutPreservingRunCount);

        internal int SceneOnlyRunCount => Volatile.Read(ref _sceneOnlyRunCount);

        internal void FailNextSceneOnlyRun() =>
            Interlocked.Exchange(ref _failNextSceneOnlyRun, 1);

        public ValueTask<EditingSessionPipelineResult> RunFullAsync(
            DocumentSnapshot document,
            DocumentScopeId activeScopeId,
            EditorStateSnapshot editorState,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fullRunCount);
            var documentRun = Interlocked.Increment(ref _documentRunCount);
            return documentRun == failFullRunNumber
                ? ValueTask.FromResult(EditingSessionPipelineResult.Failure(
                [
                    new Diagnostic(
                        "TEST_N1_PIPELINE_FAILURE",
                        DiagnosticSeverity.Error,
                        "The test pipeline rejected this full rebuild."),
                ]))
                : inner.RunFullAsync(
                    document,
                    activeScopeId,
                    editorState,
                    cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
            DocumentSnapshot document,
            DocumentScopeId activeScopeId,
            EditingSessionPipelineArtifacts previousArtifacts,
            ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
            NodeGeometryPipelineImpact nodeGeometryImpact,
            EditorStateSnapshot editorState,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _nodeLayoutPreservingRunCount);
            var documentRun = Interlocked.Increment(ref _documentRunCount);
            return documentRun == failFullRunNumber
                ? ValueTask.FromResult(EditingSessionPipelineResult.Failure(
                [
                    new Diagnostic(
                        "TEST_N1_PIPELINE_FAILURE",
                        DiagnosticSeverity.Error,
                        "The test pipeline rejected this selective rebuild."),
                ]))
                : inner.RunPreservingNodeLayoutAsync(
                    document,
                    activeScopeId,
                    previousArtifacts,
                    nodeLayoutHistory,
                    nodeGeometryImpact,
                    editorState,
                    cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
            EditingSessionPipelineArtifacts artifacts,
            VisualModelSnapshot visualModel,
            EditorStateSnapshot editorState,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sceneOnlyRunCount);
            return Interlocked.Exchange(ref _failNextSceneOnlyRun, 0) == 1
                ? ValueTask.FromResult(EditingSessionPipelineResult.Failure(
                [
                    new Diagnostic(
                        "TEST_N2_SCENE_PIPELINE_FAILURE",
                        DiagnosticSeverity.Error,
                        "The test pipeline rejected this Scene-only rebuild."),
                ]))
                : inner.RebuildSceneAsync(
                    artifacts,
                    visualModel,
                    editorState,
                    cancellationToken);
        }
    }

    internal sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class RecordingRenderExecution : ICanvas2DRenderExecution
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

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            Interlocked.Increment(ref _renderCount);
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request)
        {
            var width = request.Text.Sum(character => character switch
            {
                ' ' => request.FontSize * 0.33d,
                >= 'A' and <= 'Z' => request.FontSize * 0.62d,
                >= 'a' and <= 'z' => request.FontSize * 0.54d,
                >= '0' and <= '9' => request.FontSize * 0.55d,
                _ => request.FontSize * 0.58d,
            });
            return ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = Math.Max(1d, width),
                Ascent = request.FontSize * 0.75d,
                Descent = request.FontSize * 0.25d,
                LineHeight = request.LineHeight,
                BoundingX = 0d,
                BoundingY = -(request.FontSize * 0.75d),
                BoundingWidth = Math.Max(1d, width),
                BoundingHeight = request.FontSize,
                ResolvedFontIdentity = $"{request.FontIdentity}@{request.FontVersion}",
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
