using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Fact]
    public async Task RecreatedScopeIdentityCannotImplicitlyReviveDeletedGeometryHistory()
    {
        await using var fixture = await Fixture.CreatePopulatedDemoPoolFixtureAsync();
        var rootScope = fixture.State.ActiveScopeId;
        var ownerId = new SemanticElementId("test:reused-scope:owner");
        var ownerVisualId = new VisualStateId("test:reused-scope:owner:visual");
        var childId = new DocumentScopeId("test:reused-scope:child");
        var taskId = new SemanticElementId("test:reused-scope:task");
        var taskVisualId = new VisualStateId("test:reused-scope:task:visual");

        async Task CreateOwnerAsync() => await fixture.ExecuteAsync(state => new CreateBpmnSubProcessCommand(
            state.DocumentId, state.DocumentRevision, ownerId, ownerVisualId,
            rootScope, childId, new PointD(1100d, 300d), new SizeD(120d, 80d),
            "REUSED_SCOPE", "Reused scope", VisualPlacementMode.Pinned));

        async Task CreateTaskAsync(double x) => await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(
            state.DocumentId, state.DocumentRevision, taskId, taskVisualId,
            new PointD(x, 160d), new SizeD(120d, 80d), "REUSED_TASK", "Reused task", 3100,
            VisualPlacementMode.Pinned, targetScopeId: childId));

        await CreateOwnerAsync();
        Assert.True((await fixture.Session.NavigateToScopeAsync(childId)).Succeeded);
        await CreateTaskAsync(180d);
        var oldGraph = fixture.State.ProjectedGraph!;
        var deletedGeometry = Assert.Single(fixture.State.LayoutResult!.Nodes);
        Assert.True((await fixture.Session.NavigateToScopeAsync(rootScope)).Succeeded);
        await fixture.ExecuteAsync(state => new DeleteBpmnFlowNodeCommand(
            state.DocumentId, state.DocumentRevision, ownerId, ownerVisualId));
        Assert.False(fixture.Document.SemanticModel.TryGetScope(childId, out _));

        // Reuse explicit identities through fresh commands, not History restoration.
        await CreateOwnerAsync();
        Assert.True((await fixture.Session.NavigateToScopeAsync(childId)).Succeeded);
        await CreateTaskAsync(220d);
        await fixture.ExecuteAsync(state => new MoveVisualStateCommand(
            state.DocumentId, state.DocumentRevision, taskVisualId,
            new PointD(180d, 160d), VisualPlacementMode.Pinned));

        Assert.Equal(oldGraph.Nodes.AsEnumerable(), fixture.State.ProjectedGraph!.Nodes.AsEnumerable());
        var currentGeometry = Assert.Single(fixture.State.LayoutResult!.Nodes);
        Assert.Equal(deletedGeometry.Bounds, currentGeometry.Bounds);
        Assert.NotSame(deletedGeometry, currentGeometry);
        Assert.Equal(new PointD(180d, 160d), fixture.Visual(taskVisualId).Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedoChildCreationAfterScopeRemovalUsesExactOriginalGeometryTwice(bool compound)
    {
        await using var fixture = await Fixture.CreatePopulatedDemoPoolFixtureAsync();
        await fixture.DragAsync(fixture.NodePoint(BpmnDemoPipeline.TaskVisualId), new VectorD(40d, 20d));
        var initial = fixture.Document;
        var rootScope = fixture.State.ActiveScopeId;
        var initialHistory = fixture.State.HistoryStatus.EntryCount;
        var ownerId = new SemanticElementId("test:pinned-child-history:owner");
        var ownerVisualId = new VisualStateId("test:pinned-child-history:owner:visual");
        var childId = new DocumentScopeId("test:pinned-child-history:scope");
        await fixture.ExecuteAsync(state => new CreateBpmnSubProcessCommand(
            state.DocumentId, state.DocumentRevision, ownerId, ownerVisualId,
            rootScope, childId, new PointD(1100d, 300d), new SizeD(120d, 80d),
            "CHILD_HISTORY", "Child history", VisualPlacementMode.Pinned));
        var ownerGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        Assert.True((await fixture.Session.NavigateToScopeAsync(childId)).Succeeded);
        await fixture.ExecuteAsync(state =>
        {
            CreateBpmnTaskCommand Task(string suffix, double x, long number) => new(
                state.DocumentId, state.DocumentRevision,
                new SemanticElementId($"test:pinned-child-history:{suffix}"),
                new VisualStateId($"test:pinned-child-history:{suffix}:visual"),
                new PointD(x, 160d), new SizeD(120d, 80d), suffix, suffix, number,
                VisualPlacementMode.Pinned, targetScopeId: childId);
            var first = Task("first", 180d, 3001);
            return compound
                ? new CompoundDocumentCommand(state.DocumentId, state.DocumentRevision,
                    [first, Task("second", 380d, 3002)])
                : first;
        });
        var childGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        var completeDocument = fixture.Document;
        Assert.Equal(compound ? 2 : 1, childGeometry.Count);
        Assert.True((await fixture.Session.NavigateToScopeAsync(rootScope)).Succeeded);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Assert.True((await fixture.Session.UndoAsync()).IsApplied);
            Assert.Equal(childId, fixture.State.ActiveScopeId);
            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            fixture.AssertReady();
            Assert.Empty(fixture.State.LayoutResult!.Nodes);
            Assert.True((await fixture.Session.UndoAsync()).IsApplied);
            Assert.Equal(rootScope, fixture.State.ActiveScopeId);
            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            fixture.AssertReady();
            Assert.False(fixture.Document.SemanticModel.TryGetScope(childId, out _));
            Assert.Equal(initial.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
            Assert.False((await fixture.Session.NavigateToScopeAsync(childId)).Succeeded);

            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            fixture.AssertReady();
            foreach (var (id, geometry) in ownerGeometry)
            {
                Assert.Same(geometry, fixture.State.LayoutResult!.Nodes.Single(node => node.ProjectedObjectId == id));
            }
            Assert.True((await fixture.Session.RedoAsync()).IsApplied);
            Assert.Equal(childId, fixture.State.ActiveScopeId);
            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            fixture.AssertReady();
            var restoredGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
            foreach (var (id, geometry) in childGeometry)
            {
                Assert.Same(geometry, restoredGeometry[id]);
            }
            Assert.Equal(completeDocument.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
            Assert.Equal(completeDocument.SemanticModel.NestedScopes.AsEnumerable(), fixture.Document.SemanticModel.NestedScopes.AsEnumerable());
            Assert.True((await fixture.Session.RedoAsync()).IsApplied);
            Assert.Equal(rootScope, fixture.State.ActiveScopeId);
            Assert.Equal(initialHistory + 4, fixture.State.HistoryStatus.EntryCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoPinnedSubProcessAfterChildNavigationRetainsExactEditedParentGeometry(bool compound)
    {
        await using var fixture = await Fixture.CreatePopulatedDemoPoolFixtureAsync();
        await fixture.DragAsync(fixture.NodePoint(BpmnDemoPipeline.TaskVisualId), new VectorD(40d, 20d));
        Assert.Contains(fixture.State.ProjectedGraph!.Nodes, node =>
            fixture.Visual(node.Source.VisualStateId!).PlacementMode == VisualPlacementMode.Manual);
        var before = fixture.Document;
        var rootScope = fixture.State.ActiveScopeId;
        var parentGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        var parentDisplayed = fixture.State.ProjectedGraph!.Nodes.ToDictionary(
            node => node.Source.VisualStateId!, node => fixture.Node(node.Source.VisualStateId!).Bounds);
        var elementId = new SemanticElementId("test:pinned-subprocess-recovery:element");
        var visualId = new VisualStateId("test:pinned-subprocess-recovery:visual");
        var childId = new DocumentScopeId("test:pinned-subprocess-recovery:child");

        await fixture.ExecuteAsync(state =>
        {
            var creation = new CreateBpmnSubProcessCommand(state.DocumentId, state.DocumentRevision,
                elementId, visualId, rootScope, childId, new PointD(1100d, 300d), new SizeD(120d, 80d),
                "RECOVERY", "Recovery", VisualPlacementMode.Pinned);
            return compound
                ? new CompoundDocumentCommand(state.DocumentId, state.DocumentRevision,
                    [creation, new AssignOrganizationalElementCommand(state.DocumentId,
                        state.DocumentRevision, elementId, Fixture.PoolA)])
                : creation;
        });
        var created = fixture.Document;
        var createdGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        foreach (var (id, geometry) in parentGeometry)
        {
            Assert.Same(geometry, createdGeometry[id]);
        }

        Assert.True((await fixture.Session.NavigateToScopeAsync(childId)).Succeeded);
        Assert.Equal(childId, fixture.State.ActiveScopeId);
        // Scope navigation is itself a transient global History entry. Undo returns
        // to the parent before the following Undo removes the created SubProcess.
        Assert.True((await fixture.Session.UndoAsync()).IsApplied);
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        Assert.Equal(rootScope, fixture.State.ActiveScopeId);
        Assert.Equal(created.Revision, fixture.Document.Revision);
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        Assert.Equal(rootScope, fixture.State.ActiveScopeId);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(before.SemanticModel.NestedScopes.AsEnumerable(), fixture.Document.SemanticModel.NestedScopes.AsEnumerable());
        var restoredGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        var changed = parentGeometry.Where(pair => pair.Value.Bounds != restoredGeometry[pair.Key].Bounds).ToArray();
        Assert.True(changed.Length == 0, "Scope recovery changed retained parent nodes: " +
            string.Join("; ", changed.Select(pair => $"{pair.Key}: {pair.Value.Bounds} -> {restoredGeometry[pair.Key].Bounds}")));
        foreach (var (id, bounds) in parentDisplayed)
        {
            Assert.Equal(bounds, fixture.Node(id).Bounds);
        }

        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        Assert.Equal(rootScope, fixture.State.ActiveScopeId);
        Assert.Equal(created.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(created.SemanticModel.NestedScopes.AsEnumerable(), fixture.Document.SemanticModel.NestedScopes.AsEnumerable());
        var redoneGeometry = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        foreach (var (id, geometry) in createdGeometry)
        {
            Assert.Same(geometry, redoneGeometry[id]);
        }

        var revisionBeforeNavigationRedo = fixture.Document.Revision;
        Assert.True((await fixture.Session.RedoAsync()).IsApplied);
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        Assert.Equal(childId, fixture.State.ActiveScopeId);
        Assert.Equal(revisionBeforeNavigationRedo, fixture.Document.Revision);
    }
}
