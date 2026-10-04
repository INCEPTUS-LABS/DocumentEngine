using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN101OrganizationalPoolIntegrationTests
{
    private static readonly VisualStateId IsolationSource = new("a1211:isolation:source");
    private static readonly VisualStateId IsolationTarget = new("a1211:isolation:target");
    private static readonly VisualStateId IsolationFlow = new("a1211:isolation:flow");

    [Theory]
    [InlineData("other", false, false)]
    [InlineData("unassigned", false, false)]
    [InlineData("same", false, false)]
    [InlineData("other", true, false)]
    [InlineData("same", true, false)]
    [InlineData("other", false, true)]
    [InlineData("unassigned-endpoints", false, false)]
    [InlineData("cross", false, false)]
    public async Task RoutingIsolationToolboxPlacementUsesAuthorizedDomainAndExactHistory(
        string scenario, bool coversEndpoint, bool guidance)
    {
        await using var fixture = await DragFixture.CreateRoutingIsolationAsync(
            cross: scenario == "cross", unassigned: scenario == "unassigned-endpoints");
        if (guidance)
        {
            await fixture.ExecuteAsync(state => new SetConnectorRoutingTypeCommand(
                state.DocumentId, state.DocumentRevision, IsolationFlow, ConnectorRoutingType.Manual));
            var path = fixture.SavedRoute(IsolationFlow).Path;
            await fixture.ExecuteAsync(state => new UpdateConnectionRouteCommand(
                state.DocumentId, state.DocumentRevision, IsolationFlow,
                [path[0], new(120, 160), new(320, 160), path[^1]]));
        }
        var beforeDocument = fixture.Document;
        var beforeRouting = fixture.State.RoutingResult!;
        var beforeDisplayed = PlacementConnectorPath(fixture, IsolationFlow);
        var beforeLayout = fixture.State.LayoutResult!;
        var pool = scenario switch
        {
            "same" or "unassigned-endpoints" => PoolAId,
            "unassigned" => null,
            _ => PoolBId,
        };
        var center = new PointD(coversEndpoint ? 340 : 220, guidance ? 150 : 100);

        if (scenario == "same" && coversEndpoint)
        {
            // A1.2.14 rejects an overlapping body before planning a persistent edit.
            // The other-domain case still exercises isolation for equal local geometry.
            var before = fixture.State;
            fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
            var region = PlacementRegion(fixture, pool);
            var rejected = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session,
                fixture.Css(region.MapLocalToScene(center)));
            Assert.False(rejected.IsCommitted);
            Assert.Null(rejected.CreatedVisualStateId);
            Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "TOOLBOX_PLACEMENT_BLOCKED");
            Assert.Same(beforeDocument, fixture.Document);
            Assert.Equal(before.HistoryStatus, fixture.State.HistoryStatus);
            Assert.Same(beforeRouting, fixture.State.RoutingResult);
            Assert.Equal(beforeDisplayed, PlacementConnectorPath(fixture, IsolationFlow));
            return;
        }

        var added = await AddStableToolboxElementAsync(fixture, pool, "task", center);

        AssertUnrelatedVisualsUnchanged(beforeDocument, fixture.Document);
        foreach (var node in beforeLayout.Nodes)
        {
            Assert.Equal(node, fixture.State.LayoutResult!.Nodes.Single(current => current.ProjectedObjectId == node.ProjectedObjectId));
        }
        var afterRouting = fixture.State.RoutingResult!;
        var afterDocument = fixture.Document;
        var afterDisplayed = PlacementConnectorPath(fixture, IsolationFlow);
        if (scenario == "same")
        {
            Assert.Empty(afterRouting.NoRouteEdgeIds);
            var region = PlacementRegion(fixture, PoolAId);
            Assert.Equal(new PointD[] { new(76, 100), new(150, 100), new(150, 50),
                new(290, 50), new(290, 100), new(360, 100) }.Select(region.MapLocalToScene),
                CanonicalIsolationPath(fixture));
            Assert.False(IsolationPathCrossesBody(afterDisplayed, fixture.Node(added.Id).Bounds));
        }
        else if (scenario == "cross")
        {
            Assert.Empty(afterRouting.NoRouteEdgeIds);
            Assert.False(IsolationPathCrossesBody(afterDisplayed, fixture.Node(added.Id).Bounds));
            if (!IsolationPathCrossesBody(beforeDisplayed, fixture.Node(added.Id).Bounds))
                Assert.Equal(beforeDisplayed, afterDisplayed);
        }
        else
        {
            Assert.Equal(beforeRouting.Computation, afterRouting.Computation);
            AssertIsolationDiagnostics(beforeRouting.Diagnostics, afterRouting.Diagnostics);
            Assert.Equal(beforeDisplayed, afterDisplayed);
            Assert.Empty(afterRouting.NoRouteEdgeIds);
            var otherBody = fixture.Node(added.Id).Bounds;
            Assert.All(afterDisplayed, point => Assert.False(otherBody.Contains(point)));
        }

        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(beforeDocument, fixture.Document);
        // Undo removes the new obstacle but does not optimise an already valid saved detour.
        Assert.Equal(afterRouting.Computation, fixture.State.RoutingResult!.Computation);
        AssertIsolationDiagnostics(afterRouting.Diagnostics, fixture.State.RoutingResult.Diagnostics);
        Assert.Equal(afterDisplayed, PlacementConnectorPath(fixture, IsolationFlow));
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(afterDocument, fixture.Document);
        Assert.Equal(afterRouting.Computation, fixture.State.RoutingResult!.Computation);
        AssertIsolationDiagnostics(afterRouting.Diagnostics, fixture.State.RoutingResult.Diagnostics);
        Assert.Equal(afterDisplayed, PlacementConnectorPath(fixture, IsolationFlow));
    }

    [Fact]
    public async Task RoutingIsolationPersistentMoveInSecondPoolPreservesUnrelatedRouteAndHistory()
    {
        await using var fixture = await DragFixture.CreateRoutingIsolationAsync();
        var added = await AddStableToolboxElementAsync(fixture, PoolBId, "task", new PointD(220, 140));
        var beforeDocument = fixture.Document;
        var before = fixture.State;
        var path = PlacementConnectorPath(fixture, IsolationFlow);

        await fixture.DragNodeAsync(added.Id, new VectorD(0, -40));

        var afterDocument = fixture.Document;
        Assert.Equal(new PointD(160, 60), fixture.Visual(added.Id).Position);
        Assert.Equal(PoolBId, AssignedPool(fixture.Document, added.SemanticElementId));
        Assert.Equal(before.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(before.RoutingResult!.Computation, fixture.State.RoutingResult!.Computation);
        Assert.Equal(path, PlacementConnectorPath(fixture, IsolationFlow));
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(beforeDocument, fixture.Document);
        Assert.Equal(before.RoutingResult.Computation, fixture.State.RoutingResult!.Computation);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(afterDocument, fixture.Document);
        Assert.Equal(before.RoutingResult.Computation, fixture.State.RoutingResult!.Computation);
        Assert.Equal(path, PlacementConnectorPath(fixture, IsolationFlow));
    }

    private static PointD[] CanonicalIsolationPath(DragFixture fixture)
    {
        var route = Assert.Single(fixture.State.RoutingResult!.Routes);
        return [.. route.Path];
    }

    private static bool IsolationPathCrossesBody(PointD[] path, RectD body) =>
        path.Zip(path.Skip(1)).Any(segment => segment.First.X == segment.Second.X
            ? segment.First.X > body.Left && segment.First.X < body.Right &&
              Math.Max(segment.First.Y, segment.Second.Y) > body.Top && Math.Min(segment.First.Y, segment.Second.Y) < body.Bottom
            : segment.First.Y == segment.Second.Y && segment.First.Y > body.Top && segment.First.Y < body.Bottom &&
              Math.Max(segment.First.X, segment.Second.X) > body.Left && Math.Min(segment.First.X, segment.Second.X) < body.Right);

    [Fact]
    public async Task RoutingIsolationUsesAttachmentInheritanceAndKeepsUnassignedDistinct()
    {
        await using var fixture = await DragFixture.CreateRoutingIsolationAsync();
        var owner = await AddStableToolboxElementAsync(fixture, PoolBId, "task", new PointD(220, 100));
        var boundaryId = new VisualStateId("a1211:isolation:boundary");
        await fixture.ExecuteAsync(state => new CreateBpmnTimerBoundaryEventCommand(
            state.DocumentId, state.DocumentRevision, new SemanticElementId("a1211:isolation:boundary"),
            boundaryId, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.5,
            new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height),
            "Timer", "PT5M", targetScopeId: state.ActiveScopeId));
        var unassigned = await AddStableToolboxElementAsync(fixture, null, "task", new PointD(220, 100));
        var state = fixture.State;
        var document = fixture.Document;
        var provider = OrganizationalPluginRegistration.Create(
            new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode)).RoutingInputPreparer;
        var prepared = Assert.IsType<PreparedRoutingInput>(provider.Prepare(document, state.ActiveScopeId,
            state.ProjectedGraph!, state.LayoutResult!, CancellationToken.None));
        var domain = prepared.EdgeDomains[Assert.Single(state.ProjectedGraph!.Edges).Id];
        var includedVisuals = state.ProjectedGraph.Nodes.Where(node => domain.Contains(node.Id))
            .Select(node => node.Source.VisualStateId).ToArray();
        Assert.Equal(new[] { IsolationSource, IsolationTarget }.OrderBy(id => id.Value, StringComparer.Ordinal),
            includedVisuals.OrderBy(id => id!.Value, StringComparer.Ordinal));
        Assert.DoesNotContain(owner.Id, includedVisuals);
        Assert.DoesNotContain(boundaryId, includedVisuals);
        Assert.DoesNotContain(unassigned.Id, includedVisuals);
        // Independently probe a B-local projected edge, so treating the attachment as
        // Unassigned would fail even though both groups are excluded from A's domain.
        var graph = state.ProjectedGraph;
        var ownerNode = graph.Nodes.Single(node => node.Source.VisualStateId == owner.Id);
        var boundaryNode = graph.Nodes.Single(node => node.Source.VisualStateId == boundaryId);
        var localEdge = new ProjectedEdge(graph.Edges[0].Source, ownerNode.Id, boundaryNode.Id);
        var localGraph = new ProjectedGraph(graph.DocumentId, graph.SourceRevision,
            graph.Nodes, [localEdge], graph.Groups, graph.Ports, graph.Labels);
        var localInput = Assert.IsType<PreparedRoutingInput>(provider.Prepare(document, state.ActiveScopeId,
            localGraph, state.LayoutResult!, CancellationToken.None));
        Assert.Equal(new[] { ownerNode.Id, boundaryNode.Id }.OrderBy(id => id.Value, StringComparer.Ordinal),
            localInput.EdgeDomains[localEdge.Id].NodeIds);
        Assert.Same(document, fixture.Document);
        Assert.Equal(state.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(new PointD[] { new(76, 100), new(360, 100) }.Select(PlacementRegion(fixture, PoolAId).MapLocalToScene),
            CanonicalIsolationPath(fixture));
    }

    private static void AssertIsolationDiagnostics(IEnumerable<Diagnostic> expected, IEnumerable<Diagnostic> actual) =>
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual));

    private sealed partial class DragFixture
    {
        internal static async Task<DragFixture> CreateRoutingIsolationAsync(bool cross = false, bool unassigned = false)
        {
            var composition = await new BpmnModelerCompositionFactory().CreateAsync();
            var attached = await EditingSession.AttachAsync(
                composition.Document, await CreateRendererAsync(), composition.Configuration);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            var fixture = new DragFixture(Assert.IsType<EditingSession>(attached.Session), composition);
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
                state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            foreach (var pool in new[] { PoolAId, PoolBId })
            {
                await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                    state.DocumentId, state.DocumentRevision, pool, state.ActiveScopeId,
                    pool == PoolAId ? OrganizationalPoolCreationMode.AdoptEligibleUnassigned : OrganizationalPoolCreationMode.Empty,
                    pool == PoolAId ? "A" : "B"));
            }
            var sourceId = new SemanticElementId("a1211:isolation:source");
            var targetId = new SemanticElementId("a1211:isolation:target");
            await fixture.ExecuteAsync(state => new CreateBpmnStartEventCommand(
                state.DocumentId, state.DocumentRevision, sourceId, IsolationSource,
                new PointD(40, 82), new SizeD(36, 36), VisualPlacementMode.Pinned,
                targetScopeId: state.ActiveScopeId));
            await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(
                state.DocumentId, state.DocumentRevision, targetId, IsolationTarget,
                new PointD(360, 60), new SizeD(120, 80), "T", "Task", 1,
                VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
            if (!unassigned)
            {
                await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
                    state.DocumentId, state.DocumentRevision, sourceId, PoolAId));
                await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
                    state.DocumentId, state.DocumentRevision, targetId, cross ? PoolBId : PoolAId));
            }
            var sourceAnchor = new ConnectorAnchorId("a1211:isolation:source");
            var targetAnchor = new ConnectorAnchorId("a1211:isolation:target");
            await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(
                state.DocumentId, state.DocumentRevision, IsolationSource, sourceAnchor,
                ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
            await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(
                state.DocumentId, state.DocumentRevision, IsolationTarget, targetAnchor,
                ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
            await fixture.ExecuteAsync(state => new CreateBpmnSequenceFlowCommand(
                state.DocumentId, state.DocumentRevision, new SemanticElementId("a1211:isolation:flow"),
                IsolationFlow, sourceId, targetId, sourceAnchor, targetAnchor));
            return fixture;
        }
    }
}
