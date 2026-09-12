using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSemanticSceneSelectionTests
{
    private static readonly SemanticElementId PoolId =
        new("test:n101:selection-pool");
    private static readonly DocumentScopeId PeerScopeId =
        new("test:n101:selection-peer");

    [Fact]
    public async Task SemanticAndVisualSceneSelectionsAreExclusiveAndTransient()
    {
        var context = await AttachAsync(includePeerScope: false);
        await using var session = context.Session;
        await using var controller = new Canvas2DInteractionController(session);
        var before = session.CaptureState();
        var header = Header(before.CurrentScene!);

        EnqueueScene(context, ModelProfileViewStateSnapshot.Empty);
        var semantic = await controller.PointerActivatedAsync(Center(header.Bounds));

        Assert.True(semantic.Succeeded);
        Assert.Empty(semantic.SessionState.EditorState.Selection);
        Assert.Equal(
            PoolId,
            semantic.SessionState.EditorState.SemanticSceneSelection);
        Assert.Equal(before.DocumentRevision, semantic.SessionState.DocumentRevision);
        Assert.Equal(before.HistoryStatus, semantic.SessionState.HistoryStatus);

        var semanticContext = await controller.PointerContextMenuAsync(Center(header.Bounds));
        Assert.Equal(PoolId, semanticContext.TargetOrigin?.SemanticElementId);
        Assert.Null(semanticContext.TargetOrigin?.VisualStateId);

        EnqueueScene(context, ModelProfileViewStateSnapshot.Empty);
        var processTarget = semantic.SessionState.CurrentScene!.Items.First(item =>
            item.IsVisible &&
            item.Origin.VisualStateId == context.Inputs.VisualModel.VisualStates[0].Id &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var visual = await controller.PointerActivatedAsync(Center(processTarget.Bounds));

        Assert.True(visual.Succeeded);
        Assert.Single(visual.SessionState.EditorState.Selection);
        Assert.Null(visual.SessionState.EditorState.SemanticSceneSelection);

        EnqueueScene(context, ModelProfileViewStateSnapshot.Empty);
        var semanticAgain = await controller.PointerActivatedAsync(Center(header.Bounds));

        Assert.True(semanticAgain.Succeeded);
        Assert.Empty(semanticAgain.SessionState.EditorState.Selection);
        Assert.Equal(
            PoolId,
            semanticAgain.SessionState.EditorState.SemanticSceneSelection);

        var background = PoolBackground(semanticAgain.SessionState.CurrentScene!);
        EnqueueScene(context, ModelProfileViewStateSnapshot.Empty);
        var poolBody = await controller.PointerContextMenuAsync(
            new PointD(background.Bounds.Right - 4d, background.Bounds.Bottom - 4d));

        Assert.Null(poolBody.TargetOrigin);
        Assert.Equal(
            PoolId,
            poolBody.SessionState.EditorState.SemanticSceneSelection);
        Assert.Equal(before.DocumentRevision, poolBody.SessionState.DocumentRevision);
        Assert.Equal(before.HistoryStatus, poolBody.SessionState.HistoryStatus);
    }

    [Fact]
    public async Task ScopeContainedPoolHeaderHoverRebuildsAndContextTargetRemainsExact()
    {
        var context = await AttachAsync(includePeerScope: false);
        await using var session = context.Session;
        await using var controller = new Canvas2DInteractionController(session);
        var before = session.CaptureState();
        var header = Header(before.CurrentScene!);
        EnqueueScene(context, ModelProfileViewStateSnapshot.Empty);

        var hovered = await controller.PointerMovedAsync(Center(header.Bounds));

        Assert.True(hovered.Succeeded, string.Join(
            Environment.NewLine,
            hovered.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(Canvas2DInteractionStatus.Updated, hovered.Status);
        Assert.Equal(header.Id, hovered.SessionState.EditorState.HoveredObjectId);
        var hoverOverlay = Assert.Single(
            hovered.SessionState.CurrentScene!.Items,
            item =>
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
                item.Origin.SemanticElementId == PoolId &&
                item.Origin.RelatedSceneObjectIds.Contains(header.Id));
        Assert.Null(hoverOverlay.Origin.VisualStateId);
        Assert.Equal(before.DocumentRevision, hovered.SessionState.DocumentRevision);
        Assert.Equal(before.HistoryStatus, hovered.SessionState.HistoryStatus);

        EnqueueScene(context, ModelProfileViewStateSnapshot.Empty);
        var menu = await controller.PointerContextMenuAsync(Center(header.Bounds));

        Assert.True(menu.Succeeded);
        Assert.Equal(header.Id, menu.TargetId);
        Assert.Equal(PoolId, menu.TargetOrigin?.SemanticElementId);
        Assert.Null(menu.TargetOrigin?.VisualStateId);
    }

    [Fact]
    public async Task NavigationAndProfileHideClearPoolSemanticSelection()
    {
        var navigationContext = await AttachAsync(includePeerScope: true);
        await using (navigationContext.Session)
        await using (var controller = new Canvas2DInteractionController(
            navigationContext.Session))
        {
            EnqueueScene(navigationContext, ModelProfileViewStateSnapshot.Empty);
            var selected = await controller.PointerActivatedAsync(
                Center(Header(navigationContext.Session.CaptureState().CurrentScene!).Bounds));
            Assert.Equal(
                PoolId,
                selected.SessionState.EditorState.SemanticSceneSelection);

            navigationContext.Pipeline.EnqueueScopedFull(
                (document, scopeId, editorState, _) => ValueTask.FromResult(
                    PipelineResult(
                        navigationContext,
                        document,
                        scopeId,
                        editorState,
                        ModelProfileViewStateSnapshot.Empty)));
            var navigation = await navigationContext.Session.NavigateToScopeAsync(PeerScopeId);

            Assert.True(navigation.Succeeded, string.Join(
                Environment.NewLine,
                navigation.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Assert.Null(navigation.State.EditorState.SemanticSceneSelection);
        }

        var visibilityContext = await AttachAsync(includePeerScope: false);
        await using (visibilityContext.Session)
        await using (var controller = new Canvas2DInteractionController(
            visibilityContext.Session))
        {
            var before = visibilityContext.Session.CaptureState();
            EnqueueScene(visibilityContext, ModelProfileViewStateSnapshot.Empty);
            _ = await controller.PointerActivatedAsync(
                Center(Header(before.CurrentScene!).Bounds));
            var hidden = new ModelProfileViewStateSnapshot(
                [BpmnModelProfiles.OrganizationalId]);
            EnqueueScene(visibilityContext, hidden);

            var result = await visibilityContext.Session.UpdateModelProfileViewStateAsync(hidden);

            Assert.True(result.Succeeded);
            Assert.Null(result.State.EditorState.SemanticSceneSelection);
            Assert.Empty(result.State.CurrentScene!.Items.Where(
                item => item.Origin.SemanticElementId == PoolId));
            Assert.Equal(before.DocumentRevision, result.State.DocumentRevision);
            Assert.Equal(before.HistoryStatus, result.State.HistoryStatus);
            Assert.Same(before.ProjectedGraph, result.State.ProjectedGraph);
            Assert.Same(before.LayoutResult, result.State.LayoutResult);
            Assert.Same(before.RoutingResult, result.State.RoutingResult);
        }
    }

    [Fact]
    public void EditorStateRejectsSimultaneousVisualAndSemanticSelection()
    {
        var visualId = new VisualStateId("test:n101:visual-selection");

        var exception = Assert.Throws<ArgumentException>(() => new EditorStateSnapshot(
            selection: [visualId],
            semanticSceneSelection: PoolId));

        Assert.Contains("cannot coexist", exception.Message, StringComparison.Ordinal);
    }

    private static async ValueTask<SelectionTestContext> AttachAsync(bool includePeerScope)
    {
        var inputs = Canvas2DSceneTestData.CreateWithNodeLabels();
        var document = Reconstruct(CreateSnapshot(inputs, includePeerScope));
        var rootScopeId = document.SemanticModel.RootScopeId;
        var builder = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.N100.SceneContributors.Add(
                OrganizationalPoolSceneContributor.CreateRegistration(
                    new OrganizationalElementEligibilityPolicy(static _ => true))));
        var pipeline = new ControlledEditingSessionPipeline();
        var context = new SelectionTestContext(
            inputs,
            document,
            rootScopeId,
            builder,
            pipeline,
            Session: null!);
        pipeline.EnqueueFull(PipelineResult(
            context,
            document.CaptureSnapshot(),
            rootScopeId,
            EditorStateSnapshot.Empty,
            ModelProfileViewStateSnapshot.Empty));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(),
            new LayoutEngine(),
            new AlgorithmId("test:n101:unused-layout"),
            new RoutingEngine(),
            new AlgorithmId("test:n101:unused-routing"),
            builder,
            modelProfileCatalog: new ModelProfileCatalog(BpmnModelProfiles.Definitions));
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            configuration,
            pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        return context with { Session = attachment.Session! };
    }

    private static void EnqueueScene(
        SelectionTestContext context,
        ModelProfileViewStateSnapshot viewState) =>
        context.Pipeline.EnqueueScene((artifacts, _, editorState, _) =>
            ValueTask.FromResult(PipelineResult(
                context,
                context.Document.CaptureSnapshot(),
                artifacts.ScopeId,
                editorState,
                viewState)));

    private static EditingSessionPipelineResult PipelineResult(
        SelectionTestContext context,
        DocumentSnapshot document,
        DocumentScopeId scopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot viewState)
    {
        var artifacts = new EditingSessionPipelineArtifacts(
            scopeId,
            context.Inputs.Graph,
            context.Inputs.Layout,
            context.Inputs.Routing);
        var build = context.Builder.Build(
            document,
            scopeId,
            viewState,
            ModelProfileElementViewStateSnapshot.Empty,
            artifacts.ProjectedGraph,
            artifacts.LayoutResult,
            artifacts.RoutingResult,
            document.VisualModel,
            editorState);
        Assert.True(build.Succeeded, string.Join(
            Environment.NewLine,
            build.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return EditingSessionPipelineResult.Success(artifacts, build.Scene!);
    }

    private static DocumentSnapshot CreateSnapshot(
        Canvas2DSceneTestData inputs,
        bool includePeerScope)
    {
        var source = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                source.DocumentId,
                source.Revision,
                source.SemanticModel.Elements.Concat(
                [
                    OrganizationalSemanticFactory.CreatePool(PoolId, "Warehouse"),
                ]),
                source.SemanticModel.Relationships,
                nestedScopes: includePeerScope
                    ? [new DocumentScopeSnapshot(PeerScopeId)]
                    : null,
                modelProfiles: new ModelProfileStateSnapshot(
                    [BpmnModelProfiles.OrganizationalId]),
                profileAssignments: source.SemanticModel.Elements.Select(element =>
                    new ModelProfileElementAssignmentSnapshot(
                        BpmnModelProfiles.OrganizationalId, element.Id, PoolId))),
            source.VisualModel,
            new DocumentMetadataSnapshot(
                source.DocumentId,
                source.Revision));
    }

    private static Document Reconstruct(DocumentSnapshot snapshot)
    {
        var result = DocumentReconstructor.Reconstruct(snapshot);
        Assert.True(result.Succeeded);
        return result.Document!;
    }

    private static Canvas2DSceneItem Header(Canvas2DScene scene) =>
        Assert.Single(
            scene.Items,
            item => item.Origin.SemanticElementId == PoolId &&
                    Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item));

    private static Canvas2DSceneItem PoolBackground(Canvas2DScene scene) =>
        Assert.Single(
            scene.Items,
            item => item.Origin.SemanticElementId == PoolId &&
                    item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
                    item.HitTestPolicy.Mode == Canvas2DHitTestMode.None);

    private static PointD Center(RectD bounds) =>
        new(
            bounds.Left + (bounds.Width / 2d),
            bounds.Top + (bounds.Height / 2d));

    private sealed record SelectionTestContext(
        Canvas2DSceneTestData Inputs,
        Document Document,
        DocumentScopeId RootScopeId,
        Canvas2DSceneBuilder Builder,
        ControlledEditingSessionPipeline Pipeline,
        EditingSession Session);
}
