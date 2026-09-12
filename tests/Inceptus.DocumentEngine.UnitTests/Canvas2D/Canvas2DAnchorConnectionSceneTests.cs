using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DAnchorConnectionSceneTests
{
    [Fact]
    internal void GestureMetadataRoundTripsSourceAndOptionalTargetIdentities()
    {
        var sourceSemanticId = new SemanticElementId("semantic:source");
        var sourceVisualId = new VisualStateId("visual:source");
        var sourceAnchorId = new ConnectorAnchorId("anchor:source");
        var targetSemanticId = new SemanticElementId("semantic:target");
        var targetVisualId = new VisualStateId("visual:target");
        var targetAnchorId = new ConnectorAnchorId("anchor:target");

        var withoutTarget = Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
            sourceSemanticId,
            sourceVisualId,
            sourceAnchorId,
            new AnchorConnectionCreationId("connection:create"));
        var withTarget = Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
            sourceSemanticId,
            sourceVisualId,
            sourceAnchorId,
            new AnchorConnectionCreationId("connection:create"),
            targetSemanticId,
            targetVisualId,
            targetAnchorId);

        Assert.True(Canvas2DAnchorConnectionGestureMetadata.TryRead(
            withoutTarget,
            out var decodedSource));
        Assert.NotNull(decodedSource);
        Assert.Equal(sourceSemanticId, decodedSource.SourceSemanticElementId);
        Assert.Equal(sourceVisualId, decodedSource.SourceVisualStateId);
        Assert.Equal(sourceAnchorId, decodedSource.SourceAnchorId);
        Assert.Equal(
            new AnchorConnectionCreationId("connection:create"),
            decodedSource.ConnectionCreationId);
        Assert.False(decodedSource.HasTarget);

        Assert.True(Canvas2DAnchorConnectionGestureMetadata.TryRead(
            withTarget,
            out var decodedTarget));
        Assert.NotNull(decodedTarget);
        Assert.True(decodedTarget.HasTarget);
        Assert.Equal(targetSemanticId, decodedTarget.TargetSemanticElementId);
        Assert.Equal(targetVisualId, decodedTarget.TargetVisualStateId);
        Assert.Equal(targetAnchorId, decodedTarget.TargetAnchorId);

        var proposed = TargetAnchorAcquisitionResult.Proposed(
            targetSemanticId,
            targetVisualId,
            ConnectorAnchorSide.Left,
            targetAnchorId,
            new PointD(200d, 45d),
            1);
        var withProposal = Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
            sourceSemanticId,
            sourceVisualId,
            sourceAnchorId,
            new AnchorConnectionCreationId("connection:create"),
            proposed);
        Assert.True(Canvas2DAnchorConnectionGestureMetadata.TryRead(
            withProposal,
            out var decodedProposal));
        Assert.True(decodedProposal!.HasProposedTarget);
        Assert.Equal(ConnectorAnchorSide.Left, decodedProposal.TargetSide);
        Assert.Equal(1, decodedProposal.TargetInsertionIndex);
    }

    [Fact]
    internal void GestureMetadataRejectsPartialTargetIdentityTuple()
    {
        Assert.Throws<ArgumentException>(() =>
            Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
                new SemanticElementId("semantic:source"),
                new VisualStateId("visual:source"),
                new ConnectorAnchorId("anchor:source"),
                new AnchorConnectionCreationId("connection:create"),
                targetSemanticElementId: new SemanticElementId("semantic:target")));

        var partial = new PropertyMap(
        [
            new(Canvas2DAnchorConnectionGestureMetadata.SourceSemanticElementId,
                PropertyValue.FromText("semantic:source")),
            new(Canvas2DAnchorConnectionGestureMetadata.SourceVisualStateId,
                PropertyValue.FromText("visual:source")),
            new(Canvas2DAnchorConnectionGestureMetadata.SourceAnchorId,
                PropertyValue.FromText("anchor:source")),
            new(Canvas2DAnchorConnectionGestureMetadata.ConnectionCreationId,
                PropertyValue.FromText("connection:create")),
            new(Canvas2DAnchorConnectionGestureMetadata.TargetAnchorId,
                PropertyValue.FromText("anchor:target")),
        ]);

        Assert.False(Canvas2DAnchorConnectionGestureMetadata.TryRead(partial, out _));
    }

    [Fact]
    internal void ActiveGestureShowsOnlyActualDynamicTargetAnchorsOnUnselectedNodes()
    {
        var source = CreateSource();
        var sourceVisual = Visual(source, "test:semantic:a");
        var targetVisual = Visual(source, "test:semantic:b");
        var gesture = Gesture(sourceVisual);
        var state = new EditorStateSnapshot(
            selection: [sourceVisual.Id],
            activeGesture: gesture);

        var scene = Scene(source, state);
        var candidates = CandidateHandles(scene);
        var candidate = Assert.Single(candidates);

        Assert.Equal(TargetAnchorId, AnchorId(candidate));
        Assert.Equal(targetVisual.SemanticElementId, candidate.Origin.SemanticElementId);
        Assert.Equal(targetVisual.Id, candidate.Origin.VisualStateId);
        Assert.Equal(Canvas2DSceneLayer.Overlay, candidate.Layer);
        Assert.Equal(Canvas2DConnectorAnchorMetadata.HandleZIndex, candidate.ZIndex);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, candidate.HitTestPolicy.Mode);
        Assert.Equal(
            candidate.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, Center(candidate))?.SceneObjectId);
        Assert.DoesNotContain(targetVisual.Id, state.Selection);

        var visibleAnchorIds = AnchorHandles(scene).Select(AnchorId).ToArray();
        Assert.Contains(SourceAnchorId, visibleAnchorIds);
        Assert.Contains(TargetAnchorId, visibleAnchorIds);
        Assert.DoesNotContain(TargetSourceAnchorId, visibleAnchorIds);
        Assert.DoesNotContain(PhantomTargetAnchorId, visibleAnchorIds);
        Assert.DoesNotContain(PredefinedTargetAnchorId(targetVisual.Id), visibleAnchorIds);

        var idle = Scene(source, new EditorStateSnapshot(selection: [sourceVisual.Id]));
        Assert.Empty(CandidateHandles(idle));
        Assert.DoesNotContain(TargetAnchorId, AnchorHandles(idle).Select(AnchorId));
    }

    [Fact]
    internal void ActiveGestureDoesNotExposeAnOccupiedTargetAnchorAsACandidate()
    {
        var source = CreateSource();
        var sourceVisual = Visual(source, "test:semantic:a");
        var targetVisual = Visual(source, "test:semantic:b");
        var occupiedConnector = new VisualStateSnapshot(
            new VisualStateId("visual:occupied-connector"),
            new SemanticElementId("semantic:occupied-relationship"),
            default,
            default,
            VisualPlacementMode.Manual,
            targetAnchorId: TargetAnchorId);
        source = source.WithVisualModel(new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Append(occupiedConnector)));
        var state = new EditorStateSnapshot(
            selection: [sourceVisual.Id],
            activeGesture: Gesture(sourceVisual));

        var scene = Scene(source, state);

        Assert.Empty(CandidateHandles(scene));
        Assert.DoesNotContain(TargetAnchorId, AnchorHandles(scene).Select(AnchorId));
        Assert.DoesNotContain(targetVisual.Id, state.Selection);
        Assert.Contains(SourceAnchorId, AnchorHandles(scene).Select(AnchorId));

        var selectedTargetScene = Scene(
            source,
            new EditorStateSnapshot(
                selection: [sourceVisual.Id, targetVisual.Id],
                activeGesture: Gesture(sourceVisual)));
        var visibleOccupied = Assert.Single(
            AnchorHandles(selectedTargetScene),
            handle => AnchorId(handle) == TargetAnchorId);
        Assert.DoesNotContain(visibleOccupied, CandidateHandles(selectedTargetScene));
    }

    [Fact]
    internal void SelectedOwnerHandlesAreNotDuplicatedByConnectionCandidates()
    {
        var source = CreateSource();
        var sourceVisual = Visual(source, "test:semantic:a");
        var targetVisual = Visual(source, "test:semantic:b");
        var state = new EditorStateSnapshot(
            selection: [sourceVisual.Id, targetVisual.Id],
            activeGesture: Gesture(sourceVisual));

        var handles = AnchorHandles(Scene(source, state));

        Assert.Equal(handles.Length, handles.Select(static handle => handle.Id).Distinct().Count());
        Assert.Single(handles, handle => AnchorId(handle) == TargetAnchorId);
        Assert.Empty(CandidateHandles(handles));
    }

    [Fact]
    internal void ActiveGesturePreviewIsStraightTransientAndNonHittable()
    {
        var source = CreateSource();
        var sourceVisual = Visual(source, "test:semantic:a");
        var gesture = Gesture(sourceVisual);

        var scene = Scene(
            source,
            new EditorStateSnapshot(selection: [sourceVisual.Id], activeGesture: gesture));
        var preview = Assert.Single(scene.Items, item => StringComparer.Ordinal.Equals(
            item.Origin.StableSourceKey,
            $"anchor-connection-preview:{gesture.Id}"));

        Assert.Equal(Canvas2DSceneLayer.Overlay, preview.Layer);
        Assert.Equal(Canvas2DAnchorConnectionGestureMetadata.PreviewZIndex, preview.ZIndex);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, preview.Geometry.Kind);
        Assert.Equal(new[] { gesture.Origin, gesture.Current }, preview.Geometry.Points);
        Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);
        Assert.Equal(sourceVisual.SemanticElementId, preview.Origin.SemanticElementId);
        Assert.Equal(sourceVisual.Id, preview.Origin.VisualStateId);
        Assert.NotEqual(
            preview.Id,
            new Canvas2DSceneHitTestService().HitTest(
                scene,
                new PointD(160d, 45d))?.SceneObjectId);
    }

    [Fact]
    internal void ProposedTargetPreviewSnapsConnectorAndAddsNonHittableGhostAnchor()
    {
        var source = CreateSource();
        var sourceVisual = Visual(source, "test:semantic:a");
        var targetVisual = Visual(source, "test:semantic:b");
        var proposedPoint = new PointD(200d, 45d);
        var gesture = new EditorGestureSnapshot(
            "anchor-connection:proposed",
            Canvas2DAnchorConnectionGestureMetadata.Kind,
            new PointD(110d, 45d),
            proposedPoint,
            Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
                sourceVisual.SemanticElementId,
                sourceVisual.Id,
                SourceAnchorId,
                new AnchorConnectionCreationId("connection:create"),
                TargetAnchorAcquisitionResult.Proposed(
                    targetVisual.SemanticElementId,
                    targetVisual.Id,
                    ConnectorAnchorSide.Left,
                    new ConnectorAnchorId("proposed-target"),
                    proposedPoint,
                    0)));

        var scene = Scene(
            source,
            new EditorStateSnapshot(selection: [sourceVisual.Id], activeGesture: gesture));
        var preview = Assert.Single(scene.Items, item => StringComparer.Ordinal.Equals(
            item.Origin.StableSourceKey,
            $"anchor-connection-preview:{gesture.Id}"));
        var proposed = Assert.Single(scene.Items, item => StringComparer.Ordinal.Equals(
            item.Origin.StableSourceKey,
            $"anchor-connection-proposed-target:{gesture.Id}"));

        Assert.Equal(proposedPoint, preview.Geometry.Points[^1]);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, proposed.Geometry.Kind);
        Assert.Equal(Canvas2DHitTestMode.None, proposed.HitTestPolicy.Mode);
        Assert.Equal(targetVisual.Id, proposed.Origin.VisualStateId);
        Assert.NotEqual(
            proposed.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, proposedPoint)?.SceneObjectId);
    }

    [Fact]
    internal void CanonicalProjectedNodesCarryAnExplicitGenericNodeBodyMarker()
    {
        var source = CreateSource();
        var scene = Scene(source, EditorStateSnapshot.Empty);
        var nodes = scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId is not null).ToArray();

        Assert.NotEmpty(nodes);
        Assert.All(nodes, item => Assert.True(Canvas2DNodeBodyMetadata.IsNodeBody(item)));
    }

    private static readonly ConnectorAnchorId SourceAnchorId = new("anchor:a:source");
    private static readonly ConnectorAnchorId TargetAnchorId = new("anchor:b:target");
    private static readonly ConnectorAnchorId TargetSourceAnchorId = new("anchor:b:source");
    private static readonly ConnectorAnchorId PhantomTargetAnchorId = new("anchor:b:phantom");

    private static Canvas2DSceneTestData CreateSource()
    {
        var source = Canvas2DSceneTestData.Create();
        var sourceVisual = Visual(source, "test:semantic:a");
        var targetVisual = Visual(source, "test:semantic:b");
        var sourceAnchor = new ConnectorAnchor(
            SourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        var targetAnchor = new ConnectorAnchor(
            TargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        var targetSourceAnchor = new ConnectorAnchor(
            TargetSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Select(visual =>
                visual.Id == sourceVisual.Id
                    ? Copy(visual, [sourceAnchor])
                    : visual.Id == targetVisual.Id
                        ? Copy(visual, [targetAnchor, targetSourceAnchor])
                        : visual));

        var sourceNode = source.Graph.Nodes.Single(node =>
            node.Source.VisualStateId == sourceVisual.Id);
        var targetNode = source.Graph.Nodes.Single(node =>
            node.Source.VisualStateId == targetVisual.Id);
        var ports = new[]
        {
            Port(sourceNode, sourceVisual, new ProjectedConnectorAnchor(
                SourceAnchorId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.Source,
                0,
                1,
                ResolvedConnectorAnchorKind.Dynamic)),
            Port(targetNode, targetVisual, new ProjectedConnectorAnchor(
                TargetAnchorId,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRoleCapability.Target,
                0,
                1,
                ResolvedConnectorAnchorKind.Dynamic)),
            Port(targetNode, targetVisual, new ProjectedConnectorAnchor(
                TargetSourceAnchorId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.Source,
                0,
                1,
                ResolvedConnectorAnchorKind.Dynamic)),
            Port(targetNode, targetVisual, new ProjectedConnectorAnchor(
                PhantomTargetAnchorId,
                ConnectorAnchorSide.Bottom,
                ConnectorAnchorRoleCapability.Target,
                0,
                1,
                ResolvedConnectorAnchorKind.Dynamic)),
            Port(targetNode, targetVisual, new ProjectedConnectorAnchor(
                PredefinedTargetAnchorId(targetVisual.Id),
                ConnectorAnchorSide.Top,
                ConnectorAnchorRoleCapability.Target,
                0,
                1,
                ResolvedConnectorAnchorKind.Predefined)),
        };
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports.Concat(ports),
            source.Graph.Labels);
        return source
            .WithVisualModel(visualModel)
            .WithGraph(graph);
    }

    private static ProjectedPort Port(
        ProjectedNode owner,
        VisualStateSnapshot visual,
        ProjectedConnectorAnchor anchor) =>
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
            routingHints: ProjectedConnectorAnchorMetadata.Encode(anchor));

    private static ConnectorAnchorId PredefinedTargetAnchorId(VisualStateId ownerId) =>
        ConnectorAnchorReferenceIdentity.ForPredefined(
            ownerId,
            new PredefinedConnectorAnchorDefinitionId("definition:target"));

    private static EditorGestureSnapshot Gesture(VisualStateSnapshot sourceVisual) =>
        new(
            "anchor-connection:test",
            Canvas2DAnchorConnectionGestureMetadata.Kind,
            new PointD(110d, 45d),
            new PointD(190d, 45d),
            Canvas2DAnchorConnectionGestureMetadata.CreateProperties(
                sourceVisual.SemanticElementId,
                sourceVisual.Id,
                SourceAnchorId,
                new AnchorConnectionCreationId("connection:create")));

    private static VisualStateSnapshot Copy(
        VisualStateSnapshot visual,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            visual.Id,
            visual.SemanticElementId,
            visual.Position,
            visual.Size,
            visual.PlacementMode,
            visual.Route,
            visual.Properties,
            anchors,
            visual.SourceAnchorId,
            visual.TargetAnchorId);

    private static VisualStateSnapshot Visual(
        Canvas2DSceneTestData source,
        string semanticId) =>
        source.VisualModel.VisualStates.Single(visual => StringComparer.Ordinal.Equals(
            visual.SemanticElementId.Value,
            semanticId));

    private static Canvas2DScene Scene(
        Canvas2DSceneTestData source,
        EditorStateSnapshot editorState) =>
        Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            source.Graph,
            source.Layout,
            source.Routing,
            source.VisualModel,
            editorState).Scene);

    private static Canvas2DSceneItem[] AnchorHandles(Canvas2DScene scene) =>
        scene.Items.Where(item => item.Metadata.ContainsKey(
            Canvas2DConnectorAnchorMetadata.AnchorId)).ToArray();

    private static Canvas2DSceneItem[] CandidateHandles(Canvas2DScene scene) =>
        CandidateHandles(AnchorHandles(scene));

    private static Canvas2DSceneItem[] CandidateHandles(
        IEnumerable<Canvas2DSceneItem> handles) =>
        handles.Where(item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorAnchorMetadata.ConnectionTargetCandidate,
                out var candidate) &&
            candidate.Kind == PropertyValueKind.Boolean &&
            candidate.BooleanValue).ToArray();

    private static ConnectorAnchorId AnchorId(Canvas2DSceneItem handle) =>
        new(handle.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue);

    private static PointD Center(Canvas2DSceneItem item) => new(
        item.Bounds.X + (item.Bounds.Width / 2d),
        item.Bounds.Y + (item.Bounds.Height / 2d));
}
