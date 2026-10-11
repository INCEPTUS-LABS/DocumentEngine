using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A23AutoArrangeCanExpandOnlyPoolPaddingWithExactHistory()
    {
        await using var host = A23Host(A22PoolSnapshot());
        await host.InitializeAsync("arrange-pool-only", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var scope = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single();
        var region = scope.Geometry.Regions.Single(region => region.ContainerSemanticElementId?.Value == "sales");
        Assert.True((await session.ExecuteAsync(new Inceptus.DocumentEngine.Organizational.Commands.SetOrganizationalRegionExpandedHeightCommand(
            document.DocumentId, document.Revision, scope.ScopeId, region.Id, 144))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var plan = BpmnAutoArrangePlanner.Plan(before, [scope.ScopeId]);
        Assert.Empty(plan.ChangedVisualStateIds);
        Assert.False(plan.IsNoChange);
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var after = document.CaptureSnapshot();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
        Assert.True(after.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions.Single(item => item.Id == region.Id).ExpandedHeight > 144);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Fact]
    public async Task A23AutoArrangeExpandsSmallLanesAndTranslatesFollowingBlocksWithoutReordering()
    {
        var initial = A23Snapshot(A23Edges("branches"));
        var pools = A22PoolSnapshot();
        var semantic = new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            initial.SemanticModel.Elements.Concat(pools.SemanticModel.Elements.Where(element =>
                OrganizationalSemantics.IsPool(element) || element.TypeId == BpmnSemanticTypes.Participant)),
            initial.SemanticModel.Relationships, modelProfiles: pools.SemanticModel.ModelProfiles,
            profileAssignments: initial.SemanticModel.Elements.Select(element => new ModelProfileElementAssignmentSnapshot(
                OrganizationalModelProfile.Id, element.Id, new(element.Id.Value is "merge" or "end" ? "warehouse" : "sales"))));
        var visual = new VisualModelSnapshot(initial.DocumentId, initial.Revision,
            initial.VisualModel.VisualStates.Select(state => initial.SemanticModel.TryGetElement(state.SemanticElementId, out _)
                ? new VisualStateSnapshot(state.Id, state.SemanticElementId, new(40, 30), state.Size, state.PlacementMode,
                    state.Route, state.Properties, state.ConnectorAnchors, state.SourceAnchorId, state.TargetAnchorId, state.BoundaryAttachment)
                : state), pools.VisualModel.ProfileElementPresentations);
        await using var host = A23Host(new(semantic, visual, initial.Metadata));
        await host.InitializeAsync("arrange-expansion", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var result = await host.AutoArrangeAsync();
        Assert.NotNull(result);
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var after = document.CaptureSnapshot();
        var old = before.VisualModel.RoutingScopes!.Value.Single().Geometry;
        var current = after.VisualModel.RoutingScopes!.Value.Single().Geometry;
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(old.Regions.OrderBy(region => region.ContentBounds!.Value.Top).Select(region => region.Id),
            current.Regions.OrderBy(region => region.ContentBounds!.Value.Top).Select(region => region.Id));
        var ordered = current.Regions.Where(region => region.ContainerSemanticElementId is not null)
            .OrderBy(region => region.ContentBounds!.Value.Top).ToArray();
        Assert.Equal(ordered[0].ContentBounds!.Value.Bottom, ordered[1].ContentBounds!.Value.Top);
        Assert.Equal(ordered[1].ContentBounds!.Value.Bottom + 40, ordered[2].ContentBounds!.Value.Top);
        Assert.Equal(ordered[2].ContentBounds!.Value.Bottom + 40, ordered[3].ContentBounds!.Value.Top);
        Assert.True(current.SpatialWidths.Single().OuterWidth > old.SpatialWidths.Single().OuterWidth);
        Assert.True(ordered[0].ExpandedHeight > old.Regions.Single(region => region.Id == ordered[0].Id).ExpandedHeight);
        Assert.All(current.Nodes, node =>
        {
            var region = current.Regions.Single(region => region.Id == node.RegionId);
            Assert.True(node.LocalBounds.Bottom + 32 <= region.ExpandedHeight);
            Assert.True(node.LocalBounds.Right + 70 <= current.SpatialWidths.Single().OuterWidth);
        });
        Assert.All(current.Captions, caption =>
        {
            var node = current.Nodes.Single(node => node.VisualStateId == caption.OwnerVisualStateId);
            var region = current.Regions.Single(region => region.Id == node.RegionId);
            Assert.True(caption.ContentBounds.Bottom + 64 <= region.ExpandedHeight);
            Assert.True(caption.ContentBounds.Right + 102 <= current.SpatialWidths.Single().OuterWidth);
        });
        await host.AutoArrangeAsync();
        Assert.Same(after, document.CaptureSnapshot());
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData("BPMN.TimerBoundaryEvent")]
    [InlineData("BPMN.MessageBoundaryEvent")]
    public async Task A23AutoArrangeBoundaryKeepsAuthoredPlacementAndFollowsOwner(string type)
    {
        await using var host = A23Host(CreatePropertiesUxSnapshot(type));
        await host.InitializeAsync("arrange-boundary", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var eventVisual = before.VisualModel.VisualStates.Single(state => state.BoundaryAttachment is not null);
        var result = await host.AutoArrangeAsync();
        Assert.NotNull(result);
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var after = document.CaptureSnapshot();
        var current = after.VisualModel.VisualStates.Single(state => state.Id == eventVisual.Id);
        var ownerId = after.SemanticModel.Elements.Single(element => element.Id == eventVisual.SemanticElementId).AttachedToElementId;
        var owner = after.VisualModel.VisualStates.Single(state => state.SemanticElementId == ownerId);
        Assert.Equal(eventVisual.BoundaryAttachment, current.BoundaryAttachment);
        Assert.Equal(current.BoundaryAttachment!.ResolveBounds(new(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height), current.Size).TopLeft,
            current.Position);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        await host.AutoArrangeAsync();
        Assert.Same(after, document.CaptureSnapshot());
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(after).AsMemory());
        Assert.True(opened.Succeeded);
        A23PresentationEqual(after, opened.Document!.CaptureSnapshot());
    }

    [Fact]
    public async Task A23AutoArrangeSubProcessIsOneNodeAndHiddenScopeIsExactlyRetained()
    {
        var initial = A23Snapshot(A23Edges("linear"));
        var nested = new DocumentScopeId("a23:nested");
        var child = new SemanticElementSnapshot(new("child"), BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Hidden task"))]);
        var semantic = new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            initial.SemanticModel.Elements.Select(element => element.Id.Value == "a"
                ? new SemanticElementSnapshot(element.Id, BpmnSemanticTypes.SubProcess, element.Properties) : element).Append(child),
            initial.SemanticModel.Relationships, [new(nested, initial.SemanticModel.RootScopeId, new("a"))], [new(child.Id, nested)]);
        var visual = new VisualModelSnapshot(initial.DocumentId, initial.Revision, initial.VisualModel.VisualStates.Append(
            new(new("v-child"), child.Id, new(500, 200), new(120, 80), VisualPlacementMode.Pinned)));
        await using var host = A23Host(new(semantic, visual, initial.Metadata));
        await host.InitializeAsync("arrange-nested", "container");
        Assert.True(host.CaptureState().IsInitialized, string.Join("; ", host.CaptureState().HostDiagnostics.Select(diagnostic => diagnostic.Message)));
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var after = document.CaptureSnapshot();
        Assert.Equal(before.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == nested),
            after.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == nested));
        Assert.Equal(before.VisualModel.VisualStates.Single(state => state.SemanticElementId == child.Id),
            after.VisualModel.VisualStates.Single(state => state.SemanticElementId == child.Id));
        Assert.Equal(before.VisualModel.VisualStates.Single(state => state.SemanticElementId.Value == "a").Size,
            after.VisualModel.VisualStates.Single(state => state.SemanticElementId.Value == "a").Size);
    }

    [Fact]
    public async Task A23AutoArrangeOrdersOutgoingAndIncomingPointsByRemoteTracks()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("no-merge")));
        await host.InitializeAsync("arrange-anchors", "container");
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var document = AttachedDocument(Session(host)).CaptureSnapshot();
        foreach (var ownerId in new[] { "split", "d" })
        {
            var visual = document.VisualModel.VisualStates.Single(state => state.SemanticElementId.Value == ownerId);
            var flows = document.SemanticModel.Relationships.Where(flow => ownerId == "split" ? flow.SourceId == visual.SemanticElementId : flow.TargetId == visual.SemanticElementId);
            var pairs = flows.Select(flow =>
            {
                var connector = document.VisualModel.VisualStates.Single(state => state.SemanticElementId == flow.Id);
                var anchor = visual.ConnectorAnchors.Single(anchor => anchor.Id == (ownerId == "split" ? connector.SourceAnchorId : connector.TargetAnchorId));
                var remote = A23Bounds(document, (ownerId == "split" ? flow.TargetId : flow.SourceId).Value);
                return (anchor, remote);
            }).ToArray();
            foreach (var side in pairs.GroupBy(pair => pair.anchor.Side))
                Assert.Equal(side.OrderBy(pair => pair.anchor.NormalizedPosition).Select(pair => pair.anchor.Id),
                    side.OrderBy(pair => pair.anchor.Side is ConnectorAnchorSide.Top or ConnectorAnchorSide.Bottom ? pair.remote.X : pair.remote.Y)
                        .ThenBy(pair => pair.anchor.Id.Value, StringComparer.Ordinal).Select(pair => pair.anchor.Id));
        }
    }
}
