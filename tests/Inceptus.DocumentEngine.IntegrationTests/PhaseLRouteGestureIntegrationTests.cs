using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

using static Inceptus.DocumentEngine.IntegrationTests.EditingSessionTestSynchronization;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseLRouteGestureIntegrationTests
{
    [Fact]
    public async Task SavedManualBendCommitsOncePreservesBothHistoryBranchesAndUsesCurrentEndpoints()
    {
        var context = await AttachPreparedAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var document = context.Document;
        var routeVisual = FindRouteVisual(document.CaptureSnapshot());
        Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
            document.Revision, routeVisual.Id, ConnectorRoutingType.Manual))).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(document, session);
        var initial = SavedRoute(document);
        Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId,
            document.Revision, routeVisual.Id, [initial.Path[0], new PointD(300, 220), initial.Path[^1]]))).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(document, session);
        var sourceVisual = document.CaptureSnapshot().VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "demo:beta");
        foreach (var x in new[] { 110d, 120d })
        {
            Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId,
                document.Revision, sourceVisual.Id, new PointD(x, 100), VisualPlacementMode.Pinned))).IsCommitted);
            await WaitForCommittedEventAndSessionIdleAsync(document, session);
        }
        Assert.True((await session.UndoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(document, session);
        context.Events.Events.Clear();
        var before = document.CaptureSnapshot();
        var branches = session.CaptureState().HistoryStatus;
        Assert.True(branches.CanUndo && branches.CanRedo);
        var current = SavedRoute(document);
        var scene = session.CaptureState().CurrentScene!;
        var connector = FindConnector(scene, routeVisual.Id);
        var selected = await interaction.PointerActivatedAsync(Css(scene, current.Path[1]));
        var handle = FindBendHandle(selected.SessionState.CurrentScene!, routeVisual.Id, connector.Id);
        var start = Center(handle.Bounds);
        var pressed = await interaction.PointerPressedAsync(Pointer(701, selected.SessionState.CurrentScene!, start, 0, 1));
        var endpoint = start + new VectorD(30, -10);
        var moved = await interaction.PointerMovedAsync(Pointer(701, pressed.SessionState.CurrentScene!, endpoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(branches, moved.SessionState.HistoryStatus);
        Assert.Empty(context.Events.Events);
        AssertRouteEqual([current.Path[0], endpoint, current.Path[^1]],
            FindRoutePreview(moved.SessionState.CurrentScene!, routeVisual.Id, connector.Id).Geometry.Points);

        var released = await interaction.PointerReleasedAsync(Pointer(701, moved.SessionState.CurrentScene!, endpoint, 0));
        await WaitForCommittedEventAndSessionIdleAsync(document, session);
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(before.Revision.Value + 1, document.Revision.Value);
        Assert.Equal(branches, session.CaptureState().HistoryStatus);
        Assert.Single(context.Events.Events);
        AssertRouteEqual([current.Path[0], endpoint, current.Path[^1]], SavedRoute(document).Path);
        Assert.Empty(FindRouteVisual(document.CaptureSnapshot()).Route);
        AssertRelationshipUnchanged(Assert.Single(before.SemanticModel.Relationships), document.CaptureSnapshot());

        Assert.True((await session.RedoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(document, session);
        AssertRouteEqual([endpoint], SavedRoute(document).ManualDefinition!.Value);
        AssertPointEqual(new PointD(240, 140), SavedRoute(document).Path[0]);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(document, session);
        AssertRouteEqual([endpoint], SavedRoute(document).ManualDefinition!.Value);
        AssertPointEqual(current.Path[0], SavedRoute(document).Path[0]);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
    }

    private static ConnectorRoutingRecord SavedRoute(Document document) => document.CaptureSnapshot()
        .VisualModel.RoutingScopes!.Value.SelectMany(static scope => scope.Connectors)
        .Single(record => record.VisualStateId == FindRouteVisual(document.CaptureSnapshot()).Id);

    private static async Task<(Document Document, EditingSession Session, RecordingSubscriber Events)> AttachPreparedAsync()
    {
        var id = new DocumentId("phase-l:saved-manual");
        var sourceId = new SemanticElementId("demo:beta");
        var targetId = new SemanticElementId("demo:gamma");
        var flowId = new SemanticElementId("demo:beta-gamma");
        var sourceAnchor = new ConnectorAnchorId("phase-l:source");
        var targetAnchor = new ConnectorAnchorId("phase-l:target");
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(id, DocumentRevision.Zero,
            [BpmnSemanticFactory.CreateTask(sourceId, "SOURCE", "Source", 1),
                BpmnSemanticFactory.CreateTask(targetId, "TARGET", "Target", 2)],
            [BpmnSemanticFactory.CreateSequenceFlow(flowId, sourceId, targetId)]),
            new VisualModelSnapshot(id, DocumentRevision.Zero,
            [new VisualStateSnapshot(new VisualStateId("phase-l:source-visual"), sourceId,
                new PointD(100, 100), new SizeD(120, 80), VisualPlacementMode.Pinned,
                connectorAnchors: [new ConnectorAnchor(sourceAnchor, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0)]),
                new VisualStateSnapshot(new VisualStateId("phase-l:target-visual"), targetId,
                    new PointD(400, 100), new SizeD(120, 80), VisualPlacementMode.Pinned,
                    connectorAnchors: [new ConnectorAnchor(targetAnchor, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]),
                new VisualStateSnapshot(new VisualStateId("phase-l:connector-visual"), flowId, default, default,
                    VisualPlacementMode.Automatic, sourceAnchorId: sourceAnchor, targetAnchorId: targetAnchor)]),
            new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
        var construction = DocumentReconstructor.Reconstruct(snapshot);
        Assert.True(construction.Succeeded);
        var document = construction.Document!;
        var composition = BpmnModelerComposition.Create(document);
        var events = new RecordingSubscriber();
        var source = composition.Configuration;
        var configuration = new EditingSessionConfiguration(source.ProjectionEngine, source.LayoutEngine,
            source.LayoutAlgorithmId, source.RoutingEngine, source.RoutingAlgorithmId, source.SceneBuilder,
            source.ProjectionContext, source.LayoutContext, source.RoutingContext, EditorStateSnapshot.Empty,
            source.CommandHandlers, source.CommandValidators, source.HistoryPolicies, [events],
            source.ConnectorAnchorPolicyProvider, source.ModelProfileCatalog, source.InitialModelProfileViewState,
            source.RoutingInputPreparer);
        var renderer = new Canvas2DRenderer(new RecordingRenderExecution(), RendererConfiguration());
        Assert.True((await renderer.InitializeAsync("phase-l:saved", new Canvas2DSurfaceSize(960, 640, 1))).Succeeded);
        document = await BpmnModelerTestComposition.PrepareFreshDocumentAsync(document, configuration, renderer);
        var attached = await EditingSession.AttachAsync(document, renderer, configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
        return (document, attached.Session!, events);
    }

    // The following neutral tests intentionally retain the standalone, unprepared Route compatibility path.
    [Fact]
    public async Task NeutralBendPreviewsCommitOnceThenUndoAndRedoExactRoute()
    {
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = context.Document.CaptureSnapshot();
        var initialRelationship = initialDocument.SemanticModel.Relationships.Single(
            relationship => relationship.Id.Value == "demo:beta-gamma");
        var routeVisual = FindRouteVisual(initialDocument);
        var initialRoute = routeVisual.Route;
        AssertRouteEqual(
            [new PointD(460d, 230d), new PointD(510d, 160d), new PointD(560d, 125d)],
            initialRoute);
        var connector = FindConnector(initial.CurrentScene!, routeVisual.Id);
        Assert.True(connector.Geometry.Points.AsSpan().SequenceEqual(initialRoute.AsSpan()));
        var selected = await interaction.PointerActivatedAsync(Css(initial.CurrentScene!, initialRoute[1]));
        var selectedState = selected.SessionState;
        Assert.Equal(routeVisual.Id, Assert.Single(selectedState.EditorState.Selection));
        var handle = FindBendHandle(selectedState.CurrentScene!, routeVisual.Id, connector.Id);
        AssertPointEqual(initialRoute[1], Center(handle.Bounds));
        var initialProjectionCount = context.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = context.Counters.LayoutInvocationCount;
        var initialRoutingCount = context.Counters.RoutingInvocationCount;
        var initialSceneCount = context.Counters.SceneContributionInvocationCount;
        var initialRenderCount = context.Execution.RenderCount;
        const long pointerId = 307;
        var start = Center(handle.Bounds);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            selectedState.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var firstPoint = start + new VectorD(22d, -14d);
        var firstMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            firstPoint,
            buttons: 1));
        // Keep this legacy History scenario outside endpoint snap radii; magnetic
        // candidates and mixed solid/dashed runs have dedicated interaction coverage.
        var finalPoint = start + new VectorD(35d, -15d);
        var secondMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            firstMove.SessionState.CurrentScene!,
            finalPoint,
            buttons: 1));
        PointD[] expectedRoute = [initialRoute[0], finalPoint, initialRoute[2]];
        var preview = FindRoutePreview(secondMove.SessionState.CurrentScene!, routeVisual.Id, connector.Id);

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, firstMove.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, secondMove.Status);
        Assert.Equal(
            "inceptus.canvas2d:route-bend",
            secondMove.SessionState.EditorState.ActiveGesture?.Kind);
        AssertRouteEqual(expectedRoute, preview.Geometry.Points);
        Assert.Same(selectedState.ProjectedGraph, secondMove.SessionState.ProjectedGraph);
        Assert.Same(selectedState.LayoutResult, secondMove.SessionState.LayoutResult);
        Assert.Same(selectedState.RoutingResult, secondMove.SessionState.RoutingResult);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Empty(context.Events.Events);
        Assert.Equal(new HistoryStatus(0, false, false), secondMove.SessionState.HistoryStatus);
        Assert.Equal(0, context.RouteProbe.InvocationCount);
        Assert.Equal(initialProjectionCount, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 3, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 3, context.Execution.RenderCount);

        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            secondMove.SessionState.CurrentScene!,
            finalPoint,
            button: 0));
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);
        var committed = session.CaptureState();
        var committedDocument = context.Document.CaptureSnapshot();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation!.IsCommitted);
        Assert.Equal(new DocumentRevision(1), committedDocument.Revision);
        AssertRouteEqual(expectedRoute, FindRouteVisual(committedDocument).Route);
        AssertRouteEqual(expectedRoute, FindConnector(
            committed.CurrentScene!,
            routeVisual.Id).Geometry.Points);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Null(committed.EditorState.ActiveGesture);
        AssertRelationshipUnchanged(initialRelationship, committedDocument);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(context.Events));
        Assert.Equal(1, context.RouteProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 5, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 1, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 4, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 4, context.Execution.RenderCount);

        var undo = await session.UndoAsync();
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);
        var undone = session.CaptureState();
        var undoneDocument = context.Document.CaptureSnapshot();

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), undoneDocument.Revision);
        AssertRouteEqual(initialRoute, FindRouteVisual(undoneDocument).Route);
        AssertRouteEqual(initialRoute, FindConnector(undone.CurrentScene!, routeVisual.Id).Geometry.Points);
        Assert.Equal(new HistoryStatus(1, false, true), undone.HistoryStatus);
        Assert.Null(undone.EditorState.ActiveGesture);
        AssertRelationshipUnchanged(initialRelationship, undoneDocument);

        var redo = await session.RedoAsync();
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);
        var redone = session.CaptureState();
        var redoneDocument = context.Document.CaptureSnapshot();

        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), redoneDocument.Revision);
        AssertRouteEqual(expectedRoute, FindRouteVisual(redoneDocument).Route);
        AssertRouteEqual(expectedRoute, FindConnector(redone.CurrentScene!, routeVisual.Id).Geometry.Points);
        Assert.Equal(new HistoryStatus(1, true, false), redone.HistoryStatus);
        Assert.Null(redone.EditorState.ActiveGesture);
        AssertRelationshipUnchanged(initialRelationship, redoneDocument);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            EventRevisions(context.Events));
        Assert.Equal(3, context.RouteProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 15, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 3, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 6, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 6, context.Execution.RenderCount);
    }

    [Fact]
    public async Task RouteCancellationNoOpAndStaleReleaseNeverCommitRoute()
    {
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = context.Document.CaptureSnapshot();
        var routeVisual = FindRouteVisual(initialDocument);
        var connector = FindConnector(initial.CurrentScene!, routeVisual.Id);
        var selected = await interaction.PointerActivatedAsync(Css(
            initial.CurrentScene!,
            routeVisual.Route[1]));
        var handle = FindBendHandle(selected.SessionState.CurrentScene!, routeVisual.Id, connector.Id);
        var start = Center(handle.Bounds);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            401,
            selected.SessionState.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        _ = await interaction.PointerMovedAsync(Pointer(
            401,
            pressed.SessionState.CurrentScene!,
            start + new VectorD(20d, 15d),
            buttons: 1));
        var cancelled = await interaction.PointerCancelledAsync(401);

        Assert.Equal(Canvas2DInteractionStatus.Updated, cancelled.Status);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Empty(context.Events.Events);
        Assert.Equal(0, context.RouteProbe.InvocationCount);

        var noOpHandle = FindBendHandle(cancelled.SessionState.CurrentScene!, routeVisual.Id, connector.Id);
        var noOpPoint = Center(noOpHandle.Bounds);
        var noOpPressed = await interaction.PointerPressedAsync(Pointer(
            402,
            cancelled.SessionState.CurrentScene!,
            noOpPoint,
            button: 0,
            buttons: 1));
        var noOp = await interaction.PointerReleasedAsync(Pointer(
            402,
            noOpPressed.SessionState.CurrentScene!,
            noOpPoint,
            button: 0));

        Assert.Equal(Canvas2DInteractionStatus.Updated, noOp.Status);
        Assert.Null(noOp.PersistentOperation);
        Assert.Null(noOp.SessionState.EditorState.ActiveGesture);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Empty(context.Events.Events);

        var staleHandle = FindBendHandle(noOp.SessionState.CurrentScene!, routeVisual.Id, connector.Id);
        var stalePoint = Center(staleHandle.Bounds);
        var stalePressed = await interaction.PointerPressedAsync(Pointer(
            403,
            noOp.SessionState.CurrentScene!,
            stalePoint,
            button: 0,
            buttons: 1));
        var alpha = initialDocument.VisualModel.VisualStates.Single(
            visual => visual.SemanticElementId.Value == "demo:alpha");
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            context.Document.DocumentId,
            context.Document.Revision,
            alpha.Id,
            alpha.Position + new VectorD(11d, 9d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);

        var stale = await interaction.PointerReleasedAsync(Pointer(
            403,
            session.CaptureState().CurrentScene!,
            stalePoint + new VectorD(30d, 20d),
            button: 0));

        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Null(stale.SessionState.EditorState.ActiveGesture);
        AssertRouteEqual(routeVisual.Route, FindRouteVisual(context.Document.CaptureSnapshot()).Route);
        Assert.Equal(0, context.RouteProbe.InvocationCount);
        Assert.Equal(new HistoryStatus(1, true, false), stale.SessionState.HistoryStatus);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(context.Events));
    }

    [Fact]
    public async Task PersistentBendUsesCurrentLayoutAnchorsAfterLargeSourceMoveAndResize()
    {
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initialDocument = context.Document.CaptureSnapshot();
        var initialRelationship = initialDocument.SemanticModel.Relationships.Single(
            relationship => relationship.Id.Value == "demo:beta-gamma");
        var routeVisual = FindRouteVisual(initialDocument);
        var initialPersistentRoute = routeVisual.Route;
        var beta = initialDocument.VisualModel.VisualStates.Single(
            visual => visual.SemanticElementId.Value == "demo:beta");

        var movedPosition = new PointD(800d, 420d);
        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            context.Document.DocumentId,
            context.Document.Revision,
            beta.Id,
            movedPosition,
            VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);
        var moved = session.CaptureState();
        var movedDocument = context.Document.CaptureSnapshot();

        Assert.Equal(EditingSessionStatus.Ready, moved.Status);
        AssertRouteEqual(initialPersistentRoute, FindRouteVisual(movedDocument).Route);
        AssertRouteEqual(
            [
                new PointD(movedPosition.X + beta.Size.Width, movedPosition.Y + (beta.Size.Height / 2d)),
                initialPersistentRoute[1],
                initialPersistentRoute[^1],
            ],
            FindCalculatedRoute(moved, routeVisual.Id));
        AssertRelationshipUnchanged(initialRelationship, movedDocument);

        var resizedBounds = new RectD(movedPosition.X, movedPosition.Y, 260d, 140d);
        var resize = await session.ExecuteAsync(new ResizeVisualStateCommand(
            context.Document.DocumentId,
            context.Document.Revision,
            beta.Id,
            resizedBounds,
            VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);
        var resized = session.CaptureState();
        var resizedDocument = context.Document.CaptureSnapshot();

        Assert.Equal(EditingSessionStatus.Ready, resized.Status);
        AssertRouteEqual(initialPersistentRoute, FindRouteVisual(resizedDocument).Route);
        AssertRouteEqual(
            [
                new PointD(resizedBounds.Right, resizedBounds.Y + (resizedBounds.Height / 2d)),
                initialPersistentRoute[1],
                initialPersistentRoute[^1],
            ],
            FindCalculatedRoute(resized, routeVisual.Id));
        AssertRelationshipUnchanged(initialRelationship, resizedDocument);
        Assert.Equal(new DocumentRevision(2), resized.DocumentRevision);
        Assert.Equal(new HistoryStatus(2, true, false), resized.HistoryStatus);

        var connector = FindConnector(resized.CurrentScene!, routeVisual.Id);
        var selected = await interaction.PointerActivatedAsync(Css(
            resized.CurrentScene!,
            initialPersistentRoute[1]));
        Assert.Equal(routeVisual.Id, Assert.Single(selected.SessionState.EditorState.Selection));
        var handle = FindBendHandle(
            selected.SessionState.CurrentScene!,
            routeVisual.Id,
            connector.Id);
        var handleCenter = Center(handle.Bounds);
        const long pointerId = 512;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            selected.SessionState.CurrentScene!,
            handleCenter,
            button: 0,
            buttons: 1));
        var editedBend = handleCenter + new VectorD(35d, 25d);
        var movedBend = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            editedBend,
            buttons: 1));
        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            movedBend.SessionState.CurrentScene!,
            editedBend,
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        await WaitForCommittedEventAndSessionIdleAsync(context.Document, session);
        var edited = session.CaptureState();
        var editedDocument = context.Document.CaptureSnapshot();
        var expectedPersistentRoute = new[]
        {
            initialPersistentRoute[0],
            editedBend,
            initialPersistentRoute[^1],
        };

        Assert.Equal(EditingSessionStatus.Ready, edited.Status);
        AssertRouteEqual(expectedPersistentRoute, FindRouteVisual(editedDocument).Route);
        AssertRouteEqual(
            [
                new PointD(resizedBounds.Right, resizedBounds.Y + (resizedBounds.Height / 2d)),
                editedBend,
                initialPersistentRoute[^1],
            ],
            FindCalculatedRoute(edited, routeVisual.Id));
        AssertRelationshipUnchanged(initialRelationship, editedDocument);
        Assert.Null(edited.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), edited.DocumentRevision);
        Assert.Equal(new HistoryStatus(3, true, false), edited.HistoryStatus);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            EventRevisions(context.Events));
        Assert.Equal(1, context.RouteProbe.InvocationCount);
    }

    private static async ValueTask<RouteIntegrationContext> AttachAsync()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var events = new RecordingSubscriber();
        var routeProbe = new CountingCommandValidator();
        var source = composition.Configuration;
        var configuration = new EditingSessionConfiguration(
            source.ProjectionEngine,
            source.LayoutEngine,
            source.LayoutAlgorithmId,
            source.RoutingEngine,
            source.RoutingAlgorithmId,
            source.SceneBuilder,
            source.ProjectionContext,
            source.LayoutContext,
            source.RoutingContext,
            EditorStateSnapshot.Empty,
            source.CommandHandlers,
            source.CommandValidators.Append(new CommandValidatorRegistration(
                UpdateConnectionRouteCommand.KnownTypeId,
                new CommandValidatorId("test:phase-l:route-count"),
                routeProbe)),
            source.HistoryPolicies,
            [events]);
        var execution = new RecordingRenderExecution();
        var renderer = new Canvas2DRenderer(execution, RendererConfiguration());
        Assert.True((await renderer.InitializeAsync(
            "phase-l-route-canvas",
            new Canvas2DSurfaceSize(960d, 640d, 2d))).Succeeded);
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        return new RouteIntegrationContext(
            attachment.Session!,
            composition.Document,
            composition.Counters,
            execution,
            events,
            routeProbe);
    }

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2D.Scene.Canvas2DScene scene,
        PointD documentPoint,
        int button = -1,
        int buttons = 0) =>
        new(pointerId, Css(scene, documentPoint), true, button, buttons);

    private static PointD Css(Canvas2D.Scene.Canvas2DScene scene, PointD documentPoint) =>
        scene.ViewportTransform.TransformPoint(documentPoint);

    private static Canvas2DSceneItem FindConnector(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == visualStateId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));

    private static Canvas2DSceneItem FindBendHandle(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey == $"route-bend-handle:{targetId.Value}:1" &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static Canvas2DSceneItem FindRoutePreview(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "route-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static VisualStateSnapshot FindRouteVisual(DocumentSnapshot snapshot) =>
        snapshot.VisualModel.VisualStates.Single(
            visual => visual.SemanticElementId.Value == "demo:beta-gamma");

    private static ImmutableArray<PointD> FindCalculatedRoute(
        EditingSessionState state,
        VisualStateId visualStateId)
    {
        var edge = state.ProjectedGraph!.Edges.Single(
            candidate => candidate.Source.VisualStateId == visualStateId);
        return state.RoutingResult!.Routes.Single(
            route => route.ProjectedEdgeId == edge.Id).Path;
    }

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertPointEqual(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, 10);
        Assert.Equal(expected.Y, actual.Y, 10);
    }

    private static void AssertRouteEqual(IEnumerable<PointD> expected, IEnumerable<PointD> actual)
    {
        var expectedPoints = expected.ToArray();
        var actualPoints = actual.ToArray();
        Assert.Equal(expectedPoints.Length, actualPoints.Length);
        for (var index = 0; index < expectedPoints.Length; index++)
        {
            AssertPointEqual(expectedPoints[index], actualPoints[index]);
        }
    }

    private static void AssertRelationshipUnchanged(
        Contracts.Semantics.SemanticRelationshipSnapshot expected,
        DocumentSnapshot actual)
    {
        var relationship = actual.SemanticModel.Relationships.Single(
            candidate => candidate.Id == expected.Id);
        Assert.Equal(expected, relationship);
        Assert.Equal(expected.SourceId, relationship.SourceId);
        Assert.Equal(expected.TargetId, relationship.TargetId);
    }

    private static DocumentRevision[] EventRevisions(RecordingSubscriber subscriber) =>
        subscriber.Events.Select(static change => change.CommittedRevision).ToArray();

    private static Canvas2DRendererConfiguration RendererConfiguration() =>
        new(
            fontResources:
            [
                new Canvas2DFontResource(
                    "demo:font:dejavu",
                    "2.37",
                    "DejaVu Sans",
                    "/fonts/DejaVuSans-2.37.ttf"),
            ],
            defaultFontFamily: "DejaVu Sans");

    private sealed record RouteIntegrationContext(
        EditingSession Session,
        Runtime.Documents.Document Document,
        NeutralDemoPipelineCounters Counters,
        RecordingRenderExecution Execution,
        RecordingSubscriber Events,
        CountingCommandValidator RouteProbe);

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingCommandValidator : ICommandValidator
    {
        private int _invocationCount;

        internal int InvocationCount => Volatile.Read(ref _invocationCount);

        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
        {
            _ = command;
            _ = document;
            Interlocked.Increment(ref _invocationCount);
            return [];
        }
    }

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        private int _renderCount;

        internal int RenderCount => Volatile.Read(ref _renderCount);

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) => ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) => ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            Interlocked.Increment(ref _renderCount);
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DInteropOperationResult> RenderViewportAsync(Canvas2DViewportFrame frame)
        {
            Interlocked.Increment(ref _renderCount);
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request) =>
            ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = 80d,
                Ascent = 10d,
                Descent = 3d,
                LineHeight = 18d,
                BoundingX = 0d,
                BoundingY = -10d,
                BoundingWidth = 80d,
                BoundingHeight = 13d,
                ResolvedFontIdentity = "demo:font:dejavu@2.37",
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
