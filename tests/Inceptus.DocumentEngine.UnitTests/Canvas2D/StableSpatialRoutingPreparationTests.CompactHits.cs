using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class StableSpatialRoutingPreparationTests
{
    [Fact]
    public async Task ActualCompactVisibleNodesConnectorsLabelsArrowsAndSelectionHitOnlyCurrentGeometry()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var renamed = await fixture.Processor.ExecuteAsync(fixture.Document, new UpdateBpmnSequenceFlowNameCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, new SemanticElementId("flow-b"), "Visible label"));
        Assert.True(renamed.IsCommitted, Describe(renamed.Diagnostics));
        var snapshot = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(snapshot.VisualModel.RoutingScopes!.Value);
        var bytes = NativeDocumentSerializer.Export(snapshot);
        fixture.Policy.Searches.Clear();
        var graph = fixture.Configuration.ProjectionEngine.Project(snapshot, fixture.Configuration.ProjectionContext).Graph!;
        var local = ConnectorRoutingStatePreparer.RestoreLocalLayout(graph, scope.Geometry);
        var routing = ConnectorRoutingStatePreparer.RestoreRouting(graph, local, scope, fixture.Configuration.RoutingAlgorithmId);
        var flow = graph.Edges.Single(edge => edge.Source.VisualStateId == new VisualStateId("v:flow-b"));
        var source = graph.Nodes.Single(node => node.Source.VisualStateId == new VisualStateId("v:b-source"));
        var label = graph.Labels.Single(label => label.OwnerId == flow.Id);
        var expanded = Build(false, EditorStateSnapshot.Empty);
        var compact = Build(true, EditorStateSnapshot.Empty);
        var delta = new VectorD(0, -198.4);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(source.Id, "node");
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(flow.Id, "connector");
        var arrowId = Canvas2DSceneObjectIdentity.ForProjected(flow.Id, "connector-target-arrow");
        var oldLabel = Assert.Single(expanded.Items, item => item.Origin.ProjectedObjectId == label.Id &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
        var currentLabel = compact.Items.Single(item => item.Id == oldLabel.Id);
        var arrow = compact.Items.Single(item => item.Id == arrowId);
        Assert.Equal(new PointD(610, 671.6), arrow.Geometry.Points[0]);
        AssertBoundsNear(oldLabel.Bounds.Translate(delta), currentLabel.Bounds);
        AssertBoundsNear(expanded.Items.Single(item => item.Id == nodeId).Bounds.Translate(delta),
            compact.Items.Single(item => item.Id == nodeId).Bounds);
        var hit = new Canvas2DSceneHitTestService();
        var nodePoint = new PointD(140, 641.6);
        Assert.Equal(source.Source.VisualStateId, hit.HitTest(compact, nodePoint)?.Origin.VisualStateId);
        Assert.NotEqual(source.Source.VisualStateId, hit.HitTest(compact, nodePoint - delta)?.Origin.VisualStateId);
        foreach (var (id, currentPoint) in new[]
        {
            (connectorId, new PointD(300, 671.6)),
            (arrowId, new PointD(605, 671.6)),
            (oldLabel.Id, CenterOf(currentLabel.Bounds)),
        })
        {
            Assert.Equal(id, hit.HitTest(compact, currentPoint)?.SceneObjectId);
            Assert.NotEqual(id, hit.HitTest(compact, currentPoint - delta)?.SceneObjectId);
        }
        var selected = Build(true, new EditorStateSnapshot(selection: [flow.Source.VisualStateId!],
            hoveredObjectId: connectorId));
        var endpoint = Assert.Single(selected.Items, item => item.Origin.VisualStateId == flow.Source.VisualStateId &&
            item.Metadata.TryGetValue(Canvas2DConnectorEndpointMetadata.HandleRole, out var role) &&
            role.TextValue == Canvas2DConnectorEndpointMetadata.EndEndpointRole);
        Assert.Equal(new PointD(610, 671.6), CenterOf(endpoint.Bounds));
        Assert.Equal(endpoint.Id, hit.HitTest(selected, new PointD(610, 671.6))?.SceneObjectId);
        Assert.NotEqual(endpoint.Id, hit.HitTest(selected, new PointD(610, 870))?.SceneObjectId);
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(bytes.AsEnumerable(), NativeDocumentSerializer.Export(snapshot).AsEnumerable());

        Canvas2DScene Build(bool collapse, EditorStateSnapshot editor)
        {
            var result = fixture.Configuration.SceneBuilder.Build(snapshot, scope.ScopeId,
                ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(OrganizationalModelProfile.Id, PoolA, collapse),
                graph, local, routing, snapshot.VisualModel, editor);
            Assert.True(result.Succeeded, Describe(result.Diagnostics));
            return result.Scene!;
        }
    }

    private static PointD CenterOf(RectD bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private static void AssertBoundsNear(RectD expected, RectD actual)
    {
        Assert.Equal(expected.Left, actual.Left, 9);
        Assert.Equal(expected.Top, actual.Top, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
        Assert.Equal(expected.Height, actual.Height, 9);
    }
}
