using System.IO.Compression;
using System.Text;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA11SequenceFlowLabelIntegrationTests
{
    [Theory]
    [InlineData("BPMN.ExclusiveGateway")]
    [InlineData("BPMN.ParallelGateway")]
    [InlineData("BPMN.InclusiveGateway")]
    public async Task GatewayBranchesHaveIndependentNamesAndExactOptionalHistory(string type)
    {
        await using var test = await Fixture.CreateAsync();
        var source = test.Snapshot.SemanticModel.Elements.First(element => element.TypeId.Value == type &&
            test.Snapshot.SemanticModel.Relationships.Count(flow => flow.SourceId == element.Id) >= 2);
        var branches = test.Snapshot.SemanticModel.Relationships.Where(flow => flow.SourceId == source.Id).Take(2).ToArray();
        var first = branches[0].Id;
        var second = branches[1].Id;
        Assert.Null(test.Name(first));
        Assert.Empty(test.Lines(first));
        var original = test.Snapshot;
        var routes = test.State.RoutingResult!.Routes;
        var layouts = test.State.LayoutResult!.Nodes;
        await test.NameAsync(first, "Approved");
        await test.NameAsync(second, "Rejected");
        Assert.Equal("Approved", Assert.Single(test.Lines(first)).Geometry.Content);
        Assert.Equal("Rejected", Assert.Single(test.Lines(second)).Geometry.Content);
        test.AssertAutomatic(first);
        test.AssertAutomatic(second);
        Assert.Equal(original.SemanticModel.Elements.AsEnumerable(), test.Snapshot.SemanticModel.Elements);
        Assert.Equal(original.VisualModel.VisualStates.AsEnumerable(), test.Snapshot.VisualModel.VisualStates);
        Assert.Equal(layouts.AsEnumerable(), test.State.LayoutResult!.Nodes);
        Assert.Equal(routes.AsEnumerable(), test.State.RoutingResult!.Routes);
        await test.NameAsync(first, "Accepted");
        await test.NameAsync(first, null);
        Assert.Empty(test.Lines(first));
        Assert.Null(test.Name(first));
        await test.UndoAsync();
        Assert.Equal("Accepted", test.Name(first));
        await test.UndoAsync();
        Assert.Equal("Approved", test.Name(first));
        Assert.Equal("Rejected", test.Name(second));
        await test.RedoAsync();
        Assert.Equal("Accepted", test.Name(first));
        Assert.Equal(branches[0].SourceId, test.Flow(first).SourceId);
        Assert.Equal(branches[0].TargetId, test.Flow(first).TargetId);
    }

    [Theory]
    [InlineData("BPMN.Task")]
    [InlineData("BPMN.StartEvent")]
    [InlineData("BPMN.ExclusiveGateway")]
    public async Task NameAppliesToOrdinaryFlowSourcesAndPreservesExactText(string sourceType)
    {
        await using var test = await Fixture.CreateAsync();
        var flow = test.Snapshot.SemanticModel.Relationships.First(flow =>
            test.Snapshot.SemanticModel.GetScope(flow.SourceId).Id == test.State.ActiveScopeId &&
            test.Snapshot.SemanticModel.Elements.Single(element => element.Id == flow.SourceId).TypeId.Value == sourceType);
        foreach (var text in new[] { "  Zażółć gęślą jaźń 日本語 🙂  ", "<script>alert('x')</script>",
                     string.Join(" ", Enumerable.Repeat("Long branch description", 12)), string.Empty, "   " })
        {
            await test.NameAsync(flow.Id, text);
            Assert.Equal(text, test.Name(flow.Id));
            if (string.IsNullOrWhiteSpace(text))
            {
                Assert.Empty(test.Lines(flow.Id));
            }
            else
            {
                Assert.NotEmpty(test.Lines(flow.Id));
                test.AssertAutomatic(flow.Id);
            }
            await test.UndoAsync();
            Assert.Null(test.Name(flow.Id));
            Assert.Empty(test.Lines(flow.Id));
        }
    }

    [Fact]
    public async Task NameAndManualMidpointMoveRenameResetHaveIndependentExactHistory()
    {
        await using var test = await Fixture.CreateAsync();
        var flow = BpmnDemoPipeline.ThirdSequenceFlowId;
        var initial = test.Snapshot;
        await test.NameAsync(flow, "Approved");
        var automatic = test.LabelBounds(flow);
        await test.ExecuteAsync(new MoveLabelCommand(test.Id, test.Revision,
            test.Visual(flow).Id, ConnectorLabelPlacement.Default));
        var manual = test.LabelBounds(flow);
        Assert.NotEqual(automatic, manual);
        Assert.Equal(ConnectorLabelPlacement.Default, test.Manual(flow));
        await test.NameAsync(flow, "Accepted");
        await test.ExecuteAsync(new MoveLabelCommand(test.Id, test.Revision, test.Visual(flow).Id, null));
        Assert.Null(test.Manual(flow));
        test.AssertAutomatic(flow);
        await test.UndoAsync();
        Assert.Equal(ConnectorLabelPlacement.Default, test.Manual(flow));
        await test.UndoAsync();
        Assert.Equal("Approved", test.Name(flow));
        Assert.Equal(manual, test.LabelBounds(flow));
        await test.UndoAsync();
        Assert.Null(test.Manual(flow));
        Assert.Equal(automatic, test.LabelBounds(flow));
        await test.UndoAsync();
        Assert.Null(test.Name(flow));
        Assert.Equal(initial.SemanticModel.Relationships.AsEnumerable(), test.Snapshot.SemanticModel.Relationships);
        Assert.Equal(initial.VisualModel.VisualStates.AsEnumerable(), test.Snapshot.VisualModel.VisualStates);
        for (var index = 0; index < 4; index++)
        {
            await test.RedoAsync();
        }
        Assert.Equal("Accepted", test.Name(flow));
        Assert.Null(test.Manual(flow));
        test.AssertAutomatic(flow);
    }

    [Fact]
    public async Task SourceAndTargetMoveResizeAnchorRedistributionAndRouteEditsResolveCurrentPath()
    {
        await using var test = await Fixture.CreateAsync();
        var flow = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.NameAsync(flow, "Approved");
        foreach (var id in new[] { test.Flow(flow).SourceId, test.Flow(flow).TargetId })
        {
            var visual = test.Visual(id);
            var node = test.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == visual.Id &&
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(item.Origin.ProjectedObjectId!, "node"));
            await test.ExecuteAsync(new MoveVisualStateCommand(test.Id, test.Revision, visual.Id,
                node.Bounds.TopLeft + new VectorD(45, 65), VisualPlacementMode.Pinned));
            test.AssertAutomatic(flow);
            visual = test.Visual(id);
            await test.ExecuteAsync(new ResizeVisualStateCommand(test.Id, test.Revision, visual.Id,
                new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width + 24, visual.Size.Height + 24),
                VisualPlacementMode.Pinned));
            test.AssertAutomatic(flow);
        }

        var source = test.Visual(test.Flow(flow).SourceId);
        var existing = source.ConnectorAnchors.Single(anchor => anchor.Id == test.Visual(flow).SourceAnchorId);
        await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Id, test.Revision, source.Id,
            new ConnectorAnchorId("a11:redistributed"), existing.Side, ConnectorAnchorRole.Source, 0));
        test.AssertAutomatic(flow);
        await BpmnModelerTestComposition.SetRoutingTypeAsync(test.Session, test.Visual(flow).Id, ConnectorRoutingType.Manual);
        var route = test.Path(flow);
        foreach (var bends in new PointD[][]
                 {
                     [route[0], new(route[0].X + 70, route[0].Y), new(route[0].X + 70, route[^1].Y), route[^1]],
                     [route[0], new(route[0].X + 95, route[0].Y), new(route[0].X + 95, route[^1].Y), route[^1]],
                     [],
                 })
        {
            await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Id, test.Revision, test.Visual(flow).Id, bends));
            test.AssertAutomatic(flow);
            Assert.Equal("Approved", test.Name(flow));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconnectionAndDeletionPreserveNamesIdentitiesRoutesAndManualState(bool manual)
    {
        await using var test = await Fixture.CreateAsync();
        var id = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.NameAsync(id, "Zażółć — Approved");
        if (manual)
        {
            await test.ExecuteAsync(new MoveLabelCommand(test.Id, test.Revision, test.Visual(id).Id,
                ConnectorLabelPlacement.Default));
        }
        foreach (var kind in new[] { ConnectorEndpointKind.Source, ConnectorEndpointKind.Target })
        {
            var nextId = kind == ConnectorEndpointKind.Source ? BpmnDemoPipeline.TaskId : BpmnDemoPipeline.RejectedTaskId;
            var anchorId = new ConnectorAnchorId($"a11:reconnect:{kind}");
            await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Id, test.Revision, test.Visual(nextId).Id,
                anchorId, kind == ConnectorEndpointKind.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left,
                kind == ConnectorEndpointKind.Source ? ConnectorAnchorRole.Source : ConnectorAnchorRole.Target, 0));
            var old = test.Flow(id);
            var visual = test.Visual(id);
            await test.ExecuteAsync(new ReconnectBpmnSequenceFlowEndpointCommand(test.Id, test.Revision,
                id, visual.Id, kind, kind == ConnectorEndpointKind.Source ? old.SourceId : old.TargetId,
                (kind == ConnectorEndpointKind.Source ? visual.SourceAnchorId : visual.TargetAnchorId)!, nextId, anchorId));
            Assert.Equal("Zażółć — Approved", test.Name(id));
            Assert.Equal(manual ? ConnectorLabelPlacement.Default : null, test.Manual(id));
            if (!manual)
            {
                test.AssertAutomatic(id);
            }
        }

        var before = test.Snapshot;
        var relationship = test.Flow(id);
        var connector = test.Visual(id);
        await test.ExecuteAsync(new DeleteBpmnSequenceFlowCommand(test.Id, test.Revision, id, connector.Id,
            relationship.SourceId, relationship.TargetId, connector.SourceAnchorId!, connector.TargetAnchorId!));
        Assert.False(test.Snapshot.SemanticModel.TryGetRelationship(id, out _));
        Assert.False(test.Snapshot.VisualModel.TryGetVisualState(connector.Id, out _));
        Assert.Empty(test.Lines(id));
        await test.UndoAsync();
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), test.Snapshot.SemanticModel.Relationships);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), test.Snapshot.VisualModel.VisualStates);
        Assert.NotEmpty(test.Lines(id));
    }

    [Fact]
    public async Task PoolsUseFinalRoutesAndVisibilityCollapseNeverPersistLabelGeometry()
    {
        await using var test = await Fixture.CreateAsync();
        var id = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.NameAsync(id, "Approved");
        var pool = new SemanticElementId("a11:pool");
        await test.EnablePoolAsync(pool);
        var originalVisuals = test.Snapshot.VisualModel.VisualStates;
        var source = test.Flow(id).SourceId;
        var target = test.Flow(id).TargetId;
        await test.ExecuteAsync(new UnassignOrganizationalElementCommand(test.Id, test.Revision, target));
        test.AssertAutomatic(id); // Pool -> Unassigned
        await test.ExecuteAsync(new AssignOrganizationalElementCommand(test.Id, test.Revision, target, pool));
        test.AssertAutomatic(id); // same Pool
        var secondPool = new SemanticElementId("a11:pool-b");
        await test.ExecuteAsync(new CreateOrganizationalPoolCommand(test.Id, test.Revision, secondPool,
            test.State.ActiveScopeId, OrganizationalPoolCreationMode.Empty, "Other"));
        await test.ExecuteAsync(new AssignOrganizationalElementCommand(test.Id, test.Revision, target, secondPool));
        test.AssertAutomatic(id); // cross Pool
        var beforeView = test.Snapshot;
        foreach (var visible in new[] { false, true })
        {
            Assert.True((await test.Session.UpdateModelProfileViewStateAsync(
                test.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, visible))).Succeeded);
            await test.Session.WaitForIdleAsync();
            test.AssertAutomatic(id);
            Assert.Same(beforeView, test.Snapshot);
        }
        foreach (var collapsed in new[] { true, false })
        {
            Assert.True((await test.Session.UpdateModelProfileElementViewStateAsync(
                test.State.ModelProfileElementViewState.WithCollapsed(OrganizationalModelProfile.Id, pool, collapsed))).Succeeded);
            await test.Session.WaitForIdleAsync();
            Assert.Same(beforeView, test.Snapshot);
            Assert.Equal("Approved", test.Name(id));
        }
        test.AssertAutomatic(id);
        Assert.Equal(originalVisuals.AsEnumerable(), test.Snapshot.VisualModel.VisualStates);
    }

    [Fact]
    public async Task RootChildNestedNamesAndManualPlacementRoundTripWithoutScopeLeakage()
    {
        await using var test = await Fixture.CreateAsync();
        var root = test.State.ActiveScopeId;
        var rootFlow = BpmnDemoPipeline.ThirdSequenceFlowId;
        var childFlow = BpmnDemoPipeline.ProcessOrderFirstFlowId;
        await test.NameAsync(rootFlow, "Approved");
        await test.NameAsync(childFlow, "Child — Żółć");
        Assert.Empty(test.Lines(childFlow));
        await test.NavigateAsync(BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.Empty(test.Lines(rootFlow));
        test.AssertAutomatic(childFlow);
        var nestedScope = new DocumentScopeId("a11:nested-scope");
        await test.ExecuteAsync(new CreateBpmnSubProcessCommand(test.Id, test.Revision,
            new SemanticElementId("a11:nested"), new VisualStateId("a11:nested:visual"),
            test.State.ActiveScopeId, nestedScope, new PointD(500, 300), new SizeD(120, 80),
            "NESTED", "Nested", VisualPlacementMode.Pinned));
        await test.NavigateAsync(nestedScope);
        var nodeA = new SemanticElementId("a11:task-a");
        var nodeB = new SemanticElementId("a11:task-b");
        foreach (var (id, x) in new[] { (nodeA, 100d), (nodeB, 400d) })
        {
            await test.ExecuteAsync(new CreateBpmnTaskCommand(test.Id, test.Revision, id,
                new VisualStateId(id.Value + ":visual"), new PointD(x, 100), new SizeD(120, 80),
                id.Value, id.Value, 1, VisualPlacementMode.Pinned, targetScopeId: nestedScope));
        }
        var sourceAnchor = new ConnectorAnchorId("a11:nested:source");
        var targetAnchor = new ConnectorAnchorId("a11:nested:target");
        await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Id, test.Revision, test.Visual(nodeA).Id,
            sourceAnchor, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
        await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Id, test.Revision, test.Visual(nodeB).Id,
            targetAnchor, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
        var nestedFlow = new SemanticElementId("a11:nested:flow");
        await test.ExecuteAsync(new CreateBpmnSequenceFlowCommand(test.Id, test.Revision,
            nestedFlow, new VisualStateId("a11:nested:flow:visual"), nodeA, nodeB, sourceAnchor, targetAnchor));
        Assert.Null(test.Name(nestedFlow));
        await test.NameAsync(nestedFlow, "Nested 🙂 <script>text</script>");
        test.AssertAutomatic(nestedFlow);
        Assert.Empty(test.Lines(childFlow));
        await test.ExecuteAsync(new MoveLabelCommand(test.Id, test.Revision, test.Visual(nestedFlow).Id,
            new ConnectorLabelPlacement(0.2, new VectorD(10, -30))));
        await test.EnablePoolAsync(new SemanticElementId("a11:nested:pool"));
        var bytes = NativeDocumentSerializer.Export(test.Snapshot);
        var imported = NativeDocumentSerializer.Import(bytes.ToArray());
        Assert.True(imported.Succeeded, Diagnostics(imported.Diagnostics));
        Assert.Equal(2, NativeDocumentSerializer.FormatVersion);
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());
        Assert.Equal(test.Snapshot.SemanticModel.Relationships.AsEnumerable(), imported.Document!.SemanticModel.Relationships);
        Assert.Equal(test.Snapshot.VisualModel.VisualStates.AsEnumerable(), imported.Document.VisualModel.VisualStates);
        await test.NavigateAsync(root);
        Assert.Empty(test.Lines(nestedFlow));
        test.AssertAutomatic(rootFlow);
        await test.NavigateAsync(nestedScope);
        Assert.Equal("Nested 🙂 <script>text</script>", test.Name(nestedFlow));
        Assert.NotNull(test.Manual(nestedFlow));
    }

    [Fact]
    public async Task PublishFreezesFinalBranchLabelsAndKeepsTokenGraphIndependentOfText()
    {
        await using var test = await Fixture.CreateAsync();
        await test.ExecuteAsync(new UpdateDocumentPublicationCommand(test.Id, test.Revision,
            "a11-branches", "A1.1 branches", "Branch label regression"));
        var flow = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.NameAsync(flow, "Approved <script>alert('x')</script> Żółć");
        var pool = new SemanticElementId("a11:publish:pool");
        await test.EnablePoolAsync(pool);
        await test.ExecuteAsync(new UnassignOrganizationalElementCommand(test.Id, test.Revision, test.Flow(flow).TargetId));
        var captured = await test.Session.CapturePresentationAsync(
            test.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false),
            ModelProfileElementViewStateSnapshot.Empty);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        var capture = captured.Capture!;
        var builder = new PublishedProcessPackageBuilder(new BpmnPublishedTokenRoleClassifier(), new BpmnPublishedNodeDataMapper());
        var result = builder.Build(capture);
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        var package = result.Package!;
        var lines = capture.Scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId == flow).ToArray();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var published = Assert.Single(package.Snapshot.Presentation.Items, item => item.Id == line.Id.Value);
            Assert.Equal(line.Geometry.Content, published.Content);
            Assert.Equal(line.Transform.OffsetX, published.Transform.OffsetX);
            Assert.Equal(line.Transform.OffsetY, published.Transform.OffsetY);
            Assert.Equal(line.Geometry.Bounds.Width, published.GeometryBounds.Width);
            Assert.Equal(line.Geometry.Bounds.Height, published.GeometryBounds.Height);
        }
        Assert.DoesNotContain(package.Snapshot.Presentation.Items, item => string.IsNullOrEmpty(item.Content) &&
            item.GeometryKind == (int)Canvas2DSceneGeometryKind.Text);
        using var archive = new ZipArchive(new MemoryStream(package.Archive.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(["index.html", "process.json", "process.data.js", "inceptus.publish.js", "styles.css"],
            archive.Entries.Select(static entry => entry.FullName).ToArray());
        using var reader = new StreamReader(archive.GetEntry("process.data.js")!.Open(), Encoding.UTF8);
        Assert.DoesNotContain("<script>", await reader.ReadToEndAsync(), StringComparison.Ordinal);
        var graph = package.Snapshot.TokenGraph;
        await test.NameAsync(flow, "A completely different description");
        var renamedCapture = await test.Session.CapturePresentationAsync(
            test.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false),
            ModelProfileElementViewStateSnapshot.Empty);
        var renamed = builder.Build(renamedCapture.Capture!);
        Assert.True(renamed.Succeeded, Diagnostics(renamed.Diagnostics));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(graph),
            System.Text.Json.JsonSerializer.Serialize(renamed.Package!.Snapshot.TokenGraph));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerDragStartsAtFinalAutomaticPositionAndUndoRestoresAbsence(bool crossPool)
    {
        await using var test = await Fixture.CreateAsync();
        var flow = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.NameAsync(flow, "Approved");
        if (crossPool)
        {
            var pool = new SemanticElementId("a11:drag:pool");
            await test.EnablePoolAsync(pool);
            await test.ExecuteAsync(new UnassignOrganizationalElementCommand(
                test.Id, test.Revision, test.Flow(flow).TargetId));
        }
        await using var controller = new Canvas2DInteractionController(test.Session);
        var bounds = test.LabelBounds(flow);
        var start = new PointD(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));
        PointD Css(PointD point) => test.State.CurrentScene!.ViewportTransform.TransformPoint(point);
        await controller.PointerActivatedAsync(Css(start));
        Assert.Equal(test.Visual(flow).Id, Assert.Single(test.State.EditorState.Selection));
        var before = test.Snapshot;
        var route = test.Path(flow);
        var history = test.State.HistoryStatus.EntryCount;
        var sceneBeforePan = test.State.CurrentScene!;
        Assert.True((await test.Session.PanViewportAsync(new VectorD(37.5, -18.25))).Succeeded);
        foreach (var item in sceneBeforePan.Items.Where(item => item.Layer != Canvas2DSceneLayer.Background))
        {
            Assert.Same(item, test.State.CurrentScene!.Items.Single(current => current.Id == item.Id));
        }
        var end = start + new VectorD(23d, 31d);
        await controller.PointerPressedAsync(new Canvas2DPointerInput(91, Css(start), buttons: 1));
        var moving = await controller.PointerMovedAsync(new Canvas2DPointerInput(91, Css(end), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, moving.Status);
        Assert.Same(before, test.Snapshot);
        var result = await controller.PointerReleasedAsync(new Canvas2DPointerInput(91, Css(end)));
        Assert.Equal(Canvas2DInteractionStatus.Committed, result.Status);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(history + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(before.Revision.Increment(), test.Revision);
        Assert.Equal("Approved", test.Name(flow));
        Assert.Equal(route, test.Path(flow));
        var moved = test.LabelBounds(flow);
        Assert.Equal(end.X, moved.X + (moved.Width / 2d), 8);
        Assert.Equal(end.Y, moved.Y + (moved.Height / 2d), 8);
        var manual = Assert.IsType<ConnectorLabelPlacement>(test.Manual(flow));
        await test.UndoAsync();
        test.AssertAutomatic(flow);
        Assert.Equal(bounds, test.LabelBounds(flow));
        await test.RedoAsync();
        Assert.Equal(manual, test.Manual(flow));
    }

    private static string Diagnostics(IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static item => $"{item.Code}: {item.Message}"));

    private sealed class Fixture(DocumentCanvasComposition composition, Canvas2DRenderer renderer, EditingSession session) : IAsyncDisposable
    {
        internal EditingSession Session => session;
        internal DocumentSnapshot Snapshot => composition.Document.CaptureSnapshot();
        internal EditingSessionState State => session.CaptureState();
        internal DocumentId Id => Snapshot.DocumentId;
        internal DocumentRevision Revision => Snapshot.Revision;
        internal SemanticRelationshipSnapshot Flow(SemanticElementId id) => Snapshot.SemanticModel.Relationships.Single(item => item.Id == id);
        internal VisualStateSnapshot Visual(SemanticElementId id) => Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == id);
        internal string? Name(SemanticElementId id) => Flow(id).Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) ? name.TextValue : null;
        internal ConnectorLabelPlacement? Manual(SemanticElementId id) => ConnectorLabelPlacement.TryRead(Visual(id).Properties, out var value) ? value : null;
        internal Canvas2DSceneItem[] Lines(SemanticElementId id) => State.CurrentScene!.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label && item.Origin.SemanticElementId == id).ToArray();
        internal RectD LabelBounds(SemanticElementId id) => Canvas2DConnectorLabelResolver.PresentedBounds(Lines(id));
        internal PointD[] Path(SemanticElementId id)
        {
            var edge = State.ProjectedGraph!.Edges.Single(item => item.Source.SemanticElementId == id);
            var connector = State.CurrentScene!.Items.Single(item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
            return Canvas2DConnectorPathMetadata.Resolve(connector).Select(connector.Transform.TransformPoint).ToArray();
        }
        internal void AssertAutomatic(SemanticElementId id)
        {
            Assert.Null(Manual(id));
            var label = Assert.Single(State.ProjectedGraph!.Labels, item => item.Source.SemanticElementId == id);
            Assert.NotNull(label.ConnectorPlacement);
            var bounds = LabelBounds(id);
            var anchor = Canvas2DConnectorLabelResolver.Resolve(label, Path(id), null, bounds.Size);
            Assert.Equal(anchor.X, bounds.X + (bounds.Width / 2), 8);
            Assert.Equal(anchor.Y, bounds.Y + (bounds.Height / 2), 8);
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0);
        }
        internal async Task ExecuteAsync(ICommand command)
        {
            var revision = Revision;
            var history = State.HistoryStatus.EntryCount;
            var truncatesRedo = State.HistoryStatus.CanRedo;
            var result = await session.ExecuteAsync(command);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            await session.WaitForIdleAsync();
            Assert.Equal(EditingSessionStatus.Ready, State.Status);
            Assert.Equal(revision.Increment(), Revision);
            if (command is UpdateConnectionRouteCommand)
            {
                Assert.Equal(history, State.HistoryStatus.EntryCount);
            }
            else if (!truncatesRedo)
            {
                Assert.Equal(history + 1, State.HistoryStatus.EntryCount);
            }
        }
        internal Task NameAsync(SemanticElementId id, string? text) => ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(Id, Revision, id, text));
        internal async Task UndoAsync()
        {
            Assert.True((await session.UndoAsync()).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(EditingSessionStatus.Ready, State.Status);
        }
        internal async Task RedoAsync()
        {
            Assert.True((await session.RedoAsync()).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(EditingSessionStatus.Ready, State.Status);
        }
        internal async Task NavigateAsync(DocumentScopeId scope)
        {
            Assert.True((await session.NavigateToScopeAsync(scope)).Succeeded);
            await session.WaitForIdleAsync();
        }
        internal async Task EnablePoolAsync(SemanticElementId pool)
        {
            await ExecuteAsync(new SetModelProfileAvailabilityCommand(Id, Revision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            await ExecuteAsync(new CreateOrganizationalPoolCommand(Id, Revision, pool,
                State.ActiveScopeId, OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Operations"));
        }
        internal static async Task<Fixture> CreateAsync()
        {
            var composition = await BpmnModelerTestComposition.CreateDemoAsync();
            var renderer = new Canvas2DRenderer(new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
                new Canvas2DRendererConfiguration(fontResources:
                    [new Canvas2DFontResource("org.dejavu.DejaVuSans", "2.37", "DejaVu Sans", "fonts/DejaVuSans-2.37.ttf", 400, TextFontStyle.Normal)],
                    defaultFontFamily: "DejaVu Sans"));
            Assert.True((await renderer.InitializeAsync("a11", new Canvas2DSurfaceSize(1400, 900, 1))).Succeeded);
            composition = BpmnModelerTestComposition.WithDocument(composition,
                await BpmnModelerTestComposition.PrepareFreshDocumentAsync(composition.Document, composition.Configuration, renderer));
            var attached = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            return new Fixture(composition, renderer, attached.Session!);
        }
        public async ValueTask DisposeAsync()
        {
            await session.DisposeAsync();
            await renderer.DisposeAsync();
        }
    }
}
