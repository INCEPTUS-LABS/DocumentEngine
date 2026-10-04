using System.Text;
using System.Text.Json.Nodes;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class NativeDocumentV2RoutingTests
{
    [Fact]
    public void RoundTripPreservesPriorityPointsManualDefinitionsDormantHeightsAndMeasurementProvenance()
    {
        var snapshot = CreatePreparedSnapshot();
        var bytes = NativeDocumentSerializer.Export(snapshot);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory());

        Assert.True(imported.Succeeded, string.Join(";", imported.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var actual = imported.Document!.CaptureSnapshot();
        Assert.Equal(snapshot, actual);
        Assert.True(bytes.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(actual).AsSpan()));
        Assert.Equal(2, JsonNode.Parse(bytes.AsSpan())!["formatVersion"]!.GetValue<int>());
        var scope = Assert.Single(actual.VisualModel.RoutingScopes!.Value);
        Assert.Equal(["visual:z", "visual:a", "visual:no-route"], scope.Connectors.Select(static record => record.VisualStateId.Value));
        Assert.Equal(4, scope.Connectors[0].Path.Length);
        Assert.Equal(scope.Connectors[0].Path[1], scope.Connectors[0].Path[2]);
        Assert.Empty(scope.Connectors[1].ManualDefinition!.Value);
        Assert.Null(scope.Connectors[2].ManualDefinition);
        Assert.Null(scope.Geometry.Regions.Single(static region => !region.IsActive).ContentBounds);
        Assert.Equal("font-warning", Assert.Single(Assert.Single(scope.Geometry.TextMeasurements).Metrics.Diagnostics).Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(999)]
    public void UnsupportedVersionIsRejectedBeforeDocumentShapeIsRead(int version)
    {
        var payload = Encoding.UTF8.GetBytes($"{{\"format\":\"Inceptus.Document\",\"formatVersion\":{version},\"document\":null}}");
        var result = NativeDocumentSerializer.Import(payload);
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Equal("NATIVE_DOCUMENT_VERSION_UNSUPPORTED", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("missing-scopes")]
    [InlineData("empty-scopes")]
    [InlineData("duplicate-scope")]
    [InlineData("missing-connector")]
    [InlineData("duplicate-connector")]
    [InlineData("unknown-mode")]
    [InlineData("wrong-space")]
    [InlineData("extra-rank")]
    [InlineData("height-mismatch")]
    [InlineData("half-dormant")]
    [InlineData("stale-authority")]
    [InlineData("legacy-guidance")]
    public void IncompleteOrConflictingV2IsRejectedWithoutReconstruction(string mutation)
    {
        var json = JsonNode.Parse(NativeDocumentSerializer.Export(CreatePreparedSnapshot()).AsSpan())!;
        var visual = json["document"]!["visualModel"]!;
        var scopes = visual["routingScopes"]!.AsArray();
        var connectors = scopes[0]!["connectors"]!.AsArray();
        var geometry = scopes[0]!["geometry"]!;
        switch (mutation)
        {
            case "missing-scopes": visual.AsObject().Remove("routingScopes"); break;
            case "empty-scopes": scopes.Clear(); break;
            case "duplicate-scope": scopes.Add(scopes[0]!.DeepClone()); break;
            case "missing-connector": connectors.RemoveAt(0); break;
            case "duplicate-connector": connectors.Add(connectors[0]!.DeepClone()); break;
            case "unknown-mode": connectors[0]!["routingType"] = "invented"; break;
            case "wrong-space": connectors[0]!["space"] = "viewport"; break;
            case "extra-rank": connectors[0]!["rank"] = 3; break;
            case "height-mismatch": geometry["regions"]![0]!["expandedHeight"] = 999; break;
            case "half-dormant": geometry["regions"]![1]!["contentBounds"] = geometry["regions"]![0]!["contentBounds"]!.DeepClone(); break;
            case "stale-authority": connectors[2]!["automaticProof"]!["source"]!["semanticElementId"] = "node:target"; break;
            case "legacy-guidance": visual["visualStates"]![0]!["route"] = new JsonArray(new JsonObject { ["x"] = 1, ["y"] = 2 }); break;
        }
        var result = NativeDocumentSerializer.Import(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void FreshInMemorySnapshotIsDistinctFromACompleteExportableV2Snapshot()
    {
        var fresh = DocumentFactory.CreateEmpty(new DocumentId("fresh"));
        Assert.True(fresh.Succeeded);
        Assert.Null(fresh.Document!.CaptureSnapshot().VisualModel.RoutingScopes);
        Assert.Throws<ArgumentException>(() => NativeDocumentSerializer.Export(fresh.Document));
    }

    [Fact]
    public void PriorityEqualityAndSerializationAreOrderSensitiveWithoutChangingVisualIdentityOrder()
    {
        var snapshot = CreatePreparedSnapshot();
        var scope = Assert.Single(snapshot.VisualModel.RoutingScopes!.Value);
        var reversed = new ScopeRoutingSnapshot(scope.ScopeId, scope.Geometry, scope.Connectors.Reverse());
        Assert.NotEqual(scope, reversed);
        var original = NativeDocumentSerializer.Export(snapshot);
        var reordered = NativeDocumentSerializer.Export(new DocumentSnapshot(snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates,
                snapshot.VisualModel.ProfileElementPresentations, [reversed]), snapshot.Metadata));
        Assert.False(original.AsSpan().SequenceEqual(reordered.AsSpan()));
        Assert.Equal(snapshot.VisualModel.VisualStates.Select(static visual => visual.Id.Value).Order(StringComparer.Ordinal),
            snapshot.VisualModel.VisualStates.Select(static visual => visual.Id.Value));
    }

    private static DocumentSnapshot CreatePreparedSnapshot()
    {
        var id = new DocumentId("native-v2-test");
        var revision = new DocumentRevision(7);
        var source = new SemanticElementId("node:source");
        var target = new SemanticElementId("node:target");
        var sourceVisual = new VisualStateId("visual:source");
        var targetVisual = new VisualStateId("visual:target");
        var ids = new[] { "z", "a", "no-route" };
        var relationships = ids.Select(value => new SemanticRelationshipSnapshot(new SemanticElementId("flow:" + value),
            new SemanticTypeId("test:flow"), source, target)).ToArray();
        var semantic = new SemanticModelSnapshot(id, revision,
            [new SemanticElementSnapshot(source, new SemanticTypeId("test:node")), new SemanticElementSnapshot(target, new SemanticTypeId("test:node"))], relationships);
        var visuals = new[]
        {
            new VisualStateSnapshot(sourceVisual, source, new PointD(0, 0), new SizeD(100, 50), VisualPlacementMode.Pinned),
            new VisualStateSnapshot(targetVisual, target, new PointD(300, 0), new SizeD(100, 50), VisualPlacementMode.Pinned),
        }.Concat(relationships.Select((relationship, index) => new VisualStateSnapshot(new VisualStateId("visual:" + ids[index]),
            relationship.Id, new PointD(0, 0), new SizeD(0, 0), VisualPlacementMode.Automatic))).ToArray();
        var request = new TextMeasurementRequest("Name", "Test Sans", "test-font", "1", 12, 16, 400,
            TextFontStyle.Normal, "en", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom, 1, "test-measurement", "1");
        var measurement = new ScopeTextMeasurementSnapshot(request, new TextMetrics(35, 10, 3, 16, new RectD(0, -10, 35, 13), "test-font",
            [new Diagnostic("font-warning", DiagnosticSeverity.Warning, "Test fallback", "test-font", [new("requested", "Test Sans")])]));
        var region = new Canvas2DSpatialRegionId("region:active");
        var geometry = new ScopeGeometrySnapshot("test-geometry", "1", new AlgorithmId("test-layout"), Canvas2DSceneConfiguration.Default, request,
            [new ScopeGeometryContributorSnapshot(new Canvas2DSceneContributorDescriptor(new Canvas2DSceneContributorId("test-contributor"), "1"), Canvas2DSceneContributionStage.Canonical)],
            [new ScopeNodeGeometrySnapshot(sourceVisual, new RectD(0, 0, 100, 50), Matrix2D.Identity, region),
                new ScopeNodeGeometrySnapshot(targetVisual, new RectD(300, 0, 100, 50), Matrix2D.Identity, region)],
            [new SpatialRegionGeometrySnapshot(region, new ModelProfileId("test-profile"), source, 200, Matrix2D.Identity, new RectD(0, 0, 1000, 200)),
                new SpatialRegionGeometrySnapshot(new Canvas2DSpatialRegionId("region:dormant"), new ModelProfileId("test-profile"), null, 144, null, null)],
            [new ScopeNodeCaptionSnapshot(sourceVisual, new ProjectedObjectId("label:source"), NodeLabelPlacement.InsideCentered, null,
                new RectD(0, 0, 100, 50), new RectD(32, 17, 35, 16), Matrix2D.Identity, [measurement])], [measurement],
            [new SpatialScopeWidthSnapshot(new ModelProfileId("test-profile"), 1000)]);
        var from = new PointD(100, 25);
        var to = new PointD(300, 25);
        var noRouteProof = new ConnectorAutomaticRouteProof(new AlgorithmId("test-router"), "1", null, null,
            new ConnectorRoutingEndpointObservation(source, sourceVisual, null, ConnectorAnchorRole.Source, ConnectorAnchorSide.Right, 0, 1, from, new VectorD(1, 0)),
            new ConnectorRoutingEndpointObservation(target, targetVisual, null, ConnectorAnchorRole.Target, ConnectorAnchorSide.Left, 0, 1, to, new VectorD(-1, 0)));
        var middle = new PointD(200, 25);
        ConnectorRoutingRecord[] records =
        [
            new(new VisualStateId("visual:z"), ConnectorRoutingType.Manual, ConnectorRoutingOutcome.Path, [from, middle, middle, to], [middle, middle]),
            new(new VisualStateId("visual:a"), ConnectorRoutingType.Straight, ConnectorRoutingOutcome.Path, [from, to], []),
            new(new VisualStateId("visual:no-route"), ConnectorRoutingType.Automatic, ConnectorRoutingOutcome.NoRoute, [], null, noRouteProof, ConnectorNoRouteReason.NoFeasiblePath),
        ];
        return new DocumentSnapshot(semantic, new VisualModelSnapshot(id, revision, visuals, [],
            [new ScopeRoutingSnapshot(semantic.RootScopeId, geometry, records)]), new DocumentMetadataSnapshot(id, revision));
    }
}
