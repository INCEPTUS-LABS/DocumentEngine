using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionModelProfileViewStateTests
{
    private static readonly ModelProfileId OrganizationalProfileId =
        new("test:profile/organizational");
    private static readonly ModelProfileId StageProfileId =
        new("test:profile/stage");
    private static readonly DocumentScopeId PeerScopeId =
        new("test:profile/peer-process");
    private static readonly DocumentScopeId NestedScopeId =
        new("test:profile/nested-process");
    private static readonly ModelProfileCatalog ProfileCatalog = new(
    [
        new ModelProfileDefinition(OrganizationalProfileId, "Organizational profile"),
        new ModelProfileDefinition(StageProfileId, "Stage profile", order: 1),
    ]);

    [Fact]
    public async Task BatchUpdateIsTransientAndRecomposesOnlyScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateDocument(
            inputs,
            new ModelProfileStateSnapshot(
                [OrganizationalProfileId, StageProfileId]));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var beforeDocument = document.CaptureSnapshot();
        var before = session.CaptureState();
        var requested = new ModelProfileViewStateSnapshot(
            [OrganizationalProfileId, StageProfileId]);
        var notificationCount = 0;
        session.StateChanged += (_, _) => notificationCount++;

        EnqueueSceneRebuild(pipeline);
        var result = await session.UpdateModelProfileViewStateAsync(requested);
        var after = session.CaptureState();

        Assert.True(result.Succeeded);
        Assert.Same(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, document.Revision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.True(after.Generation.Value > before.Generation.Value);
        Assert.NotSame(before.CurrentScene, after.CurrentScene);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Same(ProfileCatalog, session.ModelProfileCatalog);
        Assert.Same(ProfileCatalog, after.ModelProfileCatalog);
        Assert.Same(requested, after.ModelProfileViewState);
        Assert.False(after.IsModelProfileEffectivelyVisible(OrganizationalProfileId));
        Assert.False(after.IsModelProfileEffectivelyVisible(StageProfileId));
        Assert.True(notificationCount >= 1);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);

        var repeated = await session.UpdateModelProfileViewStateAsync(
            new ModelProfileViewStateSnapshot(
                [OrganizationalProfileId, StageProfileId]));

        Assert.True(repeated.Succeeded);
        Assert.Equal(after.Generation, repeated.State.Generation);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Same(beforeDocument, document.CaptureSnapshot());
    }

    [Fact]
    public async Task AvailabilityClampsEffectiveVisibilityAndRetainsViewPreference()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateDocument(
            inputs,
            new ModelProfileStateSnapshot([OrganizationalProfileId]));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var initial = session.CaptureState();

        Assert.True(initial.ModelProfileViewState.IsPreferredVisible(
            OrganizationalProfileId));
        Assert.True(initial.IsModelProfileEffectivelyVisible(OrganizationalProfileId));
        Assert.True(initial.ModelProfileViewState.IsPreferredVisible(StageProfileId));
        Assert.False(initial.IsModelProfileEffectivelyVisible(StageProfileId));

        var hidden = ModelProfileViewStateSnapshot.Empty.WithPreferredVisibility(
            OrganizationalProfileId,
            isVisible: false);
        EnqueueSceneRebuild(pipeline);
        var visibility = await session.UpdateModelProfileViewStateAsync(hidden);
        Assert.True(visibility.Succeeded);
        Assert.False(session.CaptureState().IsModelProfileEffectivelyVisible(
            OrganizationalProfileId));

        EnqueueSceneRebuild(pipeline);
        var disabled = await session.ExecuteAsync(new SetModelProfileAvailabilityCommand(
            document.DocumentId,
            document.Revision,
            [new ModelProfileAvailabilityChange(
                OrganizationalProfileId,
                isAvailable: false)]));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var unavailable = session.CaptureState();

        Assert.True(disabled.IsCommitted);
        Assert.Same(hidden, unavailable.ModelProfileViewState);
        Assert.False(unavailable.ModelProfileState.IsAvailable(OrganizationalProfileId));
        Assert.False(unavailable.IsModelProfileEffectivelyVisible(
            OrganizationalProfileId));

        EnqueueSceneRebuild(pipeline);
        var enabled = await session.ExecuteAsync(new SetModelProfileAvailabilityCommand(
            document.DocumentId,
            document.Revision,
            [new ModelProfileAvailabilityChange(
                OrganizationalProfileId,
                isAvailable: true)]));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var availableAgain = session.CaptureState();

        Assert.True(enabled.IsCommitted);
        Assert.Same(hidden, availableAgain.ModelProfileViewState);
        Assert.True(availableAgain.ModelProfileState.IsAvailable(OrganizationalProfileId));
        Assert.False(availableAgain.IsModelProfileEffectivelyVisible(
            OrganizationalProfileId));
        Assert.Equal(2, availableAgain.HistoryStatus.EntryCount);

        var beforeShow = document.CaptureSnapshot();
        var beforeShowState = availableAgain;
        EnqueueSceneRebuild(pipeline);
        var shown = await session.UpdateModelProfileViewStateAsync(
            ModelProfileViewStateSnapshot.Empty);
        var final = session.CaptureState();

        Assert.True(shown.Succeeded);
        Assert.Same(beforeShow, document.CaptureSnapshot());
        Assert.Equal(beforeShowState.HistoryStatus, final.HistoryStatus);
        Assert.True(final.Generation.Value > beforeShowState.Generation.Value);
        Assert.True(final.IsModelProfileEffectivelyVisible(OrganizationalProfileId));
        Assert.Equal(4, pipeline.SceneRebuildCount);
    }

    [Fact]
    public async Task MainPeerAndNestedViewsRetainIndependentPreferences()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var rootScopeId = new DocumentScopeId(inputs.Graph.DocumentId.Value);
        var document = CreateDocument(
            inputs,
            new ModelProfileStateSnapshot(
                [OrganizationalProfileId, StageProfileId]),
            new DocumentScopeSnapshot(PeerScopeId),
            new DocumentScopeSnapshot(NestedScopeId, rootScopeId));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        EnqueueSceneRebuild(pipeline);
        await session.UpdateModelProfileViewStateAsync(new ModelProfileViewStateSnapshot(
            [OrganizationalProfileId]));

        EnqueueScopeRun(pipeline);
        Assert.True((await session.NavigateToScopeAsync(PeerScopeId)).Succeeded);
        Assert.True(session.CaptureState().IsModelProfileEffectivelyVisible(
            OrganizationalProfileId));
        Assert.True(session.CaptureState().IsModelProfileEffectivelyVisible(StageProfileId));
        EnqueueSceneRebuild(pipeline);
        await session.UpdateModelProfileViewStateAsync(new ModelProfileViewStateSnapshot(
            [StageProfileId]));

        EnqueueScopeRun(pipeline);
        Assert.True((await session.NavigateToScopeAsync(NestedScopeId)).Succeeded);
        Assert.True(session.CaptureState().IsModelProfileEffectivelyVisible(
            OrganizationalProfileId));
        Assert.True(session.CaptureState().IsModelProfileEffectivelyVisible(StageProfileId));
        EnqueueSceneRebuild(pipeline);
        await session.UpdateModelProfileViewStateAsync(new ModelProfileViewStateSnapshot(
            [OrganizationalProfileId, StageProfileId]));

        EnqueueSceneRebuild(pipeline);
        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);
        var rootView = session.CaptureState().ModelProfileViewState;
        Assert.Equal(OrganizationalProfileId, Assert.Single(rootView.HiddenProfileIds));

        EnqueueSceneRebuild(pipeline);
        Assert.True((await session.NavigateToScopeAsync(PeerScopeId)).Succeeded);
        var peerView = session.CaptureState().ModelProfileViewState;
        Assert.Equal(StageProfileId, Assert.Single(peerView.HiddenProfileIds));

        EnqueueSceneRebuild(pipeline);
        Assert.True((await session.NavigateToScopeAsync(NestedScopeId)).Succeeded);
        var nestedView = session.CaptureState().ModelProfileViewState;
        Assert.Equal(2, nestedView.HiddenProfileIds.Length);
        Assert.Contains(OrganizationalProfileId, nestedView.HiddenProfileIds);
        Assert.Contains(StageProfileId, nestedView.HiddenProfileIds);
    }

    [Fact]
    public async Task DeletedScopePurgesItsCachedViewPreferenceBeforeIdentityReuse()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var rootScopeId = new DocumentScopeId(inputs.Graph.DocumentId.Value);
        var document = CreateDocument(
            inputs,
            new ModelProfileStateSnapshot([OrganizationalProfileId]),
            new DocumentScopeSnapshot(PeerScopeId));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        EnqueueScopeRun(pipeline);
        Assert.True((await session.NavigateToScopeAsync(PeerScopeId)).Succeeded);
        EnqueueSceneRebuild(pipeline);
        await session.UpdateModelProfileViewStateAsync(new ModelProfileViewStateSnapshot(
            [OrganizationalProfileId]));
        EnqueueSceneRebuild(pipeline);
        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);

        await InstallScopesAsync(document, session, pipeline, []);
        await InstallScopesAsync(
            document,
            session,
            pipeline,
            [new DocumentScopeSnapshot(PeerScopeId)]);

        EnqueueScopeRun(pipeline);
        Assert.True((await session.NavigateToScopeAsync(PeerScopeId)).Succeeded);
        var recreated = session.CaptureState();

        Assert.Empty(recreated.ModelProfileViewState.HiddenProfileIds);
        Assert.True(recreated.IsModelProfileEffectivelyVisible(OrganizationalProfileId));
    }

    [Fact]
    public async Task NewerVisibilityChangeSupersedesInFlightSceneWithoutStaleInstall()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = CreateDocument(
            inputs,
            new ModelProfileStateSnapshot([OrganizationalProfileId]),
            new DocumentScopeSnapshot(PeerScopeId));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visibilityStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseVisibility = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, _) =>
        {
            visibilityStarted.SetResult();
            await releaseVisibility.Task;
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState);
        });

        var visibilityTask = session.UpdateModelProfileViewStateAsync(
            new ModelProfileViewStateSnapshot([OrganizationalProfileId])).AsTask();
        await visibilityStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EnqueueSceneRebuild(pipeline);
        var newerViewState = new ModelProfileViewStateSnapshot([StageProfileId]);
        var newerVisibility = await session.UpdateModelProfileViewStateAsync(newerViewState);
        releaseVisibility.SetResult();
        var visibility = await visibilityTask.WaitAsync(TimeSpan.FromSeconds(5));
        var final = session.CaptureState();

        Assert.True(newerVisibility.Succeeded);
        Assert.Equal(EditingSessionOperationStatus.Superseded, visibility.Status);
        Assert.Equal(document.SemanticModel.RootScopeId, final.ActiveScopeId);
        Assert.Same(newerViewState, final.ModelProfileViewState);
        Assert.NotNull(final.CurrentScene);
        Assert.Equal(final.DocumentId, final.CurrentScene!.DocumentId);
        Assert.Equal(final.DocumentRevision, final.CurrentScene.SourceRevision);
    }

    [Fact]
    public async Task NavigationCannotOvertakeInFlightProfileSceneProviderResult()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var rootScopeId = new DocumentScopeId(inputs.Graph.DocumentId.Value);
        var document = CreateDocument(
            inputs,
            new ModelProfileStateSnapshot([OrganizationalProfileId]),
            new DocumentScopeSnapshot(PeerScopeId));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var providerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, _) =>
        {
            providerStarted.TrySetResult();
            await releaseProvider.Task;
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState);
        });

        var visibilityTask = session.UpdateModelProfileViewStateAsync(
            new ModelProfileViewStateSnapshot([OrganizationalProfileId])).AsTask();
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var navigationWhileProviderRuns = await session.NavigateToScopeAsync(PeerScopeId);
        Assert.Equal(
            EditingSessionOperationStatus.Rejected,
            navigationWhileProviderRuns.Status);
        Assert.Equal(rootScopeId, navigationWhileProviderRuns.State.ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Rebuilding, navigationWhileProviderRuns.State.Status);

        releaseProvider.TrySetResult();
        var visibility = await visibilityTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(visibility.Succeeded);
        var afterProvider = session.CaptureState();
        Assert.Equal(rootScopeId, afterProvider.ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Ready, afterProvider.Status);
        Assert.Equal(afterProvider.DocumentRevision, afterProvider.CurrentScene!.SourceRevision);

        EnqueueScopeRun(pipeline);
        var navigation = await session.NavigateToScopeAsync(PeerScopeId);
        var final = session.CaptureState();
        Assert.True(navigation.Succeeded);
        Assert.Equal(PeerScopeId, final.ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Ready, final.Status);
        Assert.Equal(final.DocumentRevision, final.CurrentScene!.SourceRevision);
    }

    private static EditingSessionConfiguration Configuration()
    {
        var source = EditingSessionTestHarness.Configuration();
        return new EditingSessionConfiguration(
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
            source.DocumentChangedSubscribers,
            source.ConnectorAnchorPolicyProvider,
            ProfileCatalog,
            ModelProfileViewStateSnapshot.Empty);
    }

    private static Document CreateDocument(
        Canvas2DSceneTestData inputs,
        ModelProfileStateSnapshot modelProfiles,
        params DocumentScopeSnapshot[] scopes)
    {
        var source = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        return new Document(new DocumentSnapshot(
            new SemanticModelSnapshot(
                source.DocumentId,
                source.Revision,
                source.SemanticModel.Elements,
                source.SemanticModel.Relationships,
                scopes,
                source.SemanticModel.ScopeMemberships,
                modelProfiles),
            source.VisualModel,
            source.Metadata));
    }

    private static void EnqueueScopeRun(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueueScopedFull((snapshot, scopeId, editorState, _) =>
            ValueTask.FromResult(SuccessfulEmptyScope(snapshot, scopeId, editorState)));

    private static void EnqueueSceneRebuild(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));

    private static EditingSessionPipelineResult SuccessfulEmptyScope(
        DocumentSnapshot snapshot,
        DocumentScopeId scopeId,
        EditorStateSnapshot editorState)
    {
        var graph = new ProjectedGraph(snapshot.DocumentId, snapshot.Revision);
        var layout = new LayoutResult(
            snapshot.DocumentId,
            snapshot.Revision,
            new AlgorithmId("test:profile/layout"),
            LayoutComputation.Empty);
        var routing = new RoutingResult(
            snapshot.DocumentId,
            snapshot.Revision,
            layout.AlgorithmId,
            new AlgorithmId("test:profile/routing"),
            RoutingComputation.Empty);
        return ControlledEditingSessionPipeline.Success(
            new EditingSessionPipelineArtifacts(scopeId, graph, layout, routing),
            snapshot.VisualModel,
            editorState);
    }

    private static async Task InstallScopesAsync(
        Document document,
        EditingSession session,
        ControlledEditingSessionPipeline pipeline,
        IEnumerable<DocumentScopeSnapshot> scopes)
    {
        var beforeState = document.CaptureState();
        var before = beforeState.Snapshot;
        var revision = before.Revision.Increment();
        var committed = new DocumentSnapshot(
            new SemanticModelSnapshot(
                before.DocumentId,
                revision,
                before.SemanticModel.Elements,
                before.SemanticModel.Relationships,
                scopes,
                before.SemanticModel.ScopeMemberships,
                before.SemanticModel.ModelProfiles),
            new VisualModelSnapshot(
                before.DocumentId,
                revision,
                before.VisualModel.VisualStates),
            new DocumentMetadataSnapshot(
                before.DocumentId,
                revision,
                before.Metadata.SystemManagedProperties,
                before.Metadata.ExtensionProperties));
        Assert.True(document.TryInstallState(beforeState, new DocumentState(committed)));
        EnqueueSceneRebuild(pipeline);
        session.ObserveDocumentChanged(new DocumentChangedEvent(
            before.DocumentId,
            before.Revision,
            committed.Revision,
            AuthoritativeDocumentComponent.SemanticModel,
            new CommandTypeId("test:profile/replace-scopes"),
            committed,
            PipelineInvalidation.Scene));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
