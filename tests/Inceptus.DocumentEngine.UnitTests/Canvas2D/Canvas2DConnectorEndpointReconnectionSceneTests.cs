using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorEndpointReconnectionSceneTests
{
    private static readonly ConnectorAnchorId OriginalSourceAnchorId =
        new("anchor:a:source:original");
    private static readonly ConnectorAnchorId AlternativeSourceAnchorId =
        new("anchor:a:source:alternative");
    private static readonly ConnectorAnchorId OccupiedSourceAnchorId =
        new("anchor:a:source:occupied");
    private static readonly ConnectorAnchorId PhantomSourceAnchorId =
        new("anchor:a:source:phantom");
    private static readonly ConnectorAnchorId OriginalTargetAnchorId =
        new("anchor:b:target:original");
    private static readonly ConnectorAnchorId AlternativeTargetAnchorId =
        new("anchor:b:target:alternative");
    private static readonly ConnectorAnchorId OccupiedTargetAnchorId =
        new("anchor:b:target:occupied");

    [Fact]
    internal void GestureMetadataRoundTripsOriginalAndOptionalCandidateIdentities()
    {
        var relationshipId = new SemanticElementId("semantic:relationship");
        var connectorVisualStateId = new VisualStateId("visual:connector");
        var originalSemanticEndpointId = new SemanticElementId("semantic:original");
        var originalAnchorId = new ConnectorAnchorId("anchor:original");
        var reconnectionId = new ConnectorEndpointReconnectionId("reconnect:test");
        var connectorSceneObjectId = new SceneObjectId("scene:connector");
        var candidateSemanticElementId = new SemanticElementId("semantic:candidate");
        var candidateVisualStateId = new VisualStateId("visual:candidate");
        var candidateAnchorId = new ConnectorAnchorId("anchor:candidate");

        var withoutCandidate =
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
                relationshipId,
                connectorVisualStateId,
                ConnectorEndpointKind.Source,
                originalSemanticEndpointId,
                originalAnchorId,
                reconnectionId,
                connectorSceneObjectId);
        var withCandidate =
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
                relationshipId,
                connectorVisualStateId,
                ConnectorEndpointKind.Target,
                originalSemanticEndpointId,
                originalAnchorId,
                reconnectionId,
                connectorSceneObjectId,
                candidateSemanticElementId,
                candidateVisualStateId,
                candidateAnchorId);

        Assert.True(Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
            withoutCandidate,
            out var decodedOriginal));
        Assert.NotNull(decodedOriginal);
        Assert.Equal(relationshipId, decodedOriginal.RelationshipId);
        Assert.Equal(connectorVisualStateId, decodedOriginal.ConnectorVisualStateId);
        Assert.Equal(ConnectorEndpointKind.Source, decodedOriginal.EndpointKind);
        Assert.Equal(originalSemanticEndpointId, decodedOriginal.OriginalSemanticEndpointId);
        Assert.Equal(originalAnchorId, decodedOriginal.OriginalAnchorId);
        Assert.Equal(reconnectionId, decodedOriginal.ReconnectionId);
        Assert.Equal(connectorSceneObjectId, decodedOriginal.ConnectorSceneObjectId);
        Assert.False(decodedOriginal.HasCandidate);

        Assert.True(Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
            withCandidate,
            out var decodedCandidate));
        Assert.NotNull(decodedCandidate);
        Assert.Equal(ConnectorEndpointKind.Target, decodedCandidate.EndpointKind);
        Assert.True(decodedCandidate.HasCandidate);
        Assert.Equal(candidateSemanticElementId, decodedCandidate.CandidateSemanticElementId);
        Assert.Equal(candidateVisualStateId, decodedCandidate.CandidateVisualStateId);
        Assert.Equal(candidateAnchorId, decodedCandidate.CandidateAnchorId);
    }

    [Fact]
    internal void GestureMetadataRejectsPartialCandidateIdentityTuple()
    {
        Assert.Throws<ArgumentException>(() =>
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
                new SemanticElementId("semantic:relationship"),
                new VisualStateId("visual:connector"),
                ConnectorEndpointKind.Target,
                new SemanticElementId("semantic:original"),
                new ConnectorAnchorId("anchor:original"),
                new ConnectorEndpointReconnectionId("reconnect:test"),
                new SceneObjectId("scene:connector"),
                candidateSemanticElementId: new SemanticElementId("semantic:candidate")));

        var complete = Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
            new SemanticElementId("semantic:relationship"),
            new VisualStateId("visual:connector"),
            ConnectorEndpointKind.Target,
            new SemanticElementId("semantic:original"),
            new ConnectorAnchorId("anchor:original"),
            new ConnectorEndpointReconnectionId("reconnect:test"),
            new SceneObjectId("scene:connector"),
            new SemanticElementId("semantic:candidate"),
            new VisualStateId("visual:candidate"),
            new ConnectorAnchorId("anchor:candidate"));
        var partial = new PropertyMap(complete.Where(entry => !StringComparer.Ordinal.Equals(
            entry.Key,
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CandidateVisualStateId)));

        Assert.False(Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
            partial,
            out _));
    }

    [Fact]
    internal void SourceReconnectionShowsOnlyRealFreeSourceAnchorsAndIncludesOriginal()
    {
        var source = CreateSource();
        var gesture = Gesture(source, ConnectorEndpointKind.Source, new PointD(80d, 90d));
        var scene = Scene(source, gesture);
        var candidates = CandidateHandles(scene);

        Assert.Equal(
            [AlternativeSourceAnchorId, OriginalSourceAnchorId],
            candidates.Select(AnchorId).OrderBy(static id => id.Value));
        Assert.DoesNotContain(OccupiedSourceAnchorId, candidates.Select(AnchorId));
        Assert.DoesNotContain(PhantomSourceAnchorId, candidates.Select(AnchorId));
        Assert.DoesNotContain(OriginalTargetAnchorId, candidates.Select(AnchorId));
        Assert.DoesNotContain(AlternativeTargetAnchorId, candidates.Select(AnchorId));
        Assert.All(candidates, candidate => Assert.Equal(
            ConnectorAnchorRole.Source.ToString(),
            candidate.Metadata[Canvas2DConnectorAnchorMetadata.Role].TextValue));

        var original = Assert.Single(
            candidates,
            candidate => AnchorId(candidate) == OriginalSourceAnchorId);
        Assert.Equal(
            original.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, Center(original))?.SceneObjectId);
        Assert.Empty(EndpointHandles(scene, Canvas2DConnectorEndpointMetadata.StartEndpointRole));
        Assert.Single(EndpointHandles(scene, Canvas2DConnectorEndpointMetadata.EndEndpointRole));
    }

    [Fact]
    internal void TargetReconnectionShowsOnlyRealFreeTargetAnchorsAndIncludesOriginal()
    {
        var source = CreateSource();
        var gesture = Gesture(source, ConnectorEndpointKind.Target, new PointD(280d, 90d));
        var scene = Scene(source, gesture);
        var candidates = CandidateHandles(scene);

        Assert.Equal(
            [AlternativeTargetAnchorId, OriginalTargetAnchorId],
            candidates.Select(AnchorId).OrderBy(static id => id.Value));
        Assert.DoesNotContain(OccupiedTargetAnchorId, candidates.Select(AnchorId));
        Assert.DoesNotContain(OriginalSourceAnchorId, candidates.Select(AnchorId));
        Assert.DoesNotContain(AlternativeSourceAnchorId, candidates.Select(AnchorId));
        Assert.All(candidates, candidate => Assert.Equal(
            ConnectorAnchorRole.Target.ToString(),
            candidate.Metadata[Canvas2DConnectorAnchorMetadata.Role].TextValue));

        var original = Assert.Single(
            candidates,
            candidate => AnchorId(candidate) == OriginalTargetAnchorId);
        Assert.Equal(
            original.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, Center(original))?.SceneObjectId);
        Assert.Single(EndpointHandles(scene, Canvas2DConnectorEndpointMetadata.StartEndpointRole));
        Assert.Empty(EndpointHandles(scene, Canvas2DConnectorEndpointMetadata.EndEndpointRole));
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    internal void PreviewReplacesOnlyMovingDisplayedEndpointAndIsNonHittable(
        ConnectorEndpointKind endpointKind)
    {
        var source = CreateSource();
        var current = endpointKind == ConnectorEndpointKind.Source
            ? new PointD(75d, 95d)
            : new PointD(285d, 95d);
        var gesture = Gesture(source, endpointKind, current);
        var scene = Scene(source, gesture);
        var connectorId = ConnectorSceneObjectId(source);
        var persistentConnector = Assert.Single(scene.Items, item => item.Id == connectorId);
        var preview = Assert.Single(scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                $"connector-endpoint-reconnection-preview:{gesture.Id}:",
                StringComparison.Ordinal) == true);
        var displayedPath = persistentConnector.Geometry.Points
            .Select(persistentConnector.Transform.TransformPoint)
            .ToArray();
        var expected = displayedPath.ToArray();
        expected[endpointKind == ConnectorEndpointKind.Source ? 0 : ^1] = current;

        Assert.Equal(Canvas2DSceneLayer.Overlay, preview.Layer);
        Assert.Equal(
            Canvas2DConnectorEndpointReconnectionGestureMetadata.PreviewZIndex,
            preview.ZIndex);
        Assert.Equal(expected, preview.Geometry.Points);
        Assert.Equal(Matrix2D.Identity, preview.Transform);
        Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);
        Assert.Equal(displayedPath, persistentConnector.Geometry.Points);
        Assert.Equal(source.Graph.Edges[0].Source.SemanticElementId, preview.Origin.SemanticElementId);
        Assert.Equal(source.Graph.Edges[0].Source.VisualStateId, preview.Origin.VisualStateId);
        Assert.NotEqual(
            preview.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, current)?.SceneObjectId);
    }

    [Fact]
    internal void BridgedPreviewAndEndpointHandleUseLogicalCenterlineInsteadOfJumpSamples()
    {
        var source = CreateSource().WithCrossingConnector();
        var current = new PointD(285d, 95d);
        var gesture = Gesture(source, ConnectorEndpointKind.Target, current);
        var scene = Scene(source, gesture);
        var connectorId = ConnectorSceneObjectId(source);
        var connector = scene.Items.Single(item => item.Id == connectorId);
        var logicalPath = Canvas2DConnectorPathMetadata.Resolve(connector);
        var preview = Assert.Single(scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                $"connector-endpoint-reconnection-preview:{gesture.Id}:",
                StringComparison.Ordinal) == true);
        var expected = logicalPath.ToArray();
        expected[^1] = current;

        Assert.True(connector.Geometry.Points.Length > logicalPath.Length);
        Assert.Equal(expected, preview.Geometry.Points);
        Assert.DoesNotContain(new PointD(180d, 49d), preview.Geometry.Points);
        var startHandle = Assert.Single(EndpointHandles(
            scene,
            Canvas2DConnectorEndpointMetadata.StartEndpointRole));
        Assert.Equal(logicalPath[0], Center(startHandle));
        Assert.Empty(EndpointHandles(
            scene,
            Canvas2DConnectorEndpointMetadata.EndEndpointRole));
    }

    [Fact]
    internal void TargetPreviewMovesArrowToTransientTargetAndSuppressesPersistentArrow()
    {
        var source = CreateSource();
        var current = new PointD(285d, 95d);
        var scene = Scene(
            source,
            Gesture(source, ConnectorEndpointKind.Target, current));
        var targetArrows = scene.Items.Where(item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorArrowMetadata.TargetArrow,
                out var targetArrow) &&
            targetArrow.Kind == PropertyValueKind.Boolean &&
            targetArrow.BooleanValue).ToArray();

        var previewArrow = Assert.Single(targetArrows);
        Assert.Equal(Canvas2DSceneLayer.Overlay, previewArrow.Layer);
        Assert.Equal(
            Canvas2DConnectorEndpointReconnectionGestureMetadata.PreviewZIndex + 1,
            previewArrow.ZIndex);
        Assert.Equal(current, previewArrow.Geometry.Points[0]);
        Assert.Equal(Canvas2DHitTestMode.None, previewArrow.HitTestPolicy.Mode);
    }

    private static Canvas2DSceneTestData CreateSource()
    {
        var source = Canvas2DSceneTestData.Create();
        var sourceNode = source.Graph.Nodes[0];
        var targetNode = source.Graph.Nodes[1];
        var sourceVisual = Visual(source, sourceNode.Source.VisualStateId!);
        var targetVisual = Visual(source, targetNode.Source.VisualStateId!);
        var connectorVisualId = source.Graph.Edges[0].Source.VisualStateId!;
        var sourceAnchors = new[]
        {
            Anchor(OriginalSourceAnchorId, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source),
            Anchor(
                AlternativeSourceAnchorId,
                ConnectorAnchorSide.Top,
                ConnectorAnchorRole.Source),
            Anchor(
                OccupiedSourceAnchorId,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Source),
        };
        var targetAnchors = new[]
        {
            Anchor(OriginalTargetAnchorId, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target),
            Anchor(
                AlternativeTargetAnchorId,
                ConnectorAnchorSide.Bottom,
                ConnectorAnchorRole.Target),
            Anchor(
                OccupiedTargetAnchorId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Target),
        };
        var ports = sourceAnchors.Select(anchor => Port(sourceNode, sourceVisual, anchor))
            .Concat(targetAnchors.Select(anchor => Port(targetNode, targetVisual, anchor)))
            .Append(Port(
                sourceNode,
                sourceVisual,
                new ProjectedConnectorAnchor(
                    PhantomSourceAnchorId,
                    ConnectorAnchorSide.Bottom,
                    ConnectorAnchorRoleCapability.Source,
                    0,
                    1,
                    ResolvedConnectorAnchorKind.Dynamic)));
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports.Concat(ports),
            source.Graph.Labels);
        var occupiedConnector = new VisualStateSnapshot(
            new VisualStateId("visual:occupied-connector"),
            new SemanticElementId("semantic:occupied-relationship"),
            default,
            default,
            VisualPlacementMode.Automatic,
            sourceAnchorId: OccupiedSourceAnchorId,
            targetAnchorId: OccupiedTargetAnchorId);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Select(visual =>
                visual.Id == sourceVisual.Id
                    ? Copy(visual, sourceAnchors)
                    : visual.Id == targetVisual.Id
                        ? Copy(visual, targetAnchors)
                        : visual.Id == connectorVisualId
                            ? CopyConnector(visual)
                            : visual)
                .Append(occupiedConnector));
        return source
            .WithGraph(graph)
            .WithVisualModel(visualModel);
    }

    private static ConnectorAnchor Anchor(
        ConnectorAnchorId id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role) =>
        new(id, side, role, 0);

    private static ProjectedPort Port(
        ProjectedNode owner,
        VisualStateSnapshot visual,
        ConnectorAnchor anchor) =>
        Port(
            owner,
            visual,
            new ProjectedConnectorAnchor(
                anchor.Id,
                anchor.Side,
                anchor.Role == ConnectorAnchorRole.Source
                    ? ConnectorAnchorRoleCapability.Source
                    : ConnectorAnchorRoleCapability.Target,
                anchor.Order,
                1,
                ResolvedConnectorAnchorKind.Dynamic));

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

    private static VisualStateSnapshot CopyConnector(VisualStateSnapshot visual) =>
        new(
            visual.Id,
            visual.SemanticElementId,
            visual.Position,
            visual.Size,
            visual.PlacementMode,
            visual.Route,
            visual.Properties,
            visual.ConnectorAnchors,
            OriginalSourceAnchorId,
            OriginalTargetAnchorId);

    private static EditorGestureSnapshot Gesture(
        Canvas2DSceneTestData source,
        ConnectorEndpointKind endpointKind,
        PointD current)
    {
        var edge = source.Graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == new SemanticElementId("test:semantic:ab"));
        var route = source.Routing.Routes.Single(candidate =>
            candidate.ProjectedEdgeId == edge.Id);
        var isSource = endpointKind == ConnectorEndpointKind.Source;
        var endpointNodeId = isSource ? edge.SourceNodeId : edge.TargetNodeId;
        var endpointNode = source.Graph.Nodes.Single(candidate => candidate.Id == endpointNodeId);
        return new EditorGestureSnapshot(
            $"connector-endpoint-reconnection:{endpointKind}",
            Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind,
            isSource ? route.SourceAnchor : route.DestinationAnchor,
            current,
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
                edge.Source.SemanticElementId,
                edge.Source.VisualStateId!,
                endpointKind,
                endpointNode.Source.SemanticElementId,
                isSource ? OriginalSourceAnchorId : OriginalTargetAnchorId,
                new ConnectorEndpointReconnectionId("reconnect:test"),
                ConnectorSceneObjectId(source)));
    }

    private static SceneObjectId ConnectorSceneObjectId(Canvas2DSceneTestData source) =>
        Canvas2DSceneObjectIdentity.ForProjected(
            source.Graph.Edges.Single(candidate =>
                candidate.Source.SemanticElementId ==
                    new SemanticElementId("test:semantic:ab")).Id,
            "connector");

    private static Canvas2DScene Scene(
        Canvas2DSceneTestData source,
        EditorGestureSnapshot gesture) =>
        Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            source.Graph,
            source.Layout,
            source.Routing,
            source.VisualModel,
            new EditorStateSnapshot(
                selection: [source.Graph.Edges[0].Source.VisualStateId!],
                activeGesture: gesture)).Scene);

    private static VisualStateSnapshot Visual(
        Canvas2DSceneTestData source,
        VisualStateId id) =>
        source.VisualModel.VisualStates.Single(visual => visual.Id == id);

    private static Canvas2DSceneItem[] CandidateHandles(Canvas2DScene scene) =>
        scene.Items.Where(item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorAnchorMetadata.ConnectionTargetCandidate,
                out var candidate) &&
            candidate.Kind == PropertyValueKind.Boolean &&
            candidate.BooleanValue).ToArray();

    private static Canvas2DSceneItem[] EndpointHandles(
        Canvas2DScene scene,
        string role) =>
        scene.Items.Where(item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorEndpointMetadata.HandleRole,
                out var handleRole) &&
            handleRole.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(handleRole.TextValue, role)).ToArray();

    private static ConnectorAnchorId AnchorId(Canvas2DSceneItem handle) =>
        new(handle.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue);

    private static PointD Center(Canvas2DSceneItem item) => new(
        item.Bounds.X + (item.Bounds.Width / 2d),
        item.Bounds.Y + (item.Bounds.Height / 2d));
}
