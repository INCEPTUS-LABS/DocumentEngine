using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class StableRoutingPreparationTests
{
    [Fact]
    public async Task SceneOnlyRevisionRebindPreservesExpandedRoutingSourceSpace()
    {
        var fixture = await Fixture.CreateAsync();
        var snapshot = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(snapshot.VisualModel.RoutingScopes!.Value);
        var graph = fixture.Configuration.ProjectionEngine.Project(snapshot, scope.ScopeId,
            fixture.Configuration.ProjectionContext, CancellationToken.None).Graph!;
        var local = ConnectorRoutingStatePreparer.RestoreLocalLayout(graph, scope.Geometry);
        var routing = ConnectorRoutingStatePreparer.RestoreRouting(graph, local, scope,
            fixture.Configuration.RoutingAlgorithmId);
        var artifacts = new EditingSessionPipelineArtifacts(scope.ScopeId, graph, local, routing);

        var rebound = artifacts.RebindToCommittedRevision(snapshot.DocumentId, snapshot.Revision,
            snapshot.Revision.Increment());

        Assert.NotNull(rebound.RoutingResult.LogicalGeometry);
        Assert.Equal(scope.ScopeId, rebound.RoutingResult.LogicalGeometry.ScopeId);
        Assert.Same(scope.Geometry, rebound.RoutingResult.LogicalGeometry.Geometry);
        Assert.Equal(snapshot.Revision.Increment(), rebound.RoutingResult.LogicalGeometry.Layout.SourceRevision);
        Assert.Same(routing.LogicalGeometry!.Layout.Computation, rebound.RoutingResult.LogicalGeometry.Layout.Computation);
        Assert.Same(routing.Computation, rebound.RoutingResult.Computation);
    }

    [Fact]
    public async Task CompatibleV2ColdPreparationPreservesEveryPointAndOrderWithoutSearch()
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Document.CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(before);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory(), fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(static item => item.Message)));
        fixture.Policy.Searches.Clear();
        var result = await fixture.Preparer.PrepareAsync(new(before, imported.Document!.CaptureSnapshot(),
            [], [], null, false, ConnectorRoutingPreparationPurpose.ValidateSavedState), CancellationToken.None);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
        Assert.Empty(fixture.Policy.Searches);
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.AsEnumerable(), result.RoutingScopes.AsEnumerable());
        Assert.Equal(bytes.AsEnumerable(), NativeDocumentSerializer.Export(imported.Document).AsEnumerable());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertionSearchesOnlyActuallyObstructedAutomaticRoutes(bool blocks)
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Document.CaptureSnapshot();
        fixture.Policy.Searches.Clear();
        var result = await fixture.Processor.ExecuteAsync(fixture.Document, new CreateBpmnTaskCommand(
            before.DocumentId, before.Revision, new SemanticElementId("inserted"), new VisualStateId("v:inserted"),
            new PointD(260, blocks ? 30 : 400), new SizeD(120, 80), "INSERTED", "Inserted", 100, VisualPlacementMode.Pinned));
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
        var after = fixture.Document.CaptureSnapshot();
        var previous = Assert.Single(before.VisualModel.RoutingScopes!.Value).Connectors;
        var current = Assert.Single(after.VisualModel.RoutingScopes!.Value).Connectors;
        Assert.Equal(blocks ? 1 : 0, fixture.Policy.Searches.Count);
        if (blocks)
        {
            Assert.Equal(new VisualStateId("v:flow-a"), fixture.Policy.Searches.Single());
            Assert.Equal([new VisualStateId("v:flow-b"), new VisualStateId("v:flow-a")], current.Select(static record => record.VisualStateId));
            Assert.Equal(previous[1], current[0]);
            Assert.NotEqual(previous[0].Path.AsEnumerable(), current[1].Path.AsEnumerable());
        }
        else Assert.Equal(previous.AsEnumerable(), current.AsEnumerable());
        Assert.Equal(before.Revision.Value + 1, after.Revision.Value);
    }

    [Fact]
    public async Task ExternalManualPointsSurviveTypeHistoryWhichOwnsOnlyMode()
    {
        var fixture = await Fixture.CreateAsync();
        var history = new HistoryManager(fixture.Document);
        var id = new VisualStateId("v:flow-a");
        var setType = await history.ExecuteAsync(fixture.Processor, new SetConnectorRoutingTypeCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, id, ConnectorRoutingType.Manual));
        Assert.True(setType.Succeeded);
        var scope = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        var record = scope.Connectors.Single(item => item.VisualStateId == id);
        PointD[] changedPath = [record.Path[0], new PointD(270, 150), record.Path[^1]];
        // A core command outside the session has no History entry. Mode replay must not own it.
        var manual = await fixture.Processor.ExecuteAsync(fixture.Document, new UpdateConnectionRouteCommand(
            fixture.Document.DocumentId, fixture.Document.Revision, id, changedPath));
        Assert.True(manual.IsCommitted);
        Assert.True((await history.UndoAsync(fixture.Processor)).Succeeded);
        var automatic = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value)
            .Connectors.Single(item => item.VisualStateId == id);
        Assert.Equal(ConnectorRoutingType.Automatic, automatic.RoutingType);
        Assert.Equal([new PointD(270, 150)], automatic.ManualDefinition!.Value.AsEnumerable());
        Assert.True((await history.RedoAsync(fixture.Processor)).Succeeded);
        var restored = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value)
            .Connectors.Single(item => item.VisualStateId == id);
        Assert.Equal(changedPath, restored.Path.AsEnumerable());
    }

    internal sealed record Fixture(Document Document, EditingSessionConfiguration Configuration,
        ConnectorRoutingStatePreparer Preparer, CommandProcessor Processor, CountingPolicy Policy)
    {
        private static readonly string[] FlowNames = ["a", "b"];
        internal static async Task<Fixture> CreateAsync(VisualPlacementMode nodePlacementMode = VisualPlacementMode.Pinned,
            Func<DocumentSnapshot, DocumentSnapshot>? transformSource = null)
        {
            var id = new DocumentId("stable:preparation");
            var revision = new DocumentRevision(0);
            var names = new[] { "a-source", "a-target", "b-source", "b-target" };
            var elements = names.Select((name, index) => BpmnSemanticFactory.CreateTask(new SemanticElementId(name), name, name, index + 1));
            var relationships = FlowNames.Select(name => new SemanticRelationshipSnapshot(
                new SemanticElementId($"flow-{name}"), BpmnSemanticTypes.SequenceFlow,
                new SemanticElementId($"{name}-source"), new SemanticElementId($"{name}-target")));
            var visuals = names.Select((name, index) => new VisualStateSnapshot(new VisualStateId($"v:{name}"),
                new SemanticElementId(name), new PointD(index % 2 == 0 ? 20 : 500, index < 2 ? 20 : 230),
                new SizeD(120, 80), nodePlacementMode, connectorAnchors:
                [new ConnectorAnchor(new ConnectorAnchorId($"{name}:source"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                    new ConnectorAnchor(new ConnectorAnchorId($"{name}:target"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]))
                .Concat(FlowNames.Select(name => new VisualStateSnapshot(new VisualStateId($"v:flow-{name}"),
                    new SemanticElementId($"flow-{name}"), default, default, VisualPlacementMode.Automatic,
                    sourceAnchorId: new ConnectorAnchorId($"{name}-source:source"), targetAnchorId: new ConnectorAnchorId($"{name}-target:target"))));
            var source = new DocumentSnapshot(new SemanticModelSnapshot(id, revision, elements, relationships),
                new VisualModelSnapshot(id, revision, visuals), new DocumentMetadataSnapshot(id, revision));
            if (transformSource is not null) source = transformSource(source);
            var constructed = DocumentReconstructor.Reconstruct(source);
            Assert.True(constructed.Succeeded, string.Join("; ", constructed.Diagnostics.Select(static item => item.Message)));
            var configuration = BpmnModelerComposition.Create(constructed.Document!).Configuration;
            var policy = new CountingPolicy();
            configuration = new EditingSessionConfiguration(configuration.ProjectionEngine, configuration.LayoutEngine,
                configuration.LayoutAlgorithmId, new RoutingEngine([new RoutingAlgorithmRegistration(BpmnAlgorithmIds.DefaultRouting, policy)]),
                configuration.RoutingAlgorithmId, configuration.SceneBuilder, configuration.ProjectionContext,
                configuration.LayoutContext, configuration.RoutingContext, configuration.InitialEditorState,
                configuration.CommandHandlers, configuration.CommandValidators, configuration.HistoryPolicies,
                configuration.DocumentChangedSubscribers, configuration.ConnectorAnchorPolicyProvider,
                configuration.ModelProfileCatalog, configuration.InitialModelProfileViewState, configuration.RoutingInputPreparer);
            var preparer = new ConnectorRoutingStatePreparer(configuration,
                new Canvas2DTextLayoutServiceTests.FixedAdvanceTextMetricsService(), CreateRequest);
            var prepared = await preparer.PrepareAsync(new(source, source, [], [], null, false,
                ConnectorRoutingPreparationPurpose.InitialConstruction), CancellationToken.None);
            Assert.True(prepared.Succeeded, string.Join("; ", prepared.Diagnostics.Select(static item => item.Message)));
            var saved = new DocumentSnapshot(source.SemanticModel, new VisualModelSnapshot(id, revision, source.VisualModel.VisualStates,
                source.VisualModel.ProfileElementPresentations, prepared.RoutingScopes), source.Metadata);
            var document = DocumentReconstructor.Reconstruct(saved, configuration.ConnectorAnchorPolicyProvider);
            Assert.True(document.Succeeded, string.Join("; ", document.Diagnostics.Select(static item => item.Message)));
            return new Fixture(document.Document!, configuration, preparer, new CommandProcessor(configuration.CommandHandlers,
                configuration.CommandValidators, configuration.DocumentChangedSubscribers, configuration.HistoryPolicies,
                configuration.ConnectorAnchorPolicyProvider, preparer), policy);
        }

        internal static TextMeasurementRequest CreateRequest(string text, Canvas2DSceneStyle style, double lineHeight) =>
            new(text, "Test Sans", "test:sans", "1", style.FontSize, lineHeight, 400, TextFontStyle.Normal,
                "und", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom, 1, "test.metrics", "1");
    }

    internal sealed class CountingPolicy : IRoutingAlgorithm, IStableConnectorRoutingPolicy
    {
        private readonly BpmnRoutingAlgorithm _inner = new();
        internal List<VisualStateId> Searches { get; } = [];
        public RoutingAlgorithmResult Route(ProjectedGraph graph, LayoutResult layout, RoutingContext context, CancellationToken cancellationToken) =>
            _inner.Route(graph, layout, context, cancellationToken);
        public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult layout, RoutingContext context,
            ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate, CancellationToken cancellationToken = default) =>
            _inner.Assess(graph, layout, context, edgeId, candidate, cancellationToken);
        public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult layout, RoutingContext context,
            ImmutableArray<ProjectedObjectId> orderedEdgeIds, CancellationToken cancellationToken = default)
        {
            Searches.AddRange(orderedEdgeIds.Select(id => graph.Edges.Single(edge => edge.Id == id).Source.VisualStateId!));
            return _inner.RouteAffected(graph, layout, context, orderedEdgeIds, cancellationToken);
        }
    }
}
