using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM323BpmnManualGatewayLabelIntegrationTests
{
    public static TheoryData<string, string, double, double> ResizeCases => new()
    {
        { "north", "ns-resize", 0d, -18d },
        { "northeast", "nesw-resize", 24d, -18d },
        { "east", "ew-resize", 24d, 0d },
        { "southeast", "nwse-resize", 24d, 18d },
        { "south", "ns-resize", 0d, 18d },
        { "southwest", "nesw-resize", -24d, 18d },
        { "west", "ew-resize", -24d, 0d },
        { "northwest", "nwse-resize", -24d, -18d },
    };

    [Fact]
    public async Task LabelMoveIsSceneOnlyUntilOneVisualCommitAndNoOpUndoRedoAreExact()
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var events = AttachSubscriber(harness.Session);
        var initial = harness.State;
        var initialDocument = harness.Composition.Document.CaptureSnapshot();
        var initialScene = harness.Scene;
        var gatewayNode = NodeBody(initialScene);
        var automaticLabel = LabelInteractionBody(initialScene);
        var automaticLabelBounds = automaticLabel.Bounds;
        var initialRoutes = CaptureRoutes(initial);
        var delta = new VectorD(119d, 31d);
        var start = Center(automaticLabelBounds);
        var finish = start + delta;

        Assert.False(NodeLabelVisualOverride.TryRead(
            GatewayVisual(harness).Properties,
            out _));
        var hover = await interaction.PointerMovedAsync(Pointer(
            3230,
            initialScene,
            start));
        Assert.Equal("grab", hover.CssCursor);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            3231,
            Assert.IsType<Canvas2DScene>(hover.SessionState.CurrentScene),
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            3231,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.Equal("grabbing", moved.CssCursor);
        var previewScene = Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene);
        Assert.Equal(
            automaticLabelBounds.Translate(delta),
            NodeLabelPreviewBody(previewScene).Bounds);
        Assert.Equal(gatewayNode.Bounds, NodeBody(previewScene).Bounds);
        Assert.Same(initial.ProjectedGraph, moved.SessionState.ProjectedGraph);
        Assert.Same(initial.LayoutResult, moved.SessionState.LayoutResult);
        Assert.Same(initial.RoutingResult, moved.SessionState.RoutingResult);
        Assert.Equal(initial.DocumentRevision, moved.SessionState.DocumentRevision);
        Assert.Equal(initial.HistoryStatus, moved.SessionState.HistoryStatus);
        Assert.Equal(initialDocument, harness.Composition.Document.CaptureSnapshot());
        Assert.Empty(events.Events);

        var released = await interaction.PointerReleasedAsync(Pointer(
            3231,
            previewScene,
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        await WaitForEventCountAsync(events, 1);

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            Assert.IsType<Contracts.History.HistoryOperationResult>(
                released.PersistentOperation).CommandTypeId);
        Assert.Equal(initial.DocumentRevision.Increment(), harness.State.DocumentRevision);
        Assert.Equal(initial.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal(harness.State.DocumentRevision, events.Events.Last().CommittedRevision);
        AssertSemanticContentEqual(
            initialDocument.SemanticModel,
            harness.Composition.Document.CaptureSnapshot().SemanticModel);
        Assert.Equal(gatewayNode.Bounds, NodeBody(harness.Scene).Bounds);
        Assert.Equal(
            initialRoutes.AsEnumerable(),
            CaptureRoutes(harness.State).AsEnumerable());
        var expectedOverride = OverrideFor(
            automaticLabelBounds.Translate(delta),
            gatewayNode.Bounds);
        Assert.Equal(expectedOverride, ReadGatewayOverride(harness));
        Assert.Equal(automaticLabelBounds.Translate(delta),
            LabelInteractionBody(harness.Scene).Bounds);
        Assert.Equal(
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            Assert.Single(harness.State.EditorState.Selection));

        var newHit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(
                harness.Scene,
                Center(LabelInteractionBody(harness.Scene).Bounds)));
        Assert.Equal(LabelInteractionBody(harness.Scene).Id, newHit.SceneObjectId);
        var oldHit = new Canvas2DSceneHitTestService().HitTest(
            harness.Scene,
            Center(automaticLabelBounds));
        Assert.NotEqual(LabelInteractionBody(harness.Scene).Id, oldHit?.SceneObjectId);

        var noOpState = harness.State;
        var noOpScene = harness.Scene;
        var noOpPoint = Center(LabelInteractionBody(noOpScene).Bounds);
        var noOpPressed = await interaction.PointerPressedAsync(Pointer(
            3232,
            noOpScene,
            noOpPoint,
            button: 0,
            buttons: 1));
        var noOpReleased = await interaction.PointerReleasedAsync(Pointer(
            3232,
            Assert.IsType<Canvas2DScene>(noOpPressed.SessionState.CurrentScene),
            noOpPoint,
            button: 0));
        await harness.Session.WaitForIdleAsync();

        Assert.NotEqual(Canvas2DInteractionStatus.Committed, noOpReleased.Status);
        Assert.Null(noOpReleased.PersistentOperation);
        Assert.Equal(noOpState.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(noOpState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Single(events.Events);

        await harness.Host.UndoAsync();
        Assert.False(NodeLabelVisualOverride.TryRead(
            GatewayVisual(harness).Properties,
            out _));
        Assert.Equal(automaticLabelBounds, LabelInteractionBody(harness.Scene).Bounds);

        await harness.Host.RedoAsync();
        Assert.Equal(expectedOverride, ReadGatewayOverride(harness));
        Assert.Equal(automaticLabelBounds.Translate(delta),
            LabelInteractionBody(harness.Scene).Bounds);

        var beforeViewport = harness.State;
        foreach (var zoom in new[] { 0.75d, 1d, 1.5d })
        {
            var viewport = beforeViewport.EditorState.Viewport;
            var viewportResult = await harness.Session.UpdateViewportAsync(
                new ViewportSnapshot(
                    zoom,
                    viewport.Pan,
                    viewport.VisibleDocumentRegion));
            Assert.True(viewportResult.Succeeded);
            Assert.Equal(expectedOverride, ReadGatewayOverride(harness));
            Assert.Equal(
                automaticLabelBounds.Translate(delta),
                LabelInteractionBody(harness.Scene).Bounds);
            Assert.Equal(beforeViewport.DocumentRevision,
                harness.State.DocumentRevision);
            Assert.Equal(beforeViewport.HistoryStatus, harness.State.HistoryStatus);
        }
    }

    [Theory]
    [MemberData(nameof(ResizeCases))]
    public async Task EveryResizeDirectionUsesExpectedGeometryCursorAndOneVisualCommit(
        string role,
        string expectedCursor,
        double deltaX,
        double deltaY)
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        await MoveLabelAsync(
            harness,
            interaction,
            new VectorD(73d, 27d),
            pointerId: 3240);
        var events = AttachSubscriber(harness.Session);
        var before = harness.State;
        var beforeDocument = harness.Composition.Document.CaptureSnapshot();
        var beforeNodeBounds = NodeBody(harness.Scene).Bounds;
        var beforeLabelBounds = LabelInteractionBody(harness.Scene).Bounds;
        var beforeRoutes = CaptureRoutes(before);
        var zone = LabelResizeZone(harness.Scene, role);
        var start = Center(zone.Bounds);
        var finish = start + new VectorD(deltaX, deltaY);
        var expectedBounds = ResizeBounds(
            beforeLabelBounds,
            role,
            new VectorD(deltaX, deltaY));

        var hover = await interaction.PointerMovedAsync(Pointer(
            3241,
            harness.Scene,
            start));
        Assert.Equal(expectedCursor, hover.CssCursor);
        var pressed = await interaction.PointerPressedAsync(Pointer(
            3242,
            Assert.IsType<Canvas2DScene>(hover.SessionState.CurrentScene),
            start,
            button: 0,
            buttons: 1));
        Assert.Equal(expectedCursor, pressed.CssCursor);
        var moved = await interaction.PointerMovedAsync(Pointer(
            3242,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));

        Assert.Equal(expectedCursor, moved.CssCursor);
        Assert.Equal(expectedBounds, NodeLabelPreviewBody(
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene)).Bounds);
        Assert.Same(before.ProjectedGraph, moved.SessionState.ProjectedGraph);
        Assert.Same(before.LayoutResult, moved.SessionState.LayoutResult);
        Assert.Same(before.RoutingResult, moved.SessionState.RoutingResult);
        Assert.Equal(before.DocumentRevision, moved.SessionState.DocumentRevision);
        Assert.Equal(before.HistoryStatus, moved.SessionState.HistoryStatus);
        Assert.Equal(beforeDocument, harness.Composition.Document.CaptureSnapshot());
        Assert.Empty(events.Events);

        var released = await interaction.PointerReleasedAsync(Pointer(
            3242,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        await WaitForEventCountAsync(events, 1);

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(before.DocumentRevision.Increment(), harness.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal(expectedBounds, LabelInteractionBody(harness.Scene).Bounds);
        Assert.Equal(OverrideFor(expectedBounds, beforeNodeBounds),
            ReadGatewayOverride(harness));
        Assert.Equal(beforeNodeBounds, NodeBody(harness.Scene).Bounds);
        AssertSemanticContentEqual(
            beforeDocument.SemanticModel,
            harness.Composition.Document.CaptureSnapshot().SemanticModel);
        Assert.Equal(
            beforeRoutes.AsEnumerable(),
            CaptureRoutes(harness.State).AsEnumerable());
        Assert.All(GatewayTextLines(harness.Scene), line =>
        {
            Assert.NotNull(line.Clip);
            Assert.Equal(expectedBounds, line.Clip);
        });
    }

    [Fact]
    public async Task NameEditKeepsManualBoxAndResetClearsOnlyOverrideWithUndoRedo()
    {
        const string longName = "Is the customer order approved?";
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        await MoveLabelAsync(
            harness,
            interaction,
            new VectorD(91d, 46d),
            pointerId: 3250);
        var manualOverride = ReadGatewayOverride(harness);
        var manualBounds = LabelInteractionBody(harness.Scene).Bounds;
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(
                BpmnDemoPipeline.ExclusiveGatewayVisualId));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        Assert.True(draft.TryGetDataField(
            new ElementPropertyFieldId("name"),
            out var nameField));
        Assert.IsType<DocumentCanvasDataPropertyDraft>(nameField).EditorValue = longName;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            isDirty: true));

        var nameResult = await harness.Host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, nameResult.Status);
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        Assert.Equal(manualBounds, LabelInteractionBody(harness.Scene).Bounds);
        Assert.Equal(longName, GatewayName(harness));
        Assert.Equal(longName, string.Join(' ', GatewayTextLines(harness.Scene)
            .Select(static item => item.Geometry.Content)));
        Assert.All(GatewayTextLines(harness.Scene), line =>
            Assert.Equal(manualBounds, line.Clip));

        await harness.Host.UndoAsync();
        Assert.Equal("Approved?", GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        await harness.Host.RedoAsync();
        Assert.Equal(longName, GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));

        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false));
        var events = AttachSubscriber(harness.Session);
        var beforeReset = harness.State;
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            Center(LabelInteractionBody(harness.Scene).Bounds));
        var context = Assert.IsType<DocumentCanvasContextMenuState>(
            harness.Host.CaptureState().ContextMenu);
        Assert.Equal(BpmnDemoPipeline.ExclusiveGatewayVisualId,
            context.TargetVisualStateId);
        var resetAction = Assert.IsType<Canvas2DNodeLabelContextAction>(
            context.NodeLabelAction);
        Assert.Equal(BpmnDemoPipeline.ExclusiveGatewayVisualId,
            resetAction.TargetVisualStateId);
        Assert.True(resetAction.IsCurrent(harness.Scene, GatewayVisual(harness)));

        var reset = Assert.IsType<Contracts.History.HistoryOperationResult>(
            await harness.Host.ExecuteNodeLabelContextActionAsync());
        await WaitForEventCountAsync(events, 1);

        Assert.True(reset.IsCommitted);
        Assert.Equal(UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            reset.CommandTypeId);
        Assert.Equal(beforeReset.DocumentRevision.Increment(),
            harness.State.DocumentRevision);
        Assert.Equal(beforeReset.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        Assert.False(NodeLabelVisualOverride.TryRead(
            GatewayVisual(harness).Properties,
            out _));
        AssertAutomaticOutsideBelow(harness.Scene);

        await harness.Host.UndoAsync();
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        Assert.Equal(manualBounds, LabelInteractionBody(harness.Scene).Bounds);
        await harness.Host.RedoAsync();
        Assert.False(NodeLabelVisualOverride.TryRead(
            GatewayVisual(harness).Properties,
            out _));
        AssertAutomaticOutsideBelow(harness.Scene);

        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            Center(LabelInteractionBody(harness.Scene).Bounds));
        Assert.Null(harness.Host.CaptureState().ContextMenu?.NodeLabelAction);
    }

    [Fact]
    public async Task EmptyNameSuppressesLabelWhileExactManualOverrideReappearsOnRestore()
    {
        const string restoredName = "Restored approval decision?";
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        await MoveLabelAsync(
            harness,
            interaction,
            new VectorD(103d, 52d),
            pointerId: 3253);
        var manualOverride = ReadGatewayOverride(harness);
        var manualBounds = LabelInteractionBody(harness.Scene).Bounds;
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(
                BpmnDemoPipeline.ExclusiveGatewayVisualId));
        var emptyDraft = new DocumentCanvasPropertiesDraft(properties);
        Assert.True(emptyDraft.TryGetDataField(
            new ElementPropertyFieldId("name"),
            out var emptyNameField));
        Assert.IsType<DocumentCanvasDataPropertyDraft>(emptyNameField).EditorValue = string.Empty;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            isDirty: true));

        var emptied = await harness.Host.ApplyPropertiesAsync(emptyDraft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, emptied.Status);
        Assert.Equal(string.Empty, GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        AssertGatewayLabelAbsent(harness);

        await harness.Host.UndoAsync();
        Assert.Equal("Approved?", GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        Assert.Equal(manualBounds, LabelInteractionBody(harness.Scene).Bounds);

        await harness.Host.RedoAsync();
        Assert.Equal(string.Empty, GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        AssertGatewayLabelAbsent(harness);

        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false));
        var reopened = await harness.OpenNodePropertiesAsync(
            BpmnDemoPipeline.ExclusiveGatewayVisualId);
        var restoredDraft = new DocumentCanvasPropertiesDraft(reopened);
        Assert.True(restoredDraft.TryGetDataField(
            new ElementPropertyFieldId("name"),
            out var restoredNameField));
        Assert.Equal(string.Empty,
            Assert.IsType<DocumentCanvasDataPropertyDraft>(restoredNameField).EditorValue);
        Assert.IsType<DocumentCanvasDataPropertyDraft>(restoredNameField).EditorValue =
            restoredName;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            isDirty: true));

        var restored = await harness.Host.ApplyPropertiesAsync(restoredDraft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, restored.Status);
        Assert.Equal(restoredName, GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        Assert.Equal(manualBounds, LabelInteractionBody(harness.Scene).Bounds);
        Assert.Equal(restoredName, string.Join(' ', GatewayTextLines(harness.Scene)
            .Select(static item => item.Geometry.Content)));

        await harness.Host.UndoAsync();
        Assert.Equal(string.Empty, GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        AssertGatewayLabelAbsent(harness);

        await harness.Host.RedoAsync();
        Assert.Equal(restoredName, GatewayName(harness));
        Assert.Equal(manualOverride, ReadGatewayOverride(harness));
        Assert.Equal(manualBounds, LabelInteractionBody(harness.Scene).Bounds);
    }

    [Fact]
    public async Task ManualWidthRewrapsLongNameAndManualHeightClipsWithoutFontOrNodeChanges()
    {
        const string longName = "Is the customer order approved?";
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        await MoveLabelAsync(
            harness,
            interaction,
            new VectorD(88d, 43d),
            pointerId: 3255);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(
                BpmnDemoPipeline.ExclusiveGatewayVisualId));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        Assert.True(draft.TryGetDataField(
            new ElementPropertyFieldId("name"),
            out var nameField));
        Assert.IsType<DocumentCanvasDataPropertyDraft>(nameField).EditorValue = longName;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            isDirty: true));
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await harness.Host.ApplyPropertiesAsync(draft)).Status);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false));

        var nodeBounds = NodeBody(harness.Scene).Bounds;
        var initialOverride = ReadGatewayOverride(harness);
        var initialLines = GatewayTextLines(harness.Scene);
        var fontSize = Assert.Single(initialLines.Select(static line => line.Style.FontSize)
            .Distinct());
        Assert.True(initialLines.Length >= 2);
        Assert.All(initialLines, line =>
            Assert.Equal(LabelInteractionBody(harness.Scene).Bounds, line.Clip));

        await ResizeLabelAsync(
            harness,
            interaction,
            "east",
            new VectorD(150d, 0d),
            pointerId: 3256);
        var widenedOverride = ReadGatewayOverride(harness);
        var widenedBounds = LabelInteractionBody(harness.Scene).Bounds;
        var widenedLines = GatewayTextLines(harness.Scene);

        Assert.True(widenedLines.Length < initialLines.Length);
        Assert.Equal(initialOverride.Height, widenedOverride.Height, precision: 8);
        Assert.Equal(initialOverride.Width + 150d, widenedOverride.Width, precision: 8);
        Assert.All(widenedLines, line =>
        {
            Assert.Equal(widenedBounds, line.Clip);
            Assert.Equal(fontSize, line.Style.FontSize);
        });

        await ResizeLabelAsync(
            harness,
            interaction,
            "west",
            new VectorD(240d, 0d),
            pointerId: 3257);
        var narrowedOverride = ReadGatewayOverride(harness);
        var narrowedBounds = LabelInteractionBody(harness.Scene).Bounds;
        var narrowedLines = GatewayTextLines(harness.Scene);

        Assert.True(narrowedLines.Length > widenedLines.Length);
        Assert.Equal(widenedOverride.Height, narrowedOverride.Height, precision: 8);
        Assert.Equal(widenedOverride.Width - 240d, narrowedOverride.Width, precision: 8);
        Assert.All(narrowedLines, line =>
        {
            Assert.Equal(narrowedBounds, line.Clip);
            Assert.Equal(fontSize, line.Style.FontSize);
        });
        Assert.Equal(nodeBounds, NodeBody(harness.Scene).Bounds);
        Assert.Equal(longName, GatewayName(harness));
    }

    [Fact]
    public async Task OwnerMoveAndResizePreserveRelativeOverrideWhileRoutesAndAnchorsFollowOwner()
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        await MoveLabelAsync(
            harness,
            interaction,
            new VectorD(84d, 39d),
            pointerId: 3260);
        var visualOverride = ReadGatewayOverride(harness);
        var labelBeforeMove = LabelInteractionBody(harness.Scene).Bounds;
        var nodeBeforeMove = NodeBody(harness.Scene).Bounds;
        var routesBeforeMove = CaptureGatewayRoutes(harness.State);
        var anchorBindings = CaptureGatewayRouteBindings(harness);
        var moveDelta = new VectorD(43d, -22d);

        var moved = await MoveNodeAsync(
            harness,
            interaction,
            moveDelta,
            pointerId: 3261);

        Assert.Equal(Canvas2DInteractionStatus.Committed, moved.Status);
        Assert.Equal(visualOverride, ReadGatewayOverride(harness));
        Assert.Equal(nodeBeforeMove.Translate(moveDelta), NodeBody(harness.Scene).Bounds);
        Assert.Equal(labelBeforeMove.Translate(moveDelta),
            LabelInteractionBody(harness.Scene).Bounds);
        Assert.False(routesBeforeMove.AsSpan().SequenceEqual(
            CaptureGatewayRoutes(harness.State).AsSpan()));
        Assert.Equal(
            anchorBindings.AsEnumerable(),
            CaptureGatewayRouteBindings(harness).AsEnumerable());

        var nodeBeforeResize = NodeBody(harness.Scene).Bounds;
        var labelBeforeResize = LabelInteractionBody(harness.Scene).Bounds;
        var routesBeforeResize = CaptureGatewayRoutes(harness.State);
        var southeastZone = NodeResizeZone(harness.Scene, "southeast");
        var resizeStart = Center(southeastZone.Bounds);
        var resizeFinish = resizeStart + new VectorD(12d, 0d);
        var pressed = await interaction.PointerPressedAsync(Pointer(
            3262,
            harness.Scene,
            resizeStart,
            button: 0,
            buttons: 1));
        var resizing = await interaction.PointerMovedAsync(Pointer(
            3262,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            resizeFinish,
            buttons: 1));
        var resized = await interaction.PointerReleasedAsync(Pointer(
            3262,
            Assert.IsType<Canvas2DScene>(resizing.SessionState.CurrentScene),
            resizeFinish,
            button: 0));
        await harness.Session.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, resized.Status);
        Assert.Equal(visualOverride, ReadGatewayOverride(harness));
        var nodeAfterResize = NodeBody(harness.Scene).Bounds;
        Assert.Equal(nodeBeforeResize.Width + 12d, nodeAfterResize.Width, precision: 8);
        Assert.Equal(nodeBeforeResize.Height, nodeAfterResize.Height, precision: 8);
        var expectedCenter = Center(nodeAfterResize) +
            new VectorD(visualOverride.OffsetX, visualOverride.OffsetY);
        Assert.Equal(expectedCenter, Center(LabelInteractionBody(harness.Scene).Bounds));
        Assert.Equal(labelBeforeResize.Size,
            LabelInteractionBody(harness.Scene).Bounds.Size);
        Assert.False(routesBeforeResize.AsSpan().SequenceEqual(
            CaptureGatewayRoutes(harness.State).AsSpan()));
        Assert.Equal(
            anchorBindings.AsEnumerable(),
            CaptureGatewayRouteBindings(harness).AsEnumerable());
    }

    [Fact]
    public async Task TaskLabelRemainsFixedAndNormalizesToItsOwningTask()
    {
        await using var harness = await HostHarness.CreateAsync();
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var taskNode = Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.TaskId);
        var taskLabel = Assert.Single(graph.Labels, label => label.OwnerId == taskNode.Id);
        var text = Assert.Single(harness.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.ProjectedObjectId == taskLabel.Id);

        Assert.Equal(NodeLabelInteractionPolicy.Fixed,
            taskLabel.NodeInteractionPolicy);
        Assert.DoesNotContain(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                taskLabel.Id,
                "label-interaction"));
        Assert.False(NodeLabelVisualOverride.TryRead(
            TaskVisual(harness).Properties,
            out _));

        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var selected = await interaction.PointerReleasedAsync(Pointer(
            3270,
            harness.Scene,
            Center(text.Bounds),
            button: 0));
        Assert.Equal(BpmnDemoPipeline.TaskVisualId,
            Assert.Single(selected.SessionState.EditorState.Selection));
        Assert.DoesNotContain(
            selected.SessionState.CurrentScene!.Items,
            item => item.Origin.StableSourceKey?.StartsWith(
                "node-label-resize-zone:",
                StringComparison.Ordinal) == true &&
                item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
    }

    private static async Task<NodeLabelVisualOverride> MoveLabelAsync(
        HostHarness harness,
        Canvas2DInteractionController interaction,
        VectorD delta,
        long pointerId)
    {
        var label = LabelInteractionBody(harness.Scene);
        var start = Center(label.Bounds);
        var finish = start + delta;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            harness.Scene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));
        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        return ReadGatewayOverride(harness);
    }

    private static async Task<Canvas2DInteractionResult> MoveNodeAsync(
        HostHarness harness,
        Canvas2DInteractionController interaction,
        VectorD delta,
        long pointerId)
    {
        var start = Center(NodeBody(harness.Scene).Bounds);
        var finish = start + delta;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            harness.Scene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));
        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        return released;
    }

    private static async Task<Canvas2DInteractionResult> ResizeLabelAsync(
        HostHarness harness,
        Canvas2DInteractionController interaction,
        string role,
        VectorD delta,
        long pointerId)
    {
        var zone = LabelResizeZone(harness.Scene, role);
        var start = Center(zone.Bounds);
        var finish = start + delta;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            harness.Scene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));
        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        return released;
    }

    private static Canvas2DSceneItem NodeBody(Canvas2DScene scene) =>
        NodeBody(scene, BpmnDemoPipeline.ExclusiveGatewayVisualId);

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) => Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                projectedObjectId,
                "node"));

    private static Canvas2DSceneItem LabelInteractionBody(Canvas2DScene scene)
        => Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                projectedObjectId,
                "label-interaction"));

    private static Canvas2DSceneItem NodeLabelPreviewBody(Canvas2DScene scene) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem LabelResizeZone(
        Canvas2DScene scene,
        string role) => Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith(
                $"node-label-resize-zone:{role}:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem NodeResizeZone(
        Canvas2DScene scene,
        string role) => Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            (item.Origin.StableSourceKey?.StartsWith(
                 $"resize-edge-zone:{role}:",
                 StringComparison.Ordinal) == true ||
             item.Origin.StableSourceKey?.StartsWith(
                 $"resize-handle:{role}:",
                 StringComparison.Ordinal) == true));

    private static Canvas2DSceneItem[] GatewayTextLines(Canvas2DScene scene) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
                item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId)
            .OrderBy(static item => item.Bounds.Top)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static VisualStateSnapshot GatewayVisual(HostHarness harness) =>
        harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ExclusiveGatewayVisualId);

    private static VisualStateSnapshot TaskVisual(HostHarness harness) =>
        harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);

    private static NodeLabelVisualOverride ReadGatewayOverride(HostHarness harness)
    {
        Assert.True(NodeLabelVisualOverride.TryRead(
            GatewayVisual(harness).Properties,
            out var visualOverride));
        return Assert.IsType<NodeLabelVisualOverride>(visualOverride);
    }

    private static string GatewayName(HostHarness harness) => harness.Composition.Document
        .SemanticModel.Elements.Single(element =>
            element.Id == BpmnDemoPipeline.ExclusiveGatewayId)
        .Properties["BPMN.Name"].TextValue;

    private static NodeLabelVisualOverride OverrideFor(
        RectD labelBounds,
        RectD nodeBounds)
    {
        var delta = Center(labelBounds) - Center(nodeBounds);
        return new NodeLabelVisualOverride(
            delta.X,
            delta.Y,
            labelBounds.Width,
            labelBounds.Height);
    }

    private static ImmutableArray<RoutedConnectorGeometry> CaptureRoutes(
        EditingSessionState state) =>
        Assert.IsType<RoutingResult>(state.RoutingResult).Routes;

    private static ImmutableArray<RoutedConnectorGeometry> CaptureGatewayRoutes(
        EditingSessionState state)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var gateway = Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        var edgeIds = graph.Edges
            .Where(edge => edge.SourceNodeId == gateway.Id || edge.TargetNodeId == gateway.Id)
            .Select(static edge => edge.Id)
            .ToHashSet();
        return [.. Assert.IsType<RoutingResult>(state.RoutingResult).Routes
            .Where(route => edgeIds.Contains(route.ProjectedEdgeId))
            .OrderBy(static route => route.ProjectedEdgeId.Value, StringComparer.Ordinal)];
    }

    private static ImmutableArray<(ConnectorAnchorId? Source, ConnectorAnchorId? Target)>
        CaptureGatewayRouteBindings(HostHarness harness) =>
        [.. harness.Composition.Document.VisualModel.VisualStates
            .Where(visual => visual.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId ||
                visual.SemanticElementId == BpmnDemoPipeline.ThirdSequenceFlowId ||
                visual.SemanticElementId == BpmnDemoPipeline.FourthSequenceFlowId)
            .OrderBy(static visual => visual.Id.Value, StringComparer.Ordinal)
            .Select(static visual => (visual.SourceAnchorId, visual.TargetAnchorId))];

    private static void AssertSemanticContentEqual(
        SemanticModelSnapshot expected,
        SemanticModelSnapshot actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.Equal(expected.Elements.AsEnumerable(), actual.Elements.AsEnumerable());
        Assert.Equal(
            expected.Relationships.AsEnumerable(),
            actual.Relationships.AsEnumerable());
    }

    private static RectD ResizeBounds(RectD original, string role, VectorD delta)
    {
        var left = role is "west" or "northwest" or "southwest"
            ? Math.Min(original.Left + delta.X, original.Right - 1d)
            : original.Left;
        var right = role is "east" or "northeast" or "southeast"
            ? Math.Max(original.Right + delta.X, original.Left + 1d)
            : original.Right;
        var top = role is "north" or "northwest" or "northeast"
            ? Math.Min(original.Top + delta.Y, original.Bottom - 1d)
            : original.Top;
        var bottom = role is "south" or "southwest" or "southeast"
            ? Math.Max(original.Bottom + delta.Y, original.Top + 1d)
            : original.Bottom;
        return new RectD(left, top, right - left, bottom - top);
    }

    private static void AssertAutomaticOutsideBelow(Canvas2DScene scene)
    {
        var nodeBounds = NodeBody(scene).Bounds;
        var labelBounds = LabelInteractionBody(scene).Bounds;
        Assert.True(labelBounds.Top >= nodeBounds.Bottom + 8d);
        Assert.Equal(Center(nodeBounds).X, Center(labelBounds).X, precision: 8);
    }

    private static void AssertGatewayLabelAbsent(HostHarness harness)
    {
        Assert.DoesNotContain(
            Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph).Labels,
            label => label.Source.SemanticElementId ==
                BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.DoesNotContain(harness.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
    }

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2DScene scene,
        PointD documentPoint,
        int button = -1,
        int buttons = 0) => new(
            pointerId,
            scene.ViewportTransform.TransformPoint(documentPoint),
            isPrimary: true,
            button: button,
            buttons: buttons);

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static RecordingSubscriber AttachSubscriber(EditingSession session)
    {
        var subscriber = new RecordingSubscriber();
        var processor = Assert.IsType<CommandProcessor>(typeof(EditingSession)
            .GetField(
                "_commandProcessor",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session));
        var subscribersField = typeof(CommandProcessor).GetField(
            "_subscribers",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var subscribers = Assert.IsType<ImmutableArray<IDocumentChangedSubscriber>>(
            subscribersField.GetValue(processor));
        subscribersField.SetValue(processor, subscribers.Add(subscriber));
        return subscriber;
    }

    private static async Task WaitForEventCountAsync(
        RecordingSubscriber subscriber,
        int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (subscriber.Events.Count < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(expected, subscriber.Events.Count);
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
}
