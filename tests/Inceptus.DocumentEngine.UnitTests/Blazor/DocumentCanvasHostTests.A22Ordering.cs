using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.ContextMenus;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("x1", false)]
    [InlineData("x2", false)]
    [InlineData("x3", false)]
    [InlineData("x1", true)]
    [InlineData("x2", true)]
    [InlineData("x3", true)]
    [InlineData("a", true)]
    [InlineData("y1", false)]
    public async Task A22OrderingTopLevelMovesAtomicBlocksWithExactGeometryAndHistory(string selected, bool down)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("ordering", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var direction = down ? OrganizationalPoolMoveDirection.Down : OrganizationalPoolMoveDirection.Up;
        var result = await session.ExecuteAsync(new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision, new(selected), direction));
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        string[] expected = selected == "y1" ? ["a", "x1", "x2", "x3", "y1", "y2", "b", "c"]
            : selected == "a" || !down ? ["x1", "x2", "x3", "a", "b", "y1", "y2", "c"]
            : ["a", "b", "x1", "x2", "x3", "y1", "y2", "c"];
        AssertOrdering(before, after, expected);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(OrderingRegions(before).Single(region => region.ContainerSemanticElementId!.Value == "c"),
            OrderingRegions(after).Single(region => region.ContainerSemanticElementId!.Value == "c"));
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Participant X");
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Participant Y");
        await host.UndoAsync();
        AssertOrderingVisuals(before, document.CaptureSnapshot());
        await host.RedoAsync();
        AssertOrderingVisuals(after, document.CaptureSnapshot());
        // Inverse operations do not accumulate offsets, even when the swapped blocks have different heights.
        for (var repeat = 0; repeat < 2; repeat++)
        {
            Assert.True((await session.ExecuteAsync(new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision,
                new(selected), down ? OrganizationalPoolMoveDirection.Up : OrganizationalPoolMoveDirection.Down))).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(OrderingRegions(before), OrderingRegions(document.CaptureSnapshot()));
            Assert.True((await session.ExecuteAsync(new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision,
                new(selected), direction))).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(OrderingRegions(after), OrderingRegions(document.CaptureSnapshot()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22OrderingLaneMovesKeepEnvelopeAndUnrelatedRegionsAndReopenExactly(bool down)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("lane-ordering", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var label = Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Participant X");
        var direction = down ? OrganizationalPoolMoveDirection.Down : OrganizationalPoolMoveDirection.Up;
        Assert.True((await session.ExecuteAsync(new MoveOrganizationalLaneCommand(document.DocumentId, document.Revision,
            new("x2"), direction))).IsCommitted);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        AssertOrdering(before, after, down ? ["a", "x1", "x3", "x2", "b", "y1", "y2", "c"]
            : ["a", "x2", "x1", "x3", "b", "y1", "y2", "c"]);
        var movedLabel = Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Participant X");
        Assert.Equal(label.Id, movedLabel.Id);
        Assert.Equal(label.Bounds, movedLabel.Bounds);
        Assert.Equal(label.Geometry.Content, movedLabel.Geometry.Content);
        Assert.Equal(label.Origin.SemanticElementId, movedLabel.Origin.SemanticElementId);
        Assert.Equal(OrderingRegions(before).Where(region => !region.ContainerSemanticElementId!.Value.StartsWith('x')),
            OrderingRegions(after).Where(region => !region.ContainerSemanticElementId!.Value.StartsWith('x')));
        await host.UndoAsync();
        AssertOrderingVisuals(before, document.CaptureSnapshot());
        await host.RedoAsync();
        AssertOrderingVisuals(after, document.CaptureSnapshot());
        var bytes = NativeDocumentSerializer.Export(after);
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = CreatePropertiesUxHost(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("reopened-ordering", "container");
        Assert.Equal(after, AttachedDocument(Session(reopened)).CaptureSnapshot());
        for (var repeat = 0; repeat < 2; repeat++)
        {
            Assert.True((await session.ExecuteAsync(new MoveOrganizationalLaneCommand(document.DocumentId, document.Revision,
                new("x2"), down ? OrganizationalPoolMoveDirection.Up : OrganizationalPoolMoveDirection.Down))).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(OrderingRegions(before), OrderingRegions(document.CaptureSnapshot()));
            Assert.True((await session.ExecuteAsync(new MoveOrganizationalLaneCommand(document.DocumentId, document.Revision,
                new("x2"), direction))).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(OrderingRegions(after), OrderingRegions(document.CaptureSnapshot()));
        }
    }

    [Theory]
    [InlineData("x1", false, true)]
    [InlineData("x3", true, true)]
    [InlineData("a", true, true)]
    [InlineData("a", false, false)]
    [InlineData("c", true, false)]
    public async Task A22OrderingBoundariesDoNotCommitOrInvalidate(string pool, bool down, bool lane)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("ordering-boundary", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var state = session.CaptureState();
        ICommand command = lane ? new MoveOrganizationalLaneCommand(before.DocumentId, before.Revision, new(pool),
            down ? OrganizationalPoolMoveDirection.Down : OrganizationalPoolMoveDirection.Up)
            : new MoveOrganizationalPoolCommand(before.DocumentId, before.Revision, new(pool),
                down ? OrganizationalPoolMoveDirection.Down : OrganizationalPoolMoveDirection.Up);
        Assert.False((await session.ExecuteAsync(command)).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Same(state.CurrentScene, session.CaptureState().CurrentScene);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22OrderingRemovesLargeHistoricalGapAndKeepsGroupAtomic(bool down)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync(historicalGap: 700));
        await host.InitializeAsync("ordering-gap", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        Assert.True(OrderingRegions(before)[1].ContentBounds!.Value.Top - OrderingRegions(before)[0].ContentBounds!.Value.Bottom > 600);
        Assert.True((await session.ExecuteAsync(new MoveOrganizationalPoolCommand(before.DocumentId, before.Revision,
            new("x2"), down ? OrganizationalPoolMoveDirection.Down : OrganizationalPoolMoveDirection.Up))).IsCommitted);
        await session.WaitForIdleAsync();
        var after = AttachedDocument(session).CaptureSnapshot();
        AssertOrdering(before, after, down ? ["a", "b", "x1", "x2", "x3", "y1", "y2", "c"]
            : ["x1", "x2", "x3", "a", "b", "y1", "y2", "c"]);
        Assert.Equal(OrderingRegions(before)[^1].ContentBounds!.Value.Bottom - 700,
            OrderingRegions(after)[^1].ContentBounds!.Value.Bottom);
        await host.UndoAsync();
        AssertOrderingVisuals(before, AttachedDocument(session).CaptureSnapshot());
        await host.RedoAsync();
        AssertOrderingVisuals(after, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task A22OrderingContextMenuSeparatesBlockAndLaneActions()
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("ordering-menu", "container");
        await OpenOrganizationalPoolContextAsync(host, new("x2"));
        var actions = host.CaptureState().ContextMenu!.SemanticCommandActions;
        Assert.Contains(actions, action => action.Id == OrganizationalPoolActions.MoveUpId);
        Assert.Contains(actions, action => action.Id == OrganizationalPoolActions.MoveDownId);
        Assert.Contains(actions, action => action.Id == OrganizationalPoolActions.MoveLaneUpId);
        Assert.Contains(actions, action => action.Id == OrganizationalPoolActions.MoveLaneDownId);
        Assert.True((await host.ExecuteSemanticSceneCommandContextActionAsync(OrganizationalPoolActions.MoveLaneUpId))!.IsCommitted);
        Assert.Equal<string>(["a", "x2", "x1", "x3", "b", "y1", "y2", "c"],
            OrderingRegions(AttachedDocument(Session(host)).CaptureSnapshot()).Select(region => region.ContainerSemanticElementId!.Value));
        await OpenOrganizationalPoolContextAsync(host, new("x2"));
        Assert.DoesNotContain(host.CaptureState().ContextMenu!.SemanticCommandActions, action => action.Id == OrganizationalPoolActions.MoveLaneUpId);
        Assert.True((await host.ExecuteSemanticSceneCommandContextActionAsync(OrganizationalPoolActions.MoveUpId))!.IsCommitted);
        Assert.Equal<string>(["x2", "x1", "x3", "a", "b", "y1", "y2", "c"],
            OrderingRegions(AttachedDocument(Session(host)).CaptureSnapshot()).Select(region => region.ContainerSemanticElementId!.Value));
        await OpenOrganizationalPoolContextAsync(host, new("a"));
        Assert.DoesNotContain(host.CaptureState().ContextMenu!.SemanticCommandActions,
            action => action.Id == OrganizationalPoolActions.MoveLaneUpId || action.Id == OrganizationalPoolActions.MoveLaneDownId);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0.3d)]
    public async Task A22OrderingUsesAuthoredPositionsInsteadOfStalePresentationOrderAtAnyZoom(double zoom)
    {
        var fixture = await OrderingFixtureAsync();
        var visuals = fixture.VisualModel;
        var reversed = new DocumentSnapshot(fixture.SemanticModel, new VisualModelSnapshot(fixture.DocumentId, fixture.Revision,
            visuals.VisualStates, visuals.ProfileElementPresentations.Select(entry => new ModelProfileElementPresentationSnapshot(
                entry.ProfileId, entry.SemanticElementId, 100 - entry.Order)), visuals.RoutingScopes), fixture.Metadata);
        await using var host = CreatePropertiesUxHost(reversed);
        await host.InitializeAsync("ordering-authored", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, default));
        await session.WaitForIdleAsync();
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
        Assert.True((await session.ExecuteAsync(new MoveOrganizationalPoolCommand(before.DocumentId, before.Revision,
            new("x2"), OrganizationalPoolMoveDirection.Down))).IsCommitted);
        await session.WaitForIdleAsync();
        AssertOrdering(before, AttachedDocument(session).CaptureSnapshot(), ["a", "b", "x1", "x2", "x3", "y1", "y2", "c"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22OrderingEqualAuthoredPositionsUseStableIdentity(bool lane)
    {
        var fixture = await OrderingFixtureAsync();
        var scope = fixture.VisualModel.RoutingScopes!.Value.Single();
        var geometry = scope.Geometry;
        var first = OrderingRegions(fixture).Single(region => region.ContainerSemanticElementId!.Value == (lane ? "x1" : "a"));
        var regions = geometry.Regions.Select(region => region.ContainerSemanticElementId?.Value == (lane ? "x2" : "b")
            ? new SpatialRegionGeometrySnapshot(region.Id, region.ProfileId, region.ContainerSemanticElementId, region.ExpandedHeight,
                first.LocalToScopeTransform, new RectD(region.ContentBounds!.Value.X, first.ContentBounds!.Value.Top,
                    region.ContentBounds.Value.Width, region.ExpandedHeight)) : region);
        var tied = new DocumentSnapshot(fixture.SemanticModel, new VisualModelSnapshot(fixture.DocumentId, fixture.Revision,
            fixture.VisualModel.VisualStates, fixture.VisualModel.ProfileElementPresentations,
            [new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion,
                geometry.LayoutAlgorithmId, geometry.Configuration, geometry.TextConfiguration, geometry.Contributors,
                geometry.Nodes, regions, geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors)]), fixture.Metadata);
        ICommand command = lane ? new MoveOrganizationalLaneCommand(tied.DocumentId, tied.Revision, new("x1"), OrganizationalPoolMoveDirection.Down)
            : new MoveOrganizationalPoolCommand(tied.DocumentId, tied.Revision, new("a"), OrganizationalPoolMoveDirection.Down);
        var registration = OrganizationalPluginRegistration.Create(new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode));
        var result = await registration.CommandHandlers.Single(entry => entry.TypeId == command.TypeId).Handler.HandleAsync(command, tied, default);
        Assert.True(result.Succeeded);
        AssertOrdering(tied, result.ProposedDocument!, lane ? ["a", "x2", "x1", "x3", "b", "y1", "y2", "c"]
            : ["b", "a", "x1", "x2", "x3", "y1", "y2", "c"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22OrderingHistoryOwnsOnlyChangedOrderAndVerticalOffsets(bool lane)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("ordering-history", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        ICommand command = lane ? new MoveOrganizationalLaneCommand(before.DocumentId, before.Revision, new("x2"), OrganizationalPoolMoveDirection.Up)
            : new MoveOrganizationalPoolCommand(before.DocumentId, before.Revision, new("x2"), OrganizationalPoolMoveDirection.Up);
        Assert.True((await session.ExecuteAsync(command)).IsCommitted);
        await session.WaitForIdleAsync();
        var committed = AttachedDocument(session).CaptureSnapshot();
        var registration = OrganizationalPluginRegistration.Create(new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode));
        var history = registration.HistoryPolicies.Single(policy => policy.TypeId == command.TypeId).Policy.Prepare(command, before, committed);
        var scope = committed.VisualModel.RoutingScopes!.Value.Single();
        var geometry = scope.Geometry;
        var laterRegions = geometry.Regions.Select(region => region.ContainerSemanticElementId?.Value is "x2" or "c"
            ? new SpatialRegionGeometrySnapshot(region.Id, region.ProfileId, region.ContainerSemanticElementId, region.ExpandedHeight + 20,
                region.LocalToScopeTransform, new RectD(region.ContentBounds!.Value.X, region.ContentBounds.Value.Y,
                    region.ContentBounds.Value.Width, region.ExpandedHeight + 20)) : region).ToArray();
        var laterNodes = committed.VisualModel.VisualStates.Select(visual => visual.SemanticElementId.Value == "c-task"
            ? new VisualStateSnapshot(visual.Id, visual.SemanticElementId, new(100, 50), visual.Size, visual.PlacementMode) : visual);
        var model = committed.SemanticModel;
        var renamed = model.Elements.Select(element => element.Id.Value is "participant-X" or "x2"
            ? new SemanticElementSnapshot(element.Id, element.TypeId, element.Properties.Where(property => property.Key != BpmnSemanticProperties.Name)
                .Append(new(BpmnSemanticProperties.Name, PropertyValue.FromText("Later name"))), element.AttachedToElementId, element.ContainmentKind) : element);
        var later = new DocumentSnapshot(new SemanticModelSnapshot(model.DocumentId, model.Revision, renamed, model.Relationships,
                model.NestedScopes, model.ScopeMemberships, model.ModelProfiles, model.ProfileAssignments),
            new VisualModelSnapshot(committed.DocumentId, committed.Revision, laterNodes, committed.VisualModel.ProfileElementPresentations,
                [new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion,
                    geometry.LayoutAlgorithmId, geometry.Configuration, geometry.TextConfiguration, geometry.Contributors,
                    geometry.Nodes, laterRegions, geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors)]), committed.Metadata);
        var undo = history.UndoFactory!.Create(later.DocumentId, later.Revision);
        var handler = registration.CommandHandlers.Single(entry => entry.TypeId == undo.TypeId).Handler;
        var result = await handler.HandleAsync(undo, later, default);
        Assert.True(result.Succeeded);
        var undone = result.ProposedDocument!;
        Assert.Equal(later.SemanticModel, undone.SemanticModel);
        Assert.Equal(later.VisualModel.VisualStates.AsEnumerable(), undone.VisualModel.VisualStates);
        Assert.Equal(laterRegions.Single(region => region.ContainerSemanticElementId?.Value == "c"),
            OrderingRegions(undone).Single(region => region.ContainerSemanticElementId?.Value == "c"));
        var x2 = OrderingRegions(undone).Single(region => region.ContainerSemanticElementId?.Value == "x2");
        Assert.Equal(OrderingRegions(before).Single(region => region.Id == x2.Id).ContentBounds!.Value.Top, x2.ContentBounds!.Value.Top);
        Assert.Equal(laterRegions.Single(region => region.Id == x2.Id).ExpandedHeight, x2.ExpandedHeight);
        var redone = await handler.HandleAsync(history.RedoFactory!.Create(undone.DocumentId, undone.Revision), undone, default);
        Assert.True(redone.Succeeded);
        Assert.Equal(later, redone.ProposedDocument);
    }

    private static void AssertOrdering(DocumentSnapshot before, DocumentSnapshot after, string[] expected)
    {
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
        var regions = OrderingRegions(after);
        Assert.Equal(expected, regions.Select(region => region.ContainerSemanticElementId!.Value));
        var old = OrderingRegions(before).ToDictionary(region => region.Id);
        foreach (var region in regions)
        {
            Assert.Equal(old[region.Id].ExpandedHeight, region.ExpandedHeight);
            Assert.Equal(old[region.Id].ContentBounds!.Value.Width, region.ContentBounds!.Value.Width);
            Assert.Equal(old[region.Id].LocalToScopeTransform!.Value.OffsetX, region.LocalToScopeTransform!.Value.OffsetX);
        }
        for (var index = 1; index < regions.Length; index++)
        {
            var previous = after.SemanticModel.Elements.Single(pool => pool.Id == regions[index - 1].ContainerSemanticElementId);
            var current = after.SemanticModel.Elements.Single(pool => pool.Id == regions[index].ContainerSemanticElementId);
            var owner = OrganizationalSemantics.GetParticipantId(previous);
            var gap = owner is not null && owner == OrganizationalSemantics.GetParticipantId(current) ? 0 : 40d;
            Assert.Equal(regions[index - 1].ContentBounds!.Value.Bottom + gap, regions[index].ContentBounds!.Value.Top);
        }
    }

    private static void AssertOrderingVisuals(DocumentSnapshot expected, DocumentSnapshot actual)
    {
        Assert.Equal(expected.VisualModel.VisualStates.AsEnumerable(), actual.VisualModel.VisualStates);
        Assert.Equal(expected.VisualModel.ProfileElementPresentations.AsEnumerable(), actual.VisualModel.ProfileElementPresentations);
        Assert.Equal(expected.VisualModel.RoutingScopes!.Value.AsEnumerable(), actual.VisualModel.RoutingScopes!.Value);
    }

    private static SpatialRegionGeometrySnapshot[] OrderingRegions(DocumentSnapshot document) =>
        document.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions.Where(region => region.ContainerSemanticElementId is not null)
            .OrderBy(region => region.ContentBounds!.Value.Top).ToArray();

    private static async Task<DocumentSnapshot> OrderingFixtureAsync(double historicalGap = 0)
    {
        var id = new DocumentId("a22:ordering");
        var revision = DocumentRevision.Zero;
        string[] names = ["a", "x1", "x2", "x3", "b", "y1", "y2", "c"];
        string[] participantNames = ["X", "Y"];
        var participants = participantNames.Select(name => new SemanticElementSnapshot(new("participant-" + name), BpmnSemanticTypes.Participant,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Participant " + name))], containmentKind: SemanticElementContainmentKind.Document));
        var pools = names.Select(name => new SemanticElementSnapshot(new(name), OrganizationalSemanticTypes.Pool,
            new[] { new KeyValuePair<string, PropertyValue>(OrganizationalSemanticProperties.Name, PropertyValue.FromText("Pool " + name)) }
                .Concat(name[0] is 'x' or 'y' ? [new(OrganizationalSemanticProperties.ParticipantId,
                    PropertyValue.FromText("participant-" + char.ToUpperInvariant(name[0])))] : []))).ToArray();
        var tasks = names.Select(name => new SemanticElementSnapshot(new(name + "-task"), BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(name + " task"))])).ToArray();
        var semantic = new SemanticModelSnapshot(id, revision, pools.Concat(tasks).Concat(participants),
            modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
            profileAssignments: names.Select(name => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, new(name + "-task"), new(name))));
        var visual = new VisualModelSnapshot(id, revision, tasks.Select(task => new VisualStateSnapshot(new(task.Id.Value + "-visual"), task.Id,
            new(80, 35), new(120, 80), VisualPlacementMode.Pinned)),
            pools.Select((pool, index) => new ModelProfileElementPresentationSnapshot(OrganizationalModelProfile.Id, pool.Id, index)));
        await using var host = CreatePropertiesUxHost(new(semantic, visual, new DocumentMetadataSnapshot(id, revision)));
        await host.InitializeAsync("ordering-fixture", "container");
        var prepared = AttachedDocument(Session(host)).CaptureSnapshot();
        var scope = prepared.VisualModel.RoutingScopes!.Value.Single();
        var geometry = scope.Geometry;
        var byPool = geometry.Regions.Where(region => region.ContainerSemanticElementId is not null).ToDictionary(region => region.ContainerSemanticElementId!.Value);
        var cursor = byPool["a"].ContentBounds!.Value.Top;
        var regions = new List<SpatialRegionGeometrySnapshot>();
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            var region = byPool[name];
            var bounds = region.ContentBounds!.Value;
            var height = 240d + index * 30d;
            if (name == "x1") cursor += historicalGap;
            regions.Add(new(region.Id, region.ProfileId, region.ContainerSemanticElementId, height,
                Matrix2D.CreateTranslation(region.LocalToScopeTransform!.Value.OffsetX,
                    region.LocalToScopeTransform.Value.OffsetY + cursor - bounds.Top), new RectD(bounds.X, cursor, bounds.Width, height)));
            cursor += height;
            if (name is not ("x1" or "x2" or "y1")) cursor += 40d;
        }
        var unassigned = geometry.Regions.Single(region => region.ContainerSemanticElementId is null);
        regions.Add(new(unassigned.Id, unassigned.ProfileId, null, unassigned.ExpandedHeight,
            Matrix2D.CreateTranslation(unassigned.LocalToScopeTransform!.Value.OffsetX,
                unassigned.LocalToScopeTransform.Value.OffsetY + cursor - unassigned.ContentBounds!.Value.Top),
            new RectD(unassigned.ContentBounds.Value.X, cursor, unassigned.ContentBounds.Value.Width, unassigned.ExpandedHeight)));
        return new(prepared.SemanticModel, new VisualModelSnapshot(id, prepared.Revision, prepared.VisualModel.VisualStates,
            prepared.VisualModel.ProfileElementPresentations, [new ScopeRoutingSnapshot(scope.ScopeId,
                new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion, geometry.LayoutAlgorithmId, geometry.Configuration,
                    geometry.TextConfiguration, geometry.Contributors, geometry.Nodes, regions, geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors)]), prepared.Metadata);
    }
}
