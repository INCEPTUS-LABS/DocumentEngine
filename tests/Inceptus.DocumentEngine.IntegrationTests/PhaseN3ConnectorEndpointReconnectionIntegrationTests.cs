using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using PlacementHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.PlacementHarness;
using SequenceIdentityProvider = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.SequenceIdentityProvider;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN3ConnectorEndpointReconnectionIntegrationTests
{
    [Fact]
    public async Task TargetEndpointReconnectionCanCommitAsSelectedNoRouteFallbackAndUndoRecovers()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("no-route-target");
        var setupDocument = harness.Document;
        var gateway = Assert.Single(setupDocument.VisualModel.VisualStates,
            visual => visual.Id == harness.GatewayVisualId);
        var newTarget = Assert.Single(setupDocument.VisualModel.VisualStates,
            visual => visual.Id == harness.NewTargetVisualId);
        var gatewayBounds = new RectD(
            gateway.Position.X,
            gateway.Position.Y,
            gateway.Size.Width,
            gateway.Size.Height);
        var gatewayCenter = Center(gatewayBounds);
        var noRouteTargetPosition = new PointD(
            gatewayCenter.X,
            gatewayCenter.Y - (newTarget.Size.Height / 2d));
        var moved = await harness.Placement.Session.ExecuteAsync(
            new MoveVisualStateCommand(
                setupDocument.DocumentId,
                setupDocument.Revision,
                harness.NewTargetVisualId,
                noRouteTargetPosition,
                VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted);
        await harness.Placement.WaitForIdleAsync();

        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeHistory = beforeState.HistoryStatus;
        var beforeRelationship = harness.PrimaryRelationship;
        var beforeVisual = harness.PrimaryVisual;
        var beforeEdge = Assert.Single(beforeState.ProjectedGraph!.Edges,
            edge => edge.Source.SemanticElementId == harness.PrimaryFlowRelationshipId);
        var beforeRoute = Assert.Single(beforeState.RoutingResult!.Routes,
            route => route.ProjectedEdgeId == beforeEdge.Id);
        Assert.DoesNotContain(beforeEdge.Id, beforeState.RoutingResult.NoRouteEdgeIds);

        var endpointPoint = Center(
            harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);
        var pressed = await harness.Interaction.PointerPressedAsync(
            Pointer(7290, harness.Scene, endpointPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        var candidate = harness.AnchorHandle(
            harness.NewTargetVisualId,
            harness.NewTargetAnchorId);
        Assert.True(IsCandidateHandle(candidate));
        var candidatePoint = Center(candidate.Bounds);
        Assert.Equal(gatewayCenter, candidatePoint);
        Assert.True(candidatePoint.X > gatewayBounds.Left &&
                    candidatePoint.X < gatewayBounds.Right);
        Assert.True(candidatePoint.Y > gatewayBounds.Top &&
                    candidatePoint.Y < gatewayBounds.Bottom);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerMovedAsync(
                Pointer(7290, harness.Scene, candidatePoint, buttons: 1))).Status);

        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(7290, harness.Scene, candidatePoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.DoesNotContain(released.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var reconnection = Assert.IsType<Inceptus.DocumentEngine.Contracts.History
            .HistoryOperationResult>(released.PersistentOperation);
        Assert.True(reconnection.IsCommitted);
        Assert.Equal(
            ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId,
            reconnection.CommandTypeId);
        var afterDocument = harness.Document;
        var afterState = harness.State;
        var afterRelationship = harness.PrimaryRelationship;
        var afterVisual = harness.PrimaryVisual;
        var afterEdge = Assert.Single(afterState.ProjectedGraph!.Edges,
            edge => edge.Source.SemanticElementId == harness.PrimaryFlowRelationshipId);

        Assert.Equal(EditingSessionStatus.Ready, afterState.Status);
        Assert.NotNull(afterState.CurrentScene);
        Assert.Null(afterState.LastKnownGoodScene);
        Assert.False(afterState.IsDisplayingStaleScene);
        Assert.True(afterState.IsGraphicalInteractionEnabled);
        Assert.Equal(beforeDocument.Revision.Increment(), afterDocument.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, afterState.HistoryStatus.EntryCount);
        AssertOnlyRequestedEndpointChanged(
            beforeDocument,
            afterDocument,
            beforeRelationship,
            afterRelationship,
            beforeVisual,
            afterVisual,
            ConnectorEndpointKind.Target,
            harness.NewTargetSemanticId,
            harness.NewTargetAnchorId);
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            afterDocument,
            afterState);
        Assert.Equal(afterEdge.Id, Assert.Single(afterState.RoutingResult!.NoRouteEdgeIds));
        Assert.DoesNotContain(afterState.RoutingResult.Routes,
            route => route.ProjectedEdgeId == afterEdge.Id);
        var expectedFallbackPath = new[] { beforeRoute.SourceAnchor, candidatePoint };
        Assert.Equal(
            expectedFallbackPath,
            harness.ConnectorPoints(harness.PrimaryFlowVisualId));
        Assert.True(harness.HasPrimaryTargetArrow);
        Assert.Equal(
            harness.PrimaryFlowVisualId,
            Assert.Single(afterState.EditorState.Selection));
        Assert.Equal(
            beforeRoute.SourceAnchor,
            Center(harness.EndpointHandle(ConnectorEndpointKind.Source).Bounds));
        Assert.Equal(
            candidatePoint,
            Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds));
        Assert.Contains(harness.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == harness.BlockingFlowVisualId);
        Assert.Contains(afterState.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.SourceIdentity == afterEdge.Id.Value);

        var undo = await harness.Placement.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await harness.Placement.WaitForIdleAsync();

        var recoveredState = harness.State;
        var recoveredEdge = Assert.Single(recoveredState.ProjectedGraph!.Edges,
            edge => edge.Source.SemanticElementId == harness.PrimaryFlowRelationshipId);
        Assert.Equal(EditingSessionStatus.Ready, recoveredState.Status);
        Assert.Equal(beforeRelationship, harness.PrimaryRelationship);
        Assert.Equal(beforeVisual, harness.PrimaryVisual);
        Assert.DoesNotContain(recoveredEdge.Id,
            recoveredState.RoutingResult!.NoRouteEdgeIds);
        Assert.Equal(beforeRoute.Path.AsEnumerable(), Assert.Single(
            recoveredState.RoutingResult.Routes,
            route => route.ProjectedEdgeId == recoveredEdge.Id).Path.AsEnumerable());
        Assert.True(harness.HasPrimaryTargetArrow);

        var redo = await harness.Placement.Session.RedoAsync();
        Assert.True(redo.IsCommitted);
        await harness.Placement.WaitForIdleAsync();

        var redoneState = harness.State;
        var redoneEdge = Assert.Single(redoneState.ProjectedGraph!.Edges,
            edge => edge.Source.SemanticElementId == harness.PrimaryFlowRelationshipId);
        Assert.Equal(EditingSessionStatus.Ready, redoneState.Status);
        Assert.Equal(afterRelationship, harness.PrimaryRelationship);
        Assert.Equal(afterVisual, harness.PrimaryVisual);
        Assert.Contains(redoneEdge.Id, redoneState.RoutingResult!.NoRouteEdgeIds);
        Assert.Equal(
            expectedFallbackPath,
            harness.ConnectorPoints(harness.PrimaryFlowVisualId));
        Assert.True(harness.HasPrimaryTargetArrow);
        Assert.Equal(beforeHistory.EntryCount + 1, redoneState.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task TargetEndpointReconnectsAtomicallyPreservesPreviewAndRoundTripsHistory()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("target");
        Assert.True((await harness.Placement.Session.PanViewportAsync(
            new VectorD(-132.25d, 77.5d))).Succeeded);
        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeRelationship = harness.PrimaryRelationship;
        var beforeVisual = harness.PrimaryVisual;
        var beforeDisplayedRoute = harness.ConnectorPoints(harness.PrimaryFlowVisualId);
        var beforeFullRuns = harness.Placement.Pipeline.FullRunCount;
        var beforePreservingRuns =
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount;
        var endpointPoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);

        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(7300, harness.Scene, endpointPoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Equal(
            Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind,
            harness.State.EditorState.ActiveGesture?.Kind);
        AssertPreviewRoute(
            beforeDisplayedRoute,
            harness.PreviewPoints(),
            ConnectorEndpointKind.Target,
            endpointPoint);

        var targetHandle = harness.AnchorHandle(
            harness.NewTargetVisualId,
            harness.NewTargetAnchorId);
        Assert.True(IsCandidateHandle(targetHandle));
        var targetPoint = Center(targetHandle.Bounds);
        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(7300, harness.Scene, targetPoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        AssertPreviewRoute(
            beforeDisplayedRoute,
            harness.PreviewPoints(),
            ConnectorEndpointKind.Target,
            targetPoint);

        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(7300, harness.Scene, targetPoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation?.IsCommitted);
        var committedDocument = harness.Document;
        var committedState = harness.State;
        var committedRelationship = harness.PrimaryRelationship;
        var committedVisual = harness.PrimaryVisual;
        Assert.Equal(beforeDocument.Revision.Increment(), committedDocument.Revision);
        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1,
            committedState.HistoryStatus.EntryCount);
        Assert.Null(committedState.EditorState.ActiveGesture);
        Assert.Equal(harness.PrimaryFlowVisualId,
            Assert.Single(committedState.EditorState.Selection));
        AssertOnlyRequestedEndpointChanged(
            beforeDocument,
            committedDocument,
            beforeRelationship,
            committedRelationship,
            beforeVisual,
            committedVisual,
            ConnectorEndpointKind.Target,
            harness.NewTargetSemanticId,
            harness.NewTargetAnchorId);
        Assert.True(harness.HasPrimaryTargetArrow);
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 1,
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            committedDocument,
            committedState);

        var undo = await harness.Placement.Session.UndoAsync();
        await harness.Placement.WaitForIdleAsync();

        Assert.True(undo.IsCommitted);
        Assert.Equal(beforeRelationship, harness.PrimaryRelationship);
        Assert.Equal(beforeVisual, harness.PrimaryVisual);
        Assert.Equal(harness.PrimaryFlowVisualId,
            Assert.Single(harness.State.EditorState.Selection));
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 2,
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            harness.Document,
            harness.State);

        var redo = await harness.Placement.Session.RedoAsync();
        await harness.Placement.WaitForIdleAsync();

        Assert.True(redo.IsCommitted);
        Assert.Equal(committedRelationship, harness.PrimaryRelationship);
        Assert.Equal(committedVisual, harness.PrimaryVisual);
        Assert.Equal(harness.PrimaryFlowVisualId,
            Assert.Single(harness.State.EditorState.Selection));
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 3,
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            harness.Document,
            harness.State);

        await AssertSameAnchorNoOpAsync(harness, 7301);
        await AssertOccupiedTargetRejectedAsync(harness, 7302);
        await AssertFreedAnchorAcceptsNewN2FlowAsync(harness, 7303);
    }

    private static async ValueTask AssertFreedAnchorAcceptsNewN2FlowAsync(
        ReconnectionHarness harness,
        long pointerId)
    {
        var selected = await harness.Placement.Session.UpdateEditorStateAsync(
            new EditorStateSnapshot(
                [harness.NewSourceVisualId],
                viewport: harness.State.EditorState.Viewport));
        Assert.True(selected.Succeeded);
        var source = harness.AnchorHandle(
            harness.NewSourceVisualId,
            harness.NewSourceAnchorId);
        var sourcePoint = Center(source.Bounds);
        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(pointerId, harness.Scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);
        var target = harness.AnchorHandle(
            harness.OriginalTargetVisualId,
            harness.OriginalTargetAnchorId);
        Assert.True(IsCandidateHandle(target));
        var targetPoint = Center(target.Bounds);
        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(pointerId, harness.Scene, targetPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(pointerId, harness.Scene, targetPoint));
        await harness.Placement.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        var relationship = Assert.Single(harness.Document.SemanticModel.Relationships,
            item => item.Id == harness.FreedAnchorFlowRelationshipId);
        var visual = Assert.Single(harness.Document.VisualModel.VisualStates,
            item => item.Id == harness.FreedAnchorFlowVisualId);
        Assert.Equal(harness.NewSourceSemanticId, relationship.SourceId);
        Assert.Equal(harness.OriginalTargetSemanticId, relationship.TargetId);
        Assert.Equal(harness.NewSourceAnchorId, visual.SourceAnchorId);
        Assert.Equal(harness.OriginalTargetAnchorId, visual.TargetAnchorId);
    }

    [Fact]
    public async Task SourceEndpointReconnectsWhilePreviewKeepsTargetAndIntermediateBends()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("source");
        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeRelationship = harness.PrimaryRelationship;
        var beforeVisual = harness.PrimaryVisual;
        var beforeDisplayedRoute = harness.ConnectorPoints(harness.PrimaryFlowVisualId);
        var endpointPoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Source).Bounds);

        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(7310, harness.Scene, endpointPoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        AssertPreviewRoute(
            beforeDisplayedRoute,
            harness.PreviewPoints(),
            ConnectorEndpointKind.Source,
            endpointPoint);

        var sourceHandle = harness.AnchorHandle(
            harness.NewSourceVisualId,
            harness.NewSourceAnchorId);
        Assert.True(IsCandidateHandle(sourceHandle));
        var sourcePoint = Center(sourceHandle.Bounds);
        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(7310, harness.Scene, sourcePoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        AssertPreviewRoute(
            beforeDisplayedRoute,
            harness.PreviewPoints(),
            ConnectorEndpointKind.Source,
            sourcePoint);

        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(7310, harness.Scene, sourcePoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation?.IsCommitted);
        Assert.Equal(beforeDocument.Revision.Increment(), harness.Document.Revision);
        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        AssertOnlyRequestedEndpointChanged(
            beforeDocument,
            harness.Document,
            beforeRelationship,
            harness.PrimaryRelationship,
            beforeVisual,
            harness.PrimaryVisual,
            ConnectorEndpointKind.Source,
            harness.NewSourceSemanticId,
            harness.NewSourceAnchorId);
        Assert.True(harness.HasPrimaryTargetArrow);
    }

    [Fact]
    public async Task DistinctAnchorSelfLoopReconnectsAndStillRoutesWithTargetArrow()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("self-loop");
        var endpoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerPressedAsync(
                Pointer(7320, harness.Scene, endpoint, buttons: 1))).Status);
        var candidate = harness.AnchorHandle(
            harness.OriginalSourceVisualId,
            harness.SelfLoopTargetAnchorId);
        var point = Center(candidate.Bounds);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerMovedAsync(
                Pointer(7320, harness.Scene, point, buttons: 1))).Status);
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await harness.Interaction.PointerReleasedAsync(
                Pointer(7320, harness.Scene, point))).Status);
        await harness.Placement.WaitForIdleAsync();
        Assert.Equal(harness.OriginalSourceSemanticId, harness.PrimaryRelationship.SourceId);
        Assert.Equal(harness.OriginalSourceSemanticId, harness.PrimaryRelationship.TargetId);
        Assert.Equal(harness.OriginalSourceAnchorId, harness.PrimaryVisual.SourceAnchorId);
        Assert.Equal(harness.SelfLoopTargetAnchorId, harness.PrimaryVisual.TargetAnchorId);
        Assert.NotNull(harness.State.RoutingResult);
        Assert.True(harness.HasPrimaryTargetArrow);
    }

    [Fact]
    public async Task GatewayEndpointReconnectsAndRebuildsRoutingAndScene()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("gateway");
        var endpoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerPressedAsync(
                Pointer(7321, harness.Scene, endpoint, buttons: 1))).Status);
        var candidate = harness.AnchorHandle(harness.GatewayVisualId, harness.GatewayTargetAnchorId);
        var point = Center(candidate.Bounds);
        _ = await harness.Interaction.PointerMovedAsync(
            Pointer(7321, harness.Scene, point, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await harness.Interaction.PointerReleasedAsync(
                Pointer(7321, harness.Scene, point))).Status);
        await harness.Placement.WaitForIdleAsync();
        Assert.Equal(harness.GatewaySemanticId, harness.PrimaryRelationship.TargetId);
        Assert.Equal(harness.GatewayTargetAnchorId, harness.PrimaryVisual.TargetAnchorId);
        Assert.NotNull(harness.State.ProjectedGraph);
        Assert.NotNull(harness.State.RoutingResult);
        Assert.True(harness.HasPrimaryTargetArrow);
    }

    [Fact]
    public async Task PostReconnectMoveResizeAndBendLifecyclePreservesEndpointBinding()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("follow-on-edits");
        var endpoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);
        _ = await harness.Interaction.PointerPressedAsync(
            Pointer(7330, harness.Scene, endpoint, buttons: 1));
        var target = harness.AnchorHandle(harness.NewTargetVisualId, harness.NewTargetAnchorId);
        var targetPoint = Center(target.Bounds);
        _ = await harness.Interaction.PointerMovedAsync(
            Pointer(7330, harness.Scene, targetPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await harness.Interaction.PointerReleasedAsync(
                Pointer(7330, harness.Scene, targetPoint))).Status);
        await harness.Placement.WaitForIdleAsync();
        AssertReconnectedBindingAndScene(harness);

        var owner = Assert.Single(harness.Document.VisualModel.VisualStates,
            item => item.Id == harness.NewTargetVisualId);
        await ExecuteAuthoritativeAsync(harness, new MoveVisualStateCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            owner.Id,
            owner.Position + new VectorD(25d, 18d),
            VisualPlacementMode.Pinned));
        AssertReconnectedBindingAndScene(harness);

        owner = Assert.Single(harness.Document.VisualModel.VisualStates,
            item => item.Id == harness.NewTargetVisualId);
        await ExecuteAuthoritativeAsync(harness, new ResizeVisualStateCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            owner.Id,
            new RectD(owner.Position.X, owner.Position.Y,
                owner.Size.Width + 24d, owner.Size.Height + 16d),
            VisualPlacementMode.Pinned));
        AssertReconnectedBindingAndScene(harness);

        var route = harness.PrimaryVisual.Route.ToArray();
        var added = route.Take(2)
            .Append(new PointD(route[1].X + 19d, route[1].Y + 13d))
            .Concat(route.Skip(2)).ToArray();
        await ExecuteAuthoritativeAsync(harness, RouteCommand(harness, added));
        AssertReconnectedBindingAndScene(harness);

        var moved = added.ToArray();
        moved[2] = moved[2] + new VectorD(17d, -11d);
        await ExecuteAuthoritativeAsync(harness, RouteCommand(harness, moved));
        AssertReconnectedBindingAndScene(harness);
        Assert.True((await harness.Placement.Session.UndoAsync()).IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertReconnectedBindingAndScene(harness);
        Assert.Equal(added, harness.PrimaryVisual.Route);
        Assert.True((await harness.Placement.Session.RedoAsync()).IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertReconnectedBindingAndScene(harness);
        Assert.Equal(moved, harness.PrimaryVisual.Route);

        var deleted = moved.Where((_, index) => index != 2).ToArray();
        await ExecuteAuthoritativeAsync(harness, RouteCommand(harness, deleted));
        AssertReconnectedBindingAndScene(harness);
        Assert.Equal(deleted, harness.PrimaryVisual.Route);
    }

    [Fact]
    public async Task StartEndpointCanMoveBetweenDistinctSourceAnchorsOnSameOwner()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("same-owner-source");
        var relationshipId = harness.PrimaryRelationship.Id;
        await ReconnectAsync(harness, ConnectorEndpointKind.Source,
            harness.OriginalSourceVisualId, harness.AlternateSourceAnchorId, 7340);
        Assert.Equal(relationshipId, harness.PrimaryRelationship.Id);
        Assert.Equal(harness.OriginalSourceSemanticId, harness.PrimaryRelationship.SourceId);
        Assert.Equal(harness.AlternateSourceAnchorId, harness.PrimaryVisual.SourceAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel, harness.OriginalSourceAnchorId));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel, harness.AlternateSourceAnchorId));
        AssertReconnectedScene(harness);
    }

    [Fact]
    public async Task GatewayStartEndpointMovesWithinGatewayThenToAnotherFlowNode()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("gateway-source");
        var relationshipId = harness.PrimaryRelationship.Id;
        await ReconnectAsync(harness, ConnectorEndpointKind.Source,
            harness.GatewayVisualId, harness.GatewaySourceAnchorId, 7341);
        Assert.Equal(harness.GatewaySemanticId, harness.PrimaryRelationship.SourceId);
        await ReconnectAsync(harness, ConnectorEndpointKind.Source,
            harness.GatewayVisualId, harness.GatewayAlternateSourceAnchorId, 7342);
        Assert.Equal(relationshipId, harness.PrimaryRelationship.Id);
        Assert.Equal(harness.GatewaySemanticId, harness.PrimaryRelationship.SourceId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel, harness.GatewaySourceAnchorId));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel, harness.GatewayAlternateSourceAnchorId));
        await ReconnectAsync(harness, ConnectorEndpointKind.Source,
            harness.NewSourceVisualId, harness.NewSourceAnchorId, 7343);
        Assert.Equal(relationshipId, harness.PrimaryRelationship.Id);
        Assert.Equal(harness.NewSourceSemanticId, harness.PrimaryRelationship.SourceId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel, harness.GatewayAlternateSourceAnchorId));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel, harness.NewSourceAnchorId));
        AssertReconnectedScene(harness);
    }

    [Fact]
    public async Task FreeAnchorAddAndRemoveAfterReconnectPreservesExactBinding()
    {
        await using var harness = await ReconnectionHarness.CreateAsync("free-anchor-lifecycle");
        await ReconnectAsync(harness, ConnectorEndpointKind.Target,
            harness.NewTargetVisualId, harness.NewTargetAnchorId, 7344);
        var relationshipId = harness.PrimaryRelationship.Id;
        var boundAnchorId = harness.PrimaryVisual.TargetAnchorId;
        var endpointBeforeAdd = harness.ConnectorPoints(harness.PrimaryFlowVisualId)[^1];
        var freeAnchorId = new ConnectorAnchorId(
            "test:n3:endpoint-reconnection:free-anchor-lifecycle:temporary-free:anchor");
        var document = harness.Document;
        await ExecuteAuthoritativeAsync(harness, new AddConnectorAnchorCommand(
            document.DocumentId, document.Revision, harness.NewTargetVisualId,
            freeAnchorId, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 1));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(harness.Document.VisualModel, freeAnchorId));
        Assert.Equal(relationshipId, harness.PrimaryRelationship.Id);
        Assert.Equal(boundAnchorId, harness.PrimaryVisual.TargetAnchorId);
        var endpointAfterAdd = harness.ConnectorPoints(harness.PrimaryFlowVisualId)[^1];
        Assert.NotEqual(endpointBeforeAdd, endpointAfterAdd);
        AssertReconnectedScene(harness);
        document = harness.Document;
        await ExecuteAuthoritativeAsync(harness, new RemoveConnectorAnchorCommand(
            document.DocumentId, document.Revision, harness.NewTargetVisualId, freeAnchorId));
        Assert.Equal(relationshipId, harness.PrimaryRelationship.Id);
        Assert.Equal(boundAnchorId, harness.PrimaryVisual.TargetAnchorId);
        var endpointAfterRemove = harness.ConnectorPoints(harness.PrimaryFlowVisualId)[^1];
        Assert.NotEqual(endpointAfterAdd, endpointAfterRemove);
        Assert.Equal(endpointBeforeAdd, endpointAfterRemove);
        AssertReconnectedScene(harness);
    }

    private static void AssertNodeGeometryUnchanged(
        DocumentSnapshot beforeDocument,
        Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState beforeState,
        DocumentSnapshot afterDocument,
        Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState afterState)
    {
        Assert.True(beforeState.LayoutResult!.Nodes.AsSpan().SequenceEqual(
            afterState.LayoutResult!.Nodes.AsSpan()));
        var elementIds = beforeDocument.SemanticModel.Elements
            .Where(element => beforeDocument.SemanticModel.GetScope(element.Id).Id ==
                beforeState.ActiveScopeId)
            .Select(static element => element.Id)
            .ToHashSet();
        var beforeScene = Assert.IsType<Canvas2DScene>(beforeState.CurrentScene);
        var afterScene = Assert.IsType<Canvas2DScene>(afterState.CurrentScene);
        foreach (var beforeVisual in beforeDocument.VisualModel.VisualStates.Where(
                     visual => elementIds.Contains(visual.SemanticElementId)))
        {
            var afterVisual = Assert.Single(
                afterDocument.VisualModel.VisualStates,
                visual => visual.Id == beforeVisual.Id);
            Assert.Equal(beforeVisual.Position, afterVisual.Position);
            Assert.Equal(beforeVisual.Size, afterVisual.Size);
            Assert.Equal(beforeVisual.PlacementMode, afterVisual.PlacementMode);
            Assert.Equal(
                NodeBody(beforeScene, beforeVisual.Id).Bounds,
                NodeBody(afterScene, beforeVisual.Id).Bounds);
        }
    }

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedObjectId, "node"));

    private static async ValueTask ReconnectAsync(
        ReconnectionHarness harness,
        ConnectorEndpointKind endpointKind,
        VisualStateId candidateVisualId,
        ConnectorAnchorId candidateAnchorId,
        long pointerId)
    {
        var endpoint = Center(harness.EndpointHandle(endpointKind).Bounds);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerPressedAsync(
                Pointer(pointerId, harness.Scene, endpoint, buttons: 1))).Status);
        var candidate = harness.AnchorHandle(candidateVisualId, candidateAnchorId);
        var point = Center(candidate.Bounds);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerMovedAsync(
                Pointer(pointerId, harness.Scene, point, buttons: 1))).Status);
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await harness.Interaction.PointerReleasedAsync(
                Pointer(pointerId, harness.Scene, point))).Status);
        await harness.Placement.WaitForIdleAsync();
    }

    private static void AssertReconnectedScene(ReconnectionHarness harness)
    {
        Assert.NotNull(harness.State.RoutingResult);
        Assert.True(harness.HasPrimaryTargetArrow);
    }

    private static UpdateConnectionRouteCommand RouteCommand(
        ReconnectionHarness harness,
        IEnumerable<PointD> route) => new(
            harness.Document.DocumentId,
            harness.Document.Revision,
            harness.PrimaryFlowVisualId,
            route);

    private static async ValueTask ExecuteAuthoritativeAsync(
        ReconnectionHarness harness,
        ICommand command)
    {
        var result = await harness.Placement.Session.ExecuteAsync(command);
        Assert.True(result.IsCommitted,
            string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        await harness.Placement.WaitForIdleAsync();
    }

    private static void AssertReconnectedBindingAndScene(ReconnectionHarness harness)
    {
        Assert.Equal(harness.NewTargetSemanticId, harness.PrimaryRelationship.TargetId);
        Assert.Equal(harness.NewTargetAnchorId, harness.PrimaryVisual.TargetAnchorId);
        Assert.NotNull(harness.State.RoutingResult);
        Assert.True(harness.HasPrimaryTargetArrow);
    }

    private static async ValueTask AssertSameAnchorNoOpAsync(
        ReconnectionHarness harness,
        long pointerId)
    {
        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeEvents = harness.Placement.Events.Events.Count;
        var endpointPoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);
        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(pointerId, harness.Scene, endpointPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);

        var originalHandle = harness.AnchorHandle(
            harness.NewTargetVisualId,
            harness.NewTargetAnchorId);
        Assert.True(IsCandidateHandle(originalHandle));
        var originalPoint = Center(originalHandle.Bounds);
        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(pointerId, harness.Scene, originalPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);

        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(pointerId, harness.Scene, originalPoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Equal(beforeEvents, harness.Placement.Events.Events.Count);
        Assert.Null(harness.State.EditorState.ActiveGesture);
        Assert.Equal(harness.PrimaryFlowVisualId,
            Assert.Single(harness.State.EditorState.Selection));
    }

    private static async ValueTask AssertOccupiedTargetRejectedAsync(
        ReconnectionHarness harness,
        long pointerId)
    {
        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeEvents = harness.Placement.Events.Events.Count;
        var occupiedPoint = harness.ConnectorPoints(harness.BlockingFlowVisualId)[^1];
        var endpointPoint = Center(harness.EndpointHandle(ConnectorEndpointKind.Target).Bounds);
        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(pointerId, harness.Scene, endpointPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);
        Assert.DoesNotContain(harness.Scene.Items, item =>
            HasAnchorId(item, harness.OccupiedTargetAnchorId) && IsCandidateHandle(item));

        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(pointerId, harness.Scene, occupiedPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(pointerId, harness.Scene, occupiedPoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Equal(beforeEvents, harness.Placement.Events.Events.Count);
        Assert.Null(harness.State.EditorState.ActiveGesture);
        Assert.Equal(harness.PrimaryFlowVisualId,
            Assert.Single(harness.State.EditorState.Selection));
    }

    private static void AssertPreviewRoute(
        PointD[] original,
        PointD[] preview,
        ConnectorEndpointKind endpointKind,
        PointD movingPoint)
    {
        Assert.Equal(original.Length, preview.Length);
        Assert.Equal(original[1..^1], preview[1..^1]);
        if (endpointKind == ConnectorEndpointKind.Source)
        {
            Assert.Equal(movingPoint, preview[0]);
            Assert.Equal(original[^1], preview[^1]);
        }
        else
        {
            Assert.Equal(original[0], preview[0]);
            Assert.Equal(movingPoint, preview[^1]);
        }
    }

    private static void AssertOnlyRequestedEndpointChanged(
        DocumentSnapshot beforeDocument,
        DocumentSnapshot afterDocument,
        SemanticRelationshipSnapshot beforeRelationship,
        SemanticRelationshipSnapshot afterRelationship,
        VisualStateSnapshot beforeVisual,
        VisualStateSnapshot afterVisual,
        ConnectorEndpointKind endpointKind,
        SemanticElementId newSemanticElementId,
        ConnectorAnchorId newAnchorId)
    {
        Assert.True(beforeDocument.SemanticModel.Elements.AsSpan().SequenceEqual(
            afterDocument.SemanticModel.Elements.AsSpan()));
        Assert.True(beforeDocument.SemanticModel.Relationships
            .Where(item => item.Id != beforeRelationship.Id)
            .SequenceEqual(afterDocument.SemanticModel.Relationships.Where(item =>
                item.Id != afterRelationship.Id)));
        Assert.True(beforeDocument.VisualModel.VisualStates
            .Where(item => item.Id != beforeVisual.Id)
            .SequenceEqual(afterDocument.VisualModel.VisualStates.Where(item =>
                item.Id != afterVisual.Id)));
        Assert.Equal(beforeDocument.Metadata.SystemManagedProperties,
            afterDocument.Metadata.SystemManagedProperties);
        Assert.Equal(beforeDocument.Metadata.ExtensionProperties,
            afterDocument.Metadata.ExtensionProperties);

        Assert.Equal(beforeRelationship.Id, afterRelationship.Id);
        Assert.Equal(beforeRelationship.TypeId, afterRelationship.TypeId);
        Assert.Equal(beforeRelationship.Properties, afterRelationship.Properties);
        Assert.Equal(
            endpointKind == ConnectorEndpointKind.Source
                ? newSemanticElementId
                : beforeRelationship.SourceId,
            afterRelationship.SourceId);
        Assert.Equal(
            endpointKind == ConnectorEndpointKind.Target
                ? newSemanticElementId
                : beforeRelationship.TargetId,
            afterRelationship.TargetId);

        Assert.Equal(beforeVisual.Id, afterVisual.Id);
        Assert.Equal(beforeVisual.SemanticElementId, afterVisual.SemanticElementId);
        Assert.Equal(beforeVisual.Position, afterVisual.Position);
        Assert.Equal(beforeVisual.Size, afterVisual.Size);
        Assert.Equal(beforeVisual.PlacementMode, afterVisual.PlacementMode);
        Assert.Equal(beforeVisual.Route, afterVisual.Route);
        Assert.Equal(beforeVisual.Properties, afterVisual.Properties);
        Assert.Equal(beforeVisual.ConnectorAnchors, afterVisual.ConnectorAnchors);
        Assert.Equal(
            endpointKind == ConnectorEndpointKind.Source
                ? newAnchorId
                : beforeVisual.SourceAnchorId,
            afterVisual.SourceAnchorId);
        Assert.Equal(
            endpointKind == ConnectorEndpointKind.Target
                ? newAnchorId
                : beforeVisual.TargetAnchorId,
            afterVisual.TargetAnchorId);
    }

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

    private static bool HasAnchorId(
        Canvas2DSceneItem item,
        ConnectorAnchorId anchorId) =>
        item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var value) &&
        value.Kind == PropertyValueKind.Text &&
        StringComparer.Ordinal.Equals(value.TextValue, anchorId.Value);

    private static bool IsCandidateHandle(Canvas2DSceneItem item) =>
        item.Metadata.TryGetValue(
            Canvas2DConnectorAnchorMetadata.ConnectionTargetCandidate,
            out var value) &&
        value.Kind == PropertyValueKind.Boolean &&
        value.BooleanValue;

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private sealed class ReconnectionHarness : IAsyncDisposable
    {
        private ReconnectionHarness(
            PlacementHarness placement,
            Canvas2DInteractionController interaction,
            string suffix)
        {
            Placement = placement;
            Interaction = interaction;
            OriginalSourceSemanticId = Id($"{suffix}:original-source");
            OriginalSourceVisualId = VisualId($"{suffix}:original-source");
            OriginalTargetSemanticId = Id($"{suffix}:original-target");
            OriginalTargetVisualId = VisualId($"{suffix}:original-target");
            NewTargetSemanticId = Id($"{suffix}:new-target");
            NewTargetVisualId = VisualId($"{suffix}:new-target");
            NewSourceSemanticId = Id($"{suffix}:new-source");
            NewSourceVisualId = VisualId($"{suffix}:new-source");
            GatewaySemanticId = Id($"{suffix}:gateway");
            GatewayVisualId = VisualId($"{suffix}:gateway");
            OriginalSourceAnchorId = AnchorId($"{suffix}:original-source");
            AlternateSourceAnchorId = AnchorId($"{suffix}:alternate-source");
            OriginalTargetAnchorId = AnchorId($"{suffix}:original-target");
            SelfLoopTargetAnchorId = AnchorId($"{suffix}:self-loop-target");
            NewTargetAnchorId = AnchorId($"{suffix}:new-target");
            OccupiedTargetAnchorId = AnchorId($"{suffix}:occupied-target");
            NewSourceAnchorId = AnchorId($"{suffix}:new-source");
            BlockingSourceAnchorId = AnchorId($"{suffix}:blocking-source");
            GatewayTargetAnchorId = AnchorId($"{suffix}:gateway-target");
            GatewaySourceAnchorId = AnchorId($"{suffix}:gateway-source");
            GatewayAlternateSourceAnchorId = AnchorId($"{suffix}:gateway-alternate-source");
            PrimaryFlowRelationshipId = Id($"{suffix}:primary-flow");
            PrimaryFlowVisualId = VisualId($"{suffix}:primary-flow");
            BlockingFlowRelationshipId = Id($"{suffix}:blocking-flow");
            BlockingFlowVisualId = VisualId($"{suffix}:blocking-flow");
            FreedAnchorFlowRelationshipId = Id($"{suffix}:freed-anchor-flow");
            FreedAnchorFlowVisualId = VisualId($"{suffix}:freed-anchor-flow");
        }

        internal PlacementHarness Placement { get; }

        internal Canvas2DInteractionController Interaction { get; }

        internal SemanticElementId OriginalSourceSemanticId { get; }

        internal VisualStateId OriginalSourceVisualId { get; }

        internal SemanticElementId OriginalTargetSemanticId { get; }

        internal VisualStateId OriginalTargetVisualId { get; }

        internal SemanticElementId NewTargetSemanticId { get; }

        internal VisualStateId NewTargetVisualId { get; }

        internal SemanticElementId NewSourceSemanticId { get; }

        internal VisualStateId NewSourceVisualId { get; }

        internal SemanticElementId GatewaySemanticId { get; }

        internal VisualStateId GatewayVisualId { get; }

        internal ConnectorAnchorId OriginalSourceAnchorId { get; }

        internal ConnectorAnchorId AlternateSourceAnchorId { get; }

        internal ConnectorAnchorId OriginalTargetAnchorId { get; }

        internal ConnectorAnchorId SelfLoopTargetAnchorId { get; }

        internal ConnectorAnchorId NewTargetAnchorId { get; }

        internal ConnectorAnchorId OccupiedTargetAnchorId { get; }

        internal ConnectorAnchorId NewSourceAnchorId { get; }

        internal ConnectorAnchorId BlockingSourceAnchorId { get; }

        internal ConnectorAnchorId GatewayTargetAnchorId { get; }

        internal ConnectorAnchorId GatewaySourceAnchorId { get; }

        internal ConnectorAnchorId GatewayAlternateSourceAnchorId { get; }

        internal SemanticElementId PrimaryFlowRelationshipId { get; }

        internal VisualStateId PrimaryFlowVisualId { get; }

        internal SemanticElementId BlockingFlowRelationshipId { get; }

        internal VisualStateId BlockingFlowVisualId { get; }

        internal SemanticElementId FreedAnchorFlowRelationshipId { get; }

        internal VisualStateId FreedAnchorFlowVisualId { get; }

        internal DocumentSnapshot Document =>
            Placement.Composition.Document.CaptureSnapshot();

        internal Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState State =>
            Placement.Session.CaptureState();

        internal Canvas2DScene Scene => Assert.IsType<Canvas2DScene>(State.CurrentScene);

        internal SemanticRelationshipSnapshot PrimaryRelationship =>
            Assert.Single(Document.SemanticModel.Relationships, item =>
                item.Id == PrimaryFlowRelationshipId);

        internal VisualStateSnapshot PrimaryVisual =>
            Assert.Single(Document.VisualModel.VisualStates, item =>
                item.Id == PrimaryFlowVisualId);

        internal bool HasPrimaryTargetArrow => Scene.Items.Any(item =>
            item.Origin.VisualStateId == PrimaryFlowVisualId &&
            item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));

        internal Canvas2DSceneItem EndpointHandle(ConnectorEndpointKind endpointKind)
        {
            var role = endpointKind == ConnectorEndpointKind.Source
                ? Canvas2DConnectorEndpointMetadata.StartEndpointRole
                : Canvas2DConnectorEndpointMetadata.EndEndpointRole;
            return Assert.Single(Scene.Items, item =>
                item.Origin.VisualStateId == PrimaryFlowVisualId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorEndpointMetadata.HandleRole,
                    out var value) &&
                value.Kind == PropertyValueKind.Text &&
                StringComparer.Ordinal.Equals(value.TextValue, role));
        }

        internal Canvas2DSceneItem AnchorHandle(
            VisualStateId visualStateId,
            ConnectorAnchorId anchorId) =>
            Assert.Single(Scene.Items, item =>
                item.Origin.VisualStateId == visualStateId &&
                HasAnchorId(item, anchorId));

        internal PointD[] ConnectorPoints(VisualStateId visualStateId)
        {
            var connector = Assert.Single(Scene.Items, item =>
                item.Layer == Canvas2DSceneLayer.Connector &&
                item.Origin.VisualStateId == visualStateId &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
                item.Metadata.ContainsKey(
                    Canvas2DConnectorPathMetadata.LogicalPathPointCount) &&
                !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
            return connector.Geometry.Points
                .Select(connector.Transform.TransformPoint)
                .ToArray();
        }

        internal PointD[] PreviewPoints()
        {
            var preview = Assert.Single(Scene.Items, item =>
                item.Origin.VisualStateId == PrimaryFlowVisualId &&
                item.Origin.StableSourceKey?.StartsWith(
                    "connector-endpoint-reconnection-preview:",
                    StringComparison.Ordinal) == true);
            return preview.Geometry.Points
                .Select(preview.Transform.TransformPoint)
                .ToArray();
        }

        internal static async ValueTask<ReconnectionHarness> CreateAsync(string suffix)
        {
            var identities = new[]
            {
                Identity($"{suffix}:original-source"),
                Identity($"{suffix}:original-target"),
                Identity($"{suffix}:new-target"),
                Identity($"{suffix}:new-source"),
                Identity($"{suffix}:gateway"),
            };
            var placement = await PlacementHarness.CreateAsync(
                identities: new SequenceIdentityProvider(identities));
            Canvas2DInteractionController? interaction = null;
            try
            {
                await PlaceTaskAsync(placement, new PointD(1120d, 1220d));
                await PlaceTaskAsync(placement, new PointD(1420d, 1220d));
                await PlaceTaskAsync(placement, new PointD(1420d, 1470d));
                await PlaceTaskAsync(placement, new PointD(1120d, 1470d));
                await PlaceNodeAsync(
                    placement,
                    BpmnSemanticTypes.ExclusiveGateway,
                    new PointD(1690d, 1350d));

                var harness = new ReconnectionHarness(
                    placement,
                    null!,
                    suffix);
                await AddAnchorAsync(
                    placement,
                    harness.OriginalSourceVisualId,
                    harness.OriginalSourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source);
                await AddAnchorAsync(
                    placement,
                    harness.OriginalSourceVisualId,
                    harness.SelfLoopTargetAnchorId,
                    ConnectorAnchorSide.Top,
                    ConnectorAnchorRole.Target);
                await AddAnchorAsync(
                    placement,
                    harness.OriginalSourceVisualId,
                    harness.AlternateSourceAnchorId,
                    ConnectorAnchorSide.Bottom,
                    ConnectorAnchorRole.Source);
                await AddAnchorAsync(
                    placement,
                    harness.OriginalTargetVisualId,
                    harness.OriginalTargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target);
                await AddAnchorAsync(
                    placement,
                    harness.NewTargetVisualId,
                    harness.NewTargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target);
                await AddAnchorAsync(
                    placement,
                    harness.NewTargetVisualId,
                    harness.OccupiedTargetAnchorId,
                    ConnectorAnchorSide.Top,
                    ConnectorAnchorRole.Target);
                await AddAnchorAsync(
                    placement,
                    harness.NewSourceVisualId,
                    harness.NewSourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source);
                await AddAnchorAsync(
                    placement,
                    harness.NewSourceVisualId,
                    harness.BlockingSourceAnchorId,
                    ConnectorAnchorSide.Bottom,
                    ConnectorAnchorRole.Source);
                await AddAnchorAsync(
                    placement,
                    harness.GatewayVisualId,
                    harness.GatewayTargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target);
                await AddAnchorAsync(
                    placement,
                    harness.GatewayVisualId,
                    harness.GatewaySourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source);
                await AddAnchorAsync(
                    placement,
                    harness.GatewayVisualId,
                    harness.GatewayAlternateSourceAnchorId,
                    ConnectorAnchorSide.Bottom,
                    ConnectorAnchorRole.Source);

                var document = placement.Composition.Document.CaptureSnapshot();
                var sourcePoint = ResolveAnchorPoint(
                    document,
                    harness.OriginalSourceVisualId,
                    harness.OriginalSourceAnchorId);
                var targetPoint = ResolveAnchorPoint(
                    document,
                    harness.OriginalTargetVisualId,
                    harness.OriginalTargetAnchorId);
                var primaryRoute = new[]
                {
                    sourcePoint,
                    new PointD(sourcePoint.X + 90d, sourcePoint.Y - 75d),
                    new PointD(targetPoint.X - 90d, targetPoint.Y + 65d),
                    targetPoint,
                };
                await ExecuteAsync(
                    placement,
                    new CreateBpmnSequenceFlowCommand(
                        document.DocumentId,
                        document.Revision,
                        harness.PrimaryFlowRelationshipId,
                        harness.PrimaryFlowVisualId,
                        harness.OriginalSourceSemanticId,
                        harness.OriginalTargetSemanticId,
                        harness.OriginalSourceAnchorId,
                        harness.OriginalTargetAnchorId,
                        primaryRoute,
                        name: "Preserved Phase N3 label",
                        description: "Preserved Phase N3 relationship properties."));

                document = placement.Composition.Document.CaptureSnapshot();
                await ExecuteAsync(
                    placement,
                    new CreateBpmnSequenceFlowCommand(
                        document.DocumentId,
                        document.Revision,
                        harness.BlockingFlowRelationshipId,
                        harness.BlockingFlowVisualId,
                        harness.NewSourceSemanticId,
                        harness.NewTargetSemanticId,
                        harness.BlockingSourceAnchorId,
                        harness.OccupiedTargetAnchorId,
                        name: "Occupancy guard"));

                var selected = await placement.Session.UpdateEditorStateAsync(
                    new EditorStateSnapshot(
                        [harness.PrimaryFlowVisualId],
                        viewport: placement.Session.CaptureState().EditorState.Viewport));
                Assert.True(selected.Succeeded);

                interaction = new Canvas2DInteractionController(
                    placement.Session,
                    connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                        BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations),
                    creationIdentityProvider: new SequenceIdentityProvider(
                        new DocumentCreationIdentity(
                            harness.FreedAnchorFlowRelationshipId,
                            harness.FreedAnchorFlowVisualId)),
                    endpointReconnectionCatalog: new ConnectorEndpointReconnectionCatalog(
                        BpmnPluginRegistration.N3
                            .ConnectorEndpointReconnectionRegistrations));
                return new ReconnectionHarness(placement, interaction, suffix);
            }
            catch
            {
                if (interaction is not null)
                {
                    await interaction.DisposeAsync();
                }

                await placement.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Interaction.DisposeAsync();
            await Placement.DisposeAsync();
        }

        private static async ValueTask PlaceTaskAsync(
            PlacementHarness placement,
            PointD center) =>
            await PlaceNodeAsync(placement, BpmnSemanticTypes.Task, center);

        private static async ValueTask PlaceNodeAsync(
            PlacementHarness placement,
            SemanticTypeId semanticTypeId,
            PointD center)
        {
            var item = placement.Item(semanticTypeId);
            Assert.True(placement.Selection.Select(item.ItemId));
            var state = placement.Session.CaptureState();
            Assert.True(
                state.CurrentScene is Canvas2DScene,
                $"Expected a current Scene before placing {semanticTypeId} at {center}; " +
                $"session status was {state.Status}.{Environment.NewLine}" +
                string.Join(Environment.NewLine, state.RuntimeDiagnostics.Select(item =>
                    $"{item.Code}: {item.Message}")));
            var cssPoint = state.CurrentScene!.ViewportTransform.TransformPoint(center);
            var result = await placement.Controller.TryPlaceAtCssPointAsync(
                placement.Session,
                cssPoint);
            Assert.True(result.IsCommitted);
            await placement.WaitForIdleAsync();
        }

        private static async ValueTask AddAnchorAsync(
            PlacementHarness placement,
            VisualStateId visualStateId,
            ConnectorAnchorId anchorId,
            ConnectorAnchorSide side,
            ConnectorAnchorRole role)
        {
            var document = placement.Composition.Document.CaptureSnapshot();
            var visual = Assert.Single(document.VisualModel.VisualStates, item =>
                item.Id == visualStateId);
            var insertionIndex = visual.ConnectorAnchors.Count(anchor => anchor.Side == side);
            await ExecuteAsync(
                placement,
                new AddConnectorAnchorCommand(
                    document.DocumentId,
                    document.Revision,
                    visualStateId,
                    anchorId,
                    side,
                    role,
                    insertionIndex));
        }

        private static async ValueTask ExecuteAsync(
            PlacementHarness placement,
            ICommand command)
        {
            var result = await placement.Session.ExecuteAsync(command);
            Assert.True(
                result.IsCommitted,
                string.Join(Environment.NewLine, result.Diagnostics.Select(item =>
                    $"{item.Code}: {item.Message}")));
            await placement.WaitForIdleAsync();
        }

        private static PointD ResolveAnchorPoint(
            DocumentSnapshot document,
            VisualStateId visualStateId,
            ConnectorAnchorId anchorId)
        {
            var visual = Assert.Single(document.VisualModel.VisualStates, item =>
                item.Id == visualStateId);
            var anchor = Assert.Single(visual.ConnectorAnchors, item => item.Id == anchorId);
            var sideCount = visual.ConnectorAnchors.Count(item => item.Side == anchor.Side);
            return ConnectorAnchorGeometryResolver.ResolvePoint(
                new RectD(
                    visual.Position.X,
                    visual.Position.Y,
                    visual.Size.Width,
                    visual.Size.Height),
                anchor.Side,
                anchor.Order,
                sideCount);
        }

        private static DocumentCreationIdentity Identity(string suffix) => new(
            Id(suffix),
            VisualId(suffix));

        private static SemanticElementId Id(string suffix) =>
            new($"test:n3:endpoint-reconnection:{suffix}");

        private static VisualStateId VisualId(string suffix) =>
            new($"test:n3:endpoint-reconnection:{suffix}:visual");

        private static ConnectorAnchorId AnchorId(string suffix) =>
            new($"test:n3:endpoint-reconnection:{suffix}:anchor");
    }
}
