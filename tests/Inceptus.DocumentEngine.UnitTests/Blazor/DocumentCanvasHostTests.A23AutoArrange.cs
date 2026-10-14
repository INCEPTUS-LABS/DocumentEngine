using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("linear")]
    [InlineData("branches")]
    [InlineData("no-merge")]
    [InlineData("cycle")]
    [InlineData("components")]
    [InlineData("multiple-events")]
    public async Task A23AutoArrangeTopologyHistoryPersistenceAndIdempotence(string topology)
    {
        var edges = A23Edges(topology);
        await using var host = A23Host(A23Snapshot(edges));
        await host.InitializeAsync("arrange-" + topology, "container");
        Assert.True(host.CanAutoArrange, string.Join("; ", host.CaptureState().HostDiagnostics.Select(diagnostic => diagnostic.Message)));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var count = session.CaptureState().HistoryStatus.EntryCount;
        var result = await host.AutoArrangeAsync();
        Assert.NotNull(result);
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(count + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        var nodes = after.VisualModel.RoutingScopes!.Value.Single().Geometry.Nodes;
        foreach (var node in nodes)
            foreach (var other in nodes.Where(other => StringComparer.Ordinal.Compare(node.VisualStateId.Value, other.VisualStateId.Value) < 0))
                Assert.False(node.LocalBounds.Intersects(other.LocalBounds));
        foreach (var (source, target) in edges.Where(edge => edge != ("c", "a")))
            Assert.True(A23Bounds(after, source).Left < A23Bounds(after, target).Left, source + " -> " + target);
        Assert.All(after.VisualModel.EnumerateConnectorRoutingRecords(), record => Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome));
        var plan = BpmnAutoArrangePlanner.Plan(after, [after.SemanticModel.RootScopeId]);
        Assert.True(plan.IsNoChange, string.Join(",", plan.ChangedVisualStateIds));
        await host.AutoArrangeAsync();
        Assert.Same(after, document.CaptureSnapshot());
        Assert.Equal(count + 1, session.CaptureState().HistoryStatus.EntryCount);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
        var bytes = NativeDocumentSerializer.Export(after);
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(opened.Succeeded);
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(opened.Document!).ToArray());
        await using var reopened = A23Host(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("arrange-reopen-" + topology, "container");
        Assert.True(reopened.CaptureState().IsInitialized);
        A23PresentationEqual(after, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic, 0)]
    [InlineData(ConnectorRoutingType.Straight, 0)]
    [InlineData(ConnectorRoutingType.Manual, 0)]
    [InlineData(ConnectorRoutingType.Automatic, 25)]
    [InlineData(ConnectorRoutingType.Straight, 25)]
    [InlineData(ConnectorRoutingType.Manual, 25)]
    public async Task A23AutoArrangePreservesModesLabelsAndManualDefinitions(ConnectorRoutingType mode, double distance)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")),
            distance == 0 ? null : new TestGridSource(new() { PointDistance = distance }));
        await host.InitializeAsync("arrange-modes", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var connector = document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().First();
        if (mode != ConnectorRoutingType.Automatic)
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId, document.Revision,
                connector.VisualStateId, mode))).IsCommitted);
        var placement = new ConnectorLabelPlacement(.4, new VectorD(11, 17));
        Assert.True((await session.ExecuteAsync(new MoveLabelCommand(document.DocumentId, document.Revision, connector.VisualStateId, placement))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var result = await host.AutoArrangeAsync();
        Assert.True(result!.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var after = document.CaptureSnapshot();
        var oldRecord = before.VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId == connector.VisualStateId);
        var newRecord = after.VisualModel.EnumerateConnectorRoutingRecords().Single(record => record.VisualStateId == connector.VisualStateId);
        Assert.Equal(mode, newRecord.RoutingType);
        Assert.Equal(oldRecord.ManualDefinition, newRecord.ManualDefinition);
        Assert.Equal(before.VisualModel.VisualStates.Single(visual => visual.Id == connector.VisualStateId).Properties,
            after.VisualModel.VisualStates.Single(visual => visual.Id == connector.VisualStateId).Properties);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
    }

    [Fact]
    public async Task A23AutoArrangeStalePlanFailsWithoutRevisionOrHistory()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("arrange-stale", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var plan = BpmnAutoArrangePlanner.Plan(document.CaptureSnapshot(), [document.CaptureSnapshot().SemanticModel.RootScopeId]);
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision, new("a"), BpmnSemanticProperties.Name, "Later name"))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        Assert.False((await session.ExecuteAsync(new BpmnAutoArrangeCommand(plan))).IsCommitted);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task A23AutoArrangePoolExpansionPreservesLaneAndBlockOrder()
    {
        await using var host = A23Host(A22PoolSnapshot());
        await host.InitializeAsync("arrange-pools", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var result = await host.AutoArrangeAsync();
        Assert.True(result!.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var after = document.CaptureSnapshot();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.VisualModel.ProfileElementPresentations.AsEnumerable(), after.VisualModel.ProfileElementPresentations);
        var old = before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        var current = after.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        Assert.Equal(old.OrderBy(region => region.ContentBounds!.Value.Top).Select(region => region.Id),
            current.OrderBy(region => region.ContentBounds!.Value.Top).Select(region => region.Id));
        Assert.Equal(current.Single(region => region.ContainerSemanticElementId?.Value == "sales").ContentBounds!.Value.Bottom,
            current.Single(region => region.ContainerSemanticElementId?.Value == "warehouse").ContentBounds!.Value.Top);
        Assert.Equal(old.Select(region => region.ExpandedHeight), current.Select(region => region.ExpandedHeight));
        await host.AutoArrangeAsync();
        Assert.Same(after, document.CaptureSnapshot());
    }

    private static (string Source, string Target)[] A23Edges(string topology) => topology switch
    {
        "branches" => [("start", "split"), ("split", "a"), ("split", "b"), ("split", "c"), ("a", "merge"), ("b", "merge"), ("c", "merge"), ("merge", "end")],
        "no-merge" => [("start", "split"), ("split", "a"), ("split", "b"), ("split", "c"), ("a", "d"), ("b", "d"), ("c", "d"), ("d", "end")],
        "cycle" => [("start", "a"), ("a", "b"), ("b", "c"), ("c", "a"), ("c", "end")],
        "components" => [("start1", "a"), ("a", "end1"), ("start2", "b"), ("b", "end2"), ("start3", "c"), ("c", "end3")],
        "multiple-events" => [("start1", "a"), ("start2", "a"), ("a", "end1"), ("a", "end2")],
        _ => [("start", "a"), ("a", "b"), ("b", "end")],
    };

    private static DocumentCanvasHost A23Host(DocumentSnapshot snapshot, Contracts.EditorState.IGridSettingsSource? grid = null) =>
        CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1920, 1080, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot), gridSettings: grid);

    private static DocumentSnapshot A23Snapshot((string Source, string Target)[] edges)
    {
        var id = new DocumentId("a23:arrange");
        var revision = DocumentRevision.Zero;
        var names = edges.SelectMany(edge => new[] { edge.Source, edge.Target }).Distinct().ToArray();
        var elements = names.Select(name => new SemanticElementSnapshot(new(name), name.StartsWith("start", StringComparison.Ordinal)
            ? BpmnSemanticTypes.StartEvent : name.StartsWith("end", StringComparison.Ordinal) ? BpmnSemanticTypes.EndEvent :
            name is "split" or "merge" ? BpmnSemanticTypes.ExclusiveGateway : BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(name))])).ToArray();
        var relationships = edges.Select((edge, index) => new SemanticRelationshipSnapshot(new("flow-" + index),
            BpmnSemanticTypes.SequenceFlow, new(edge.Source), new(edge.Target))).ToArray();
        var nodes = elements.Select((element, index) => new VisualStateSnapshot(new("v-" + element.Id.Value), element.Id,
            new PointD(50 + ((names.Length - index) % 4) * 180, 50 + index * 140),
            BpmnSemanticTypes.IsEvent(element.TypeId) ? new SizeD(36, 36) : element.TypeId == BpmnSemanticTypes.ExclusiveGateway ? new SizeD(50, 50) : new SizeD(120 + index * 2, 80),
            VisualPlacementMode.Pinned, connectorAnchors:
                relationships.Where(flow => flow.SourceId == element.Id).Select((flow, order) =>
                    new ConnectorAnchor(new(flow.Id.Value + "-s"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, order))
                .Concat(relationships.Where(flow => flow.TargetId == element.Id).Select((flow, order) =>
                    new ConnectorAnchor(new(flow.Id.Value + "-t"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, order)))));
        var connectors = relationships.Select(flow => new VisualStateSnapshot(new("v-" + flow.Id.Value), flow.Id, default, default,
            VisualPlacementMode.Automatic, sourceAnchorId: new(flow.Id.Value + "-s"), targetAnchorId: new(flow.Id.Value + "-t")));
        return new(new SemanticModelSnapshot(id, revision, elements, relationships), new VisualModelSnapshot(id, revision, nodes.Concat(connectors)),
            new DocumentMetadataSnapshot(id, revision));
    }

    private static RectD A23Bounds(DocumentSnapshot snapshot, string id) => snapshot.VisualModel.RoutingScopes!.Value
        .SelectMany(scope => scope.Geometry.Nodes).Single(node => node.VisualStateId.Value == "v-" + id).LocalBounds;

    private static void A23PresentationEqual(DocumentSnapshot expected, DocumentSnapshot actual)
    {
        Assert.Equal(expected.VisualModel.VisualStates.AsEnumerable(), actual.VisualModel.VisualStates);
        foreach (var scope in expected.VisualModel.RoutingScopes!.Value)
        {
            var other = actual.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == scope.ScopeId);
            Assert.Equal(scope.Geometry, other.Geometry);
            Assert.Equal(scope.Connectors.Select(record => record.VisualStateId), other.Connectors.Select(record => record.VisualStateId));
            foreach (var record in scope.Connectors)
                Assert.Equal(record, other.Connectors.Single(candidate => candidate.VisualStateId == record.VisualStateId));
        }
        Assert.Equal(expected.VisualModel.RoutingScopes!.Value.AsEnumerable(), actual.VisualModel.RoutingScopes!.Value);
        Assert.Equal(expected.VisualModel.DocumentRoutingStates.AsEnumerable(), actual.VisualModel.DocumentRoutingStates);
    }
}
