using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.ContextMenus;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly ElementPropertyFieldId NameFieldId =
        NeutralDemoPropertiesSchemas.NameFieldId;
    private static readonly ElementPropertyFieldId ElementNumberFieldId =
        NeutralDemoPropertiesSchemas.ElementNumberFieldId;
    private static readonly ElementPropertyFieldId DescriptionFieldId =
        NeutralDemoPropertiesSchemas.DescriptionFieldId;
    private static readonly ElementPropertyFieldId TimerDefinitionFieldId =
        new("timer-definition");

    [Fact]
    public async Task InitializeAttachesOneReadySessionAndIsIdempotent()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.5d));
        await using var host = CreateHost(execution, observer);

        await host.InitializeAsync("canvas", "container");
        await host.InitializeAsync("canvas", "container");
        var state = host.CaptureState();

        Assert.True(state.InitializationAttempted);
        Assert.True(state.IsInitialized);
        Assert.False(state.IsDisposed);
        Assert.True(state.LatestPresentationSucceeded);
        Assert.Equal(1, state.SuccessfulRenderCount);
        Assert.Equal(EditingSessionStatus.Ready, state.Session?.Status);
        Assert.NotNull(state.Session?.CurrentScene);
        Assert.Equal(1.08d, state.Session?.EditorState.Viewport.Zoom);
        Assert.Equal(
            108,
            DocumentCanvasZoomPolicy.ToPercentage(state.Session!.EditorState.Viewport.Zoom));
        Assert.Equal(1, observer.StartCount);
        Assert.Equal(1, PointerObserver(host).StartCount);
        Assert.Equal(["initialize:canvas", "render"], execution.Calls);
        Assert.Equal(5, state.PipelineCounters?.ProjectionRuleInvocationCount);
        Assert.Equal(1, state.PipelineCounters?.LayoutInvocationCount);
        Assert.Equal(1, state.PipelineCounters?.RoutingInvocationCount);
        Assert.Equal(1, state.PipelineCounters?.SceneContributionInvocationCount);
        var rootBreadcrumb = Assert.Single(state.ScopeBreadcrumb);
        Assert.Equal(DocumentCanvasScopeBreadcrumb.RootLabel, rootBreadcrumb.Label);
        Assert.Equal(state.Session!.ActiveScopeId, rootBreadcrumb.ScopeId);
        Assert.True(rootBreadcrumb.IsActive);
    }

    [Fact]
    public async Task OrganizationalPoolMenuPropertiesAndDeletionKeepOneProcessAndExactContent()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        await EnableOrganizationalProfileAsync(session);
        var before = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();
        var poolId = await AddOrganizationalPoolFromMenuAsync(host);

        var added = session.CaptureState();
        var pool = Assert.Single(document.SemanticModel.Elements, element => element.Id == poolId);
        Assert.Equal(OrganizationalSemanticTypes.Pool, pool.TypeId);
        Assert.Equal(before.ActiveScopeId, document.SemanticModel.GetScope(poolId).Id);
        Assert.Equal(before.ActiveScopeId, added.ActiveScopeId);
        Assert.Equal(before.DocumentRevision.Increment(), added.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, added.HistoryStatus.EntryCount);
        Assert.Equal(beforeDocument.SemanticModel.NestedScopes.AsEnumerable(), document.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(beforeDocument.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates.AsEnumerable());
        Assert.DoesNotContain(document.SemanticModel.Elements,
            element => element.TypeId == BpmnSemanticTypes.Participant ||
                       element.TypeId == BpmnSemanticTypes.Collaboration);
        Assert.DoesNotContain(document.VisualModel.VisualStates,
            visual => visual.SemanticElementId == poolId);
        Assert.NotEmpty(document.SemanticModel.ProfileAssignments);
        Assert.All(document.SemanticModel.ProfileAssignments,
            assignment => Assert.Equal(poolId, assignment.ContainerSemanticElementId));
        Assert.Equal(poolId, added.EditorState.SemanticSceneSelection);
        Assert.Empty(added.EditorState.Selection);
        Assert.Equal(before.ProjectedGraph!.Nodes.AsEnumerable(), added.ProjectedGraph!.Nodes.AsEnumerable());
        Assert.Equal(before.ProjectedGraph.Edges.AsEnumerable(), added.ProjectedGraph.Edges.AsEnumerable());
        Assert.Equal(before.LayoutResult!.Nodes.AsEnumerable(), added.LayoutResult!.Nodes.AsEnumerable());
        Assert.Equal(before.RoutingResult!.Routes.AsEnumerable(), added.RoutingResult!.Routes.AsEnumerable());

        await OpenOrganizationalPoolContextAsync(host, poolId);
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        Assert.Equal(DocumentCanvasContextMenuKind.Element, menu.Kind);
        Assert.Null(menu.TargetVisualStateId);
        Assert.Equal(poolId, menu.TargetSemanticElementId);
        Assert.NotNull(menu.DeletionAction);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(poolId));
        Assert.Null(properties.VisualStateId);
        Assert.False(properties.CanEditBounds);
        Assert.Equal(OrganizationalSemanticTypes.Pool, properties.TypeId);
        Assert.Equal(["name", "description"],
            properties.DataFields.Select(static field => field.FieldId.Value));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        Assert.True(draft.TryGetDataField(new ElementPropertyFieldId("name"), out var name));
        name!.EditorValue = "Fulfillment";
        Assert.True(host.UpdatePropertiesFormState(isOpen: true, targetVisualStateId: null,
            isDirty: true, targetSemanticElementId: poolId));
        var renamed = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, renamed.Status);
        Assert.True(host.UpdatePropertiesFormState(isOpen: false, targetVisualStateId: null,
            isDirty: false, targetSemanticElementId: poolId));
        var namedPool = Assert.Single(document.SemanticModel.Elements, element => element.Id == poolId);
        Assert.Equal("Fulfillment", namedPool.Properties[OrganizationalSemanticProperties.Name].TextValue);
        var assignments = document.SemanticModel.ProfileAssignments;
        var presentations = document.VisualModel.ProfileElementPresentations;
        var beforeDelete = session.CaptureState();

        await OpenOrganizationalPoolContextAsync(host, poolId);
        Assert.True((await host.ExecuteDeletionContextActionAsync())?.IsCommitted);
        var deleted = session.CaptureState();
        Assert.False(document.SemanticModel.TryGetElement(poolId, out _));
        Assert.Empty(document.SemanticModel.ProfileAssignments);
        Assert.Empty(document.VisualModel.ProfileElementPresentations);
        Assert.Equal(beforeDocument.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(beforeDocument.SemanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(beforeDelete.DocumentRevision.Increment(), deleted.DocumentRevision);
        Assert.Equal(beforeDelete.HistoryStatus.EntryCount + 1, deleted.HistoryStatus.EntryCount);
        Assert.Null(deleted.EditorState.SemanticSceneSelection);
        Assert.Null(deleted.CurrentScene!.SpatialPresentationPlan);
        await host.UndoAsync();
        Assert.Equal(namedPool, Assert.Single(document.SemanticModel.Elements,
            element => element.Id == poolId));
        Assert.Equal(assignments.AsEnumerable(), document.SemanticModel.ProfileAssignments.AsEnumerable());
        Assert.Equal(presentations.AsEnumerable(), document.VisualModel.ProfileElementPresentations.AsEnumerable());
        await host.RedoAsync();
        Assert.False(document.SemanticModel.TryGetElement(poolId, out _));
        Assert.Equal(beforeDocument.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task PoolCollapseAndSelectionAreTransientAndNeverActivateAnotherProcess()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        await EnableOrganizationalProfileAsync(session);
        var firstPool = await AddOrganizationalPoolFromMenuAsync(host);
        var secondPool = await AddOrganizationalPoolFromMenuAsync(host);
        var expanded = session.CaptureState();
        var document = AttachedDocument(session);
        var snapshot = document.CaptureSnapshot();
        var poolRegions = expanded.CurrentScene!.SpatialPresentationPlan!.Regions
            .Where(static region => region.ContainerSemanticElementId is not null).ToArray();
        Assert.Equal(2, poolRegions.Length);
        Assert.All(poolRegions, region => Assert.Equal(poolRegions[0].Bounds.Width, region.Bounds.Width));

        await OpenOrganizationalPoolContextAsync(host, firstPool);
        Assert.True((await host.ExecuteSemanticSceneViewContextActionAsync(
            OrganizationalPoolActions.CollapseId))?.Succeeded);
        var collapsed = session.CaptureState();
        Assert.Same(snapshot, document.CaptureSnapshot());
        Assert.Equal(expanded.DocumentRevision, collapsed.DocumentRevision);
        Assert.Equal(expanded.HistoryStatus, collapsed.HistoryStatus);
        Assert.Equal(expanded.ActiveScopeId, collapsed.ActiveScopeId);
        Assert.True(collapsed.ModelProfileElementViewState.IsCollapsed(
            BpmnModelProfiles.OrganizationalId, firstPool));
        Assert.False(collapsed.ModelProfileElementViewState.IsCollapsed(
            BpmnModelProfiles.OrganizationalId, secondPool));
        Assert.DoesNotContain(collapsed.CurrentScene!.Items,
            item => item.IsVisible && item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);

        await OpenOrganizationalPoolContextAsync(host, firstPool);
        Assert.True((await host.ExecuteSemanticSceneViewContextActionAsync(
            OrganizationalPoolActions.ExpandId))?.Succeeded);
        var restored = session.CaptureState();
        var body = restored.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        await PointerObserver(host).ClickDocumentPointAsync(restored.CurrentScene, Center(body.Bounds));
        var selected = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.TaskVisualId, Assert.Single(selected.EditorState.Selection));
        Assert.Null(selected.EditorState.SemanticSceneSelection);
        Assert.Equal(expanded.ActiveScopeId, selected.ActiveScopeId);
        Assert.Equal(expanded.DocumentRevision, selected.DocumentRevision);
        Assert.Equal(expanded.HistoryStatus, selected.HistoryStatus);
        await OpenOrganizationalPoolContextAsync(host, secondPool);
        Assert.Empty(session.CaptureState().EditorState.Selection);
        Assert.Equal(secondPool, session.CaptureState().EditorState.SemanticSceneSelection);
        var beforeHide = session.CaptureState();
        Assert.True((await session.UpdateModelProfileViewStateAsync(
            beforeHide.ModelProfileViewState.WithPreferredVisibility(
                BpmnModelProfiles.OrganizationalId, false))).Succeeded);
        var hidden = session.CaptureState();
        Assert.Null(hidden.EditorState.SemanticSceneSelection);
        Assert.NotNull(hidden.CurrentScene!.SpatialPresentationPlan);
        Assert.Equal(beforeHide.CurrentScene!.SpatialPresentationPlan, hidden.CurrentScene.SpatialPresentationPlan);
        Assert.DoesNotContain(hidden.CurrentScene.Items,
            item => item.Origin.SemanticElementId == firstPool || item.Origin.SemanticElementId == secondPool);
        Assert.Same(snapshot, document.CaptureSnapshot());
        Assert.Equal(expanded.HistoryStatus, hidden.HistoryStatus);
    }

    [Fact]
    public async Task PoolReorderUsesVisualPresentationHistoryAndKeepsContentGeometry()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        await EnableOrganizationalProfileAsync(session);
        var firstPool = await AddOrganizationalPoolFromMenuAsync(host);
        var secondPool = await AddOrganizationalPoolFromMenuAsync(host);
        var document = AttachedDocument(session);
        var before = session.CaptureState();
        var semanticModel = document.SemanticModel;
        var visuals = document.VisualModel.VisualStates;
        Assert.Empty(OrganizationalSemantics.GetAssignedElements(document.CaptureSnapshot(), secondPool));
        await OpenOrganizationalPoolContextAsync(host, secondPool);
        var menu = host.CaptureState().ContextMenu!;
        Assert.Contains(menu.SemanticCommandActions,
            action => action.Id == OrganizationalPoolActions.MoveUpId);
        Assert.DoesNotContain(menu.SemanticCommandActions,
            action => action.Id == OrganizationalPoolActions.MoveDownId);
        Assert.True((await host.ExecuteSemanticSceneCommandContextActionAsync(
            OrganizationalPoolActions.MoveUpId))?.IsCommitted);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.Equal(semanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(semanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(semanticModel.ProfileAssignments.AsEnumerable(), document.SemanticModel.ProfileAssignments.AsEnumerable());
        Assert.Equal(visuals.AsEnumerable(), document.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(new[] { secondPool, firstPool },
            OrganizationalSemantics.GetOrderedPoolsInScope(document.CaptureSnapshot(), after.ActiveScopeId)
                .Select(static pool => pool.Id));
        Assert.Equal(new[] { secondPool, firstPool },
            after.CurrentScene!.SpatialPresentationPlan!.Regions
                .Where(static region => region.ContainerSemanticElementId is not null)
                .OrderBy(static region => region.Bounds.Top)
                .Select(static region => region.ContainerSemanticElementId));
        await host.UndoAsync();
        Assert.Equal(new[] { firstPool, secondPool },
            OrganizationalSemantics.GetOrderedPoolsInScope(document.CaptureSnapshot(), after.ActiveScopeId)
                .Select(static pool => pool.Id));
        await host.RedoAsync();
        Assert.Equal(new[] { secondPool, firstPool },
            OrganizationalSemantics.GetOrderedPoolsInScope(document.CaptureSnapshot(), after.ActiveScopeId)
                .Select(static pool => pool.Id));
        Assert.Equal(visuals.AsEnumerable(), document.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task ToolboxCreationInSecondPoolIsOneActiveScopeCompoundAndPropertiesTargetExactVisual()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        await EnableOrganizationalProfileAsync(session);
        await AddOrganizationalPoolFromMenuAsync(host);
        var secondPool = await AddOrganizationalPoolFromMenuAsync(host);
        var before = session.CaptureState();
        var document = AttachedDocument(session);
        var beforeDocument = document.CaptureSnapshot();
        var region = before.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            item => item.ContainerSemanticElementId == secondPool);
        var point = Center(region.Bounds);
        await PointerObserver(host).ContextMenuDocumentPointAsync(before.CurrentScene, point);
        Assert.Equal(DocumentCanvasContextMenuKind.Background, host.CaptureState().ContextMenu!.Kind);
        host.CloseContextMenu();
        var taskItem = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions)
            .Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        Assert.True(selection.Select(taskItem.ItemId));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).ClickDocumentPointAsync(session.CaptureState().CurrentScene!, point);
        await session.WaitForIdleAsync();
        var after = session.CaptureState();
        var createdTask = Assert.Single(document.SemanticModel.Elements,
            element => !beforeDocument.SemanticModel.Elements.Any(old => old.Id == element.Id));
        var visual = Assert.Single(document.VisualModel.VisualStates,
            candidate => candidate.SemanticElementId == createdTask.Id);
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.Equal(before.ActiveScopeId, document.SemanticModel.GetScope(createdTask.Id).Id);
        Assert.Equal(secondPool, Assert.Single(document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == createdTask.Id).ContainerSemanticElementId);
        Assert.Equal(visual.Id, Assert.Single(after.EditorState.Selection));
        Assert.Null(after.EditorState.SemanticSceneSelection);
        var body = after.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == visual.Id && Canvas2DNodeBodyMetadata.IsNodeBody(item));
        Assert.Equal(secondPool, body.SpatialRegion!.ContainerSemanticElementId);
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [BpmnDemoPipeline.TaskVisualId, visual.Id],
            viewport: after.EditorState.Viewport))).Succeeded);
        var multiSelected = session.CaptureState();
        body = multiSelected.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == visual.Id && Canvas2DNodeBodyMetadata.IsNodeBody(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(multiSelected.CurrentScene, Center(body.Bounds));
        Assert.Equal(2, session.CaptureState().EditorState.Selection.Length);
        Assert.Equal(after.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(after.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(visual.Id, host.CaptureState().ContextMenu!.TargetVisualStateId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(visual.Id));
        Assert.Equal(before.ActiveScopeId, properties.InteractionScopeId);
        Assert.Equal(body.SpatialRegion, properties.SpatialRegion);
        Assert.True(properties.CanEditBounds);
        Assert.True(host.UpdatePropertiesFormState(isOpen: false, targetVisualStateId: visual.Id,
            isDirty: false, targetPresentationId: body.SpatialRegion!.Id));
        await host.UndoAsync();
        Assert.False(document.SemanticModel.TryGetElement(createdTask.Id, out _));
        Assert.DoesNotContain(document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == createdTask.Id);
        await host.RedoAsync();
        Assert.Equal(visual, Assert.Single(document.VisualModel.VisualStates,
            candidate => candidate.Id == visual.Id));
        Assert.Equal(secondPool, Assert.Single(document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == createdTask.Id).ContainerSemanticElementId);
    }

    [Fact]
    public async Task SpatialConnectorAnchorMenuSurvivesHoverLeaveRecompositionAndCommits()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(execution, surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        await EnableOrganizationalProfileAsync(session);
        var poolId = await AddOrganizationalPoolFromMenuAsync(host);
        var pointer = PointerObserver(host);
        var scene = session.CaptureState().CurrentScene!;
        var task = scene.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        Assert.Equal(poolId, task.SpatialRegion?.ContainerSemanticElementId);

        await pointer.ClickDocumentPointAsync(scene, Center(task.Bounds));
        scene = session.CaptureState().CurrentScene!;
        task = scene.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var edge = ResizeZone(scene, "east");
        var edgePoint = new PointD(
            Center(edge.Bounds).X,
            task.Bounds.Top + (task.Bounds.Height * 0.25d));
        var beforeAnchors = document.VisualModel.VisualStates.Single(item =>
            item.Id == BpmnDemoPipeline.TaskVisualId).ConnectorAnchors;
        await pointer.MoveDocumentPointAsync(scene, edgePoint);
        var hovered = session.CaptureState();
        Assert.Equal(edge.Id, hovered.EditorState.HoveredObjectId);

        await pointer.ContextMenuDocumentPointAsync(hovered.CurrentScene!, edgePoint);
        var beforeLeave = session.CaptureState();
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            menu.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
        Assert.True(action.CanAdd(ConnectorAnchorRole.Source));
        Assert.Equal(poolId, menu.TargetPresentation?.ContainerSemanticElementId);

        await pointer.LeaveAsync();
        var afterLeave = session.CaptureState();
        var retainedMenu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.Equal(EditingSessionStatus.Ready, afterLeave.Status);
        Assert.Null(afterLeave.EditorState.HoveredObjectId);
        Assert.Equal(BpmnDemoPipeline.TaskVisualId,
            Assert.Single(afterLeave.EditorState.Selection));
        Assert.Equal(menu.TargetPresentation, retainedMenu.TargetPresentation);
        Assert.Equal(action, retainedMenu.ConnectorAnchorAction);
        Assert.Equal(beforeLeave.DocumentRevision, afterLeave.DocumentRevision);
        Assert.Equal(beforeLeave.HistoryStatus, afterLeave.HistoryStatus);

        scene = afterLeave.CurrentScene!;
        task = scene.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        edge = ResizeZone(scene, "east");
        edgePoint = new PointD(
            Center(edge.Bounds).X,
            task.Bounds.Top + (task.Bounds.Height * 0.25d));
        await pointer.MoveDocumentPointAsync(scene, edgePoint);
        await pointer.ContextMenuDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            edgePoint);
        var leaveUpdate = pointer.LeaveAsync();
        var commitUpdate = host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source).AsTask();
        await leaveUpdate;
        var committed = await commitUpdate;
        Assert.True(committed?.IsCommitted);
        var afterCommit = session.CaptureState();
        Assert.Equal(beforeLeave.DocumentRevision.Increment(), afterCommit.DocumentRevision);
        Assert.Equal(beforeLeave.HistoryStatus.EntryCount + 1,
            afterCommit.HistoryStatus.EntryCount);
        var taskVisual = document.VisualModel.VisualStates.Single(item =>
            item.Id == BpmnDemoPipeline.TaskVisualId);
        Assert.Equal(beforeAnchors.Length + 1, taskVisual.ConnectorAnchors.Length);
        var anchor = Assert.Single(taskVisual.ConnectorAnchors,
            candidate => !beforeAnchors.Any(original => original.Id == candidate.Id));
        Assert.Equal(ConnectorAnchorSide.Right, anchor.Side);
        Assert.Equal(ConnectorAnchorRole.Source, anchor.Role);
    }

    private static async ValueTask EnableOrganizationalProfileAsync(EditingSession session)
    {
        var state = session.CaptureState();
        Assert.True((await session.ExecuteAsync(new SetModelProfileAvailabilityCommand(
            state.DocumentId, state.DocumentRevision,
            [new ModelProfileAvailabilityChange(BpmnModelProfiles.OrganizationalId, true)]))).IsCommitted);
        await session.WaitForIdleAsync();
    }

    private static async ValueTask<SemanticElementId> AddOrganizationalPoolFromMenuAsync(
        DocumentCanvasHost host)
    {
        var state = Session(host).CaptureState();
        var emptyPoint = FindEmptyDocumentPoint(state.CurrentScene!,
            new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await PointerObserver(host).ContextMenuDocumentPointAsync(state.CurrentScene!, emptyPoint);
        var action = Assert.Single(host.CaptureState().ContextMenu!.BackgroundActions);
        Assert.Equal(OrganizationalCanvasBackgroundActions.AddPoolId, action.Id);
        Assert.Equal("Pool", action.DisplayName);
        Assert.True((await host.ExecuteBackgroundContextActionAsync(action.Id))?.IsCommitted);
        return Assert.IsType<SemanticElementId>(Session(host).CaptureState().EditorState.SemanticSceneSelection);
    }

    private static async ValueTask OpenOrganizationalPoolContextAsync(
        DocumentCanvasHost host, SemanticElementId poolId)
    {
        var scene = Session(host).CaptureState().CurrentScene!;
        var header = scene.Items.Single(item => item.Origin.SemanticElementId == poolId &&
            Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(header.Bounds));
    }
    [Fact]
    public async Task EmptyCanvasAndElementContextMenusAreMutuallyExclusive()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var emptyPoint = FindEmptyDocumentPoint(
            scene,
            new Canvas2DSurfaceSize(1100d, 700d, 1d));

        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, emptyPoint);

        var background = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.Equal(DocumentCanvasContextMenuKind.Background, background.Kind);
        Assert.Null(background.TargetVisualStateId);
        Assert.Empty(background.BackgroundActions);

        var body = scene.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(body.Bounds));

        var element = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.Equal(DocumentCanvasContextMenuKind.Element, element.Kind);
        Assert.Equal(BpmnDemoPipeline.TaskVisualId, element.TargetVisualStateId);
        Assert.Empty(element.BackgroundActions);
    }


    [Fact]
    public async Task ModelViewPropertiesCancelAndMixedApplyPreserveExactAuthorities()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var emptyPoint = FindEmptyDocumentPoint(
            before.CurrentScene!,
            new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            before.CurrentScene!,
            emptyPoint);
        var snapshot = Assert.IsType<DocumentCanvasModelViewPropertiesSnapshot>(
            await host.OpenModelViewPropertiesAsync());
        var cancelledDraft = new DocumentCanvasModelViewPropertiesDraft(snapshot);
        Assert.True(cancelledDraft.TryGetProfile(
            BpmnModelProfiles.OrganizationalId,
            out var cancelledOrganization));
        Assert.NotNull(cancelledOrganization);
        cancelledOrganization.IsAvailable = true;
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: true, isDirty: true));
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: false, isDirty: false));
        var afterCancel = session.CaptureState();
        Assert.Equal(before.DocumentRevision, afterCancel.DocumentRevision);
        Assert.Equal(before.HistoryStatus, afterCancel.HistoryStatus);
        Assert.Equal(before.ModelProfileState, afterCancel.ModelProfileState);
        Assert.Equal(before.ModelProfileViewState, afterCancel.ModelProfileViewState);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            afterCancel.CurrentScene!,
            emptyPoint);
        snapshot = Assert.IsType<DocumentCanvasModelViewPropertiesSnapshot>(
            await host.OpenModelViewPropertiesAsync());
        var draft = new DocumentCanvasModelViewPropertiesDraft(snapshot);
        Assert.True(draft.TryGetProfile(BpmnModelProfiles.OrganizationalId, out var organization));
        Assert.True(draft.TryGetProfile(BpmnModelProfiles.StageId, out var stage));
        Assert.NotNull(organization);
        Assert.NotNull(stage);
        organization.IsAvailable = true;
        organization.IsPreferredVisible = false;
        stage.IsAvailable = true;
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: true, isDirty: true));

        var result = await host.ApplyModelViewPropertiesAsync(draft);

        Assert.True(result.Succeeded);
        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.Committed, result.Status);
        Assert.False(host.CaptureState().ModelViewPropertiesFormOpen);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.True(after.ModelProfileState.IsAvailable(BpmnModelProfiles.OrganizationalId));
        Assert.True(after.ModelProfileState.IsAvailable(BpmnModelProfiles.StageId));
        Assert.False(after.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.True(after.IsModelProfileEffectivelyVisible(BpmnModelProfiles.StageId));
        Assert.Equal(before.LayoutResult!.Nodes.AsEnumerable(),
            after.LayoutResult!.Nodes.AsEnumerable());

        var undone = await session.UndoAsync();
        Assert.True(undone.Succeeded);
        await session.WaitForIdleAsync();
        var restored = session.CaptureState();
        Assert.False(restored.ModelProfileState.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
        Assert.False(restored.ModelProfileState.IsAvailable(BpmnModelProfiles.StageId));
        Assert.False(restored.ModelProfileViewState.IsPreferredVisible(
            BpmnModelProfiles.OrganizationalId));
    }

    [Fact]
    public async Task ModelViewPropertiesPersistentOnlyApplyClosesAfterOneHistoryEntry()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var draft = new DocumentCanvasModelViewPropertiesDraft(
            await OpenModelViewPropertiesAsync(host));
        var organization = ModelProfileDraft(
            draft,
            BpmnModelProfiles.OrganizationalId);
        organization.IsAvailable = true;
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: true, isDirty: true));

        var result = await host.ApplyModelViewPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.Committed, result.Status);
        Assert.False(host.CaptureState().ModelViewPropertiesFormOpen);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.True(after.ModelProfileState.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
        Assert.Equal(before.ModelProfileViewState, after.ModelProfileViewState);
    }

    [Fact]
    public async Task ModelViewPropertiesViewOnlyApplyClosesWithoutDocumentMutation()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var initial = session.CaptureState();
        var setup = await session.ExecuteAsync(new SetModelProfileAvailabilityCommand(
            initial.DocumentId,
            initial.DocumentRevision,
            [new ModelProfileAvailabilityChange(
                BpmnModelProfiles.OrganizationalId,
                true)]));
        Assert.True(setup.IsCommitted);
        await session.WaitForIdleAsync();
        var validation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        var before = session.CaptureState();
        var draft = new DocumentCanvasModelViewPropertiesDraft(
            await OpenModelViewPropertiesAsync(host));
        var organization = ModelProfileDraft(
            draft,
            BpmnModelProfiles.OrganizationalId);
        organization.IsPreferredVisible = !organization.IsPreferredVisible;
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: true, isDirty: true));

        var result = await host.ApplyModelViewPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.Committed, result.Status);
        Assert.False(host.CaptureState().ModelViewPropertiesFormOpen);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.ModelProfileState, after.ModelProfileState);
        Assert.NotEqual(before.ModelProfileViewState, after.ModelProfileViewState);
        Assert.Same(validation, host.CaptureState().ValidationSnapshot);
        Assert.Equal(
            organization.IsPreferredVisible,
            after.ModelProfileViewState.IsPreferredVisible(
                BpmnModelProfiles.OrganizationalId));
    }

    [Fact]
    public async Task ModelViewPropertiesNoOpApplyClosesWithoutMutationOrHistory()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var draft = new DocumentCanvasModelViewPropertiesDraft(
            await OpenModelViewPropertiesAsync(host));

        var result = await host.ApplyModelViewPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.NoChange, result.Status);
        Assert.False(host.CaptureState().ModelViewPropertiesFormOpen);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.ModelProfileState, after.ModelProfileState);
        Assert.Equal(before.ModelProfileViewState, after.ModelProfileViewState);
    }

    [Fact]
    public async Task ModelViewPropertiesRejectedPersistentApplyKeepsFormAndDraft()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: new RejectingProfileAvailabilityCompositionFactory());
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var draft = new DocumentCanvasModelViewPropertiesDraft(
            await OpenModelViewPropertiesAsync(host));
        var organization = ModelProfileDraft(
            draft,
            BpmnModelProfiles.OrganizationalId);
        organization.IsAvailable = true;
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: true, isDirty: true));

        var result = await host.ApplyModelViewPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.Failed, result.Status);
        Assert.True(host.CaptureState().ModelViewPropertiesFormOpen);
        Assert.True(host.CaptureState().ModelViewPropertiesFormDirty);
        Assert.True(organization.IsAvailable);
        Assert.True(draft.IsDirty);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.ModelProfileState, after.ModelProfileState);
        Assert.Equal(before.ModelProfileViewState, after.ModelProfileViewState);
    }

    [Fact]
    public async Task ModelViewPropertiesStaleApplyKeepsFormAndDraft()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var draft = new DocumentCanvasModelViewPropertiesDraft(
            await OpenModelViewPropertiesAsync(host));
        var organization = ModelProfileDraft(
            draft,
            BpmnModelProfiles.OrganizationalId);
        organization.IsAvailable = true;
        Assert.True(host.UpdateModelViewPropertiesFormState(isOpen: true, isDirty: true));
        var beforeExternalChange = session.CaptureState();
        var external = await session.ExecuteAsync(new SetModelProfileAvailabilityCommand(
            beforeExternalChange.DocumentId,
            beforeExternalChange.DocumentRevision,
            [new ModelProfileAvailabilityChange(BpmnModelProfiles.StageId, true)]));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync();
        var beforeRejectedApply = session.CaptureState();

        var result = await host.ApplyModelViewPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.Stale, result.Status);
        Assert.True(host.CaptureState().ModelViewPropertiesFormOpen);
        Assert.True(host.CaptureState().ModelViewPropertiesFormDirty);
        Assert.True(organization.IsAvailable);
        Assert.True(draft.IsDirty);
        var after = session.CaptureState();
        Assert.Equal(beforeRejectedApply.DocumentRevision, after.DocumentRevision);
        Assert.Equal(beforeRejectedApply.HistoryStatus, after.HistoryStatus);
    }

    [Fact]
    public async Task ScopeContextActionNavigatesWithoutMutationAndClearsTransientUi()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        var toolboxSelection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: toolboxSelection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var documentBefore = AttachedDocument(session).CaptureSnapshot();
        var body = before.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var ordinaryTaskBody = before.CurrentScene.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var taskItem = new ToolboxCatalog(BpmnPluginRegistration.N82.ToolboxContributions)
            .Items.First(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        Assert.True(toolboxSelection.Select(taskItem.ItemId));
        await host.RefreshToolboxPlacementAsync();
        var validation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            before.CurrentScene,
            Center(ordinaryTaskBody.Bounds));
        Assert.Null(Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu).ScopeNavigationAction);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            before.CurrentScene,
            Center(body.Bounds));

        var menu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        var action = Assert.IsType<DocumentCanvasScopeNavigationContextAction>(
            menu.ScopeNavigationAction);
        Assert.Equal("Open SubProcess", action.Label);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, action.TargetScopeId);

        var result = await host.ExecuteScopeNavigationContextActionAsync();
        Assert.True(result?.Succeeded);
        var after = host.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, after.Session!.ActiveScopeId);
        Assert.Equal(before.DocumentRevision, after.Session.DocumentRevision);
        Assert.Equal(
            new HistoryStatus(1, canUndo: true, canRedo: false),
            after.Session.HistoryStatus);
        Assert.Equal(documentBefore, AttachedDocument(session).CaptureSnapshot());
        Assert.Empty(after.Session.EditorState.Selection);
        Assert.Null(after.ContextMenu);
        Assert.Null(after.ValidationSnapshot);
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal(
            [DocumentCanvasScopeBreadcrumb.RootLabel, "Process order"],
            after.ScopeBreadcrumb.Select(static segment => segment.Label).ToArray());
        Assert.False(after.ScopeBreadcrumb[0].IsActive);
        Assert.True(after.ScopeBreadcrumb[1].IsActive);
        Assert.Equal(before.ActiveScopeId, validation.ScopeId);

        await host.UndoAsync();
        var undone = host.CaptureState();
        Assert.Equal(before.ActiveScopeId, undone.Session!.ActiveScopeId);
        Assert.Equal(
            new HistoryStatus(1, canUndo: false, canRedo: true),
            undone.Session.HistoryStatus);
        Assert.Equal(documentBefore, AttachedDocument(session).CaptureSnapshot());

        await host.RedoAsync();
        var redone = host.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, redone.Session!.ActiveScopeId);
        Assert.Equal(
            new HistoryStatus(1, canUndo: true, canRedo: false),
            redone.Session.HistoryStatus);
        Assert.Equal(documentBefore, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task BreadcrumbNavigationClosesPropertiesAndDoubleClickRequiresNodeBody()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var rootScopeId = session.CaptureState().ActiveScopeId;
        var rootScene = session.CaptureState().CurrentScene!;
        var body = rootScene.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var marker = rootScene.Items.First(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId &&
            item.Origin.StableSourceKey?.StartsWith(
                "subprocess:marker-box:",
                StringComparison.Ordinal) == true);

        var markerPoint = marker.Transform.TransformPoint(Center(marker.Geometry.Bounds));
        var markerActivation = await InteractionController(host).PointerNodeBodyActivatedAsync(
            rootScene.ViewportTransform.TransformPoint(markerPoint));
        Assert.Null(markerActivation.TargetOrigin);
        await PointerObserver(host).DoubleClickDocumentPointAsync(rootScene, markerPoint);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        await PointerObserver(host).ClickDocumentPointAsync(rootScene, Center(body.Bounds));
        var selected = session.CaptureState();
        var handle = selected.CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:",
                StringComparison.Ordinal) == true);
        await PointerObserver(host).DoubleClickDocumentPointAsync(
            selected.CurrentScene,
            Center(handle.Bounds));
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            Center(body.Bounds));
        var properties = await host.OpenPropertiesAsync(
            BpmnDemoPipeline.ProcessOrderSubProcessVisualId);
        Assert.NotNull(properties);
        Assert.True(host.CaptureState().PropertiesFormOpen);
        var opened = await host.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(opened?.Succeeded);
        Assert.False(host.CaptureState().PropertiesFormOpen);

        var returned = await host.NavigateToScopeAsync(rootScopeId);
        Assert.True(returned?.Succeeded);
        var restoredRoot = session.CaptureState();
        var restoredBody = restoredRoot.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var activationPoint = new PointD(
            restoredBody.Bounds.Left + 10d,
            restoredBody.Bounds.Top + 10d);
        var canonicalHit = new Canvas2DSceneHitTestService().HitTest(
            restoredRoot.CurrentScene,
            activationPoint);
        Assert.Equal(restoredBody.Id, canonicalHit?.SceneObjectId);
        var directActivation = await InteractionController(host).PointerNodeBodyActivatedAsync(
            restoredRoot.CurrentScene.ViewportTransform.TransformPoint(activationPoint));
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderSubProcessVisualId,
            directActivation.TargetOrigin?.VisualStateId);
        await PointerObserver(host).DoubleClickDocumentPointAsync(
            restoredRoot.CurrentScene,
            activationPoint);

        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            session.CaptureState().ActiveScopeId);
        Assert.Equal(3, session.CaptureState().HistoryStatus.EntryCount);

        await host.UndoAsync();
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.True(session.CaptureState().HistoryStatus.CanRedo);
    }

    [Fact]
    public async Task DynamicSubProcessMarkerRemainsExcludedAfterScopeReturn()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var rootScopeId = session.CaptureState().ActiveScopeId;
        var ownerId = new SemanticElementId("test:n82:dynamic-marker:subprocess");
        var visualId = new VisualStateId("test:n82:dynamic-marker:subprocess:visual");
        var childScopeId = new DocumentScopeId("test:n82:dynamic-marker:scope");
        var before = session.CaptureState();

        var created = await session.ExecuteAsync(new CreateBpmnSubProcessCommand(
            before.DocumentId,
            before.DocumentRevision,
            ownerId,
            visualId,
            rootScopeId,
            childScopeId,
            new PointD(660d, 140d),
            new SizeD(120d, 80d),
            "DYNAMIC_MARKER",
            "Dynamic marker",
            VisualPlacementMode.Pinned,
            "Dynamic marker activation regression."));
        Assert.True(created.IsCommitted);
        await session.WaitForIdleAsync();

        Assert.True((await host.NavigateToScopeAsync(childScopeId))?.Succeeded);
        Assert.True((await host.NavigateToScopeAsync(rootScopeId))?.Succeeded);
        var restored = session.CaptureState();
        var marker = restored.CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId == visualId &&
            item.Origin.StableSourceKey?.StartsWith(
                "subprocess:marker-box:",
                StringComparison.Ordinal) == true);
        var markerPoint = marker.Transform.TransformPoint(Center(marker.Geometry.Bounds));

        var markerActivation = await InteractionController(host).PointerNodeBodyActivatedAsync(
            restored.CurrentScene.ViewportTransform.TransformPoint(markerPoint));
        Assert.Null(markerActivation.TargetOrigin);
        await PointerObserver(host).DoubleClickDocumentPointAsync(
            restored.CurrentScene,
            markerPoint);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);

        var body = session.CaptureState().CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == visualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var bodyPoint = new PointD(body.Bounds.Left + 10d, body.Bounds.Top + 10d);
        await PointerObserver(host).DoubleClickDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            bodyPoint);
        Assert.Equal(childScopeId, session.CaptureState().ActiveScopeId);
    }

    [Fact]
    public async Task ScopeNavigationCancelsGestureAndReleasesItsBrowserCapture()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var body = before.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.ProcessOrderSubProcessVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var start = new PointD(body.Bounds.Left + 10d, body.Bounds.Top + 10d);
        const long captureGeneration = 41;

        await PointerObserver(host).DownDocumentPointAsync(
            before.CurrentScene,
            start,
            pointerId: captureGeneration);
        await PointerObserver(host).MoveDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            start + new VectorD(20d, 10d),
            pointerId: captureGeneration,
            buttons: 1);
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);

        var navigated = await host.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId);

        Assert.True(navigated?.Succeeded);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            session.CaptureState().ActiveScopeId);
        Assert.Contains(
            captureGeneration,
            PointerObserver(host).ReleasedCaptureGenerations);
    }

    [Fact]
    public async Task UndoScopeNavigationCancelsToolboxPlacementBeforeReplay()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        var toolboxSelection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: toolboxSelection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        Assert.True((await host.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId))?.Succeeded);
        var parent = session.CaptureState();
        var nestedScopeId = new DocumentScopeId("test:n82:history-recovery:scope");

        var created = await session.ExecuteAsync(new CreateBpmnSubProcessCommand(
            parent.DocumentId,
            parent.DocumentRevision,
            new SemanticElementId("test:n82:history-recovery:subprocess"),
            new VisualStateId("test:n82:history-recovery:subprocess:visual"),
            parent.ActiveScopeId,
            nestedScopeId,
            new PointD(660d, 140d),
            new SizeD(120d, 80d),
            "HISTORY_RECOVERY",
            "History recovery",
            VisualPlacementMode.Pinned,
            "Undo recovery test owner."));
        Assert.True(created.IsCommitted);
        await session.WaitForIdleAsync();
        Assert.True((await host.NavigateToScopeAsync(nestedScopeId))?.Succeeded);
        var taskItem = new ToolboxCatalog(BpmnPluginRegistration.N82.ToolboxContributions)
            .Items.First(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        Assert.True(toolboxSelection.Select(taskItem.ItemId));
        await host.RefreshToolboxPlacementAsync();
        Assert.Equal(taskItem.ItemId, toolboxSelection.SelectedItemId);
        Assert.Equal("crosshair", host.CaptureState().CssCursor);

        await host.UndoAsync();

        var recovered = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, recovered.ActiveScopeId);
        Assert.Equal(EditingSessionStatus.Ready, recovered.Status);
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal("default", host.CaptureState().CssCursor);
        var beforeClick = AttachedDocument(session).CaptureSnapshot();
        await PointerObserver(host).ClickDocumentPointAsync(
            recovered.CurrentScene!,
            new PointD(1000d, 650d));
        await session.WaitForIdleAsync();
        Assert.Equal(beforeClick, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task BreadcrumbUsesLiveOwnerNameAndSupportsDirectAncestorNavigation()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var rootScopeId = session.CaptureState().ActiveScopeId;
        Assert.True((await host.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId))?.Succeeded);
        var parentState = session.CaptureState();
        var nestedOwnerId = new SemanticElementId("test:n82:nested-subprocess");
        var nestedVisualId = new VisualStateId("test:n82:nested-subprocess:visual");
        var nestedScopeId = new DocumentScopeId("test:n82:nested-subprocess:scope");

        var created = await session.ExecuteAsync(new CreateBpmnSubProcessCommand(
            parentState.DocumentId,
            parentState.DocumentRevision,
            nestedOwnerId,
            nestedVisualId,
            BpmnDemoPipeline.ProcessOrderScopeId,
            nestedScopeId,
            new PointD(660d, 140d),
            new SizeD(120d, 80d),
            "REVIEW_ORDER",
            "Review order",
            VisualPlacementMode.Pinned,
            "Nested breadcrumb test owner."));
        Assert.True(
            created.IsCommitted,
            string.Join(
                Environment.NewLine,
                created.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
        await session.WaitForIdleAsync();
        var nestedNavigation = await host.NavigateToScopeAsync(nestedScopeId);
        Assert.True(
            nestedNavigation?.Succeeded,
            string.Join(
                Environment.NewLine,
                nestedNavigation?.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}") ?? []));
        Assert.Equal(
            [DocumentCanvasScopeBreadcrumb.RootLabel, "Process order", "Review order"],
            host.CaptureState().ScopeBreadcrumb
                .Select(static segment => segment.Label)
                .ToArray());

        var nestedState = session.CaptureState();
        var renamed = await session.ExecuteAsync(new UpdateSemanticElementPropertyCommand(
            nestedState.DocumentId,
            nestedState.DocumentRevision,
            BpmnDemoPipeline.ProcessOrderSubProcessId,
            BpmnSemanticProperties.Name,
            PropertyValue.FromText("Order handling")));
        Assert.True(
            renamed.IsCommitted,
            string.Join(
                Environment.NewLine,
                renamed.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
        await session.WaitForIdleAsync();
        Assert.Equal(
            [DocumentCanvasScopeBreadcrumb.RootLabel, "Order handling", "Review order"],
            host.CaptureState().ScopeBreadcrumb
                .Select(static segment => segment.Label)
                .ToArray());

        Assert.True((await host.NavigateToScopeAsync(rootScopeId))?.Succeeded);
        Assert.Equal(
            DocumentCanvasScopeBreadcrumb.RootLabel,
            Assert.Single(host.CaptureState().ScopeBreadcrumb).Label);

        await host.UndoAsync();
        Assert.Equal(nestedScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(
            [DocumentCanvasScopeBreadcrumb.RootLabel, "Order handling", "Review order"],
            host.CaptureState().ScopeBreadcrumb
                .Select(static segment => segment.Label)
                .ToArray());

        await host.RedoAsync();
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
    }

    [Fact]
    public async Task ScopeNavigationIsolatesNoRouteAndValidationToTheActiveScope()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var rootScopeId = session.CaptureState().ActiveScopeId;
        var rootTask = document.CaptureSnapshot().VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);
        var rootTaskNode = session.CaptureState().ProjectedGraph!.Nodes.Single(node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.TaskId);
        var rootTaskBounds = session.CaptureState().LayoutResult!.Nodes.Single(node =>
            node.ProjectedObjectId == rootTaskNode.Id).Bounds;

        var moved = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            rootTask.Id,
            rootTaskBounds.TopLeft + new VectorD(90d, 45d),
            VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted);
        await session.WaitForIdleAsync();

        var rootNoRoute = session.CaptureState();
        var rootEdge = Assert.Single(rootNoRoute.ProjectedGraph!.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        Assert.Contains(rootEdge.Id, rootNoRoute.RoutingResult!.NoRouteEdgeIds);
        var rootValidation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        var rootRoutingIssue = Assert.Single(rootValidation.Issues, issue =>
            issue.Code == RoutingModelValidationIssueProvider.NoLegalRouteCode &&
            issue.Target.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        Assert.Equal(rootScopeId, rootValidation.ScopeId);

        Assert.True((await host.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId))?.Succeeded);
        var child = session.CaptureState();

        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, child.ActiveScopeId);
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Null(await host.SelectValidationIssueAsync(rootRoutingIssue.Id));
        Assert.Equal(5, child.ProjectedGraph!.Nodes.Length);
        Assert.Equal(4, child.ProjectedGraph.Edges.Length);
        Assert.All(child.ProjectedGraph.Nodes, node => Assert.Contains(
            node.Source.SemanticElementId,
            new[]
            {
                BpmnDemoPipeline.ProcessOrderStartEventId,
                BpmnDemoPipeline.ProcessOrderTaskId,
                BpmnDemoPipeline.ProcessOrderEndEventId,
                BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId,
                BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskId,
            }));
        Assert.Empty(child.RoutingResult!.NoRouteEdgeIds);
        Assert.DoesNotContain(child.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute);

        var childTaskId = new SemanticElementId("test:n82:active-scope-issue");
        var childTaskVisualId = new VisualStateId("test:n82:active-scope-issue:visual");
        var childTask = await session.ExecuteAsync(new CreateBpmnTaskCommand(
            child.DocumentId,
            child.DocumentRevision,
            childTaskId,
            childTaskVisualId,
            new PointD(700d, 300d),
            new SizeD(180d, 92d),
            "ACTIVE_SCOPE_ISSUE",
            "Active scope issue",
            121,
            VisualPlacementMode.Pinned,
            targetScopeId: BpmnDemoPipeline.ProcessOrderScopeId));
        Assert.True(childTask.IsCommitted);
        await session.WaitForIdleAsync();
        var childValidation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, childValidation.ScopeId);
        Assert.DoesNotContain(childValidation.Issues, issue =>
            issue.Code == RoutingModelValidationIssueProvider.NoLegalRouteCode);
        var activeScopeIssue = Assert.Single(childValidation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.FlowNodeIsolated &&
            issue.Target.VisualStateId == childTaskVisualId);
        var selected = await host.SelectValidationIssueAsync(activeScopeIssue.Id);
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected?.Status);
        Assert.Equal(
            childTaskVisualId,
            Assert.Single(session.CaptureState().EditorState.Selection));
        Assert.Same(childValidation, host.CaptureState().ValidationSnapshot);

        Assert.True((await host.NavigateToScopeAsync(rootScopeId))?.Succeeded);
        var restoredRoot = session.CaptureState();
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Equal(rootScopeId, restoredRoot.ActiveScopeId);
        Assert.DoesNotContain(restoredRoot.ProjectedGraph!.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ProcessOrderTaskId);
        var restoredValidation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        Assert.Equal(rootScopeId, restoredValidation.ScopeId);
        Assert.DoesNotContain(restoredValidation.Issues, issue =>
            issue.Target.VisualStateId == childTaskVisualId);
    }

    [Fact]
    public async Task InjectedAsyncCompositionFactorySelectsTheApplicationDocument()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.5d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);

        await host.InitializeAsync("canvas", "container");

        var state = host.CaptureState();
        var document = AttachedDocument(Session(host));
        Assert.True(state.IsInitialized);
        Assert.Equal(EditingSessionStatus.Ready, state.Session?.Status);
        Assert.Equal(BpmnDemoPipeline.DemoDocumentId, document.DocumentId);
        Assert.Equal(new DocumentRevision(103), document.Revision);
        Assert.Equal(26, document.SemanticModel.ElementCount);
        Assert.Equal(25, document.SemanticModel.RelationshipCount);
        Assert.Null(state.PipelineCounters);
        Assert.NotNull(state.Session?.CurrentScene);
    }

    [Fact]
    public async Task ExplicitValidationIsReadOnlyAndViewportChangesPreserveItsSnapshot()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var beforeState = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();
        var beforeCounters = (
            Counters(host).ProjectionRuleInvocationCount,
            Counters(host).LayoutInvocationCount,
            Counters(host).RoutingInvocationCount,
            Counters(host).SceneContributionInvocationCount);

        var validation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());

        Assert.Empty(validation.Issues);
        Assert.Equal(beforeState.DocumentRevision, validation.SourceRevision);
        Assert.Equal(beforeState.Generation.Value, validation.SourceGeneration);
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(beforeState.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(beforeState.EditorState.Selection, session.CaptureState().EditorState.Selection);
        Assert.Equal(beforeCounters, (
            Counters(host).ProjectionRuleInvocationCount,
            Counters(host).LayoutInvocationCount,
            Counters(host).RoutingInvocationCount,
            Counters(host).SceneContributionInvocationCount));

        await host.ZoomInAsync();
        await session.PanViewportAsync(new VectorD(40d, -20d));
        Assert.Same(validation, host.CaptureState().ValidationSnapshot);
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
    }

    [Fact]
    public async Task ValidationIssueSelectionIsTransientAndDocumentMutationInvalidatesResults()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();
        var validation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        var issue = validation.Issues.First(item => item.Target.VisualStateId is not null);

        var selectionResult = await host.SelectValidationIssueAsync(issue.Id);
        Assert.Equal(Canvas2DInteractionStatus.Updated, selectionResult?.Status);
        await session.WaitForIdleAsync();
        var selected = session.CaptureState();
        Assert.Equal(issue.Target.VisualStateId, Assert.Single(selected.EditorState.Selection));
        Assert.Equal(before.DocumentRevision, selected.DocumentRevision);
        Assert.Equal(before.HistoryStatus, selected.HistoryStatus);
        Assert.Equal(before.EditorState.Viewport, selected.EditorState.Viewport);
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Same(validation, host.CaptureState().ValidationSnapshot);

        var visual = beforeDocument.VisualModel.VisualStates.First(item =>
            item.SemanticElementId == BpmnDemoPipeline.TaskId);
        var committed = await session.ExecuteAsync(new MoveVisualStateCommand(
            beforeDocument.DocumentId,
            beforeDocument.Revision,
            visual.Id,
            visual.Position + new VectorD(1d, 1d),
            VisualPlacementMode.Pinned));
        Assert.True(committed.IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Null(host.CaptureState().ValidationSnapshot);

        var refreshed = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        var rejected = await session.ExecuteAsync(new MoveVisualStateCommand(
            beforeDocument.DocumentId,
            beforeDocument.Revision,
            visual.Id,
            visual.Position,
            VisualPlacementMode.Pinned));
        Assert.False(rejected.IsCommitted);
        Assert.Same(refreshed, host.CaptureState().ValidationSnapshot);
    }

    [Fact]
    public async Task RegisteredToolboxPlacementCreatesOnePinnedNodeThenSelectsItSceneOnly()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1.25d));
        var toolboxSelection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: toolboxSelection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = host.CaptureState().Session!;
        var beforeDocument = document.CaptureSnapshot();
        var renderCount = execution.Calls.Count(call => call == "render");
        var taskItem = new ToolboxCatalog(BpmnPluginRegistration.N1.ToolboxContributions)
            .Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.Task);

        Assert.True(toolboxSelection.Select(taskItem.ItemId));
        await host.RefreshToolboxPlacementAsync();

        var activated = host.CaptureState();
        Assert.Equal("crosshair", activated.CssCursor);
        Assert.Equal(before.DocumentRevision, activated.Session!.DocumentRevision);
        Assert.Equal(before.HistoryStatus, activated.Session.HistoryStatus);
        Assert.Equal(renderCount, execution.Calls.Count(call => call == "render"));

        var clicked = new PointD(650d, 350d);
        await PointerObserver(host).ClickDocumentPointAsync(before.CurrentScene!, clicked);
        await session.WaitForIdleAsync();
        var after = host.CaptureState().Session!;
        var afterDocument = document.CaptureSnapshot();
        var createdTask = Assert.Single(
            afterDocument.SemanticModel.Elements
                .Where(item => item.TypeId == BpmnSemanticTypes.Task &&
                    !beforeDocument.SemanticModel.Elements.Any(beforeItem => beforeItem.Id == item.Id)));
        var createdVisual = Assert.Single(
            afterDocument.VisualModel.VisualStates,
            item => item.SemanticElementId == createdTask.Id);

        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.Equal(new PointD(590d, 310d), createdVisual.Position);
        Assert.Equal(new SizeD(120d, 80d), createdVisual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, createdVisual.PlacementMode);
        Assert.Empty(createdVisual.ConnectorAnchors);
        Assert.True(beforeDocument.SemanticModel.Relationships.SequenceEqual(
            afterDocument.SemanticModel.Relationships));
        Assert.Equal(createdVisual.Id, Assert.Single(after.EditorState.Selection));
        Assert.Null(toolboxSelection.SelectedItemId);
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Equal(renderCount + 2, execution.Calls.Count(call => call == "render"));

        await PointerObserver(host).ClickDocumentPointAsync(after.CurrentScene!, new PointD(700d, 400d));
        await session.WaitForIdleAsync();
        Assert.Equal(after.DocumentRevision, host.CaptureState().Session!.DocumentRevision);
        Assert.Equal(after.HistoryStatus, host.CaptureState().Session!.HistoryStatus);
    }

    [Fact]
    public async Task BpmnNeutralViewportKnownCanvasPlacementKeepsDocumentGeometryStable()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1.25d));
        var toolboxSelection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: toolboxSelection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var initial = session.CaptureState();
        var initialScene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
            initial.CurrentScene);
        var before = document.CaptureSnapshot();
        var messageItem = new ToolboxCatalog(BpmnPluginRegistration.N4.ToolboxContributions)
            .Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.MessageCatchEvent);

        Assert.Equal(
            new ViewportSnapshot(1d, default, new RectD(0d, 0d, 900d, 600d)),
            initial.EditorState.Viewport);
        Assert.Equal(Matrix2D.Identity, initialScene.ViewportTransform);
        Assert.Equal(
            new PointD(0d, 0d),
            Canvas2DRenderer.ConvertCssToDocument(initialScene, new PointD(0d, 0d)));
        Assert.True(toolboxSelection.Select(messageItem.ItemId));
        await host.RefreshToolboxPlacementAsync();

        await PointerObserver(host).ClickCssPointAsync(new PointD(850d, 570d));
        await session.WaitForIdleAsync();
        var afterPlacement = document.CaptureSnapshot();
        var createdElement = Assert.Single(afterPlacement.SemanticModel.Elements.Where(element =>
            element.TypeId == BpmnSemanticTypes.MessageCatchEvent &&
            !before.SemanticModel.Elements.Any(candidate => candidate.Id == element.Id)));
        var createdVisual = Assert.Single(afterPlacement.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == createdElement.Id);
        var expectedBounds = new RectD(832d, 552d, 36d, 36d);

        var expectedPosition = new PointD(expectedBounds.X, expectedBounds.Y);
        Assert.Equal(expectedPosition, createdVisual.Position);
        Assert.Equal(expectedBounds.Size, createdVisual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, createdVisual.PlacementMode);
        var placedState = session.CaptureState();
        Assert.True(
            placedState.CurrentScene is not null,
            string.Join(Environment.NewLine, placedState.RuntimeDiagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var placedScene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
            placedState.CurrentScene);
        var body = Assert.Single(placedScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == createdVisual.Id &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.ProjectedRuntimeObject));
        Assert.Equal(expectedBounds, body.Bounds);
        Assert.Equal(new PointD(850d, 570d), Center(body.Bounds));

        Assert.Null(await host.OpenPropertiesAsync(createdVisual.Id));
        Assert.False(host.TryCaptureSelectedProperties(createdVisual.Id, out var properties));
        Assert.NotNull(properties);
        Assert.Equal(expectedBounds, properties.Bounds);
        Assert.Null(await host.OpenPropertiesAsync(createdVisual.Id));
        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(expectedBounds, body.Bounds);

        var panned = await session.UpdateViewportAsync(
            new ViewportSnapshot(1d, new VectorD(50d, 30d)));
        Assert.True(panned.Succeeded);
        var pannedScene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
            session.CaptureState().CurrentScene);
        Assert.Equal(
            new PointD(882d, 582d),
            pannedScene.ViewportTransform.TransformPoint(expectedPosition));
        Assert.Equal(createdVisual, document.CaptureSnapshot().VisualModel.VisualStates.Single(
            visual => visual.Id == createdVisual.Id));

        var zoomed = await session.UpdateViewportAsync(new ViewportSnapshot(2d, default));
        Assert.True(zoomed.Succeeded);
        var zoomedScene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
            session.CaptureState().CurrentScene);
        Assert.Equal(
            new PointD(1664d, 1104d),
            zoomedScene.ViewportTransform.TransformPoint(expectedPosition));
        Assert.Equal(createdVisual, document.CaptureSnapshot().VisualModel.VisualStates.Single(
            visual => visual.Id == createdVisual.Id));

        await surface.RaiseAsync(new Canvas2DSurfaceSize(1200d, 720d, 2d));
        await session.WaitForIdleAsync();
        Assert.Equal(
            new ViewportSnapshot(
                2d,
                default,
                new RectD(0d, 0d, 600d, 360d)),
            session.CaptureState().EditorState.Viewport);
        Assert.Equal(createdVisual, document.CaptureSnapshot().VisualModel.VisualStates.Single(
            visual => visual.Id == createdVisual.Id));
    }

    [Fact]
    public async Task SwitchingRegisteredToolsAndClickingOverAnObjectUsesTheLatestToolFirst()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var catalog = new ToolboxCatalog(BpmnPluginRegistration.N1.ToolboxContributions);
        var task = catalog.Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        var inclusive = catalog.Items.Single(
            item => item.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        var before = document.CaptureSnapshot();
        var state = host.CaptureState().Session!;
        var existingTask = state.CurrentScene!.Items.First(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.TaskId);

        Assert.True(selection.Select(task.ItemId));
        await host.RefreshToolboxPlacementAsync();
        Assert.True(selection.Select(inclusive.ItemId));
        await host.RefreshToolboxPlacementAsync();

        Assert.Equal(before, document.CaptureSnapshot());
        await PointerObserver(host).ClickDocumentPointAsync(
            state.CurrentScene,
            Center(existingTask.Bounds));
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var created = Assert.Single(after.SemanticModel.Elements.Where(item =>
            item.TypeId == BpmnSemanticTypes.InclusiveGateway &&
            !before.SemanticModel.Elements.Any(old => old.Id == item.Id)));
        var visual = Assert.Single(after.VisualModel.VisualStates,
            item => item.SemanticElementId == created.Id);

        Assert.Equal(
            before.SemanticModel.Elements.Count(item => item.TypeId == BpmnSemanticTypes.Task),
            after.SemanticModel.Elements.Count(item => item.TypeId == BpmnSemanticTypes.Task));
        Assert.Equal(visual.Id, Assert.Single(host.CaptureState().Session!.EditorState.Selection));
        Assert.Null(selection.SelectedItemId);
    }

    [Fact]
    public async Task ContextClickDoesNotPlaceAndCancelsTheActivePlacementTool()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 2d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var task = new ToolboxCatalog(BpmnPluginRegistration.N1.ToolboxContributions)
            .Items.Single(item => item.ElementTypeId == BpmnSemanticTypes.Task);
        var before = document.CaptureSnapshot();
        var scene = session.CaptureState().CurrentScene!;

        Assert.True(selection.Select(task.ItemId));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).DownDocumentPointAsync(
            scene,
            new PointD(650d, 350d),
            pointerId: 17,
            isPrimary: false);
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, new PointD(650d, 350d));

        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Null(selection.SelectedItemId);
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.NotNull(host.CaptureState().ContextMenu);

        await host.CancelToolboxPlacementAsync();

        Assert.Null(selection.SelectedItemId);
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Equal(before, document.CaptureSnapshot());
    }

    [Fact]
    public async Task EmptyPlacementCatalogPreservesHistoricalSelectionOnlyBehavior()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(execution, surface, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var item = NeutralDemoToolbox.Catalog.Items[0];

        Assert.True(selection.Select(item.ItemId));
        await host.RefreshToolboxPlacementAsync();
        Assert.Equal("default", host.CaptureState().CssCursor);

        await PointerObserver(host).ClickDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            new PointD(5d, 5d));
        await session.WaitForIdleAsync();

        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(item.ItemId, selection.SelectedItemId);

        await host.CancelToolboxPlacementAsync();

        Assert.Equal(item.ItemId, selection.SelectedItemId);
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.Equal(before, document.CaptureSnapshot());
    }

    [Fact]
    public async Task FailedFactoryRetainsToolAndPreviousSelectionWithoutClickFallthrough()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var selection = new ToolboxSelectionState();
        var compositionFactory = new PlacementOverrideCompositionFactory(
            _ => new FailingPlacementFactory());
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: compositionFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var scene = session.CaptureState().CurrentScene!;
        var firstNode = scene.Items.First(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId is not null);
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(firstNode.Bounds));
        await session.WaitForIdleAsync();
        var selectedBefore = Assert.Single(session.CaptureState().EditorState.Selection);
        var before = document.CaptureSnapshot();
        var itemId = PlacementOverrideCompositionFactory.TaskItemId;

        Assert.True(selection.Select(itemId));
        await host.RefreshToolboxPlacementAsync();
        var currentScene = session.CaptureState().CurrentScene!;
        var underlying = currentScene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId is not null &&
            item.Origin.VisualStateId != selectedBefore);
        await PointerObserver(host).ClickDocumentPointAsync(currentScene, Center(underlying.Bounds));

        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(selectedBefore, Assert.Single(session.CaptureState().EditorState.Selection));
        Assert.Equal(itemId, selection.SelectedItemId);
        Assert.Equal("crosshair", host.CaptureState().CssCursor);
        Assert.Contains(host.CaptureState().InteractionDiagnostics,
            diagnostic => diagnostic.Code == "TEST_TOOLBOX_PLACEMENT_FAILED");
    }

    [Fact]
    public async Task CandidateHoverPublishesAndClearsPreviewWhileInvalidPlacementStaysArmed()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var selection = new ToolboxSelectionState();
        var candidateProvider = new RecordingCandidateProvider(request =>
        {
            var target = request.VisibleTargets.FirstOrDefault(candidate =>
                candidate.Bounds.Contains(request.DocumentPoint));
            return target is null
                ? null
                : new ToolboxPlacementCandidate(
                    target,
                    "test:toolbox-placement-preview",
                    target.Bounds,
                    [new("test:candidate", PropertyValue.FromBoolean(true))],
                    EditorFeedbackPresentationMode.ContributorOnly);
        });
        var candidateRequiredFactory = new CandidateRequiredPlacementFactory(
            PlacementOverrideCompositionFactory.RealTaskFactory);
        var compositionFactory = new PlacementOverrideCompositionFactory(
            _ => candidateRequiredFactory,
            _ => candidateProvider);
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: compositionFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var ready = session.CaptureState();
        var beforeDocument = document.CaptureSnapshot();
        var beforeRevision = beforeDocument.Revision;
        var beforeHistory = ready.HistoryStatus;
        var hoverTarget = ready.LayoutResult!.Nodes.First(static node =>
            !node.Bounds.IsEmpty);

        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).MoveDocumentPointAsync(
            ready.CurrentScene!,
            Center(hoverTarget.Bounds));

        var hovered = session.CaptureState();
        var feedback = Assert.Single(
            hovered.EditorState.TemporaryFeedback,
            static item => item.Id == "inceptus:toolbox-placement-candidate");
        Assert.Equal("test:toolbox-placement-preview", feedback.Kind);
        Assert.Equal(hoverTarget.Bounds, feedback.Bounds);
        Assert.True(feedback.Properties["test:candidate"].BooleanValue);
        Assert.Equal(
            EditorFeedbackPresentationMode.ContributorOnly,
            feedback.PresentationMode);
        Assert.DoesNotContain(hovered.CurrentScene!.Items, static item =>
            item.Origin.StableSourceKey ==
                "feedback:inceptus:toolbox-placement-candidate");
        var hoverRequest = Assert.Single(candidateProvider.Requests);
        Assert.Equal(ready.ActiveScopeId, hoverRequest.TargetScopeId);
        Assert.NotEmpty(hoverRequest.VisibleTargets);
        Assert.Equal(
            hoverRequest.VisibleTargets.OrderBy(static target =>
                target.VisualStateId.Value,
                StringComparer.Ordinal),
            hoverRequest.VisibleTargets);
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(beforeRevision, hovered.DocumentRevision);
        Assert.Equal(beforeRevision, document.CaptureSnapshot().Revision);
        Assert.Equal(beforeHistory, hovered.HistoryStatus);

        await PointerObserver(host).LeaveAsync();

        Assert.DoesNotContain(
            session.CaptureState().EditorState.TemporaryFeedback,
            static item => item.Id == "inceptus:toolbox-placement-candidate");
        Assert.DoesNotContain(
            session.CaptureState().CurrentScene!.Items,
            static item => item.Origin.StableSourceKey?.Contains(
                "inceptus:toolbox-placement-candidate",
                StringComparison.Ordinal) == true);

        await PointerObserver(host).DownDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            new PointD(5000d, 5000d));

        var invalid = session.CaptureState();
        Assert.Equal(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(beforeHistory, invalid.HistoryStatus);
        Assert.Equal(
            PlacementOverrideCompositionFactory.TaskItemId,
            selection.SelectedItemId);
        Assert.Equal("crosshair", host.CaptureState().CssCursor);
        Assert.NotNull(candidateRequiredFactory.LastRequest);
        Assert.Null(candidateRequiredFactory.LastRequest!.Candidate);
        Assert.DoesNotContain(
            invalid.EditorState.TemporaryFeedback,
            static item => item.Id == "inceptus:toolbox-placement-candidate");
        Assert.Contains(
            host.CaptureState().InteractionDiagnostics,
            static diagnostic => diagnostic.Code == "TEST_TOOLBOX_CANDIDATE_REQUIRED");
    }

    [Fact]
    public async Task RevisionChangedInsideFactoryRejectsPlacementAsStale()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var selection = new ToolboxSelectionState();
        var compositionFactory = new PlacementOverrideCompositionFactory(source =>
            new RevisionAdvancingPlacementFactory(
                PlacementOverrideCompositionFactory.RealTaskFactory,
                source.Document));
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: compositionFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();

        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).DownDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            new PointD(650d, 350d));

        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(
            before.SemanticModel.Elements.Count(item => item.TypeId == BpmnSemanticTypes.Task),
            after.SemanticModel.Elements.Count(item => item.TypeId == BpmnSemanticTypes.Task));
        Assert.Equal(PlacementOverrideCompositionFactory.TaskItemId, selection.SelectedItemId);
        Assert.Equal("crosshair", host.CaptureState().CssCursor);
        Assert.Contains(host.CaptureState().InteractionDiagnostics,
            diagnostic => diagnostic.Code == "TOOLBOX_PLACEMENT_STALE");
    }

    [Fact]
    public async Task RuntimeFaultedSessionRejectsPlacementAndRetainsTool()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var selection = new ToolboxSelectionState();
        var primaryRuntimeDiagnostic = new Diagnostic(
            "TEST_PRIMARY_RUNTIME_FAULT",
            DiagnosticSeverity.Error,
            "The authoritative runtime pipeline is faulted.");
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var ready = session.CaptureState();
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        SetSessionField(session, "_lastKnownGoodScene", ready.CurrentScene);
        SetSessionField(session, "_currentScene", null);
        SetSessionField(session, "_artifacts", null);
        SetSessionField(session, "_status", EditingSessionStatus.RuntimeFaulted);
        SetSessionField(
            session,
            "_runtimeDiagnostics",
            ImmutableArray.Create(primaryRuntimeDiagnostic));

        Assert.Null(await host.ValidateAsync());
        Assert.Null(host.CaptureState().ValidationSnapshot);

        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).DownDocumentPointAsync(
            ready.CurrentScene!,
            new PointD(650d, 350d));

        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, session.CaptureState().Status);
        Assert.Equal(PlacementOverrideCompositionFactory.TaskItemId, selection.SelectedItemId);
        Assert.Equal("crosshair", host.CaptureState().CssCursor);
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Equal(
            primaryRuntimeDiagnostic,
            Assert.Single(host.CaptureState().Session!.RuntimeDiagnostics));
    }

    [Fact]
    public async Task BpmnNoRouteStaysReadyShowsSelectableFallbackAndUndoRedoRecovers()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var ready = session.CaptureState();
        var before = document.CaptureSnapshot();
        var beforeHistory = ready.HistoryStatus;
        var beforeRelationship = Assert.Single(before.SemanticModel.Relationships, relationship =>
            relationship.Id == BpmnDemoPipeline.SecondSequenceFlowId);
        var beforeConnector = Assert.Single(before.VisualModel.VisualStates, visual =>
            visual.Id == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        var beforeGraph = Assert.IsType<ProjectedGraph>(ready.ProjectedGraph);
        var beforeLayout = Assert.IsType<LayoutResult>(ready.LayoutResult);
        var reviewNode = Assert.Single(beforeGraph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.TaskId);
        var reviewBounds = Assert.Single(beforeLayout.Nodes, node =>
            node.ProjectedObjectId == reviewNode.Id).Bounds;
        var flowEdge = Assert.Single(beforeGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var beforeRoute = Assert.Single(
            Assert.IsType<RoutingResult>(ready.RoutingResult).Routes,
            route => route.ProjectedEdgeId == flowEdge.Id);
        var pointer = PointerObserver(host);

        Assert.Equal(new RectD(122d, 76d, 180d, 92d), reviewBounds);
        Assert.Equal(
            new[] { new PointD(302d, 122d), new PointD(382d, 122d) },
            beforeRoute.Path.AsEnumerable());
        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            before.DocumentId,
            before.Revision,
            BpmnDemoPipeline.TaskVisualId,
            reviewBounds.TopLeft + new VectorD(90d, 45d),
            VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        await session.WaitForIdleAsync();

        var noRouteHost = host.CaptureState();
        var noRoute = Assert.IsType<EditingSessionState>(noRouteHost.Session);
        var noRouteScene = Assert.IsType<Canvas2DScene>(noRoute.CurrentScene);
        var noRouteDocument = document.CaptureSnapshot();
        var noRouteGraph = Assert.IsType<ProjectedGraph>(noRoute.ProjectedGraph);
        var noRouteEdge = Assert.Single(noRouteGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var noRouteRouting = Assert.IsType<RoutingResult>(noRoute.RoutingResult);

        Assert.Equal(EditingSessionStatus.Ready, noRoute.Status);
        Assert.Null(noRoute.LastKnownGoodScene);
        Assert.False(noRoute.IsDisplayingStaleScene);
        Assert.True(noRoute.IsGraphicalInteractionEnabled);
        Assert.Equal(noRoute.DocumentRevision, noRouteScene.SourceRevision);
        Assert.Contains(noRouteEdge.Id, noRouteRouting.NoRouteEdgeIds);
        Assert.DoesNotContain(noRouteRouting.Routes, route =>
            route.ProjectedEdgeId == noRouteEdge.Id);
        var fallbackId = Canvas2DSceneObjectIdentity.ForProjected(noRouteEdge.Id, "connector");
        var fallback = Assert.Single(noRouteScene.Items, item => item.Id == fallbackId);
        var expectedFallbackPath = new[]
        {
            new PointD(392d, 167d),
            new PointD(382d, 122d),
        };
        Assert.Equal(Canvas2DSceneLayer.Connector, fallback.Layer);
        Assert.Equal(BpmnDemoPipeline.SecondSequenceFlowId, fallback.Origin.SemanticElementId);
        Assert.Equal(
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            fallback.Origin.VisualStateId);
        Assert.Equal(noRouteEdge.Id, fallback.Origin.ProjectedObjectId);
        Assert.Equal(
            expectedFallbackPath,
            Canvas2DConnectorPathMetadata.Resolve(fallback)
                .Select(fallback.Transform.TransformPoint));
        var fallbackArrow = Assert.Single(noRouteScene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                noRouteEdge.Id,
                "connector-target-arrow"));
        Assert.Equal(expectedFallbackPath[^1],
            fallbackArrow.Transform.TransformPoint(fallbackArrow.Geometry.Points[0]));
        var fallbackMidpoint = new PointD(
            (expectedFallbackPath[0].X + expectedFallbackPath[1].X) / 2d,
            (expectedFallbackPath[0].Y + expectedFallbackPath[1].Y) / 2d);
        Assert.Equal(
            fallbackId,
            new Canvas2DSceneHitTestService().HitTest(
                noRouteScene,
                fallbackMidpoint)?.SceneObjectId);
        Assert.Contains(noRouteScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId != BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Contains(noRoute.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.SourceIdentity == noRouteEdge.Id.Value);
        Assert.Empty(noRouteHost.InteractionDiagnostics);
        Assert.Equal(before.Revision.Increment(), noRoute.DocumentRevision);
        Assert.Equal(before.Revision.Increment(), noRouteDocument.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, noRoute.HistoryStatus.EntryCount);
        Assert.True(noRoute.HistoryStatus.CanUndo);
        Assert.Equal(beforeRelationship, Assert.Single(
            noRouteDocument.SemanticModel.Relationships,
            relationship => relationship.Id == beforeRelationship.Id));
        Assert.Equal(beforeConnector, Assert.Single(
            noRouteDocument.VisualModel.VisualStates,
            visual => visual.Id == beforeConnector.Id));
        var movedReview = Assert.Single(noRouteDocument.VisualModel.VisualStates, visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);
        Assert.Equal(new PointD(212d, 121d), movedReview.Position);
        Assert.Equal(VisualPlacementMode.Pinned, movedReview.PlacementMode);

        var validation = Assert.IsType<ValidationSnapshot>(await host.ValidateAsync());
        var routingIssue = Assert.Single(validation.Issues, issue =>
            issue.Code == RoutingModelValidationIssueProvider.NoLegalRouteCode &&
            issue.Target.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        Assert.Equal(ModelValidationSeverity.Warning, routingIssue.Severity);
        Assert.Equal(BpmnDemoPipeline.SecondSequenceFlowVisualId,
            routingIssue.Target.VisualStateId);
        var selectedByIssue = await host.SelectValidationIssueAsync(routingIssue.Id);
        Assert.Equal(Canvas2DInteractionStatus.Updated, selectedByIssue?.Status);
        await session.WaitForIdleAsync();
        Assert.Equal(
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            Assert.Single(session.CaptureState().EditorState.Selection));
        Assert.Equal(noRoute.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(noRoute.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Same(validation, host.CaptureState().ValidationSnapshot);

        await pointer.ClickDocumentPointAsync(noRouteScene, fallbackMidpoint);
        var selectedFallback = session.CaptureState();
        Assert.Equal(
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            Assert.Single(selectedFallback.EditorState.Selection));
        var endpointHandles = selectedFallback.CurrentScene!.Items.Where(item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.SecondSequenceFlowVisualId &&
            item.Metadata.ContainsKey(Canvas2DConnectorEndpointMetadata.HandleRole)).ToArray();
        Assert.Equal(2, endpointHandles.Length);
        Assert.Equal(
            expectedFallbackPath,
            endpointHandles
                .OrderBy(item => StringComparer.Ordinal.Equals(
                    item.Metadata[Canvas2DConnectorEndpointMetadata.HandleRole].TextValue,
                    Canvas2DConnectorEndpointMetadata.StartEndpointRole)
                        ? 0
                        : 1)
                .Select(item => Center(item.Bounds)));

        await pointer.ContextMenuDocumentPointAsync(
            selectedFallback.CurrentScene,
            fallbackMidpoint);
        var fallbackMenu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.Equal(
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            fallbackMenu.TargetVisualStateId);
        Assert.Equal(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            fallbackMenu.ConnectorRouteAction?.Kind);
        Assert.Equal(
            DiagramDeletionTargetKind.Connection,
            fallbackMenu.DeletionAction?.TargetKind);

        var addedFallbackPoint = await host.ExecuteConnectorRouteContextActionAsync();
        Assert.True(addedFallbackPoint?.IsCommitted);
        Assert.Equal(
            UpdateConnectionRouteCommand.KnownTypeId,
            addedFallbackPoint?.CommandTypeId);
        await session.WaitForIdleAsync();

        var afterFallbackAdd = session.CaptureState();
        var afterFallbackAddDocument = document.CaptureSnapshot();
        var afterFallbackAddVisual = Assert.Single(
            afterFallbackAddDocument.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Equal(3, afterFallbackAddVisual.Route.Length);
        Assert.Equal(expectedFallbackPath[0], afterFallbackAddVisual.Route[0]);
        Assert.Equal(fallbackMidpoint.X, afterFallbackAddVisual.Route[1].X, 10);
        Assert.Equal(fallbackMidpoint.Y, afterFallbackAddVisual.Route[1].Y, 10);
        Assert.Equal(expectedFallbackPath[^1], afterFallbackAddVisual.Route[^1]);
        Assert.Equal(beforeHistory.EntryCount + 2, afterFallbackAdd.HistoryStatus.EntryCount);
        Assert.Equal(
            noRouteEdge.Id,
            Assert.Single(afterFallbackAdd.RoutingResult!.NoRouteEdgeIds));
        Assert.DoesNotContain(afterFallbackAdd.RoutingResult.Routes, route =>
            route.ProjectedEdgeId == noRouteEdge.Id);
        var afterFallbackAddConnector = Assert.Single(
            afterFallbackAdd.CurrentScene!.Items,
            item => item.Id == fallbackId);
        Assert.Equal(
            expectedFallbackPath,
            Canvas2DConnectorPathMetadata.Resolve(afterFallbackAddConnector)
                .Select(afterFallbackAddConnector.Transform.TransformPoint));
        Assert.Equal(
            new[]
            {
                expectedFallbackPath[0],
                fallbackMidpoint,
                expectedFallbackPath[^1],
            },
            Canvas2DConnectorPathMetadata.ResolveEditable(afterFallbackAddConnector)
                .Select(afterFallbackAddConnector.Transform.TransformPoint));
        var authoredWaypointHandle = Assert.Single(afterFallbackAdd.CurrentScene.Items, item =>
            item.Origin.VisualStateId == BpmnDemoPipeline.SecondSequenceFlowVisualId &&
            item.Metadata.TryGetValue(Canvas2DRouteGestureMetadata.HandleRole, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(
                role.TextValue,
                Canvas2DRouteGestureMetadata.BendRole));
        Assert.Equal(fallbackMidpoint, Center(authoredWaypointHandle.Bounds));

        await host.UndoAsync();
        await session.WaitForIdleAsync();

        var undoneFallbackAdd = session.CaptureState();
        var undoneFallbackAddDocument = document.CaptureSnapshot();
        var undoneFallbackAddVisual = Assert.Single(
            undoneFallbackAddDocument.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Empty(undoneFallbackAddVisual.Route);
        Assert.Equal(beforeHistory.EntryCount + 2, undoneFallbackAdd.HistoryStatus.EntryCount);
        Assert.True(undoneFallbackAdd.HistoryStatus.CanRedo);
        Assert.Contains(noRouteEdge.Id, undoneFallbackAdd.RoutingResult!.NoRouteEdgeIds);
        Assert.Contains(undoneFallbackAdd.CurrentScene!.Items, item => item.Id == fallbackId);

        Assert.Null(await host.OpenPropertiesAsync(BpmnDemoPipeline.SecondSequenceFlowVisualId));
        Assert.False(host.TryCaptureSelectedProperties(BpmnDemoPipeline.SecondSequenceFlowVisualId,
            out var fallbackProperties));
        Assert.NotNull(fallbackProperties);
        Assert.True(fallbackProperties.IsConnector);
        Assert.Empty(fallbackProperties.DataFields);
        Assert.False(host.CaptureState().PropertiesFormOpen);

        var interactionScene = session.CaptureState().CurrentScene!;
        var interactiveNode = Assert.Single(interactionScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.NotifyCustomerTaskVisualId);
        await pointer.ClickDocumentPointAsync(
            interactionScene,
            Center(interactiveNode.Bounds));
        var afterInput = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, afterInput.Status);
        Assert.Contains(BpmnDemoPipeline.NotifyCustomerTaskVisualId,
            afterInput.EditorState.Selection);
        Assert.Equal(undoneFallbackAddDocument, document.CaptureSnapshot());
        Assert.Equal(undoneFallbackAdd.HistoryStatus, afterInput.HistoryStatus);
        var afterInputHost = host.CaptureState();
        Assert.Contains(afterInputHost.InteractionDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.SourceIdentity == noRouteEdge.Id.Value);
        Assert.DoesNotContain(afterInputHost.InteractionDiagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);

        await host.UndoAsync();
        await session.WaitForIdleAsync();

        var recoveredHost = host.CaptureState();
        var recovered = Assert.IsType<EditingSessionState>(recoveredHost.Session);
        var recoveredDocument = document.CaptureSnapshot();
        Assert.Equal(EditingSessionStatus.Ready, recovered.Status);
        Assert.NotNull(recovered.CurrentScene);
        Assert.Null(recovered.LastKnownGoodScene);
        Assert.True(recovered.IsGraphicalInteractionEnabled);
        Assert.Empty(recovered.RuntimeDiagnostics);
        Assert.Empty(recoveredHost.InteractionDiagnostics);
        Assert.Equal(undoneFallbackAdd.DocumentRevision.Increment(), recovered.DocumentRevision);
        Assert.True(before.SemanticModel.Elements.SequenceEqual(
            recoveredDocument.SemanticModel.Elements));
        Assert.True(before.SemanticModel.Relationships.SequenceEqual(
            recoveredDocument.SemanticModel.Relationships));
        Assert.True(before.VisualModel.VisualStates.SequenceEqual(
            recoveredDocument.VisualModel.VisualStates));
        Assert.False(recovered.HistoryStatus.CanUndo);
        Assert.True(recovered.HistoryStatus.CanRedo);
        Assert.Equal(beforeHistory.EntryCount + 2, recovered.HistoryStatus.EntryCount);
        var recoveredGraph = Assert.IsType<ProjectedGraph>(recovered.ProjectedGraph);
        var recoveredEdge = Assert.Single(recoveredGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var recoveredRouting = Assert.IsType<RoutingResult>(recovered.RoutingResult);
        Assert.DoesNotContain(recoveredEdge.Id, recoveredRouting.NoRouteEdgeIds);
        Assert.Equal(beforeRoute.Path.AsEnumerable(), Assert.Single(
            recoveredRouting.Routes,
            route => route.ProjectedEdgeId == recoveredEdge.Id).Path.AsEnumerable());
        Assert.Contains(recovered.CurrentScene!.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == BpmnDemoPipeline.SecondSequenceFlowVisualId);

        await host.RedoAsync();
        await session.WaitForIdleAsync();

        var redone = session.CaptureState();
        var redoneGraph = Assert.IsType<ProjectedGraph>(redone.ProjectedGraph);
        var redoneEdge = Assert.Single(redoneGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        Assert.Equal(EditingSessionStatus.Ready, redone.Status);
        Assert.NotNull(redone.CurrentScene);
        Assert.Contains(redoneEdge.Id,
            Assert.IsType<RoutingResult>(redone.RoutingResult).NoRouteEdgeIds);
        var redoneFallback = Assert.Single(redone.CurrentScene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                redoneEdge.Id,
                "connector"));
        Assert.Equal(
            expectedFallbackPath,
            Canvas2DConnectorPathMetadata.Resolve(redoneFallback)
                .Select(redoneFallback.Transform.TransformPoint));
        Assert.Contains(redone.CurrentScene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                redoneEdge.Id,
                "connector-target-arrow"));
        Assert.Equal(beforeHistory.EntryCount + 2, redone.HistoryStatus.EntryCount);
        Assert.True(redone.HistoryStatus.CanUndo);
        Assert.True(redone.HistoryStatus.CanRedo);

        await host.RedoAsync();
        await session.WaitForIdleAsync();

        var redoneFallbackAdd = session.CaptureState();
        var redoneFallbackAddVisual = Assert.Single(
            document.CaptureSnapshot().VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Equal(
            new[]
            {
                expectedFallbackPath[0],
                fallbackMidpoint,
                expectedFallbackPath[^1],
            },
            redoneFallbackAddVisual.Route.AsEnumerable());
        Assert.Contains(noRouteEdge.Id, redoneFallbackAdd.RoutingResult!.NoRouteEdgeIds);
        Assert.Contains(redoneFallbackAdd.CurrentScene!.Items, item => item.Id == fallbackId);
        Assert.Equal(beforeHistory.EntryCount + 2,
            redoneFallbackAdd.HistoryStatus.EntryCount);
        Assert.True(redoneFallbackAdd.HistoryStatus.CanUndo);
        Assert.False(redoneFallbackAdd.HistoryStatus.CanRedo);

        var beforeFallbackDelete = document.CaptureSnapshot();
        var beforeFallbackDeleteVisual = Assert.Single(
            beforeFallbackDelete.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        await pointer.ContextMenuDocumentPointAsync(
            redoneFallbackAdd.CurrentScene,
            fallbackMidpoint);
        Assert.Equal(
            DiagramDeletionTargetKind.Connection,
            host.CaptureState().ContextMenu?.DeletionAction?.TargetKind);

        var deletedFallback = await host.ExecuteDeletionContextActionAsync();
        Assert.True(deletedFallback?.IsCommitted);
        await session.WaitForIdleAsync();

        var afterFallbackDelete = document.CaptureSnapshot();
        Assert.False(afterFallbackDelete.SemanticModel.TryGetRelationship(
            BpmnDemoPipeline.SecondSequenceFlowId,
            out _));
        Assert.False(afterFallbackDelete.VisualModel.TryGetVisualState(
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            out _));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterFallbackDelete.VisualModel,
            beforeFallbackDeleteVisual.SourceAnchorId!));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterFallbackDelete.VisualModel,
            beforeFallbackDeleteVisual.TargetAnchorId!));
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item =>
            item.Id == fallbackId);

        await host.UndoAsync();
        await session.WaitForIdleAsync();

        var restoredFallbackDelete = session.CaptureState();
        var restoredFallbackDeleteDocument = document.CaptureSnapshot();
        Assert.True(restoredFallbackDeleteDocument.SemanticModel.TryGetRelationship(
            BpmnDemoPipeline.SecondSequenceFlowId,
            out _));
        Assert.Equal(
            beforeFallbackDeleteVisual,
            Assert.Single(restoredFallbackDeleteDocument.VisualModel.VisualStates, visual =>
                visual.Id == BpmnDemoPipeline.SecondSequenceFlowVisualId));
        Assert.Contains(noRouteEdge.Id,
            restoredFallbackDelete.RoutingResult!.NoRouteEdgeIds);
        var restoredFallback = Assert.Single(restoredFallbackDelete.CurrentScene!.Items,
            item => item.Id == fallbackId);
        Assert.Equal(
            expectedFallbackPath,
            Canvas2DConnectorPathMetadata.Resolve(restoredFallback)
                .Select(restoredFallback.Transform.TransformPoint));
        Assert.Equal(
            new[]
            {
                expectedFallbackPath[0],
                fallbackMidpoint,
                expectedFallbackPath[^1],
            },
            Canvas2DConnectorPathMetadata.ResolveEditable(restoredFallback)
                .Select(restoredFallback.Transform.TransformPoint));
        Assert.Equal(beforeHistory.EntryCount + 3,
            restoredFallbackDelete.HistoryStatus.EntryCount);
        Assert.True(restoredFallbackDelete.HistoryStatus.CanRedo);
    }

    [Fact]
    public async Task ConcurrentSecondPlacementPressIsIgnoredWhileFactoryIsInFlight()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var selection = new ToolboxSelectionState();
        using var blockingFactory = new BlockingPlacementFactory(
            PlacementOverrideCompositionFactory.RealTaskFactory);
        var compositionFactory = new PlacementOverrideCompositionFactory(_ => blockingFactory);
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: compositionFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var scene = session.CaptureState().CurrentScene!;

        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        var first = Task.Run(() => PointerObserver(host).DownDocumentPointAsync(
            scene,
            new PointD(650d, 350d),
            pointerId: 31));
        Assert.True(blockingFactory.Entered.Wait(TimeSpan.FromSeconds(5)));

        await PointerObserver(host).DownDocumentPointAsync(
            scene,
            new PointD(700d, 400d),
            pointerId: 32);
        blockingFactory.Release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        await PointerObserver(host).UpDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            new PointD(650d, 350d),
            pointerId: 31);
        await session.WaitForIdleAsync();

        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Single(after.SemanticModel.Elements.Where(item =>
            item.TypeId == BpmnSemanticTypes.Task &&
            !before.SemanticModel.Elements.Any(old => old.Id == item.Id)));
        Assert.Null(selection.SelectedItemId);
    }

    [Theory]
    [InlineData(0.75d, 1d)]
    [InlineData(1d, 1.25d)]
    [InlineData(1.5d, 2d)]
    public async Task PlacementUsesCurrentZoomPanAndIgnoresDevicePixelRatio(
        double zoom,
        double devicePixelRatio)
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(900d, 600d, devicePixelRatio));
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(
            execution,
            surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var viewportUpdate = await session.UpdateViewportAsync(
            new ViewportSnapshot(zoom, new VectorD(30d, -20d)));
        Assert.True(viewportUpdate.Succeeded);
        var before = document.CaptureSnapshot();

        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        var clicked = new PointD(400d, 250d);
        await PointerObserver(host).ClickDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            clicked);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var task = Assert.Single(after.SemanticModel.Elements.Where(item =>
            item.TypeId == BpmnSemanticTypes.Task &&
            !before.SemanticModel.Elements.Any(old => old.Id == item.Id)));
        var visual = Assert.Single(after.VisualModel.VisualStates,
            item => item.SemanticElementId == task.Id);

        Assert.Equal(340d, visual.Position.X, precision: 10);
        Assert.Equal(210d, visual.Position.Y, precision: 10);
        Assert.Equal(new SizeD(120d, 80d), visual.Size);
    }

    [Fact]
    public async Task DefaultHostExposesMixedAnchorPolicyThroughRealContextInteractions()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(900d, 600d, 1.25d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = host.CaptureState();
        var session = Session(host);
        var gammaVisualId = new VisualStateId("demo:visual:gamma");
        var gammaElement = AttachedDocument(session).CaptureSnapshot().SemanticModel.Elements
            .Single(element => element.Id.Value == "demo:gamma");
        var policy = session.ConnectorAnchorPolicyProvider.Resolve(gammaElement.TypeId);

        Assert.Same(
            global::Inceptus.DocumentEngine.Blazor.Demo.NeutralDemoPipeline
                .ConnectorAnchorPolicyProvider,
            session.ConnectorAnchorPolicyProvider);
        Assert.Equal(ConnectorAnchorPolicyMode.Disabled, policy.Top.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.Predefined, policy.Right.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.DynamicSingle, policy.Bottom.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, policy.Left.Mode);
        Assert.Empty(AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates
            .Single(visual => visual.Id == gammaVisualId).ConnectorAnchors);

        var gammaLabel = initial.Session!.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == gammaVisualId);
        await PointerObserver(host).ClickDocumentPointAsync(
            initial.Session.CurrentScene,
            Center(gammaLabel.Bounds));
        var selected = host.CaptureState();
        var scene = selected.Session!.CurrentScene!;
        Assert.Equal(gammaVisualId, Assert.Single(selected.Session.EditorState.Selection));
        var predefinedHandles = scene.Items.Where(item =>
            item.Origin.VisualStateId == gammaVisualId &&
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorKind, out var kind) &&
            kind.Kind == PropertyValueKind.Integer &&
            kind.IntegerValue == (long)ResolvedConnectorAnchorKind.Predefined).ToArray();
        Assert.Equal(2, predefinedHandles.Length);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            scene,
            Center(ResizeZone(scene, "north").Bounds));
        Assert.Null(host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        host.CloseContextMenu();

        var gammaNode = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == gammaVisualId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var rightEdge = ResizeZone(scene, "east");
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            scene,
            new PointD(
                Center(rightEdge.Bounds).X,
                gammaNode.Bounds.Top + (gammaNode.Bounds.Height * 0.1d)));
        Assert.Null(host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        host.CloseContextMenu();

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            scene,
            Center(ResizeZone(scene, "west").Bounds));
        var unlimitedAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.True(unlimitedAction.CanAdd(ConnectorAnchorRole.Source));
        Assert.True(unlimitedAction.CanAdd(ConnectorAnchorRole.Target));
        host.CloseContextMenu();

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            scene,
            Center(ResizeZone(scene, "south").Bounds));
        var singleAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.True(singleAction.CanAdd(ConnectorAnchorRole.Source));
        Assert.True(singleAction.CanAdd(ConnectorAnchorRole.Target));
        var added = await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source);
        Assert.True(added?.IsCommitted);

        var afterAdd = host.CaptureState();
        var gammaVisual = AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates
            .Single(visual => visual.Id == gammaVisualId);
        var anchor = Assert.Single(gammaVisual.ConnectorAnchors);
        Assert.Equal(ConnectorAnchorSide.Bottom, anchor.Side);
        Assert.Equal(ConnectorAnchorRole.Source, anchor.Role);
        var currentScene = afterAdd.Session!.CurrentScene!;
        var southEdge = ResizeZone(currentScene, "south");
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            currentScene,
            new PointD(
                gammaNode.Bounds.Left + (gammaNode.Bounds.Width * 0.25d),
                Center(southEdge.Bounds).Y));
        Assert.Null(host.CaptureState().ContextMenu?.ConnectorAnchorAction);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            currentScene,
            Center(predefinedHandles[1].Bounds));
        Assert.Equal(gammaVisualId, host.CaptureState().ContextMenu?.TargetVisualStateId);
        Assert.Null(host.CaptureState().ContextMenu?.ConnectorAnchorAction);
    }

    [Fact]
    public async Task ContextMenuForAlreadySelectedTargetPublishesAndReclampsAfterResize()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.5d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var alpha = scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId?.Value == "demo:visual:alpha");

        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(alpha.Bounds));

        var opened = host.CaptureState();
        Assert.Equal(before.Session.DocumentRevision, opened.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, opened.Session.HistoryStatus);
        Assert.Equal(
            before.PipelineCounters!.SceneContributionInvocationCount,
            opened.PipelineCounters!.SceneContributionInvocationCount);
        Assert.Equal("demo:visual:alpha", opened.ContextMenu?.TargetVisualStateId?.Value);

        await observer.RaiseAsync(new Canvas2DSurfaceSize(320d, 240d, 2d));

        var reclamped = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.InRange(reclamped.CssPosition.X, 8d, 136d);
        Assert.InRange(reclamped.CssPosition.Y, 8d, 188d);

        var beta = scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId?.Value == "demo:visual:beta");
        await PointerObserver(host).MoveDocumentPointAsync(scene, Center(beta.Bounds));
        Assert.NotNull(host.CaptureState().ContextMenu);

        var session = Session(host);
        var betaVisual = AttachedDocument(session).VisualModel.VisualStates.Single(item =>
            item.Id.Value == "demo:visual:beta");
        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            session.CaptureState().DocumentId,
            session.CaptureState().DocumentRevision,
            betaVisual.Id,
            betaVisual.Position + new VectorD(5d, 5d),
            VisualPlacementMode.Pinned));
        Assert.True(command.IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Null(host.CaptureState().ContextMenu);
    }

    [Fact]
    public async Task ContextMenuIsNotCreatedForAnUnavailableActiveGesture()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var scene = host.CaptureState().Session!.CurrentScene!;
        var resizeZone = ResizeZone(scene, "southeast");

        await PointerObserver(host).DownDocumentPointAsync(
            scene,
            Center(resizeZone.Bounds),
            pointerId: 47);
        Assert.NotNull(host.CaptureState().Session!.EditorState.ActiveGesture);

        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(resizeZone.Bounds));

        Assert.Null(host.CaptureState().ContextMenu);
        await PointerObserver(host).CancelAsync(pointerId: 47);
    }

    [Fact]
    public async Task ConnectorRouteContextActionsCommitOneVisualEditAndRemainUndoable()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.25d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var connector = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId?.Value == "demo:visual:alpha-beta" &&
            !IsTargetArrow(item));
        var target = connector.Origin.VisualStateId!;
        var start = connector.Transform.TransformPoint(connector.Geometry.Points[0]);
        var end = connector.Transform.TransformPoint(connector.Geometry.Points[^1]);
        var insertion = new PointD(
            start.X + ((end.X - start.X) * 0.5d),
            start.Y + ((end.Y - start.Y) * 0.5d));
        var beforeDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var beforeVisual = beforeDocument.VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Empty(beforeVisual.Route);

        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, insertion);

        var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        Assert.Equal(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            menu.ConnectorRouteAction?.Kind);
        var resolvedInsertion = Assert.IsType<PointD>(menu.ConnectorRouteAction?.RoutePoint);
        Assert.Equal(insertion.X, resolvedInsertion.X, 10);
        Assert.Equal(insertion.Y, resolvedInsertion.Y, 10);
        var alpha = scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId?.Value == "demo:visual:alpha");
        await PointerObserver(host).MoveDocumentPointAsync(scene, Center(alpha.Bounds));
        var hovered = host.CaptureState();
        Assert.NotSame(menu.SourceScene, hovered.Session!.CurrentScene);
        Assert.Equal(menu.DocumentRevision, hovered.Session.DocumentRevision);
        Assert.True(hovered.Session.Generation.Value > menu.SessionGeneration.Value);
        Assert.Same(menu, hovered.ContextMenu);
        var added = await host.ExecuteConnectorRouteContextActionAsync();

        Assert.True(added?.IsCommitted);
        Assert.Equal(UpdateConnectionRouteCommand.KnownTypeId, added?.CommandTypeId);
        var afterAdd = host.CaptureState();
        Assert.Null(afterAdd.ContextMenu);
        Assert.Equal(before.Session.DocumentRevision.Increment(), afterAdd.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            afterAdd.Session.HistoryStatus.EntryCount);
        var addedDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var addedVisual = addedDocument.VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Equal(3, addedVisual.Route.Length);
        Assert.Equal(start, addedVisual.Route[0]);
        Assert.Equal(resolvedInsertion.X, addedVisual.Route[1].X, 10);
        Assert.Equal(resolvedInsertion.Y, addedVisual.Route[1].Y, 10);
        Assert.Equal(end, addedVisual.Route[2]);
        Assert.True(beforeDocument.SemanticModel.Elements.SequenceEqual(
            addedDocument.SemanticModel.Elements));
        Assert.True(beforeDocument.SemanticModel.Relationships.SequenceEqual(
            addedDocument.SemanticModel.Relationships));
        Assert.Equal(beforeVisual.Properties, addedVisual.Properties);
        Assert.Equal(target, Assert.Single(afterAdd.Session.EditorState.Selection));

        var addedScene = afterAdd.Session.CurrentScene!;
        var bend = addedScene.Items.Single(item =>
            item.Origin.VisualStateId == target &&
            item.Metadata.TryGetValue(Canvas2DRouteGestureMetadata.HandleRole, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(role.TextValue, Canvas2DRouteGestureMetadata.BendRole));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            addedScene,
            new PointD(
                bend.Bounds.X + (bend.Bounds.Width / 2d),
                bend.Bounds.Y + (bend.Bounds.Height / 2d)));
        Assert.Equal(
            Canvas2DConnectorRouteContextActionKind.DeletePoint,
            host.CaptureState().ContextMenu?.ConnectorRouteAction?.Kind);

        var deleted = await host.ExecuteConnectorRouteContextActionAsync();

        Assert.True(deleted?.IsCommitted);
        Assert.Equal(UpdateConnectionRouteCommand.KnownTypeId, deleted?.CommandTypeId);
        var afterDelete = host.CaptureState();
        Assert.Equal(before.Session.DocumentRevision.Increment().Increment(),
            afterDelete.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 2,
            afterDelete.Session.HistoryStatus.EntryCount);
        var deletedVisual = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Empty(deletedVisual.Route);

        await host.UndoAsync();
        var undoDelete = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.True(addedVisual.Route.AsSpan().SequenceEqual(undoDelete.Route.AsSpan()));

        await host.UndoAsync();
        var undoAdd = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Empty(undoAdd.Route);

        await host.RedoAsync();
        await host.RedoAsync();
        var redone = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Empty(redone.Route);
    }

    [Fact]
    public async Task BpmnDeletionContextActionsDeleteNodeAndConnectionThroughOneCommandEach()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var beforeNode = document.CaptureSnapshot();
        var beforeNodeState = host.CaptureState().Session!;
        var node = beforeNodeState.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            beforeNodeState.CurrentScene,
            Center(node.Bounds));

        var nodeMenu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.Equal(
            DiagramDeletionTargetKind.Element,
            nodeMenu.DeletionAction?.TargetKind);
        Assert.Equal(BpmnDemoPipeline.TaskId, nodeMenu.DeletionAction?.SemanticId);
        var deletedNode = await host.ExecuteDeletionContextActionAsync();

        Assert.True(deletedNode?.IsCommitted);
        Assert.Null(host.CaptureState().ContextMenu);
        var afterNodeState = host.CaptureState().Session!;
        var afterNode = document.CaptureSnapshot();
        Assert.Equal(beforeNode.Revision.Increment(), afterNode.Revision);
        Assert.Equal(beforeNodeState.HistoryStatus.EntryCount + 1,
            afterNodeState.HistoryStatus.EntryCount);
        Assert.False(afterNode.SemanticModel.TryGetElement(BpmnDemoPipeline.TaskId, out _));
        Assert.DoesNotContain(BpmnDemoPipeline.TaskVisualId,
            afterNodeState.EditorState.Selection);
        Assert.DoesNotContain(afterNodeState.EditorState.Selection, visualStateId =>
            beforeNode.VisualModel.VisualStates.Any(visual =>
                visual.Id == visualStateId &&
                (visual.SemanticElementId == BpmnDemoPipeline.FirstSequenceFlowId ||
                 visual.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId)));

        await host.UndoAsync();
        Assert.True(document.CaptureSnapshot().SemanticModel.TryGetElement(
            BpmnDemoPipeline.TaskId,
            out _));
        var beforeConnectionState = host.CaptureState().Session!;
        var connectionScene = beforeConnectionState.CurrentScene!;
        var connector = connectionScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ThirdSequenceFlowVisualId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            !IsTargetArrow(item));
        var connectorSegment = Enumerable.Range(0, connector.Geometry.Points.Length - 1)
            .Select(index =>
            {
                var start = connector.Transform.TransformPoint(connector.Geometry.Points[index]);
                var end = connector.Transform.TransformPoint(connector.Geometry.Points[index + 1]);
                var delta = end - start;
                return (Start: start, End: end, LengthSquared:
                    (delta.X * delta.X) + (delta.Y * delta.Y));
            })
            .OrderByDescending(static segment => segment.LengthSquared)
            .First();
        var connectorStart = connectorSegment.Start;
        var connectorEnd = connectorSegment.End;
        var connectorPoint = new PointD(
            (connectorStart.X + connectorEnd.X) / 2d,
            (connectorStart.Y + connectorEnd.Y) / 2d);
        var beforeConnection = document.CaptureSnapshot();
        var connectorVisual = beforeConnection.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ThirdSequenceFlowVisualId);

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            connectionScene,
            connectorPoint);

        var connectionMenu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        Assert.Equal(
            DiagramDeletionTargetKind.Connection,
            connectionMenu.DeletionAction?.TargetKind);
        Assert.Equal(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            connectionMenu.ConnectorRouteAction?.Kind);
        var deletedConnection = await host.ExecuteDeletionContextActionAsync();

        Assert.True(deletedConnection?.IsCommitted);
        Assert.Null(host.CaptureState().ContextMenu);
        var afterConnection = document.CaptureSnapshot();
        Assert.False(afterConnection.SemanticModel.TryGetRelationship(
            BpmnDemoPipeline.ThirdSequenceFlowId,
            out _));
        Assert.False(afterConnection.VisualModel.TryGetVisualState(
            BpmnDemoPipeline.ThirdSequenceFlowVisualId,
            out _));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterConnection.VisualModel,
            connectorVisual.SourceAnchorId!));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterConnection.VisualModel,
            connectorVisual.TargetAnchorId!));

        await host.UndoAsync();
        var restoredConnection = document.CaptureSnapshot();
        Assert.True(restoredConnection.SemanticModel.TryGetRelationship(
            BpmnDemoPipeline.ThirdSequenceFlowId,
            out _));
        Assert.Equal(connectorVisual, restoredConnection.VisualModel.VisualStates.Single(
            visual => visual.Id == BpmnDemoPipeline.ThirdSequenceFlowVisualId));
    }

    [Fact]
    public async Task DeletionContextActionRejectsAStaleRevisionAndEmptyCatalogOffersNoAction()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var task = scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(task.Bounds));
        Assert.NotNull(host.CaptureState().ContextMenu?.DeletionAction);

        var snapshot = AttachedDocument(session).CaptureSnapshot();
        var approved = snapshot.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ApprovedTaskVisualId);
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            snapshot.DocumentId,
            snapshot.Revision,
            approved.Id,
            approved.Position + new VectorD(5d, 7d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync();

        Assert.Null(await host.ExecuteDeletionContextActionAsync());
        Assert.Null(host.CaptureState().ContextMenu);
        Assert.True(AttachedDocument(session).CaptureSnapshot().SemanticModel.TryGetElement(
            BpmnDemoPipeline.TaskId,
            out _));

        var neutralExecution = new RecordingRenderExecution();
        var neutralObserver = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var neutral = CreateHost(neutralExecution, neutralObserver);
        await neutral.InitializeAsync("neutral-canvas", "neutral-container");
        var neutralScene = neutral.CaptureState().Session!.CurrentScene!;
        var neutralNode = neutralScene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId is not null);
        await PointerObserver(neutral).ContextMenuDocumentPointAsync(
            neutralScene,
            Center(neutralNode.Bounds));
        Assert.Null(neutral.CaptureState().ContextMenu?.DeletionAction);
    }

    [Fact]
    public async Task ConnectorRouteContextActionCannotCommitAfterItsSceneOrRevisionIsStale()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var connector = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId?.Value == "demo:visual:alpha-beta" &&
            !IsTargetArrow(item));
        var start = connector.Transform.TransformPoint(connector.Geometry.Points[0]);
        var end = connector.Transform.TransformPoint(connector.Geometry.Points[^1]);
        var insertion = new PointD(
            start.X + ((end.X - start.X) * 0.4d),
            start.Y + ((end.Y - start.Y) * 0.4d));
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, insertion);
        Assert.Equal(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            host.CaptureState().ContextMenu?.ConnectorRouteAction?.Kind);

        var session = Session(host);
        var document = AttachedDocument(session).CaptureSnapshot();
        var beta = document.VisualModel.VisualStates.Single(item =>
            item.Id.Value == "demo:visual:beta");
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            before.Session.DocumentId,
            before.Session.DocumentRevision,
            beta.Id,
            beta.Position + new VectorD(3d, 4d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync();

        var stale = await host.ExecuteConnectorRouteContextActionAsync();
        var after = host.CaptureState().Session!;

        Assert.Null(stale);
        Assert.Null(host.CaptureState().ContextMenu);
        Assert.Equal(before.Session.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.Empty(AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates
            .Single(item => item.Id.Value == "demo:visual:alpha-beta").Route);
    }

    [Fact]
    public async Task ConnectorAnchorContextActionsAddInEdgeOrderAndDeleteThroughOneVisualCommit()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.25d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var target = new VisualStateId("demo:visual:alpha");
        var edge = ResizeZone(scene, "east");
        var targetItem = scene.Items.First(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == target &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        var sourceContextPoint = new PointD(
            Center(edge.Bounds).X,
            targetItem.Bounds.Top + (targetItem.Bounds.Height * 0.25d));
        var beforeDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var beforeVisual = beforeDocument.VisualModel.VisualStates.Single(item =>
            item.Id == target);
        var projectionCount = before.PipelineCounters!.ProjectionRuleInvocationCount;
        var layoutCount = before.PipelineCounters.LayoutInvocationCount;
        var routingCount = before.PipelineCounters.RoutingInvocationCount;
        var sceneCount = before.PipelineCounters.SceneContributionInvocationCount;

        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, sourceContextPoint);

        var sourceMenu = Assert.IsType<DocumentCanvasContextMenuState>(
            host.CaptureState().ContextMenu);
        var sourceAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            sourceMenu.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, sourceAction.Kind);
        Assert.Equal(ConnectorAnchorSide.Right, sourceAction.Side);
        Assert.Equal(0, sourceAction.InsertionIndex);
        Assert.Null(sourceMenu.ConnectorRouteAction);

        var sourceResult = await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source);

        Assert.True(sourceResult?.IsCommitted);
        var afterSource = host.CaptureState();
        Assert.Null(afterSource.ContextMenu);
        Assert.Equal(before.Session.DocumentRevision.Increment(),
            afterSource.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            afterSource.Session.HistoryStatus.EntryCount);
        Assert.Equal(projectionCount + 5,
            afterSource.PipelineCounters!.ProjectionRuleInvocationCount);
        Assert.Equal(layoutCount, afterSource.PipelineCounters.LayoutInvocationCount);
        Assert.Equal(routingCount + 1, afterSource.PipelineCounters.RoutingInvocationCount);
        Assert.Equal(sceneCount + 1,
            afterSource.PipelineCounters.SceneContributionInvocationCount);
        var sourceDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var sourceVisual = sourceDocument.VisualModel.VisualStates.Single(item =>
            item.Id == target);
        var sourceAnchor = Assert.Single(sourceVisual.ConnectorAnchors);
        Assert.Equal(ConnectorAnchorSide.Right, sourceAnchor.Side);
        Assert.Equal(ConnectorAnchorRole.Source, sourceAnchor.Role);
        Assert.Equal(0, sourceAnchor.Order);
        Assert.Equal(
            FormattableString.Invariant(
                $"inceptus:connector-anchor:{beforeDocument.DocumentId.Value.Length}:{beforeDocument.DocumentId.Value}:{target.Value.Length}:{target.Value}:{beforeDocument.Revision.Value}:Right:Source:0"),
            sourceAnchor.Id.Value);
        Assert.Equal(beforeVisual.Position, sourceVisual.Position);
        Assert.Equal(beforeVisual.Size, sourceVisual.Size);
        Assert.Equal(beforeVisual.PlacementMode, sourceVisual.PlacementMode);
        Assert.True(beforeVisual.Route.AsSpan().SequenceEqual(sourceVisual.Route.AsSpan()));
        Assert.Equal(beforeVisual.Properties, sourceVisual.Properties);
        Assert.Equal(beforeVisual.SourceAnchorId, sourceVisual.SourceAnchorId);
        Assert.Equal(beforeVisual.TargetAnchorId, sourceVisual.TargetAnchorId);
        Assert.Equal(target, Assert.Single(afterSource.Session.EditorState.Selection));
        Assert.True(beforeDocument.SemanticModel.Elements.SequenceEqual(
            sourceDocument.SemanticModel.Elements));
        Assert.True(beforeDocument.SemanticModel.Relationships.SequenceEqual(
            sourceDocument.SemanticModel.Relationships));

        var sourceScene = afterSource.Session.CurrentScene!;
        var sourceEdge = ResizeZone(sourceScene, "east");
        var currentTarget = sourceScene.Items.First(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == target &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        var targetContextPoint = new PointD(
            Center(sourceEdge.Bounds).X,
            currentTarget.Bounds.Top + (currentTarget.Bounds.Height * 0.75d));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            sourceScene,
            targetContextPoint);
        var targetAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, targetAction.Kind);
        Assert.Equal(1, targetAction.InsertionIndex);

        var targetResult = await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Target);

        Assert.True(targetResult?.IsCommitted);
        var afterTarget = host.CaptureState();
        Assert.Equal(before.Session.DocumentRevision.Increment().Increment(),
            afterTarget.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 2,
            afterTarget.Session.HistoryStatus.EntryCount);
        var targetDocument = AttachedDocument(Session(host)).CaptureSnapshot();
        var targetVisual = targetDocument.VisualModel.VisualStates.Single(item =>
            item.Id == target);
        Assert.Collection(
            targetVisual.ConnectorAnchors,
            anchor =>
            {
                Assert.Equal(sourceAnchor.Id, anchor.Id);
                Assert.Equal(ConnectorAnchorRole.Source, anchor.Role);
                Assert.Equal(0, anchor.Order);
            },
            anchor =>
            {
                Assert.Equal(ConnectorAnchorRole.Target, anchor.Role);
                Assert.Equal(1, anchor.Order);
            });

        var targetHandle = afterTarget.Session.CurrentScene!.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.Role, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(
                role.TextValue,
                ConnectorAnchorRole.Target.ToString()));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            afterTarget.Session.CurrentScene,
            Center(targetHandle.Bounds));
        var deleteAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, deleteAction.Kind);
        Assert.True(deleteAction.CanDelete);
        Assert.Equal(target, host.CaptureState().ContextMenu?.TargetVisualStateId);

        var deleteResult = await host.ExecuteConnectorAnchorContextActionAsync();

        Assert.True(deleteResult?.IsCommitted);
        var afterDelete = host.CaptureState();
        Assert.Equal(before.Session.DocumentRevision.Increment().Increment().Increment(),
            afterDelete.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 3,
            afterDelete.Session.HistoryStatus.EntryCount);
        var deletedVisual = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Equal(sourceAnchor.Id, Assert.Single(deletedVisual.ConnectorAnchors).Id);

        await host.UndoAsync();
        var undoDelete = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Equal(2, undoDelete.ConnectorAnchors.Length);

        await host.RedoAsync();
        var redoDelete = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.Equal(sourceAnchor.Id, Assert.Single(redoDelete.ConnectorAnchors).Id);
    }

    [Fact]
    public async Task ConnectorAnchorAddIsRevalidatedAgainstCurrentPolicyBeforeHostCommit()
    {
        var provider = new MutableConnectorAnchorPolicyProvider(
            ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges);
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(720d, 480d, 1.25d));
        await using var host = CreateHost(execution, observer, provider);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var edge = ResizeZone(scene, "east");

        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(edge.Bounds));
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.True(action.CanAdd(ConnectorAnchorRole.Source));
        Assert.True(action.CanAdd(ConnectorAnchorRole.Target));

        provider.Policy = DisabledConnectorAnchorPolicy();
        var result = await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source);
        var after = host.CaptureState();

        Assert.Null(result);
        Assert.Null(after.ContextMenu);
        Assert.Equal(before.Session.DocumentRevision, after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, after.Session.HistoryStatus);
        Assert.Empty(AttachedDocument(Session(host)).CaptureSnapshot().VisualModel.VisualStates
            .Single(item => item.Id.Value == "demo:visual:alpha").ConnectorAnchors);
    }

    [Fact]
    public async Task RoleRestrictedAnchorContextCannotCommitTheUnadvertisedRole()
    {
        var provider = new MutableConnectorAnchorPolicyProvider(
            new ElementConnectorAnchorPolicy(
                EdgeConnectorAnchorPolicy.Disabled,
                EdgeConnectorAnchorPolicy.DynamicUnlimited(
                    ConnectorAnchorRoleCapability.Target),
                EdgeConnectorAnchorPolicy.Disabled,
                EdgeConnectorAnchorPolicy.Disabled));
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(720d, 480d, 1.25d));
        await using var host = CreateHost(execution, observer, provider);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            scene,
            Center(ResizeZone(scene, "east").Bounds));
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.False(action.CanAdd(ConnectorAnchorRole.Source));
        Assert.True(action.CanAdd(ConnectorAnchorRole.Target));

        var result = await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source);
        var after = host.CaptureState();

        Assert.Null(result);
        Assert.Null(after.ContextMenu);
        Assert.Equal(before.Session.DocumentRevision, after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, after.Session.HistoryStatus);
    }

    [Fact]
    public async Task ConnectorAnchorContextActionCannotCommitAfterRevisionIsStaleOrCancellation()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var edge = ResizeZone(scene, "east");
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(edge.Bounds));
        Assert.Equal(
            Canvas2DConnectorAnchorContextActionKind.AddAnchor,
            host.CaptureState().ContextMenu?.ConnectorAnchorAction?.Kind);

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = await host.ExecuteConnectorAnchorContextActionAsync(
                ConnectorAnchorRole.Source,
                cancellation.Token);
            Assert.Null(cancelled);
        }

        var afterCancellation = host.CaptureState();
        Assert.Equal(before.Session.DocumentRevision, afterCancellation.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, afterCancellation.Session.HistoryStatus);
        Assert.NotNull(afterCancellation.ContextMenu);

        var session = Session(host);
        var document = AttachedDocument(session).CaptureSnapshot();
        var beta = document.VisualModel.VisualStates.Single(item =>
            item.Id.Value == "demo:visual:beta");
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            before.Session.DocumentId,
            before.Session.DocumentRevision,
            beta.Id,
            beta.Position + new VectorD(3d, 4d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync();

        var stale = await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source);
        var afterStale = host.CaptureState();

        Assert.Null(stale);
        Assert.Null(afterStale.ContextMenu);
        Assert.Equal(before.Session.DocumentRevision.Increment(),
            afterStale.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            afterStale.Session.HistoryStatus.EntryCount);
        Assert.Empty(AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates
            .Single(item => item.Id.Value == "demo:visual:alpha").ConnectorAnchors);
    }

    [Fact]
    public async Task PropertiesApplyUsesOneMoveCommandAndAnUnchangedApplyIsExactNoOp()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = DocumentCanvasPropertiesDraft.Format(authoritative.Bounds.X + 25d),
            Y = DocumentCanvasPropertiesDraft.Format(authoritative.Bounds.Y + 15d),
        };
        host.UpdatePropertiesFormState(true, target, isDirty: true);
        var before = host.CaptureState();
        var projectionCount = before.PipelineCounters!.ProjectionRuleInvocationCount;
        var layoutCount = before.PipelineCounters.LayoutInvocationCount;
        var routingCount = before.PipelineCounters.RoutingInvocationCount;
        var sceneCount = before.PipelineCounters.SceneContributionInvocationCount;

        var committed = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, committed.Status);
        var after = host.CaptureState();
        Assert.Equal(before.Session!.DocumentRevision.Increment(), after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1, after.Session.HistoryStatus.EntryCount);
        Assert.Equal(
            projectionCount + 5,
            after.PipelineCounters!.ProjectionRuleInvocationCount);
        Assert.Equal(
            layoutCount,
            after.PipelineCounters.LayoutInvocationCount);
        Assert.Equal(
            routingCount + 1,
            after.PipelineCounters.RoutingInvocationCount);
        Assert.Equal(
            sceneCount + 1,
            after.PipelineCounters.SceneContributionInvocationCount);
        Assert.True(after.PropertiesFormOpen);
        Assert.False(after.PropertiesFormDirty);

        var noOpDraft = new DocumentCanvasPropertiesDraft(
            Assert.IsType<DocumentCanvasPropertySnapshot>(committed.Authoritative));
        var beforeNoOp = host.CaptureState();
        var noOpSceneCount = beforeNoOp.PipelineCounters!.SceneContributionInvocationCount;
        var noOp = await host.ApplyPropertiesAsync(noOpDraft);
        var afterNoOp = host.CaptureState();

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.NoChange, noOp.Status);
        Assert.Equal(beforeNoOp.Session!.DocumentRevision, afterNoOp.Session!.DocumentRevision);
        Assert.Equal(beforeNoOp.Session.HistoryStatus, afterNoOp.Session.HistoryStatus);
        Assert.Equal(
            noOpSceneCount,
            afterNoOp.PipelineCounters!.SceneContributionInvocationCount);
    }

    [Fact]
    public async Task PropertiesCombinedBoundsApplyUsesOneResizeCommit()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = "72",
            Y = "82",
            Width = "190",
            Height = "95",
        };
        host.UpdatePropertiesFormState(true, target, isDirty: true);
        var before = host.CaptureState();

        var result = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.Equal(new RectD(72d, 82d, 190d, 95d), result.Authoritative?.Bounds);
        var after = host.CaptureState();
        Assert.Equal(before.Session!.DocumentRevision.Increment(), after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1, after.Session.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task PropertiesNameApplyUsesOneSemanticCommitAndUndoRedoRefreshAuthoritativeLabel()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        SetDataValue(draft, NameFieldId, "Alpha renamed");
        Assert.True(host.UpdatePropertiesFormState(true, target, isDirty: draft.IsDirty));
        var before = host.CaptureState();
        var beforeSceneCount = before.PipelineCounters!.SceneContributionInvocationCount;

        var committed = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, committed.Status);
        Assert.Equal("Alpha renamed", DataEditorValue(committed.Authoritative, NameFieldId));
        Assert.Equal(authoritative.Bounds, committed.Authoritative?.Bounds);
        var after = host.CaptureState();
        Assert.Equal(before.Session!.DocumentRevision.Increment(), after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1, after.Session.HistoryStatus.EntryCount);
        Assert.Equal(beforeSceneCount + 1,
            after.PipelineCounters!.SceneContributionInvocationCount);
        Assert.Equal("Alpha renamed", LabelContent(after.Session.CurrentScene!, target));
        Assert.Equal(target, Assert.Single(after.Session.EditorState.Selection));
        Assert.False(after.PropertiesFormDirty);

        await host.UndoAsync();

        var undone = host.CaptureState();
        Assert.True(host.TryCaptureSelectedProperties(target, out var undoneProperties));
        Assert.Equal("Alpha", DataEditorValue(undoneProperties, NameFieldId));
        Assert.Equal(authoritative.Bounds, undoneProperties?.Bounds);
        Assert.Equal("Alpha", LabelContent(undone.Session!.CurrentScene!, target));
        Assert.Equal(target, Assert.Single(undone.Session.EditorState.Selection));

        await host.RedoAsync();

        var redone = host.CaptureState();
        Assert.True(host.TryCaptureSelectedProperties(target, out var redoneProperties));
        Assert.Equal("Alpha renamed", DataEditorValue(redoneProperties, NameFieldId));
        Assert.Equal(authoritative.Bounds, redoneProperties?.Bounds);
        Assert.Equal("Alpha renamed", LabelContent(redone.Session!.CurrentScene!, target));
        Assert.Equal(target, Assert.Single(redone.Session.EditorState.Selection));
    }

    [Fact]
    public async Task TimerDefinitionSchemaAndCommandsRemainIntactWhileEventPropertiesAreUnavailable()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var target = BpmnDemoPipeline.TimerCatchEventVisualId;
        var document = AttachedDocument(Session(host));
        var beforeDocument = document.CaptureSnapshot();
        var beforeTimer = beforeDocument.SemanticModel.Elements.Single(element =>
            element.Id == BpmnDemoPipeline.TimerCatchEventId);
        Assert.Equal(
            string.Empty,
            beforeTimer.Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
        var scene = host.CaptureState().Session!.CurrentScene!;
        var timerBody = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == target);
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(timerBody.Bounds));
        Assert.Equal(target, Assert.Single(host.CaptureState().Session!.EditorState.Selection));

        Assert.Null(await host.OpenPropertiesAsync(target));
        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(beforeDocument, target,
            new ElementPropertiesSchemaCatalog(BpmnPluginRegistration.N100.PropertiesSchemas), out var authoritative));
        Assert.Equal(string.Empty, DataEditorValue(authoritative, TimerDefinitionFieldId));
        Assert.True(DataField(authoritative, TimerDefinitionFieldId).CanEdit);
        var before = host.CaptureState().Session!;

        var committed = await Session(host).ExecuteAsync(new UpdateSemanticElementPropertyCommand(
            beforeDocument.DocumentId, beforeDocument.Revision, BpmnDemoPipeline.TimerCatchEventId,
            BpmnSemanticProperties.TimerDefinition, PropertyValue.FromText("PT15M")));
        await Session(host).WaitForIdleAsync();

        Assert.True(committed.IsCommitted);
        Assert.False(host.CaptureState().PropertiesFormOpen);
        var after = host.CaptureState().Session!;
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, after.HistoryStatus.EntryCount);
        Assert.Equal(
            "PT15M",
            document.CaptureSnapshot().SemanticModel.Elements.Single(element =>
                element.Id == BpmnDemoPipeline.TimerCatchEventId)
                .Properties[BpmnSemanticProperties.TimerDefinition].TextValue);

        await host.UndoAsync();

        Assert.Equal(
            string.Empty,
            document.CaptureSnapshot().SemanticModel.Elements.Single(element =>
                element.Id == BpmnDemoPipeline.TimerCatchEventId)
                .Properties[BpmnSemanticProperties.TimerDefinition].TextValue);

        await host.RedoAsync();

        Assert.Equal(
            "PT15M",
            document.CaptureSnapshot().SemanticModel.Elements.Single(element =>
                element.Id == BpmnDemoPipeline.TimerCatchEventId)
                .Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
    }

    [Fact]
    public async Task ConnectorPropertiesApplyEditableNameAndDescriptionAsSeparateRelationshipCommits()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = host.CaptureState().Session!;
        var connector = before.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId?.Value == "demo:visual:beta-gamma" &&
            !IsTargetArrow(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            before.CurrentScene,
            connector.Geometry.Points[1]);
        var target = new VisualStateId("demo:visual:beta-gamma");
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));

        Assert.True(original.IsConnector);
        Assert.True(DataField(original, NameFieldId).CanEdit);
        Assert.True(DataField(original, DescriptionFieldId).CanEdit);
        Assert.False(original.TryGetDataField(ElementNumberFieldId, out _));
        Assert.False(original.CanEditBounds);
        Assert.Equal(ConnectorLabelPlacement.Default, original.LabelPlacement);

        var name = new DocumentCanvasPropertiesDraft(original);
        SetDataValue(name, NameFieldId, "Approved");
        Assert.True(host.UpdatePropertiesFormState(true, target, name.IsDirty));
        var renamed = await host.ApplyPropertiesAsync(name);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, renamed.Status);
        Assert.Equal("Approved", DataEditorValue(renamed.Authoritative, NameFieldId));
        Assert.Equal("Approved", LabelContent(host.CaptureState().Session!.CurrentScene!, target));

        var renamedSnapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(renamed.Authoritative);
        var description = new DocumentCanvasPropertiesDraft(renamedSnapshot);
        SetDataValue(description, DescriptionFieldId, "Approved connector description");
        Assert.True(host.UpdatePropertiesFormState(true, target, description.IsDirty));
        var described = await host.ApplyPropertiesAsync(description);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, described.Status);
        Assert.Equal(
            "Approved connector description",
            DataEditorValue(described.Authoritative, DescriptionFieldId));
        var state = host.CaptureState().Session!;
        Assert.Equal(before.DocumentRevision.Increment().Increment(), state.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 2, state.HistoryStatus.EntryCount);
        Assert.Equal(target, Assert.Single(state.EditorState.Selection));
        var relationship = AttachedDocument(Session(host)).CaptureSnapshot()
            .SemanticModel.Relationships.Single(item => item.Id.Value == "demo:beta-gamma");
        Assert.Equal("Approved", relationship.Properties["demo:label"].TextValue);
        Assert.Equal(
            "Approved connector description",
            relationship.Properties["demo:description"].TextValue);
    }

    [Fact]
    public async Task SelectedConnectorLabelDragPreviewsSceneOnlyAndCommitsOnePlacement()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.25d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = host.CaptureState().Session!;
        var connector = initial.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId?.Value == "demo:visual:beta-gamma" &&
            !IsTargetArrow(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            initial.CurrentScene,
            connector.Geometry.Points[1]);
        var target = new VisualStateId("demo:visual:beta-gamma");
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var connectorNameDraft = new DocumentCanvasPropertiesDraft(properties);
        SetDataValue(connectorNameDraft, NameFieldId, "Approved");
        var named = await host.ApplyPropertiesAsync(connectorNameDraft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, named.Status);
        Assert.True(host.UpdatePropertiesFormState(false, null, isDirty: false));

        var before = host.CaptureState();
        var scene = before.Session!.CurrentScene!;
        var label = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == target);
        var translation = new VectorD(34d, 21d);
        var start = new PointD(label.Bounds.Left + 1d, Center(label.Bounds).Y);
        var destination = start + translation;
        var pointer = PointerObserver(host);
        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(scene, start));
        Assert.Equal(label.Id, hit.SceneObjectId);
        Assert.True(label.Metadata[
            global::Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DLabelGestureMetadata
                .LabelMoveCapable].BooleanValue);
        await pointer.MoveDocumentPointAsync(scene, start);
        await WaitForCursorAsync(host, "grab");

        await pointer.DownDocumentPointAsync(scene, start, pointerId: 211);
        Assert.Equal(
            "inceptus.canvas2d:connector-label-move",
            host.CaptureState().Session!.EditorState.ActiveGesture?.Kind);
        await WaitForCursorAsync(host, "grabbing");
        await pointer.MoveDocumentPointAsync(scene, destination, pointerId: 211, buttons: 1);

        var previewed = host.CaptureState();
        Assert.Equal(before.Session.DocumentRevision, previewed.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, previewed.Session.HistoryStatus);
        Assert.Equal(
            before.PipelineCounters!.ProjectionRuleInvocationCount,
            previewed.PipelineCounters!.ProjectionRuleInvocationCount);
        Assert.Equal(before.PipelineCounters.LayoutInvocationCount,
            previewed.PipelineCounters.LayoutInvocationCount);
        Assert.Equal(before.PipelineCounters.RoutingInvocationCount,
            previewed.PipelineCounters.RoutingInvocationCount);
        var preview = previewed.Session.CurrentScene!.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-label-preview:",
                StringComparison.Ordinal) == true);
        Assert.Equal(Center(label.Bounds) + translation, Center(preview.Bounds));

        await pointer.UpDocumentPointAsync(
            previewed.Session.CurrentScene,
            destination,
            pointerId: 211);
        await Session(host).WaitForIdleAsync();
        var committed = host.CaptureState().Session!;

        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal(before.Session.DocumentRevision.Increment(), committed.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            committed.HistoryStatus.EntryCount);
        Assert.Equal(target, Assert.Single(committed.EditorState.Selection));
        var connectorVisual = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.True(ConnectorLabelPlacement.TryRead(
            connectorVisual.Properties,
            out var placement));
        Assert.NotEqual(ConnectorLabelPlacement.Default, placement);

        await host.UndoAsync();
        var undone = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.False(ConnectorLabelPlacement.TryRead(undone.Properties, out _));

        await host.RedoAsync();
        var redone = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.True(ConnectorLabelPlacement.TryRead(redone.Properties, out var redonePlacement));
        Assert.Equal(placement, redonePlacement);
    }

    [Fact]
    public async Task ConnectorLabelNoOpAndCancellationNeverPersistPlacement()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:beta-gamma");
        var initial = host.CaptureState().Session!;
        var connector = initial.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == target &&
            !IsTargetArrow(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            initial.CurrentScene,
            connector.Geometry.Points[1]);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var connectorNameDraft = new DocumentCanvasPropertiesDraft(properties);
        SetDataValue(connectorNameDraft, NameFieldId, "Approved");
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await host.ApplyPropertiesAsync(connectorNameDraft)).Status);
        Assert.True(host.UpdatePropertiesFormState(false, null, isDirty: false));
        var baseline = host.CaptureState().Session!;
        var pointer = PointerObserver(host);
        var label = baseline.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label && item.Origin.VisualStateId == target);
        var start = new PointD(label.Bounds.Left + 1d, Center(label.Bounds).Y);

        await pointer.DownDocumentPointAsync(baseline.CurrentScene, start, pointerId: 221);
        await pointer.UpDocumentPointAsync(baseline.CurrentScene, start, pointerId: 221);
        var noOp = host.CaptureState().Session!;

        Assert.Equal(baseline.DocumentRevision, noOp.DocumentRevision);
        Assert.Equal(baseline.HistoryStatus, noOp.HistoryStatus);
        Assert.Null(noOp.EditorState.ActiveGesture);

        await pointer.DownDocumentPointAsync(noOp.CurrentScene!, start, pointerId: 222);
        await pointer.MoveDocumentPointAsync(
            noOp.CurrentScene!,
            start + new VectorD(18d, -27d),
            pointerId: 222,
            buttons: 1);
        Assert.NotNull(host.CaptureState().Session!.EditorState.ActiveGesture);
        await pointer.CancelAsync(pointerId: 222);
        var cancelled = host.CaptureState().Session!;

        Assert.Equal(baseline.DocumentRevision, cancelled.DocumentRevision);
        Assert.Equal(baseline.HistoryStatus, cancelled.HistoryStatus);
        Assert.Null(cancelled.EditorState.ActiveGesture);
        var visual = AttachedDocument(Session(host)).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.False(ConnectorLabelPlacement.TryRead(visual.Properties, out _));
    }

    [Fact]
    public async Task ConnectorLabelGestureCannotCommitAfterDocumentRevisionChanges()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:beta-gamma");
        var initial = host.CaptureState().Session!;
        var connector = initial.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == target &&
            !IsTargetArrow(item));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            initial.CurrentScene,
            connector.Geometry.Points[1]);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var connectorNameDraft = new DocumentCanvasPropertiesDraft(properties);
        SetDataValue(connectorNameDraft, NameFieldId, "Approved");
        Assert.Equal(
            DocumentCanvasPropertiesApplyStatus.Committed,
            (await host.ApplyPropertiesAsync(connectorNameDraft)).Status);
        Assert.True(host.UpdatePropertiesFormState(false, null, isDirty: false));
        var before = host.CaptureState().Session!;
        var label = before.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label && item.Origin.VisualStateId == target);
        var start = new PointD(label.Bounds.Left + 1d, Center(label.Bounds).Y);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(before.CurrentScene, start, pointerId: 223);
        await pointer.MoveDocumentPointAsync(
            before.CurrentScene,
            start + new VectorD(20d, 16d),
            pointerId: 223,
            buttons: 1);
        Assert.NotNull(host.CaptureState().Session!.EditorState.ActiveGesture);

        var session = Session(host);
        var beta = AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates.Single(
            item => item.Id.Value == "demo:visual:beta");
        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            before.DocumentId,
            before.DocumentRevision,
            beta.Id,
            beta.Position + new VectorD(5d, 7d),
            VisualPlacementMode.Pinned));
        Assert.True(command.IsCommitted);
        await session.WaitForIdleAsync();
        await pointer.CaptureReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var invalidated = host.CaptureState().Session!;
        Assert.Null(invalidated.EditorState.ActiveGesture);
        Assert.Equal(before.DocumentRevision.Increment(), invalidated.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, invalidated.HistoryStatus.EntryCount);
        var connectorVisual = AttachedDocument(session).CaptureSnapshot()
            .VisualModel.VisualStates.Single(item => item.Id == target);
        Assert.False(ConnectorLabelPlacement.TryRead(connectorVisual.Properties, out _));

        await pointer.UpDocumentPointAsync(
            invalidated.CurrentScene!,
            start + new VectorD(20d, 16d),
            pointerId: 223);
        Assert.Equal(before.DocumentRevision.Increment(),
            host.CaptureState().Session!.DocumentRevision);
    }

    [Fact]
    public async Task PropertiesElementNumberApplyUsesTypedCommandAndPreservesMultiSelection()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var alpha = new VisualStateId("demo:visual:alpha");
        var beta = new VisualStateId("demo:visual:beta");
        var gamma = new VisualStateId("demo:visual:gamma");
        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [alpha, beta, gamma]));
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(alpha));
        Assert.Equal(10L, DataIntegerValue(authoritative, ElementNumberFieldId));
        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        SetDataValue(draft, ElementNumberFieldId, "25");
        Assert.True(host.UpdatePropertiesFormState(true, alpha, isDirty: draft.IsDirty));
        Assert.True(session.TryCaptureDocumentSnapshot(out var beforeDocument));
        var before = host.CaptureState();
        var projection = Counters(host).ProjectionRuleInvocationCount;
        var layout = Counters(host).LayoutInvocationCount;
        var routing = Counters(host).RoutingInvocationCount;
        var scene = Counters(host).SceneContributionInvocationCount;

        var committed = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, committed.Status);
        Assert.Equal(25L, DataIntegerValue(committed.Authoritative, ElementNumberFieldId));
        Assert.Equal(
            DataEditorValue(authoritative, NameFieldId),
            DataEditorValue(committed.Authoritative, NameFieldId));
        Assert.Equal(
            DataEditorValue(authoritative, DescriptionFieldId),
            DataEditorValue(committed.Authoritative, DescriptionFieldId));
        var after = host.CaptureState();
        Assert.Equal(before.Session!.DocumentRevision.Increment(), after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1, after.Session.HistoryStatus.EntryCount);
        Assert.Equal(projection + 5, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layout + 1, Counters(host).LayoutInvocationCount);
        Assert.Equal(routing + 1, Counters(host).RoutingInvocationCount);
        Assert.Equal(scene + 1, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(before.SuccessfulRenderCount + 1, after.SuccessfulRenderCount);
        Assert.Equal(3, after.Session.EditorState.Selection.Length);
        Assert.Contains(alpha, after.Session.EditorState.Selection);
        Assert.Contains(beta, after.Session.EditorState.Selection);
        Assert.Contains(gamma, after.Session.EditorState.Selection);
        Assert.True(session.TryCaptureDocumentSnapshot(out var afterDocument));
        Assert.True(beforeDocument!.VisualModel.VisualStates.SequenceEqual(
            afterDocument!.VisualModel.VisualStates));
        Assert.Equal("Alpha", LabelContent(after.Session.CurrentScene!, alpha));

        await host.UndoAsync();

        Assert.True(host.TryCaptureSelectedProperties(alpha, out var undone));
        Assert.Equal(10L, DataIntegerValue(undone, ElementNumberFieldId));
        Assert.Equal(3, host.CaptureState().Session!.EditorState.Selection.Length);

        await host.RedoAsync();

        Assert.True(host.TryCaptureSelectedProperties(alpha, out var redone));
        Assert.Equal(25L, DataIntegerValue(redone, ElementNumberFieldId));
        Assert.Equal(3, host.CaptureState().Session!.EditorState.Selection.Length);
    }

    [Fact]
    public async Task PropertiesDescriptionApplyPreservesNewlinesAndUndoRedo()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        Assert.Equal(
            "Initial Alpha description",
            DataEditorValue(authoritative, DescriptionFieldId));
        const string multiline = "Review the order\nbefore approval.\r\nRetain this line.";
        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        SetDataValue(draft, DescriptionFieldId, multiline);
        Assert.True(host.UpdatePropertiesFormState(true, target, isDirty: draft.IsDirty));
        var before = host.CaptureState();

        var committed = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, committed.Status);
        Assert.Equal(multiline, DataEditorValue(committed.Authoritative, DescriptionFieldId));
        Assert.Equal(before.Session!.DocumentRevision.Increment(),
            host.CaptureState().Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus.EntryCount + 1,
            host.CaptureState().Session!.HistoryStatus.EntryCount);
        Assert.Equal("Alpha", LabelContent(host.CaptureState().Session!.CurrentScene!, target));

        await host.UndoAsync();

        Assert.True(host.TryCaptureSelectedProperties(target, out var undone));
        Assert.Equal(
            "Initial Alpha description",
            DataEditorValue(undone, DescriptionFieldId));

        await host.RedoAsync();

        Assert.True(host.TryCaptureSelectedProperties(target, out var redone));
        Assert.Equal(multiline, DataEditorValue(redone, DescriptionFieldId));
    }

    [Fact]
    public async Task InvalidElementNumberApplyPerformsNoPersistentOrPipelineWork()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        SetDataValue(draft, ElementNumberFieldId, "1.5");
        Assert.True(host.UpdatePropertiesFormState(true, target, isDirty: draft.IsDirty));
        var before = host.CaptureState();
        var projection = Counters(host).ProjectionRuleInvocationCount;
        var layout = Counters(host).LayoutInvocationCount;
        var routing = Counters(host).RoutingInvocationCount;
        var scene = Counters(host).SceneContributionInvocationCount;

        var result = await host.ApplyPropertiesAsync(draft);

        var after = host.CaptureState();
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.ValidationFailed, result.Status);
        Assert.Contains("Element number", result.Message, StringComparison.Ordinal);
        Assert.Equal(before.Session!.DocumentRevision, after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, after.Session.HistoryStatus);
        Assert.Equal(before.SuccessfulRenderCount, after.SuccessfulRenderCount);
        Assert.Equal(projection, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layout, Counters(host).LayoutInvocationCount);
        Assert.Equal(routing, Counters(host).RoutingInvocationCount);
        Assert.Equal(scene, Counters(host).SceneContributionInvocationCount);
        Assert.True(host.TryCaptureSelectedProperties(target, out var unchanged));
        Assert.Equal(10L, DataIntegerValue(unchanged, ElementNumberFieldId));
    }

    [Fact]
    public async Task PropertiesRejectCombinedNameAndBoundsDraftWithoutPersistentWork()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = DocumentCanvasPropertiesDraft.Format(authoritative.Bounds.X + 10d),
        };
        SetDataValue(draft, NameFieldId, "Alpha renamed");
        Assert.True(host.UpdatePropertiesFormState(true, target, isDirty: true));
        var before = host.CaptureState();

        var result = await host.ApplyPropertiesAsync(draft);

        var after = host.CaptureState();
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.ValidationFailed, result.Status);
        Assert.Contains("one Data field or visual bounds", result.Message,
            StringComparison.Ordinal);
        Assert.Equal(before.Session!.DocumentRevision, after.Session!.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, after.Session.HistoryStatus);
        Assert.Equal(before.SuccessfulRenderCount, after.SuccessfulRenderCount);
        Assert.Equal(
            DataEditorValue(authoritative, NameFieldId),
            DataEditorValue(
                Assert.IsType<DocumentCanvasPropertySnapshot>(
                    result.Authoritative ?? authoritative),
                NameFieldId));
        Assert.True(after.PropertiesFormDirty);
    }

    [Fact]
    public async Task PropertiesValidationAndCommandFailureKeepDraftAndPerformNoPersistentOrRuntimeWork()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var invalidDraft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            Width = "not-a-number",
        };
        Assert.True(host.UpdatePropertiesFormState(true, target, isDirty: true));
        var beforeValidation = host.CaptureState();
        var beforeValidationCounters = Counters(host);
        var beforeValidationProjection = beforeValidationCounters.ProjectionRuleInvocationCount;
        var beforeValidationLayout = beforeValidationCounters.LayoutInvocationCount;
        var beforeValidationRouting = beforeValidationCounters.RoutingInvocationCount;
        var beforeValidationScene = beforeValidationCounters.SceneContributionInvocationCount;

        var validation = await host.ApplyPropertiesAsync(invalidDraft);

        var afterValidation = host.CaptureState();
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.ValidationFailed, validation.Status);
        Assert.Equal("not-a-number", invalidDraft.Width);
        Assert.True(afterValidation.PropertiesFormOpen);
        Assert.True(afterValidation.PropertiesFormDirty);
        Assert.Equal(target, afterValidation.PropertiesTargetVisualStateId);
        Assert.Equal(beforeValidation.Session!.DocumentRevision,
            afterValidation.Session!.DocumentRevision);
        Assert.Equal(beforeValidation.Session.HistoryStatus, afterValidation.Session.HistoryStatus);
        Assert.Equal(beforeValidationProjection, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(beforeValidationLayout, Counters(host).LayoutInvocationCount);
        Assert.Equal(beforeValidationRouting, Counters(host).RoutingInvocationCount);
        Assert.Equal(beforeValidationScene, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(beforeValidation.SuccessfulRenderCount, afterValidation.SuccessfulRenderCount);

        var excessive = Math.Sqrt(double.MaxValue) / 8d;
        var failedDraft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            Width = DocumentCanvasPropertiesDraft.Format(excessive),
        };
        Assert.True(failedDraft.TryParseBounds(out _, out var validationMessages));
        Assert.Empty(validationMessages);
        Assert.True(host.UpdatePropertiesFormState(true, target, isDirty: true));
        var beforeFailure = host.CaptureState();
        var beforeFailureProjection = Counters(host).ProjectionRuleInvocationCount;
        var beforeFailureLayout = Counters(host).LayoutInvocationCount;
        var beforeFailureRouting = Counters(host).RoutingInvocationCount;
        var beforeFailureScene = Counters(host).SceneContributionInvocationCount;

        var failure = await host.ApplyPropertiesAsync(failedDraft);

        var afterFailure = host.CaptureState();
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Failed, failure.Status);
        Assert.Contains(failure.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(DocumentCanvasPropertiesDraft.Format(excessive), failedDraft.Width);
        Assert.True(afterFailure.PropertiesFormOpen);
        Assert.True(afterFailure.PropertiesFormDirty);
        Assert.Equal(target, afterFailure.PropertiesTargetVisualStateId);
        Assert.Equal(beforeFailure.Session!.DocumentRevision, afterFailure.Session!.DocumentRevision);
        Assert.Equal(beforeFailure.Session.HistoryStatus, afterFailure.Session.HistoryStatus);
        Assert.Equal(beforeFailureProjection, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(beforeFailureLayout, Counters(host).LayoutInvocationCount);
        Assert.Equal(beforeFailureRouting, Counters(host).RoutingInvocationCount);
        Assert.Equal(beforeFailureScene, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(beforeFailure.SuccessfulRenderCount, afterFailure.SuccessfulRenderCount);
        Assert.True(host.TryCaptureSelectedProperties(target, out var unchanged));
        Assert.Equal(authoritative.Bounds,
            Assert.IsType<DocumentCanvasPropertySnapshot>(unchanged).Bounds);
    }

    [Fact]
    public async Task DirtyPropertiesBlockHistoryZoomAndCanvasWhileStaleApplyKeepsDraftState()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var target = new VisualStateId("demo:visual:alpha");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(target));
        var draft = new DocumentCanvasPropertiesDraft(authoritative) { X = "88" };
        host.UpdatePropertiesFormState(true, target, isDirty: true);
        var beta = AttachedDocument(session).VisualModel.VisualStates.Single(item =>
            item.Id.Value == "demo:visual:beta");
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            session.CaptureState().DocumentId,
            session.CaptureState().DocumentRevision,
            beta.Id,
            beta.Position + new VectorD(5d, 5d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync();
        var beforeBlocked = host.CaptureState();
        var scene = beforeBlocked.Session!.CurrentScene!;
        var betaItem = scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content && item.Origin.VisualStateId == beta.Id);

        await host.UndoAsync();
        await host.ZoomInAsync();
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(betaItem.Bounds));
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(betaItem.Bounds));

        var blocked = host.CaptureState();
        Assert.Equal(beforeBlocked.Session.DocumentRevision, blocked.Session!.DocumentRevision);
        Assert.Equal(beforeBlocked.Session.EditorState.Viewport, blocked.Session.EditorState.Viewport);
        Assert.Equal(target, Assert.Single(blocked.Session.EditorState.Selection));
        Assert.Null(blocked.ContextMenu);

        var stale = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Stale, stale.Status);
        Assert.True(host.CaptureState().PropertiesFormOpen);
        Assert.True(host.CaptureState().PropertiesFormDirty);
    }

    [Fact]
    public async Task PropertiesApplyCannotCommitAfterTheSoleSelectionChanges()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var alpha = new VisualStateId("demo:visual:alpha");
        var beta = new VisualStateId("demo:visual:beta");
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(alpha));
        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = DocumentCanvasPropertiesDraft.Format(authoritative.Bounds.X + 20d),
        };
        host.UpdatePropertiesFormState(true, alpha, isDirty: true);
        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [beta]));
        var before = session.CaptureState();

        var result = await host.ApplyPropertiesAsync(draft);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Unavailable, result.Status);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task PropertiesFormPreservesTargetInMultiSelectionAndSwitchesOrClosesWhenRemoved()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var alpha = new VisualStateId("demo:visual:alpha");
        var beta = new VisualStateId("demo:visual:beta");
        Assert.NotNull(await host.OpenPropertiesAsync(alpha));

        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [beta]));

        Assert.True(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(beta, host.CaptureState().PropertiesTargetVisualStateId);

        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [alpha, beta]));

        Assert.True(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(beta, host.CaptureState().PropertiesTargetVisualStateId);

        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [alpha]));

        Assert.True(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(alpha, host.CaptureState().PropertiesTargetVisualStateId);

        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            []));

        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.Null(host.CaptureState().PropertiesTargetVisualStateId);
    }

    [Fact]
    public async Task StaleComponentCannotReinstateOrClosePropertiesForAFormerSelection()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var alpha = new VisualStateId("demo:visual:alpha");
        var beta = new VisualStateId("demo:visual:beta");
        Assert.NotNull(await host.OpenPropertiesAsync(alpha));
        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [beta]));
        var switched = host.CaptureState();
        Assert.True(switched.PropertiesFormOpen);
        Assert.Equal(beta, switched.PropertiesTargetVisualStateId);

        var accepted = host.UpdatePropertiesFormState(
            isOpen: true,
            alpha,
            isDirty: true);

        Assert.False(accepted);
        Assert.True(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(beta, host.CaptureState().PropertiesTargetVisualStateId);
        Assert.True(host.TryCaptureCurrentPropertiesForm(out var current));
        Assert.Equal(beta, current?.VisualStateId);

        var gamma = new VisualStateId("demo:visual:gamma");
        await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [gamma]));

        Assert.True(host.TryCaptureCurrentPropertiesForm(out current));
        Assert.Equal(gamma, current?.VisualStateId);
        Assert.True(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(gamma, host.CaptureState().PropertiesTargetVisualStateId);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(2d)]
    public async Task ToolbarZoomPreservesCssCentreAndUsesOnlySceneInvalidation(
        double devicePixelRatio)
    {
        var execution = new RecordingRenderExecution();
        var surface = new Canvas2DSurfaceSize(720d, 480d, devicePixelRatio);
        var observer = new RecordingSurfaceObserver(surface);
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var projectionCount = Counters(host).ProjectionRuleInvocationCount;
        var layoutCount = Counters(host).LayoutInvocationCount;
        var routingCount = Counters(host).RoutingInvocationCount;
        var sceneCount = Counters(host).SceneContributionInvocationCount;
        var beforeRenderCount = host.CaptureState().SuccessfulRenderCount;
        var cssCenter = new PointD(surface.CssWidth / 2d, surface.CssHeight / 2d);
        var documentAnchor = Canvas2DRenderer.ConvertCssToDocument(
            before.CurrentScene!,
            cssCenter);

        await host.ZoomInAsync();
        var after = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.Equal(1.18d, after.EditorState.Viewport.Zoom);
        Assert.Equal(
            cssCenter,
            after.CurrentScene!.ViewportTransform.TransformPoint(documentAnchor));
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.True(before.EditorState.Selection.AsSpan().SequenceEqual(
            after.EditorState.Selection.AsSpan()));
        Assert.Equal(before.EditorState.HoveredObjectId, after.EditorState.HoveredObjectId);
        Assert.Equal(
            projectionCount,
            Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(
            layoutCount,
            Counters(host).LayoutInvocationCount);
        Assert.Equal(
            routingCount,
            Counters(host).RoutingInvocationCount);
        Assert.Equal(
            sceneCount + 1,
            Counters(host).SceneContributionInvocationCount);
        Assert.Equal(beforeRenderCount + 1, host.CaptureState().SuccessfulRenderCount);
    }

    [Theory]
    [InlineData(1d, 0d, 0d)]
    [InlineData(1.25d, 40d, 25d)]
    [InlineData(0.8d, -35d, -20d)]
    [InlineData(1.37d, 12.5d, -7.25d)]
    public async Task ToolbarZoomPreservesCssCentreAcrossCanonicalPanAndZoomInputs(
        double startingZoom,
        double panX,
        double panY)
    {
        var execution = new RecordingRenderExecution();
        var surface = new Canvas2DSurfaceSize(735d, 485d, 1.25d);
        var observer = new RecordingSurfaceObserver(surface);
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var currentViewport = session.CaptureState().EditorState.Viewport;
        var installed = await session.UpdateViewportAsync(new ViewportSnapshot(
            startingZoom,
            new VectorD(panX, panY),
            currentViewport.VisibleDocumentRegion));
        Assert.True(installed.Succeeded);
        var before = session.CaptureState();
        var cssCenter = new PointD(surface.CssWidth / 2d, surface.CssHeight / 2d);
        var documentAnchor = Canvas2DRenderer.ConvertCssToDocument(
            before.CurrentScene!,
            cssCenter);

        await host.ZoomInAsync();
        var after = session.CaptureState();

        Assert.Equal(DocumentCanvasZoomPolicy.ZoomIn(startingZoom), after.EditorState.Viewport.Zoom);
        Assert.Equal(
            cssCenter.X,
            after.CurrentScene!.ViewportTransform.TransformPoint(documentAnchor).X,
            precision: 10);
        Assert.Equal(
            cssCenter.Y,
            after.CurrentScene.ViewportTransform.TransformPoint(documentAnchor).Y,
            precision: 10);
    }

    [Fact]
    public async Task ToolbarZoomUsesLatestResizedCssCentreAndActualSizeIsAnExactNoOp()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(640d, 360d, 1.25d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var resized = new Canvas2DSurfaceSize(860d, 520d, 2d);
        await observer.RaiseAsync(resized);
        var before = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var cssCenter = new PointD(resized.CssWidth / 2d, resized.CssHeight / 2d);
        var documentAnchor = Canvas2DRenderer.ConvertCssToDocument(
            before.CurrentScene!,
            cssCenter);

        await host.SetZoomToActualSizeAsync();
        var actualSize = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal(1d, actualSize.EditorState.Viewport.Zoom);
        Assert.Equal(
            cssCenter,
            actualSize.CurrentScene!.ViewportTransform.TransformPoint(documentAnchor));
        var projectionCount = Counters(host).ProjectionRuleInvocationCount;
        var layoutCount = Counters(host).LayoutInvocationCount;
        var routingCount = Counters(host).RoutingInvocationCount;
        var sceneCount = Counters(host).SceneContributionInvocationCount;
        var renders = host.CaptureState().SuccessfulRenderCount;

        await host.SetZoomToActualSizeAsync();

        Assert.Equal(projectionCount, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layoutCount, Counters(host).LayoutInvocationCount);
        Assert.Equal(routingCount, Counters(host).RoutingInvocationCount);
        Assert.Equal(sceneCount, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(renders, host.CaptureState().SuccessfulRenderCount);
        Assert.Equal(resized, host.CaptureState().SurfaceSize);
    }

    [Fact]
    public async Task ToolbarZoomOutChangesOnceAndRequestsAtBothLimitsAreExactNoOps()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1.25d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);

        var beforeSceneCount = Counters(host).SceneContributionInvocationCount;
        var beforeRenderCount = host.CaptureState().SuccessfulRenderCount;
        await host.ZoomOutAsync();
        Assert.Equal(0.98d, session.CaptureState().EditorState.Viewport.Zoom);
        Assert.Equal(beforeSceneCount + 1, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(beforeRenderCount + 1, host.CaptureState().SuccessfulRenderCount);

        var current = session.CaptureState().EditorState.Viewport;
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(
            0.25d,
            current.Pan,
            current.VisibleDocumentRegion))).Succeeded);
        var minimumSceneCount = Counters(host).SceneContributionInvocationCount;
        var minimumRenderCount = host.CaptureState().SuccessfulRenderCount;
        await host.ZoomOutAsync();
        Assert.Equal(0.25d, session.CaptureState().EditorState.Viewport.Zoom);
        Assert.Equal(minimumSceneCount, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(minimumRenderCount, host.CaptureState().SuccessfulRenderCount);

        current = session.CaptureState().EditorState.Viewport;
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(
            4d,
            current.Pan,
            current.VisibleDocumentRegion))).Succeeded);
        var maximumSceneCount = Counters(host).SceneContributionInvocationCount;
        var maximumRenderCount = host.CaptureState().SuccessfulRenderCount;
        await host.ZoomInAsync();
        Assert.Equal(4d, session.CaptureState().EditorState.Viewport.Zoom);
        Assert.Equal(maximumSceneCount, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(maximumRenderCount, host.CaptureState().SuccessfulRenderCount);
    }

    [Theory]
    [InlineData(2.4d, 0d, (int)CanvasWheelDeltaMode.Pixel)]
    [InlineData(0d, 7.8d, (int)CanvasWheelDeltaMode.Pixel)]
    [InlineData(2.4d, 7.8d, (int)CanvasWheelDeltaMode.Pixel)]
    [InlineData(1.5d, -2.25d, (int)CanvasWheelDeltaMode.Line)]
    [InlineData(0.25d, 0.5d, (int)CanvasWheelDeltaMode.Page)]
    public async Task OrdinaryWheelPansViewportOnlyWithSmoothNormalizedCssTranslation(
        double deltaX,
        double deltaY,
        int deltaModeValue)
    {
        var deltaMode = (CanvasWheelDeltaMode)deltaModeValue;
        var execution = new RecordingRenderExecution();
        var surfaceSize = new Canvas2DSurfaceSize(720d, 480d, 1.25d);
        var observer = new RecordingSurfaceObserver(surfaceSize);
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var documentBefore = document.CaptureSnapshot();
        var before = session.CaptureState();
        var projectionCount = Counters(host).ProjectionRuleInvocationCount;
        var layoutCount = Counters(host).LayoutInvocationCount;
        var routingCount = Counters(host).RoutingInvocationCount;
        var expectedTranslation = new CanvasWheelInput(
            deltaX,
            deltaY,
            deltaMode,
            false,
            false).ResolveCanvasTranslation(surfaceSize);

        await PointerObserver(host).WheelAsync(deltaX, deltaY, deltaMode);
        var after = session.CaptureState();

        Assert.Equal(before.EditorState.Viewport.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(before.EditorState.Viewport.Pan + expectedTranslation,
            after.EditorState.Viewport.Pan);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.True(before.EditorState.Selection.AsSpan().SequenceEqual(
            after.EditorState.Selection.AsSpan()));
        Assert.Equal(projectionCount, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layoutCount, Counters(host).LayoutInvocationCount);
        Assert.Equal(routingCount, Counters(host).RoutingInvocationCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ModifiedWheelIsNotConsumedAsViewportPan(bool controlKey, bool metaKey)
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = Session(host).CaptureState();

        await PointerObserver(host).WheelAsync(
            12d,
            34d,
            controlKey: controlKey,
            metaKey: metaKey);
        var after = Session(host).CaptureState();

        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
    }

    [Fact]
    public async Task WheelCannotPanThroughPropertiesOrAnOwnedContextMenu()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var initial = session.CaptureState();
        var task = initial.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        var pan = initial.EditorState.Viewport.Pan;

        await PointerObserver(host).ClickDocumentPointAsync(
            initial.CurrentScene,
            Center(task.Bounds));
        Assert.Equal(BpmnDemoPipeline.TaskVisualId,
            Assert.Single(session.CaptureState().EditorState.Selection));
        Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));
        await PointerObserver(host).WheelAsync(20d, 30d);
        Assert.Equal(pan, session.CaptureState().EditorState.Viewport.Pan);
        Assert.True(host.UpdatePropertiesFormState(false, null, isDirty: false));

        await PointerObserver(host).ContextMenuDocumentPointAsync(
            session.CaptureState().CurrentScene!,
            Center(task.Bounds));
        Assert.NotNull(host.CaptureState().ContextMenu);
        await PointerObserver(host).WheelAsync(20d, 30d);
        Assert.Equal(pan, session.CaptureState().EditorState.Viewport.Pan);
    }

    [Fact]
    public async Task MiddlePanCancelClearsTransientGestureAndRestoresCursor()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var pointer = PointerObserver(host);
        var initialPan = Session(host).CaptureState().EditorState.Viewport.Pan;

        await pointer.MiddleDownCssPointAsync(new PointD(100d, 100d), pointerId: 41);
        Assert.Equal("grabbing", host.CaptureState().CssCursor);
        await pointer.CancelAsync(pointerId: 41);
        Assert.Equal("default", host.CaptureState().CssCursor);

        await pointer.MiddleDownCssPointAsync(new PointD(100d, 100d), pointerId: 42);
        await pointer.MiddleMoveCssPointAsync(new PointD(125d, 130d), pointerId: 42);
        await pointer.MiddleUpCssPointAsync(new PointD(125d, 130d), pointerId: 42);
        Assert.Equal(initialPan + new VectorD(25d, 30d),
            Session(host).CaptureState().EditorState.Viewport.Pan);
        Assert.Equal("default", host.CaptureState().CssCursor);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(2d)]
    public async Task MiddleDragOverNodePansByCssDeltaWithoutEditingOrChangingSelection(
        double devicePixelRatio)
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(900d, 600d, devicePixelRatio));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = session.CaptureState();
        var documentBefore = document.CaptureSnapshot();
        var task = before.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        var start = before.CurrentScene.ViewportTransform.TransformPoint(Center(task.Bounds));
        var delta = new VectorD(100.5d, 50.25d);

        await PointerObserver(host).MiddleDownCssPointAsync(start);
        Assert.Equal("grabbing", host.CaptureState().CssCursor);
        await PointerObserver(host).MiddleMoveCssPointAsync(start + delta);
        await PointerObserver(host).MiddleUpCssPointAsync(start + delta);
        var after = session.CaptureState();

        Assert.Equal(before.EditorState.Viewport.Pan + delta, after.EditorState.Viewport.Pan);
        Assert.Equal(before.EditorState.Viewport.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.True(before.EditorState.Selection.AsSpan().SequenceEqual(
            after.EditorState.Selection.AsSpan()));
        Assert.Equal("default", host.CaptureState().CssCursor);
    }

    [Fact]
    public async Task ActiveDiagramGestureExcludesWheelAndRejectsMiddlePanPriorityChange()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var task = before.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        var center = Center(task.Bounds);

        await PointerObserver(host).DownDocumentPointAsync(before.CurrentScene, center, pointerId: 3);
        await PointerObserver(host).MoveDocumentPointAsync(
            before.CurrentScene,
            center + new VectorD(30d, 20d),
            pointerId: 3,
            buttons: 1);
        var active = session.CaptureState();
        Assert.NotNull(active.EditorState.ActiveGesture);
        var panBefore = active.EditorState.Viewport.Pan;

        await PointerObserver(host).WheelAsync(25d, 40d);
        await PointerObserver(host).MiddleDownCssPointAsync(new PointD(100d, 100d), pointerId: 8);

        Assert.Equal(panBefore, session.CaptureState().EditorState.Viewport.Pan);
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        Assert.Contains(8L, PointerObserver(host).ReleasedCaptureGenerations);
        await PointerObserver(host).CancelAsync(3);
    }

    [Fact]
    public async Task ToolbarHistoryUsesSessionUndoRedoAndReflectsCanonicalAvailability()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var visual = document.VisualModel.VisualStates.Single(item =>
            item.Id.Value == "demo:visual:beta");
        var original = visual.Position;
        var moved = original + new VectorD(35d, 20d);

        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            moved,
            VisualPlacementMode.Pinned));
        Assert.True(command.IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(host.CaptureState().Session!.HistoryStatus.CanUndo);
        Assert.False(host.CaptureState().Session!.HistoryStatus.CanRedo);

        await host.UndoAsync();
        Assert.Equal(original, document.VisualModel.VisualStates.Single(item =>
            item.Id == visual.Id).Position);
        Assert.False(host.CaptureState().Session!.HistoryStatus.CanUndo);
        Assert.True(host.CaptureState().Session!.HistoryStatus.CanRedo);

        await host.RedoAsync();
        Assert.Equal(moved, document.VisualModel.VisualStates.Single(item =>
            item.Id == visual.Id).Position);
        Assert.True(host.CaptureState().Session!.HistoryStatus.CanUndo);
        Assert.False(host.CaptureState().Session!.HistoryStatus.CanRedo);
    }

    [Fact]
    public async Task ToolbarOperationsAreRejectedWhilePersistentGestureIsActive()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var alpha = document.VisualModel.VisualStates.Single(item =>
            item.Id.Value == "demo:visual:alpha");
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            alpha.Id,
            alpha.Position + new VectorD(10d, 5d),
            VisualPlacementMode.Pinned))).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var ready = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var resizeZone = ResizeZone(ready.CurrentScene!, "southeast");
        await PointerObserver(host).DownDocumentPointAsync(
            ready.CurrentScene!,
            Center(resizeZone.Bounds),
            pointerId: 99);
        var active = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.NotNull(active.EditorState.ActiveGesture);
        var projectionCount = Counters(host).ProjectionRuleInvocationCount;
        var layoutCount = Counters(host).LayoutInvocationCount;
        var routingCount = Counters(host).RoutingInvocationCount;
        var sceneCount = Counters(host).SceneContributionInvocationCount;

        await host.UndoAsync();
        await host.RedoAsync();
        await host.ZoomInAsync();
        await host.ZoomOutAsync();
        await host.SetZoomToActualSizeAsync();

        var unchanged = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal(active.DocumentRevision, unchanged.DocumentRevision);
        Assert.Equal(active.HistoryStatus, unchanged.HistoryStatus);
        Assert.Equal(active.EditorState.Viewport, unchanged.EditorState.Viewport);
        Assert.Equal(projectionCount, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layoutCount, Counters(host).LayoutInvocationCount);
        Assert.Equal(routingCount, Counters(host).RoutingInvocationCount);
        Assert.Equal(sceneCount, Counters(host).SceneContributionInvocationCount);
        Assert.NotNull(unchanged.EditorState.ActiveGesture);
        await PointerObserver(host).CancelAsync(pointerId: 99);
    }

    [Theory]
    [InlineData(1.08d, 1.18d, 0.98d, 108)]
    [InlineData(0.25d, 0.35d, 0.25d, 25)]
    [InlineData(4d, 4d, 3.9d, 400)]
    [InlineData(0.1d, 0.25d, 0.25d, 10)]
    [InlineData(5d, 4d, 4d, 500)]
    public void ZoomPolicyUsesDeterministicIntegerPercentagesAndCanonicalLimits(
        double current,
        double expectedIn,
        double expectedOut,
        int expectedPercentage)
    {
        Assert.Equal(expectedPercentage, DocumentCanvasZoomPolicy.ToPercentage(current));
        Assert.Equal(expectedIn, DocumentCanvasZoomPolicy.ZoomIn(current));
        Assert.Equal(expectedOut, DocumentCanvasZoomPolicy.ZoomOut(current));
    }

    [Fact]
    public async Task PointerInputUpdatesOnlyEditorSceneAndPresentationForChangedTargets()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var graph = before.ProjectedGraph;
        var layout = before.LayoutResult;
        var routing = before.RoutingResult;
        var history = before.HistoryStatus;
        var revision = before.DocumentRevision;
        var beta = before.CurrentScene!.Items.Last(item =>
            item.Origin.SemanticElementId?.Value == "demo:beta" &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var gamma = before.CurrentScene.Items.Last(item =>
            item.Origin.SemanticElementId?.Value == "demo:gamma" &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        Assert.Equal(
            beta.Id,
            new Canvas2DSceneHitTestService()
                .HitTest(before.CurrentScene, Center(beta.Bounds))?.SceneObjectId);

        await PointerObserver(host).MoveDocumentPointAsync(before.CurrentScene, Center(beta.Bounds));
        var afterHover = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.True(
            beta.Id == afterHover.EditorState.HoveredObjectId,
            string.Join(Environment.NewLine, host.CaptureState().InteractionDiagnostics.Select(
                static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Same(graph, afterHover.ProjectedGraph);
        Assert.Same(layout, afterHover.LayoutResult);
        Assert.Same(routing, afterHover.RoutingResult);
        Assert.Equal(history, afterHover.HistoryStatus);
        Assert.Equal(revision, afterHover.DocumentRevision);
        Assert.Equal(2, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));

        await PointerObserver(host).MoveDocumentPointAsync(
            afterHover.CurrentScene!,
            Center(beta.Bounds));
        Assert.Equal(2, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));

        await PointerObserver(host).ClickDocumentPointAsync(
            afterHover.CurrentScene!,
            Center(gamma.Bounds));
        var afterSelection = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal(gamma.Origin.VisualStateId, Assert.Single(afterSelection.EditorState.Selection));
        Assert.Null(afterSelection.EditorState.ActiveGesture);
        Assert.Equal(3, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(3, execution.Calls.Count(call => call == "render"));

        await PointerObserver(host).ClickDocumentPointAsync(
            afterSelection.CurrentScene!,
            new PointD(-1000d, -1000d));
        var afterClear = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Empty(afterClear.EditorState.Selection);

        await PointerObserver(host).LeaveAsync();
        var afterLeave = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Null(afterLeave.EditorState.HoveredObjectId);
        Assert.Equal(5, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(5, execution.Calls.Count(call => call == "render"));
        Assert.Equal(5, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(1, Counters(host).LayoutInvocationCount);
        Assert.Equal(1, Counters(host).RoutingInvocationCount);
    }

    [Fact]
    public async Task ControlClickTogglesCanonicalVisualSelectionWithoutPersistentWork()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var pointer = PointerObserver(host);
        var revision = initial.DocumentRevision;
        var history = initial.HistoryStatus;
        var projectionCount = Counters(host).ProjectionRuleInvocationCount;
        var layoutCount = Counters(host).LayoutInvocationCount;
        var routingCount = Counters(host).RoutingInvocationCount;

        await ClickAsync("demo:beta", controlKey: false);
        AssertSelection("demo:visual:beta");

        await ClickAsync("demo:gamma", controlKey: true);
        AssertSelection("demo:visual:beta", "demo:visual:gamma");

        await ClickAsync("demo:beta", controlKey: true);
        AssertSelection("demo:visual:gamma");

        await ClickAsync("demo:gamma", controlKey: true);
        AssertSelection();

        var beforeEmpty = host.CaptureState();
        await pointer.ClickDocumentPointAsync(
            beforeEmpty.Session!.CurrentScene!,
            new PointD(-1000d, -1000d),
            controlKey: true);
        var afterEmpty = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.Empty(afterEmpty.EditorState.Selection);
        Assert.Equal(beforeEmpty.SuccessfulRenderCount, host.CaptureState().SuccessfulRenderCount);
        Assert.Equal(beforeEmpty.PipelineCounters?.SceneContributionInvocationCount,
            host.CaptureState().PipelineCounters?.SceneContributionInvocationCount);
        Assert.Equal(revision, afterEmpty.DocumentRevision);
        Assert.Equal(history, afterEmpty.HistoryStatus);
        Assert.Equal(projectionCount, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layoutCount, Counters(host).LayoutInvocationCount);
        Assert.Equal(routingCount, Counters(host).RoutingInvocationCount);

        async Task ClickAsync(string semanticId, bool controlKey)
        {
            var scene = host.CaptureState().Session!.CurrentScene!;
            var target = scene.Items.Last(item =>
                item.Origin.SemanticElementId?.Value == semanticId &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Label);
            await pointer.ClickDocumentPointAsync(scene, Center(target.Bounds), controlKey);
        }

        void AssertSelection(params string[] expected)
        {
            var actual = host.CaptureState().Session!.EditorState.Selection
                .Select(static id => id.Value)
                .ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task ResizeCursorFlowsAsFinalScalarWithoutRedundantBrowserUpdates()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var pointer = PointerObserver(host);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["north"] = "ns-resize",
            ["south"] = "ns-resize",
            ["east"] = "ew-resize",
            ["west"] = "ew-resize",
            ["northeast"] = "nesw-resize",
            ["southwest"] = "nesw-resize",
            ["northwest"] = "nwse-resize",
            ["southeast"] = "nwse-resize",
        };

        foreach (var (role, cssCursor) in expected)
        {
            var scene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
                host.CaptureState().Session?.CurrentScene);
            var handle = ResizeZone(scene, role);
            await pointer.MoveDocumentPointAsync(scene, Center(handle.Bounds));
            await WaitForCursorAsync(host, cssCursor);

            Assert.Equal(cssCursor, pointer.CursorValues[^1]);
            var updateCount = pointer.CursorValues.Count;
            var currentScene = host.CaptureState().Session!.CurrentScene!;
            var currentHandle = ResizeZone(currentScene, role);
            await pointer.MoveDocumentPointAsync(currentScene, Center(currentHandle.Bounds));
            Assert.Equal(updateCount, pointer.CursorValues.Count);
        }

        await pointer.LeaveAsync();
        await WaitForCursorAsync(host, "default");
        Assert.Equal("default", pointer.CursorValues[^1]);

        var activeScene = host.CaptureState().Session!.CurrentScene!;
        var southEast = ResizeHandle(activeScene, "southeast");
        await pointer.DownDocumentPointAsync(activeScene, Center(southEast.Bounds), pointerId: 72);
        await WaitForCursorAsync(host, "nwse-resize");
        var cursorUpdates = pointer.CursorValues.Count;
        var pressed = host.CaptureState().Session!.CurrentScene!;
        await pointer.MoveDocumentPointAsync(
            pressed,
            Center(southEast.Bounds) + new VectorD(20d, 15d),
            pointerId: 72,
            buttons: 1);
        Assert.Equal(cursorUpdates, pointer.CursorValues.Count);

        await pointer.CancelAsync(pointerId: 72);
        await WaitForCursorAsync(host, "default");
        Assert.Null(host.CaptureState().Session!.EditorState.ActiveGesture);
        Assert.Equal(0, pointer.ReleaseCaptureCount);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(2d)]
    public async Task ResizeGestureLogicalBoundsAreIndependentOfDevicePixelRatio(
        double devicePixelRatio)
    {
        var execution = new RecordingRenderExecution();
        var surface = new Canvas2DSurfaceSize(720d, 480d, devicePixelRatio);
        var observer = new RecordingSurfaceObserver(surface);
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var initialScene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
            initial.CurrentScene);
        var handle = ResizeHandle(initialScene, "southeast");
        var targetId = Assert.Single(handle.Origin.RelatedSceneObjectIds);
        var target = initialScene.Items.Single(item => item.Id == targetId);
        var pointer = PointerObserver(host);
        const long pointerId = 73;
        var start = Center(handle.Bounds);
        var end = start + new VectorD(12.5d, 8.25d);
        var expected = new RectD(60d, 70d, 162.5d, 78.25d);

        Assert.Equal(new RectD(60d, 70d, 150d, 70d), target.Bounds);
        await pointer.DownDocumentPointAsync(initialScene, start, pointerId);
        var pressed = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        await pointer.MoveDocumentPointAsync(
            pressed.CurrentScene!,
            end,
            pointerId,
            buttons: 1);
        var previewed = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.Equal(expected, ResizePreview(previewed.CurrentScene!, targetId).Bounds);

        await pointer.UpDocumentPointAsync(previewed.CurrentScene!, end, pointerId);
        await Session(host).WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committed = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.Equal(surface, host.CaptureState().SurfaceSize);
        Assert.Equal(expected, committed.CurrentScene!.Items.Single(item => item.Id == targetId).Bounds);
    }

    [Fact]
    public async Task NormalizedPointerGestureIsForwardedAndCancellationRemainsTransient()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var target = before.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId?.Value == "demo:alpha" &&
            item.Origin.VisualStateId is not null &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var pointer = PointerObserver(host);

        await pointer.DownDocumentPointAsync(before.CurrentScene, Center(target.Bounds), pointerId: 42);
        var pressed = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Null(pressed.EditorState.ActiveGesture);

        var movedPoint = Center(target.Bounds) + new VectorD(18d, 12d);
        await pointer.MoveDocumentPointAsync(
            pressed.CurrentScene!,
            movedPoint,
            pointerId: 42,
            buttons: 1);
        var moved = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal("inceptus.canvas2d:move", moved.EditorState.ActiveGesture?.Kind);
        Assert.Equal(target.Origin.VisualStateId, Assert.Single(moved.EditorState.Selection));
        var actualPoint = Assert.IsType<PointD>(moved.EditorState.ActiveGesture?.Current);
        Assert.Equal(movedPoint.X, actualPoint.X, precision: 10);
        Assert.Equal(movedPoint.Y, actualPoint.Y, precision: 10);

        await pointer.CancelAsync(pointerId: 42);
        var cancelled = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Null(cancelled.EditorState.ActiveGesture);
        Assert.Equal(before.DocumentRevision, cancelled.DocumentRevision);
        Assert.Equal(before.HistoryStatus, cancelled.HistoryStatus);
        Assert.Same(before.ProjectedGraph, cancelled.ProjectedGraph);
        Assert.Same(before.LayoutResult, cancelled.LayoutResult);
        Assert.Same(before.RoutingResult, cancelled.RoutingResult);
        Assert.Equal(5, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(1, Counters(host).LayoutInvocationCount);
        Assert.Equal(1, Counters(host).RoutingInvocationCount);
        Assert.Equal(3, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(3, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task SuccessfulMovePointerUpPreservesControllerGrabCursorAfterRevisionChange()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var target = MovableLabel(initial);
        var pointer = PointerObserver(host);
        const long pointerId = 87;
        var start = Center(target.Bounds);
        var end = start + new VectorD(18d, 12d);

        await pointer.DownDocumentPointAsync(initial.CurrentScene!, start, pointerId);
        await pointer.MoveDocumentPointAsync(
            initial.CurrentScene!,
            end,
            pointerId,
            buttons: 1);
        await WaitForCursorAsync(host, "grabbing");
        var preview = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        await pointer.UpDocumentPointAsync(preview.CurrentScene!, end, pointerId);
        await Session(host).WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForCursorAsync(host, "grab");
        await Task.Delay(25);

        var committed = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal(initial.DocumentRevision.Increment(), committed.DocumentRevision);
        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal("grab", host.CaptureState().CssCursor);
        Assert.Equal("grab", pointer.CursorValues[^1]);
    }

    [Fact]
    public async Task SuccessfulThreeObjectMoveKeepsGrabAfterDelayedFullRebuildNotification()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var initial = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var pointer = PointerObserver(host);

        await ControlClickAsync("demo:beta");
        await ControlClickAsync("demo:gamma");
        var selected = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal(
            ["demo:visual:alpha", "demo:visual:beta", "demo:visual:gamma"],
            selected.EditorState.Selection.Select(static item => item.Value));
        var beta = selected.CurrentScene!.Items.Last(item =>
            item.Origin.SemanticElementId?.Value == "demo:beta" &&
            item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Label &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        const long pointerId = 88;
        var start = Center(beta.Bounds);
        var end = start + new VectorD(50d, 30d);

        await pointer.DownDocumentPointAsync(selected.CurrentScene, start, pointerId);
        await pointer.MoveDocumentPointAsync(
            selected.CurrentScene,
            end,
            pointerId,
            buttons: 1);
        await WaitForCursorAsync(host, "grabbing");
        var preview = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        await pointer.UpDocumentPointAsync(preview.CurrentScene!, end, pointerId);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForCursorAsync(host, "grab");
        await Task.Yield();

        var committed = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal(initial.DocumentRevision.Increment(), committed.DocumentRevision);
        Assert.Equal(1, committed.HistoryStatus.EntryCount);
        Assert.Equal(
            ["demo:visual:alpha", "demo:visual:beta", "demo:visual:gamma"],
            committed.EditorState.Selection.Select(static item => item.Value));
        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal("grab", host.CaptureState().CssCursor);
        Assert.Equal("grab", pointer.CursorValues[^1]);

        async Task ControlClickAsync(string semanticId)
        {
            var scene = host.CaptureState().Session!.CurrentScene!;
            var target = scene.Items.Last(item =>
                item.Origin.SemanticElementId?.Value == semanticId &&
                item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Label &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
            await pointer.ClickDocumentPointAsync(
                scene,
                Center(target.Bounds),
                controlKey: true);
        }
    }

    [Fact]
    public async Task OverflowingMoveAndUpCancelGestureWithoutPersistentEffects()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var session = Session(host);
        var pointer = PointerObserver(host);
        var initialObservedEventRevision = ObservedEventRevision(session);
        var initialProjectionCount = Counters(host).ProjectionRuleInvocationCount;
        var initialLayoutCount = Counters(host).LayoutInvocationCount;
        var initialRoutingCount = Counters(host).RoutingInvocationCount;

        var firstTarget = MovableLabel(initial);
        await pointer.DownDocumentPointAsync(
            initial.CurrentScene!,
            Center(firstTarget.Bounds),
            pointerId: 91);
        await pointer.MoveDocumentPointAsync(
            initial.CurrentScene!,
            Center(firstTarget.Bounds) + new VectorD(12d, 8d),
            pointerId: 91,
            buttons: 1);
        Assert.NotNull(host.CaptureState().Session?.EditorState.ActiveGesture);
        await pointer.InvalidNormalizedAsync(CanvasPointerEventKind.Move, pointerId: 91);
        await WaitForReleaseCountAsync(pointer, 1);
        var afterMove = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.Null(afterMove.EditorState.ActiveGesture);
        Assert.Equal(initial.DocumentRevision, afterMove.DocumentRevision);
        Assert.Equal(initial.HistoryStatus, afterMove.HistoryStatus);
        Assert.Equal(initialObservedEventRevision, ObservedEventRevision(session));
        Assert.Contains(host.CaptureState().InteractionDiagnostics, diagnostic =>
            diagnostic.Code == "CANVAS_HOST_POINTER_COORDINATE_INVALID");

        var secondTarget = MovableLabel(afterMove);
        await pointer.DownDocumentPointAsync(
            afterMove.CurrentScene!,
            Center(secondTarget.Bounds),
            pointerId: 92);
        await pointer.MoveDocumentPointAsync(
            afterMove.CurrentScene!,
            Center(secondTarget.Bounds) + new VectorD(12d, 8d),
            pointerId: 92,
            buttons: 1);
        Assert.NotNull(host.CaptureState().Session?.EditorState.ActiveGesture);
        await pointer.InvalidNormalizedAsync(CanvasPointerEventKind.Up, pointerId: 92);
        await WaitForCursorAsync(host, "default");
        var afterUp = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.Null(afterUp.EditorState.ActiveGesture);
        Assert.Equal(initial.DocumentRevision, afterUp.DocumentRevision);
        Assert.Equal(initial.HistoryStatus, afterUp.HistoryStatus);
        Assert.Equal(initialObservedEventRevision, ObservedEventRevision(session));
        Assert.Equal(initialProjectionCount, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, Counters(host).LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, Counters(host).RoutingInvocationCount);
        Assert.Equal(1, pointer.ReleaseCaptureCount);
    }

    [Fact]
    public async Task CommittedDocumentInvalidationClearsGestureAndReleasesBrowserCapture()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var scene = Assert.IsType<global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene>(
            before.CurrentScene);
        var target = ResizeHandle(scene, "southeast");
        var pointer = PointerObserver(host);

        await pointer.DownDocumentPointAsync(scene, Center(target.Bounds), pointerId: 84);
        var active = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.NotNull(active.EditorState.ActiveGesture);
        await WaitForCursorAsync(host, "nwse-resize");

        var command = await Session(host).ExecuteAsync(new MoveVisualStateCommand(
            active.DocumentId,
            active.DocumentRevision,
            new VisualStateId("demo:visual:alpha"),
            new PointD(80d, 135d),
            VisualPlacementMode.Pinned));
        Assert.True(command.IsCommitted);
        await Session(host).WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await pointer.CaptureReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var after = Assert.IsType<EditingSessionState>(host.CaptureState().Session);

        Assert.True(
            after.Status == EditingSessionStatus.Ready,
            string.Join(Environment.NewLine, after.RuntimeDiagnostics.Select(
                static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Null(after.EditorState.ActiveGesture);
        Assert.Equal(1, pointer.ReleaseCaptureCount);
        Assert.Equal([84L], pointer.ReleasedCaptureGenerations);
        await WaitForCursorAsync(host, "default");
        Assert.True(active.EditorState.Selection.AsSpan().SequenceEqual(
            after.EditorState.Selection.AsSpan()));
        Assert.Equal(active.EditorState.Viewport, after.EditorState.Viewport);

        var nextTarget = after.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId?.Value == "demo:alpha" &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        await pointer.DownDocumentPointAsync(
            after.CurrentScene,
            Center(nextTarget.Bounds),
            pointerId: 85);
        await pointer.MoveDocumentPointAsync(
            after.CurrentScene,
            Center(nextTarget.Bounds) + new VectorD(12d, 8d),
            pointerId: 85,
            buttons: 1);
        var restarted = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.NotNull(restarted.EditorState.ActiveGesture);
        await pointer.CancelAsync(pointerId: 85);
    }

    [Fact]
    public async Task RevisionInvalidationReleasesCaptureForMoveActivatedByPointerMove()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 2d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var before = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var target = MovableLabel(before);
        var start = Center(target.Bounds);
        var pointer = PointerObserver(host);
        const long pointerId = 86;

        await pointer.DownDocumentPointAsync(
            before.CurrentScene!,
            start,
            pointerId);
        Assert.Null(host.CaptureState().Session?.EditorState.ActiveGesture);

        await pointer.MoveDocumentPointAsync(
            before.CurrentScene!,
            start + new VectorD(14d, 9d),
            pointerId,
            buttons: 1);
        var active = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        Assert.Equal("inceptus.canvas2d:move", active.EditorState.ActiveGesture?.Kind);

        var command = await Session(host).ExecuteAsync(new MoveVisualStateCommand(
            active.DocumentId,
            active.DocumentRevision,
            new VisualStateId("demo:visual:alpha"),
            new PointD(80d, 135d),
            VisualPlacementMode.Pinned));
        Assert.True(command.IsCommitted);
        await Session(host).WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await pointer.CaptureReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(host.CaptureState().Session?.EditorState.ActiveGesture);
        Assert.Equal(1, pointer.ReleaseCaptureCount);
        Assert.Equal([pointerId], pointer.ReleasedCaptureGenerations);
        await WaitForCursorAsync(host, "default");
    }

    [Fact]
    public async Task SurfaceChangesDeduplicateAndResizeThenRenderThroughSession()
    {
        var execution = new RecordingRenderExecution();
        var initial = new Canvas2DSurfaceSize(640d, 360d, 1d);
        var observer = new RecordingSurfaceObserver(initial);
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");

        await observer.RaiseAsync(initial);
        var changed = new Canvas2DSurfaceSize(800d, 450d, 2d);
        await observer.RaiseAsync(changed);
        var state = host.CaptureState();

        Assert.Equal(["initialize:canvas", "render", "resize", "render"], execution.Calls);
        Assert.Equal(changed, state.SurfaceSize);
        Assert.Equal(2, state.SuccessfulRenderCount);
        Assert.True(state.LatestPresentationSucceeded);
        Assert.Equal(1, execution.MaximumConcurrency);
    }

    [Fact]
    public async Task SidebarDrivenSurfaceResizePreservesTransientStateAndReusesPipelineArtifacts()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(860d, 480d, 1.25d));
        var toolboxSelection = new ToolboxSelectionState();
        var activeToolboxItem = NeutralDemoToolbox.Catalog.Items[0].ItemId;
        Assert.True(toolboxSelection.Select(activeToolboxItem));
        await using var host = CreateHost(
            execution,
            observer,
            toolboxSelection: toolboxSelection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var initialViewport = session.CaptureState().EditorState.Viewport;
        var installedViewport = new ViewportSnapshot(
            1.4d,
            new VectorD(37d, -19d),
            initialViewport.VisibleDocumentRegion);
        Assert.True((await session.UpdateViewportAsync(installedViewport)).Succeeded);
        var selectedVisualStateId = new VisualStateId("demo:visual:alpha");
        Assert.True((await session.UpdateEditorStateAsync(CopyEditorState(
            session.CaptureState().EditorState,
            [selectedVisualStateId]))).Succeeded);
        var before = session.CaptureState();
        var counters = Counters(host);
        var projection = counters.ProjectionRuleInvocationCount;
        var layout = counters.LayoutInvocationCount;
        var routing = counters.RoutingInvocationCount;
        var scene = counters.SceneContributionInvocationCount;

        await observer.RaiseAsync(new Canvas2DSurfaceSize(708d, 480d, 2d));

        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(
            before.EditorState.Selection.ToArray(),
            after.EditorState.Selection.ToArray());
        Assert.Equal(selectedVisualStateId, Assert.Single(after.EditorState.Selection));
        Assert.Equal(before.EditorState.Viewport.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(before.EditorState.Viewport.Pan, after.EditorState.Viewport.Pan);
        Assert.Equal(activeToolboxItem, toolboxSelection.SelectedItemId);
        Assert.Equal(projection, Counters(host).ProjectionRuleInvocationCount);
        Assert.Equal(layout, Counters(host).LayoutInvocationCount);
        Assert.Equal(routing, Counters(host).RoutingInvocationCount);
        Assert.Equal(scene + 1, Counters(host).SceneContributionInvocationCount);
        Assert.Equal(708d, host.CaptureState().SurfaceSize?.CssWidth);
        Assert.Equal(480d, host.CaptureState().SurfaceSize?.CssHeight);
        Assert.Equal(2d, host.CaptureState().SurfaceSize?.DevicePixelRatio);
    }

    [Fact]
    public async Task PointerInputWaitsBehindAnInFlightResizeAndRenderingRemainsSerialized()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(640d, 360d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var initial = Assert.IsType<EditingSessionState>(host.CaptureState().Session);
        var beta = initial.CurrentScene!.Items.Last(item =>
            item.Origin.SemanticElementId?.Value == "demo:beta" &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        execution.BlockResize = true;

        var resize = observer.RaiseAsync(new Canvas2DSurfaceSize(800d, 450d, 2d));
        await execution.ResizeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pointer = PointerObserver(host).MoveDocumentPointAsync(
            initial.CurrentScene,
            Center(beta.Bounds));

        await Task.Yield();
        Assert.False(pointer.IsCompleted);
        execution.ReleaseResize();
        await Task.WhenAll(resize, pointer);

        Assert.Equal(1, execution.MaximumConcurrency);
        Assert.True(
            beta.Id == host.CaptureState().Session?.EditorState.HoveredObjectId,
            string.Join(Environment.NewLine, host.CaptureState().InteractionDiagnostics.Select(
                static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    [Fact]
    public async Task ResizeRenderFailureIsPresentationOnlyAndKeepsCurrentScene()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(640d, 360d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var current = host.CaptureState().Session?.CurrentScene;
        execution.RenderResult = new Canvas2DInteropOperationResult
        {
            Succeeded = false,
            Code = "TEST_RENDER_FAILED",
        };

        await observer.RaiseAsync(new Canvas2DSurfaceSize(800d, 450d, 2d));
        var state = host.CaptureState();

        Assert.Equal(EditingSessionStatus.Ready, state.Session?.Status);
        Assert.NotSame(current, state.Session?.CurrentScene);
        Assert.Equal(
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                state.Session!.EditorState.Viewport,
                new Canvas2DSurfaceSize(800d, 450d, 2d)),
            state.Session?.EditorState.Viewport.VisibleDocumentRegion);
        Assert.False(state.LatestPresentationSucceeded);
        Assert.Equal(1, state.SuccessfulRenderCount);
        Assert.NotEmpty(state.Session?.PresentationDiagnostics ?? []);
    }

    [Fact]
    public async Task DisposeDisconnectsObserverBeforeSessionRendererAndIgnoresLateCallbacks()
    {
        var lifecycle = new List<string>();
        var execution = new RecordingRenderExecution(lifecycle);
        var observer = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(640d, 360d, 1d),
            lifecycle);
        var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var currentScene = Assert.IsType<EditingSessionState>(host.CaptureState().Session)
            .CurrentScene!;
        var pointerObserver = PointerObserver(host);
        await pointerObserver.MoveDocumentPointAsync(
            currentScene,
            Center(ResizeHandle(currentScene, "southeast").Bounds));
        await WaitForCursorAsync(host, "nwse-resize");

        await host.DisposeAsync();
        await host.DisposeAsync();
        await observer.RaiseAsync(new Canvas2DSurfaceSize(900d, 600d, 2d));
        await PointerObserver(host).MoveDocumentPointAsync(currentScene, new PointD(100d, 100d));
        var state = host.CaptureState();

        Assert.True(state.IsDisposed);
        Assert.True(state.Session?.IsClosed);
        Assert.Equal(1, observer.DisposeCount);
        Assert.Equal(1, PointerObserver(host).DisposeCount);
        Assert.Equal("default", state.CssCursor);
        Assert.Equal("default", pointerObserver.CursorValues[^1]);
        Assert.Equal(1, execution.DisposeCount);
        Assert.True(lifecycle.IndexOf("pointer:dispose") < lifecycle.IndexOf("observer:dispose"));
        Assert.True(lifecycle.IndexOf("observer:dispose") < lifecycle.IndexOf("renderer:dispose"));
        Assert.DoesNotContain("resize", execution.Calls);
    }

    [Fact]
    public async Task ObserverBootstrapFailureIsReportedWithoutPartialSession()
    {
        var execution = new RecordingRenderExecution();
        await using var host = new DocumentCanvasHost(
            BpmnModelerTestComposition.NeutralFactory,
            CreateRenderer(execution),
            new ThrowingSurfaceObserverFactory());

        await host.InitializeAsync("canvas", "container");
        var state = host.CaptureState();

        Assert.True(state.InitializationAttempted);
        Assert.False(state.IsInitialized);
        Assert.Null(state.Session);
        Assert.Contains(state.HostDiagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(0, state.SuccessfulRenderCount);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task PointerObserverBootstrapFailureClosesAttachedSessionWithoutPartialHost()
    {
        var lifecycle = new List<string>();
        var execution = new RecordingRenderExecution(lifecycle);
        var surface = new RecordingSurfaceObserver(
            new Canvas2DSurfaceSize(640d, 360d, 1d),
            lifecycle);
        await using var host = new DocumentCanvasHost(
            BpmnModelerTestComposition.NeutralFactory,
            CreateRenderer(execution),
            new RecordingSurfaceObserverFactory(surface),
            new ThrowingPointerObserverFactory());

        await host.InitializeAsync("canvas", "container");
        var state = host.CaptureState();

        Assert.True(state.InitializationAttempted);
        Assert.False(state.IsInitialized);
        Assert.Null(state.Session);
        Assert.Contains(state.HostDiagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(1, surface.DisposeCount);
        Assert.Equal(1, execution.DisposeCount);
        Assert.True(lifecycle.IndexOf("observer:dispose") < lifecycle.IndexOf("renderer:dispose"));
    }

    private static void SetDataValue(
        DocumentCanvasPropertiesDraft draft,
        ElementPropertyFieldId fieldId,
        string editorValue)
    {
        Assert.True(draft.TryGetDataField(fieldId, out var dataField));
        Assert.NotNull(dataField);
        dataField.EditorValue = editorValue;
    }

    private static DocumentCanvasDataPropertySnapshot DataField(
        DocumentCanvasPropertySnapshot? snapshot,
        ElementPropertyFieldId fieldId)
    {
        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetDataField(fieldId, out var dataField));
        return Assert.IsType<DocumentCanvasDataPropertySnapshot>(dataField);
    }

    private static string DataEditorValue(
        DocumentCanvasPropertySnapshot? snapshot,
        ElementPropertyFieldId fieldId) =>
        DataField(snapshot, fieldId).EditorValue;

    private static long DataIntegerValue(
        DocumentCanvasPropertySnapshot? snapshot,
        ElementPropertyFieldId fieldId)
    {
        var value = DataField(snapshot, fieldId).Value;
        Assert.NotNull(value);
        Assert.Equal(PropertyValueKind.Integer, value.Kind);
        return value.IntegerValue;
    }

    private static async Task<DocumentCanvasModelViewPropertiesSnapshot>
        OpenModelViewPropertiesAsync(DocumentCanvasHost host)
    {
        var state = Session(host).CaptureState();
        var emptyPoint = FindEmptyDocumentPoint(
            state.CurrentScene!,
            new Canvas2DSurfaceSize(1100d, 700d, 1d));
        await PointerObserver(host).ContextMenuDocumentPointAsync(
            state.CurrentScene!,
            emptyPoint);
        return Assert.IsType<DocumentCanvasModelViewPropertiesSnapshot>(
            await host.OpenModelViewPropertiesAsync());
    }

    private static DocumentCanvasModelProfileDraft ModelProfileDraft(
        DocumentCanvasModelViewPropertiesDraft draft,
        ModelProfileId profileId)
    {
        Assert.True(draft.TryGetProfile(profileId, out var profile));
        return Assert.IsType<DocumentCanvasModelProfileDraft>(profile);
    }

    private static DocumentCanvasHost CreateHost(
        RecordingRenderExecution execution,
        RecordingSurfaceObserver observer,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null,
        IDocumentCanvasCompositionFactory? compositionFactory = null,
        ToolboxSelectionState? toolboxSelection = null,
        Func<Canvas2DRenderer>? replacementRendererFactory = null,
        Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedProcessPackageBuilder?
            publishPackageBuilder = null)
    {
        var pointerObserver = new RecordingPointerObserver(observer.Lifecycle);
        var renderer = CreateRenderer(execution);
        var observerFactory = new RecordingSurfaceObserverFactory(observer);
        var pointerObserverFactory = new RecordingPointerObserverFactory(pointerObserver);
        var host = new DocumentCanvasHost(
            compositionFactory ?? BpmnModelerTestComposition.CreateNeutralFactory(
                connectorAnchorPolicyProvider),
            renderer,
            observerFactory,
            pointerObserverFactory,
            toolboxSelection,
            replacementRendererFactory,
            publishPackageBuilder);
        HostObservers.Add(host, pointerObserver);
        return host;
    }

    private static ElementConnectorAnchorPolicy DisabledConnectorAnchorPolicy() =>
        new(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);

    private static readonly ConditionalWeakTable<DocumentCanvasHost, RecordingPointerObserver>
        HostObservers = new();

    private static RecordingPointerObserver PointerObserver(DocumentCanvasHost host) =>
        HostObservers.TryGetValue(host, out var observer)
            ? observer
            : throw new InvalidOperationException("The host has no test pointer observer.");

    private static EditingSession Session(DocumentCanvasHost host) =>
        Assert.IsType<EditingSession>(typeof(DocumentCanvasHost)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host));

    private static Canvas2DInteractionController InteractionController(
        DocumentCanvasHost host) =>
        Assert.IsType<Canvas2DInteractionController>(typeof(DocumentCanvasHost)
            .GetField("_interactionController", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host));

    private static Document AttachedDocument(EditingSession session) =>
        Assert.IsType<Document>(typeof(EditingSession)
            .GetField("_document", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session));

    private static DocumentRevision ObservedEventRevision(EditingSession session) =>
        Assert.IsType<DocumentRevision>(typeof(EditingSession)
            .GetField("_observedEventRevision", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session));

    private static void SetSessionField(EditingSession session, string name, object? value) =>
        typeof(EditingSession)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(session, value);

    private static Canvas2DSceneItem MovableLabel(EditingSessionState state) =>
        state.CurrentScene!.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId?.Value == "demo:alpha" &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static string? LabelContent(
        global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId).Geometry.Content;

    private static bool IsTargetArrow(Canvas2DSceneItem item) =>
        item.Metadata.TryGetValue(Canvas2DConnectorArrowMetadata.TargetArrow, out var value) &&
        value.Kind == PropertyValueKind.Boolean &&
        value.BooleanValue;

    private static Canvas2DSceneItem ResizeHandle(
        global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
        string role)
    {
        var handle = scene.Items.SingleOrDefault(item =>
            item.Origin.StableSourceKey?.StartsWith(
                $"resize-handle:{role}:",
                StringComparison.Ordinal) == true);
        return handle ?? throw new InvalidOperationException(
            $"Missing resize role '{role}'. Scene keys: {string.Join(", ", scene.Items.Select(static item => item.Origin.StableSourceKey).Where(static key => key is not null))}");
    }

    private static Canvas2DSceneItem ResizeZone(
        global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
        string role) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                role is "north" or "east" or "south" or "west"
                    ? $"resize-edge-zone:{role}:"
                    : $"resize-handle:{role}:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem ResizePreview(
        global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static async Task WaitForReleaseCountAsync(
        RecordingPointerObserver observer,
        int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (observer.ReleaseCaptureCount < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(expected, observer.ReleaseCaptureCount);
    }

    private static async Task WaitForCursorAsync(DocumentCanvasHost host, string expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!StringComparer.Ordinal.Equals(host.CaptureState().CssCursor, expected) &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(expected, host.CaptureState().CssCursor);
    }

    private static global::Inceptus.DocumentEngine.Blazor.Demo.NeutralDemoPipelineCounters Counters(
        DocumentCanvasHost host) =>
        Assert.IsType<NeutralDemoPipelineCountersAdapter>(
            host.CaptureState().PipelineCounters).Counters;

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static PointD FindEmptyDocumentPoint(
        Canvas2DScene scene,
        Canvas2DSurfaceSize surfaceSize)
    {
        Assert.True(scene.ViewportTransform.TryInvert(out var inverse));
        var hitTesting = new Canvas2DSceneHitTestService();
        for (var cssY = 12d; cssY < surfaceSize.CssHeight - 12d; cssY += 24d)
        {
            for (var cssX = 12d; cssX < surfaceSize.CssWidth - 12d; cssX += 24d)
            {
                var documentPoint = inverse.TransformPoint(new PointD(cssX, cssY));
                if (hitTesting.HitTest(scene, documentPoint) is null)
                {
                    return documentPoint;
                }
            }
        }

        throw new InvalidOperationException("The test scene contains no empty Canvas point.");
    }

    private static EditorStateSnapshot CopyEditorState(
        EditorStateSnapshot source,
        IEnumerable<VisualStateId> selection) =>
        new(
            selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState);

    private static Canvas2DRenderer CreateRenderer(RecordingRenderExecution execution) =>
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

    private sealed class PlacementOverrideCompositionFactory :
        IDocumentCanvasCompositionFactory
    {
        private static readonly ToolboxCatalog Catalog =
            new(BpmnPluginRegistration.N1.ToolboxContributions);

        private readonly Func<DocumentCanvasComposition, IToolboxPlacementCommandFactory>
            _factory;
        private readonly Func<DocumentCanvasComposition, IToolboxPlacementCandidateProvider?>
            _candidateProvider;

        internal PlacementOverrideCompositionFactory(
            Func<DocumentCanvasComposition, IToolboxPlacementCommandFactory> factory,
            Func<DocumentCanvasComposition, IToolboxPlacementCandidateProvider?>?
                candidateProvider = null)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
            _candidateProvider = candidateProvider ?? (static _ => null);
        }

        internal static ToolboxItemId TaskItemId { get; } =
            new("bpmn:toolbox:task");

        internal static IToolboxPlacementCommandFactory RealTaskFactory { get; } =
            BpmnPluginRegistration.N1.ToolboxPlacementRegistrations.Single(
                registration => registration.ToolboxItemId == TaskItemId).CommandFactory;

        public async ValueTask<DocumentCanvasComposition> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            var source = await BpmnModelerTestComposition.DemoFactory
                .CreateAsync(cancellationToken).ConfigureAwait(false);
            var placementCatalog = new ToolboxPlacementCatalog(
            [
                new ToolboxPlacementRegistration(
                    TaskItemId,
                    _factory(source),
                    _candidateProvider(source)),
            ],
            Catalog);
            return new DocumentCanvasComposition(
                source.Document,
                source.Configuration,
                source.PropertiesSchemaCatalog,
                source.Counters,
                placementCatalog,
                source.AnchorConnectionCreationCatalog,
                source.DocumentCreationIdentityProvider);
        }
    }

    private sealed class FailingPlacementFactory : IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ToolboxPlacementPlanResult.Failure(
            [
                new Diagnostic(
                    "TEST_TOOLBOX_PLACEMENT_FAILED",
                    DiagnosticSeverity.Error,
                    "The test placement factory rejected the request.",
                    request.ToolboxItemId.Value),
            ]);
        }
    }

    private sealed class CandidateRequiredPlacementFactory(
        IToolboxPlacementCommandFactory inner) : IToolboxPlacementCommandFactory
    {
        internal ToolboxPlacementRequest? LastRequest { get; private set; }

        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            LastRequest = request;
            return request.Candidate is null
                ? ToolboxPlacementPlanResult.Failure(
                [
                    new Diagnostic(
                        "TEST_TOOLBOX_CANDIDATE_REQUIRED",
                        DiagnosticSeverity.Error,
                        "The test placement requires a visible candidate.",
                        request.ToolboxItemId.Value),
                ])
                : inner.CreatePlan(request);
        }
    }

    private sealed class RecordingCandidateProvider(
        Func<ToolboxPlacementRequest, ToolboxPlacementCandidate?> resolver) :
        IToolboxPlacementCandidateProvider
    {
        internal List<ToolboxPlacementRequest> Requests { get; } = [];

        public ToolboxPlacementCandidate? ResolveCandidate(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Requests.Add(request);
            return resolver(request);
        }
    }

    private sealed class RevisionAdvancingPlacementFactory(
        IToolboxPlacementCommandFactory inner,
        Document document) : IToolboxPlacementCommandFactory
    {
        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var plan = inner.CreatePlan(request);
            var taskElementId = request.Document.SemanticModel.Elements
                .First(element => element.TypeId == BpmnSemanticTypes.Task).Id;
            var visual = request.Document.VisualModel.VisualStates
                .Single(state => state.SemanticElementId == taskElementId);
            var result = new CommandProcessor().ExecuteAsync(
                document,
                new MoveVisualStateCommand(
                    request.Document.DocumentId,
                    request.Document.Revision,
                    visual.Id,
                    visual.Position + new VectorD(1d, 1d)))
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (!result.IsCommitted)
            {
                throw new InvalidOperationException(
                    "The test could not advance the document revision.");
            }

            return plan;
        }
    }

    private sealed class BlockingPlacementFactory(
        IToolboxPlacementCommandFactory inner) :
        IToolboxPlacementCommandFactory,
        IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new(false);

        internal ManualResetEventSlim Release { get; } = new(false);

        public ToolboxPlacementPlanResult CreatePlan(ToolboxPlacementRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The test placement factory was not released.");
            }

            return inner.CreatePlan(request);
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private sealed class RecordingSurfaceObserverFactory : ICanvasPresentationSurfaceObserverFactory
    {
        private readonly RecordingSurfaceObserver _observer;

        internal RecordingSurfaceObserverFactory(RecordingSurfaceObserver observer) =>
            _observer = observer;

        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _observer.Callback = onSurfaceChanged;
            return ValueTask.FromResult<ICanvasPresentationSurfaceObserver>(_observer);
        }
    }

    private sealed class ThrowingSurfaceObserverFactory : ICanvasPresentationSurfaceObserverFactory
    {
        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<ICanvasPresentationSurfaceObserver>(
                new InvalidOperationException("observer unavailable"));
    }

    private sealed class ThrowingPointerObserverFactory : ICanvasPresentationPointerObserverFactory
    {
        public ValueTask<ICanvasPresentationPointerObserver> CreateAsync(
            string canvasElementId,
            Func<CanvasPointerInput, Task> onPointerInput,
            Func<CanvasWheelInput, Task> onWheelInput,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<ICanvasPresentationPointerObserver>(
                new InvalidOperationException("pointer observer unavailable"));
    }

    private sealed class RecordingSurfaceObserver : ICanvasPresentationSurfaceObserver
    {
        private readonly Canvas2DSurfaceSize _initial;
        private readonly List<string>? _lifecycle;

        internal RecordingSurfaceObserver(Canvas2DSurfaceSize initial, List<string>? lifecycle = null)
        {
            _initial = initial;
            _lifecycle = lifecycle;
        }

        internal List<string>? Lifecycle => _lifecycle;

        internal Func<Canvas2DSurfaceSize, Task>? Callback { get; set; }

        internal int StartCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal TaskCompletionSource<Canvas2DSurfaceSize>? InitialMeasurement { get; set; }

        public ValueTask<Canvas2DSurfaceSize> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return InitialMeasurement is { } pending
                ? new ValueTask<Canvas2DSurfaceSize>(pending.Task.WaitAsync(cancellationToken))
                : ValueTask.FromResult(_initial);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _lifecycle?.Add("observer:dispose");
            return ValueTask.CompletedTask;
        }

        internal Task RaiseAsync(Canvas2DSurfaceSize surfaceSize) =>
            Callback?.Invoke(surfaceSize) ?? Task.CompletedTask;
    }

    private sealed class RecordingPointerObserverFactory : ICanvasPresentationPointerObserverFactory
    {
        private readonly RecordingPointerObserver _observer;
        private bool _initialObserverIssued;

        internal RecordingPointerObserverFactory(RecordingPointerObserver observer) =>
            _observer = observer;

        public ValueTask<ICanvasPresentationPointerObserver> CreateAsync(
            string canvasElementId,
            Func<CanvasPointerInput, Task> onPointerInput,
            Func<CanvasWheelInput, Task> onWheelInput,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observer = _initialObserverIssued
                ? new RecordingPointerObserver(_observer.Lifecycle)
                : _observer;
            _initialObserverIssued = true;
            observer.OnPointerInput = onPointerInput;
            observer.OnWheelInput = onWheelInput;
            return ValueTask.FromResult<ICanvasPresentationPointerObserver>(observer);
        }
    }

    private sealed class RecordingPointerObserver(List<string>? lifecycle) :
        ICanvasPresentationPointerObserver
    {
        internal List<string>? Lifecycle => lifecycle;

        internal Func<CanvasPointerInput, Task>? OnPointerInput { get; set; }

        internal Func<CanvasWheelInput, Task>? OnWheelInput { get; set; }

        internal int StartCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal int ReleaseCaptureCount { get; private set; }

        internal List<string> CursorValues { get; } = [];

        internal List<long> ReleasedCaptureGenerations { get; } = [];

        internal TaskCompletionSource CaptureReleased { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseCaptureAsync(
            long captureGeneration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCaptureCount++;
            ReleasedCaptureGenerations.Add(captureGeneration);
            CaptureReleased.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask SetCursorAsync(string cssCursor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CursorValues.Add(cssCursor);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            lifecycle?.Add("pointer:dispose");
            return ValueTask.CompletedTask;
        }

        internal Task MoveDocumentPointAsync(
            global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
            PointD point,
            long pointerId = 1,
            int buttons = 0,
            bool controlKey = false)
        {
            var css = scene.ViewportTransform.TransformPoint(point);
            return RaiseAsync(
                CanvasPointerEventKind.Move,
                css.X + 17d,
                css.Y + 23d,
                pointerId,
                buttons: buttons,
                captureGeneration: buttons == 0 ? 0 : pointerId,
                controlKey: controlKey);
        }

        internal Task DownDocumentPointAsync(
            global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
            PointD point,
            long pointerId = 1,
            bool controlKey = false,
            bool isPrimary = true)
        {
            var css = scene.ViewportTransform.TransformPoint(point);
            return RaiseAsync(
                CanvasPointerEventKind.Down,
                css.X + 17d,
                css.Y + 23d,
                pointerId,
                button: 0,
                buttons: 1,
                captureGeneration: pointerId,
                controlKey: controlKey,
                isPrimary: isPrimary);
        }

        internal Task UpDocumentPointAsync(
            global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
            PointD point,
            long pointerId,
            bool controlKey = false)
        {
            var css = scene.ViewportTransform.TransformPoint(point);
            return RaiseAsync(
                CanvasPointerEventKind.Up,
                css.X + 17d,
                css.Y + 23d,
                pointerId,
                button: 0,
                buttons: 0,
                captureGeneration: pointerId,
                controlKey: controlKey);
        }

        internal async Task ClickDocumentPointAsync(
            global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
            PointD point,
            bool controlKey = false)
        {
            var css = scene.ViewportTransform.TransformPoint(point);
            await DownDocumentPointAsync(scene, point, controlKey: controlKey);
            await RaiseAsync(
                CanvasPointerEventKind.Up,
                css.X + 17d,
                css.Y + 23d,
                pointerId: 1,
                button: 0,
                buttons: 0,
                controlKey: controlKey);
        }

        internal async Task ClickCssPointAsync(PointD point)
        {
            await RaiseAsync(
                CanvasPointerEventKind.Down,
                point.X + 17d,
                point.Y + 23d,
                pointerId: 1,
                button: 0,
                buttons: 1,
                captureGeneration: 1);
            await RaiseAsync(
                CanvasPointerEventKind.Up,
                point.X + 17d,
                point.Y + 23d,
                pointerId: 1,
                button: 0,
                buttons: 0);
        }

        internal Task ContextMenuDocumentPointAsync(
            global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
            PointD point)
        {
            var css = scene.ViewportTransform.TransformPoint(point);
            return RaiseAsync(
                CanvasPointerEventKind.ContextMenu,
                css.X + 17d,
                css.Y + 23d,
                pointerId: 0,
                button: 2,
                buttons: 0);
        }

        internal Task DoubleClickDocumentPointAsync(
            global::Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene scene,
            PointD point)
        {
            var css = scene.ViewportTransform.TransformPoint(point);
            return RaiseAsync(
                CanvasPointerEventKind.DoubleClick,
                css.X + 17d,
                css.Y + 23d,
                pointerId: 0,
                button: 0,
                buttons: 0);
        }

        internal Task LeaveAsync() => RaiseAsync(
            CanvasPointerEventKind.Leave,
            clientX: 17d,
            clientY: 23d,
            pointerId: 1,
            buttons: 0);

        internal Task CancelAsync(long pointerId) => RaiseAsync(
            CanvasPointerEventKind.Cancel,
            clientX: 17d,
            clientY: 23d,
            pointerId: pointerId,
            button: 0,
            buttons: 0);

        internal Task WheelAsync(
            double deltaX,
            double deltaY,
            CanvasWheelDeltaMode deltaMode = CanvasWheelDeltaMode.Pixel,
            bool controlKey = false,
            bool metaKey = false) =>
            (OnWheelInput ?? throw new InvalidOperationException(
                "The test pointer observer has no managed wheel callback."))(new CanvasWheelInput(
                deltaX,
                deltaY,
                deltaMode,
                controlKey,
                metaKey));

        internal Task MiddleDownCssPointAsync(PointD point, long pointerId = 7) => RaiseAsync(
            CanvasPointerEventKind.Down,
            point.X + 17d,
            point.Y + 23d,
            pointerId,
            button: 1,
            buttons: 4,
            captureGeneration: pointerId);

        internal Task MiddleMoveCssPointAsync(PointD point, long pointerId = 7) => RaiseAsync(
            CanvasPointerEventKind.Move,
            point.X + 17d,
            point.Y + 23d,
            pointerId,
            buttons: 4,
            captureGeneration: pointerId);

        internal Task MiddleUpCssPointAsync(PointD point, long pointerId = 7) => RaiseAsync(
            CanvasPointerEventKind.Up,
            point.X + 17d,
            point.Y + 23d,
            pointerId,
            button: 1,
            buttons: 0,
            captureGeneration: pointerId);

        internal Task InvalidNormalizedAsync(CanvasPointerEventKind kind, long pointerId) =>
            (OnPointerInput ?? throw new InvalidOperationException(
                "The test pointer observer has no managed callback."))(new CanvasPointerInput(
                kind,
                pointerId,
                kind == CanvasPointerEventKind.Up ? 0 : -1,
                kind == CanvasPointerEventKind.Move ? 1 : 0,
                IsPrimary: true,
                ClientX: double.MaxValue,
                ClientY: double.MaxValue,
                CanvasLeft: -double.MaxValue,
                CanvasTop: -double.MaxValue,
                AltKey: false,
                ControlKey: false,
                MetaKey: false,
                ShiftKey: false));

        private Task RaiseAsync(
            CanvasPointerEventKind kind,
            double clientX,
            double clientY,
            long pointerId,
            int button = -1,
            int buttons = 0,
            long captureGeneration = 0,
            bool controlKey = false,
            bool isPrimary = true) =>
            (OnPointerInput ?? throw new InvalidOperationException(
                "The test pointer observer has no managed callback."))(new CanvasPointerInput(
                kind,
                pointerId,
                button,
                buttons,
                IsPrimary: isPrimary,
                ClientX: clientX,
                ClientY: clientY,
                CanvasLeft: 17d,
                CanvasTop: 23d,
                AltKey: false,
                ControlKey: controlKey,
                MetaKey: false,
                ShiftKey: false,
                CaptureGeneration: captureGeneration));
    }

    private sealed class MutableConnectorAnchorPolicyProvider(
        ElementConnectorAnchorPolicy policy) : IElementConnectorAnchorPolicyProvider
    {
        internal ElementConnectorAnchorPolicy Policy { get; set; } = policy;

        public ElementConnectorAnchorPolicy Resolve(SemanticTypeId elementTypeId)
        {
            ArgumentNullException.ThrowIfNull(elementTypeId);
            return Policy;
        }
    }

    private sealed class RejectingProfileAvailabilityCompositionFactory :
        IDocumentCanvasCompositionFactory
    {
        public async ValueTask<DocumentCanvasComposition> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            var composition = await BpmnModelerTestComposition.CreateDemoAsync(cancellationToken)
                .ConfigureAwait(false);
            var original = composition.Configuration;
            var rejectingRegistration = new CommandValidatorRegistration(
                SetModelProfileAvailabilityCommand.KnownTypeId,
                new CommandValidatorId("test:model-profile-availability:reject"),
                new RejectingProfileAvailabilityValidator());
            var configuration = new EditingSessionConfiguration(
                original.ProjectionEngine,
                original.LayoutEngine,
                original.LayoutAlgorithmId,
                original.RoutingEngine,
                original.RoutingAlgorithmId,
                original.SceneBuilder,
                original.ProjectionContext,
                original.LayoutContext,
                original.RoutingContext,
                original.InitialEditorState,
                original.CommandHandlers,
                original.CommandValidators.Add(rejectingRegistration),
                original.HistoryPolicies,
                original.DocumentChangedSubscribers,
                original.ConnectorAnchorPolicyProvider,
                original.ModelProfileCatalog,
                original.InitialModelProfileViewState);
            return new DocumentCanvasComposition(
                composition.Document,
                configuration,
                composition.PropertiesSchemaCatalog,
                composition.Counters,
                composition.ToolboxPlacementCatalog,
                composition.AnchorConnectionCreationCatalog,
                composition.DocumentCreationIdentityProvider,
                composition.EndpointReconnectionCatalog,
                composition.DeletionCatalog,
                composition.ModelValidationCatalog,
                composition.ScopeNavigationCatalog,
                composition.BackgroundActionCatalog);
        }
    }

    private sealed class RejectingProfileAvailabilityValidator : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(
            ICommand command,
            DocumentSnapshot document)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(document);
            return
            [
                new Diagnostic(
                    "TEST_MODEL_PROFILE_AVAILABILITY_REJECTED",
                    DiagnosticSeverity.Error,
                    "The model profile availability change was rejected for this test."),
            ];
        }
    }

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        private readonly List<string>? _lifecycle;
        private int _active;

        internal RecordingRenderExecution(List<string>? lifecycle = null) => _lifecycle = lifecycle;

        internal List<string> Calls { get; } = [];

        internal int MaximumConcurrency { get; private set; }

        internal int DisposeCount { get; private set; }

        internal Canvas2DInteropOperationResult InitializeResult { get; set; } = Success();

        internal Canvas2DInteropOperationResult RenderResult { get; set; } = Success();

        internal Exception? MeasureTextException { get; set; }

        internal bool BlockResize { get; set; }

        internal bool BlockRender { get; set; }

        internal TaskCompletionSource RenderStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource RenderRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ResizeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource ResizeRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily)
        {
            Calls.Add($"initialize:{canvasElementId}");
            return ValueTask.FromResult(InitializeResult);
        }

        public async ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize)
        {
            Enter();
            try
            {
                Calls.Add("resize");
                if (BlockResize)
                {
                    ResizeStarted.TrySetResult();
                    await ResizeRelease.Task.ConfigureAwait(false);
                }

                return Success();
            }
            finally
            {
                Exit();
            }
        }

        public async ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            Enter();
            try
            {
                Calls.Add("render");
                if (BlockRender)
                {
                    RenderStarted.TrySetResult();
                    await RenderRelease.Task.ConfigureAwait(false);
                }

                return RenderResult;
            }
            finally
            {
                Exit();
            }
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request)
        {
            if (MeasureTextException is not null)
            {
                return ValueTask.FromException<Canvas2DTextMeasurementInteropResult>(
                    MeasureTextException);
            }

            var width = request.Text.Sum(character => character switch
            {
                ' ' => request.FontSize * 0.33d,
                >= 'A' and <= 'Z' => request.FontSize * 0.62d,
                >= 'a' and <= 'z' => request.FontSize * 0.54d,
                >= '0' and <= '9' => request.FontSize * 0.55d,
                _ => request.FontSize * 0.58d,
            });
            var ascent = request.FontSize * 0.75d;
            var descent = request.FontSize * 0.25d;
            return ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = width,
                Ascent = ascent,
                Descent = descent,
                LineHeight = request.LineHeight,
                BoundingX = 0d,
                BoundingY = -ascent,
                BoundingWidth = width,
                BoundingHeight = ascent + descent,
                ResolvedFontIdentity = $"{request.FontIdentity}@{request.FontVersion}",
            });
        }

        public ValueTask DisposeAsync()
        {
            Calls.Add("dispose");
            DisposeCount++;
            _lifecycle?.Add("renderer:dispose");
            return ValueTask.CompletedTask;
        }

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };

        internal void ReleaseResize() => ResizeRelease.TrySetResult();

        internal void ReleaseRender() => RenderRelease.TrySetResult();

        private void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
        }

        private void Exit() => Interlocked.Decrement(ref _active);
    }
}
