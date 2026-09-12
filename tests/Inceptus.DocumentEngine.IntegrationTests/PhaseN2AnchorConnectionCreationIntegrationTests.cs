using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
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
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using PlacementHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.PlacementHarness;
using SequenceIdentityProvider = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.SequenceIdentityProvider;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN2AnchorConnectionCreationIntegrationTests
{
    [Fact]
    public async Task ValidCreationCanCommitAsSelectedNoRouteFallbackAndNodeCascadeDeletesByIdentity()
    {
        await using var harness = await ConnectionHarness.CreateAsync("no-route-creation");
        var targetBeforeMove = Assert.Single(harness.Document.VisualModel.VisualStates,
            visual => visual.Id == harness.TargetVisualId);
        var sourceBeforeMove = Assert.Single(harness.Document.VisualModel.VisualStates,
            visual => visual.Id == harness.SourceVisualId);
        var overlappingTargetPosition = sourceBeforeMove.Position +
            new VectorD(sourceBeforeMove.Size.Width - 50d, 0d);
        var moved = await harness.Placement.Session.ExecuteAsync(
            new MoveVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.TargetVisualId,
                overlappingTargetPosition,
                VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted);
        await harness.Placement.WaitForIdleAsync();

        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeHistory = beforeState.HistoryStatus;
        var sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        var pressed = await harness.Interaction.PointerPressedAsync(
            Pointer(7090, harness.Scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        var targetHandle = harness.AnchorHandle(
            harness.TargetVisualId,
            harness.TargetAnchorId);
        Assert.True(IsCandidateHandle(targetHandle));
        var targetPoint = Center(targetHandle.Bounds);
        var targetBounds = new RectD(
            overlappingTargetPosition.X,
            overlappingTargetPosition.Y,
            targetBeforeMove.Size.Width,
            targetBeforeMove.Size.Height);
        var sourceBounds = new RectD(
            sourceBeforeMove.Position.X,
            sourceBeforeMove.Position.Y,
            sourceBeforeMove.Size.Width,
            sourceBeforeMove.Size.Height);
        Assert.True(sourcePoint.X > targetBounds.Left && sourcePoint.X < targetBounds.Right);
        Assert.True(sourcePoint.Y > targetBounds.Top && sourcePoint.Y < targetBounds.Bottom);
        Assert.True(targetPoint.X > sourceBounds.Left && targetPoint.X < sourceBounds.Right);
        Assert.True(targetPoint.Y > sourceBounds.Top && targetPoint.Y < sourceBounds.Bottom);
        Assert.Equal(Canvas2DInteractionStatus.Updated,
            (await harness.Interaction.PointerMovedAsync(
                Pointer(7090, harness.Scene, targetPoint, buttons: 1))).Status);

        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(7090, harness.Scene, targetPoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.DoesNotContain(released.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var creation = Assert.IsType<Inceptus.DocumentEngine.Contracts.History
            .HistoryOperationResult>(released.PersistentOperation);
        Assert.True(creation.IsCommitted);
        Assert.Equal(CreateBpmnSequenceFlowCommand.KnownTypeId, creation.CommandTypeId);
        var createdDocument = harness.Document;
        var createdState = harness.State;
        var relationship = Assert.Single(createdDocument.SemanticModel.Relationships,
            candidate => candidate.Id == harness.FlowRelationshipId);
        var connector = Assert.Single(createdDocument.VisualModel.VisualStates,
            visual => visual.Id == harness.FlowVisualId);
        var edge = Assert.Single(createdState.ProjectedGraph!.Edges,
            candidate => candidate.Source.SemanticElementId == harness.FlowRelationshipId);

        Assert.Equal(EditingSessionStatus.Ready, createdState.Status);
        Assert.NotNull(createdState.CurrentScene);
        Assert.Null(createdState.LastKnownGoodScene);
        Assert.False(createdState.IsDisplayingStaleScene);
        Assert.True(createdState.IsGraphicalInteractionEnabled);
        Assert.Equal(beforeDocument.Revision.Increment(), createdDocument.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, createdState.HistoryStatus.EntryCount);
        Assert.Equal(harness.SourceSemanticId, relationship.SourceId);
        Assert.Equal(harness.TargetSemanticId, relationship.TargetId);
        Assert.Equal(harness.FlowRelationshipId, connector.SemanticElementId);
        Assert.Equal(harness.SourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(harness.TargetAnchorId, connector.TargetAnchorId);
        Assert.Empty(connector.Route);
        Assert.Equal(edge.Id, Assert.Single(createdState.RoutingResult!.NoRouteEdgeIds));
        Assert.DoesNotContain(createdState.RoutingResult.Routes,
            route => route.ProjectedEdgeId == edge.Id);
        Assert.Equal(harness.FlowVisualId, Assert.Single(createdState.EditorState.Selection));
        var fallbackId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var fallback = Assert.Single(harness.Scene.Items, item => item.Id == fallbackId);
        Assert.Equal(harness.FlowRelationshipId, fallback.Origin.SemanticElementId);
        Assert.Equal(harness.FlowVisualId, fallback.Origin.VisualStateId);
        Assert.Equal(edge.Id, fallback.Origin.ProjectedObjectId);
        Assert.Equal(
            new[] { sourcePoint, targetPoint },
            Canvas2DConnectorPathMetadata.Resolve(fallback)
                .Select(fallback.Transform.TransformPoint));
        var targetArrow = Assert.Single(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
        Assert.Equal(
            targetPoint,
            targetArrow.Transform.TransformPoint(targetArrow.Geometry.Points[0]));
        var endpointHandles = harness.Scene.Items.Where(item =>
            item.Origin.VisualStateId == harness.FlowVisualId &&
            item.Metadata.ContainsKey(Canvas2DConnectorEndpointMetadata.HandleRole)).ToArray();
        Assert.Equal(2, endpointHandles.Length);
        Assert.Equal(
            new[] { sourcePoint, targetPoint },
            endpointHandles
                .OrderBy(item => StringComparer.Ordinal.Equals(
                    item.Metadata[Canvas2DConnectorEndpointMetadata.HandleRole].TextValue,
                    Canvas2DConnectorEndpointMetadata.StartEndpointRole)
                        ? 0
                        : 1)
                .Select(item => Center(item.Bounds)));
        Assert.Contains(createdState.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.SourceIdentity == edge.Id.Value);

        var targetBeforeDelete = Assert.Single(createdDocument.SemanticModel.Elements,
            element => element.Id == harness.TargetSemanticId);
        var targetVisualBeforeDelete = Assert.Single(createdDocument.VisualModel.VisualStates,
            visual => visual.Id == harness.TargetVisualId);
        var deleted = await harness.Placement.Session.ExecuteAsync(
            new DeleteBpmnFlowNodeCommand(
                createdDocument.DocumentId,
                createdDocument.Revision,
                harness.SourceSemanticId,
                harness.SourceVisualId));
        Assert.True(deleted.IsCommitted);
        Assert.Equal(DeleteBpmnFlowNodeCommand.KnownTypeId, deleted.CommandTypeId);
        await harness.Placement.WaitForIdleAsync();

        var deletedDocument = harness.Document;
        var deletedState = harness.State;
        Assert.Equal(EditingSessionStatus.Ready, deletedState.Status);
        Assert.NotNull(deletedState.CurrentScene);
        Assert.False(deletedState.IsDisplayingStaleScene);
        Assert.True(deletedState.IsGraphicalInteractionEnabled);
        Assert.Equal(createdDocument.Revision.Increment(), deletedDocument.Revision);
        Assert.Equal(createdState.HistoryStatus.EntryCount + 1,
            deletedState.HistoryStatus.EntryCount);
        Assert.DoesNotContain(deletedDocument.SemanticModel.Elements,
            element => element.Id == harness.SourceSemanticId);
        Assert.DoesNotContain(deletedDocument.SemanticModel.Relationships,
            candidate => candidate.Id == harness.FlowRelationshipId);
        Assert.DoesNotContain(deletedDocument.VisualModel.VisualStates,
            visual => visual.Id == harness.SourceVisualId || visual.Id == harness.FlowVisualId);
        Assert.Equal(targetBeforeDelete, Assert.Single(deletedDocument.SemanticModel.Elements,
            element => element.Id == harness.TargetSemanticId));
        Assert.Equal(targetVisualBeforeDelete,
            Assert.Single(deletedDocument.VisualModel.VisualStates,
                visual => visual.Id == harness.TargetVisualId));
        Assert.DoesNotContain(deletedState.ProjectedGraph!.Edges,
            candidate => candidate.Source.SemanticElementId == harness.FlowRelationshipId);
        Assert.DoesNotContain(deletedState.RuntimeDiagnostics,
            diagnostic => diagnostic.SourceIdentity == edge.Id.Value);
    }

    [Fact]
    public async Task N1NodesAndExistingAnchorsCreateOneRoutedSelectedSequenceFlowWithExactHistory()
    {
        await using var harness = await ConnectionHarness.CreateAsync("success");
        Assert.True((await harness.Placement.Session.PanViewportAsync(
            new VectorD(-173.5d, 86.25d))).Succeeded);
        var beforeDocument = harness.Document;
        var beforeState = harness.State;
        var beforeEvents = harness.Placement.Events.Events.Count;
        var beforeFullRuns = harness.Placement.Pipeline.FullRunCount;
        var beforePreservingRuns =
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount;
        var beforeSceneRuns = harness.Placement.Pipeline.SceneOnlyRunCount;
        var beforeRenders = harness.Placement.Execution.RenderCount;
        var sourceHandle = harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId);
        var sourcePoint = Center(sourceHandle.Bounds);

        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(7100, harness.Scene, sourcePoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(beforeSceneRuns + 1, harness.Placement.Pipeline.SceneOnlyRunCount);
        Assert.Equal(beforeRenders + 1, harness.Placement.Execution.RenderCount);
        Assert.Equal(
            Canvas2DAnchorConnectionGestureMetadata.Kind,
            harness.State.EditorState.ActiveGesture?.Kind);
        Assert.Equal(sourcePoint, harness.State.EditorState.ActiveGesture?.Origin);

        var candidate = harness.AnchorHandle(
            harness.TargetVisualId,
            harness.TargetAnchorId);
        Assert.True(BooleanMetadata(
            candidate,
            Canvas2DConnectorAnchorMetadata.ConnectionTargetCandidate));
        Assert.DoesNotContain(
            harness.State.EditorState.Selection,
            selected => selected == harness.TargetVisualId);
        var targetPoint = Center(candidate.Bounds);
        var initialPreview = Preview(harness.Scene);
        Assert.Equal(
            new[] { sourcePoint, sourcePoint }.AsEnumerable(),
            initialPreview.Geometry.Points.AsEnumerable());
        Assert.Equal(Canvas2DHitTestMode.None, initialPreview.HitTestPolicy.Mode);

        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(7100, harness.Scene, targetPoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
        var snappedPreview = Preview(harness.Scene);
        Assert.Equal(sourcePoint, snappedPreview.Geometry.Points[0]);
        Assert.Equal(targetPoint, snappedPreview.Geometry.Points[^1]);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(beforeSceneRuns + 2, harness.Placement.Pipeline.SceneOnlyRunCount);
        Assert.Equal(beforeRenders + 2, harness.Placement.Execution.RenderCount);

        var unchanged = await harness.Interaction.PointerMovedAsync(
            Pointer(7100, harness.Scene, targetPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, unchanged.Status);
        Assert.Equal(beforeSceneRuns + 2, harness.Placement.Pipeline.SceneOnlyRunCount);
        Assert.Equal(beforeRenders + 2, harness.Placement.Execution.RenderCount);

        var up = await harness.Interaction.PointerReleasedAsync(
            Pointer(7100, harness.Scene, targetPoint));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, up.Status);
        Assert.True(Assert.IsType<Inceptus.DocumentEngine.Contracts.History.HistoryOperationResult>(
            up.PersistentOperation).IsCommitted);
        var afterDocument = harness.Document;
        var afterState = harness.State;
        Assert.Equal(beforeDocument.Revision.Increment(), afterDocument.Revision);
        Assert.Equal(
            beforeDocument.SemanticModel.RelationshipCount + 1,
            afterDocument.SemanticModel.RelationshipCount);
        Assert.Equal(beforeDocument.VisualModel.Count + 1, afterDocument.VisualModel.Count);
        var relationship = Assert.Single(afterDocument.SemanticModel.Relationships, item =>
            item.Id == harness.FlowRelationshipId);
        var connectorVisual = Assert.Single(afterDocument.VisualModel.VisualStates, item =>
            item.Id == harness.FlowVisualId);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, relationship.TypeId);
        Assert.Equal(harness.SourceSemanticId, relationship.SourceId);
        Assert.Equal(harness.TargetSemanticId, relationship.TargetId);
        Assert.Equal(harness.FlowRelationshipId, connectorVisual.SemanticElementId);
        Assert.Equal(harness.SourceAnchorId, connectorVisual.SourceAnchorId);
        Assert.Equal(harness.TargetAnchorId, connectorVisual.TargetAnchorId);
        Assert.Empty(connectorVisual.Route);
        Assert.Equal(harness.FlowVisualId, Assert.Single(afterState.EditorState.Selection));
        Assert.Null(afterState.EditorState.ActiveGesture);
        Assert.DoesNotContain(afterState.CurrentScene!.Items, IsCandidateHandle);
        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1,
            afterState.HistoryStatus.EntryCount);
        Assert.Equal(beforeEvents + 1, harness.Placement.Events.Events.Count);
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 1,
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount);
        Assert.Equal(beforeSceneRuns + 3, harness.Placement.Pipeline.SceneOnlyRunCount);
        Assert.Equal(beforeRenders + 4, harness.Placement.Execution.RenderCount);

        var edge = Assert.Single(afterState.ProjectedGraph!.Edges, item =>
            item.Source.SemanticElementId == harness.FlowRelationshipId);
        var route = Assert.Single(afterState.RoutingResult!.Routes, item =>
            item.ProjectedEdgeId == edge.Id);
        Assert.Equal(sourcePoint, route.SourceAnchor);
        Assert.Equal(targetPoint, route.DestinationAnchor);
        Assert.Equal(sourcePoint, route.Path[0]);
        Assert.Equal(targetPoint, route.Path[^1]);
        Assert.Contains(afterState.CurrentScene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            afterDocument,
            afterState);

        var duplicateUp = await harness.Interaction.PointerReleasedAsync(
            Pointer(7100, harness.Scene, targetPoint));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, duplicateUp.Status);
        Assert.Equal(
            beforeDocument.SemanticModel.RelationshipCount + 1,
            harness.Document.SemanticModel.RelationshipCount);

        var undo = await harness.Placement.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        Assert.DoesNotContain(harness.Document.SemanticModel.Relationships, item =>
            item.Id == harness.FlowRelationshipId);
        Assert.DoesNotContain(harness.Document.VisualModel.VisualStates, item =>
            item.Id == harness.FlowVisualId);
        AssertAnchorStillExists(harness, harness.SourceVisualId, harness.SourceAnchorId);
        AssertAnchorStillExists(harness, harness.TargetVisualId, harness.TargetAnchorId);
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
        Assert.True(redo.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        var restored = Assert.Single(harness.Document.VisualModel.VisualStates, item =>
            item.Id == harness.FlowVisualId);
        Assert.Equal(harness.SourceAnchorId, restored.SourceAnchorId);
        Assert.Equal(harness.TargetAnchorId, restored.TargetAnchorId);
        Assert.Equal(beforeFullRuns, harness.Placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 3,
            harness.Placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            harness.Document,
            harness.State);

        var removal = await harness.Placement.Session.ExecuteAsync(
            new RemoveConnectorAnchorCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.SourceVisualId,
                harness.SourceAnchorId));
        Assert.False(removal.IsCommitted);
        AssertAnchorStillExists(harness, harness.SourceVisualId, harness.SourceAnchorId);

        var stableRelationship = Assert.Single(harness.Document.SemanticModel.Relationships,
            item => item.Id == harness.FlowRelationshipId);
        var sourceVisual = Assert.Single(harness.Document.VisualModel.VisualStates,
            item => item.Id == harness.SourceVisualId);
        var movedSourcePosition = sourceVisual.Position + new VectorD(45d, 25d);
        var moveSource = await harness.Placement.Session.ExecuteAsync(
            new MoveVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.SourceVisualId,
                movedSourcePosition,
                VisualPlacementMode.Pinned));
        Assert.True(moveSource.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Equal(
            new PointD(
                movedSourcePosition.X + sourceVisual.Size.Width,
                movedSourcePosition.Y + (sourceVisual.Size.Height / 2d)),
            RoutedEndpoints(harness).Source);

        var targetVisual = Assert.Single(harness.Document.VisualModel.VisualStates,
            item => item.Id == harness.TargetVisualId);
        var movedTargetPosition = targetVisual.Position + new VectorD(35d, -20d);
        var moveTarget = await harness.Placement.Session.ExecuteAsync(
            new MoveVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.TargetVisualId,
                movedTargetPosition,
                VisualPlacementMode.Pinned));
        Assert.True(moveTarget.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Equal(
            new PointD(
                movedTargetPosition.X,
                movedTargetPosition.Y + (targetVisual.Size.Height / 2d)),
            RoutedEndpoints(harness).Target);

        var resizedSourceBounds = new RectD(
            movedSourcePosition.X,
            movedSourcePosition.Y,
            sourceVisual.Size.Width + 40d,
            sourceVisual.Size.Height + 20d);
        var resizeSource = await harness.Placement.Session.ExecuteAsync(
            new ResizeVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.SourceVisualId,
                resizedSourceBounds,
                VisualPlacementMode.Pinned));
        Assert.True(resizeSource.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Equal(
            new PointD(
                resizedSourceBounds.Right,
                resizedSourceBounds.Y + (resizedSourceBounds.Height / 2d)),
            RoutedEndpoints(harness).Source);

        var resizedTargetBounds = new RectD(
            movedTargetPosition.X,
            movedTargetPosition.Y,
            targetVisual.Size.Width + 30d,
            targetVisual.Size.Height + 15d);
        var resizeTarget = await harness.Placement.Session.ExecuteAsync(
            new ResizeVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.TargetVisualId,
                resizedTargetBounds,
                VisualPlacementMode.Pinned));
        Assert.True(resizeTarget.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Equal(
            new PointD(
                resizedTargetBounds.Left,
                resizedTargetBounds.Y + (resizedTargetBounds.Height / 2d)),
            RoutedEndpoints(harness).Target);

        var endpoints = RoutedEndpoints(harness);
        var addedBend = new PointD(
            (endpoints.Source.X + endpoints.Target.X) / 2d,
            Math.Min(endpoints.Source.Y, endpoints.Target.Y) - 60d);
        var addBend = await harness.Placement.Session.ExecuteAsync(
            new UpdateConnectionRouteCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.FlowVisualId,
                [endpoints.Source, addedBend, endpoints.Target]));
        Assert.True(addBend.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Equal(
            new[] { endpoints.Source, addedBend, endpoints.Target }.AsEnumerable(),
            FlowVisual(harness).Route.AsEnumerable());

        var movedBend = addedBend + new VectorD(25d, 30d);
        var moveBend = await harness.Placement.Session.ExecuteAsync(
            new UpdateConnectionRouteCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.FlowVisualId,
                [endpoints.Source, movedBend, endpoints.Target]));
        Assert.True(moveBend.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Equal(
            new[] { endpoints.Source, movedBend, endpoints.Target }.AsEnumerable(),
            FlowVisual(harness).Route.AsEnumerable());

        var deleteBend = await harness.Placement.Session.ExecuteAsync(
            new UpdateConnectionRouteCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.FlowVisualId,
                []));
        Assert.True(deleteBend.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        AssertFlowStillBound(harness, stableRelationship);
        Assert.Empty(FlowVisual(harness).Route);
        var editedEdge = Assert.Single(harness.State.ProjectedGraph!.Edges,
            item => item.Source.SemanticElementId == harness.FlowRelationshipId);
        Assert.Contains(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                editedEdge.Id,
                "connector-target-arrow"));
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            harness.Document,
            harness.FlowVisualId,
            harness.Placement.Composition.PropertiesSchemaCatalog,
            out var properties));
        Assert.NotNull(properties);
        Assert.True(properties.IsConnector);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, properties.TypeId);
        Assert.Equal(harness.SourceSemanticId, properties.SourceId);
        Assert.Equal(harness.TargetSemanticId, properties.TargetId);
        Assert.NotNull(properties.LabelPlacement);
        Assert.Empty(properties.DataFields);
    }

    [Fact]
    public async Task ConnectionPreviewStopsAtOriginAndOutsideDropCreatesNothing()
    {
        await using var harness = await ConnectionHarness.CreateAsync("outside-boundary");
        var before = harness.Document;
        var beforeHistory = harness.State.HistoryStatus;
        var sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);

        var pressed = await harness.Interaction.PointerPressedAsync(
            Pointer(7101, harness.Scene, sourcePoint, buttons: 1));
        var moved = await harness.Interaction.PointerMovedAsync(
            Pointer(7101, harness.Scene, new PointD(-100d, -100d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        var preview = Preview(harness.Scene);
        Assert.Equal(new PointD(0d, 0d), preview.Geometry.Points[^1]);
        Assert.All(preview.Geometry.Points, point =>
        {
            Assert.True(point.X >= 0d);
            Assert.True(point.Y >= 0d);
        });

        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(7101, harness.Scene, new PointD(-100d, -100d)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Equal(before, harness.Document);
        Assert.Equal(beforeHistory, harness.State.HistoryStatus);
        Assert.Null(harness.State.EditorState.ActiveGesture);
    }

    [Fact]
    public async Task ExistingStartSourceAndTaskTargetCreateAValidSequenceFlow()
    {
        await using var harness = await ConnectionHarness.CreateAsync(
            "start-task",
            BpmnSemanticTypes.StartEvent,
            BpmnSemanticTypes.Task);

        await CommitAndAssertValidFlowAsync(harness, 7150);
    }

    [Fact]
    public async Task ExistingTaskSourceAndEndTargetCreateAValidSequenceFlow()
    {
        await using var harness = await ConnectionHarness.CreateAsync(
            "task-end",
            BpmnSemanticTypes.Task,
            BpmnSemanticTypes.EndEvent);

        await CommitAndAssertValidFlowAsync(harness, 7151);
    }

    [Fact]
    public async Task N31NodeBodyDropCreatesTargetAnchorAndFlowAtomicallyWithExactUndoRedo()
    {
        var sourceIdentity = new DocumentCreationIdentity(
            new SemanticElementId("test:n31:source"),
            new VisualStateId("test:n31:source:visual"));
        var targetIdentity = new DocumentCreationIdentity(
            new SemanticElementId("test:n31:target"),
            new VisualStateId("test:n31:target:visual"));
        var sourceAnchorId = new ConnectorAnchorId("test:n31:source-anchor");
        var proposedAnchorId = new ConnectorAnchorId("test:n2:n31:proposed-target-anchor");
        await using var placement = await PlacementHarness.CreateAsync(
            identities: new SequenceIdentityProvider(sourceIdentity, targetIdentity));
        await PlaceNodeThroughToolboxAsync(
            placement,
            BpmnSemanticTypes.Task,
            new PointD(1120d, 1200d));
        await PlaceNodeThroughToolboxAsync(
            placement,
            BpmnSemanticTypes.Task,
            new PointD(1420d, 1200d));
        await AddAnchorAsync(
            placement,
            sourceIdentity.VisualStateId,
            sourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await SelectVisualAsync(placement, sourceIdentity.VisualStateId);
        await using var interaction = new Canvas2DInteractionController(
            placement.Session,
            connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnPluginRegistration.N31.AnchorConnectionCreationRegistrations),
            creationIdentityProvider: new FixedIdentityProvider("n31"));
        var before = placement.Composition.Document.CaptureSnapshot();
        var beforeHistory = placement.Session.CaptureState().HistoryStatus;
        var beforeEvents = placement.Events.Events.Count;
        var beforeFullRuns = placement.Pipeline.FullRunCount;
        var beforePreservingRuns = placement.Pipeline.NodeLayoutPreservingRunCount;
        var beforeSceneRuns = placement.Pipeline.SceneOnlyRunCount;
        var beforeState = placement.Session.CaptureState();
        Assert.Contains(
            before.VisualModel.VisualStates,
            visual => visual.PlacementMode == VisualPlacementMode.Pinned);
        Assert.Contains(
            before.VisualModel.VisualStates,
            visual => visual.PlacementMode != VisualPlacementMode.Pinned);
        var scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var sourcePoint = Center(AnchorHandle(
            scene,
            sourceIdentity.VisualStateId,
            sourceAnchorId).Bounds);
        var body = NodeBody(scene, targetIdentity.VisualStateId);

        var pressed = await interaction.PointerPressedAsync(
            Pointer(7152, scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var hitTest = new Inceptus.DocumentEngine.Canvas2D.HitTesting
            .Canvas2DSceneHitTestService();
        var bodyPoints = (from x in Enumerable.Range(
                             (int)body.Bounds.Left + 2,
                             (int)body.Bounds.Width - 4)
                          from y in Enumerable.Range(
                              (int)body.Bounds.Top + 2,
                              (int)body.Bounds.Height - 4)
                          where hitTest.HitTest(scene, new PointD(x, y))?.SceneObjectId == body.Id
                          let point = new PointD(x, y)
                          orderby new[]
                          {
                             Math.Abs(point.Y - body.Bounds.Top),
                             Math.Abs(point.X - body.Bounds.Right),
                             Math.Abs(point.Y - body.Bounds.Bottom),
                             Math.Abs(point.X - body.Bounds.Left),
                         }.Min()
                          select point).ToArray();
        Assert.True(
            bodyPoints.Length > 0,
            string.Join(
                "|",
                scene.Items
                    .Where(item => item.Bounds.Intersects(body.Bounds))
                    .Select(item => $"{item.Layer}:{item.Origin.StableSourceKey}:{item.HitTestPolicy.Mode}")));
        var bodyPoint = bodyPoints[0];
        Assert.Equal(
            body.Id,
            hitTest.HitTest(scene, bodyPoint)?.SceneObjectId);
        var moved = await interaction.PointerMovedAsync(
            Pointer(7152, scene, bodyPoint, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.Equal(before, placement.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforeHistory, placement.Session.CaptureState().HistoryStatus);
        Assert.Equal(beforeEvents, placement.Events.Events.Count);
        Assert.Equal(beforeFullRuns, placement.Pipeline.FullRunCount);
        Assert.Equal(beforeSceneRuns + 2, placement.Pipeline.SceneOnlyRunCount);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var preview = Preview(scene);
        var predictedPoint = preview.Geometry.Points[^1];
        var selectedSide = ConnectorTargetEdgeResolver.Resolve(body.Bounds, bodyPoint);
        if (selectedSide is ConnectorAnchorSide.Top or ConnectorAnchorSide.Bottom)
        {
            Assert.Equal(
                selectedSide == ConnectorAnchorSide.Top
                    ? body.Bounds.Top
                    : body.Bounds.Bottom,
                predictedPoint.Y);
        }
        else
        {
            Assert.Equal(
                selectedSide == ConnectorAnchorSide.Left
                    ? body.Bounds.Left
                    : body.Bounds.Right,
                predictedPoint.X);
        }
        Assert.Contains(scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "anchor-connection-proposed-target:",
                StringComparison.Ordinal) == true &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None);

        var released = await interaction.PointerReleasedAsync(
            Pointer(7152, scene, bodyPoint));
        await placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        var after = placement.Composition.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(before.SemanticModel.RelationshipCount + 1,
            after.SemanticModel.RelationshipCount);
        Assert.Equal(before.VisualModel.Count + 1, after.VisualModel.Count);
        Assert.Equal(beforeEvents + 1, placement.Events.Events.Count);
        Assert.Equal(beforeFullRuns, placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 1,
            placement.Pipeline.NodeLayoutPreservingRunCount);
        var targetVisual = after.VisualModel.VisualStates.Single(item =>
            item.Id == targetIdentity.VisualStateId);
        var createdAnchor = Assert.Single(targetVisual.ConnectorAnchors);
        Assert.Equal(proposedAnchorId, createdAnchor.Id);
        Assert.Equal(ConnectorAnchorRole.Target, createdAnchor.Role);
        var connector = after.VisualModel.VisualStates.Single(item =>
            item.Id == new VisualStateId("test:n2:n31:visual"));
        Assert.Equal(sourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(proposedAnchorId, connector.TargetAnchorId);
        Assert.Equal(connector.Id,
            Assert.Single(placement.Session.CaptureState().EditorState.Selection));
        AssertNodeGeometryUnchanged(
            before,
            beforeState,
            after,
            placement.Session.CaptureState());

        Assert.True((await placement.Session.UndoAsync()).IsCommitted);
        await placement.WaitForIdleAsync();
        var undone = placement.Composition.Document.CaptureSnapshot();
        Assert.DoesNotContain(undone.VisualModel.VisualStates, item =>
            item.Id == connector.Id);
        Assert.DoesNotContain(
            undone.VisualModel.VisualStates.Single(item =>
                item.Id == targetIdentity.VisualStateId).ConnectorAnchors,
            item => item.Id == proposedAnchorId);
        Assert.Equal(beforeFullRuns, placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 2,
            placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            before,
            beforeState,
            undone,
            placement.Session.CaptureState());

        Assert.True((await placement.Session.RedoAsync()).IsCommitted);
        await placement.WaitForIdleAsync();
        var redone = placement.Composition.Document.CaptureSnapshot();
        Assert.Contains(
            redone.VisualModel.VisualStates.Single(item =>
                item.Id == targetIdentity.VisualStateId).ConnectorAnchors,
            item => item.Id == proposedAnchorId);
        Assert.Equal(proposedAnchorId,
            redone.VisualModel.VisualStates.Single(item =>
                item.Id == connector.Id).TargetAnchorId);
        Assert.Equal(beforeFullRuns, placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 3,
            placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            before,
            beforeState,
            redone,
            placement.Session.CaptureState());
    }

    [Fact]
    public async Task N311SmartTargetAnchorRedistributesCurrentRoutingWithoutMovingAnyNode()
    {
        await using var placement = await PlacementHarness.CreateAsync();
        Assert.True((await placement.Session.PanViewportAsync(
            new VectorD(91.75d, -64.5d))).Succeeded);
        await SelectVisualAsync(placement, BpmnDemoPipeline.RejectedTaskVisualId);
        await using var interaction = new Canvas2DInteractionController(
            placement.Session,
            connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnPluginRegistration.N31.AnchorConnectionCreationRegistrations),
            creationIdentityProvider: new FixedIdentityProvider("n311-stability"));
        var beforeDocument = placement.Composition.Document.CaptureSnapshot();
        var beforeState = placement.Session.CaptureState();
        var beforeFullRuns = placement.Pipeline.FullRunCount;
        var beforePreservingRuns = placement.Pipeline.NodeLayoutPreservingRunCount;
        var beforeExistingRoute = Route(
            beforeState,
            BpmnDemoPipeline.ThirdSequenceFlowId);
        Assert.Contains(
            beforeDocument.VisualModel.VisualStates,
            visual => visual.PlacementMode != VisualPlacementMode.Pinned);
        var scene = Assert.IsType<Canvas2DScene>(beforeState.CurrentScene);
        var sourcePoint = Center(AnchorHandle(
            scene,
            BpmnDemoPipeline.RejectedTaskVisualId,
            BpmnDemoPipeline.RejectedTaskReconnectSourceAnchorId).Bounds);

        var pressed = await interaction.PointerPressedAsync(
            Pointer(7153, scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var approvedBody = NodeBody(scene, BpmnDemoPipeline.ApprovedTaskVisualId);
        var bodyPoint = FindBodyPointNearSide(
            scene,
            approvedBody,
            ConnectorAnchorSide.Left,
            0.25d);

        var moved = await interaction.PointerMovedAsync(
            Pointer(7153, scene, bodyPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var proposedPoint = Preview(scene).Geometry.Points[^1];
        var released = await interaction.PointerReleasedAsync(
            Pointer(7153, scene, bodyPoint));
        await placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation?.IsCommitted);
        var afterDocument = placement.Composition.Document.CaptureSnapshot();
        var afterState = placement.Session.CaptureState();
        Assert.Equal(beforeFullRuns, placement.Pipeline.FullRunCount);
        Assert.Equal(
            beforePreservingRuns + 1,
            placement.Pipeline.NodeLayoutPreservingRunCount);
        AssertNodeGeometryUnchanged(
            beforeDocument,
            beforeState,
            afterDocument,
            afterState);

        var proposedAnchorId = new ConnectorAnchorId(
            "test:n2:n311-stability:proposed-target-anchor");
        var approvedVisual = Assert.Single(
            afterDocument.VisualModel.VisualStates,
            visual => visual.Id == BpmnDemoPipeline.ApprovedTaskVisualId);
        var createdAnchor = Assert.Single(
            approvedVisual.ConnectorAnchors,
            anchor => anchor.Id == proposedAnchorId);
        Assert.Equal(ConnectorAnchorSide.Left, createdAnchor.Side);
        Assert.Equal(ConnectorAnchorRole.Target, createdAnchor.Role);
        var existingRoute = Route(afterState, BpmnDemoPipeline.ThirdSequenceFlowId);
        Assert.NotEqual(beforeExistingRoute.DestinationAnchor, existingRoute.DestinationAnchor);
        var createdRoute = Route(
            afterState,
            new SemanticElementId("test:n2:n311-stability:relationship"));
        Assert.Equal(afterState.DocumentRevision, afterState.RoutingResult!.SourceRevision);
        Assert.Equal(sourcePoint, createdRoute.SourceAnchor);
        Assert.Equal(proposedPoint, createdRoute.DestinationAnchor);
    }

    [Fact]
    public async Task ExclusiveParallelAndInclusiveGatewaysCreateIndependentBranches()
    {
        await using var placement = await PlacementHarness.CreateAsync();
        var cases = new[]
        {
            new GatewayBranchCase(
                BpmnDemoPipeline.ExclusiveGatewayId,
                BpmnDemoPipeline.ExclusiveGatewayVisualId,
                new ConnectorAnchorId("test:n2:exclusive:source:approved"),
                BpmnDemoPipeline.ApprovedTaskId,
                BpmnDemoPipeline.ApprovedTaskVisualId,
                new ConnectorAnchorId("test:n2:exclusive:target:approved"),
                new ConnectorAnchorId("test:n2:exclusive:source:rejected"),
                BpmnDemoPipeline.RejectedTaskId,
                BpmnDemoPipeline.RejectedTaskVisualId,
                new ConnectorAnchorId("test:n2:exclusive:target:rejected")),
            new GatewayBranchCase(
                BpmnDemoPipeline.ParallelSplitGatewayId,
                BpmnDemoPipeline.ParallelSplitGatewayVisualId,
                new ConnectorAnchorId("test:n2:parallel:source:prepare"),
                BpmnDemoPipeline.PrepareShipmentTaskId,
                BpmnDemoPipeline.PrepareShipmentTaskVisualId,
                new ConnectorAnchorId("test:n2:parallel:target:prepare"),
                new ConnectorAnchorId("test:n2:parallel:source:notify"),
                BpmnDemoPipeline.NotifyCustomerTaskId,
                BpmnDemoPipeline.NotifyCustomerTaskVisualId,
                new ConnectorAnchorId("test:n2:parallel:target:notify")),
            new GatewayBranchCase(
                BpmnDemoPipeline.InclusiveSplitGatewayId,
                BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
                new ConnectorAnchorId("test:n2:inclusive:source:insurance"),
                BpmnDemoPipeline.AddInsuranceTaskId,
                BpmnDemoPipeline.AddInsuranceTaskVisualId,
                new ConnectorAnchorId("test:n2:inclusive:target:insurance"),
                new ConnectorAnchorId("test:n2:inclusive:source:gift-wrap"),
                BpmnDemoPipeline.AddGiftWrapTaskId,
                BpmnDemoPipeline.AddGiftWrapTaskVisualId,
                new ConnectorAnchorId("test:n2:inclusive:target:gift-wrap")),
        };
        var identities = Enumerable.Range(0, cases.Length * 2)
            .Select(index => new DocumentCreationIdentity(
                new SemanticElementId($"test:n2:gateway-branch:{index}:flow"),
                new VisualStateId($"test:n2:gateway-branch:{index}:visual")))
            .ToArray();
        await using var interaction = new Canvas2DInteractionController(
            placement.Session,
            connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations),
            creationIdentityProvider: new SequenceIdentityProvider(identities));
        var initialRelationshipCount = placement.Composition.Document
            .CaptureSnapshot().SemanticModel.RelationshipCount;
        var initialEndpointBindings = placement.Composition.Document
            .CaptureSnapshot().VisualModel.VisualStates
            .Where(static visual =>
                visual.SourceAnchorId is not null || visual.TargetAnchorId is not null)
            .ToDictionary(
                static visual => visual.Id,
                static visual => (visual.SourceAnchorId, visual.TargetAnchorId));

        foreach (var gateway in cases)
        {
            foreach (var branch in gateway.Branches)
            {
                await AddAnchorAsync(
                    placement,
                    gateway.SourceVisualId,
                    branch.SourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source);
                await AddAnchorAsync(
                    placement,
                    branch.TargetVisualId,
                    branch.TargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target);
            }
        }

        var identityIndex = 0;
        var pointerId = 7160L;
        foreach (var gateway in cases)
        {
            foreach (var branch in gateway.Branches)
            {
                var state = placement.Session.CaptureState();
                var selected = await placement.Session.UpdateEditorStateAsync(
                    new EditorStateSnapshot(
                        [gateway.SourceVisualId],
                        viewport: state.EditorState.Viewport));
                Assert.True(selected.Succeeded);
                var scene = Assert.IsType<Canvas2DScene>(
                    placement.Session.CaptureState().CurrentScene);
                var sourcePoint = Center(AnchorHandle(
                    scene,
                    gateway.SourceVisualId,
                    branch.SourceAnchorId).Bounds);
                var pressed = await interaction.PointerPressedAsync(
                    Pointer(pointerId, scene, sourcePoint, buttons: 1));
                Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
                scene = Assert.IsType<Canvas2DScene>(
                    placement.Session.CaptureState().CurrentScene);
                var targetPoint = Center(AnchorHandle(
                    scene,
                    branch.TargetVisualId,
                    branch.TargetAnchorId).Bounds);
                Assert.DoesNotContain(
                    placement.Session.CaptureState().EditorState.Selection,
                    item => item == branch.TargetVisualId);
                _ = await interaction.PointerMovedAsync(
                    Pointer(pointerId, scene, targetPoint, buttons: 1));
                var released = await interaction.PointerReleasedAsync(
                    Pointer(pointerId, scene, targetPoint));
                Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
                await placement.WaitForIdleAsync();

                var identity = identities[identityIndex++];
                var document = placement.Composition.Document.CaptureSnapshot();
                var relationship = Assert.Single(document.SemanticModel.Relationships,
                    item => item.Id == identity.SemanticElementId);
                var visual = Assert.Single(document.VisualModel.VisualStates,
                    item => item.Id == identity.VisualStateId);
                Assert.Equal(gateway.SourceSemanticId, relationship.SourceId);
                Assert.Equal(branch.TargetSemanticId, relationship.TargetId);
                Assert.Equal(branch.SourceAnchorId, visual.SourceAnchorId);
                Assert.Equal(branch.TargetAnchorId, visual.TargetAnchorId);
                var after = placement.Session.CaptureState();
                var edge = Assert.Single(after.ProjectedGraph!.Edges, item =>
                    item.Source.SemanticElementId == identity.SemanticElementId);
                var route = Assert.Single(after.RoutingResult!.Routes, item =>
                    item.ProjectedEdgeId == edge.Id);
                Assert.Equal(sourcePoint, route.SourceAnchor);
                Assert.Equal(targetPoint, route.DestinationAnchor);
                Assert.Contains(after.CurrentScene!.Items, item =>
                    item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                        edge.Id,
                        "connector-target-arrow"));
                pointerId++;
            }
        }

        Assert.Equal(
            initialRelationshipCount + identities.Length,
            placement.Composition.Document.CaptureSnapshot()
                .SemanticModel.RelationshipCount);
        Assert.Equal(
            identities.Length,
            identities.Select(identity => identity.SemanticElementId).Distinct().Count());
        Assert.Equal(
            identities.Length,
            identities.Select(identity => identity.VisualStateId).Distinct().Count());
        var finalDocument = placement.Composition.Document.CaptureSnapshot();
        foreach (var binding in initialEndpointBindings)
        {
            var visual = Assert.Single(finalDocument.VisualModel.VisualStates, item =>
                item.Id == binding.Key);
            Assert.Equal(binding.Value.SourceAnchorId, visual.SourceAnchorId);
            Assert.Equal(binding.Value.TargetAnchorId, visual.TargetAnchorId);
        }
    }

    [Fact]
    public async Task ExclusiveAnchorOccupancyFiltersGesturesAndSupportsUndoRedoAndRedistribution()
    {
        var taskA = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:task-a"),
            new VisualStateId("test:n2:occupancy:task-a:visual"));
        var taskB = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:task-b"),
            new VisualStateId("test:n2:occupancy:task-b:visual"));
        var taskC = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:task-c"),
            new VisualStateId("test:n2:occupancy:task-c:visual"));
        var firstFlow = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:flow-1"),
            new VisualStateId("test:n2:occupancy:flow-1:visual"));
        var secondFlow = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:flow-2"),
            new VisualStateId("test:n2:occupancy:flow-2:visual"));
        var replacementFlow = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:flow-3"),
            new VisualStateId("test:n2:occupancy:flow-3:visual"));
        var occupiedSourceAttempt = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:occupied-source-attempt"),
            new VisualStateId("test:n2:occupancy:occupied-source-attempt:visual"));
        var occupiedTargetAttempt = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:occupancy:occupied-target-attempt"),
            new VisualStateId("test:n2:occupancy:occupied-target-attempt:visual"));
        var sourceAnchor1 = new ConnectorAnchorId("test:n2:occupancy:s1");
        var sourceAnchor2 = new ConnectorAnchorId("test:n2:occupancy:s2");
        var sourceAnchor3 = new ConnectorAnchorId("test:n2:occupancy:s3");
        var targetAnchor1 = new ConnectorAnchorId("test:n2:occupancy:t1");
        var targetAnchor2 = new ConnectorAnchorId("test:n2:occupancy:t2");
        await using var placement = await PlacementHarness.CreateAsync(
            identities: new SequenceIdentityProvider(taskA, taskB, taskC));
        await PlaceNodeThroughToolboxAsync(
            placement,
            BpmnSemanticTypes.Task,
            new PointD(1080d, 1220d));
        await PlaceNodeThroughToolboxAsync(
            placement,
            BpmnSemanticTypes.Task,
            new PointD(1420d, 1220d));
        await PlaceNodeThroughToolboxAsync(
            placement,
            BpmnSemanticTypes.Task,
            new PointD(1080d, 1500d));
        await AddAnchorAsync(
            placement,
            taskA.VisualStateId,
            sourceAnchor1,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await AddAnchorAsync(
            placement,
            taskB.VisualStateId,
            targetAnchor1,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);
        await SelectVisualAsync(placement, taskB.VisualStateId);
        var scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var originalTargetPoint = Center(AnchorHandle(
            scene,
            taskB.VisualStateId,
            targetAnchor1).Bounds);
        await AddAnchorAsync(
            placement,
            taskB.VisualStateId,
            targetAnchor2,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target);
        await SelectVisualAsync(placement, taskB.VisualStateId);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        var redistributedTargetPoint = Center(AnchorHandle(
            scene,
            taskB.VisualStateId,
            targetAnchor1).Bounds);
        Assert.NotEqual(originalTargetPoint, redistributedTargetPoint);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            placement.Composition.Document.CaptureSnapshot().VisualModel,
            targetAnchor2));
        await AddAnchorAsync(
            placement,
            taskC.VisualStateId,
            sourceAnchor3,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);
        await using var interaction = new Canvas2DInteractionController(
            placement.Session,
            connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations),
            creationIdentityProvider: new SequenceIdentityProvider(
                firstFlow,
                secondFlow,
                replacementFlow));

        await CommitFlowAsync(
            placement,
            interaction,
            taskA.VisualStateId,
            sourceAnchor1,
            taskB.VisualStateId,
            targetAnchor1,
            7170);
        var afterFirst = placement.Composition.Document.CaptureSnapshot();
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            afterFirst.VisualModel,
            sourceAnchor1));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            afterFirst.VisualModel,
            targetAnchor1));

        var beforeAuthoritativeRejections = afterFirst;
        var beforeAuthoritativeHistory = placement.Session.CaptureState().HistoryStatus;
        var occupiedSourceResult = await placement.Session.ExecuteAsync(
            new CreateBpmnSequenceFlowCommand(
                beforeAuthoritativeRejections.DocumentId,
                beforeAuthoritativeRejections.Revision,
                occupiedSourceAttempt.SemanticElementId,
                occupiedSourceAttempt.VisualStateId,
                taskA.SemanticElementId,
                taskB.SemanticElementId,
                sourceAnchor1,
                targetAnchor2));
        Assert.False(occupiedSourceResult.IsCommitted);
        Assert.Contains(occupiedSourceResult.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
        var occupiedTargetResult = await placement.Session.ExecuteAsync(
            new CreateBpmnSequenceFlowCommand(
                beforeAuthoritativeRejections.DocumentId,
                beforeAuthoritativeRejections.Revision,
                occupiedTargetAttempt.SemanticElementId,
                occupiedTargetAttempt.VisualStateId,
                taskC.SemanticElementId,
                taskB.SemanticElementId,
                sourceAnchor3,
                targetAnchor1));
        Assert.False(occupiedTargetResult.IsCommitted);
        Assert.Contains(occupiedTargetResult.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
        Assert.Same(
            beforeAuthoritativeRejections,
            placement.Composition.Document.CaptureSnapshot());
        Assert.Equal(
            beforeAuthoritativeHistory,
            placement.Session.CaptureState().HistoryStatus);

        await SelectVisualAsync(placement, taskA.VisualStateId);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var occupiedSourcePoint = Center(AnchorHandle(
            scene,
            taskA.VisualStateId,
            sourceAnchor1).Bounds);
        var occupiedSourceRevision = afterFirst.Revision;
        var occupiedSourceHistory = placement.Session.CaptureState().HistoryStatus;
        _ = await interaction.PointerPressedAsync(
            Pointer(7171, scene, occupiedSourcePoint, buttons: 1));
        Assert.Null(placement.Session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(
            occupiedSourceRevision,
            placement.Composition.Document.CaptureSnapshot().Revision);
        Assert.Equal(
            occupiedSourceHistory,
            placement.Session.CaptureState().HistoryStatus);

        await SelectVisualAsync(placement, taskA.VisualStateId);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        var originalSourcePoint = Center(AnchorHandle(
            scene,
            taskA.VisualStateId,
            sourceAnchor1).Bounds);
        await AddAnchorAsync(
            placement,
            taskA.VisualStateId,
            sourceAnchor2,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source);

        await SelectVisualAsync(placement, taskA.VisualStateId);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        var redistributedSourcePoint = Center(AnchorHandle(
            scene,
            taskA.VisualStateId,
            sourceAnchor1).Bounds);
        Assert.NotEqual(originalSourcePoint, redistributedSourcePoint);
        var firstConnector = Assert.Single(
            placement.Composition.Document.CaptureSnapshot().VisualModel.VisualStates,
            visual => visual.Id == firstFlow.VisualStateId);
        Assert.Equal(sourceAnchor1, firstConnector.SourceAnchorId);
        Assert.Equal(targetAnchor1, firstConnector.TargetAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            placement.Composition.Document.CaptureSnapshot().VisualModel,
            sourceAnchor2));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            placement.Composition.Document.CaptureSnapshot().VisualModel,
            targetAnchor2));

        await SelectVisualAsync(placement, taskA.VisualStateId);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        var freeSourcePoint = Center(AnchorHandle(
            scene,
            taskA.VisualStateId,
            sourceAnchor2).Bounds);
        var pressed = await interaction.PointerPressedAsync(
            Pointer(7172, scene, freeSourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        var candidateAnchorIds = scene.Items
            .Where(IsCandidateHandle)
            .Select(AnchorId)
            .ToArray();
        Assert.Contains(targetAnchor2, candidateAnchorIds);
        Assert.DoesNotContain(targetAnchor1, candidateAnchorIds);
        var beforeOccupiedTargetDrop = placement.Composition.Document.CaptureSnapshot();
        var beforeOccupiedTargetHistory = placement.Session.CaptureState().HistoryStatus;
        _ = await interaction.PointerMovedAsync(
            Pointer(7172, scene, redistributedTargetPoint, buttons: 1));
        Assert.True(Canvas2DAnchorConnectionGestureMetadata.TryRead(
            placement.Session.CaptureState().EditorState.ActiveGesture!.Properties,
            out var hoveringOccupiedTarget));
        Assert.NotNull(hoveringOccupiedTarget);
        Assert.False(hoveringOccupiedTarget.HasTarget);
        var rejectedOccupiedTarget = await interaction.PointerReleasedAsync(
            Pointer(
                7172,
                Assert.IsType<Canvas2DScene>(
                    placement.Session.CaptureState().CurrentScene),
                redistributedTargetPoint));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, rejectedOccupiedTarget.Status);
        Assert.Equal(
            beforeOccupiedTargetDrop,
            placement.Composition.Document.CaptureSnapshot());
        Assert.Equal(
            beforeOccupiedTargetHistory,
            placement.Session.CaptureState().HistoryStatus);
        Assert.Null(placement.Session.CaptureState().EditorState.ActiveGesture);

        await CommitFlowAsync(
            placement,
            interaction,
            taskA.VisualStateId,
            sourceAnchor2,
            taskB.VisualStateId,
            targetAnchor2,
            7173);
        var afterSecond = placement.Composition.Document.CaptureSnapshot();
        Assert.Equal(
            new[] { firstFlow.SemanticElementId, secondFlow.SemanticElementId },
            afterSecond.SemanticModel.Relationships
                .Where(relationship =>
                    relationship.Id == firstFlow.SemanticElementId ||
                    relationship.Id == secondFlow.SemanticElementId)
                .Select(static relationship => relationship.Id));
        var incoming = afterSecond.VisualModel.VisualStates
            .Where(visual =>
                visual.Id == firstFlow.VisualStateId ||
                visual.Id == secondFlow.VisualStateId)
            .Select(static visual => visual.TargetAnchorId)
            .ToArray();
        Assert.Equal(2, incoming.Distinct().Count());
        Assert.Contains(targetAnchor1, incoming);
        Assert.Contains(targetAnchor2, incoming);

        await SelectVisualAsync(placement, taskC.VisualStateId);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        var thirdSourcePoint = Center(AnchorHandle(
            scene,
            taskC.VisualStateId,
            sourceAnchor3).Bounds);
        pressed = await interaction.PointerPressedAsync(
            Pointer(7174, scene, thirdSourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        scene = Assert.IsType<Canvas2DScene>(placement.Session.CaptureState().CurrentScene);
        Assert.DoesNotContain(scene.Items.Where(IsCandidateHandle), item =>
            AnchorId(item) == targetAnchor1);
        var beforeSecondOccupiedDrop = placement.Composition.Document.CaptureSnapshot();
        var beforeSecondOccupiedHistory = placement.Session.CaptureState().HistoryStatus;
        _ = await interaction.PointerMovedAsync(
            Pointer(7174, scene, redistributedTargetPoint, buttons: 1));
        _ = await interaction.PointerReleasedAsync(
            Pointer(
                7174,
                Assert.IsType<Canvas2DScene>(
                    placement.Session.CaptureState().CurrentScene),
                redistributedTargetPoint));
        Assert.Equal(
            beforeSecondOccupiedDrop,
            placement.Composition.Document.CaptureSnapshot());
        Assert.Equal(
            beforeSecondOccupiedHistory,
            placement.Session.CaptureState().HistoryStatus);

        var undo = await placement.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await placement.WaitForIdleAsync();
        var afterUndo = placement.Composition.Document.CaptureSnapshot();
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterUndo.VisualModel,
            sourceAnchor2));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterUndo.VisualModel,
            targetAnchor2));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            afterUndo.VisualModel,
            sourceAnchor1));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            afterUndo.VisualModel,
            targetAnchor1));

        var redo = await placement.Session.RedoAsync();
        Assert.True(redo.IsCommitted);
        await placement.WaitForIdleAsync();
        var restoredSecond = Assert.Single(
            placement.Composition.Document.CaptureSnapshot().VisualModel.VisualStates,
            visual => visual.Id == secondFlow.VisualStateId);
        Assert.Equal(sourceAnchor2, restoredSecond.SourceAnchorId);
        Assert.Equal(targetAnchor2, restoredSecond.TargetAnchorId);

        undo = await placement.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await placement.WaitForIdleAsync();
        await CommitFlowAsync(
            placement,
            interaction,
            taskA.VisualStateId,
            sourceAnchor2,
            taskB.VisualStateId,
            targetAnchor2,
            7175);
        var replacement = Assert.Single(
            placement.Composition.Document.CaptureSnapshot().VisualModel.VisualStates,
            visual => visual.Id == replacementFlow.VisualStateId);
        Assert.Equal(sourceAnchor2, replacement.SourceAnchorId);
        Assert.Equal(targetAnchor2, replacement.TargetAnchorId);
    }

    [Fact]
    public async Task ExistingSourceAndTargetAnchorsOnOneTaskCreateASelfLoop()
    {
        var nodeIdentity = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:self-loop:task"),
            new VisualStateId("test:n2:self-loop:task:visual"));
        var flowIdentity = new DocumentCreationIdentity(
            new SemanticElementId("test:n2:self-loop:flow"),
            new VisualStateId("test:n2:self-loop:flow:visual"));
        await using var placement = await PlacementHarness.CreateAsync(
            identities: new SequenceIdentityProvider(nodeIdentity));
        var taskItem = placement.Item(BpmnSemanticTypes.Task);
        Assert.True(placement.Selection.Select(taskItem.ItemId));
        var scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var placed = await placement.Controller.TryPlaceAtCssPointAsync(
            placement.Session,
            scene.ViewportTransform.TransformPoint(new PointD(1120d, 260d)));
        Assert.True(placed.IsCommitted);
        await placement.WaitForIdleAsync();

        var sourceAnchorId = new ConnectorAnchorId("test:n2:self-loop:source");
        var targetAnchorId = new ConnectorAnchorId("test:n2:self-loop:target");
        foreach (var command in new ICommand[]
                 {
                     new AddConnectorAnchorCommand(
                         placement.Composition.Document.DocumentId,
                         placement.Composition.Document.Revision,
                         nodeIdentity.VisualStateId,
                         sourceAnchorId,
                         ConnectorAnchorSide.Right,
                         ConnectorAnchorRole.Source,
                         insertionIndex: 0),
                     new AddConnectorAnchorCommand(
                         placement.Composition.Document.DocumentId,
                         placement.Composition.Document.Revision.Increment(),
                         nodeIdentity.VisualStateId,
                         targetAnchorId,
                         ConnectorAnchorSide.Left,
                         ConnectorAnchorRole.Target,
                         insertionIndex: 0),
                 })
        {
            var result = await placement.Session.ExecuteAsync(command);
            Assert.True(result.IsCommitted);
            await placement.WaitForIdleAsync();
        }

        var selected = await placement.Session.UpdateEditorStateAsync(
            new EditorStateSnapshot(
                [nodeIdentity.VisualStateId],
                viewport: placement.Session.CaptureState().EditorState.Viewport));
        Assert.True(selected.Succeeded);
        await using var interaction = new Canvas2DInteractionController(
            placement.Session,
            connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations),
            creationIdentityProvider: new SequenceIdentityProvider(flowIdentity));
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var sourcePoint = Center(AnchorHandle(
            scene,
            nodeIdentity.VisualStateId,
            sourceAnchorId).Bounds);
        var pressed = await interaction.PointerPressedAsync(
            Pointer(7190, scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var targetPoint = Center(AnchorHandle(
            scene,
            nodeIdentity.VisualStateId,
            targetAnchorId).Bounds);
        _ = await interaction.PointerMovedAsync(
            Pointer(7190, scene, targetPoint, buttons: 1));
        var released = await interaction.PointerReleasedAsync(
            Pointer(7190, scene, targetPoint));
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        await placement.WaitForIdleAsync();

        var document = placement.Composition.Document.CaptureSnapshot();
        var relationship = Assert.Single(document.SemanticModel.Relationships,
            item => item.Id == flowIdentity.SemanticElementId);
        var visual = Assert.Single(document.VisualModel.VisualStates,
            item => item.Id == flowIdentity.VisualStateId);
        Assert.Equal(nodeIdentity.SemanticElementId, relationship.SourceId);
        Assert.Equal(nodeIdentity.SemanticElementId, relationship.TargetId);
        Assert.Equal(sourceAnchorId, visual.SourceAnchorId);
        Assert.Equal(targetAnchorId, visual.TargetAnchorId);
        var after = placement.Session.CaptureState();
        var edge = Assert.Single(after.ProjectedGraph!.Edges,
            item => item.Source.SemanticElementId == flowIdentity.SemanticElementId);
        Assert.Contains(after.CurrentScene!.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
    }

    [Fact]
    public async Task ScenePipelineFaultDuringPreviewRestoresCleanFallbackAndCreatesNothing()
    {
        await using var harness = await ConnectionHarness.CreateAsync("scene-fault");
        var beforeDocument = harness.Document;
        var beforeHistory = harness.State.HistoryStatus;
        var sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        var pressed = await harness.Interaction.PointerPressedAsync(
            Pointer(7195, harness.Scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Contains(harness.Scene.Items, IsCandidateHandle);
        Assert.NotNull(Preview(harness.Scene));
        var renderedTransientCount = harness.Placement.Execution.RenderCount;

        harness.Placement.Pipeline.FailNextSceneOnlyRun();
        var targetPoint = Center(harness.AnchorHandle(
            harness.TargetVisualId,
            harness.TargetAnchorId).Bounds);
        var failed = await harness.Interaction.PointerMovedAsync(
            Pointer(7195, harness.Scene, targetPoint, buttons: 1));
        await harness.Placement.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Failed, failed.Status);
        var after = harness.State;
        Assert.Equal(
            Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionStatus
                .RuntimeFaulted,
            after.Status);
        Assert.Null(after.CurrentScene);
        var fallback = Assert.IsType<Canvas2DScene>(after.LastKnownGoodScene);
        Assert.Null(after.EditorState.ActiveGesture);
        Assert.DoesNotContain(fallback.Items, IsCandidateHandle);
        Assert.DoesNotContain(fallback.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "anchor-connection-preview:",
                StringComparison.Ordinal) == true);
        Assert.Equal(beforeDocument, harness.Document);
        Assert.Equal(beforeHistory, after.HistoryStatus);
        Assert.Equal(
            renderedTransientCount + 1,
            harness.Placement.Execution.RenderCount);
        Assert.Contains(after.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == "TEST_N2_SCENE_PIPELINE_FAILURE");
        var rejected = await harness.Interaction.PointerPressedAsync(
            Pointer(7196, fallback, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Unavailable, rejected.Status);
        Assert.Equal(beforeDocument, harness.Document);
    }

    [Fact]
    public async Task InvalidDropEscapeEmptyAmbiguousFactoryFailureAndStaleRevisionCreateNothing()
    {
        await using var harness = await ConnectionHarness.CreateAsync("cancellation");
        var baselineRelationshipCount = harness.Document.SemanticModel.RelationshipCount;
        var baselineHistory = harness.State.HistoryStatus;
        var sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);

        _ = await harness.Interaction.PointerPressedAsync(
            Pointer(7200, harness.Scene, sourcePoint, buttons: 1));
        var targetBodyPoint = Center(NodeBody(harness.Scene, harness.TargetVisualId).Bounds);
        var invalid = await harness.Interaction.PointerReleasedAsync(
            Pointer(7200, harness.Scene, targetBodyPoint));
        Assert.Equal(Canvas2DInteractionStatus.Updated, invalid.Status);
        AssertTransientCleanup(harness, baselineRelationshipCount, baselineHistory);

        sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        _ = await harness.Interaction.PointerPressedAsync(
            Pointer(7201, harness.Scene, sourcePoint, buttons: 1));
        var escaped = await harness.Interaction.CancelActiveGestureAsync();
        Assert.Equal(Canvas2DInteractionStatus.Updated, escaped.Status);
        AssertTransientCleanup(harness, baselineRelationshipCount, baselineHistory);

        sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        _ = await harness.Interaction.PointerPressedAsync(
            Pointer(72015, harness.Scene, sourcePoint, buttons: 1));
        var lostPointer = await harness.Interaction.PointerCancelledAsync(72015);
        Assert.Equal(Canvas2DInteractionStatus.Updated, lostPointer.Status);
        AssertTransientCleanup(harness, baselineRelationshipCount, baselineHistory);

        var disposable = new Canvas2DInteractionController(
            harness.Placement.Session,
            connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations),
            creationIdentityProvider: new FixedIdentityProvider("dispose"));
        sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        var disposableDown = await disposable.PointerPressedAsync(
            Pointer(72016, harness.Scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, disposableDown.Status);
        await disposable.DisposeAsync();
        AssertTransientCleanup(harness, baselineRelationshipCount, baselineHistory);

        await using (var empty = new Canvas2DInteractionController(
                         harness.Placement.Session))
        {
            sourcePoint = Center(harness.AnchorHandle(
                harness.SourceVisualId,
                harness.SourceAnchorId).Bounds);
            var noCapability = await empty.PointerPressedAsync(
                Pointer(7202, harness.Scene, sourcePoint, buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, noCapability.Status);
            Assert.Null(harness.State.EditorState.ActiveGesture);
            _ = await empty.PointerCancelledAsync(7202);
        }

        var ambiguousCatalog = new AnchorConnectionCreationCatalog(
        [
            new(new AnchorConnectionCreationId("test:n2:a"), new MatchingFactory()),
            new(new AnchorConnectionCreationId("test:n2:b"), new MatchingFactory()),
        ]);
        await using (var ambiguous = new Canvas2DInteractionController(
                         harness.Placement.Session,
                         connectionCreationCatalog: ambiguousCatalog,
                         creationIdentityProvider: new FixedIdentityProvider(
                             "ambiguous")))
        {
            sourcePoint = Center(harness.AnchorHandle(
                harness.SourceVisualId,
                harness.SourceAnchorId).Bounds);
            var rejected = await ambiguous.PointerPressedAsync(
                Pointer(7203, harness.Scene, sourcePoint, buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Unavailable, rejected.Status);
            Assert.Contains(rejected.Diagnostics, diagnostic =>
                diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.AmbiguousConnectionCreation);
            Assert.Null(harness.State.EditorState.ActiveGesture);
        }

        var failingCatalog = new AnchorConnectionCreationCatalog(
        [
            new(
                new AnchorConnectionCreationId("test:n2:failure"),
                new RejectingFactory()),
        ]);
        await using (var failing = new Canvas2DInteractionController(
                         harness.Placement.Session,
                         connectionCreationCatalog: failingCatalog,
                         creationIdentityProvider: new FixedIdentityProvider("failure")))
        {
            sourcePoint = Center(harness.AnchorHandle(
                harness.SourceVisualId,
                harness.SourceAnchorId).Bounds);
            _ = await failing.PointerPressedAsync(
                Pointer(7204, harness.Scene, sourcePoint, buttons: 1));
            var candidate = harness.AnchorHandle(
                harness.TargetVisualId,
                harness.TargetAnchorId);
            var failed = await failing.PointerReleasedAsync(
                Pointer(7204, harness.Scene, Center(candidate.Bounds)));
            Assert.Equal(Canvas2DInteractionStatus.Failed, failed.Status);
            Assert.Contains(failed.Diagnostics, diagnostic =>
                diagnostic.Code == "TEST_N2_FACTORY_REJECTED");
            AssertTransientCleanup(harness, baselineRelationshipCount, baselineHistory);
        }

        var competingSourceAnchorId = new ConnectorAnchorId(
            "test:n2:race:competing-source-anchor");
        var addedCompetingSource = await harness.Placement.Session.ExecuteAsync(
            new AddConnectorAnchorCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                harness.SourceVisualId,
                competingSourceAnchorId,
                ConnectorAnchorSide.Bottom,
                ConnectorAnchorRole.Source,
                insertionIndex: 0));
        Assert.True(addedCompetingSource.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        var raceHistoryBeforeGesture = harness.State.HistoryStatus;
        sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        _ = await harness.Interaction.PointerPressedAsync(
            Pointer(7205, harness.Scene, sourcePoint, buttons: 1));
        var staleTargetPoint = Center(harness.AnchorHandle(
            harness.TargetVisualId,
            harness.TargetAnchorId).Bounds);
        var external = await harness.Placement.Session.ExecuteAsync(
            new CreateBpmnSequenceFlowCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                new SemanticElementId("test:n2:race:competing-flow"),
                new VisualStateId("test:n2:race:competing-flow:visual"),
                harness.SourceSemanticId,
                harness.TargetSemanticId,
                competingSourceAnchorId,
                harness.TargetAnchorId));
        Assert.True(external.IsCommitted);
        await harness.Placement.WaitForIdleAsync();
        var stale = await harness.Interaction.PointerReleasedAsync(
            Pointer(7205, harness.Scene, staleTargetPoint));
        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Equal(
            baselineRelationshipCount + 1,
            harness.Document.SemanticModel.RelationshipCount);
        Assert.Equal(
            raceHistoryBeforeGesture.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        var competingFlow = Assert.Single(
            harness.Document.VisualModel.VisualStates,
            visual => visual.Id ==
                new VisualStateId("test:n2:race:competing-flow:visual"));
        Assert.Equal(competingSourceAnchorId, competingFlow.SourceAnchorId);
        Assert.Equal(harness.TargetAnchorId, competingFlow.TargetAnchorId);
        Assert.Null(harness.State.EditorState.ActiveGesture);
        Assert.DoesNotContain(harness.Scene.Items, IsCandidateHandle);
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

    private static async ValueTask CommitAndAssertValidFlowAsync(
        ConnectionHarness harness,
        long pointerId)
    {
        var before = harness.Document;
        var sourcePoint = Center(harness.AnchorHandle(
            harness.SourceVisualId,
            harness.SourceAnchorId).Bounds);
        var down = await harness.Interaction.PointerPressedAsync(
            Pointer(pointerId, harness.Scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, down.Status);

        var targetPoint = Center(harness.AnchorHandle(
            harness.TargetVisualId,
            harness.TargetAnchorId).Bounds);
        var move = await harness.Interaction.PointerMovedAsync(
            Pointer(pointerId, harness.Scene, targetPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
        var released = await harness.Interaction.PointerReleasedAsync(
            Pointer(pointerId, harness.Scene, targetPoint));
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        await harness.Placement.WaitForIdleAsync();

        var after = harness.Document;
        var relationship = Assert.Single(after.SemanticModel.Relationships, item =>
            item.Id == harness.FlowRelationshipId);
        var visual = Assert.Single(after.VisualModel.VisualStates, item =>
            item.Id == harness.FlowVisualId);
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, relationship.TypeId);
        Assert.Equal(harness.SourceSemanticId, relationship.SourceId);
        Assert.Equal(harness.TargetSemanticId, relationship.TargetId);
        Assert.Equal(harness.SourceAnchorId, visual.SourceAnchorId);
        Assert.Equal(harness.TargetAnchorId, visual.TargetAnchorId);
        Assert.Equal(harness.FlowVisualId,
            Assert.Single(harness.State.EditorState.Selection));
        var edge = Assert.Single(harness.State.ProjectedGraph!.Edges, item =>
            item.Source.SemanticElementId == harness.FlowRelationshipId);
        var route = Assert.Single(harness.State.RoutingResult!.Routes, item =>
            item.ProjectedEdgeId == edge.Id);
        Assert.Equal(sourcePoint, route.SourceAnchor);
        Assert.Equal(targetPoint, route.DestinationAnchor);
        Assert.Contains(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
    }

    private static Canvas2DSceneItem Preview(Canvas2DScene scene) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.StableSourceKey?.StartsWith(
                "anchor-connection-preview:",
                StringComparison.Ordinal) == true);

    private static async ValueTask PlaceNodeThroughToolboxAsync(
        PlacementHarness placement,
        SemanticTypeId semanticTypeId,
        PointD center)
    {
        var item = placement.Item(semanticTypeId);
        Assert.True(placement.Selection.Select(item.ItemId));
        var cssPoint = Assert.IsType<Canvas2DScene>(
                placement.Session.CaptureState().CurrentScene)
            .ViewportTransform.TransformPoint(center);
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
        var result = await placement.Session.ExecuteAsync(new AddConnectorAnchorCommand(
            document.DocumentId,
            document.Revision,
            visualStateId,
            anchorId,
            side,
            role,
            insertionIndex));
        Assert.True(result.IsCommitted);
        await placement.WaitForIdleAsync();
    }

    private static async ValueTask SelectVisualAsync(
        PlacementHarness placement,
        VisualStateId visualStateId)
    {
        var state = placement.Session.CaptureState();
        var result = await placement.Session.UpdateEditorStateAsync(
            new EditorStateSnapshot(
                [visualStateId],
                viewport: state.EditorState.Viewport));
        Assert.True(result.Succeeded);
    }

    private static async ValueTask<Canvas2DInteractionResult> CommitFlowAsync(
        PlacementHarness placement,
        Canvas2DInteractionController interaction,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId targetAnchorId,
        long pointerId)
    {
        await SelectVisualAsync(placement, sourceVisualStateId);
        var scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var sourcePoint = Center(AnchorHandle(
            scene,
            sourceVisualStateId,
            sourceAnchorId).Bounds);
        var pressed = await interaction.PointerPressedAsync(
            Pointer(pointerId, scene, sourcePoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        scene = Assert.IsType<Canvas2DScene>(
            placement.Session.CaptureState().CurrentScene);
        var targetHandle = AnchorHandle(scene, targetVisualStateId, targetAnchorId);
        Assert.True(IsCandidateHandle(targetHandle));
        var targetPoint = Center(targetHandle.Bounds);
        var moved = await interaction.PointerMovedAsync(
            Pointer(pointerId, scene, targetPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        var released = await interaction.PointerReleasedAsync(
            Pointer(pointerId, Assert.IsType<Canvas2DScene>(
                placement.Session.CaptureState().CurrentScene), targetPoint));
        await placement.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        return released;
    }

    private static Canvas2DSceneItem AnchorHandle(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId) =>
        Assert.Single(scene.Items, item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Metadata.TryGetValue(
                Canvas2DConnectorAnchorMetadata.AnchorId,
                out var value) &&
            value.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(value.TextValue, anchorId.Value));

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedObjectId, "node"));

    private static PointD FindBodyPointNearSide(
        Canvas2DScene scene,
        Canvas2DSceneItem body,
        ConnectorAnchorSide side,
        double edgeFraction)
    {
        var hitTest = new Inceptus.DocumentEngine.Canvas2D.HitTesting
            .Canvas2DSceneHitTestService();
        var expected = side switch
        {
            ConnectorAnchorSide.Top => new PointD(
                body.Bounds.Left + (body.Bounds.Width * edgeFraction),
                body.Bounds.Top),
            ConnectorAnchorSide.Right => new PointD(
                body.Bounds.Right,
                body.Bounds.Top + (body.Bounds.Height * edgeFraction)),
            ConnectorAnchorSide.Bottom => new PointD(
                body.Bounds.Left + (body.Bounds.Width * edgeFraction),
                body.Bounds.Bottom),
            ConnectorAnchorSide.Left => new PointD(
                body.Bounds.Left,
                body.Bounds.Top + (body.Bounds.Height * edgeFraction)),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };
        return (from x in Enumerable.Range(
                    (int)body.Bounds.Left + 2,
                    (int)body.Bounds.Width - 4)
                from y in Enumerable.Range(
                    (int)body.Bounds.Top + 2,
                    (int)body.Bounds.Height - 4)
                let point = new PointD(x, y)
                where hitTest.HitTest(scene, point)?.SceneObjectId == body.Id
                where ConnectorTargetEdgeResolver.Resolve(body.Bounds, point) == side
                orderby Math.Abs(point.X - expected.X) + Math.Abs(point.Y - expected.Y)
                select point).First();
    }

    private static Inceptus.DocumentEngine.Contracts.Routing.RoutedConnectorGeometry Route(
        Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState state,
        SemanticElementId relationshipId)
    {
        var edge = Assert.Single(
            state.ProjectedGraph!.Edges,
            item => item.Source.SemanticElementId == relationshipId);
        return Assert.Single(
            state.RoutingResult!.Routes,
            item => item.ProjectedEdgeId == edge.Id);
    }

    private static void AssertNodeGeometryUnchanged(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot beforeDocument,
        Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState beforeState,
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot afterDocument,
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

    private static bool BooleanMetadata(Canvas2DSceneItem item, string key) =>
        item.Metadata.TryGetValue(key, out var value) &&
        value.Kind == PropertyValueKind.Boolean &&
        value.BooleanValue;

    private static bool IsCandidateHandle(Canvas2DSceneItem item) =>
        BooleanMetadata(
            item,
            Canvas2DConnectorAnchorMetadata.ConnectionTargetCandidate);

    private static ConnectorAnchorId AnchorId(Canvas2DSceneItem item) =>
        new(item.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue);

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static void AssertAnchorStillExists(
        ConnectionHarness harness,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId) =>
        Assert.Contains(
            harness.Document.VisualModel.VisualStates.Single(item =>
                item.Id == visualStateId).ConnectorAnchors,
            anchor => anchor.Id == anchorId);

    private static VisualStateSnapshot FlowVisual(ConnectionHarness harness) =>
        Assert.Single(harness.Document.VisualModel.VisualStates,
            item => item.Id == harness.FlowVisualId);

    private static void AssertFlowStillBound(
        ConnectionHarness harness,
        Inceptus.DocumentEngine.Contracts.Semantics.SemanticRelationshipSnapshot
            expectedRelationship)
    {
        Assert.Equal(
            expectedRelationship,
            Assert.Single(harness.Document.SemanticModel.Relationships,
                item => item.Id == harness.FlowRelationshipId));
        var visual = FlowVisual(harness);
        Assert.Equal(harness.SourceAnchorId, visual.SourceAnchorId);
        Assert.Equal(harness.TargetAnchorId, visual.TargetAnchorId);
    }

    private static (PointD Source, PointD Target) RoutedEndpoints(
        ConnectionHarness harness)
    {
        var state = harness.State;
        var edge = Assert.Single(state.ProjectedGraph!.Edges,
            item => item.Source.SemanticElementId == harness.FlowRelationshipId);
        var route = Assert.Single(state.RoutingResult!.Routes,
            item => item.ProjectedEdgeId == edge.Id);
        return (route.SourceAnchor, route.DestinationAnchor);
    }

    private static void AssertTransientCleanup(
        ConnectionHarness harness,
        int expectedRelationshipCount,
        Inceptus.DocumentEngine.Contracts.History.HistoryStatus expectedHistory)
    {
        Assert.Equal(expectedRelationshipCount,
            harness.Document.SemanticModel.RelationshipCount);
        Assert.Equal(expectedHistory, harness.State.HistoryStatus);
        Assert.Null(harness.State.EditorState.ActiveGesture);
        Assert.Equal(harness.SourceVisualId,
            Assert.Single(harness.State.EditorState.Selection));
        Assert.DoesNotContain(harness.Scene.Items, IsCandidateHandle);
        Assert.DoesNotContain(harness.Scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "anchor-connection-preview:",
                StringComparison.Ordinal) == true);
    }

    private sealed class ConnectionHarness : IAsyncDisposable
    {
        private ConnectionHarness(
            PlacementHarness placement,
            Canvas2DInteractionController interaction,
            string suffix)
        {
            Placement = placement;
            Interaction = interaction;
            SourceSemanticId = new SemanticElementId($"test:n2:{suffix}:source");
            SourceVisualId = new VisualStateId($"test:n2:{suffix}:source:visual");
            TargetSemanticId = new SemanticElementId($"test:n2:{suffix}:target");
            TargetVisualId = new VisualStateId($"test:n2:{suffix}:target:visual");
            SourceAnchorId = new ConnectorAnchorId($"test:n2:{suffix}:source-anchor");
            TargetAnchorId = new ConnectorAnchorId($"test:n2:{suffix}:target-anchor");
            FlowRelationshipId = new SemanticElementId($"test:n2:{suffix}:flow");
            FlowVisualId = new VisualStateId($"test:n2:{suffix}:flow:visual");
        }

        internal PlacementHarness Placement { get; }

        internal Canvas2DInteractionController Interaction { get; }

        internal SemanticElementId SourceSemanticId { get; }

        internal VisualStateId SourceVisualId { get; }

        internal SemanticElementId TargetSemanticId { get; }

        internal VisualStateId TargetVisualId { get; }

        internal ConnectorAnchorId SourceAnchorId { get; }

        internal ConnectorAnchorId TargetAnchorId { get; }

        internal SemanticElementId FlowRelationshipId { get; }

        internal VisualStateId FlowVisualId { get; }

        internal Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot Document =>
            Placement.Composition.Document.CaptureSnapshot();

        internal Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState State =>
            Placement.Session.CaptureState();

        internal Canvas2DScene Scene => Assert.IsType<Canvas2DScene>(State.CurrentScene);

        internal Canvas2DSceneItem AnchorHandle(
            VisualStateId visualStateId,
            ConnectorAnchorId anchorId) =>
            Assert.Single(Scene.Items, item =>
                item.Origin.VisualStateId == visualStateId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorAnchorMetadata.AnchorId,
                    out var value) &&
                value.Kind == PropertyValueKind.Text &&
                StringComparer.Ordinal.Equals(value.TextValue, anchorId.Value));

        internal static async ValueTask<ConnectionHarness> CreateAsync(string suffix)
            => await CreateAsync(
                suffix,
                BpmnSemanticTypes.Task,
                BpmnSemanticTypes.Task);

        internal static async ValueTask<ConnectionHarness> CreateAsync(
            string suffix,
            SemanticTypeId sourceType,
            SemanticTypeId targetType)
        {
            var sourceIdentity = new DocumentCreationIdentity(
                new SemanticElementId($"test:n2:{suffix}:source"),
                new VisualStateId($"test:n2:{suffix}:source:visual"));
            var targetIdentity = new DocumentCreationIdentity(
                new SemanticElementId($"test:n2:{suffix}:target"),
                new VisualStateId($"test:n2:{suffix}:target:visual"));
            var placement = await PlacementHarness.CreateAsync(
                identities: new SequenceIdentityProvider(sourceIdentity, targetIdentity));
            try
            {
                await PlaceNodeAsync(
                    placement,
                    sourceType,
                    new PointD(1120d, 1260d));
                await PlaceNodeAsync(
                    placement,
                    targetType,
                    new PointD(1420d, 1260d));
                var sourceAnchorId = new ConnectorAnchorId(
                    $"test:n2:{suffix}:source-anchor");
                var targetAnchorId = new ConnectorAnchorId(
                    $"test:n2:{suffix}:target-anchor");
                await ExecuteAnchorAsync(
                    placement,
                    sourceIdentity.VisualStateId,
                    sourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source);
                await ExecuteAnchorAsync(
                    placement,
                    targetIdentity.VisualStateId,
                    targetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target);
                if (targetType != BpmnSemanticTypes.EndEvent)
                {
                    await ExecuteAnchorAsync(
                        placement,
                        targetIdentity.VisualStateId,
                        new ConnectorAnchorId($"test:n2:{suffix}:target-source-anchor"),
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source);
                }
                var selected = await placement.Session.UpdateEditorStateAsync(
                    new EditorStateSnapshot(
                        [sourceIdentity.VisualStateId],
                        viewport: placement.Session.CaptureState().EditorState.Viewport));
                Assert.True(selected.Succeeded);
                var interaction = new Canvas2DInteractionController(
                    placement.Session,
                    connectionCreationCatalog: new AnchorConnectionCreationCatalog(
                        BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations),
                    creationIdentityProvider: new SequenceIdentityProvider(
                        new DocumentCreationIdentity(
                            new SemanticElementId($"test:n2:{suffix}:flow"),
                            new VisualStateId($"test:n2:{suffix}:flow:visual"))));
                return new ConnectionHarness(placement, interaction, suffix);
            }
            catch
            {
                await placement.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Interaction.DisposeAsync();
            await Placement.DisposeAsync();
        }

        private static async ValueTask PlaceNodeAsync(
            PlacementHarness placement,
            SemanticTypeId semanticTypeId,
            PointD center)
        {
            var item = placement.Item(semanticTypeId);
            Assert.True(placement.Selection.Select(item.ItemId));
            var cssPoint = Assert.IsType<Canvas2DScene>(
                    placement.Session.CaptureState().CurrentScene)
                .ViewportTransform.TransformPoint(center);
            var result = await placement.Controller.TryPlaceAtCssPointAsync(
                placement.Session,
                cssPoint);
            Assert.True(result.IsCommitted);
            await placement.WaitForIdleAsync();
        }

        private static async ValueTask ExecuteAnchorAsync(
            PlacementHarness placement,
            VisualStateId visualStateId,
            ConnectorAnchorId anchorId,
            ConnectorAnchorSide side,
            ConnectorAnchorRole role)
        {
            var document = placement.Composition.Document.CaptureSnapshot();
            var result = await placement.Session.ExecuteAsync(new AddConnectorAnchorCommand(
                document.DocumentId,
                document.Revision,
                visualStateId,
                anchorId,
                side,
                role,
                insertionIndex: 0));
            Assert.True(result.IsCommitted);
            await placement.WaitForIdleAsync();
        }
    }

    private sealed class FixedIdentityProvider(string suffix) :
        IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => new(
            new SemanticElementId($"test:n2:{suffix}:relationship"),
            new VisualStateId($"test:n2:{suffix}:visual"));

        public ConnectorAnchorId CreateConnectorAnchorId() =>
            new($"test:n2:{suffix}:proposed-target-anchor");
    }

    private sealed record GatewayBranchCase(
        SemanticElementId SourceSemanticId,
        VisualStateId SourceVisualId,
        ConnectorAnchorId FirstSourceAnchorId,
        SemanticElementId FirstTargetSemanticId,
        VisualStateId FirstTargetVisualId,
        ConnectorAnchorId FirstTargetAnchorId,
        ConnectorAnchorId SecondSourceAnchorId,
        SemanticElementId SecondTargetSemanticId,
        VisualStateId SecondTargetVisualId,
        ConnectorAnchorId SecondTargetAnchorId)
    {
        internal IEnumerable<GatewayBranch> Branches =>
        [
            new(
                FirstSourceAnchorId,
                FirstTargetSemanticId,
                FirstTargetVisualId,
                FirstTargetAnchorId),
            new(
                SecondSourceAnchorId,
                SecondTargetSemanticId,
                SecondTargetVisualId,
                SecondTargetAnchorId),
        ];
    }

    private sealed record GatewayBranch(
        ConnectorAnchorId SourceAnchorId,
        SemanticElementId TargetSemanticId,
        VisualStateId TargetVisualId,
        ConnectorAnchorId TargetAnchorId);

    private class MatchingFactory : IAnchorConnectionCreationCommandFactory
    {
        public bool CanStart(AnchorConnectionCreationSourceRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return true;
        }

        public virtual AnchorConnectionCreationPlanResult CreatePlan(
            AnchorConnectionCreationRequest request) =>
            throw new InvalidOperationException("Ambiguous test factories must not plan.");
    }

    private sealed class RejectingFactory : MatchingFactory
    {
        public override AnchorConnectionCreationPlanResult CreatePlan(
            AnchorConnectionCreationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return AnchorConnectionCreationPlanResult.Failure(
            [
                new Diagnostic(
                    "TEST_N2_FACTORY_REJECTED",
                    DiagnosticSeverity.Error,
                    "The test connection factory rejected the target.",
                    request.TargetAnchorId.Value),
            ]);
        }
    }
}
