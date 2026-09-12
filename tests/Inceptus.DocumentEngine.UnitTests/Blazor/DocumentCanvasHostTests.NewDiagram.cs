using System.Reflection;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task NewDiagramReplacesAComplexDocumentWithCanonicalFreshPersistentAndRuntimeState()
    {
        var initialExecution = new RecordingRenderExecution();
        var replacementExecution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        var toolboxSelection = new ToolboxSelectionState();
        var composition = await CreateNewDiagramComplexCompositionAsync();
        await using var host = CreateHost(
            initialExecution,
            observer,
            compositionFactory: new NewDiagramFixedCompositionFactory(composition),
            toolboxSelection: toolboxSelection,
            replacementRendererFactory: () => CreateRenderer(replacementExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var oldSession = Session(host);
        var oldDocument = AttachedDocument(oldSession);

        await EnableOrganizationalProfileAsync(oldSession);
        var firstPool = await AddOrganizationalPoolFromMenuAsync(host);
        _ = await AddOrganizationalPoolFromMenuAsync(host);
        var routed = oldSession.CaptureState();
        var firstEdge = routed.ProjectedGraph!.Edges.Single(edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.FirstSequenceFlowId);
        var firstRoute = routed.RoutingResult!.Routes.Single(route =>
            route.ProjectedEdgeId == firstEdge.Id).Path;
        var bend = new PointD(
            (firstRoute[0].X + firstRoute[^1].X) / 2d,
            Math.Max(firstRoute[0].Y, firstRoute[^1].Y) + 41d);
        Assert.True((await oldSession.ExecuteAsync(new UpdateConnectionRouteCommand(
            oldDocument.DocumentId,
            oldDocument.Revision,
            BpmnDemoPipeline.FirstSequenceFlowVisualId,
            [firstRoute[0], bend, firstRoute[^1]]))).IsCommitted);
        await oldSession.WaitForIdleAsync();
        Assert.True((await oldSession.UpdateModelProfileElementViewStateAsync(
            oldSession.CaptureState().ModelProfileElementViewState.WithCollapsed(
                BpmnModelProfiles.OrganizationalId,
                firstPool,
                isCollapsed: true))).Succeeded);
        await oldSession.WaitForIdleAsync();
        Assert.True((await oldSession.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await oldSession.WaitForIdleAsync();
        Assert.True((await oldSession.UpdateModelProfileViewStateAsync(
            oldSession.CaptureState().ModelProfileViewState.WithPreferredVisibility(
                BpmnModelProfiles.OrganizationalId,
                isVisible: false))).Succeeded);
        await oldSession.WaitForIdleAsync();
        var selectedVisualId = oldSession.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        var oldViewport = new ViewportSnapshot(1.55d, new VectorD(83d, -47d));
        Assert.True((await oldSession.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "test:n10.5:old-tool",
            viewport: oldViewport))).Succeeded);
        await oldSession.WaitForIdleAsync();
        Assert.NotNull(await host.ValidateAsync());
        var oldState = oldSession.CaptureState();
        var oldSnapshot = oldDocument.CaptureSnapshot();

        Assert.NotEqual(DocumentRevision.Zero, oldState.DocumentRevision);
        Assert.True(oldState.HistoryStatus.EntryCount > 0);
        Assert.NotEmpty(oldSnapshot.SemanticModel.Elements);
        Assert.NotEmpty(oldSnapshot.SemanticModel.Relationships);
        Assert.NotEmpty(oldSnapshot.SemanticModel.NestedScopes);
        Assert.NotEmpty(oldSnapshot.SemanticModel.ScopeMemberships);
        Assert.NotEmpty(oldSnapshot.SemanticModel.ProfileAssignments);
        Assert.NotEmpty(oldSnapshot.SemanticModel.ModelProfiles.AvailableProfileIds);
        Assert.NotEmpty(oldSnapshot.VisualModel.VisualStates);
        Assert.NotEmpty(oldSnapshot.VisualModel.ProfileElementPresentations);
        Assert.Contains(oldSnapshot.VisualModel.VisualStates, static visual =>
            !visual.ConnectorAnchors.IsEmpty);
        Assert.Contains(oldSnapshot.VisualModel.VisualStates, static visual =>
            !visual.Route.IsEmpty);
        Assert.Contains(oldSnapshot.VisualModel.VisualStates, static visual =>
            visual.BoundaryAttachment is not null);
        Assert.NotEmpty(oldSnapshot.Metadata.SystemManagedProperties);
        Assert.NotEmpty(oldSnapshot.Metadata.ExtensionProperties);
        Assert.NotEqual(oldSnapshot.SemanticModel.RootScopeId, oldState.ActiveScopeId);
        Assert.Equal(oldViewport, oldState.EditorState.Viewport);
        Assert.False(oldState.ModelProfileViewState.IsPreferredVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.True(oldState.ModelProfileElementViewState.IsCollapsed(
            BpmnModelProfiles.OrganizationalId,
            firstPool));
        Assert.NotNull(host.CaptureState().ValidationSnapshot);

        var result = await host.NewDiagramAsync();

        Assert.True(result.Succeeded, NewDiagramDiagnostics(result.Diagnostics));
        Assert.Equal(NewDiagramHostOperationStatus.Succeeded, result.Status);
        var newSession = Session(host);
        var newState = newSession.CaptureState();
        var newDocument = AttachedDocument(newSession);
        var fresh = newDocument.CaptureSnapshot();
        Assert.NotSame(oldSession, newSession);
        Assert.True(oldSession.CaptureState().IsClosed);
        Assert.NotEqual(oldDocument.DocumentId, newDocument.DocumentId);
        Assert.Equal(DocumentRevision.Zero, newDocument.Revision);
        Assert.Empty(fresh.SemanticModel.Elements);
        Assert.Empty(fresh.SemanticModel.Relationships);
        Assert.Empty(fresh.SemanticModel.NestedScopes);
        Assert.Empty(fresh.SemanticModel.ScopeMemberships);
        Assert.Empty(fresh.SemanticModel.ModelProfiles.AvailableProfileIds);
        Assert.Empty(fresh.SemanticModel.ProfileAssignments);
        Assert.Empty(fresh.VisualModel.VisualStates);
        Assert.Empty(fresh.VisualModel.ProfileElementPresentations);
        Assert.Empty(fresh.Metadata.SystemManagedProperties);
        Assert.Empty(fresh.Metadata.ExtensionProperties);
        Assert.Equal(newDocument.SemanticModel.RootScopeId, newState.ActiveScopeId);
        Assert.Equal(0, newState.HistoryStatus.EntryCount);
        Assert.False(newState.HistoryStatus.CanUndo);
        Assert.False(newState.HistoryStatus.CanRedo);
        Assert.Empty(newState.EditorState.Selection);
        Assert.Null(newState.EditorState.SemanticSceneSelection);
        Assert.Null(newState.EditorState.HoveredObjectId);
        Assert.Null(newState.EditorState.ActiveToolId);
        Assert.Null(newState.EditorState.ActiveGesture);
        Assert.Empty(newState.EditorState.TemporaryFeedback);
        Assert.Empty(newState.EditorState.ToolState);
        Assert.Equal(composition.Configuration.InitialEditorState.Viewport.Zoom,
            newState.EditorState.Viewport.Zoom);
        Assert.Equal(composition.Configuration.InitialEditorState.Viewport.Pan,
            newState.EditorState.Viewport.Pan);
        Assert.Equal(composition.Configuration.InitialModelProfileViewState,
            newState.ModelProfileViewState);
        Assert.Equal(ModelProfileElementViewStateSnapshot.Empty,
            newState.ModelProfileElementViewState);
        Assert.DoesNotContain(newState.CurrentScene!.Items, static item =>
            item.Origin.SemanticElementId is not null ||
            item.Origin.VisualStateId is not null);
        Assert.Empty(newState.ProjectedGraph!.Nodes);
        Assert.Empty(newState.ProjectedGraph.Edges);
        Assert.Empty(newState.RuntimeDiagnostics);
        Assert.Empty(newState.PresentationDiagnostics);
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal("standby-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Equal(1, initialExecution.DisposeCount);
        Assert.Equal(0, replacementExecution.DisposeCount);

        var unavailableUndo = await newSession.UndoAsync();
        Assert.False(unavailableUndo.Succeeded);
        Assert.Equal(newDocument.DocumentId, newSession.CaptureState().DocumentId);
        Assert.Empty(newDocument.SemanticModel.Elements);
    }

    [Fact]
    public async Task FirstToolboxEditAfterNewDiagramIsUndoableOnlyWithinTheNewDocument()
    {
        var initialExecution = new RecordingRenderExecution();
        var replacementExecution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1200d, 800d, 1d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(
            initialExecution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: selection,
            replacementRendererFactory: () => CreateRenderer(replacementExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var oldDocumentId = Session(host).CaptureState().DocumentId;
        Assert.True((await host.NewDiagramAsync()).Succeeded);
        var freshSession = Session(host);
        var freshDocument = AttachedDocument(freshSession);
        var freshDocumentId = freshDocument.DocumentId;
        Assert.NotEqual(oldDocumentId, freshDocumentId);
        Assert.Equal(0, freshSession.CaptureState().HistoryStatus.EntryCount);

        var taskItem = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions)
            .Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        Assert.True(selection.Select(taskItem.ItemId));
        await host.RefreshToolboxPlacementAsync();
        await ActiveReplacementPointerObserver(host).ClickCssPointAsync(new PointD(420d, 260d));
        await freshSession.WaitForIdleAsync();

        var afterCreate = freshSession.CaptureState();
        var task = Assert.Single(freshDocument.SemanticModel.Elements);
        Assert.Equal(BpmnSemanticTypes.Task, task.TypeId);
        Assert.Equal(freshDocument.SemanticModel.RootScopeId,
            freshDocument.SemanticModel.GetScope(task.Id).Id);
        Assert.Equal(new DocumentRevision(1), afterCreate.DocumentRevision);
        Assert.Equal(1, afterCreate.HistoryStatus.EntryCount);
        Assert.True(afterCreate.HistoryStatus.CanUndo);

        var undo = await freshSession.UndoAsync();
        Assert.True(undo.Succeeded, NewDiagramDiagnostics(undo.Diagnostics));
        await freshSession.WaitForIdleAsync();

        var afterUndo = freshSession.CaptureState();
        Assert.Equal(freshDocumentId, afterUndo.DocumentId);
        Assert.Empty(freshDocument.SemanticModel.Elements);
        Assert.Empty(freshDocument.VisualModel.VisualStates);
        Assert.False(afterUndo.HistoryStatus.CanUndo);
        Assert.True(afterUndo.HistoryStatus.CanRedo);
        Assert.NotEqual(oldDocumentId, afterUndo.DocumentId);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("initialization")]
    [InlineData("attachment")]
    public async Task NewDiagramCandidateFailurePreservesExactOldSessionCanvasAndRenderer(
        string failureStage)
    {
        var initialExecution = new RecordingRenderExecution();
        var candidateExecution = new RecordingRenderExecution();
        if (failureStage == "initialization")
        {
            candidateExecution.InitializeResult = new Canvas2DInteropOperationResult
            {
                Succeeded = false,
                Code = "TEST_NEW_DIAGRAM_INITIALIZATION_FAILED",
            };
        }
        else if (failureStage == "attachment")
        {
            candidateExecution.RenderResult = new Canvas2DInteropOperationResult
            {
                Succeeded = false,
                Code = "TEST_NEW_DIAGRAM_ATTACHMENT_FAILED",
            };
        }

        Func<Canvas2DRenderer> candidateFactory = failureStage == "factory"
            ? () => throw new InvalidOperationException("New diagram renderer creation failed.")
            : () => CreateRenderer(candidateExecution);
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            initialExecution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            replacementRendererFactory: candidateFactory);
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var oldSession = Session(host);
        var selectedVisualId = oldSession.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        Assert.True((await oldSession.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "test:n10.5:preserved",
            viewport: new ViewportSnapshot(1.4d, new VectorD(37d, -19d))))).Succeeded);
        await oldSession.WaitForIdleAsync();
        var before = oldSession.CaptureState();
        Assert.True(oldSession.TryCaptureDocumentSnapshot(out var oldDocument));

        var result = await host.NewDiagramAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(NewDiagramHostOperationStatus.Failed, result.Status);
        Assert.Equal("CANVAS_NEW_DIAGRAM_REPLACEMENT_FAILED",
            Assert.Single(result.Diagnostics).Code);
        Assert.Same(oldSession, Session(host));
        var after = oldSession.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.Equal(before.DocumentId, after.DocumentId);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.True(oldSession.TryCaptureDocumentSnapshot(out var unchanged));
        Assert.Same(oldDocument, unchanged);
        Assert.Equal("active-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Equal(0, initialExecution.DisposeCount);
        Assert.Equal(0, PointerObserver(host).DisposeCount);
        if (failureStage != "factory")
        {
            Assert.Contains("initialize:standby-canvas", candidateExecution.Calls);
            Assert.Equal(1, candidateExecution.DisposeCount);
        }

        await observer.RaiseAsync(new Canvas2DSurfaceSize(960d, 640d, 1d));
        Assert.Contains("resize", initialExecution.Calls);
        Assert.Equal(EditingSessionStatus.Ready, oldSession.CaptureState().Status);
    }

    [Fact]
    public async Task RepeatedNewDiagramCreatesUniqueRevisionZeroDocumentsAndDisposesEachOldSession()
    {
        var initialExecution = new RecordingRenderExecution();
        var firstExecution = new RecordingRenderExecution();
        var secondExecution = new RecordingRenderExecution();
        var executions = new Queue<RecordingRenderExecution>([firstExecution, secondExecution]);
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            initialExecution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            replacementRendererFactory: () => CreateRenderer(executions.Dequeue()));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var initialSession = Session(host);

        Assert.True((await host.NewDiagramAsync()).Succeeded);
        var firstSession = Session(host);
        var first = firstSession.CaptureState();
        Assert.True((await host.NewDiagramAsync()).Succeeded);
        var secondSession = Session(host);
        var second = secondSession.CaptureState();

        Assert.NotEqual(initialSession.CaptureState().DocumentId, first.DocumentId);
        Assert.NotEqual(first.DocumentId, second.DocumentId);
        Assert.Equal(DocumentRevision.Zero, first.DocumentRevision);
        Assert.Equal(DocumentRevision.Zero, second.DocumentRevision);
        Assert.Equal(0, first.HistoryStatus.EntryCount);
        Assert.Equal(0, second.HistoryStatus.EntryCount);
        Assert.True(initialSession.CaptureState().IsClosed);
        Assert.True(firstSession.CaptureState().IsClosed);
        Assert.False(secondSession.CaptureState().IsClosed);
        Assert.Equal(1, initialExecution.DisposeCount);
        Assert.Equal(1, firstExecution.DisposeCount);
        Assert.Equal(0, secondExecution.DisposeCount);
        Assert.Empty(executions);
    }

    [Fact]
    public async Task EmptyNewDiagramExportsImportsAndRejectsPublishWithoutMutatingIt()
    {
        var initialExecution = new RecordingRenderExecution();
        var emptyExecution = new RecordingRenderExecution();
        var importedExecution = new RecordingRenderExecution();
        var replacements = new Queue<RecordingRenderExecution>(
            [emptyExecution, importedExecution]);
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        await using var host = CreateHost(
            initialExecution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            replacementRendererFactory: () => CreateRenderer(replacements.Dequeue()));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var nonemptyPayload = NativeDocumentSerializer.Export(AttachedDocument(Session(host)));

        Assert.True((await host.NewDiagramAsync()).Succeeded);
        var emptySession = Session(host);
        var emptyDocument = AttachedDocument(emptySession);
        var emptyIdentity = emptyDocument.DocumentId;
        var export = await host.ExportNativeDocumentAsync();
        Assert.True(export.Succeeded, NewDiagramDiagnostics(export.Diagnostics));
        var roundTrip = NativeDocumentSerializer.Import(
            export.Payload.ToArray(),
            emptySession.ConnectorAnchorPolicyProvider);
        Assert.True(roundTrip.Succeeded, NewDiagramDiagnostics(roundTrip.Diagnostics));
        Assert.Equal(emptyDocument.CaptureSnapshot(), roundTrip.Document!.CaptureSnapshot());

        var beforePublish = emptyDocument.CaptureSnapshot();
        var publish = await host.PublishProcessAsync();
        Assert.False(publish.Succeeded);
        Assert.Contains(publish.Diagnostics, diagnostic =>
            diagnostic.Code == "PUBLISH_PUBLICATION_REQUIRED");
        Assert.Null(emptyDocument.Publication);
        Assert.Same(emptySession, Session(host));
        Assert.Same(beforePublish, emptyDocument.CaptureSnapshot());

        var import = await host.ImportNativeDocumentAsync(nonemptyPayload.AsMemory());
        Assert.True(import.Succeeded, NewDiagramDiagnostics(import.Diagnostics));
        var imported = Session(host).CaptureState();
        Assert.NotEqual(emptyIdentity, imported.DocumentId);
        Assert.NotEmpty(AttachedDocument(Session(host)).SemanticModel.Elements);
        Assert.Equal(0, imported.HistoryStatus.EntryCount);
        Assert.Equal(EditingSessionStatus.Ready, imported.Status);
        Assert.Empty(replacements);
    }

    private static async ValueTask<DocumentCanvasComposition>
        CreateNewDiagramComplexCompositionAsync()
    {
        var source = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var snapshot = source.Document.CaptureSnapshot();
        var metadata = new DocumentMetadataSnapshot(
            snapshot.DocumentId,
            snapshot.Revision,
            [new("test:n10.5:system", PropertyValue.FromText("old system metadata"))],
            [new("test:n10.5:extension", PropertyValue.FromBoolean(true))]);
        var reconstruction = DocumentReconstructor.Reconstruct(
            new DocumentSnapshot(snapshot.SemanticModel, snapshot.VisualModel, metadata),
            source.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(reconstruction.Succeeded, NewDiagramDiagnostics(reconstruction.Diagnostics));
        return CopyCompositionWithDocument(source, reconstruction.Document!);
    }

    private static DocumentCanvasComposition CopyCompositionWithDocument(
        DocumentCanvasComposition source,
        Document document) =>
        new(
            document,
            source.Configuration,
            source.PropertiesSchemaCatalog,
            source.Counters,
            source.ToolboxPlacementCatalog,
            source.AnchorConnectionCreationCatalog,
            source.DocumentCreationIdentityProvider,
            source.EndpointReconnectionCatalog,
            source.DeletionCatalog,
            source.ModelValidationCatalog,
            source.ScopeNavigationCatalog,
            source.BackgroundActionCatalog,
            source.SemanticSceneViewActionCatalog,
            source.SemanticSceneCommandActionCatalog,
            source.SpatialEditPlanners);

    private static RecordingPointerObserver ActiveReplacementPointerObserver(
        DocumentCanvasHost host) =>
        Assert.IsType<RecordingPointerObserver>(typeof(DocumentCanvasHost)
            .GetField("_pointerObserver", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host));

    private static string NewDiagramDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class NewDiagramFixedCompositionFactory(DocumentCanvasComposition composition) :
        IDocumentCanvasCompositionFactory
    {
        public ValueTask<DocumentCanvasComposition> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(composition);
        }
    }
}
