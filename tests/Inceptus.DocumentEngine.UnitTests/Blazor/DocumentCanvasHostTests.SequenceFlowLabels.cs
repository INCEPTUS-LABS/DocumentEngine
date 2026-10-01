using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("demo:bpmn:sequence-flow:event-based-gateway-to-message", "Message", 45d)]
    [InlineData("demo:bpmn:sequence-flow:event-based-gateway-to-timer", "Timeout", -45d)]
    public async Task DemoEventBranchesRenderAuthoredLabelsWithoutChangingTheDocument(
        string relationshipId, string expectedName, double offsetY)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var initial = composition.Document.CaptureSnapshot();
        await using var host = CreatePropertiesUxHost(initial);
        await host.InitializeAsync("release-demo-labels", "release-demo-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var flowId = new SemanticElementId(relationshipId);
        var flow = document.SemanticModel.Relationships.Single(item => item.Id == flowId);
        Assert.Equal(expectedName, flow.Properties[BpmnSemanticProperties.Name].TextValue);
        var visual = document.VisualModel.VisualStates.Single(item => item.SemanticElementId == flowId);
        Assert.True(ConnectorLabelPlacement.TryRead(visual.Properties, out var placement));
        Assert.Equal(new ConnectorLabelPlacement(0d, new VectorD(90d, offsetY)), placement);
        var labels = session.CaptureState().CurrentScene!.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label && item.Origin.VisualStateId == visual.Id &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text).ToArray();
        Assert.NotEmpty(labels);
        var projectedLabel = Assert.Single(session.CaptureState().ProjectedGraph!.Labels,
            item => item.Source.SemanticElementId == flowId);
        Assert.Equal(expectedName, projectedLabel.Text);
        Assert.NotNull(projectedLabel.ConnectorPlacement);
        Assert.True(SequenceFlowLabelBounds(host, visual.Id).Width > 0d);
        var bounds = SequenceFlowLabelBounds(host, visual.Id);
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == BpmnDemoPipeline.EventBasedGatewayVisualId &&
            bounds.Intersects(item.Bounds));
        Assert.Equal(initial, document.CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task SequenceFlowNamePropertiesSetRenameAndClearUseOneHistoryEntryEach()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("a11-properties", "a11-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var flowId = BpmnDemoPipeline.ThirdSequenceFlowId;
        var visualId = document.VisualModel.VisualStates.Single(item => item.SemanticElementId == flowId).Id;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visualId]))).Succeeded);
        await session.WaitForIdleAsync();
        foreach (var name in new[] { "Approved", "Accepted", string.Empty })
        {
            var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(visualId));
            var draft = new DocumentCanvasPropertiesDraft(snapshot);
            var field = Assert.Single(draft.DataFields);
            Assert.Equal("Name", field.Definition.DisplayName);
            Assert.True(field.CanEdit);
            field.EditorValue = name;
            Assert.True(host.UpdatePropertiesFormState(true, visualId, draft.IsDirty));
            var before = document.CaptureSnapshot();
            var history = session.CaptureState().HistoryStatus.EntryCount;
            var result = await host.ApplyPropertiesAsync(draft);
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(before.Revision.Increment(), document.Revision);
            Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
            Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
            var relationship = document.SemanticModel.Relationships.Single(item => item.Id == flowId);
            Assert.Equal(name.Length > 0, relationship.Properties.TryGetValue(BpmnSemanticProperties.Name, out var value));
            Assert.Equal(name.Length > 0 ? name : null, value?.TextValue);
            Assert.True(host.UpdatePropertiesFormState(false, null, false));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SequenceFlowContextResetPreservesExactManualStateAndSemanticGeometry(
        bool labelOrigin, bool oldMidpointValues)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("a11-reset", "a11-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var flowId = BpmnDemoPipeline.ThirdSequenceFlowId;
        var visualId = document.VisualModel.VisualStates.Single(item => item.SemanticElementId == flowId).Id;
        Assert.True((await session.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(
            document.DocumentId, document.Revision, flowId, "Approved"))).IsCommitted);
        await session.WaitForIdleAsync();
        var automatic = SequenceFlowLabelBounds(host, visualId);
        var automaticMenu = await OpenSequenceFlowMenu(host, visualId, false);
        Assert.Null(automaticMenu.ConnectorLabelAction);
        Assert.Null(await host.ExecuteConnectorLabelContextActionAsync());
        var manual = oldMidpointValues ? ConnectorLabelPlacement.Default :
            new ConnectorLabelPlacement(0.7d, new VectorD(12d, 64d));
        Assert.True((await session.ExecuteAsync(new MoveLabelCommand(document.DocumentId,
            document.Revision, visualId, manual))).IsCommitted);
        await session.WaitForIdleAsync();
        var manualBounds = SequenceFlowLabelBounds(host, visualId);
        Assert.NotEqual(automatic, manualBounds);
        var menu = await OpenSequenceFlowMenu(host, visualId, labelOrigin);
        Assert.Equal(visualId, menu.TargetVisualStateId);
        Assert.Equal(Canvas2DSceneLayer.Connector, menu.SourceScene.Items.Single(item =>
            item.Id == menu.TargetSceneObjectId).Layer);
        Assert.Equal(flowId, menu.ConnectorLabelAction?.RelationshipId);
        Assert.Equal(manual, menu.ConnectorLabelAction?.ManualPlacement);
        Assert.Equal(DiagramDeletionTargetKind.Connection, menu.DeletionAction?.TargetKind);
        if (!labelOrigin)
        {
            Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, menu.ConnectorRouteAction?.Kind);
        }
        else
        {
            Assert.Null(menu.ConnectorRouteAction);
        }
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var hoverTask = menu.SourceScene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        await PointerObserver(host).MoveDocumentPointAsync(menu.SourceScene, Center(hoverTask.Bounds));
        Assert.NotSame(menu.SourceScene, session.CaptureState().CurrentScene);
        var result = await host.ExecuteConnectorLabelContextActionAsync();
        Assert.True(result?.IsCommitted);
        Assert.Equal(MoveLabelCommand.KnownTypeId, result?.CommandTypeId);
        Assert.Equal(before.Revision.Increment(), document.Revision);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements);
        foreach (var oldVisual in before.VisualModel.VisualStates)
        {
            var current = document.VisualModel.VisualStates.Single(item => item.Id == oldVisual.Id);
            if (oldVisual.Id != visualId)
            {
                Assert.Equal(oldVisual, current);
                continue;
            }
            Assert.Equal(oldVisual.Route, current.Route);
            Assert.Equal(oldVisual.ConnectorAnchors, current.ConnectorAnchors);
            Assert.Equal(oldVisual.SourceAnchorId, current.SourceAnchorId);
            Assert.Equal(oldVisual.TargetAnchorId, current.TargetAnchorId);
            Assert.Equal(oldVisual.Position, current.Position);
            Assert.Equal(oldVisual.Size, current.Size);
            Assert.False(ConnectorLabelPlacement.TryRead(current.Properties, out _));
        }
        Assert.Equal(automatic, SequenceFlowLabelBounds(host, visualId));
        Assert.Null((await OpenSequenceFlowMenu(host, visualId, false)).ConnectorLabelAction);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(manualBounds, SequenceFlowLabelBounds(host, visualId));
        Assert.Equal(manual, (await OpenSequenceFlowMenu(host, visualId, labelOrigin)).ConnectorLabelAction?.ManualPlacement);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(automatic, SequenceFlowLabelBounds(host, visualId));
        Assert.Null((await OpenSequenceFlowMenu(host, visualId, false)).ConnectorLabelAction);
        Assert.False(ConnectorLabelPlacement.TryRead(document.VisualModel.VisualStates.Single(item => item.Id == visualId).Properties, out _));
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(visualId));
        Assert.Equal("Approved", Assert.Single(new DocumentCanvasPropertiesDraft(properties).DataFields).EditorValue);
    }

    [Theory]
    [InlineData("scene")]
    [InlineData("revision")]
    [InlineData("automatic")]
    [InlineData("relationship")]
    [InlineData("visual")]
    [InlineData("placement")]
    public async Task SequenceFlowContextResetRejectsStaleTargetsWithoutRevisionOrHistory(string change)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("a11-stale", "a11-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var visualId = document.VisualModel.VisualStates.Single(item =>
            item.SemanticElementId == BpmnDemoPipeline.ThirdSequenceFlowId).Id;
        Assert.True((await session.ExecuteAsync(new MoveLabelCommand(document.DocumentId,
            document.Revision, visualId, ConnectorLabelPlacement.Default))).IsCommitted);
        await session.WaitForIdleAsync();
        var menu = await OpenSequenceFlowMenu(host, visualId, false);
        Assert.NotNull(menu.ConnectorLabelAction);
        if (change == "scene")
        {
            Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(2d, default))).Succeeded);
            Assert.NotSame(menu.SourceScene, session.CaptureState().CurrentScene);
            Assert.NotEqual(menu.SessionGeneration, session.CaptureState().Generation);
        }
        else if (change is "revision" or "automatic")
        {
            Assert.True((await session.ExecuteAsync(new MoveLabelCommand(document.DocumentId,
                document.Revision, visualId, change == "automatic" ? null :
                    new ConnectorLabelPlacement(0.6d, new VectorD(2d, 3d))))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        else
        {
            menu = change switch
            {
                "relationship" => menu with
                {
                    ConnectorLabelAction = menu.ConnectorLabelAction! with
                    { RelationshipId = BpmnDemoPipeline.FirstSequenceFlowId }
                },
                "visual" => menu with { TargetVisualStateId = BpmnDemoPipeline.TaskVisualId },
                _ => menu with
                {
                    ConnectorLabelAction = menu.ConnectorLabelAction! with
                    { ManualPlacement = new ConnectorLabelPlacement(0.2d, new VectorD(1d, 1d)) }
                },
            };
            typeof(DocumentCanvasHost).GetField("_contextMenu", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.SetValue(host, menu);
        }
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.Null(await host.ExecuteConnectorLabelContextActionAsync());
        Assert.Equal(before.Revision, document.Revision);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    private static RectD SequenceFlowLabelBounds(DocumentCanvasHost host, VisualStateId visualId) =>
        Canvas2DConnectorLabelResolver.PresentedBounds(host.CaptureState().Session!.CurrentScene!.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label && item.Origin.VisualStateId == visualId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text));

    private static async Task<DocumentCanvasContextMenuState> OpenSequenceFlowMenu(
        DocumentCanvasHost host, VisualStateId visualId, bool labelOrigin)
    {
        var scene = host.CaptureState().Session!.CurrentScene!;
        var edge = host.CaptureState().Session!.ProjectedGraph!.Edges.Single(item => item.Source.VisualStateId == visualId);
        var connector = scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
        var path = Canvas2DConnectorPathMetadata.Resolve(connector).Select(connector.Transform.TransformPoint).ToArray();
        var point = labelOrigin ? Center(SequenceFlowLabelBounds(host, visualId)) :
            new PointD((path[0].X + path[1].X) / 2d, (path[0].Y + path[1].Y) / 2d);
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, point);
        return Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
    }
}
