using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA22MessageFlowIntegrationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(null, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false, true)]
    public async Task ProductionHostMessageFlowPropertiesManualRouteAndLabelReopenWithExactOwnedState(bool? moveLane, bool appendPool, bool assignFromAbove = false, bool unassignPool = false)
    {
        var sample = await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var initial = sample.CaptureSnapshot();
        if (moveLane is not null)
        {
            var model = initial.SemanticModel;
            initial = new DocumentSnapshot(new SemanticModelSnapshot(model.DocumentId, model.Revision,
                model.Elements.Append(BpmnSemanticFactory.CreateTask(Id("review-next"), "NEXT", "Next review", 7)), model.Relationships,
                model.NestedScopes, model.ScopeMemberships, model.ModelProfiles, model.ProfileAssignments.Append(
                    new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, Id("review-next"), Id("supplier2")))),
                new VisualModelSnapshot(initial.DocumentId, initial.Revision, initial.VisualModel.VisualStates.Append(
                    new VisualStateSnapshot(Visual("review-next"), Id("review-next"), new(270, 25), new(140, 80), VisualPlacementMode.Pinned)),
                    initial.VisualModel.ProfileElementPresentations), initial.Metadata);
        }
        var composition = await new BpmnModelerCompositionFactory(initialDocument: initial).CreateAsync();
        await using var source = await HostHarness.CreateAsync(composition);
        var document = source.Composition.Document;
        var initialRegions = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        RectD InitialBounds(string name) => initialRegions.Single(region => region.ContainerSemanticElementId == Id(name)).ContentBounds!.Value;
        Assert.Equal(InitialBounds("sales").Bottom, InitialBounds("warehouse").Top);
        Assert.Equal(40d, InitialBounds("supplier").Top - InitialBounds("warehouse").Bottom);
        Assert.Equal(40d, InitialBounds("supplier2").Top - InitialBounds("supplier").Bottom);
        var sequenceTarget = moveLane is null ? "send" : "pack";
        foreach (var (name, role) in new[] { ("prepare", ConnectorAnchorRole.Source), ("send", ConnectorAnchorRole.Source),
                     (sequenceTarget, ConnectorAnchorRole.Target), ("receive", ConnectorAnchorRole.Target) })
            Assert.True((await source.Session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
                Visual(name), Anchor(name, role == ConnectorAnchorRole.Source ? "source" : "target"),
                role == ConnectorAnchorRole.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left, role, 0))).IsCommitted);
        var sequence = await source.Session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision,
            Id("sequence"), Visual("sequence"), Id("prepare"), Id(sequenceTarget), Anchor("prepare", "source"), Anchor(sequenceTarget, "target")));
        Assert.True(sequence.IsCommitted, string.Join("; ", sequence.Diagnostics.Select(diagnostic => diagnostic.Message)));
        if (moveLane is not null)
        {
            foreach (var (name, role) in new[] { ("review", ConnectorAnchorRole.Source), ("review-next", ConnectorAnchorRole.Target) })
                Assert.True((await source.Session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
                    Visual(name), Anchor(name, "unrelated"), role == ConnectorAnchorRole.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left, role, 0))).IsCommitted);
            Assert.True((await source.Session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision,
                Id("unrelated"), Visual("unrelated"), Id("review"), Id("review-next"), Anchor("review", "unrelated"), Anchor("review-next", "unrelated")))).IsCommitted);
        }
        await source.Session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var message = await source.Session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            Id("message"), Visual("message"), Id("send"), Id("receive"), Anchor("send", "source"), Anchor("receive", "target")));
        Assert.True(message.IsCommitted, string.Join("; ", message.Diagnostics.Select(diagnostic => diagnostic.Message)));
        await source.Session.WaitForIdleAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates.Where(visual => visual.Id != Visual("message")));
        Assert.Equal(before.VisualModel.EnumerateConnectorRoutingRecords(), document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().Where(record => record.VisualStateId != Visual("message")));

        await source.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [Visual("message")]));
        await source.Session.WaitForIdleAsync();
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await source.Host.OpenPropertiesAsync(Visual("message")));
        Assert.False(properties.CanEditType);
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = "manual" };
        Assert.Single(draft.DataFields).EditorValue = "Order dispatched";
        Assert.True(source.Host.UpdatePropertiesFormState(true, Visual("message"), draft.IsDirty));
        Assert.True((await source.Host.ApplyPropertiesAsync(draft)).Succeeded);
        Assert.True(source.Host.UpdatePropertiesFormState(false, null, false));
        var route = Assert.Single(document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords(), record => record.VisualStateId == Visual("message"));
        Assert.Equal(ConnectorRoutingType.Manual, route.RoutingType);
        Assert.True((await source.Session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId, document.Revision,
            Visual("message"), [route.Path[0], new PointD(710d, 280d), new PointD(760d, 280d), route.Path[^1]]))).IsCommitted);
        var placement = new ConnectorLabelPlacement(0.6d, new VectorD(16d, 24d));
        Assert.True((await source.Session.ExecuteAsync(new MoveLabelCommand(document.DocumentId, document.Revision,
            Visual("message"), placement))).IsCommitted);
        await source.Session.WaitForIdleAsync();
        if (moveLane is not null)
        {
            var beforeMove = document.CaptureSnapshot();
            ICommand move = moveLane.Value
                ? new MoveOrganizationalLaneCommand(document.DocumentId, document.Revision, Id("sales"), OrganizationalPoolMoveDirection.Down)
                : new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision, Id("warehouse"), OrganizationalPoolMoveDirection.Down);
            var history = source.Session.CaptureState().HistoryStatus.EntryCount;
            var moved = await source.Session.ExecuteAsync(move);
            Assert.True(moved.IsCommitted, string.Join("; ", moved.Diagnostics.Select(diagnostic => diagnostic.Message)));
            await source.Session.WaitForIdleAsync();
            var afterMove = document.CaptureSnapshot();
            Assert.Equal(history + 1, source.Session.CaptureState().HistoryStatus.EntryCount);
            Assert.Equal(beforeMove.SemanticModel.Elements.AsEnumerable(), afterMove.SemanticModel.Elements);
            Assert.Equal(beforeMove.SemanticModel.Relationships.AsEnumerable(), afterMove.SemanticModel.Relationships);
            Assert.Equal(beforeMove.SemanticModel.ProfileAssignments.AsEnumerable(), afterMove.SemanticModel.ProfileAssignments);
            Assert.Equal(beforeMove.VisualModel.VisualStates.AsEnumerable(), afterMove.VisualModel.VisualStates);
            var beforeRecords = beforeMove.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
            var afterRecords = afterMove.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
            Assert.Equal(beforeRecords.Select(record => record.VisualStateId), afterRecords.Select(record => record.VisualStateId));
            Assert.Equal(beforeRecords.Select(record => record.RoutingType), afterRecords.Select(record => record.RoutingType));
            Assert.Equal(beforeRecords.Single(record => record.VisualStateId == Visual("message")).ManualDefinition!.Value.AsEnumerable(),
                afterRecords.Single(record => record.VisualStateId == Visual("message")).ManualDefinition!.Value);
            Assert.All(afterRecords, record => Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome));
            Assert.Equal(beforeRecords.Single(record => record.VisualStateId == Visual("unrelated")),
                afterRecords.Single(record => record.VisualStateId == Visual("unrelated")));
            Assert.Single(source.Scene.Items, item => item.Geometry.Content == "Company A");
            await source.Host.UndoAsync();
            Assert.Equal(beforeMove.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
            await source.Host.RedoAsync();
            Assert.Equal(afterMove.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        }
        if (appendPool || unassignPool)
        {
            if (appendPool)
            {
                Assert.True((await source.Session.ExecuteAsync(new CreateOrganizationalPoolCommand(document.DocumentId, document.Revision,
                    Id("added"), document.CaptureSnapshot().SemanticModel.RootScopeId, OrganizationalPoolCreationMode.Empty))).IsCommitted);
                await source.Session.WaitForIdleAsync();
            }
            if (assignFromAbove)
            {
                // Supplier stays above the assigned Pool, matching the reported browser sequence.
                Assert.True((await source.Session.ExecuteAsync(new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision,
                    Id("sales"), OrganizationalPoolMoveDirection.Down))).IsCommitted);
                for (var index = 0; index < 2; index++)
                    Assert.True((await source.Session.ExecuteAsync(new MoveOrganizationalPoolCommand(document.DocumentId, document.Revision,
                        Id("added"), OrganizationalPoolMoveDirection.Up))).IsCommitted);
                foreach (var (name, role) in new[] { ("receive", ConnectorAnchorRole.Source), ("catch", ConnectorAnchorRole.Target) })
                    Assert.True((await source.Session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
                        Visual(name), Anchor(name, "prefix"), role == ConnectorAnchorRole.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left, role, 0))).IsCommitted);
                Assert.True((await source.Session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision,
                    Id("prefix"), Visual("prefix"), Id("receive"), Id("catch"), Anchor("receive", "prefix"), Anchor("catch", "prefix")))).IsCommitted);
                await source.Session.WaitForIdleAsync();
            }
            var beforeAssignment = document.CaptureSnapshot();
            var oldRegions = beforeAssignment.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
            var oldRecords = beforeAssignment.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
            Assert.True((await source.Session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
                Id(unassignPool ? "sales" : "added"), unassignPool ? null : Id("company-a")))).IsCommitted);
            await source.Session.WaitForIdleAsync();
            var assigned = document.CaptureSnapshot();
            var regions = assigned.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
            var offset = assignFromAbove ? -(oldRegions.Single(region => region.ContainerSemanticElementId == Id("added")).ExpandedHeight + 40d) : 0d;
            foreach (var name in new[] { "sales", "warehouse" })
            {
                var original = oldRegions.Single(region => region.ContainerSemanticElementId == Id(name));
                var current = regions.Single(region => region.ContainerSemanticElementId == Id(name));
                Assert.Equal(original.ContentBounds!.Value.Top + offset + (unassignPool && name == "warehouse" ? 40d : 0d), current.ContentBounds!.Value.Top);
                Assert.Equal(original.ExpandedHeight, current.ExpandedHeight);
            }
            if (assignFromAbove)
                Assert.Equal(regions.Single(region => region.ContainerSemanticElementId == Id("supplier")).ContentBounds!.Value.Bottom + 40d,
                    regions.Single(region => region.ContainerSemanticElementId == Id("sales")).ContentBounds!.Value.Top);
            if (appendPool)
                Assert.Equal(oldRegions.Single(region => region.ContainerSemanticElementId == Id("warehouse")).ContentBounds!.Value.Bottom + offset,
                    regions.Single(region => region.ContainerSemanticElementId == Id("added")).ContentBounds!.Value.Top);
            else
            {
                Assert.Equal(40d, regions.Single(region => region.ContainerSemanticElementId == Id("warehouse")).ContentBounds!.Value.Top -
                    regions.Single(region => region.ContainerSemanticElementId == Id("sales")).ContentBounds!.Value.Bottom);
                foreach (var name in new[] { "supplier", "supplier2" })
                    Assert.Equal(oldRegions.Single(region => region.ContainerSemanticElementId == Id(name)).ContentBounds!.Value.Top + 40d,
                        regions.Single(region => region.ContainerSemanticElementId == Id(name)).ContentBounds!.Value.Top);
            }
            Assert.Equal(beforeAssignment.VisualModel.VisualStates.AsEnumerable(), assigned.VisualModel.VisualStates);
            Assert.Equal(beforeAssignment.SemanticModel.Relationships.AsEnumerable(), assigned.SemanticModel.Relationships);
            Assert.Equal(beforeAssignment.SemanticModel.ProfileAssignments.AsEnumerable(), assigned.SemanticModel.ProfileAssignments);
            var records = assigned.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
            Assert.Equal(oldRecords.Select(record => record.VisualStateId), records.Select(record => record.VisualStateId));
            if (!assignFromAbove)
                Assert.Equal(oldRecords.Single(record => record.VisualStateId == Visual("sequence")),
                    records.Single(record => record.VisualStateId == Visual("sequence")));
            else
            {
                Assert.Equal(oldRecords.Single(record => record.VisualStateId == Visual("prefix")),
                    records.Single(record => record.VisualStateId == Visual("prefix")));
                Assert.Equal(oldRecords.Single(record => record.VisualStateId == Visual("sequence")).Path.Select(point => new PointD(point.X, point.Y + offset)),
                    records.Single(record => record.VisualStateId == Visual("sequence")).Path);
            }
            Assert.Equal(oldRecords.Single(record => record.VisualStateId == Visual("message")).ManualDefinition!.Value.AsEnumerable(),
                records.Single(record => record.VisualStateId == Visual("message")).ManualDefinition!.Value);
            Assert.All(records, record => Assert.Equal(ConnectorRoutingOutcome.Path, record.Outcome));
            await source.Host.UndoAsync();
            Assert.Equal(beforeAssignment.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
            await source.Host.RedoAsync();
            Assert.Equal(assigned.VisualModel.RoutingScopes!.Value.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        }
        var expected = document.CaptureSnapshot();
        var exported = await source.Host.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded, string.Join("; ", exported.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var imported = NativeDocumentSerializer.Import(exported.Payload.AsMemory(), composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var reopenedComposition = await new BpmnModelerCompositionFactory(initialDocument: imported.Document!.CaptureSnapshot()).CreateAsync();
        await using var reopened = await HostHarness.CreateAsync(reopenedComposition);
        Assert.IsType<ProcessScopePresentationContext>(reopened.State.PresentationContext);
        Assert.Equal(0, reopened.State.HistoryStatus.EntryCount);
        Assert.Equal(expected, reopened.Composition.Document.CaptureSnapshot());
        var flow = Assert.Single(reopened.Composition.Document.SemanticModel.Relationships,
            relationship => relationship.TypeId == BpmnSemanticTypes.MessageFlow);
        Assert.Equal("Order dispatched", flow.Properties[BpmnSemanticProperties.Name].TextValue);
        var line = Assert.Single(reopened.Scene.Items, item => item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == Visual("message") && item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            !item.Style.DashPattern.IsEmpty);
        Assert.Equal<double>([6d, 4d], line.Style.DashPattern);
        Assert.True(ConnectorLabelPlacement.TryRead(reopened.Composition.Document.VisualModel.VisualStates.Single(
            visual => visual.Id == Visual("message")).Properties, out var actualPlacement));
        Assert.Equal(placement, actualPlacement);
        Assert.Equal(exported.Payload.ToArray(), (await reopened.Host.ExportNativeDocumentAsync()).Payload.ToArray());
    }

    private static SemanticElementId Id(string name) => new("demo:pools:" + name);
    private static VisualStateId Visual(string name) => new("demo:pools:visual:" + name);
    private static ConnectorAnchorId Anchor(string name, string role) => new("demo:pools:anchor:" + name + ":" + role);
}
