using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN82MixedHistoryIntegrationTests
{
    [Fact]
    public async Task CreateOpenCreateReturnUndoRedoFollowsOneGlobalChronology()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = await CreateRendererAsync("phase-n82-mixed-history-basic");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var ownerId = new SemanticElementId("bpmn:n8.2:mixed-history:a");
        var ownerVisualId = new VisualStateId($"{ownerId.Value}:visual");
        var childScopeId = new DocumentScopeId($"{ownerId.Value}:scope");
        var taskId = new SemanticElementId("bpmn:n8.2:mixed-history:x");
        var taskVisualId = new VisualStateId($"{taskId.Value}:visual");

        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerId,
            ownerVisualId,
            rootScopeId,
            childScopeId,
            new PointD(1500d, 680d),
            new SizeD(120d, 80d),
            "MIXED_A",
            "Mixed A",
            VisualPlacementMode.Pinned));
        var owner = composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == ownerId);
        var ownerVisual = composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == ownerVisualId);
        var childScope = composition.Document.SemanticModel.NestedScopes.Single(scope =>
            scope.Id == childScopeId);

        var beforeOpenRevision = composition.Document.Revision;
        Assert.True((await session.NavigateToScopeAsync(childScopeId)).Succeeded);
        Assert.Equal(beforeOpenRevision, composition.Document.Revision);

        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskId,
            taskVisualId,
            new PointD(180d, 160d),
            new SizeD(120d, 80d),
            "MIXED_X",
            "Mixed X",
            801,
            VisualPlacementMode.Pinned,
            targetScopeId: childScopeId));
        var task = composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == taskId);
        var taskVisual = composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == taskVisualId);

        var beforeReturnRevision = composition.Document.Revision;
        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);
        Assert.Equal(beforeReturnRevision, composition.Document.Revision);
        Assert.Equal(new HistoryStatus(4, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);

        await AssertAppliedUndoAsync(session, childScopeId);
        Assert.Contains(composition.Document.SemanticModel.Elements, element =>
            element.Id == taskId);

        await AssertCommittedUndoAsync(session, childScopeId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == taskId);

        await AssertAppliedUndoAsync(session, rootScopeId);
        Assert.Contains(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == childScopeId);

        await AssertCommittedUndoAsync(session, rootScopeId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == ownerId);
        Assert.DoesNotContain(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == childScopeId);
        Assert.Equal(new HistoryStatus(4, canUndo: false, canRedo: true),
            session.CaptureState().HistoryStatus);

        await AssertCommittedRedoAsync(session, rootScopeId);
        Assert.Equal(owner, composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == ownerId));
        Assert.Equal(ownerVisual, composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == ownerVisualId));
        Assert.Equal(childScope, composition.Document.SemanticModel.NestedScopes.Single(scope =>
            scope.Id == childScopeId));

        await AssertAppliedRedoAsync(session, childScopeId);
        await AssertCommittedRedoAsync(session, childScopeId);
        Assert.Equal(task, composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == taskId));
        Assert.Equal(taskVisual, composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == taskVisualId));

        await AssertAppliedRedoAsync(session, rootScopeId);
        Assert.Equal(new HistoryStatus(4, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task NestedMixedHistoryReplaysDirectAncestorJumpAsOneAction()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = await CreateRendererAsync("phase-n82-mixed-history-nested");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var ownerAId = new SemanticElementId("bpmn:n8.2:mixed-history:nested:a");
        var ownerAVisualId = new VisualStateId($"{ownerAId.Value}:visual");
        var scopeAId = new DocumentScopeId($"{ownerAId.Value}:scope");
        var taskXId = new SemanticElementId("bpmn:n8.2:mixed-history:nested:x");
        var taskXVisualId = new VisualStateId($"{taskXId.Value}:visual");
        var ownerBId = new SemanticElementId("bpmn:n8.2:mixed-history:nested:b");
        var ownerBVisualId = new VisualStateId($"{ownerBId.Value}:visual");
        var scopeBId = new DocumentScopeId($"{ownerBId.Value}:scope");
        var taskYId = new SemanticElementId("bpmn:n8.2:mixed-history:nested:y");
        var taskYVisualId = new VisualStateId($"{taskYId.Value}:visual");

        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerAId,
            ownerAVisualId,
            rootScopeId,
            scopeAId,
            new PointD(1500d, 680d),
            new SizeD(120d, 80d),
            "NESTED_A",
            "Nested A",
            VisualPlacementMode.Pinned));
        Assert.True((await session.NavigateToScopeAsync(scopeAId)).Succeeded);

        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskXId,
            taskXVisualId,
            new PointD(150d, 140d),
            new SizeD(120d, 80d),
            "NESTED_X",
            "Nested X",
            802,
            VisualPlacementMode.Pinned,
            targetScopeId: scopeAId));
        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerBId,
            ownerBVisualId,
            scopeAId,
            scopeBId,
            new PointD(380d, 240d),
            new SizeD(120d, 80d),
            "NESTED_B",
            "Nested B",
            VisualPlacementMode.Pinned));
        Assert.True((await session.NavigateToScopeAsync(scopeBId)).Succeeded);

        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskYId,
            taskYVisualId,
            new PointD(180d, 150d),
            new SizeD(120d, 80d),
            "NESTED_Y",
            "Nested Y",
            803,
            VisualPlacementMode.Pinned,
            targetScopeId: scopeBId));
        var completeElements = new[] { ownerAId, taskXId, ownerBId, taskYId }
            .ToDictionary(
                static id => id,
                id => composition.Document.SemanticModel.Elements.Single(element =>
                    element.Id == id));
        var completeVisuals = new[]
            { ownerAVisualId, taskXVisualId, ownerBVisualId, taskYVisualId }
            .ToDictionary(
                static id => id,
                id => composition.Document.VisualModel.VisualStates.Single(visual =>
                    visual.Id == id));
        var completeScopes = new[] { scopeAId, scopeBId }
            .ToDictionary(
                static id => id,
                id => composition.Document.SemanticModel.NestedScopes.Single(scope =>
                    scope.Id == id));

        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);
        Assert.Equal(new HistoryStatus(7, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);

        await AssertAppliedUndoAsync(session, scopeBId);
        await AssertCommittedUndoAsync(session, scopeBId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == taskYId);
        await AssertAppliedUndoAsync(session, scopeAId);
        await AssertCommittedUndoAsync(session, scopeAId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == ownerBId);
        Assert.DoesNotContain(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == scopeBId);
        await AssertCommittedUndoAsync(session, scopeAId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == taskXId);
        await AssertAppliedUndoAsync(session, rootScopeId);
        await AssertCommittedUndoAsync(session, rootScopeId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == ownerAId);
        Assert.DoesNotContain(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == scopeAId);

        await AssertCommittedRedoAsync(session, rootScopeId);
        await AssertAppliedRedoAsync(session, scopeAId);
        await AssertCommittedRedoAsync(session, scopeAId);
        await AssertCommittedRedoAsync(session, scopeAId);
        await AssertAppliedRedoAsync(session, scopeBId);
        await AssertCommittedRedoAsync(session, scopeBId);
        await AssertAppliedRedoAsync(session, rootScopeId);

        foreach (var (id, expected) in completeElements)
        {
            Assert.Equal(expected, composition.Document.SemanticModel.Elements.Single(element =>
                element.Id == id));
        }

        foreach (var (id, expected) in completeVisuals)
        {
            Assert.Equal(expected, composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == id));
        }

        foreach (var (id, expected) in completeScopes)
        {
            Assert.Equal(expected, composition.Document.SemanticModel.NestedScopes.Single(scope =>
                scope.Id == id));
        }

        Assert.Equal(new HistoryStatus(7, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task RecursiveDeletionInterleavesExactlyWithNavigationAndChildEdits()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = await CreateRendererAsync("phase-n82-mixed-history-delete");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var ownerId = new SemanticElementId("bpmn:n8.2:mixed-history:delete:a");
        var ownerVisualId = new VisualStateId($"{ownerId.Value}:visual");
        var childScopeId = new DocumentScopeId($"{ownerId.Value}:scope");
        var taskId = new SemanticElementId("bpmn:n8.2:mixed-history:delete:x");
        var taskVisualId = new VisualStateId($"{taskId.Value}:visual");

        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerId,
            ownerVisualId,
            rootScopeId,
            childScopeId,
            new PointD(1500d, 680d),
            new SizeD(120d, 80d),
            "DELETE_A",
            "Delete A",
            VisualPlacementMode.Pinned));
        Assert.True((await session.NavigateToScopeAsync(childScopeId)).Succeeded);
        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskId,
            taskVisualId,
            new PointD(180d, 160d),
            new SizeD(120d, 80d),
            "DELETE_X",
            "Delete X",
            806,
            VisualPlacementMode.Pinned,
            targetScopeId: childScopeId));
        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);

        var expectedOwner = composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == ownerId);
        var expectedTask = composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == taskId);
        var expectedOwnerVisual = composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == ownerVisualId);
        var expectedTaskVisual = composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == taskVisualId);
        var expectedScope = composition.Document.SemanticModel.NestedScopes.Single(scope =>
            scope.Id == childScopeId);

        await ExecuteAsync(session, new DeleteBpmnFlowNodeCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerId,
            ownerVisualId));
        Assert.Equal(new HistoryStatus(5, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == ownerId || element.Id == taskId);
        Assert.DoesNotContain(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == childScopeId);

        await AssertCommittedUndoAsync(session, rootScopeId);
        Assert.Equal(expectedOwner, composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == ownerId));
        Assert.Equal(expectedTask, composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == taskId));
        Assert.Equal(expectedOwnerVisual,
            composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == ownerVisualId));
        Assert.Equal(expectedTaskVisual,
            composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == taskVisualId));
        Assert.Equal(expectedScope, composition.Document.SemanticModel.NestedScopes.Single(scope =>
            scope.Id == childScopeId));

        await AssertAppliedUndoAsync(session, childScopeId);
        await AssertCommittedUndoAsync(session, childScopeId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == taskId);
        await AssertAppliedUndoAsync(session, rootScopeId);
        await AssertCommittedUndoAsync(session, rootScopeId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == ownerId);
        Assert.DoesNotContain(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == childScopeId);

        await AssertCommittedRedoAsync(session, rootScopeId);
        await AssertAppliedRedoAsync(session, childScopeId);
        await AssertCommittedRedoAsync(session, childScopeId);
        await AssertAppliedRedoAsync(session, rootScopeId);
        await AssertCommittedRedoAsync(session, rootScopeId);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == ownerId || element.Id == taskId);
        Assert.DoesNotContain(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == childScopeId);
        Assert.Equal(new HistoryStatus(5, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task PersistentEditAfterUndoNavigationClearsGlobalRedo()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = await CreateRendererAsync("phase-n82-mixed-history-persistent-divergence");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var ownerId = new SemanticElementId("bpmn:n8.2:mixed-history:persistent-divergence:a");
        var ownerVisualId = new VisualStateId($"{ownerId.Value}:visual");
        var childScopeId = new DocumentScopeId($"{ownerId.Value}:scope");

        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerId,
            ownerVisualId,
            rootScopeId,
            childScopeId,
            new PointD(1500d, 680d),
            new SizeD(120d, 80d),
            "PERSISTENT_DIVERGENCE_A",
            "Persistent divergence A",
            VisualPlacementMode.Pinned));
        Assert.True((await session.NavigateToScopeAsync(childScopeId)).Succeeded);
        await AssertAppliedUndoAsync(session, rootScopeId);
        Assert.True(session.CaptureState().HistoryStatus.CanRedo);

        var taskId = new SemanticElementId("bpmn:n8.2:mixed-history:persistent-divergence:task");
        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskId,
            new VisualStateId($"{taskId.Value}:visual"),
            new PointD(1650d, 760d),
            new SizeD(120d, 80d),
            "PERSISTENT_DIVERGENCE_TASK",
            "Persistent divergence task",
            804,
            VisualPlacementMode.Pinned,
            targetScopeId: rootScopeId));

        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        Assert.Equal(
            HistoryOperationStatus.NothingToRedo,
            (await session.RedoAsync()).Status);
    }

    [Fact]
    public async Task NavigationAfterUndoPersistentEditClearsGlobalRedo()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = await CreateRendererAsync("phase-n82-mixed-history-navigation-divergence");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var ownerId = new SemanticElementId("bpmn:n8.2:mixed-history:navigation-divergence:a");
        var ownerVisualId = new VisualStateId($"{ownerId.Value}:visual");
        var childScopeId = new DocumentScopeId($"{ownerId.Value}:scope");

        await ExecuteAsync(session, new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            ownerId,
            ownerVisualId,
            rootScopeId,
            childScopeId,
            new PointD(1500d, 680d),
            new SizeD(120d, 80d),
            "NAVIGATION_DIVERGENCE_A",
            "Navigation divergence A",
            VisualPlacementMode.Pinned));
        var taskId = new SemanticElementId("bpmn:n8.2:mixed-history:navigation-divergence:task");
        await ExecuteAsync(session, new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            taskId,
            new VisualStateId($"{taskId.Value}:visual"),
            new PointD(1650d, 760d),
            new SizeD(120d, 80d),
            "NAVIGATION_DIVERGENCE_TASK",
            "Navigation divergence task",
            805,
            VisualPlacementMode.Pinned,
            targetScopeId: rootScopeId));
        await AssertCommittedUndoAsync(session, rootScopeId);
        Assert.True(session.CaptureState().HistoryStatus.CanRedo);

        Assert.True((await session.NavigateToScopeAsync(childScopeId)).Succeeded);

        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false),
            session.CaptureState().HistoryStatus);
        Assert.DoesNotContain(composition.Document.SemanticModel.Elements, element =>
            element.Id == taskId);
        Assert.Equal(
            HistoryOperationStatus.NothingToRedo,
            (await session.RedoAsync()).Status);
    }

    private static async ValueTask<Canvas2DRenderer> CreateRendererAsync(string canvasId)
    {
        var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
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
        var initialization = await renderer.InitializeAsync(
            canvasId,
            new Canvas2DSurfaceSize(1200d, 800d, 1d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        return renderer;
    }

    private static async ValueTask ExecuteAsync(
        EditingSession session,
        Inceptus.DocumentEngine.Contracts.Commands.ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static async ValueTask AssertAppliedUndoAsync(
        EditingSession session,
        DocumentScopeId expectedScopeId)
    {
        var revision = session.CaptureState().DocumentRevision;
        var result = await session.UndoAsync();
        Assert.True(result.IsApplied, Diagnostics(result.Diagnostics));
        Assert.False(result.IsCommitted);
        Assert.Equal(revision, session.CaptureState().DocumentRevision);
        Assert.Equal(expectedScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static async ValueTask AssertAppliedRedoAsync(
        EditingSession session,
        DocumentScopeId expectedScopeId)
    {
        var revision = session.CaptureState().DocumentRevision;
        var result = await session.RedoAsync();
        Assert.True(result.IsApplied, Diagnostics(result.Diagnostics));
        Assert.False(result.IsCommitted);
        Assert.Equal(revision, session.CaptureState().DocumentRevision);
        Assert.Equal(expectedScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static async ValueTask AssertCommittedUndoAsync(
        EditingSession session,
        DocumentScopeId expectedScopeId)
    {
        var result = await session.UndoAsync();
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expectedScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static async ValueTask AssertCommittedRedoAsync(
        EditingSession session,
        DocumentScopeId expectedScopeId)
    {
        var result = await session.RedoAsync();
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();
        Assert.Equal(expectedScopeId, state.ActiveScopeId);
        Assert.True(state.Status == EditingSessionStatus.Ready,
            $"Expected Ready after Redo in {expectedScopeId}, got {state.Status}. " +
            Diagnostics(state.RuntimeDiagnostics.Concat(state.PresentationDiagnostics)));
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
}
