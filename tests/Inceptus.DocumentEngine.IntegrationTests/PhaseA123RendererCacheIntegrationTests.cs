using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA123RendererCacheIntegrationTests
{
    [Theory]
    [InlineData("move")]
    [InlineData("route")]
    [InlineData("label-move")]
    [InlineData("label-reset")]
    [InlineData("tool")]
    [InlineData("preview")]
    public async Task PersistentEditsAndTransientPreviewsReplaceContentBeforePanResumes(string change)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var flow = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, flow, "Automatic branch label"));
        var edgeVisual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == flow);
        if (change == "route")
            await BpmnModelerTestComposition.SetRoutingTypeAsync(test.Session, edgeVisual.Id, ConnectorRoutingType.Manual);
        if (change == "label-reset")
        {
            await test.ExecuteAsync(new MoveLabelCommand(test.Snapshot.DocumentId,
                test.Snapshot.Revision, edgeVisual.Id, ConnectorLabelPlacement.Default));
        }
        await test.AssertPanReusedAsync();
        var uploads = test.Execution.FullUploadCount;
        var before = test.State;
        var document = test.Snapshot;
        switch (change)
        {
            case "move":
                var source = document.SemanticModel.Relationships.Single(item => item.Id == flow).SourceId;
                var visual = document.VisualModel.VisualStates.Single(item => item.SemanticElementId == source);
                await test.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
                    visual.Id, visual.Position + new VectorD(45, 65), VisualPlacementMode.Pinned));
                break;
            case "route":
                var edge = before.ProjectedGraph!.Edges.Single(item => item.Source.SemanticElementId == flow);
                var route = before.RoutingResult!.Routes.Single(item => item.ProjectedEdgeId == edge.Id).Path;
                await test.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId, document.Revision,
                    edgeVisual.Id, [route[0], new PointD(route[0].X + 70, route[0].Y),
                        new PointD(route[0].X + 70, route[^1].Y), route[^1]]));
                break;
            case "label-move":
            case "label-reset":
                await test.ExecuteAsync(new MoveLabelCommand(document.DocumentId, document.Revision,
                    edgeVisual.Id, change == "label-move" ? ConnectorLabelPlacement.Default : null));
                break;
            case "tool":
                Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
                    viewport: before.EditorState.Viewport, activeToolId: "test:a123:tool"))).Succeeded);
                break;
            case "preview":
                for (var i = 0; i < 2; i++)
                {
                    Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
                        viewport: before.EditorState.Viewport,
                        temporaryFeedback: [new EditorFeedbackSnapshot("test:a123:preview", "placement",
                            new RectD(10 + i * 20, 30, 80, 50))]))).Succeeded);
                    Assert.True(test.Execution.FullUploadCount > uploads + i);
                }
                Assert.True((await test.Session.UpdateEditorStateAsync(before.EditorState)).Succeeded);
                break;
        }
        Assert.True(test.Execution.FullUploadCount > uploads);
        Assert.Equal(test.State.CurrentScene!.Items.Select(item => item.Id.Value),
            test.Execution.LastContent!.Items.Select(item => item.Id));
        await test.AssertPanReusedAsync();
    }

    [Theory]
    [InlineData("document")]
    [InlineData("scope")]
    [InlineData("profile")]
    [InlineData("resize")]
    [InlineData("dpr")]
    [InlineData("dispose")]
    public async Task DelayedCachedPresentationCannotOverwriteNewerSessionOrSurface(string transition)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        if (transition == "profile") await test.EnablePoolsAsync();
        await test.AssertPanReusedAsync();
        var before = test.State;
        var execution = test.Execution;
        var uploads = execution.FullUploadCount;
        execution.RenderStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = test.Session.RenderCurrentAsync().AsTask();
        await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(execution.LastViewport);
        var newerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var superseded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Session.StateChanged += (_, _) =>
        {
            if (test.State.Generation != before.Generation) superseded.TrySetResult();
        };
        var newer = Task.Run(async () =>
        {
            newerStarted.SetResult();
            switch (transition)
            {
                case "document":
                    await test.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(test.Snapshot.DocumentId,
                        test.Snapshot.Revision, BpmnDemoPipeline.ThirdSequenceFlowId, "New authoritative content"));
                    break;
                case "scope": Assert.True((await test.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded); break;
                case "profile": Assert.True((await test.Session.UpdateModelProfileViewStateAsync(new ModelProfileViewStateSnapshot([OrganizationalModelProfile.Id]))).Succeeded); break;
                case "resize": Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(800, 600, 1))).Succeeded); break;
                case "dpr": Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, 2))).Succeeded); break;
                case "dispose": await test.Session.DisposeAsync(); break;
            }
        });
        try
        {
            await newerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (transition is "scope" or "profile")
            {
                await superseded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(test.State.IsCurrentScenePresented);
            }
            else if (transition == "document")
            {
                // Saved-state preparation uses renderer text metrics and waits behind this
                // presentation. The Document stays atomic until the renderer is released.
                Assert.Equal(before.DocumentRevision, test.State.DocumentRevision);
            }
        }
        finally
        {
            execution.RenderRelease.TrySetResult();
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await newer.WaitAsync(TimeSpan.FromSeconds(20));
        if (transition == "dispose")
        {
            Assert.False((await test.Session.RenderCurrentAsync()).Succeeded);
            return;
        }
        await test.Session.WaitForIdleAsync();
        Assert.True(test.State.IsCurrentScenePresented);
        Assert.True(execution.FullUploadCount > uploads);
        Assert.Equal(test.State.CurrentScene!.Items.Select(item => item.Id.Value),
            execution.LastContent!.Items.Select(item => item.Id));
        await test.AssertPanReusedAsync();
        Assert.Equal(execution.LastContent.ContentVersion, execution.LastViewport!.ContentVersion);
        Assert.Equal(test.State.CurrentScene.ViewportTransform.OffsetX, execution.LastViewport.ViewportTransform.OffsetX);
    }

    [Fact]
    public async Task ViewportFailureKeepsReadyDocumentHistoryAndSceneThenFullRenderRecovers()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.AssertPanReusedAsync();
        var before = test.State;
        var snapshot = test.Snapshot;
        var uploads = test.Execution.FullUploadCount;
        test.Execution.RenderResult = new Canvas2DInteropOperationResult
        {
            Succeeded = false,
            Code = Canvas2DRendererDiagnosticCodes.RenderingFailed,
        };
        Assert.True((await test.Session.PanViewportAsync(new VectorD(-10, -10))).Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, test.State.Status);
        Assert.False(test.State.IsCurrentScenePresented);
        Assert.NotEmpty(test.State.PresentationDiagnostics);
        Assert.Same(snapshot, test.Snapshot);
        Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        test.Execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = true };
        Assert.True((await test.Session.RenderCurrentAsync()).Succeeded);
        Assert.Equal(uploads + 1, test.Execution.FullUploadCount);
        Assert.True(test.State.IsCurrentScenePresented);
        Assert.Empty(test.State.PresentationDiagnostics);
        await test.AssertPanReusedAsync();
    }
}
