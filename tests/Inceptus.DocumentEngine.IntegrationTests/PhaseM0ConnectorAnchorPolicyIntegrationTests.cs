using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

using static Inceptus.DocumentEngine.IntegrationTests.EditingSessionTestSynchronization;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM0ConnectorAnchorPolicyIntegrationTests
{
    private static readonly VisualStateId AlphaId = new("demo:visual:alpha");
    private static readonly VisualStateId AlphaBetaId = new("demo:visual:alpha-beta");

    [Fact]
    public async Task MixedPerEdgePolicyControlsInteractionCommandsAndResolvedScene()
    {
        var leftDefinitions = new[]
        {
            Definition("left-source", ConnectorAnchorSide.Left,
                ConnectorAnchorRoleCapability.Source, 0),
            Definition("left-both", ConnectorAnchorSide.Left,
                ConnectorAnchorRoleCapability.SourceOrTarget, 1),
            Definition("left-target", ConnectorAnchorSide.Left,
                ConnectorAnchorRoleCapability.Target, 2),
        };
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited(
                ConnectorAnchorRoleCapability.Source),
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.Predefined(leftDefinitions));
        var provider = Provider(policy);
        await using var harness = await Harness.CreateAsync(provider);

        var initial = harness.Session.CaptureState();
        var initialDocument = CaptureDocument(harness.Session);
        var initialCounters = Counters(harness.Composition);
        var initialScene = CurrentScene(harness.Session);
        Assert.Empty(Visual(initialDocument, AlphaId).ConnectorAnchors);

        var top = await ContextAsync(
            harness,
            ResizeEdge(initialScene, AlphaId, "north"));
        Assert.Null(top.ConnectorAnchorContextAction);

        var right = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            (await ContextAsync(
                harness,
                ResizeEdge(CurrentScene(harness.Session), AlphaId, "east")))
            .ConnectorAnchorContextAction);
        Assert.True(right.CanAdd(ConnectorAnchorRole.Source));
        Assert.False(right.CanAdd(ConnectorAnchorRole.Target));

        var bottom = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            (await ContextAsync(
                harness,
                ResizeEdge(CurrentScene(harness.Session), AlphaId, "south")))
            .ConnectorAnchorContextAction);
        Assert.True(bottom.CanAdd(ConnectorAnchorRole.Source));
        Assert.True(bottom.CanAdd(ConnectorAnchorRole.Target));

        var addResult = await ExecuteAsync(
            harness,
            new AddConnectorAnchorCommand(
                initial.DocumentId,
                initial.DocumentRevision,
                AlphaId,
                new ConnectorAnchorId("test:m0:mixed:bottom"),
                ConnectorAnchorSide.Bottom,
                ConnectorAnchorRole.Source,
                bottom.InsertionIndex));
        Assert.True(addResult.IsCommitted);

        var committed = harness.Session.CaptureState();
        var committedDocument = CaptureDocument(harness.Session);
        var committedCounters = Counters(harness.Composition);
        var added = Assert.Single(Visual(committedDocument, AlphaId).ConnectorAnchors);
        Assert.Equal(ConnectorAnchorSide.Bottom, added.Side);
        Assert.Equal(initial.DocumentRevision.Increment(), committed.DocumentRevision);
        Assert.Equal(1, committed.HistoryStatus.EntryCount);
        Assert.Equal(1, harness.Events.Count);
        Assert.Equal(initialCounters.Layout, committedCounters.Layout);
        Assert.Equal(initialCounters.Routing + 1, committedCounters.Routing);
        Assert.Equal(initialCounters.Scene + 1, committedCounters.Scene);
        AssertSemanticContentUnchanged(initialDocument, committedDocument);

        var scene = CurrentScene(harness.Session);
        var bottomEdge = ResizeEdge(scene, AlphaId, "south");
        var occupiedPoint = new PointD(
            bottomEdge.Bounds.Left + (bottomEdge.Bounds.Width * 0.25d),
            bottomEdge.Bounds.Top + (bottomEdge.Bounds.Height / 2d));
        var occupied = await ContextAsync(harness, bottomEdge, occupiedPoint);
        Assert.Null(occupied.ConnectorAnchorContextAction);

        var rejectedState = harness.Session.CaptureState();
        var rejectedEvents = harness.Events.Count;
        var rejected = await ExecuteAsync(
            harness,
            new AddConnectorAnchorCommand(
                rejectedState.DocumentId,
                rejectedState.DocumentRevision,
                AlphaId,
                new ConnectorAnchorId("test:m0:mixed:second-bottom"),
                ConnectorAnchorSide.Bottom,
                ConnectorAnchorRole.Target,
                1));
        Assert.False(rejected.IsCommitted);
        Assert.Equal(rejectedState.DocumentRevision,
            harness.Session.CaptureState().DocumentRevision);
        Assert.Equal(rejectedState.HistoryStatus,
            harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(rejectedEvents, harness.Events.Count);

        scene = CurrentScene(harness.Session);
        var predefined = AnchorHandles(scene, AlphaId)
            .Where(item => MetadataInteger(
                item,
                Canvas2DConnectorAnchorMetadata.AnchorKind) ==
                (long)ResolvedConnectorAnchorKind.Predefined)
            .OrderBy(item => Center(item.Bounds).Y)
            .ToArray();
        Assert.Equal(3, predefined.Length);
        Assert.All(predefined, item => Assert.False(MetadataBoolean(
            item,
            Canvas2DConnectorAnchorMetadata.DeleteCapable)));
        var node = Node(scene, AlphaId);
        Assert.Equal(node.Bounds.Top + (node.Bounds.Height * 0.25d),
            Center(predefined[0].Bounds).Y, precision: 10);
        Assert.Equal(node.Bounds.Top + (node.Bounds.Height * 0.50d),
            Center(predefined[1].Bounds).Y, precision: 10);
        Assert.Equal(node.Bounds.Top + (node.Bounds.Height * 0.75d),
            Center(predefined[2].Bounds).Y, precision: 10);
        Assert.All(predefined, item => Assert.Equal(
            node.Bounds.Left,
            Center(item.Bounds).X,
            precision: 10));

        var predefinedContext = await ContextAsync(harness, predefined[1]);
        Assert.Null(predefinedContext.ConnectorAnchorContextAction);
        Assert.Equal(AlphaId, predefinedContext.TargetOrigin?.VisualStateId);
        Assert.Single(Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors);
    }

    [Fact]
    public async Task DynamicUnlimitedUsesOneSharedOrderAndUndoRedoOneOperationAtATime()
    {
        var provider = Provider(ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges);
        await using var harness = await Harness.CreateAsync(provider);
        var semanticBefore = CaptureDocument(harness.Session).SemanticModel;
        var expectedOrder = new[]
        {
            new ConnectorAnchorId("test:m0:unlimited:first"),
            new ConnectorAnchorId("test:m0:unlimited:third"),
            new ConnectorAnchorId("test:m0:unlimited:second"),
        };

        await AddAsync(harness, expectedOrder[0], ConnectorAnchorRole.Source, 0);
        await AddAsync(harness, expectedOrder[2], ConnectorAnchorRole.Target, 1);
        await AddAsync(harness, expectedOrder[1], ConnectorAnchorRole.Source, 1);

        var afterAdds = harness.Session.CaptureState();
        var anchors = Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors
            .Where(anchor => anchor.Side == ConnectorAnchorSide.Right)
            .OrderBy(static anchor => anchor.Order)
            .ToArray();
        Assert.Equal(expectedOrder, anchors.Select(static anchor => anchor.Id));
        Assert.Equal(
            [ConnectorAnchorRole.Source, ConnectorAnchorRole.Source, ConnectorAnchorRole.Target],
            anchors.Select(static anchor => anchor.Role));
        Assert.Equal(new DocumentRevision(3), afterAdds.DocumentRevision);
        Assert.Equal(3, afterAdds.HistoryStatus.EntryCount);
        Assert.Equal(3, harness.Events.Count);

        var scene = CurrentScene(harness.Session);
        var node = Node(scene, AlphaId);
        var rightHandles = AnchorHandles(scene, AlphaId)
            .Where(item => MetadataText(
                item,
                Canvas2DConnectorAnchorMetadata.Side) == ConnectorAnchorSide.Right.ToString())
            .OrderBy(item => Center(item.Bounds).Y)
            .ToArray();
        Assert.Equal(3, rightHandles.Length);
        for (var index = 0; index < rightHandles.Length; index++)
        {
            Assert.Equal(
                node.Bounds.Top +
                (node.Bounds.Height * ((index + 1d) / (rightHandles.Length + 1d))),
                Center(rightHandles[index].Bounds).Y,
                precision: 10);
        }

        var removeState = harness.Session.CaptureState();
        var removed = await ExecuteAsync(
            harness,
            new RemoveConnectorAnchorCommand(
                removeState.DocumentId,
                removeState.DocumentRevision,
                AlphaId,
                expectedOrder[1]));
        Assert.True(removed.IsCommitted);
        Assert.DoesNotContain(
            Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors,
            anchor => anchor.Id == expectedOrder[1]);

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(harness.Document, harness.Session);
        Assert.Contains(
            Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors,
            anchor => anchor.Id == expectedOrder[1]);

        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(harness.Document, harness.Session);
        Assert.DoesNotContain(
            Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors,
            anchor => anchor.Id == expectedOrder[1]);
        var finalSemantic = CaptureDocument(harness.Session).SemanticModel;
        AssertSemanticContentUnchanged(semanticBefore, finalSemantic);
    }

    [Fact]
    public async Task DynamicSingleAppliesOneTotalAnchorPerEdgeAcrossRoles()
    {
        var single = EdgeConnectorAnchorPolicy.DynamicSingle();
        var provider = Provider(new ElementConnectorAnchorPolicy(
            single,
            single,
            single,
            single));
        await using var harness = await Harness.CreateAsync(provider);

        foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
        {
            var state = harness.Session.CaptureState();
            var accepted = await ExecuteAsync(
                harness,
                new AddConnectorAnchorCommand(
                    state.DocumentId,
                    state.DocumentRevision,
                    AlphaId,
                    new ConnectorAnchorId($"test:m0:single:{side}"),
                    side,
                    ConnectorAnchorRole.Source,
                    0));
            Assert.True(accepted.IsCommitted);
        }

        var afterFirst = harness.Session.CaptureState();
        Assert.Equal(new DocumentRevision(4), afterFirst.DocumentRevision);
        Assert.Equal(4, afterFirst.HistoryStatus.EntryCount);
        Assert.Equal(4, harness.Events.Count);
        var anchors = Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors;
        Assert.Equal(4, anchors.Length);
        Assert.All(anchors, anchor => Assert.Equal(0, anchor.Order));

        var scene = CurrentScene(harness.Session);
        var nodeCenter = Center(Node(scene, AlphaId).Bounds);
        foreach (var handle in AnchorHandles(scene, AlphaId))
        {
            var side = Enum.Parse<ConnectorAnchorSide>(MetadataText(
                handle,
                Canvas2DConnectorAnchorMetadata.Side));
            var point = Center(handle.Bounds);
            if (side is ConnectorAnchorSide.Top or ConnectorAnchorSide.Bottom)
            {
                Assert.Equal(nodeCenter.X, point.X, precision: 10);
            }
            else
            {
                Assert.Equal(nodeCenter.Y, point.Y, precision: 10);
            }
        }

        foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
        {
            var state = harness.Session.CaptureState();
            var rejected = await ExecuteAsync(
                harness,
                new AddConnectorAnchorCommand(
                    state.DocumentId,
                    state.DocumentRevision,
                    AlphaId,
                    new ConnectorAnchorId($"test:m0:single:rejected:{side}"),
                    side,
                    ConnectorAnchorRole.Target,
                    1));
            Assert.False(rejected.IsCommitted);
        }

        Assert.Equal(afterFirst.DocumentRevision,
            harness.Session.CaptureState().DocumentRevision);
        Assert.Equal(afterFirst.HistoryStatus,
            harness.Session.CaptureState().HistoryStatus);
        Assert.Equal(4, harness.Events.Count);
    }

    [Fact]
    public async Task PredefinedReferencesRouteAndArrowThroughStableTypeDefinitionsAfterMove()
    {
        var right = Definition(
            "right-middle",
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            0);
        var left = Definition(
            "left-middle",
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target,
            0);
        var provider = Provider(new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([right]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([left])));
        await using var harness = await Harness.CreateAsync(
            provider,
            composition => CreateReferencedDocument(composition, provider, right, left));

        var initial = harness.Session.CaptureState();
        var initialRoute = Route(initial, AlphaBetaId);
        Assert.Equal(new PointD(210d, 105d), initialRoute.SourceAnchor);
        Assert.Equal(new PointD(300d, 230d), initialRoute.DestinationAnchor);
        Assert.Equal(initialRoute.DestinationAnchor, TargetArrowTip(initial, AlphaBetaId));
        Assert.Empty(Visual(CaptureDocument(harness.Session), AlphaId).ConnectorAnchors);

        var sourceReference = ConnectorAnchorReferenceIdentity.ForPredefined(AlphaId, right.Id);
        var beforeMove = harness.Session.CaptureState();
        var move = await ExecuteAsync(
            harness,
            new MoveVisualStateCommand(
                beforeMove.DocumentId,
                beforeMove.DocumentRevision,
                AlphaId,
                new PointD(80d, 90d)));
        Assert.True(move.IsCommitted);

        var moved = harness.Session.CaptureState();
        var movedRoute = Route(moved, AlphaBetaId);
        var movedDocument = CaptureDocument(harness.Session);
        Assert.Equal(new PointD(230d, 125d), movedRoute.SourceAnchor);
        Assert.Equal(new PointD(300d, 230d), movedRoute.DestinationAnchor);
        Assert.Equal(movedRoute.DestinationAnchor, TargetArrowTip(moved, AlphaBetaId));
        Assert.Equal(
            sourceReference,
            Visual(movedDocument, AlphaBetaId).SourceAnchorId);
        Assert.Empty(Visual(movedDocument, AlphaId).ConnectorAnchors);
        Assert.Equal(1, moved.HistoryStatus.EntryCount);
        Assert.Equal(1, harness.Events.Count);
    }

    private static async ValueTask AddAsync(
        Harness harness,
        ConnectorAnchorId anchorId,
        ConnectorAnchorRole role,
        int insertionIndex)
    {
        var state = harness.Session.CaptureState();
        var result = await ExecuteAsync(
            harness,
            new AddConnectorAnchorCommand(
                state.DocumentId,
                state.DocumentRevision,
                AlphaId,
                anchorId,
                ConnectorAnchorSide.Right,
                role,
                insertionIndex));
        Assert.True(result.IsCommitted);
    }

    private static async ValueTask<Inceptus.DocumentEngine.Contracts.History.HistoryOperationResult>
        ExecuteAsync(Harness harness, ICommand command)
    {
        var result = await harness.Session.ExecuteForSelectedVisualStateAsync(
            AlphaId,
            command);
        if (result.IsCommitted)
        {
            await WaitForCommittedEventAndSessionIdleAsync(harness.Document, harness.Session);
        }

        return result;
    }

    private static async ValueTask<Canvas2DInteractionResult> ContextAsync(
        Harness harness,
        Canvas2DSceneItem item,
        PointD? documentPoint = null)
    {
        var scene = CurrentScene(harness.Session);
        var point = documentPoint ?? Center(item.Bounds);
        return await harness.Interaction.PointerContextMenuAsync(
            scene.ViewportTransform.TransformPoint(point));
    }

    private static ElementConnectorAnchorPolicyRegistry Provider(
        ElementConnectorAnchorPolicy policy) =>
        new(
        [
            new ElementConnectorAnchorPolicyRegistration(
                NeutralDemoPipeline.NeutralNodeTypeId,
                policy),
        ]);

    private static PredefinedConnectorAnchorDefinition Definition(
        string id,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roles,
        int order) =>
        new(new PredefinedConnectorAnchorDefinitionId(id), side, roles, order);

    private static Document CreateReferencedDocument(
        NeutralDemoComposition composition,
        IElementConnectorAnchorPolicyProvider provider,
        PredefinedConnectorAnchorDefinition sourceDefinition,
        PredefinedConnectorAnchorDefinition targetDefinition)
    {
        var snapshot = composition.Document.CaptureSnapshot();
        var sourceReference = ConnectorAnchorReferenceIdentity.ForPredefined(
            AlphaId,
            sourceDefinition.Id);
        var targetVisualId = new VisualStateId("demo:visual:beta");
        var targetReference = ConnectorAnchorReferenceIdentity.ForPredefined(
            targetVisualId,
            targetDefinition.Id);
        var visuals = snapshot.VisualModel.VisualStates.Select(visual =>
            visual.Id == AlphaBetaId
                ? new VisualStateSnapshot(
                    visual.Id,
                    visual.SemanticElementId,
                    visual.Position,
                    visual.Size,
                    visual.PlacementMode,
                    visual.Route,
                    visual.Properties,
                    visual.ConnectorAnchors,
                    sourceReference,
                    targetReference)
                : visual);
        var referenced = new DocumentSnapshot(
            snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, visuals),
            snapshot.Metadata);
        var creation = DocumentFactory.Create(referenced, provider);
        Assert.True(creation.Succeeded);
        return Assert.IsType<Document>(creation.Document);
    }

    private static Inceptus.DocumentEngine.Contracts.Routing.RoutedConnectorGeometry Route(
        EditingSessionState state,
        VisualStateId connectorVisualStateId)
    {
        var edge = state.ProjectedGraph!.Edges.Single(candidate =>
            candidate.Source.VisualStateId == connectorVisualStateId);
        return state.RoutingResult!.Routes.Single(candidate =>
            candidate.ProjectedEdgeId == edge.Id);
    }

    private static PointD TargetArrowTip(
        EditingSessionState state,
        VisualStateId connectorVisualStateId)
    {
        var item = state.CurrentScene!.Items.Single(candidate =>
            candidate.Origin.VisualStateId == connectorVisualStateId &&
            candidate.Metadata.TryGetValue(
                Canvas2DConnectorArrowMetadata.TargetArrow,
                out var marker) &&
            marker.Kind == PropertyValueKind.Boolean &&
            marker.BooleanValue);
        return item.Geometry.Points[0];
    }

    private static Canvas2DScene CurrentScene(EditingSession session) =>
        Assert.IsType<Canvas2DScene>(session.CaptureState().CurrentScene);

    private static Canvas2DSceneItem Node(Canvas2DScene scene, VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.VisualStateId == visualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem ResizeEdge(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        string role) =>
        scene.Items.Single(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                $"resize-edge-zone:{role}:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem[] AnchorHandles(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Where(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out _))
        .ToArray();

    private static string MetadataText(Canvas2DSceneItem item, string key)
    {
        Assert.True(item.Metadata.TryGetValue(key, out var value));
        Assert.Equal(PropertyValueKind.Text, value.Kind);
        return value.TextValue;
    }

    private static long MetadataInteger(Canvas2DSceneItem item, string key)
    {
        Assert.True(item.Metadata.TryGetValue(key, out var value));
        Assert.Equal(PropertyValueKind.Integer, value.Kind);
        return value.IntegerValue;
    }

    private static bool MetadataBoolean(Canvas2DSceneItem item, string key)
    {
        Assert.True(item.Metadata.TryGetValue(key, out var value));
        Assert.Equal(PropertyValueKind.Boolean, value.Kind);
        return value.BooleanValue;
    }

    private static VisualStateSnapshot Visual(
        DocumentSnapshot document,
        VisualStateId visualStateId) =>
        document.VisualModel.VisualStates.Single(visual => visual.Id == visualStateId);

    private static void AssertSemanticContentUnchanged(
        DocumentSnapshot expected,
        DocumentSnapshot actual) =>
        AssertSemanticContentUnchanged(expected.SemanticModel, actual.SemanticModel);

    private static void AssertSemanticContentUnchanged(
        Inceptus.DocumentEngine.Contracts.Semantics.SemanticModelSnapshot expected,
        Inceptus.DocumentEngine.Contracts.Semantics.SemanticModelSnapshot actual)
    {
        Assert.Equal(expected.Elements.Length, actual.Elements.Length);
        foreach (var expectedElement in expected.Elements)
        {
            var actualElement = actual.Elements.Single(element => element.Id == expectedElement.Id);
            Assert.Equal(expectedElement.TypeId, actualElement.TypeId);
            Assert.Equal(expectedElement.Properties, actualElement.Properties);
        }

        Assert.Equal(expected.Relationships.Length, actual.Relationships.Length);
        foreach (var expectedRelationship in expected.Relationships)
        {
            var actualRelationship = actual.Relationships.Single(relationship =>
                relationship.Id == expectedRelationship.Id);
            Assert.Equal(expectedRelationship.TypeId, actualRelationship.TypeId);
            Assert.Equal(expectedRelationship.SourceId, actualRelationship.SourceId);
            Assert.Equal(expectedRelationship.TargetId, actualRelationship.TargetId);
            Assert.Equal(expectedRelationship.Properties, actualRelationship.Properties);
        }
    }

    private static DocumentSnapshot CaptureDocument(EditingSession session)
    {
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));
        return Assert.IsType<DocumentSnapshot>(document);
    }

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static CounterSnapshot Counters(NeutralDemoComposition composition) =>
        new(
            composition.Counters.LayoutInvocationCount,
            composition.Counters.RoutingInvocationCount,
            composition.Counters.SceneContributionInvocationCount);

    private sealed record CounterSnapshot(int Layout, int Routing, int Scene);

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            NeutralDemoComposition composition,
            Document document,
            EditingSession session,
            Canvas2DInteractionController interaction,
            RecordingSubscriber events)
        {
            Composition = composition;
            Document = document;
            Session = session;
            Interaction = interaction;
            Events = events;
        }

        internal NeutralDemoComposition Composition { get; }

        internal Document Document { get; }

        internal EditingSession Session { get; }

        internal Canvas2DInteractionController Interaction { get; }

        internal RecordingSubscriber Events { get; }

        internal static async ValueTask<Harness> CreateAsync(
            IElementConnectorAnchorPolicyProvider provider,
            Func<NeutralDemoComposition, Document>? documentFactory = null)
        {
            var composition = NeutralDemoPipeline.CreateComposition(provider);
            var events = new RecordingSubscriber();
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
                source.InitialEditorState,
                source.CommandHandlers,
                source.CommandValidators,
                source.HistoryPolicies,
                source.DocumentChangedSubscribers.Append(events),
                provider);
            var renderer = new Canvas2DRenderer(
                new RecordingRenderExecution(),
                RendererConfiguration());
            Assert.True((await renderer.InitializeAsync(
                "phase-m0-policy-canvas",
                new Canvas2DSurfaceSize(960d, 640d, 1.25d))).Succeeded);
            var document = documentFactory?.Invoke(composition) ?? composition.Document;
            var attachment = await EditingSession.AttachAsync(
                document,
                renderer,
                configuration);
            var session = Assert.IsType<EditingSession>(attachment.Session);
            Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
            return new Harness(
                composition,
                document,
                session,
                new Canvas2DInteractionController(session),
                events);
        }

        public async ValueTask DisposeAsync()
        {
            await Interaction.DisposeAsync();
            await Session.DisposeAsync();
        }
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        private readonly ConcurrentQueue<DocumentChangedEvent> _events = new();

        internal int Count => _events.Count;

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            _events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame) =>
            ValueTask.FromResult(Success());

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
}
