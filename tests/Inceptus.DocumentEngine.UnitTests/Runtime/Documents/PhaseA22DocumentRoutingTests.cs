using System.Text;
using System.Text.Json.Nodes;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class PhaseA22DocumentRoutingTests
{
    [Fact]
    public void FreshVisualModelHasNoSavedConnectorRecords()
    {
        var visual = new VisualModelSnapshot(new DocumentId("fresh-routing"), DocumentRevision.Zero);
        Assert.Null(visual.RoutingScopes);
        Assert.Empty(visual.EnumerateConnectorRoutingRecords());
    }

    [Fact]
    public void ProcessOnlyNativeBytesAndVersionRemainUnchangedWithEmptyOptionalCollection()
    {
        var sample = CreateSample(documentFlow: false);
        var source = sample.Document;
        var legacyContract = new DocumentSnapshot(source.SemanticModel,
            new VisualModelSnapshot(source.DocumentId, source.Revision, source.VisualModel.VisualStates,
                source.VisualModel.ProfileElementPresentations, source.VisualModel.RoutingScopes), source.Metadata);
        var bytes = NativeDocumentSerializer.Export(legacyContract);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(source).AsSpan()));
        var json = JsonNode.Parse(bytes.AsSpan())!;
        Assert.Equal(2, json["formatVersion"]!.GetValue<int>());
        Assert.False(json["document"]!["visualModel"]!.AsObject().ContainsKey("documentRoutingStates"));
        var reopened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(reopened.Succeeded, Diagnostics(reopened));
        Assert.Empty(reopened.Document!.CaptureSnapshot().VisualModel.DocumentRoutingStates);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(reopened.Document).AsSpan()));
    }

    [Fact]
    public void OptionalDocumentRoutesRoundTripWithoutDuplicatingProcessNodeGeometry()
    {
        var sample = CreateSample();
        var bytes = NativeDocumentSerializer.Export(sample.Document);
        var json = JsonNode.Parse(bytes.AsSpan())!;
        Assert.Equal(2, json["formatVersion"]!.GetValue<int>());
        var state = Assert.Single(json["document"]!["visualModel"]!["documentRoutingStates"]!.AsArray());
        Assert.Equal(["ownerId", "connectors"], state!.AsObject().Select(static property => property.Key));
        Assert.Equal("documentLogical", state["connectors"]![0]!["space"]!.GetValue<string>());
        var reopened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(reopened.Succeeded, Diagnostics(reopened));
        var actual = reopened.Document!.CaptureSnapshot();
        Assert.Equal(sample.Document, actual);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(actual).AsSpan()));
        Assert.Single(Assert.Single(actual.VisualModel.RoutingScopes!.Value).Connectors);
        Assert.Single(Assert.Single(actual.VisualModel.DocumentRoutingStates).Connectors);
        Assert.Equal(2, Assert.Single(actual.VisualModel.RoutingScopes.Value).Geometry.Nodes.Length);
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("scoped-owner")]
    [InlineData("missing-visual")]
    [InlineData("duplicate-owner")]
    [InlineData("duplicate-route")]
    [InlineData("wrong-space")]
    [InlineData("missing-route")]
    [InlineData("duplicate-process-route")]
    [InlineData("stolen-process-route")]
    [InlineData("stolen-document-route")]
    [InlineData("empty-state")]
    public void InvalidDocumentRoutingIsRejectedAtomically(string mutation)
    {
        var json = JsonNode.Parse(NativeDocumentSerializer.Export(CreateSample().Document).AsSpan())!;
        var visual = json["document"]!["visualModel"]!;
        var states = visual["documentRoutingStates"]!.AsArray();
        var state = states[0]!;
        var records = state["connectors"]!.AsArray();
        switch (mutation)
        {
            case "missing-owner": state["ownerId"] = "missing"; break;
            case "scoped-owner": state["ownerId"] = "source"; break;
            case "missing-visual": records[0]!["visualStateId"] = "missing"; break;
            case "duplicate-owner": states.Add(state.DeepClone()); break;
            case "duplicate-route": records.Add(records[0]!.DeepClone()); break;
            case "wrong-space": records[0]!["space"] = "scopeLogical"; break;
            case "missing-route": states.Clear(); break;
            case "empty-state":
                states.Add(new JsonObject { ["ownerId"] = "participant", ["connectors"] = new JsonArray() });
                break;
            case "stolen-document-route":
                var stolenDocument = records[0]!.DeepClone();
                stolenDocument["space"] = "scopeLogical";
                visual["routingScopes"]![0]!["connectors"]!.AsArray().Add(stolenDocument);
                states.Clear();
                break;
            case "stolen-process-route":
                var stolenProcess = visual["routingScopes"]![0]!["connectors"]![0]!.DeepClone();
                stolenProcess["space"] = "documentLogical";
                records.Add(stolenProcess);
                visual["routingScopes"]![0]!["connectors"]!.AsArray().Clear();
                break;
            case "duplicate-process-route":
                var copy = visual["routingScopes"]![0]!["connectors"]![0]!.DeepClone();
                copy["space"] = "documentLogical";
                records.Add(copy);
                break;
        }
        var result = NativeDocumentSerializer.Import(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void DocumentViewUsesActualParticipantBoundsAndTranslatedSavedProcessGeometryAndRoutes()
    {
        var sample = CreateSample();
        var originalBytes = NativeDocumentSerializer.Export(sample.Document);
        var layout = ConnectorRoutingStatePreparer.RestoreDocumentLayout(sample.Graph, sample.Document, sample.Plan);
        var routing = ConnectorRoutingStatePreparer.RestoreDocumentRouting(sample.Graph, layout, sample.Document,
            sample.Plan, Assert.Single(sample.Document.VisualModel.DocumentRoutingStates), new AlgorithmId("test-router"));
        var source = sample.Graph.Nodes.Single(node => node.Source.SemanticElementId.Value == "source");
        var participant = sample.Graph.Nodes.Single(node => node.Source.SemanticElementId.Value == "participant");
        Assert.Equal(new RectD(60, 220, 100, 50), layout.Nodes.Single(node => node.ProjectedObjectId == source.Id).Bounds);
        Assert.Equal(new RectD(600, 100, 300, 200), layout.Nodes.Single(node => node.ProjectedObjectId == participant.Id).Bounds);
        var processEdge = sample.Graph.Edges.Single(edge => edge.Source.SemanticElementId.Value == "process-flow");
        Assert.Equal(new PointD(160, 245), routing.Routes.Single(route => route.ProjectedEdgeId == processEdge.Id).SourceAnchor);
        var documentEdge = sample.Graph.Edges.Single(edge => edge.Source.SemanticElementId.Value == "document-flow");
        Assert.Equal(new PointD(600, 200), routing.Routes.Single(route => route.ProjectedEdgeId == documentEdge.Id).DestinationAnchor);
        Assert.True(originalBytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(sample.Document).AsSpan()));
    }

    [Fact]
    public void MissingParticipantVisualIsNotSynthesizedDuringDocumentLayoutRestoration()
    {
        var sample = CreateSample();
        var document = sample.Document;
        var visual = document.VisualModel;
        var incomplete = new DocumentSnapshot(document.SemanticModel,
            new VisualModelSnapshot(document.DocumentId, document.Revision,
                visual.VisualStates.Where(static item => item.SemanticElementId.Value != "participant"),
                visual.ProfileElementPresentations, visual.RoutingScopes, visual.DocumentRoutingStates), document.Metadata);
        Assert.Throws<InvalidOperationException>(() =>
            ConnectorRoutingStatePreparer.RestoreDocumentLayout(sample.Graph, incomplete, sample.Plan));
        Assert.False(incomplete.VisualModel.VisualStates.Any(static item => item.SemanticElementId.Value == "participant"));
    }

    [Fact]
    public void DocumentRoutingPriorityAndUniqueOwnerArePartOfValueEquality()
    {
        var sample = CreateSample();
        var state = Assert.Single(sample.Document.VisualModel.DocumentRoutingStates);
        var first = Assert.Single(state.Connectors);
        var second = new ConnectorRoutingRecord(new VisualStateId("other-visual"), ConnectorRoutingType.Straight,
            ConnectorRoutingOutcome.Path, [new PointD(1, 1), new PointD(2, 2)]);
        Assert.NotEqual(new DocumentRoutingSnapshot(state.OwnerId, [first, second]),
            new DocumentRoutingSnapshot(state.OwnerId, [second, first]));
        Assert.Throws<ArgumentException>(() => new DocumentRoutingSnapshot(state.OwnerId, [first, first]));
        var visual = sample.Document.VisualModel;
        Assert.Throws<ArgumentException>(() => new VisualModelSnapshot(visual.DocumentId, visual.Revision,
            visual.VisualStates, visual.ProfileElementPresentations, visual.RoutingScopes, [state, state]));
    }

    [Fact]
    public async Task RevisionCloneAndExistingGeometryAndLabelCommandsPreserveBothRoutingAuthorities()
    {
        var original = CreateSample().Document;
        var cloned = DocumentSnapshotCloner.CloneAtRevision(original, original.Revision.Increment());
        Assert.True(original.VisualModel.DocumentRoutingStates.AsSpan().SequenceEqual(cloned.VisualModel.DocumentRoutingStates.AsSpan()));
        Assert.True(original.VisualModel.RoutingScopes!.Value.AsSpan().SequenceEqual(cloned.VisualModel.RoutingScopes!.Value.AsSpan()));
        var moved = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(original.DocumentId, original.Revision, new VisualStateId("source-visual"), new PointD(20, 30)),
            original, CancellationToken.None);
        Assert.True(moved.Succeeded);
        Assert.True(original.VisualModel.DocumentRoutingStates.AsSpan().SequenceEqual(moved.ProposedDocument!.VisualModel.DocumentRoutingStates.AsSpan()));
        Assert.True(original.VisualModel.RoutingScopes!.Value.AsSpan().SequenceEqual(moved.ProposedDocument.VisualModel.RoutingScopes!.Value.AsSpan()));
        var labelled = await new MoveLabelCommandHandler().HandleAsync(
            new MoveLabelCommand(original.DocumentId, original.Revision, new VisualStateId("document-flow-visual"),
                new ConnectorLabelPlacement(0.4, new VectorD(5, 10))), original, CancellationToken.None);
        Assert.True(labelled.Succeeded);
        Assert.True(original.VisualModel.DocumentRoutingStates.AsSpan().SequenceEqual(labelled.ProposedDocument!.VisualModel.DocumentRoutingStates.AsSpan()));
        Assert.True(original.VisualModel.RoutingScopes!.Value.AsSpan().SequenceEqual(labelled.ProposedDocument.VisualModel.RoutingScopes!.Value.AsSpan()));
    }

    private static (DocumentSnapshot Document, ProjectedGraph Graph, PresentationViewPlan Plan) CreateSample(bool documentFlow = true)
    {
        var id = new DocumentId("a22-document-routing");
        var revision = new DocumentRevision(3);
        var type = new SemanticTypeId("test:node");
        var relationshipType = new SemanticTypeId("test:flow");
        var owner = new SemanticElementId("view-owner");
        var participant = new SemanticElementId("participant");
        var source = new SemanticElementId("source");
        var target = new SemanticElementId("target");
        var relationships = new List<SemanticRelationshipSnapshot>
        {
            new(new SemanticElementId("process-flow"), relationshipType, source, target),
        };
        if (documentFlow) relationships.Add(new(new SemanticElementId("document-flow"), relationshipType, source, participant));
        var semantic = new SemanticModelSnapshot(id, revision,
            [new(owner, type, containmentKind: SemanticElementContainmentKind.Document),
                new(participant, type, containmentKind: SemanticElementContainmentKind.Document), new(source, type), new(target, type)], relationships);
        var visuals = new List<VisualStateSnapshot>
        {
            new(new VisualStateId("participant-visual"), participant, new PointD(600, 100), new SizeD(300, 200), VisualPlacementMode.Pinned),
            new(new VisualStateId("source-visual"), source, new PointD(10, 20), new SizeD(100, 50), VisualPlacementMode.Pinned),
            new(new VisualStateId("target-visual"), target, new PointD(310, 20), new SizeD(100, 50), VisualPlacementMode.Pinned),
        };
        visuals.AddRange(relationships.Select(relationship => new VisualStateSnapshot(new VisualStateId(relationship.Id.Value + "-visual"),
            relationship.Id, new PointD(0, 0), new SizeD(0, 0), VisualPlacementMode.Automatic)));
        var text = new TextMeasurementRequest("", "Test", "test-font", "1", 12, 16, 400, TextFontStyle.Normal,
            "en", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom, 1, "test", "1");
        var geometry = new ScopeGeometrySnapshot("test", "1", new AlgorithmId("test-layout"), Canvas2DSceneConfiguration.Default,
            text, [], visuals.Where(visual => visual.SemanticElementId == source || visual.SemanticElementId == target)
                .Select(visual => new ScopeNodeGeometrySnapshot(visual.Id,
                    new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height),
                    Matrix2D.CreateTranslation(visual.Position.X, visual.Position.Y), null)), [], []);
        var processRecord = new ConnectorRoutingRecord(new VisualStateId("process-flow-visual"), ConnectorRoutingType.Straight,
            ConnectorRoutingOutcome.Path, [new PointD(110, 45), new PointD(310, 45)]);
        var documentRecord = new ConnectorRoutingRecord(new VisualStateId("document-flow-visual"), ConnectorRoutingType.Manual,
            ConnectorRoutingOutcome.Path, [new PointD(160, 245), new PointD(400, 245), new PointD(600, 200)], [new PointD(400, 245)]);
        var snapshot = new DocumentSnapshot(semantic, new VisualModelSnapshot(id, revision, visuals, [],
            [new ScopeRoutingSnapshot(semantic.RootScopeId, geometry, [processRecord])],
            documentFlow ? [new DocumentRoutingSnapshot(owner, [documentRecord])] : []), new DocumentMetadataSnapshot(id, revision));
        var nodes = visuals.Where(visual => semantic.TryGetElement(visual.SemanticElementId, out _)).Select(visual => new ProjectedNode(
            new ProjectionSourceTrace(id, new ProjectionRuleId("test:node-rule"), ProjectionSourceKind.SemanticElement,
                visual.SemanticElementId, type, "node", visual.Id))).ToArray();
        var edges = relationships.Select(relationship => new ProjectedEdge(new ProjectionSourceTrace(id,
            new ProjectionRuleId("test:flow-rule"), ProjectionSourceKind.SemanticRelationship, relationship.Id, relationshipType,
            "edge", new VisualStateId(relationship.Id.Value + "-visual")),
            nodes.Single(node => node.Source.SemanticElementId == relationship.SourceId).Id,
            nodes.Single(node => node.Source.SemanticElementId == relationship.TargetId).Id)).ToArray();
        var graph = new ProjectedGraph(id, revision, nodes, edges);
        var plan = new PresentationViewPlan(id, revision, new DocumentPresentationContext(owner),
            nodes.Select(static node => node.Source.SemanticElementId), relationships.Select(static relationship => relationship.Id),
            [new PresentationScopePlacement(semantic.RootScopeId, new VectorD(50, 200))]);
        return (snapshot, graph, plan);
    }

    private static string Diagnostics(DocumentConstructionResult result) =>
        string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message));
}
