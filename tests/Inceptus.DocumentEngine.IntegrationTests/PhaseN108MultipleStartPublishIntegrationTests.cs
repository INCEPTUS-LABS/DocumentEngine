using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN108MultipleStartPublishIntegrationTests
{
    private const string BootstrapPrefix = "globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = ";

    [Fact]
    public async Task TwoIndependentStartsPublishBothCompletePaths()
    {
        var snapshot = CreateSnapshot(
            ["start-a", "task-a", "end-a", "start-b", "task-b", "end-b"],
            [("start-a", "task-a"), ("task-a", "end-a"),
             ("start-b", "task-b"), ("task-b", "end-b")]);
        var composition = await new BpmnModelerCompositionFactory(
            initialDocument: snapshot).CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync(
            "phase-n108-two-starts", new Canvas2DSurfaceSize(1400d, 900d, 1d))).Succeeded);
        var attached = await EditingSession.AttachAsync(
            composition.Document, renderer, composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var before = session.CaptureState();
        var documentBefore = composition.Document.CaptureSnapshot();

        var captured = await session.CapturePresentationAsync(
            before.ModelProfileViewState, before.ModelProfileElementViewState);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        var built = new PublishedProcessPackageBuilder(
            new BpmnPublishedTokenRoleClassifier(), new BpmnPublishedNodeDataMapper())
            .Build(captured.Capture!);

        Assert.True(built.Succeeded, Diagnostics(built.Diagnostics));
        var package = Assert.IsType<PublishedProcessPackage>(built.Package);
        Assert.Equal(2, package.Snapshot.TokenGraph.Nodes.Count(node =>
            node.Role == PublishedTokenRole.Start));
        Assert.Equal(snapshot.SemanticModel.Elements.Select(element => element.Id.Value),
            package.Snapshot.TokenGraph.Nodes.Select(node => node.Id));
        Assert.Equal(4, package.Snapshot.Presentation.Connectors.Length);
        Assert.Equal(1, package.Snapshot.FormatVersion);
        Assert.Same(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task EveryStartCountUsesExistingVersionOneDataWithoutChangingSource(int startCount)
    {
        var nodes = Enumerable.Range(0, startCount)
            .SelectMany(index => new[] { $"start-{index}", $"task-{index}", $"end-{index}" })
            .ToArray();
        var edges = Enumerable.Range(0, startCount)
            .SelectMany(index => new[]
            {
                ($"start-{index}", $"task-{index}"), ($"task-{index}", $"end-{index}"),
            }).ToArray();
        var snapshot = CreateSnapshot(nodes, edges);
        var captured = await CaptureAsync(snapshot);

        var built = Builder().Build(captured);
        var repeated = Builder().Build(captured);

        Assert.True(built.Succeeded, Diagnostics(built.Diagnostics));
        Assert.True(repeated.Succeeded, Diagnostics(repeated.Diagnostics));
        var package = Assert.IsType<PublishedProcessPackage>(built.Package);
        Assert.Equal(startCount, package.Snapshot.TokenGraph.Nodes.Count(node =>
            node.Role == PublishedTokenRole.Start));
        Assert.Equal(startCount * 3, package.Snapshot.TokenGraph.Nodes.Length);
        Assert.Equal(startCount * 2, package.Snapshot.Presentation.Connectors.Length);
        Assert.Equal(PublishedProcessPackageBuilder.Format, package.Snapshot.Format);
        Assert.Equal(1, package.Snapshot.FormatVersion);
        Assert.Equal(snapshot.Publication!.Code, package.Snapshot.Publication.Code);
        Assert.Equal(snapshot.Publication.Title, package.Snapshot.Publication.Title);
        Assert.Equal(snapshot.Publication.Description, package.Snapshot.Publication.Description);
        foreach (var index in Enumerable.Range(0, startCount))
        {
            Assert.Equal($"Description for task-{index}\nPreserved in Publish.",
                package.Snapshot.Presentation.Nodes.Single(node =>
                    node.Id == NodeId($"task-{index}").Value).Description);
        }

        Assert.True(package.ProcessJson.AsSpan().SequenceEqual(repeated.Package!.ProcessJson.AsSpan()));
        Assert.True(package.Archive.AsSpan().SequenceEqual(repeated.Package.Archive.AsSpan()));
        AssertBootstrapEqualsProcessJson(package);
    }

    [Fact]
    public async Task ConvergentStartsShareCanonicalMergeAndDownstreamIdentitiesExactlyOnce()
    {
        var snapshot = CreateSnapshot(
            ["start-a", "start-b", "merge", "task-shared", "end-shared"],
            [("start-a", "merge"), ("start-b", "merge"),
             ("merge", "task-shared"), ("task-shared", "end-shared")]);
        var capture = await CaptureAsync(snapshot);

        var built = Builder().Build(capture);

        Assert.True(built.Succeeded, Diagnostics(built.Diagnostics));
        var package = Assert.IsType<PublishedProcessPackage>(built.Package);
        Assert.Equal(snapshot.SemanticModel.Elements.Select(element => element.Id.Value),
            package.Snapshot.TokenGraph.Nodes.Select(node => node.Id));
        Assert.Equal(snapshot.SemanticModel.Relationships.Select(flow => flow.Id.Value),
            package.Snapshot.Presentation.Connectors.Select(connector => connector.Id));
        var merge = Assert.Single(package.Snapshot.TokenGraph.Nodes, node =>
            node.Id == NodeId("merge").Value);
        Assert.Equal(PublishedTokenRole.MergeInvariant, merge.Role);
        Assert.Equal(2, merge.IncomingConnectorIds.Length);
        Assert.Single(merge.OutgoingConnectorIds);
        Assert.Equal(5, package.Snapshot.TokenGraph.Nodes.Select(node => node.Id)
            .Distinct(StringComparer.Ordinal).Count());
        AssertBootstrapEqualsProcessJson(package);
    }

    [Fact]
    public async Task InvalidNodeReachableOnlyFromSecondStartRetainsActionableIdentity()
    {
        var snapshot = CreateSnapshot(
            ["start-a", "task-a", "end-a", "start-b", "task-b"],
            [("start-a", "task-a"), ("task-a", "end-a"), ("start-b", "task-b")]);

        var built = Builder().Build(await CaptureAsync(snapshot));

        Assert.False(built.Succeeded);
        Assert.Null(built.Package);
        var diagnostic = Assert.Single(built.Diagnostics);
        Assert.Equal("PUBLISH_TOKEN_CONTINUATION_AMBIGUOUS", diagnostic.Code);
        Assert.Equal(NodeId("task-b").Value, diagnostic.SourceIdentity);
        Assert.Equal(NodeId("task-b").Value, diagnostic.Context["ElementId"]);
        Assert.Equal(BpmnSemanticTypes.Task.Value, diagnostic.Context["ElementType"]);
        Assert.Contains("task-b", diagnostic.Context["ElementName"], StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Context["SuggestedAction"]));
    }

    [Fact]
    public async Task MultipleReachableGraphFailuresAreNotHiddenByTheStartCount()
    {
        var snapshot = CreateSnapshot(
            ["start-a", "start-b", "task-b"], [("start-b", "task-b")]);

        var built = Builder().Build(await CaptureAsync(snapshot));

        Assert.False(built.Succeeded);
        Assert.Equal(2, built.Diagnostics.Length);
        Assert.Contains(built.Diagnostics, diagnostic =>
            diagnostic.Code == "PUBLISH_TOKEN_START_INVALID" &&
            diagnostic.Context["ElementId"] == NodeId("start-a").Value);
        Assert.Contains(built.Diagnostics, diagnostic =>
            diagnostic.Code == "PUBLISH_TOKEN_CONTINUATION_AMBIGUOUS" &&
            diagnostic.Context["ElementId"] == NodeId("task-b").Value);
    }

    [Fact]
    public async Task ZeroStartsRemainRejectedWithAnActionableScopeDiagnostic()
    {
        var snapshot = CreateSnapshot(["task-a", "end-a"], [("task-a", "end-a")]);

        var built = Builder().Build(await CaptureAsync(snapshot));

        Assert.False(built.Succeeded);
        var diagnostic = Assert.Single(built.Diagnostics);
        Assert.Equal("PUBLISH_TOKEN_START_INVALID", diagnostic.Code);
        Assert.Equal(snapshot.SemanticModel.RootScopeId.Value, diagnostic.SourceIdentity);
        Assert.Contains("at least one Start", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Context["SuggestedAction"]));
    }

    [Fact]
    public async Task MultipleStartsDoNotPermitAmbiguousOutgoingConnectionsFromAStart()
    {
        var snapshot = CreateSnapshot(
            ["start-a", "task-a", "task-extra", "end-a", "start-b", "task-b", "end-b"],
            [("start-a", "task-a"), ("start-a", "task-extra"),
             ("task-a", "end-a"), ("task-extra", "end-a"),
             ("start-b", "task-b"), ("task-b", "end-b")]);

        var built = Builder().Build(await CaptureAsync(snapshot));

        Assert.False(built.Succeeded);
        var diagnostic = Assert.Single(built.Diagnostics);
        Assert.Equal("PUBLISH_TOKEN_START_INVALID", diagnostic.Code);
        Assert.Equal(NodeId("start-a").Value, diagnostic.Context["ElementId"]);
        Assert.False(diagnostic.Context.ContainsKey("ScopeId"));
        Assert.Equal("Give this Start element exactly one outgoing connector.",
            diagnostic.Context["SuggestedAction"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeAndDocumentDiagnosticsDoNotBorrowAnElementWithTheSameOpaqueId(
        bool missingPublication)
    {
        var snapshot = CreateSnapshot(["task-a", "end-a"], [("task-a", "end-a")],
            documentIdentity: NodeId("task-a").Value);
        if (missingPublication)
        {
            snapshot = new DocumentSnapshot(snapshot.SemanticModel, snapshot.VisualModel,
                snapshot.Metadata, publication: null);
        }

        var built = Builder().Build(await CaptureAsync(snapshot));

        Assert.False(built.Succeeded);
        var diagnostic = Assert.Single(built.Diagnostics);
        Assert.Equal(missingPublication ? "PUBLISH_PUBLICATION_REQUIRED" : "PUBLISH_TOKEN_START_INVALID",
            diagnostic.Code);
        Assert.Equal(snapshot.DocumentId.Value, diagnostic.SourceIdentity);
        Assert.False(diagnostic.Context.ContainsKey("ElementId"));
        Assert.False(diagnostic.Context.ContainsKey("ElementName"));
        Assert.False(diagnostic.Context.ContainsKey("ElementType"));
        Assert.Equal(!missingPublication, diagnostic.Context.ContainsKey("ScopeId"));
    }

    [Fact]
    public async Task UnreachableNodeRemainsPresentationOnlyUnderExistingReachabilityPolicy()
    {
        var snapshot = CreateSnapshot(
            ["start-a", "task-a", "end-a", "start-b", "task-b", "end-b", "task-unreachable"],
            [("start-a", "task-a"), ("task-a", "end-a"),
             ("start-b", "task-b"), ("task-b", "end-b")]);

        var built = Builder().Build(await CaptureAsync(snapshot));

        Assert.True(built.Succeeded, Diagnostics(built.Diagnostics));
        Assert.Contains(built.Package!.Snapshot.Presentation.Nodes,
            node => node.Id == NodeId("task-unreachable").Value);
        Assert.DoesNotContain(built.Package.Snapshot.TokenGraph.Nodes,
            node => node.Id == NodeId("task-unreachable").Value);
    }

    [Fact]
    public async Task MissingRouteRetainsConnectorAndEndpointIdentitiesSeparatelyFromGraphFailures()
    {
        var snapshot = CreateSnapshot(["start-a", "task-a", "end-a"],
            [("start-a", "task-a"), ("task-a", "end-a")]);
        var capture = await CaptureAsync(snapshot);
        var missing = capture.ProjectedGraph.Edges.Single(edge =>
            edge.Source.SemanticElementId == FlowId(("start-a", "task-a")));
        var routing = capture.RoutingResult;
        var missingRouteCapture = capture with
        {
            RoutingResult = new RoutingResult(routing.DocumentId, routing.SourceRevision,
                routing.LayoutAlgorithmId, routing.RoutingAlgorithmId,
                new RoutingComputation(routing.Routes.Where(route =>
                    route.ProjectedEdgeId != missing.Id), routing.Metadata, [missing.Id])),
        };

        var built = Builder().Build(missingRouteCapture);

        Assert.False(built.Succeeded);
        var diagnostic = Assert.Single(built.Diagnostics, item =>
            item.Code == "PUBLISH_CONNECTOR_ROUTE_MISSING");
        Assert.Equal(missing.Source.SemanticElementId.Value, diagnostic.Context["ConnectorId"]);
        Assert.Equal(NodeId("start-a").Value, diagnostic.Context["SourceElementId"]);
        Assert.Equal(NodeId("task-a").Value, diagnostic.Context["TargetElementId"]);
        Assert.False(diagnostic.Context.ContainsKey("ElementId"));
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Context["SuggestedAction"]));
    }

    [Fact]
    public async Task PackageFailureKeepsEarlierRouteDiagnosticWithoutExposingExceptionMessage()
    {
        var snapshot = CreateSnapshot(["start-a", "task-a", "end-a"],
            [("start-a", "task-a"), ("task-a", "end-a")]);
        var capture = await CaptureAsync(snapshot);
        var missing = capture.ProjectedGraph.Edges.Single(edge =>
            edge.Source.SemanticElementId == FlowId(("start-a", "task-a")));
        var routing = capture.RoutingResult;
        var missingRouteCapture = capture with
        {
            RoutingResult = new RoutingResult(routing.DocumentId, routing.SourceRevision,
                routing.LayoutAlgorithmId, routing.RoutingAlgorithmId,
                new RoutingComputation(routing.Routes.Where(route =>
                    route.ProjectedEdgeId != missing.Id), routing.Metadata, [missing.Id])),
        };

        var built = new PublishedProcessPackageBuilder(new BpmnPublishedTokenRoleClassifier(),
            new ThrowingNodeDataMapper()).Build(missingRouteCapture);

        Assert.False(built.Succeeded);
        Assert.Null(built.Package);
        Assert.Equal(2, built.Diagnostics.Length);
        var routeFailure = Assert.Single(built.Diagnostics, item =>
            item.Code == "PUBLISH_CONNECTOR_ROUTE_MISSING");
        Assert.Equal(missing.Source.SemanticElementId.Value, routeFailure.Context["ConnectorId"]);
        var packageFailure = Assert.Single(built.Diagnostics, item =>
            item.Code == "PUBLISH_PACKAGE_BUILD_FAILED");
        Assert.Equal("nodes", packageFailure.Context["BuildStage"]);
        Assert.False(packageFailure.Context.ContainsKey("ElementId"));
        Assert.All(built.Diagnostics, diagnostic =>
        {
            Assert.DoesNotContain("Private mapper detail", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostic.Context.Values, value =>
                value.Contains("Private mapper detail", StringComparison.Ordinal));
        });
    }

    private static async Task<EditingSessionPresentationCapture> CaptureAsync(DocumentSnapshot snapshot)
    {
        var composition = await new BpmnModelerCompositionFactory(initialDocument: snapshot).CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync("phase-n108-capture",
            new Canvas2DSurfaceSize(1800d, 1200d, 1d))).Succeeded);
        var attached = await EditingSession.AttachAsync(composition.Document, renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        var before = session.CaptureState();
        var documentBefore = composition.Document.CaptureSnapshot();
        var captured = await session.CapturePresentationAsync(before.ModelProfileViewState,
            before.ModelProfileElementViewState);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        Assert.Same(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Same(before.EditorState, session.CaptureState().EditorState);
        return Assert.IsType<EditingSessionPresentationCapture>(captured.Capture);
    }

    private static PublishedProcessPackageBuilder Builder() =>
        new(new BpmnPublishedTokenRoleClassifier(), new BpmnPublishedNodeDataMapper());

    private static void AssertBootstrapEqualsProcessJson(PublishedProcessPackage package)
    {
        using var stream = new MemoryStream(package.Archive.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("process.data.js"));
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var bootstrap = reader.ReadToEnd();
        Assert.StartsWith(BootstrapPrefix, bootstrap, StringComparison.Ordinal);
        Assert.EndsWith(";\n", bootstrap, StringComparison.Ordinal);
        using var process = JsonDocument.Parse(package.ProcessJson.AsMemory());
        using var data = JsonDocument.Parse(bootstrap[BootstrapPrefix.Length..^2]);
        Assert.True(JsonElement.DeepEquals(process.RootElement, data.RootElement));
    }

    private static DocumentSnapshot CreateSnapshot(
        string[] nodeKeys,
        (string Source, string Target)[] edges,
        string? documentIdentity = null)
    {
        var documentId = new DocumentId(documentIdentity ?? "test:n108:multiple-starts");
        var revision = new DocumentRevision(7);
        var elements = nodeKeys.Select((key, index) =>
            key.StartsWith("start", StringComparison.Ordinal)
                ? BpmnSemanticFactory.CreateStartEvent(NodeId(key), "Shared Start name", $"Start {key}")
                : key.StartsWith("end", StringComparison.Ordinal)
                    ? BpmnSemanticFactory.CreateEndEvent(NodeId(key), key)
                    : key == "merge"
                        ? BpmnSemanticFactory.CreateExclusiveGateway(NodeId(key), key, key)
                        : BpmnSemanticFactory.CreateTask(NodeId(key), key, key, index + 1,
                            $"Description for {key}\nPreserved in Publish.")).ToArray();
        var relationships = edges.Select(edge => BpmnSemanticFactory.CreateSequenceFlow(
            FlowId(edge), NodeId(edge.Source), NodeId(edge.Target))).ToArray();
        var visuals = new List<VisualStateSnapshot>();
        for (var index = 0; index < nodeKeys.Length; index++)
        {
            var key = nodeKeys[index];
            var anchors = edges.Where(edge => edge.Source == key)
                .Select((edge, order) => new ConnectorAnchor(
                    SourceAnchorId(edge), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, order))
                .Concat(edges.Where(edge => edge.Target == key)
                    .Select((edge, order) => new ConnectorAnchor(
                        TargetAnchorId(edge), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, order)));
            visuals.Add(new VisualStateSnapshot(
                VisualId(key), NodeId(key), Position(index), new SizeD(120d, 80d),
                VisualPlacementMode.Manual, connectorAnchors: anchors));
        }

        foreach (var edge in edges)
        {
            var source = Position(Array.IndexOf(nodeKeys, edge.Source));
            var target = Position(Array.IndexOf(nodeKeys, edge.Target));
            visuals.Add(new VisualStateSnapshot(
                new VisualStateId($"test:n108:visual/{edge.Source}-{edge.Target}"),
                FlowId(edge), new PointD(0d, 0d), new SizeD(0d, 0d), VisualPlacementMode.Manual,
                route: [new PointD(source.X + 120d, source.Y + 40d),
                    new PointD(target.X, target.Y + 40d)],
                sourceAnchorId: SourceAnchorId(edge), targetAnchorId: TargetAnchorId(edge)));
        }

        return new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision, elements, relationships),
            new VisualModelSnapshot(documentId, revision, visuals),
            new DocumentMetadataSnapshot(documentId, revision),
            new DocumentPublicationSnapshot("multi-start", "Multiple Starts", "Publication description"));
    }

    private static PointD Position(int index) => new(80d + index % 3 * 260d, 80d + index / 3 * 240d);

    private static SemanticElementId NodeId(string key) => new($"test:n108:node/{key}");

    private static VisualStateId VisualId(string key) => new($"test:n108:visual/{key}");

    private static SemanticElementId FlowId((string Source, string Target) edge) =>
        new($"test:n108:flow/{edge.Source}-{edge.Target}");

    private static ConnectorAnchorId SourceAnchorId((string Source, string Target) edge) =>
        new($"test:n108:anchor/{edge.Source}-{edge.Target}/source");

    private static ConnectorAnchorId TargetAnchorId((string Source, string Target) edge) =>
        new($"test:n108:anchor/{edge.Source}-{edge.Target}/target");

    private static Canvas2DRenderer CreateRenderer() =>
        new(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources: [new Canvas2DFontResource("org.dejavu.DejaVuSans", "2.37",
                    "DejaVu Sans", "fonts/DejaVuSans-2.37.ttf", 400, TextFontStyle.Normal)],
                defaultFontFamily: "DejaVu Sans"));

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class ThrowingNodeDataMapper : IPublishedNodeDataMapper
    {
        public PublishedNodeData Map(SemanticElementSnapshot semanticElement) =>
            throw new InvalidOperationException("Private mapper detail");
    }
}
