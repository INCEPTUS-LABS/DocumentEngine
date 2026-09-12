using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN82EditingSessionScopeNavigationIntegrationTests
{
    private static readonly SemanticElementId NestedSubProcessId =
        new("bpmn:n8.2:integration:nested-subprocess");
    private static readonly VisualStateId NestedSubProcessVisualId =
        new("bpmn:n8.2:integration:nested-subprocess:visual");
    private static readonly DocumentScopeId NestedScopeId =
        new("bpmn:n8.2:integration:nested-subprocess:scope");

    [Fact]
    public async Task NestedNavigationRestoresViewportsAndReplaysChronologically()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
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
            "phase-n82-editing-session-scope-navigation",
            new Canvas2DSurfaceSize(1000d, 700d, 1.25d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var rootViewport = await SetViewportAsync(
            session,
            new ViewportSnapshot(0.8d, new VectorD(48d, -32d)));
        var revisionBeforeNavigation = composition.Document.Revision;
        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        var firstChildVisit = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, firstChildVisit.ActiveScopeId);
        Assert.Equal(ViewportSnapshot.Default.Zoom, firstChildVisit.EditorState.Viewport.Zoom);
        Assert.Equal(ViewportSnapshot.Default.Pan, firstChildVisit.EditorState.Viewport.Pan);
        Assert.Equal(revisionBeforeNavigation, composition.Document.Revision);
        Assert.Equal(1, firstChildVisit.HistoryStatus.EntryCount);
        var childViewport = await SetViewportAsync(
            session,
            new ViewportSnapshot(1.1d, new VectorD(-27d, 19d)));

        var creation = await session.ExecuteAsync(new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            NestedSubProcessId,
            NestedSubProcessVisualId,
            BpmnDemoPipeline.ProcessOrderScopeId,
            NestedScopeId,
            new PointD(520d, 260d),
            new SizeD(120d, 80d),
            "NESTED_PROCESS",
            "Nested process",
            VisualPlacementMode.Pinned));
        Assert.True(creation.IsCommitted, Diagnostics(creation.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var createdRevision = composition.Document.Revision;
        var created = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, created.ActiveScopeId);
        Assert.Equal(2, created.HistoryStatus.EntryCount);
        Assert.Contains(created.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == NestedSubProcessId);
        var nestedScope = Assert.Single(
            composition.Document.SemanticModel.NestedScopes,
            scope => scope.Id == NestedScopeId);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, nestedScope.ParentScopeId);
        Assert.Equal(NestedSubProcessId, nestedScope.OwnerSemanticElementId);

        Assert.True((await session.NavigateToScopeAsync(NestedScopeId)).Succeeded);
        var firstNestedVisit = session.CaptureState();
        Assert.Equal(NestedScopeId, firstNestedVisit.ActiveScopeId);
        Assert.Empty(firstNestedVisit.ProjectedGraph!.Nodes);
        Assert.Empty(firstNestedVisit.ProjectedGraph.Edges);
        Assert.Equal(ViewportSnapshot.Default.Zoom, firstNestedVisit.EditorState.Viewport.Zoom);
        Assert.Equal(ViewportSnapshot.Default.Pan, firstNestedVisit.EditorState.Viewport.Pan);
        var nestedViewport = await SetViewportAsync(
            session,
            new ViewportSnapshot(0.6d, new VectorD(13d, 41d)));

        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        AssertViewport(childViewport, session.CaptureState().EditorState.Viewport);
        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);
        AssertViewport(rootViewport, session.CaptureState().EditorState.Viewport);
        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        AssertViewport(childViewport, session.CaptureState().EditorState.Viewport);
        Assert.True((await session.NavigateToScopeAsync(NestedScopeId)).Succeeded);
        AssertViewport(nestedViewport, session.CaptureState().EditorState.Viewport);
        Assert.Equal(createdRevision, composition.Document.Revision);
        Assert.Equal(7, session.CaptureState().HistoryStatus.EntryCount);

        var undo = await session.UndoAsync();
        Assert.True(undo.IsApplied, Diagnostics(undo.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var recovered = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, recovered.Status);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, recovered.ActiveScopeId);
        Assert.Null(recovered.LastKnownGoodScene);
        Assert.Contains(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == NestedScopeId);
        Assert.Contains(recovered.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == NestedSubProcessId);
        AssertViewport(childViewport, recovered.EditorState.Viewport);
        Assert.Equal(createdRevision, composition.Document.Revision);

        var redo = await session.RedoAsync();
        Assert.True(redo.IsApplied, Diagnostics(redo.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var restored = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, restored.Status);
        Assert.Equal(NestedScopeId, restored.ActiveScopeId);
        Assert.Contains(composition.Document.SemanticModel.NestedScopes, scope =>
            scope.Id == NestedScopeId);
        Assert.Empty(restored.ProjectedGraph!.Nodes);
        AssertViewport(nestedViewport, restored.EditorState.Viewport);
        Assert.Equal(createdRevision, composition.Document.Revision);
    }

    [Fact]
    public async Task RecursiveDeletionRemovesDescendantScopesAndCachedViewportsStayHarmless()
    {
        var outerSubProcessId = new SemanticElementId(
            "bpmn:n8.2:integration:recursive-outer");
        var outerVisualId = new VisualStateId(
            "bpmn:n8.2:integration:recursive-outer:visual");
        var outerScopeId = new DocumentScopeId(
            "bpmn:n8.2:integration:recursive-outer:scope");
        var innerSubProcessId = new SemanticElementId(
            "bpmn:n8.2:integration:recursive-inner");
        var innerVisualId = new VisualStateId(
            "bpmn:n8.2:integration:recursive-inner:visual");
        var innerScopeId = new DocumentScopeId(
            "bpmn:n8.2:integration:recursive-inner:scope");
        var descendantTaskId = new SemanticElementId(
            "bpmn:n8.2:integration:recursive-task");
        var descendantTaskVisualId = new VisualStateId(
            "bpmn:n8.2:integration:recursive-task:visual");
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
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
            "phase-n82-recursive-scope-deletion",
            new Canvas2DSurfaceSize(1000d, 700d, 1.25d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);

        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        var childViewport = await SetViewportAsync(
            session,
            new ViewportSnapshot(1.2d, new VectorD(-31d, 24d)));
        var outerCreation = await session.ExecuteAsync(new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            outerSubProcessId,
            outerVisualId,
            BpmnDemoPipeline.ProcessOrderScopeId,
            outerScopeId,
            new PointD(500d, 360d),
            new SizeD(120d, 80d),
            "OUTER_PROCESS",
            "Outer process",
            VisualPlacementMode.Pinned));
        Assert.True(outerCreation.IsCommitted, Diagnostics(outerCreation.Diagnostics));
        await session.WaitForIdleAsync();

        var initialOuterNavigation = await session.NavigateToScopeAsync(outerScopeId);
        Assert.True(
            initialOuterNavigation.Succeeded,
            $"{session.CaptureState().Status}: {Diagnostics(initialOuterNavigation.Diagnostics)}");
        var outerViewport = await SetViewportAsync(
            session,
            new ViewportSnapshot(0.7d, new VectorD(17d, -13d)));
        var innerCreation = await session.ExecuteAsync(new CreateBpmnSubProcessCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            innerSubProcessId,
            innerVisualId,
            outerScopeId,
            innerScopeId,
            new PointD(300d, 220d),
            new SizeD(120d, 80d),
            "INNER_PROCESS",
            "Inner process",
            VisualPlacementMode.Pinned));
        Assert.True(innerCreation.IsCommitted, Diagnostics(innerCreation.Diagnostics));
        await session.WaitForIdleAsync();

        Assert.True((await session.NavigateToScopeAsync(innerScopeId)).Succeeded);
        var innerViewport = await SetViewportAsync(
            session,
            new ViewportSnapshot(0.55d, new VectorD(71d, 38d)));
        var descendantCreation = await session.ExecuteAsync(new CreateBpmnTaskCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            descendantTaskId,
            descendantTaskVisualId,
            new PointD(180d, 140d),
            new SizeD(120d, 80d),
            "DESCENDANT_TASK",
            "Descendant task",
            999,
            VisualPlacementMode.Pinned,
            targetScopeId: innerScopeId));
        Assert.True(
            descendantCreation.IsCommitted,
            Diagnostics(descendantCreation.Diagnostics));
        await session.WaitForIdleAsync();
        var completeTree = composition.Document.CaptureSnapshot();
        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        AssertViewport(childViewport, session.CaptureState().EditorState.Viewport);

        var deletion = await session.ExecuteAsync(new DeleteBpmnFlowNodeCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            outerSubProcessId,
            outerVisualId));
        Assert.True(deletion.IsCommitted, Diagnostics(deletion.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var deleted = composition.Document.CaptureSnapshot();
        var recovered = session.CaptureState();
        Assert.True(
            recovered.Status == EditingSessionStatus.Ready,
            $"{recovered.Status}: {Diagnostics(recovered.RuntimeDiagnostics)}");
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, recovered.ActiveScopeId);
        AssertViewport(childViewport, recovered.EditorState.Viewport);
        Assert.DoesNotContain(deleted.SemanticModel.Elements, element =>
            element.Id == outerSubProcessId ||
            element.Id == innerSubProcessId ||
            element.Id == descendantTaskId);
        Assert.DoesNotContain(deleted.SemanticModel.NestedScopes, scope =>
            scope.Id == outerScopeId || scope.Id == innerScopeId);
        Assert.DoesNotContain(deleted.SemanticModel.ScopeMemberships, membership =>
            membership.ScopeId == outerScopeId || membership.ScopeId == innerScopeId);
        Assert.DoesNotContain(deleted.VisualModel.VisualStates, visual =>
            visual.Id == outerVisualId ||
            visual.Id == innerVisualId ||
            visual.Id == descendantTaskVisualId);

        var beforeMissingNavigation = session.CaptureState();
        var missingNavigation = await session.NavigateToScopeAsync(innerScopeId);
        Assert.False(missingNavigation.Succeeded);
        var afterMissingNavigation = session.CaptureState();
        Assert.Equal(beforeMissingNavigation.ActiveScopeId, afterMissingNavigation.ActiveScopeId);
        Assert.Same(beforeMissingNavigation.CurrentScene, afterMissingNavigation.CurrentScene);
        AssertViewport(
            beforeMissingNavigation.EditorState.Viewport,
            afterMissingNavigation.EditorState.Viewport);

        var undo = await session.UndoAsync();
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        AssertAuthoritativeContentEqual(
            completeTree,
            composition.Document.CaptureSnapshot());
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            session.CaptureState().ActiveScopeId);

        var redo = await session.RedoAsync();
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var recoveredAgain = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, recoveredAgain.Status);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, recoveredAgain.ActiveScopeId);
        AssertViewport(childViewport, recoveredAgain.EditorState.Viewport);
        AssertAuthoritativeContentEqual(
            deleted,
            composition.Document.CaptureSnapshot());

        var restoreAgain = await session.UndoAsync();
        Assert.True(restoreAgain.IsCommitted, Diagnostics(restoreAgain.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var restoredChild = session.CaptureState();
        Assert.True(
            restoredChild.Status == EditingSessionStatus.Ready,
            $"{restoredChild.Status}: {Diagnostics(restoredChild.RuntimeDiagnostics)}");
        var restoredOuterNavigation = await session.NavigateToScopeAsync(outerScopeId);
        Assert.True(
            restoredOuterNavigation.Succeeded,
            $"{session.CaptureState().Status}: {Diagnostics(restoredOuterNavigation.Diagnostics)}");
        AssertViewport(outerViewport, session.CaptureState().EditorState.Viewport);
        Assert.True((await session.NavigateToScopeAsync(innerScopeId)).Succeeded);
        var restoredDescendant = session.CaptureState();
        Assert.Equal(innerScopeId, restoredDescendant.ActiveScopeId);
        AssertViewport(innerViewport, restoredDescendant.EditorState.Viewport);
        Assert.Contains(restoredDescendant.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == descendantTaskId);

        var divergentRedo = await session.RedoAsync();
        Assert.Equal(HistoryOperationStatus.NothingToRedo, divergentRedo.Status);
    }

    private static async ValueTask<ViewportSnapshot> SetViewportAsync(
        EditingSession session,
        ViewportSnapshot requested)
    {
        var update = await session.UpdateViewportAsync(requested);
        Assert.True(update.Succeeded, Diagnostics(update.Diagnostics));
        return session.CaptureState().EditorState.Viewport;
    }

    private static void AssertViewport(ViewportSnapshot expected, ViewportSnapshot actual)
    {
        Assert.Equal(expected.Zoom, actual.Zoom);
        Assert.Equal(expected.Pan, actual.Pan);
        var expectedRegion = expected.VisibleDocumentRegion;
        var actualRegion = actual.VisibleDocumentRegion;
        Assert.Equal(expectedRegion is null, actualRegion is null);
        if (expectedRegion is null)
        {
            return;
        }

        Assert.True(actualRegion.HasValue);
        var expectedBounds = expectedRegion.Value;
        var actualBounds = actualRegion.GetValueOrDefault();
        Assert.Equal(expectedBounds.X, actualBounds.X, 10);
        Assert.Equal(expectedBounds.Y, actualBounds.Y, 10);
        Assert.Equal(
            expectedBounds.Width,
            actualBounds.Width,
            10);
        Assert.Equal(
            expectedBounds.Height,
            actualBounds.Height,
            10);
    }

    private static void AssertAuthoritativeContentEqual(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot expected,
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot actual)
    {
        Assert.Equal(
            expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(
            expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(
            expected.Metadata.SystemManagedProperties,
            actual.Metadata.SystemManagedProperties);
        Assert.Equal(
            expected.Metadata.ExtensionProperties,
            actual.Metadata.ExtensionProperties);
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));
}
