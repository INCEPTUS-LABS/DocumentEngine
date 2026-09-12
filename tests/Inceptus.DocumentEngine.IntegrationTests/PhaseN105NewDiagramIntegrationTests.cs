using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN105NewDiagramIntegrationTests
{
    [Fact]
    public async Task ComplexEditorResetThenFirstEditAndUndoStayInsideTheFreshDocument()
    {
        var initialExecution = new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution();
        var replacementExecution =
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution();
        var renderer = CreateRenderer(initialExecution);
        await using var host = new DocumentCanvasHost(
            BpmnModelerTestComposition.DemoFactory,
            renderer,
            new FixedSurfaceObserverFactory(
                new Canvas2DSurfaceSize(1400d, 900d, 1d)),
            replacementRendererFactory: () => CreateRenderer(replacementExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var oldSession = Session(host);
        var oldDocument = Document(oldSession);

        await CommitAsync(oldSession, new SetModelProfileAvailabilityCommand(
            oldDocument.DocumentId,
            oldDocument.Revision,
            [new ModelProfileAvailabilityChange(BpmnModelProfiles.OrganizationalId, true)]));
        await CommitAsync(oldSession, new CreateOrganizationalPoolCommand(
            oldDocument.DocumentId,
            oldDocument.Revision,
            new SemanticElementId("test:n10.5:integration:pool-a"),
            oldDocument.SemanticModel.RootScopeId,
            OrganizationalPoolCreationMode.AdoptEligibleUnassigned,
            "Operations"));
        await CommitAsync(oldSession, new CreateOrganizationalPoolCommand(
            oldDocument.DocumentId,
            oldDocument.Revision,
            new SemanticElementId("test:n10.5:integration:pool-b"),
            oldDocument.SemanticModel.RootScopeId,
            OrganizationalPoolCreationMode.Empty,
            "Fulfilment"));
        var rootState = oldSession.CaptureState();
        var edge = rootState.ProjectedGraph!.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == BpmnDemoPipeline.FirstSequenceFlowId);
        var route = rootState.RoutingResult!.Routes.Single(candidate =>
            candidate.ProjectedEdgeId == edge.Id).Path;
        await CommitAsync(oldSession, new UpdateConnectionRouteCommand(
            oldDocument.DocumentId,
            oldDocument.Revision,
            BpmnDemoPipeline.FirstSequenceFlowVisualId,
            [route[0], new PointD(route[0].X + 80d, route[0].Y + 55d), route[^1]]));
        Assert.True((await oldSession.UpdateModelProfileElementViewStateAsync(
            oldSession.CaptureState().ModelProfileElementViewState.WithCollapsed(
                BpmnModelProfiles.OrganizationalId,
                new SemanticElementId("test:n10.5:integration:pool-a"),
                isCollapsed: true))).Succeeded);
        await oldSession.WaitForIdleAsync();
        Assert.True((await oldSession.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await oldSession.WaitForIdleAsync();
        var oldScoped = oldSession.CaptureState();
        var oldSelection = oldScoped.CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        Assert.True((await oldSession.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [oldSelection],
            activeToolId: "test:n10.5:integration:tool",
            viewport: new ViewportSnapshot(1.6d, new VectorD(90d, -35d))))).Succeeded);
        await oldSession.WaitForIdleAsync();
        var oldState = oldSession.CaptureState();
        var oldIdentity = oldDocument.DocumentId;
        Assert.NotEmpty(oldDocument.SemanticModel.Elements);
        Assert.NotEmpty(oldDocument.SemanticModel.Relationships);
        Assert.NotEmpty(oldDocument.SemanticModel.NestedScopes);
        Assert.NotEmpty(oldDocument.SemanticModel.ProfileAssignments);
        Assert.NotEmpty(oldDocument.VisualModel.ProfileElementPresentations);
        Assert.Contains(oldDocument.VisualModel.VisualStates, static visual =>
            !visual.Route.IsEmpty);
        Assert.NotEqual(oldDocument.SemanticModel.RootScopeId, oldState.ActiveScopeId);
        Assert.True(oldState.HistoryStatus.EntryCount >= 5);

        var reset = await host.NewDiagramAsync();

        Assert.True(reset.Succeeded, Format(reset.Diagnostics));
        var newSession = Session(host);
        var newDocument = Document(newSession);
        var fresh = newSession.CaptureState();
        Assert.NotEqual(oldIdentity, newDocument.DocumentId);
        Assert.Equal(DocumentRevision.Zero, fresh.DocumentRevision);
        Assert.Empty(newDocument.SemanticModel.Elements);
        Assert.Empty(newDocument.SemanticModel.Relationships);
        Assert.Empty(newDocument.SemanticModel.NestedScopes);
        Assert.Empty(newDocument.SemanticModel.ScopeMemberships);
        Assert.Empty(newDocument.SemanticModel.ModelProfiles.AvailableProfileIds);
        Assert.Empty(newDocument.SemanticModel.ProfileAssignments);
        Assert.Empty(newDocument.VisualModel.VisualStates);
        Assert.Empty(newDocument.VisualModel.ProfileElementPresentations);
        Assert.Equal(newDocument.SemanticModel.RootScopeId, fresh.ActiveScopeId);
        Assert.Equal(0, fresh.HistoryStatus.EntryCount);
        Assert.Empty(fresh.EditorState.Selection);
        Assert.Null(fresh.EditorState.ActiveToolId);
        Assert.Equal(1d, fresh.EditorState.Viewport.Zoom);
        Assert.Equal(default, fresh.EditorState.Viewport.Pan);
        Assert.Equal(EditingSessionStatus.Ready, fresh.Status);
        Assert.True(oldSession.CaptureState().IsClosed);
        Assert.Equal("standby-canvas", host.CaptureState().ActiveCanvasElementId);

        await CommitAsync(newSession, new CreateBpmnTaskCommand(
            newDocument.DocumentId,
            newDocument.Revision,
            new SemanticElementId("test:n10.5:integration:first-task"),
            new VisualStateId("test:n10.5:integration:first-task:visual"),
            new PointD(240d, 180d),
            new SizeD(160d, 100d),
            "TASK-1",
            "First task",
            1,
            VisualPlacementMode.Pinned,
            targetScopeId: newDocument.SemanticModel.RootScopeId));
        Assert.Single(newDocument.SemanticModel.Elements);
        Assert.Equal(1, newSession.CaptureState().HistoryStatus.EntryCount);

        var undo = await newSession.UndoAsync();
        Assert.True(undo.Succeeded, Format(undo.Diagnostics));
        await newSession.WaitForIdleAsync();
        Assert.Equal(newDocument.DocumentId, newSession.CaptureState().DocumentId);
        Assert.Empty(newDocument.SemanticModel.Elements);
        Assert.NotEqual(oldIdentity, newSession.CaptureState().DocumentId);
    }

    private static Canvas2DRenderer CreateRenderer(
        PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution execution) =>
        new(
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

    private static async ValueTask CommitAsync(EditingSession session, ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Format(result.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static EditingSession Session(DocumentCanvasHost host) =>
        Assert.IsType<EditingSession>(typeof(DocumentCanvasHost)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host));

    private static Document Document(EditingSession session) =>
        Assert.IsType<Document>(typeof(EditingSession)
            .GetField("_document", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session));

    private static string Format(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class FixedSurfaceObserverFactory(Canvas2DSurfaceSize initial) :
        ICanvasPresentationSurfaceObserverFactory
    {
        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICanvasPresentationSurfaceObserver>(
                new FixedSurfaceObserver(initial));
        }
    }

    private sealed class FixedSurfaceObserver(Canvas2DSurfaceSize initial) :
        ICanvasPresentationSurfaceObserver
    {
        public ValueTask<Canvas2DSurfaceSize> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(initial);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
