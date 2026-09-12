using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DBoundaryAttachmentInteractionTests
{
    private static readonly DocumentId DocumentId = new("test:canvas-boundary-attachment");
    private static readonly DocumentRevision Revision = new(3);
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticElementId OwnerId = new("test:owner");
    private static readonly SemanticElementId AttachedId = new("test:attached");
    private static readonly VisualStateId OwnerVisualId = new("test:visual:owner");
    private static readonly VisualStateId AttachedVisualId = new("test:visual:attached");
    private static readonly ProjectionRuleId ProjectionRuleId =
        new("test:projection:boundary-attachment");
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout");
    private static readonly AlgorithmId RoutingAlgorithmId = new("test:routing");
    private static readonly RectD InitialOwnerBounds = new(100d, 100d, 100d, 60d);
    private static readonly SizeD AttachedSize = new(36d, 36d);
    private static readonly BoundaryAttachmentPlacement InitialPlacement =
        new(BoundaryAttachmentSide.Bottom, 0.5d);

    [Fact]
    public void AttachedNodeIsTopmostCanonicalMoveOnlyBodyWithoutResizeInteractions()
    {
        var snapshot = CreateSnapshot();
        var scene = BuildScene(snapshot, EditorStateSnapshot.Empty);
        var owner = FindNode(scene, OwnerVisualId);
        var attached = FindNode(scene, AttachedVisualId);

        Assert.True(attached.ZIndex > owner.ZIndex);
        Assert.True(Canvas2DNodeBodyMetadata.IsNodeBody(attached));
        Assert.True(BooleanMetadata(
            attached,
            Canvas2DMoveGestureMetadata.MoveCapable));
        Assert.False(BooleanMetadata(
            attached,
            Canvas2DResizeGestureMetadata.ResizeCapable));

        var overlapPoint = new PointD(150d, 150d);
        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(scene, overlapPoint));
        Assert.Equal(attached.Id, hit.SceneObjectId);

        var selected = BuildScene(
            snapshot,
            new EditorStateSnapshot(selection: [AttachedVisualId]));
        Assert.DoesNotContain(
            selected.Items,
            item => item.Origin.RelatedSceneObjectIds.Contains(attached.Id) &&
                IsResizeInteraction(item));
    }

    [Fact]
    public async Task AttachedDragPreviewsAndCommitsOneAttachmentCommandWithUndoRedo()
    {
        var initial = CreateSnapshot();
        var context = await AttachAsync(
            initial,
            new EditorStateSnapshot(selection: [AttachedVisualId]));
        EnqueueSceneRebuild(context.Pipeline);
        EnqueueDocumentRun(context.Pipeline);
        EnqueueDocumentRun(context.Pipeline);
        EnqueueDocumentRun(context.Pipeline);
        await using var session = context.Session;
        await using var controller = new Canvas2DInteractionController(session);
        var originalAttachedBounds = InitialPlacement.ResolveBounds(
            InitialOwnerBounds,
            AttachedSize);
        var start = new PointD(
            originalAttachedBounds.Left + 8d,
            originalAttachedBounds.Top + 8d);
        var rawReleasePoint = new PointD(InitialOwnerBounds.Right, 115d);
        Assert.True(BoundaryAttachmentPlacement.TryProjectToBoundary(
            InitialOwnerBounds,
            rawReleasePoint,
            AttachedSize,
            out var expectedPlacement));
        Assert.Equal(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Right, 0.25d),
            expectedPlacement);
        var expectedBounds = expectedPlacement!.ResolveBounds(
            InitialOwnerBounds,
            AttachedSize);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(90, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(90, rawReleasePoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.True(
            moved.Status == Canvas2DInteractionStatus.Updated,
            string.Join(Environment.NewLine, moved.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Equal("grabbing", moved.CssCursor);
        Assert.Equal(
            expectedBounds,
            FindMovePreview(moved.SessionState.CurrentScene!, AttachedVisualId).Bounds);
        Assert.DoesNotContain(
            moved.SessionState.CurrentScene!.Items,
            item => IsMovePreview(item, OwnerVisualId));
        Assert.Equal(
            InitialOwnerBounds,
            FindNode(moved.SessionState.CurrentScene!, OwnerVisualId).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(90, rawReleasePoint));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            UpdateBoundaryAttachmentCommand.KnownTypeId,
            released.PersistentOperation?.CommandTypeId);
        Assert.Equal(1, released.SessionState.HistoryStatus.EntryCount);
        AssertAttachment(
            context.Document.CaptureSnapshot(),
            expectedPlacement,
            expectedBounds);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertAttachment(
            context.Document.CaptureSnapshot(),
            InitialPlacement,
            InitialPlacement.ResolveBounds(InitialOwnerBounds, AttachedSize));

        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertAttachment(
            context.Document.CaptureSnapshot(),
            expectedPlacement,
            expectedBounds);
    }

    [Fact]
    public async Task OwnerMovePreviewsAndSynchronizesAttachedDependentWithoutASecondCommand()
    {
        var context = await AttachAsync(
            CreateSnapshot(),
            new EditorStateSnapshot(selection: [OwnerVisualId]));
        EnqueueSceneRebuild(context.Pipeline);
        EnqueueDocumentRun(context.Pipeline);
        await using var session = context.Session;
        await using var controller = new Canvas2DInteractionController(session);
        var start = new PointD(120d, 120d);
        var end = new PointD(140d, 145d);
        var translation = new VectorD(20d, 25d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(91, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(91, end, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.Equal(
            InitialOwnerBounds.Translate(translation),
            FindMovePreview(moved.SessionState.CurrentScene!, OwnerVisualId).Bounds);
        Assert.Equal(
            InitialPlacement.ResolveBounds(InitialOwnerBounds, AttachedSize)
                .Translate(translation),
            FindMovePreview(moved.SessionState.CurrentScene!, AttachedVisualId).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(91, end));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            MoveVisualStateCommand.KnownTypeId,
            released.PersistentOperation?.CommandTypeId);
        Assert.Equal(1, released.SessionState.HistoryStatus.EntryCount);
        var committed = context.Document.CaptureSnapshot();
        AssertVisualBounds(committed, OwnerVisualId, InitialOwnerBounds.Translate(translation));
        AssertAttachment(
            committed,
            InitialPlacement,
            InitialPlacement.ResolveBounds(
                InitialOwnerBounds.Translate(translation),
                AttachedSize),
            InitialOwnerBounds.Translate(translation));
    }

    [Fact]
    public async Task OwnerResizePreviewsAndSynchronizesFixedSizeAttachedDependent()
    {
        var context = await AttachAsync(
            CreateSnapshot(),
            new EditorStateSnapshot(selection: [OwnerVisualId]));
        EnqueueSceneRebuild(context.Pipeline);
        EnqueueSceneRebuild(context.Pipeline);
        EnqueueDocumentRun(context.Pipeline);
        await using var session = context.Session;
        await using var controller = new Canvas2DInteractionController(session);
        var initialScene = session.CaptureState().CurrentScene!;
        var ownerId = FindNode(initialScene, OwnerVisualId).Id;
        var handle = FindResizeInteraction(initialScene, ownerId, "southeast");
        var start = Center(handle.Bounds);
        var end = start + new VectorD(20d, 20d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(92, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(92, end, buttons: 1));

        var resizedOwnerBounds = new RectD(100d, 100d, 120d, 80d);
        Assert.True(
            moved.Status == Canvas2DInteractionStatus.Updated,
            string.Join(Environment.NewLine, moved.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Equal(
            resizedOwnerBounds,
            FindResizePreview(moved.SessionState.CurrentScene!, ownerId).Bounds);
        Assert.Equal(
            InitialPlacement.ResolveBounds(resizedOwnerBounds, AttachedSize),
            FindMovePreview(moved.SessionState.CurrentScene!, AttachedVisualId).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(92, end));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            ResizeVisualStateCommand.KnownTypeId,
            released.PersistentOperation?.CommandTypeId);
        var committed = context.Document.CaptureSnapshot();
        AssertVisualBounds(committed, OwnerVisualId, resizedOwnerBounds);
        AssertAttachment(
            committed,
            InitialPlacement,
            InitialPlacement.ResolveBounds(resizedOwnerBounds, AttachedSize),
            resizedOwnerBounds);
    }

    private static async ValueTask<AttachmentSessionContext> AttachAsync(
        DocumentSnapshot initial,
        EditorStateSnapshot editorState)
    {
        var reconstruction = DocumentReconstructor.Reconstruct(initial);
        Assert.True(
            reconstruction.Succeeded,
            string.Join(Environment.NewLine, reconstruction.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var document = Assert.IsType<Document>(reconstruction.Document);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(BuildPipelineResult(initial, editorState));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(editorState),
            pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        return new AttachmentSessionContext(
            Assert.IsType<EditingSession>(attachment.Session),
            pipeline,
            document);
    }

    private static void EnqueueSceneRebuild(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));

    private static void EnqueueDocumentRun(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueuePreservingNodeLayout((snapshot, _, editorState, _) =>
            ValueTask.FromResult(BuildPipelineResult(snapshot, editorState)));

    private static EditingSessionPipelineResult BuildPipelineResult(
        DocumentSnapshot snapshot,
        EditorStateSnapshot editorState)
    {
        var artifacts = BuildArtifacts(snapshot);
        return ControlledEditingSessionPipeline.Success(
            artifacts,
            snapshot.VisualModel,
            editorState);
    }

    private static Canvas2DScene BuildScene(
        DocumentSnapshot snapshot,
        EditorStateSnapshot editorState)
    {
        var artifacts = BuildArtifacts(snapshot);
        var build = new Canvas2DSceneBuilder().Build(
            artifacts.ProjectedGraph,
            artifacts.LayoutResult,
            artifacts.RoutingResult,
            snapshot.VisualModel,
            editorState);
        return Assert.IsType<Canvas2DScene>(build.Scene);
    }

    private static EditingSessionPipelineArtifacts BuildArtifacts(DocumentSnapshot snapshot)
    {
        var ownerVisual = snapshot.VisualModel.VisualStates.Single(
            visual => visual.Id == OwnerVisualId);
        var attachedVisual = snapshot.VisualModel.VisualStates.Single(
            visual => visual.Id == AttachedVisualId);
        var attachedElement = snapshot.SemanticModel.Elements.Single(
            element => element.Id == AttachedId);
        var attachment = Assert.IsType<BoundaryAttachmentPlacement>(
            attachedVisual.BoundaryAttachment);
        var ownerNode = new ProjectedNode(
            Source("owner", OwnerId, OwnerVisualId),
            new ProjectedPlacementHint(
                ownerVisual.Position,
                ownerVisual.Size,
                ownerVisual.PlacementMode));
        var attachedNode = new ProjectedNode(
            Source("attached", AttachedId, AttachedVisualId),
            new ProjectedPlacementHint(
                attachedVisual.Position,
                attachedVisual.Size,
                attachedVisual.PlacementMode,
                new ProjectedBoundaryAttachment(
                    Assert.IsType<SemanticElementId>(attachedElement.AttachedToElementId),
                    attachment)),
            geometryInteractionPolicy:
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
        var graph = new ProjectedGraph(
            snapshot.DocumentId,
            snapshot.Revision,
            [ownerNode, attachedNode],
            []);
        var layout = new LayoutResult(
            snapshot.DocumentId,
            snapshot.Revision,
            LayoutAlgorithmId,
            new LayoutComputation(
            [
                Geometry(ownerNode, ownerVisual),
                Geometry(attachedNode, attachedVisual),
            ]));
        var routing = new RoutingResult(
            snapshot.DocumentId,
            snapshot.Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            new RoutingComputation([]));
        return new EditingSessionPipelineArtifacts(graph, layout, routing);
    }

    private static LayoutNodeGeometry Geometry(
        ProjectedNode node,
        VisualStateSnapshot visual) =>
        new(
            node.Id,
            new RectD(
                visual.Position.X,
                visual.Position.Y,
                visual.Size.Width,
                visual.Size.Height),
            Matrix2D.CreateTranslation(visual.Position.X, visual.Position.Y));

    private static ProjectionSourceTrace Source(
        string localKey,
        SemanticElementId semanticElementId,
        VisualStateId visualStateId) =>
        new(
            DocumentId,
            ProjectionRuleId,
            ProjectionSourceKind.SemanticElement,
            semanticElementId,
            NodeTypeId,
            localKey,
            visualStateId);

    private static DocumentSnapshot CreateSnapshot()
    {
        var attachedBounds = InitialPlacement.ResolveBounds(
            InitialOwnerBounds,
            AttachedSize);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
            [
                new SemanticElementSnapshot(OwnerId, NodeTypeId),
                new SemanticElementSnapshot(
                    AttachedId,
                    NodeTypeId,
                    attachedToElementId: OwnerId),
            ]),
            new VisualModelSnapshot(
                DocumentId,
                Revision,
            [
                Visual(OwnerVisualId, OwnerId, InitialOwnerBounds),
                Visual(
                    AttachedVisualId,
                    AttachedId,
                    attachedBounds,
                    InitialPlacement),
            ]),
            new DocumentMetadataSnapshot(DocumentId, Revision));
    }

    private static VisualStateSnapshot Visual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        RectD bounds,
        BoundaryAttachmentPlacement? attachment = null) =>
        new(
            visualStateId,
            semanticElementId,
            bounds.TopLeft,
            new SizeD(bounds.Width, bounds.Height),
            VisualPlacementMode.Manual,
            boundaryAttachment: attachment);

    private static bool BooleanMetadata(Canvas2DSceneItem item, string key) =>
        item.Metadata.TryGetValue(key, out var value) &&
        value.Kind == Contracts.Properties.PropertyValueKind.Boolean &&
        value.BooleanValue;

    private static bool IsResizeInteraction(Canvas2DSceneItem item) =>
        item.Origin.StableSourceKey?.StartsWith(
            "resize-handle:",
            StringComparison.Ordinal) == true ||
        item.Origin.StableSourceKey?.StartsWith(
            "resize-edge-zone:",
            StringComparison.Ordinal) == true;

    private static bool IsMovePreview(
        Canvas2DSceneItem item,
        VisualStateId visualStateId) =>
        item.Origin.VisualStateId == visualStateId &&
        item.Origin.StableSourceKey?.StartsWith(
            "move-preview:",
            StringComparison.Ordinal) == true &&
        item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle;

    private static Canvas2DSceneItem FindNode(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));

    private static Canvas2DSceneItem FindMovePreview(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item => IsMovePreview(item, visualStateId));

    private static Canvas2DSceneItem FindResizeInteraction(
        Canvas2DScene scene,
        SceneObjectId ownerId,
        string role) =>
        scene.Items.Single(item =>
            IsResizeInteraction(item) &&
            item.Origin.StableSourceKey?.Contains(
                $":{role}:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(ownerId));

    private static Canvas2DSceneItem FindResizePreview(
        Canvas2DScene scene,
        SceneObjectId ownerId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(ownerId));

    private static void AssertAttachment(
        DocumentSnapshot snapshot,
        BoundaryAttachmentPlacement expectedPlacement,
        RectD expectedAttachedBounds,
        RectD? expectedOwnerBounds = null)
    {
        Assert.Equal(
            OwnerId,
            snapshot.SemanticModel.Elements.Single(element => element.Id == AttachedId)
                .AttachedToElementId);
        AssertVisualBounds(
            snapshot,
            OwnerVisualId,
            expectedOwnerBounds ?? InitialOwnerBounds);
        var attached = snapshot.VisualModel.VisualStates.Single(
            visual => visual.Id == AttachedVisualId);
        Assert.Equal(expectedPlacement, attached.BoundaryAttachment);
        Assert.Equal(expectedAttachedBounds.TopLeft, attached.Position);
        Assert.Equal(
            new SizeD(expectedAttachedBounds.Width, expectedAttachedBounds.Height),
            attached.Size);
    }

    private static void AssertVisualBounds(
        DocumentSnapshot snapshot,
        VisualStateId visualStateId,
        RectD expectedBounds)
    {
        var visual = snapshot.VisualModel.VisualStates.Single(
            candidate => candidate.Id == visualStateId);
        Assert.Equal(expectedBounds.TopLeft, visual.Position);
        Assert.Equal(
            new SizeD(expectedBounds.Width, expectedBounds.Height),
            visual.Size);
    }

    private static PointD Center(RectD bounds) =>
        new(bounds.Left + (bounds.Width / 2d), bounds.Top + (bounds.Height / 2d));

    private sealed record AttachmentSessionContext(
        EditingSession Session,
        ControlledEditingSessionPipeline Pipeline,
        Document Document);
}
