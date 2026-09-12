using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DPresentationInteractionTests
{
    private static readonly ModelProfileId TestProfileId = new("test:spatial");
    private static readonly Canvas2DSpatialEditPlannerCatalog SpatialEditPlanners = new(
        [new Canvas2DSpatialEditPlannerRegistration(TestProfileId, new PassThroughPlanner())]);

    private sealed class PassThroughPlanner : ICanvas2DSpatialEditPlanner
    {
        public Canvas2DSpatialEditPlanResult Plan(Canvas2DSpatialEditRequest request) =>
            Canvas2DSpatialEditPlanResult.Success(request.BaseCommand);
    }

    [Fact]
    public async Task SpatialPresentationRetainsVisualSelectionAuthorityWithoutNavigation()
    {
        var attached = await AttachPresentedNodeAsync();
        await using var session = attached.Session;
        await using var controller = new Canvas2DInteractionController(
            session, spatialEditPlanners: SpatialEditPlanners);
        var before = session.CaptureState();
        attached.Pipeline.EnqueueScene(EditingSessionPipelineResult.Success(
            attached.Artifacts,
            attached.Scene));

        var selected = await controller.PointerActivatedAsync(Center(attached.PresentedNode.Bounds));

        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        Assert.Equal(
            attached.PresentedNode.Origin.VisualStateId!,
            Assert.Single(selected.SessionState.EditorState.Selection));
        Assert.Null(selected.SessionState.EditorState.SemanticSceneSelection);
        Assert.Equal(before.ActiveScopeId, selected.SessionState.ActiveScopeId);
        Assert.Equal(before.DocumentRevision, selected.SessionState.DocumentRevision);
        Assert.Equal(before.HistoryStatus, selected.SessionState.HistoryStatus);
        Assert.Same(attached.Instance, selected.HitResult?.SpatialRegion);
    }

    [Fact]
    public async Task SpatialTranslatedMovePersistsProcessLocalCoordinates()
    {
        var attached = await AttachPresentedNodeAsync();
        await using var session = attached.Session;
        await using var controller = new Canvas2DInteractionController(
            session, spatialEditPlanners: SpatialEditPlanners);
        var before = session.CaptureState();
        var visualStateId = attached.PresentedNode.Origin.VisualStateId!;
        Assert.True(attached.Document.VisualModel.TryGetVisualState(
            visualStateId,
            out var beforeVisual));
        var start = Center(attached.PresentedNode.Bounds);
        var movement = new VectorD(40d, 20d);
        attached.Pipeline.EnqueueScene(EditingSessionPipelineResult.Success(
            attached.Artifacts,
            attached.Scene));
        attached.Pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(
                attached.Inputs,
                snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });

        _ = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            4402,
            start,
            buttons: 1));
        var moving = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            4402,
            start + movement,
            buttons: 1));
        var released = await controller.PointerReleasedAsync(new Canvas2DPointerInput(
            4402,
            start + movement));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moving.Status);
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(attached.Document.VisualModel.TryGetVisualState(
            visualStateId,
            out var afterVisual));
        Assert.Equal(beforeVisual!.Position + movement, afterVisual!.Position);
        Assert.NotEqual(
            attached.Instance.MapLocalToScene(beforeVisual.Position) + movement,
            afterVisual.Position);
        Assert.Equal(before.DocumentRevision.Increment(), released.SessionState.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1,
            released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(before.ActiveScopeId, released.SessionState.ActiveScopeId);
    }

    [Fact]
    public async Task TranslatedSpatialRouteContextAddPersistsOnlyProcessLocalPoints()
    {
        var attached = await AttachPresentedConnectorAsync([]);
        await using var session = attached.Session;
        await using var controller = new Canvas2DInteractionController(
            session, spatialEditPlanners: SpatialEditPlanners);
        var before = session.CaptureState();
        var localPath = Canvas2DConnectorPathMetadata.Resolve(attached.PresentedConnector);
        var localInsertion = new PointD(
            localPath[0].X + ((localPath[1].X - localPath[0].X) * 0.5d),
            localPath[0].Y + ((localPath[1].Y - localPath[0].Y) * 0.5d));
        var sceneInsertion = attached.Instance.MapLocalToScene(localInsertion);

        var context = await controller.PointerContextMenuAsync(sceneInsertion);

        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            context.ConnectorRouteContextAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, action.Kind);
        Assert.Equal(localInsertion, action.DocumentPoint);
        Assert.Equal(localInsertion, action.RoutePoint);
        Assert.True(action.TryResolveTargetRoute(
            attached.Scene,
            [],
            out var targetRoute));
        Assert.Equal(3, targetRoute.Length);
        AssertPoint(localPath[0], targetRoute[0]);
        AssertPoint(localInsertion, targetRoute[1]);
        AssertPoint(localPath[^1], targetRoute[2]);
        attached.Pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(
                attached.Inputs,
                snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });

        var committed = await session.ExecuteForSelectedVisualStateAsync(
            attached.VisualStateId,
            new UpdateConnectionRouteCommand(
                before.DocumentId,
                before.DocumentRevision,
                attached.VisualStateId,
                targetRoute));

        Assert.True(committed.IsCommitted);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        var persisted = attached.Document.CaptureSnapshot().VisualModel.VisualStates.Single(
            visual => visual.Id == attached.VisualStateId);
        Assert.Equal(targetRoute, persisted.Route);
        Assert.DoesNotContain(
            persisted.Route,
            point => point == attached.Instance.MapLocalToScene(localInsertion));
    }

    [Fact]
    public async Task TranslatedSpatialRouteContextDeleteUsesAuthoredProcessLocalRoute()
    {
        var authoredRoute = new[]
        {
            new PointD(110d, 45d),
            new PointD(160d, 45d),
            new PointD(210d, 45d),
        };
        var attached = await AttachPresentedConnectorAsync(authoredRoute, includeBendHandle: true);
        await using var session = attached.Session;
        await using var controller = new Canvas2DInteractionController(
            session, spatialEditPlanners: SpatialEditPlanners);
        var before = session.CaptureState();
        var bendHandle = Assert.IsType<Canvas2DSceneItem>(attached.BendHandle);

        var context = await controller.PointerContextMenuAsync(Center(bendHandle.Bounds));

        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            context.ConnectorRouteContextAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.DeletePoint, action.Kind);
        Assert.Equal(authoredRoute[1], action.RoutePoint);
        Assert.True(action.TryResolveTargetRoute(
            attached.Scene,
            authoredRoute,
            out var targetRoute));
        Assert.Empty(targetRoute);
        attached.Pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(
                attached.Inputs,
                snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });

        var committed = await session.ExecuteForSelectedVisualStateAsync(
            attached.VisualStateId,
            new UpdateConnectionRouteCommand(
                before.DocumentId,
                before.DocumentRevision,
                attached.VisualStateId,
                targetRoute));

        Assert.True(committed.IsCommitted);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.Empty(attached.Document.CaptureSnapshot().VisualModel.VisualStates.Single(
            visual => visual.Id == attached.VisualStateId).Route);
    }

    private static async ValueTask<PresentedNodeTestContext> AttachPresentedNodeAsync()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs);
        var canonicalScene = EditingSessionTestHarness.CreateScene(
            artifacts,
            inputs.VisualModel,
            EditorStateSnapshot.Empty);
        var canonicalNode = canonicalScene.Items.First(item =>
            Canvas2DNodeBodyMetadata.IsNodeBody(item) &&
            item.Origin.VisualStateId is not null);
        var instance = new Canvas2DSpatialRegion(
            new Canvas2DSpatialRegionId("test:participant-instance"),
            TestProfileId,
            null,
            Matrix2D.CreateTranslation(300d, 400d),
            new RectD(300d, 400d, 500d, 300d));
        var presentedNode = new Canvas2DSceneItem(
            canonicalNode.Id,
            canonicalNode.Layer,
            canonicalNode.ZIndex,
            canonicalNode.Geometry,
            canonicalNode.Origin,
            canonicalNode.Transform.Then(instance.LocalToSceneTransform),
            canonicalNode.Clip is { } clip ? instance.MapLocalToScene(clip) : null,
            canonicalNode.Style,
            canonicalNode.IsVisible,
            canonicalNode.HitTestPolicy,
            canonicalNode.PersistentAppearance,
            canonicalNode.Metadata,
            instance.MapLocalToScene(canonicalNode.Bounds),
            instance);
        var scene = new Canvas2DScene(
            canonicalScene.DocumentId,
            canonicalScene.SourceRevision,
            canonicalScene.LayoutAlgorithmId,
            canonicalScene.RoutingAlgorithmId,
            canonicalScene.Configuration,
            canonicalScene.Contributors,
            canonicalScene.Viewport,
            canonicalScene.ViewportTransform,
            canonicalScene.ActiveToolId,
            canonicalScene.FocusTargetId,
            canonicalScene.ToolState,
            canonicalScene.ContributorMetadata,
            [presentedNode],
            canonicalScene.Diagnostics,
            new Canvas2DSpatialPresentationPlan([instance], [], instance.LocalToSceneTransform));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(EditingSessionPipelineResult.Success(artifacts, scene));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(EditorStateSnapshot.Empty),
            pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        return new PresentedNodeTestContext(
            attachment.Session!,
            pipeline,
            document,
            inputs,
            artifacts,
            scene,
            presentedNode,
            instance);
    }

    private static async ValueTask<PresentedConnectorTestContext> AttachPresentedConnectorAsync(
        IEnumerable<PointD> route,
        bool includeBendHandle = false)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var visualStateId = inputs.Graph.Edges.Single().Source.VisualStateId!;
        var visualModel = new VisualModelSnapshot(
            inputs.VisualModel.DocumentId,
            inputs.VisualModel.Revision,
            inputs.VisualModel.VisualStates.Select(visual =>
                visual.Id != visualStateId
                    ? visual
                    : new VisualStateSnapshot(
                        visual.Id,
                        visual.SemanticElementId,
                        visual.Position,
                        visual.Size,
                        visual.PlacementMode,
                        route,
                        visual.Properties,
                        visual.ConnectorAnchors,
                        visual.SourceAnchorId,
                        visual.TargetAnchorId,
                        visual.BoundaryAttachment)));
        inputs = inputs.WithVisualModel(visualModel);
        var selectedEditorState = new EditorStateSnapshot(selection: [visualStateId]);
        var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs);
        var canonicalScene = EditingSessionTestHarness.CreateScene(
            artifacts,
            visualModel,
            selectedEditorState);
        var canonicalConnector = canonicalScene.Items.First(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == visualStateId &&
            !(item.Metadata.TryGetValue(
                    Canvas2DConnectorArrowMetadata.TargetArrow,
                    out var targetArrow) &&
                targetArrow.Kind == PropertyValueKind.Boolean &&
                targetArrow.BooleanValue));
        var instance = new Canvas2DSpatialRegion(
            new Canvas2DSpatialRegionId("test:connector-presentation"),
            TestProfileId,
            null,
            Matrix2D.CreateTranslation(300d, 400d),
            new RectD(300d, 400d, 800d, 600d));
        var presentedConnector = Present(canonicalConnector, instance);
        Canvas2DSceneItem? bendHandle = null;
        if (includeBendHandle)
        {
            var editableRoute = Canvas2DConnectorPathMetadata.ResolveEditable(presentedConnector);
            var bend = presentedConnector.Transform.TransformPoint(editableRoute[1]);
            var extent = Canvas2DRouteGestureMetadata.HandleExtent;
            var bounds = new RectD(
                bend.X - (extent / 2d),
                bend.Y - (extent / 2d),
                extent,
                extent);
            bendHandle = new Canvas2DSceneItem(
                new SceneObjectId("scene:test:presented-bend-handle"),
                Canvas2DSceneLayer.Overlay,
                5000,
                Canvas2DSceneGeometry.Rectangle(bounds),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.VisualState |
                    Canvas2DSceneOriginCategory.EditorState,
                    visualStateId: visualStateId,
                    stableSourceKey: "test:presented-bend-handle",
                    relatedSceneObjectIds: [presentedConnector.Id]),
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                metadata:
                [
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DRouteGestureMetadata.HandleRole,
                        PropertyValue.FromText(Canvas2DRouteGestureMetadata.BendRole)),
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DRouteGestureMetadata.TargetSceneObjectId,
                        PropertyValue.FromText(presentedConnector.Id.Value)),
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DRouteGestureMetadata.TargetVisualStateId,
                        PropertyValue.FromText(visualStateId.Value)),
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DRouteGestureMetadata.BendIndex,
                        PropertyValue.FromInteger(1)),
                ],
                bounds: bounds,
                spatialRegion: instance);
        }

        var scene = new Canvas2DScene(
            canonicalScene.DocumentId,
            canonicalScene.SourceRevision,
            canonicalScene.LayoutAlgorithmId,
            canonicalScene.RoutingAlgorithmId,
            canonicalScene.Configuration,
            canonicalScene.Contributors,
            canonicalScene.Viewport,
            canonicalScene.ViewportTransform,
            canonicalScene.ActiveToolId,
            canonicalScene.FocusTargetId,
            canonicalScene.ToolState,
            canonicalScene.ContributorMetadata,
            bendHandle is null ? [presentedConnector] : [presentedConnector, bendHandle],
            canonicalScene.Diagnostics,
            new Canvas2DSpatialPresentationPlan([instance], [], instance.LocalToSceneTransform));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(EditingSessionPipelineResult.Success(artifacts, scene));
        pipeline.EnqueueScene(EditingSessionPipelineResult.Success(artifacts, scene));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(selectedEditorState),
            pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        return new PresentedConnectorTestContext(
            attachment.Session!,
            pipeline,
            document,
            inputs,
            scene,
            presentedConnector,
            bendHandle,
            instance,
            visualStateId);
    }

    private static Canvas2DSceneItem Present(
        Canvas2DSceneItem source,
        Canvas2DSpatialRegion instance) =>
        new(
            source.Id,
            source.Layer,
            source.ZIndex,
            source.Geometry,
            source.Origin,
            source.Transform.Then(instance.LocalToSceneTransform),
            source.Clip is { } clip ? instance.MapLocalToScene(clip) : null,
            source.Style,
            source.IsVisible,
            source.HitTestPolicy,
            source.PersistentAppearance,
            source.Metadata,
            instance.MapLocalToScene(source.Bounds),
            instance,
            new Canvas2DConnectorPresentationMapping(
                Canvas2DConnectorPathMetadata.ResolveEditable(source),
                Canvas2DConnectorPathMetadata.ResolveEditable(source).Select(instance.MapLocalToScene),
                instance.LocalToSceneTransform, instance.Id, instance.Id));

    private static PointD Center(RectD bounds) =>
        new(bounds.Left + (bounds.Width / 2d), bounds.Top + (bounds.Height / 2d));

    private static void AssertPoint(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, 10);
        Assert.Equal(expected.Y, actual.Y, 10);
    }

    private sealed record PresentedNodeTestContext(
        EditingSession Session,
        ControlledEditingSessionPipeline Pipeline,
        Inceptus.DocumentEngine.Runtime.Documents.Document Document,
        Canvas2DSceneTestData Inputs,
        EditingSessionPipelineArtifacts Artifacts,
        Canvas2DScene Scene,
        Canvas2DSceneItem PresentedNode,
        Canvas2DSpatialRegion Instance);

    private sealed record PresentedConnectorTestContext(
        EditingSession Session,
        ControlledEditingSessionPipeline Pipeline,
        Inceptus.DocumentEngine.Runtime.Documents.Document Document,
        Canvas2DSceneTestData Inputs,
        Canvas2DScene Scene,
        Canvas2DSceneItem PresentedConnector,
        Canvas2DSceneItem? BendHandle,
        Canvas2DSpatialRegion Instance,
        VisualStateId VisualStateId);
}
