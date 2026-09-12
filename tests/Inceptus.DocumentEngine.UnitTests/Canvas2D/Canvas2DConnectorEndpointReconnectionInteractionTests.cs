using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorEndpointReconnectionInteractionTests
{
    private static readonly SemanticElementId SourceSemanticId = new("test:semantic:a");
    private static readonly SemanticElementId TargetSemanticId = new("test:semantic:b");
    private static readonly SemanticElementId RelationshipId = new("test:semantic:ab");
    private static readonly SemanticElementId BlockingRelationshipId =
        new("test:semantic:occupied-relationship");
    private static readonly VisualStateId SourceVisualId = new("test:visual:a");
    private static readonly VisualStateId TargetVisualId = new("test:visual:b");
    private static readonly VisualStateId ConnectorVisualId = new("test:visual:ab");
    private static readonly VisualStateId BlockingConnectorVisualId =
        new("test:visual:occupied-connector");
    private static readonly ConnectorAnchorId OriginalSourceAnchorId =
        new("test:anchor:a:source:original");
    private static readonly ConnectorAnchorId AlternativeSourceAnchorId =
        new("test:anchor:a:source:alternative");
    private static readonly ConnectorAnchorId OccupiedSourceAnchorId =
        new("test:anchor:a:source:occupied");
    private static readonly ConnectorAnchorId OriginalTargetAnchorId =
        new("test:anchor:b:target:original");
    private static readonly ConnectorAnchorId AlternativeTargetAnchorId =
        new("test:anchor:b:target:alternative");
    private static readonly ConnectorAnchorId OccupiedTargetAnchorId =
        new("test:anchor:b:target:occupied");
    private static readonly IElementConnectorAnchorPolicyProvider AnchorPolicies =
        new ElementConnectorAnchorPolicyRegistry(
            BpmnPluginRegistration.N3.ConnectorAnchorPolicies);

    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    public async Task EndpointHandleMapsRoleAndWinsOverOverlappingN2Anchor(
        ConnectorEndpointKind endpointKind)
    {
        var endpointFactory = new RecordingEndpointFactory(matches: true);
        var n2Factory = new RecordingConnectionFactory();
        await using var harness = await Harness.CreateAsync(
            Catalog(endpointFactory),
            new AnchorConnectionCreationCatalog(
            [
                new AnchorConnectionCreationRegistration(
                    new AnchorConnectionCreationId("test:n2:priority"),
                    n2Factory),
            ]),
            new FixedIdentityProvider());
        harness.EnqueueSceneSuccess();
        var endpoint = harness.EndpointHandle(endpointKind);

        var result = await harness.Controller.PointerPressedAsync(
            Pointer(101, harness.Scene, Center(endpoint), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
        var request = Assert.Single(endpointFactory.StartRequests);
        Assert.Equal(endpointKind, request.EndpointKind);
        Assert.Equal(
            endpointKind == ConnectorEndpointKind.Source
                ? SourceSemanticId
                : TargetSemanticId,
            request.CurrentSemanticElementId);
        Assert.Equal(
            endpointKind == ConnectorEndpointKind.Source
                ? OriginalSourceAnchorId
                : OriginalTargetAnchorId,
            request.CurrentAnchorId);
        Assert.Equal(0, n2Factory.MatchCount);

        harness.EnqueueSceneSuccess();
        _ = await harness.Controller.PointerCancelledAsync(101);
    }

    [Fact]
    public async Task EmptyZeroOneAmbiguousAndThrowingCatalogMatchesAreDeterministic()
    {
        await using (var empty = await Harness.CreateAsync(
                         ConnectorEndpointReconnectionCatalog.Empty))
        {
            var result = await PressEndpointAsync(empty, ConnectorEndpointKind.Target, 110);
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
            Assert.Null(result.SessionState.EditorState.ActiveGesture);
            _ = await empty.Controller.PointerCancelledAsync(110);
        }

        var zeroFactory = new RecordingEndpointFactory(matches: false);
        await using (var zero = await Harness.CreateAsync(Catalog(zeroFactory)))
        {
            var result = await PressEndpointAsync(zero, ConnectorEndpointKind.Target, 111);
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
            Assert.Single(zeroFactory.StartRequests);
            Assert.Null(result.SessionState.EditorState.ActiveGesture);
            _ = await zero.Controller.PointerCancelledAsync(111);
        }

        var oneFactory = new RecordingEndpointFactory(matches: true);
        await using (var one = await Harness.CreateAsync(Catalog(oneFactory)))
        {
            one.EnqueueSceneSuccess();
            var result = await PressEndpointAsync(one, ConnectorEndpointKind.Target, 112);
            Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
            Assert.Equal(
                Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind,
                result.SessionState.EditorState.ActiveGesture?.Kind);
            one.EnqueueSceneSuccess();
            _ = await one.Controller.PointerCancelledAsync(112);
        }

        var first = new RecordingEndpointFactory(matches: true);
        var second = new RecordingEndpointFactory(matches: true);
        await using (var ambiguous = await Harness.CreateAsync(Catalog(first, second)))
        {
            var result = await PressEndpointAsync(
                ambiguous,
                ConnectorEndpointKind.Target,
                113);
            Assert.Equal(Canvas2DInteractionStatus.Unavailable, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.AmbiguousEndpointReconnection);
            Assert.Null(result.SessionState.EditorState.ActiveGesture);
        }

        var throwingFactory = new RecordingEndpointFactory(
            matches: true,
            throwWhenMatching: true);
        await using (var throwing = await Harness.CreateAsync(Catalog(throwingFactory)))
        {
            var result = await PressEndpointAsync(
                throwing,
                ConnectorEndpointKind.Target,
                114);
            Assert.Equal(Canvas2DInteractionStatus.Failed, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed);
            Assert.Null(result.SessionState.EditorState.ActiveGesture);
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("node-body")]
    [InlineData("wrong-role")]
    [InlineData("connector-body")]
    [InlineData("occupied")]
    [InlineData("outside-document")]
    public async Task InvalidDropCancelsWithoutPersistentMutationAndRetainsSelection(
        string targetKind)
    {
        await using var harness = await Harness.CreateAsync(
            BpmnCatalog(),
            includeSourceNodeInSelection: true);
        var before = harness.Document.CaptureSnapshot();
        var expectedSelection = harness.State.EditorState.Selection;
        harness.EnqueueSceneSuccess();
        var pressed = await PressEndpointAsync(
            harness,
            ConnectorEndpointKind.Target,
            120);
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);

        if (StringComparer.Ordinal.Equals(targetKind, "occupied"))
        {
            Assert.DoesNotContain(harness.Scene.Items, item => HasAnchorId(
                item,
                OccupiedTargetAnchorId));
        }

        var documentPoint = targetKind switch
        {
            "empty" => new PointD(700d, 500d),
            "node-body" => new PointD(260d, 45d),
            "wrong-role" => Center(harness.AnchorHandle(AlternativeSourceAnchorId)),
            "connector-body" => new PointD(160d, 45d),
            "occupied" => new PointD(310d, 45d),
            "outside-document" => new PointD(-100d, -100d),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind)),
        };
        harness.EnqueueSceneSuccess();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(120, harness.Scene, documentPoint));

        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.True(expectedSelection.AsSpan().SequenceEqual(
            released.SessionState.EditorState.Selection.AsSpan()));
    }

    [Fact]
    public async Task OutsideDocumentReconnectionPreviewStopsAtOriginAndDropCancels()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        var before = harness.Document.CaptureSnapshot();
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 121);
        harness.EnqueueSceneSuccess();

        var moved = await harness.Controller.PointerMovedAsync(
            Pointer(121, harness.Scene, new PointD(-100d, -100d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        var preview = Assert.Single(harness.Scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-endpoint-reconnection-preview:",
                StringComparison.Ordinal) == true);
        Assert.Equal(new PointD(0d, 0d), preview.Geometry.Points[^1]);
        Assert.All(preview.Geometry.Points, point =>
        {
            Assert.True(point.X >= 0d);
            Assert.True(point.Y >= 0d);
        });
        harness.EnqueueSceneSuccess();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(121, harness.Scene, new PointD(-100d, -100d)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("pointer-cancel")]
    public async Task EscapePointerCancelAndLostCaptureCleanupAreIdempotent(string boundary)
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        var before = harness.Document.CaptureSnapshot();
        var expectedSelection = harness.State.EditorState.Selection;
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 130);
        harness.EnqueueSceneSuccess();

        var cancelled = StringComparer.Ordinal.Equals(boundary, "escape")
            ? await harness.Controller.CancelActiveGestureAsync()
            : await harness.Controller.PointerCancelledAsync(130);
        var duplicate = StringComparer.Ordinal.Equals(boundary, "escape")
            ? await harness.Controller.CancelActiveGestureAsync()
            : await harness.Controller.PointerCancelledAsync(130);

        Assert.Equal(Canvas2DInteractionStatus.Updated, cancelled.Status);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, duplicate.Status);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Null(duplicate.SessionState.EditorState.ActiveGesture);
        Assert.Equal(0, duplicate.SessionState.HistoryStatus.EntryCount);
        Assert.True(expectedSelection.AsSpan().SequenceEqual(
            duplicate.SessionState.EditorState.Selection.AsSpan()));
    }

    [Fact]
    public async Task SameAnchorReleaseIsNoOpAndRetainsConnectorSelection()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        var before = harness.Document.CaptureSnapshot();
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 140);
        var original = harness.AnchorHandle(OriginalTargetAnchorId);
        harness.EnqueueSceneSuccess();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(140, harness.Scene, Center(original)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(ConnectorVisualId, Assert.Single(
            released.SessionState.EditorState.Selection));
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
    }

    [Fact]
    public async Task SessionGenerationChangeMakesGestureStaleAndCleansPreview()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 150);
        var active = harness.State.EditorState;
        harness.EnqueueSceneSuccess();
        var external = await harness.Session.UpdateEditorStateAsync(
            new EditorStateSnapshot(
                active.Selection,
                active.HoveredObjectId,
                active.ActiveToolId,
                focusTargetId: "test:external-focus-change",
                active.Viewport,
                active.ActiveGesture,
                active.TemporaryFeedback,
                active.ToolState));
        Assert.True(external.Succeeded);
        harness.EnqueueSceneSuccess();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(150, harness.Scene, new PointD(260d, 70d)));

        Assert.Equal(Canvas2DInteractionStatus.Stale, released.Status);
        Assert.Contains(released.Diagnostics, diagnostic => diagnostic.Code ==
            Canvas2DInteractionDiagnosticCodes.StaleGesture);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), released.SessionState.DocumentRevision);
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(ConnectorVisualId, Assert.Single(
            released.SessionState.EditorState.Selection));
    }

    [Fact]
    public async Task CurrentEndpointMutationMakesPendingReleaseStale()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 160);
        var candidatePoint = Center(harness.AnchorHandle(AlternativeTargetAnchorId));
        harness.EnqueueFullSuccess();
        var current = harness.Document.CaptureSnapshot();
        var external = await harness.Session.ExecuteAsync(
            new ReconnectBpmnSequenceFlowEndpointCommand(
                current.DocumentId,
                current.Revision,
                RelationshipId,
                ConnectorVisualId,
                ConnectorEndpointKind.Target,
                TargetSemanticId,
                OriginalTargetAnchorId,
                TargetSemanticId,
                AlternativeTargetAnchorId));
        Assert.True(external.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(160, harness.Scene, candidatePoint));

        Assert.Equal(Canvas2DInteractionStatus.Stale, released.Status);
        Assert.Null(released.PersistentOperation);
        var after = harness.Document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(4), after.Revision);
        Assert.Equal(
            AlternativeTargetAnchorId,
            after.VisualModel.VisualStates.Single(item => item.Id == ConnectorVisualId)
                .TargetAnchorId);
        Assert.Equal(1, released.SessionState.HistoryStatus.EntryCount);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
    }

    [Fact]
    public async Task CandidateOccupiedAfterStartMakesReleaseStaleWithoutReconnect()
    {
        var factory = new CountingPlanningEndpointFactory();
        await using var harness = await Harness.CreateAsync(Catalog(factory));
        var before = harness.Document.CaptureSnapshot();
        var beforePrimaryRelationship = before.SemanticModel.Relationships.Single(item =>
            item.Id == RelationshipId);
        var beforePrimaryVisual = before.VisualModel.VisualStates.Single(item =>
            item.Id == ConnectorVisualId);
        var beforeHistory = harness.State.HistoryStatus;
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 165);
        var candidate = harness.AnchorHandle(AlternativeTargetAnchorId);
        var candidatePoint = Center(candidate);
        Assert.False(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            before.VisualModel,
            AlternativeTargetAnchorId,
            ConnectorVisualId,
            ConnectorEndpointKind.Target));
        harness.EnqueueSceneSuccess();
        var moved = await harness.Controller.PointerMovedAsync(
            Pointer(165, harness.Scene, candidatePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);

        var occupyingRelationshipId = new SemanticElementId(
            "test:n3:occupancy-race:relationship");
        var occupyingVisualId = new VisualStateId("test:n3:occupancy-race:visual");
        harness.EnqueueFullSuccess();
        var current = harness.Document.CaptureSnapshot();
        var occupied = await harness.Session.ExecuteAsync(
            new CreateBpmnSequenceFlowCommand(
                current.DocumentId,
                current.Revision,
                occupyingRelationshipId,
                occupyingVisualId,
                SourceSemanticId,
                TargetSemanticId,
                AlternativeSourceAnchorId,
                AlternativeTargetAnchorId));
        Assert.True(occupied.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var afterOccupation = harness.Document.CaptureSnapshot();
        Assert.True(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            afterOccupation.VisualModel,
            AlternativeTargetAnchorId,
            ConnectorVisualId,
            ConnectorEndpointKind.Target));

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(165, harness.Scene, candidatePoint));

        Assert.Equal(Canvas2DInteractionStatus.Stale, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(0, factory.PlanCount);
        Assert.Equal(1, factory.MatchCount);
        var afterRelease = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), afterRelease.Revision);
        Assert.Equal(
            beforeHistory.EntryCount + 1,
            released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(
            beforePrimaryRelationship,
            afterRelease.SemanticModel.Relationships.Single(item =>
                item.Id == RelationshipId));
        Assert.Equal(
            beforePrimaryVisual,
            afterRelease.VisualModel.VisualStates.Single(item =>
                item.Id == ConnectorVisualId));
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.Equal(ConnectorVisualId, Assert.Single(
            released.SessionState.EditorState.Selection));
        Assert.DoesNotContain(harness.Scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-endpoint-reconnection-preview:",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task RuntimeFaultAfterStartRefusesCompletionAndCleansTransientGesture()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        var before = harness.Document.CaptureSnapshot();
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 166);
        var activeScene = harness.Scene;
        var candidatePoint = Center(harness.AnchorHandle(AlternativeTargetAnchorId));
        var active = harness.State.EditorState;
        harness.Pipeline.EnqueueScene(
            ControlledEditingSessionPipeline.Failure("TEST_N3_ACTIVE_RUNTIME_FAULT"));
        var fault = await harness.Session.UpdateEditorStateAsync(
            new EditorStateSnapshot(
                active.Selection,
                active.HoveredObjectId,
                active.ActiveToolId,
                focusTargetId: "test:n3:fault-after-start",
                active.Viewport,
                active.ActiveGesture,
                active.TemporaryFeedback,
                active.ToolState));
        Assert.False(fault.Succeeded);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, harness.State.Status);

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(166, activeScene, candidatePoint));

        Assert.Equal(Canvas2DInteractionStatus.Stale, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.Equal(ConnectorVisualId, Assert.Single(
            released.SessionState.EditorState.Selection));
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("failure")]
    [InlineData("command-rejected")]
    public async Task CompletionPlanningAndCommandFailuresAlwaysCleanTransientState(
        string failureMode)
    {
        var factory = new CompletionFailureEndpointFactory(failureMode);
        await using var harness = await Harness.CreateAsync(Catalog(factory));
        var before = harness.Document.CaptureSnapshot();
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 167);
        var candidatePoint = Center(harness.AnchorHandle(AlternativeTargetAnchorId));
        harness.EnqueueSceneSuccess();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(167, harness.Scene, candidatePoint));

        Assert.Equal(Canvas2DInteractionStatus.Failed, released.Status);
        Assert.Equal(1, factory.PlanCount);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.Equal(ConnectorVisualId, Assert.Single(
            released.SessionState.EditorState.Selection));
        if (StringComparer.Ordinal.Equals(failureMode, "throw"))
        {
            Assert.Contains(released.Diagnostics, diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed);
            Assert.Null(released.PersistentOperation);
        }
        else if (StringComparer.Ordinal.Equals(failureMode, "failure"))
        {
            Assert.Contains(released.Diagnostics, diagnostic =>
                diagnostic.Code == "TEST_N3_PLAN_FAILURE");
            Assert.Null(released.PersistentOperation);
        }
        else
        {
            Assert.NotNull(released.PersistentOperation);
            Assert.False(released.PersistentOperation.IsCommitted);
            Assert.Contains(released.Diagnostics, diagnostic => diagnostic.Code ==
                BpmnCommandDiagnosticCodes.SequenceFlowEndpointStateMismatch);
        }
    }

    [Fact]
    public async Task RuntimeFaultedSessionRefusesEndpointReconnection()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        var readyScene = harness.Scene;
        var endpointPoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target));
        harness.Pipeline.EnqueueScene(
            ControlledEditingSessionPipeline.Failure("TEST_N3_RUNTIME_FAULT"));
        var fault = await harness.Session.UpdateEditorStateAsync(
            new EditorStateSnapshot(
                selection: [ConnectorVisualId],
                activeToolId: "test:fault-trigger"));
        Assert.False(fault.Succeeded);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, harness.State.Status);

        var pressed = await harness.Controller.PointerPressedAsync(
            Pointer(170, readyScene, endpointPoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unavailable, pressed.Status);
        Assert.Contains(pressed.Diagnostics, diagnostic => diagnostic.Code ==
            Canvas2DInteractionDiagnosticCodes.UnavailableSession);
        Assert.Null(pressed.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), pressed.SessionState.DocumentRevision);
    }

    [Fact]
    public async Task NoRouteFallbackAfterReconnectionRemainsPresentAndSelected()
    {
        await using var harness = await Harness.CreateAsync(BpmnCatalog());
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 175);
        var candidatePoint = Center(harness.AnchorHandle(AlternativeTargetAnchorId));
        harness.EnqueueNoRouteFullSuccess();

        var released = await harness.Controller.PointerReleasedAsync(
            Pointer(175, harness.Scene, candidatePoint));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation?.IsCommitted);
        Assert.DoesNotContain(released.Diagnostics, diagnostic => diagnostic.Code ==
            Canvas2DInteractionDiagnosticCodes.EditorStateUpdateFailed);
        Assert.Equal(ConnectorVisualId, Assert.Single(
            released.SessionState.EditorState.Selection));
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        var edge = Assert.Single(released.SessionState.ProjectedGraph!.Edges, candidate =>
            candidate.Source.VisualStateId == ConnectorVisualId);
        Assert.Equal(edge.Id, Assert.Single(
            released.SessionState.RoutingResult!.NoRouteEdgeIds));
        var fallback = Assert.Single(released.SessionState.CurrentScene!.Items,
            Canvas2DConnectorPathMetadata.IsNoRouteFallbackPath);
        Assert.Equal(ConnectorVisualId, fallback.Origin.VisualStateId);
    }

    [Fact]
    public async Task DuplicateAndInflightPointerUpCommitExactlyOnceAndRetainSelection()
    {
        var factory = new BlockingCompletionEndpointFactory();
        await using var harness = await Harness.CreateAsync(Catalog(factory));
        harness.EnqueueSceneSuccess();
        _ = await PressEndpointAsync(harness, ConnectorEndpointKind.Target, 180);
        var candidatePoint = Center(harness.AnchorHandle(AlternativeTargetAnchorId));
        harness.EnqueueFullSuccess();

        var scene = harness.Scene;
        var firstTask = Task.Run(async () => await harness.Controller.PointerReleasedAsync(
            Pointer(180, scene, candidatePoint)));
        Canvas2DInteractionResult[] results;
        try
        {
            await factory.CompletionRevalidationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicateTask = harness.Controller.PointerReleasedAsync(
                Pointer(180, scene, candidatePoint)).AsTask();
            factory.ReleaseCompletionRevalidation.TrySetResult();
            results = await Task.WhenAll(firstTask, duplicateTask)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            factory.ReleaseCompletionRevalidation.TrySetResult();
        }

        Assert.Single(results, result => result.Status == Canvas2DInteractionStatus.Committed);
        Assert.Single(results, result => result.Status == Canvas2DInteractionStatus.Unchanged);
        var after = harness.Document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(4), after.Revision);
        Assert.Equal(
            AlternativeTargetAnchorId,
            after.VisualModel.VisualStates.Single(item => item.Id == ConnectorVisualId)
                .TargetAnchorId);
        Assert.Equal(1, harness.State.HistoryStatus.EntryCount);
        Assert.Equal(ConnectorVisualId, Assert.Single(harness.State.EditorState.Selection));
        Assert.Null(harness.State.EditorState.ActiveGesture);
    }

    private static async ValueTask<Canvas2DInteractionResult> PressEndpointAsync(
        Harness harness,
        ConnectorEndpointKind endpointKind,
        long pointerId)
    {
        var endpoint = harness.EndpointHandle(endpointKind);
        return await harness.Controller.PointerPressedAsync(
            Pointer(pointerId, harness.Scene, Center(endpoint), buttons: 1));
    }

    private static ConnectorEndpointReconnectionCatalog BpmnCatalog() =>
        new(BpmnPluginRegistration.N3.ConnectorEndpointReconnectionRegistrations);

    private static ConnectorEndpointReconnectionCatalog Catalog(
        params IConnectorEndpointReconnectionCommandFactory[] factories) =>
        new(factories.Select((factory, index) =>
            new ConnectorEndpointReconnectionRegistration(
                new ConnectorEndpointReconnectionId($"test:n3:factory:{index}"),
                factory)));

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2DScene scene,
        PointD documentPoint,
        int buttons = 0) =>
        new(
            pointerId,
            scene.ViewportTransform.TransformPoint(documentPoint),
            isPrimary: true,
            button: 0,
            buttons);

    private static PointD Center(Canvas2DSceneItem item) => new(
        item.Bounds.Left + (item.Bounds.Width / 2d),
        item.Bounds.Top + (item.Bounds.Height / 2d));

    private static bool HasAnchorId(Canvas2DSceneItem item, ConnectorAnchorId anchorId) =>
        item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var value) &&
        value.Kind == PropertyValueKind.Text &&
        StringComparer.Ordinal.Equals(value.TextValue, anchorId.Value);

    private static Canvas2DSceneTestData CreateInputs()
    {
        var source = Canvas2DSceneTestData.Create();
        var sourceNode = source.Graph.Nodes.Single(item =>
            item.Source.SemanticElementId == SourceSemanticId);
        var targetNode = source.Graph.Nodes.Single(item =>
            item.Source.SemanticElementId == TargetSemanticId);
        var sourceVisual = source.VisualModel.VisualStates.Single(item =>
            item.Id == SourceVisualId);
        var targetVisual = source.VisualModel.VisualStates.Single(item =>
            item.Id == TargetVisualId);
        var sourceAnchors = new[]
        {
            Anchor(OriginalSourceAnchorId, ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source),
            Anchor(AlternativeSourceAnchorId, ConnectorAnchorSide.Top,
                ConnectorAnchorRole.Source),
            Anchor(OccupiedSourceAnchorId, ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Source),
        };
        var targetAnchors = new[]
        {
            Anchor(OriginalTargetAnchorId, ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target),
            Anchor(AlternativeTargetAnchorId, ConnectorAnchorSide.Bottom,
                ConnectorAnchorRole.Target),
            Anchor(OccupiedTargetAnchorId, ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Target),
        };
        var ports = sourceAnchors.Select(anchor => Port(sourceNode, sourceVisual, anchor))
            .Concat(targetAnchors.Select(anchor => Port(targetNode, targetVisual, anchor)));
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports.Concat(ports),
            source.Graph.Labels);
        var blockingConnector = new VisualStateSnapshot(
            BlockingConnectorVisualId,
            BlockingRelationshipId,
            default,
            default,
            VisualPlacementMode.Automatic,
            sourceAnchorId: OccupiedSourceAnchorId,
            targetAnchorId: OccupiedTargetAnchorId);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Select(visual =>
                visual.Id == SourceVisualId
                    ? CopyNode(visual, sourceAnchors)
                    : visual.Id == TargetVisualId
                        ? CopyNode(visual, targetAnchors)
                        : visual.Id == ConnectorVisualId
                            ? CopyConnector(visual)
                            : visual)
                .Append(blockingConnector));
        return source.WithGraph(graph).WithVisualModel(visualModel);
    }

    private static ConnectorAnchor Anchor(
        ConnectorAnchorId id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role) =>
        new(id, side, role, order: 0);

    private static ProjectedPort Port(
        ProjectedNode owner,
        VisualStateSnapshot visual,
        ConnectorAnchor anchor) =>
        new(
            new ProjectionSourceTrace(
                owner.Source.DocumentId,
                owner.Source.RuleId,
                owner.Source.SourceKind,
                owner.Source.SemanticElementId,
                owner.Source.SemanticTypeId,
                $"connector-anchor:{anchor.Id.Value}",
                visual.Id),
            owner.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(
                new ProjectedConnectorAnchor(
                    anchor.Id,
                    anchor.Side,
                    anchor.Role == ConnectorAnchorRole.Source
                        ? ConnectorAnchorRoleCapability.Source
                        : ConnectorAnchorRoleCapability.Target,
                    anchor.Order,
                    sideCount: 1,
                    ResolvedConnectorAnchorKind.Dynamic)));

    private static VisualStateSnapshot CopyNode(
        VisualStateSnapshot source,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            source.Id,
            source.SemanticElementId,
            source.Position,
            source.Size,
            source.PlacementMode,
            source.Route,
            source.Properties,
            anchors,
            source.SourceAnchorId,
            source.TargetAnchorId);

    private static VisualStateSnapshot CopyConnector(VisualStateSnapshot source) =>
        new(
            source.Id,
            source.SemanticElementId,
            source.Position,
            source.Size,
            source.PlacementMode,
            source.Route,
            source.Properties,
            source.ConnectorAnchors,
            OriginalSourceAnchorId,
            OriginalTargetAnchorId);

    private static Inceptus.DocumentEngine.Runtime.Documents.Document CreateDocument(
        Canvas2DSceneTestData inputs)
    {
        var semanticModel = new SemanticModelSnapshot(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            [
                new SemanticElementSnapshot(SourceSemanticId, BpmnSemanticTypes.Task),
                new SemanticElementSnapshot(TargetSemanticId, BpmnSemanticTypes.Task),
            ],
            [
                new SemanticRelationshipSnapshot(
                    RelationshipId,
                    BpmnSemanticTypes.SequenceFlow,
                    SourceSemanticId,
                    TargetSemanticId),
                new SemanticRelationshipSnapshot(
                    BlockingRelationshipId,
                    BpmnSemanticTypes.SequenceFlow,
                    SourceSemanticId,
                    TargetSemanticId),
            ]);
        var snapshot = new DocumentSnapshot(
            semanticModel,
            inputs.VisualModel,
            new DocumentMetadataSnapshot(
                inputs.Graph.DocumentId,
                inputs.Graph.SourceRevision));
        var reconstruction = DocumentReconstructor.Reconstruct(snapshot, AnchorPolicies);
        return reconstruction.Document ?? throw new InvalidOperationException(string.Join(
            Environment.NewLine,
            reconstruction.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            Canvas2DSceneTestData inputs,
            Inceptus.DocumentEngine.Runtime.Documents.Document document,
            EditingSession session,
            ControlledEditingSessionPipeline pipeline,
            Canvas2DInteractionController controller)
        {
            Inputs = inputs;
            Document = document;
            Session = session;
            Pipeline = pipeline;
            Controller = controller;
        }

        internal Canvas2DSceneTestData Inputs { get; }

        internal Inceptus.DocumentEngine.Runtime.Documents.Document Document { get; }

        internal EditingSession Session { get; }

        internal ControlledEditingSessionPipeline Pipeline { get; }

        internal Canvas2DInteractionController Controller { get; }

        internal EditingSessionState State => Session.CaptureState();

        internal Canvas2DScene Scene => Assert.IsType<Canvas2DScene>(State.CurrentScene);

        internal static async ValueTask<Harness> CreateAsync(
            ConnectorEndpointReconnectionCatalog endpointCatalog,
            AnchorConnectionCreationCatalog? connectionCatalog = null,
            IDocumentCreationIdentityProvider? identityProvider = null,
            bool includeSourceNodeInSelection = false)
        {
            var inputs = CreateInputs();
            var selection = includeSourceNodeInSelection
                ? new[] { ConnectorVisualId, SourceVisualId }
                : new[] { ConnectorVisualId };
            var editorState = new EditorStateSnapshot(selection);
            var pipeline = new ControlledEditingSessionPipeline();
            pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, editorState));
            var document = CreateDocument(inputs);
            var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            var baseline = EditingSessionTestHarness.Configuration(editorState, AnchorPolicies);
            var configuration = new EditingSessionConfiguration(
                baseline.ProjectionEngine,
                baseline.LayoutEngine,
                baseline.LayoutAlgorithmId,
                baseline.RoutingEngine,
                baseline.RoutingAlgorithmId,
                baseline.SceneBuilder,
                baseline.ProjectionContext,
                baseline.LayoutContext,
                baseline.RoutingContext,
                editorState,
                BpmnPluginRegistration.N3.CommandHandlers,
                BpmnPluginRegistration.N3.CommandValidators,
                BpmnPluginRegistration.N3.HistoryPolicies,
                connectorAnchorPolicyProvider: AnchorPolicies);
            var attachment = await EditingSession.AttachAsync(
                document,
                renderer,
                configuration,
                pipeline);
            Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
            var session = Assert.IsType<EditingSession>(attachment.Session);
            var controller = new Canvas2DInteractionController(
                session,
                connectionCreationCatalog: connectionCatalog,
                creationIdentityProvider: identityProvider,
                endpointReconnectionCatalog: endpointCatalog);
            return new Harness(inputs, document, session, pipeline, controller);
        }

        internal Canvas2DSceneItem EndpointHandle(ConnectorEndpointKind endpointKind)
        {
            var role = endpointKind == ConnectorEndpointKind.Source
                ? Canvas2DConnectorEndpointMetadata.StartEndpointRole
                : Canvas2DConnectorEndpointMetadata.EndEndpointRole;
            return Assert.Single(Scene.Items, item =>
                item.Origin.VisualStateId == ConnectorVisualId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorEndpointMetadata.HandleRole,
                    out var value) &&
                value.Kind == PropertyValueKind.Text &&
                StringComparer.Ordinal.Equals(value.TextValue, role));
        }

        internal Canvas2DSceneItem AnchorHandle(ConnectorAnchorId anchorId) =>
            Assert.Single(Scene.Items, item => HasAnchorId(item, anchorId));

        internal void EnqueueSceneSuccess() =>
            Pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
                ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                    artifacts,
                    visualModel,
                    editorState)));

        internal void EnqueueFullSuccess() =>
            Pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
                ControlledEditingSessionPipeline.Success(
                    EditingSessionTestHarness.CreateArtifacts(Inputs, snapshot.Revision),
                    snapshot.VisualModel,
                    editorState)));

        internal void EnqueueNoRouteFullSuccess() =>
            Pipeline.EnqueueFull((snapshot, editorState, _) =>
            {
                var artifacts = EditingSessionTestHarness.CreateArtifacts(
                    Inputs,
                    snapshot.Revision);
                var connectorEdgeId = artifacts.ProjectedGraph.Edges.Single(edge =>
                    edge.Source.VisualStateId == ConnectorVisualId).Id;
                var routing = artifacts.RoutingResult;
                var noRouteRouting = new RoutingResult(
                    routing.DocumentId,
                    routing.SourceRevision,
                    routing.LayoutAlgorithmId,
                    routing.RoutingAlgorithmId,
                    new RoutingComputation(
                        routing.Routes.Where(route =>
                            route.ProjectedEdgeId != connectorEdgeId),
                        routing.Metadata,
                        [connectorEdgeId]),
                    routing.Diagnostics);
                var noRouteArtifacts = new EditingSessionPipelineArtifacts(
                    artifacts.ProjectedGraph,
                    artifacts.LayoutResult,
                    noRouteRouting);
                return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                    noRouteArtifacts,
                    snapshot.VisualModel,
                    editorState));
            });

        public async ValueTask DisposeAsync()
        {
            await Controller.DisposeAsync();
            await Session.DisposeAsync();
        }
    }

    private sealed class RecordingEndpointFactory(
        bool matches,
        bool throwWhenMatching = false) : IConnectorEndpointReconnectionCommandFactory
    {
        internal List<ConnectorEndpointReconnectionStartRequest> StartRequests { get; } = [];

        public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
        {
            StartRequests.Add(request);
            if (throwWhenMatching)
            {
                throw new InvalidOperationException("Test matching failure.");
            }

            return matches;
        }

        public ConnectorEndpointReconnectionPlanResult CreatePlan(
            ConnectorEndpointReconnectionRequest request) =>
            throw new NotSupportedException("This factory is used only for gesture-start tests.");
    }

    private sealed class RecordingConnectionFactory : IAnchorConnectionCreationCommandFactory
    {
        internal int MatchCount { get; private set; }

        public bool CanStart(AnchorConnectionCreationSourceRequest request)
        {
            MatchCount++;
            return true;
        }

        public AnchorConnectionCreationPlanResult CreatePlan(
            AnchorConnectionCreationRequest request) =>
            throw new NotSupportedException("The N2 gesture must not start in this test.");
    }

    private sealed class BlockingCompletionEndpointFactory :
        IConnectorEndpointReconnectionCommandFactory
    {
        private int _matchCount;

        internal TaskCompletionSource CompletionRevalidationEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseCompletionRevalidation { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
        {
            if (Interlocked.Increment(ref _matchCount) == 2)
            {
                CompletionRevalidationEntered.TrySetResult();
                ReleaseCompletionRevalidation.Task.GetAwaiter().GetResult();
            }

            return true;
        }

        public ConnectorEndpointReconnectionPlanResult CreatePlan(
            ConnectorEndpointReconnectionRequest request) =>
            ConnectorEndpointReconnectionPlanResult.Success(
                new ConnectorEndpointReconnectionPlan(
                    new ReconnectBpmnSequenceFlowEndpointCommand(
                        request.Document.DocumentId,
                        request.ExpectedRevision,
                        request.RelationshipId,
                        request.ConnectorVisualStateId,
                        request.EndpointKind,
                        request.CurrentSemanticElementId,
                        request.CurrentAnchorId,
                        request.CandidateSemanticElementId,
                        request.CandidateAnchorId),
                    request.RelationshipId,
                    request.ConnectorVisualStateId));
    }

    private sealed class CountingPlanningEndpointFactory :
        IConnectorEndpointReconnectionCommandFactory
    {
        private int _matchCount;
        private int _planCount;

        internal int MatchCount => Volatile.Read(ref _matchCount);

        internal int PlanCount => Volatile.Read(ref _planCount);

        public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
        {
            Interlocked.Increment(ref _matchCount);
            return true;
        }

        public ConnectorEndpointReconnectionPlanResult CreatePlan(
            ConnectorEndpointReconnectionRequest request)
        {
            Interlocked.Increment(ref _planCount);
            return SuccessfulPlan(request);
        }
    }

    private sealed class CompletionFailureEndpointFactory(string failureMode) :
        IConnectorEndpointReconnectionCommandFactory
    {
        private int _planCount;

        internal int PlanCount => Volatile.Read(ref _planCount);

        public bool CanStart(ConnectorEndpointReconnectionStartRequest request) => true;

        public ConnectorEndpointReconnectionPlanResult CreatePlan(
            ConnectorEndpointReconnectionRequest request)
        {
            Interlocked.Increment(ref _planCount);
            if (StringComparer.Ordinal.Equals(failureMode, "throw"))
            {
                throw new InvalidOperationException("Test completion planning failure.");
            }

            if (StringComparer.Ordinal.Equals(failureMode, "failure"))
            {
                return ConnectorEndpointReconnectionPlanResult.Failure(
                [
                    new Diagnostic(
                        "TEST_N3_PLAN_FAILURE",
                        DiagnosticSeverity.Error,
                        "The test endpoint-reconnection plan was rejected."),
                ]);
            }

            var command = new ReconnectBpmnSequenceFlowEndpointCommand(
                request.Document.DocumentId,
                request.ExpectedRevision,
                request.RelationshipId,
                request.ConnectorVisualStateId,
                request.EndpointKind,
                request.CurrentSemanticElementId,
                new ConnectorAnchorId("test:n3:wrong-current-anchor"),
                request.CandidateSemanticElementId,
                request.CandidateAnchorId);
            return ConnectorEndpointReconnectionPlanResult.Success(
                new ConnectorEndpointReconnectionPlan(
                    command,
                    request.RelationshipId,
                    request.ConnectorVisualStateId));
        }
    }

    private static ConnectorEndpointReconnectionPlanResult SuccessfulPlan(
        ConnectorEndpointReconnectionRequest request) =>
        ConnectorEndpointReconnectionPlanResult.Success(
            new ConnectorEndpointReconnectionPlan(
                new ReconnectBpmnSequenceFlowEndpointCommand(
                    request.Document.DocumentId,
                    request.ExpectedRevision,
                    request.RelationshipId,
                    request.ConnectorVisualStateId,
                    request.EndpointKind,
                    request.CurrentSemanticElementId,
                    request.CurrentAnchorId,
                    request.CandidateSemanticElementId,
                    request.CandidateAnchorId),
                request.RelationshipId,
                request.ConnectorVisualStateId));

    private sealed class FixedIdentityProvider : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => new(
            new SemanticElementId("test:n2:unused:semantic"),
            new VisualStateId("test:n2:unused:visual"));
    }
}
