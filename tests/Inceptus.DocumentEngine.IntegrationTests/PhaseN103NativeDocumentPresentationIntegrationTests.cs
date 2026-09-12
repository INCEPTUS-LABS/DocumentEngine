using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN103NativeDocumentPresentationIntegrationTests
{
    private static readonly SemanticElementId PoolAId =
        new("inceptus:n10.3:organization:pool:a");
    private static readonly SemanticElementId PoolBId =
        new("inceptus:n10.3:organization:pool:b");

    [Fact]
    public async Task OrganizationalAndScopeDocumentOpensThroughFreshHostSessionsRepeatedly()
    {
        var (source, unassignedElementId) = await CreateSourceDocumentAsync();
        var expected = source.CaptureSnapshot();
        var payload = NativeDocumentSerializer.Export(source);
        var replacementExecutions = new Queue<
            PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution>(
        [
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
        ]);
        await using var host = new DocumentCanvasHost(
            BpmnModelerTestComposition.DemoFactory,
            CreateRenderer(
                new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution()),
            new FixedSurfaceObserverFactory(new Canvas2DSurfaceSize(1600d, 1000d, 1d)),
            replacementRendererFactory: () => CreateRenderer(
                replacementExecutions.Dequeue()));
        await host.InitializeAsync(
            "phase-n103-canvas-a",
            "phase-n103-canvas-b",
            "phase-n103-container");

        var firstImport = await host.ImportNativeDocumentAsync(payload.AsMemory());

        Assert.True(firstImport.Succeeded, Diagnostics(firstImport.Diagnostics));
        var firstSession = Session(host);
        Assert.True(firstSession.TryCaptureDocumentSnapshot(out var firstDocument));
        Assert.Equal(expected, firstDocument);
        var firstState = firstSession.CaptureState();
        Assert.Equal(expected.Revision, firstState.DocumentRevision);
        Assert.Equal(expected.SemanticModel.RootScopeId, firstState.ActiveScopeId);
        Assert.Equal(0, firstState.HistoryStatus.EntryCount);
        Assert.Equal(EditingSessionStatus.Ready, firstState.Status);
        Assert.NotNull(firstState.CurrentScene);
        Assert.NotEmpty(firstDocument.SemanticModel.ProfileAssignments);
        Assert.Equal(2, firstDocument.VisualModel.ProfileElementPresentations.Length);
        Assert.Equal(
            2,
            OrganizationalSemantics.GetOrderedPoolsInScope(
                firstDocument,
                firstDocument.SemanticModel.RootScopeId).Length);
        Assert.DoesNotContain(firstDocument.SemanticModel.ProfileAssignments, assignment =>
            assignment.SemanticElementId == unassignedElementId);
        Assert.Contains(firstDocument.SemanticModel.Elements, element =>
            element.Id == unassignedElementId);
        Assert.NotEmpty(firstDocument.SemanticModel.NestedScopes);

        var nestedScope = firstDocument.SemanticModel.NestedScopes.Single(scope =>
            scope.ParentScopeId == firstDocument.SemanticModel.RootScopeId);
        Assert.True((await firstSession.NavigateToScopeAsync(nestedScope.Id)).Succeeded);
        await firstSession.WaitForIdleAsync();
        var nestedVisual = firstSession.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        Assert.True((await firstSession.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [nestedVisual],
            activeToolId: "test:n10.3:transient-tool",
            viewport: new ViewportSnapshot(1.3d, new VectorD(29d, -17d)))))
            .Succeeded);
        Assert.True((await firstSession.UpdateModelProfileViewStateAsync(
            firstSession.CaptureState().ModelProfileViewState.WithPreferredVisibility(
                OrganizationalModelProfile.Id,
                isVisible: false))).Succeeded);
        var pool = OrganizationalSemantics.GetOrderedPoolsInScope(
            firstDocument,
            firstDocument.SemanticModel.RootScopeId)[0];
        Assert.True((await firstSession.UpdateModelProfileElementViewStateAsync(
            firstSession.CaptureState().ModelProfileElementViewState.WithCollapsed(
                OrganizationalModelProfile.Id,
                pool.Id,
                isCollapsed: true))).Succeeded);
        await firstSession.WaitForIdleAsync();
        Assert.NotNull(await host.ValidateAsync());
        var transient = firstSession.CaptureState();
        Assert.Equal(nestedScope.Id, transient.ActiveScopeId);
        Assert.False(transient.ModelProfileViewState.IsPreferredVisible(
            OrganizationalModelProfile.Id));
        Assert.True(transient.ModelProfileElementViewState.IsCollapsed(
            OrganizationalModelProfile.Id,
            pool.Id));
        Assert.NotNull(host.CaptureState().ValidationSnapshot);

        var secondImport = await host.ImportNativeDocumentAsync(payload.AsMemory());

        Assert.True(secondImport.Succeeded, Diagnostics(secondImport.Diagnostics));
        var secondSession = Session(host);
        Assert.NotSame(firstSession, secondSession);
        Assert.True(firstSession.CaptureState().IsClosed);
        Assert.True(secondSession.TryCaptureDocumentSnapshot(out var secondDocument));
        Assert.Equal(expected, secondDocument);
        var fresh = secondSession.CaptureState();
        Assert.Equal(expected.Revision, fresh.DocumentRevision);
        Assert.Equal(expected.SemanticModel.RootScopeId, fresh.ActiveScopeId);
        Assert.Equal(0, fresh.HistoryStatus.EntryCount);
        Assert.False(fresh.HistoryStatus.CanUndo);
        Assert.False(fresh.HistoryStatus.CanRedo);
        Assert.Empty(fresh.EditorState.Selection);
        Assert.Null(fresh.EditorState.SemanticSceneSelection);
        Assert.Null(fresh.EditorState.ActiveGesture);
        Assert.Equal(ViewportSnapshot.Default.Zoom, fresh.EditorState.Viewport.Zoom);
        Assert.Equal(ViewportSnapshot.Default.Pan, fresh.EditorState.Viewport.Pan);
        Assert.Equal(ModelProfileViewStateSnapshot.Empty, fresh.ModelProfileViewState);
        Assert.Equal(
            ModelProfileElementViewStateSnapshot.Empty,
            fresh.ModelProfileElementViewState);
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Equal(EditingSessionStatus.Ready, fresh.Status);
        Assert.NotNull(fresh.CurrentScene);
        var reexport = await host.ExportNativeDocumentAsync();
        Assert.True(reexport.Succeeded, Diagnostics(reexport.Diagnostics));
        Assert.True(payload.AsSpan().SequenceEqual(reexport.Payload.AsSpan()));
        Assert.Empty(replacementExecutions);
    }

    private static async ValueTask<(Document Document, SemanticElementId UnassignedElementId)>
        CreateSourceDocumentAsync()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var document = composition.Document;
        var renderer = CreateRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution());
        var initialization = await renderer.InitializeAsync(
            "phase-n103-source",
            new Canvas2DSurfaceSize(1600d, 1000d, 1d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            composition.Configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var rootScopeId = session.CaptureState().ActiveScopeId;

        await ExecuteCommittedAsync(
            session,
            new SetModelProfileAvailabilityCommand(
                document.DocumentId,
                document.Revision,
                [new ModelProfileAvailabilityChange(
                    OrganizationalModelProfile.Id,
                    isAvailable: true)]));
        await ExecuteCommittedAsync(
            session,
            new CreateOrganizationalPoolCommand(
                document.DocumentId,
                document.Revision,
                PoolAId,
                rootScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned,
                "Operations"));
        await ExecuteCommittedAsync(
            session,
            new CreateOrganizationalPoolCommand(
                document.DocumentId,
                document.Revision,
                PoolBId,
                rootScopeId,
                OrganizationalPoolCreationMode.Empty,
                "Fulfilment"));
        var assigned = document.SemanticModel.ProfileAssignments
            .Where(static assignment =>
                assignment.ProfileId == OrganizationalModelProfile.Id)
            .Select(static assignment => assignment.SemanticElementId)
            .Take(2)
            .ToArray();
        Assert.Equal(2, assigned.Length);
        await ExecuteCommittedAsync(
            session,
            new AssignOrganizationalElementCommand(
                document.DocumentId,
                document.Revision,
                assigned[0],
                PoolBId));
        await ExecuteCommittedAsync(
            session,
            new UnassignOrganizationalElementCommand(
                document.DocumentId,
                document.Revision,
                assigned[1]));

        return (document, assigned[1]);
    }

    private static async ValueTask ExecuteCommittedAsync(
        EditingSession session,
        ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
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

    private static EditingSession Session(DocumentCanvasHost host) =>
        Assert.IsType<EditingSession>(typeof(DocumentCanvasHost)
            .GetField(
                "_session",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!
            .GetValue(host));

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class FixedSurfaceObserverFactory(Canvas2DSurfaceSize size) :
        ICanvasPresentationSurfaceObserverFactory
    {
        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICanvasPresentationSurfaceObserver>(
                new FixedSurfaceObserver(size));
        }
    }

    private sealed class FixedSurfaceObserver(Canvas2DSurfaceSize size) :
        ICanvasPresentationSurfaceObserver
    {
        public ValueTask<Canvas2DSurfaceSize> StartAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(size);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
