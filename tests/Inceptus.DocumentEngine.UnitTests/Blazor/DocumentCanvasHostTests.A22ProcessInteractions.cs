using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly VisualStateId A22ProcessFlowVisual = new("a22:process-flow:visual");

    [Fact]
    public async Task A22SavedProcessRegionOffsetIsIncludedInInverseNodeEditingCoordinates()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-process-region", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var root = document.CaptureSnapshot().SemanticModel.RootScopeId;
        var before = document.CaptureSnapshot();
        var scope = before.VisualModel.RoutingScopes!.Value.Single(state => state.ScopeId == root);
        var savedNode = scope.Geometry.Nodes.Single(node => node.VisualStateId == A22ViewTaskVisualId);
        var region = scope.Geometry.Regions.Single(value => value.Id == savedNode.RegionId);
        var transform = region.LocalToScopeTransform!.Value;
        var regionOffset = new VectorD(transform.OffsetX, transform.OffsetY);
        Assert.NotEqual(new VectorD(0d, 0d), regionOffset);
        var scene = session.CaptureState().CurrentScene!;
        var body = A22ViewBody(scene, A22ViewTaskId);
        Assert.Equal(savedNode.LocalBounds.Translate(regionOffset), body.Bounds);
        Assert.Equal(savedNode.LocalBounds, body.MapPresentationToLocal(body.Bounds));
        Assert.Same(before, document.CaptureSnapshot());

        var start = Center(body.Bounds);
        var end = start + new VectorD(20d, 30d);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 224);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 224, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 224);
        await session.WaitForIdleAsync();

        var changed = document.CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == A22ViewTaskVisualId);
        Assert.Equal(new PointD(70d, 90d), changed.Position);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), document.CaptureSnapshot().SemanticModel.ProfileAssignments);
        await host.UndoAsync();
        Assert.Equal(new PointD(50d, 60d), document.CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == A22ViewTaskVisualId).Position);
        Assert.Equal(body.Bounds, A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22ProcessNavigationHistoryRetainsPoolPresentationAndExactScope(bool redo)
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-navigation-history", "container");
        var session = Session(host);
        var peer = new DocumentScopeId("a22:host:peer");
        Assert.True((await session.NavigateToScopeAsync(peer)).Succeeded);
        if (redo) Assert.True((await session.UndoAsync()).IsApplied);
        var document = AttachedDocument(session).CaptureSnapshot();
        var entries = session.CaptureState().HistoryStatus.EntryCount;

        var replayed = redo ? await session.RedoAsync() : await session.UndoAsync();

        Assert.True(replayed.IsApplied);
        var state = session.CaptureState();
        var target = redo ? peer : document.SemanticModel.RootScopeId;
        Assert.Equal(new ProcessScopePresentationContext(target), state.PresentationContext);
        Assert.Equal(target, state.ActiveScopeId);
        Assert.Equal(redo ? 0 : 6, state.ProjectedGraph!.Nodes.Length);
        Assert.Equal(entries, state.HistoryStatus.EntryCount);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task A22ComposedProcessRouteKeepsScopeOwnershipAndEditsBendsInLocalCoordinates()
    {
        await using var host = CreatePropertiesUxHost(A22ProcessConnectorSnapshot());
        await host.InitializeAsync("a22-process-route", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
            document.Revision, A22ProcessFlowVisual, ConnectorRoutingType.Manual))).IsCommitted);
        await session.WaitForIdleAsync();
        var record = A22ProcessRoute(document.CaptureSnapshot());
        var bend = new PointD(240d, 190d);
        Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId,
            document.Revision, A22ProcessFlowVisual, [record.Path[0], bend, record.Path[^1]]))).IsCommitted);
        await session.WaitForIdleAsync();
        var process = document.CaptureSnapshot();
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ProcessFlowVisual]))).Succeeded);
        await session.WaitForIdleAsync();

        Assert.Same(process, document.CaptureSnapshot());
        Assert.Empty(process.VisualModel.DocumentRoutingStates);
        var scene = session.CaptureState().CurrentScene!;
        var connector = Assert.Single(scene.Items, item => item.Origin.VisualStateId == A22ProcessFlowVisual &&
            item.ConnectorPresentationMapping is not null && item.Layer == Canvas2DSceneLayer.Connector &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(session.CaptureState().ProjectedGraph!.Edges.Single().Id, "connector"));
        Assert.Equal(A22ProcessRoute(process).Path.AsEnumerable(), connector.ConnectorPresentationMapping!.CanonicalEditablePath);
        var translation = new VectorD(0d, 0d);
        Assert.Equal(A22ProcessRoute(process).Path.Select(point => point + translation),
            Canvas2DConnectorPathMetadata.ResolveEditable(connector));
        var handle = Assert.Single(scene.Items, item => item.Origin.VisualStateId == A22ProcessFlowVisual &&
            item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.BendIndex));
        var start = Center(handle.Bounds);
        Assert.Equal(bend + translation, start);
        var end = start + new VectorD(20d, 30d);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 223);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 223, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 223);
        await session.WaitForIdleAsync();

        var changed = document.CaptureSnapshot();
        Assert.Equal(bend + new VectorD(20d, 30d), A22ProcessRoute(changed).Path[1]);
        Assert.Empty(changed.VisualModel.DocumentRoutingStates);
        Assert.Equal(process.SemanticModel.ScopeMemberships.AsEnumerable(), changed.SemanticModel.ScopeMemberships);
        await host.UndoAsync();
        Assert.Equal(bend, A22ProcessRoute(document.CaptureSnapshot()).Path[1]);
        Assert.Equal(A22ViewContext, session.CaptureState().PresentationContext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22ComposedNodeAnchorEditingUsesNormalCommandsAndRetainsGeometry(bool participant)
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-anchor", "container");
        var session = Session(host);
        var visualId = participant ? A22ViewParticipantVisualId : A22ViewTaskVisualId;
        var semanticId = participant ? A22ViewParticipantId : A22ViewTaskId;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visualId]))).Succeeded);
        await session.WaitForIdleAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var original = before.VisualModel.VisualStates.Single(visual => visual.Id == visualId);
        var scene = session.CaptureState().CurrentScene!;
        var bounds = A22ViewBody(scene, semanticId).Bounds;
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene,
            new PointD(bounds.Right, bounds.Top + bounds.Height * 0.25d));
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor,
            host.CaptureState().ContextMenu?.ConnectorAnchorAction?.Kind);
        Assert.True((await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source))?.IsCommitted);
        await session.WaitForIdleAsync();

        var changed = AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == visualId);
        Assert.Single(changed.ConnectorAnchors);
        Assert.Equal(original.Position, changed.Position);
        Assert.Equal(original.Size, changed.Size);
        Assert.Equal(original.Properties, changed.Properties);
        Assert.Equal(A22ViewContext, session.CaptureState().PresentationContext);
        await host.UndoAsync();
        Assert.Equal(original, AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates.Single(visual => visual.Id == visualId));
    }

    private static ConnectorRoutingRecord A22ProcessRoute(DocumentSnapshot snapshot) =>
        snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == A22ProcessFlowVisual);

    private static DocumentSnapshot A22ProcessConnectorSnapshot()
    {
        var source = A22ViewSnapshot();
        var next = new SemanticElementId("a22:process-flow:next");
        var flow = new SemanticElementId("a22:process-flow");
        var semantic = new SemanticModelSnapshot(source.DocumentId, source.Revision,
            source.SemanticModel.Elements.Append(BpmnSemanticFactory.CreateTask(next, "NEXT", "Next", 3)),
            [BpmnSemanticFactory.CreateSequenceFlow(flow, A22ViewTaskId, next)],
            source.SemanticModel.NestedScopes, source.SemanticModel.ScopeMemberships,
            source.SemanticModel.ModelProfiles, source.SemanticModel.ProfileAssignments.Append(
                new(Inceptus.DocumentEngine.Organizational.Profiles.OrganizationalModelProfile.Id, next, A22ViewPoolId)));
        var visuals = new VisualModelSnapshot(source.DocumentId, source.Revision,
            source.VisualModel.VisualStates.Concat([
                new VisualStateSnapshot(new VisualStateId("a22:process-flow:next:visual"), next,
                    new PointD(350d, 60d), new SizeD(120d, 80d), VisualPlacementMode.Pinned),
                new VisualStateSnapshot(A22ProcessFlowVisual, flow, default, default, VisualPlacementMode.Automatic)]));
        return new DocumentSnapshot(semantic, visuals, source.Metadata);
    }
}
