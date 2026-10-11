using System.Text;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class PhaseA22MessageFlowDomainTests
{
    [Theory]
    [InlineData("BPMN.Participant", true, true)]
    [InlineData("BPMN.Task", true, true)]
    [InlineData("BPMN.UserTask", true, true)]
    [InlineData("BPMN.ManualTask", true, true)]
    [InlineData("BPMN.ServiceTask", true, true)]
    [InlineData("BPMN.SubProcess", true, true)]
    [InlineData("BPMN.SendTask", true, false)]
    [InlineData("BPMN.ReceiveTask", false, true)]
    [InlineData("BPMN.MessageThrowEvent", true, false)]
    [InlineData("BPMN.MessageCatchEvent", false, true)]
    [InlineData("BPMN.MessageBoundaryEvent", false, true)]
    [InlineData("BPMN.StartEvent", false, false)]
    [InlineData("BPMN.EndEvent", false, false)]
    [InlineData("BPMN.TimerCatchEvent", false, false)]
    [InlineData("BPMN.SignalCatchEvent", false, false)]
    [InlineData("BPMN.SignalThrowEvent", false, false)]
    [InlineData("BPMN.TimerBoundaryEvent", false, false)]
    [InlineData("BPMN.SignalBoundaryEvent", false, false)]
    [InlineData("BPMN.ExclusiveGateway", false, false)]
    [InlineData("BPMN.ParallelGateway", false, false)]
    [InlineData("BPMN.InclusiveGateway", false, false)]
    [InlineData("BPMN.EventBasedGateway", false, false)]
    [InlineData("BPMN.SequenceFlow", false, false)]
    [InlineData("BPMN.MessageFlow", false, false)]
    public void CentralAuthorityDefinesTheCompleteSupportedEndpointDirections(string type, bool source, bool target)
    {
        Assert.Equal(source, BpmnMessageFlowSemantics.CanBeSource(new SemanticTypeId(type)));
        Assert.Equal(target, BpmnMessageFlowSemantics.CanBeTarget(new SemanticTypeId(type)));
    }

    [Theory]
    [InlineData("a", "b")]
    [InlineData("pa", "pb")]
    [InlineData("pa", "b")]
    [InlineData("a", "pb")]
    [InlineData("throw", "catch")]
    [InlineData("throw", "boundary")]
    [InlineData("a", "subprocess")]
    public async Task CreatesCrossParticipantRelationshipsWithStableNodeAndProcessState(string source, string target)
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Snapshot;
        await fixture.CreateFlow(source, target);
        var after = fixture.Snapshot;
        var flow = Assert.Single(after.SemanticModel.Relationships, flow => flow.TypeId == BpmnSemanticTypes.MessageFlow);
        Assert.Equal(Id(source), flow.SourceId);
        Assert.Equal(Id(target), flow.TargetId);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.Where(visual => visual.SemanticElementId != flow.Id));
        Assert.Equal(before.SemanticModel.ScopeMemberships.AsEnumerable(), after.SemanticModel.ScopeMemberships);
        Assert.Empty(after.VisualModel.DocumentRoutingStates);
        Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords(), record => record.VisualStateId == Visual("message"));
        Assert.Empty(new BpmnMessageFlowValidationRule().Validate(new ModelValidationContext(after)));
        Assert.Equal(new BpmnStructuralValidationRule().Validate(new ModelValidationContext(before)).Select(issue => issue.Id),
            new BpmnStructuralValidationRule().Validate(new ModelValidationContext(after)).Select(issue => issue.Id));
        Assert.All(flow.Properties.Keys, key => Assert.DoesNotContain("Participant", key, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("a", "a2", BpmnMessageFlowSemantics.SameParticipant)]
    [InlineData("pa", "a", BpmnMessageFlowSemantics.SameParticipant)]
    [InlineData("b", "a", BpmnMessageFlowSemantics.SourceInvalid)]
    [InlineData("catch", "a", BpmnMessageFlowSemantics.SourceInvalid)]
    [InlineData("a", "throw", BpmnMessageFlowSemantics.TargetInvalid)]
    [InlineData("gateway", "b", BpmnMessageFlowSemantics.SourceInvalid)]
    [InlineData("a", "gateway", BpmnMessageFlowSemantics.TargetInvalid)]
    [InlineData("missing", "b", BpmnMessageFlowSemantics.EndpointMissing)]
    [InlineData("a", "nested", BpmnMessageFlowSemantics.PresentationUnavailable)]
    public async Task InvalidCreationFailsAtomicallyWithCentralDiagnostics(string source, string target, string code)
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Snapshot;
        var result = await fixture.History.ExecuteAsync(fixture.Processor, fixture.Creation(source, target));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Same(before, fixture.Snapshot);
        Assert.Equal(0, fixture.History.CaptureStatus().EntryCount);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("document")]
    [InlineData("anchor-owner")]
    [InlineData("anchor-role")]
    [InlineData("duplicate-id")]
    public async Task InvalidEnvelopeIdentityOrAnchorNeverPartiallyCreates(string scenario)
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Snapshot;
        var command = new CreateBpmnMessageFlowCommand(scenario == "document" ? new DocumentId("other") : before.DocumentId,
            scenario == "stale" ? DocumentRevision.Zero : before.Revision,
            Id(scenario == "duplicate-id" ? "a" : "message"), Visual("message"), Id("a"), Id("b"),
            Anchor(scenario == "anchor-owner" ? "a2" : "a", scenario == "anchor-role" ? "target" : "source"), Anchor("b", "target"));
        Assert.False((await fixture.History.ExecuteAsync(fixture.Processor, command)).IsCommitted);
        Assert.Same(before, fixture.Snapshot);
        Assert.Equal(0, fixture.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task NameAndLifetimeHistoryPreserveLaterGeometryAndUnrelatedRoutes()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var created = fixture.Snapshot;
        var record = fixture.MessageRoute;
        await fixture.Commit(new UpdateBpmnMessageFlowNameCommand(fixture.Document.DocumentId, fixture.Document.Revision, Id("message"), "Order received"), history: true);
        await fixture.Commit(new MoveVisualStateCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Visual("a2"), new PointD(400d, 300d), VisualPlacementMode.Pinned));
        Assert.Equal(record, fixture.MessageRoute);
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.False(fixture.Flow.Properties.ContainsKey(BpmnSemanticProperties.Name));
        Assert.Equal(new PointD(400d, 300d), fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("a2")).Position);
        Assert.Equal(record, fixture.MessageRoute);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal("Order received", fixture.Flow.Properties[BpmnSemanticProperties.Name].TextValue);
        await fixture.Commit(fixture.Delete(), history: true);
        Assert.DoesNotContain(fixture.Snapshot.SemanticModel.Relationships, relationship => relationship.Id == Id("message"));
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(record, fixture.MessageRoute);
        Assert.Equal(created.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")),
            fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")));
        Assert.Equal(new PointD(400d, 300d), fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("a2")).Position);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Empty(fixture.Snapshot.VisualModel.EnumerateConnectorRoutingRecords());
    }

    [Fact]
    public async Task CreationRouteOverloadsUsePreparedAutomaticUnlessManualIsExplicit()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var endpoints = fixture.MessageRoute.Path;
        await fixture.Commit(fixture.Delete());
        var route = new[] { endpoints[0], new PointD(920d, 520d), endpoints[^1] };
        await fixture.Commit(new CreateBpmnMessageFlowCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Id("message"), Visual("message"), Id("a"), Id("b"), Anchor("a", "source"), Anchor("b", "target"), route));
        Assert.Equal(ConnectorRoutingType.Automatic, fixture.MessageRoute.RoutingType);
        Assert.Null(fixture.MessageRoute.ManualDefinition);
        await fixture.Commit(fixture.Delete());
        await fixture.Commit(new CreateBpmnMessageFlowCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Id("message"), Visual("message"), Id("a"), Id("b"), Anchor("a", "source"), Anchor("b", "target"),
            ConnectorRoutingType.Manual, route));
        Assert.Equal(ConnectorRoutingType.Manual, fixture.MessageRoute.RoutingType);
        Assert.Equal(route.AsEnumerable(), fixture.MessageRoute.Path);
        Assert.Equal(route.Skip(1).Take(1), fixture.MessageRoute.ManualDefinition!.Value);
    }

    [Fact]
    public async Task CompoundNameAndManualRouteHistoryKeepsUnrelatedProcessRouting()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Commit(new CreateBpmnSequenceFlowCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Id("sequence"), Visual("sequence"), Id("a"), Id("a2"), Anchor("a", "source"), Anchor("a2", "target")));
        var sequence = fixture.Snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == Visual("sequence"));
        await fixture.CreateFlow("pa", "pb");
        await fixture.Commit(new SetConnectorRoutingTypeCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Visual("message"), ConnectorRoutingType.Manual));
        var original = fixture.MessageRoute;
        var document = fixture.Snapshot;
        await fixture.Commit(new CompoundDocumentCommand(document.DocumentId, document.Revision,
            [new UpdateConnectionRouteCommand(document.DocumentId, document.Revision, Visual("message"),
                [original.Path[0], new PointD(900d, 480d), original.Path[^1]]),
             new UpdateBpmnMessageFlowNameCommand(document.DocumentId, document.Revision, Id("message"), "Revised route")]), history: true);
        var edited = fixture.MessageRoute;
        Assert.Equal("Revised route", fixture.Flow.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(original, fixture.MessageRoute);
        Assert.False(fixture.Flow.Properties.ContainsKey(BpmnSemanticProperties.Name));
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(edited, fixture.MessageRoute);
        Assert.Equal("Revised route", fixture.Flow.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(sequence, fixture.Snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == Visual("sequence")));
    }

    [Fact]
    public async Task ParticipantDirectEndpointUsesAssignedPoolsWithoutProcessReferenceOrAuthoredBounds()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow("pa", "pb");
        Assert.Equal(Id("pb"), fixture.Flow.TargetId);
        var result = fixture.Configuration.ProjectionEngine.ResolvePresentation(fixture.Snapshot,
            new ProcessScopePresentationContext(fixture.Snapshot.SemanticModel.RootScopeId));
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        Assert.True(result.Plan!.Contains(Id("pb")));
        Assert.True(result.Plan.Contains(Id("b")));
        Assert.All(fixture.Snapshot.SemanticModel.Elements.Where(element => element.TypeId == BpmnSemanticTypes.Participant),
            participant => Assert.Single(participant.Properties));
        Assert.All(fixture.Snapshot.VisualModel.VisualStates.Where(visual => visual.PlacementMode == VisualPlacementMode.Derived),
            visual => Assert.Equal(new SizeD(0d, 0d), visual.Size));
    }

    [Fact]
    public async Task AtomicTargetAnchorCreationUndoRedoOwnsOnlyItsAnchorAndRelationship()
    {
        var fixture = await Fixture.CreateAsync();
        var anchorId = new ConnectorAnchorId("message:owned-target");
        var before = fixture.Snapshot;
        await fixture.Commit(new CreateBpmnMessageFlowWithTargetAnchorCommand(before.DocumentId, before.Revision,
            Id("message"), Visual("message"), Id("a"), Id("b"), Anchor("a", "source"), Visual("b"), anchorId,
            ConnectorAnchorSide.Left, 0, ConnectorRoutingType.Manual), history: true);
        await fixture.Commit(new AddConnectorAnchorCommand(before.DocumentId, fixture.Document.Revision,
            Visual("b"), new ConnectorAnchorId("later-target"), ConnectorAnchorSide.Top, ConnectorAnchorRole.Target, 0));
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        var target = fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("b"));
        Assert.DoesNotContain(target.ConnectorAnchors, anchor => anchor.Id == anchorId);
        Assert.Contains(target.ConnectorAnchors, anchor => anchor.Id.Value == "later-target");
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        target = fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("b"));
        Assert.Contains(target.ConnectorAnchors, anchor => anchor.Id == anchorId);
        Assert.Contains(target.ConnectorAnchors, anchor => anchor.Id.Value == "later-target");
        Assert.Equal(anchorId, fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")).TargetAnchorId);
    }

    [Fact]
    public async Task ReconnectionKeepsIdentityAndRejectsSameParticipantAndInvalidDirection()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var before = fixture.Snapshot;
        var bad = new ReconnectBpmnMessageFlowEndpointCommand(before.DocumentId, before.Revision, Id("message"), Visual("message"),
            ConnectorEndpointKind.Target, Id("b"), Anchor("b", "target"), Id("a2"), Anchor("a2", "target"));
        Assert.False((await fixture.History.ExecuteAsync(fixture.Processor, bad)).IsCommitted);
        Assert.Same(before, fixture.Snapshot);
        await fixture.Commit(new ReconnectBpmnMessageFlowEndpointCommand(before.DocumentId, before.Revision, Id("message"), Visual("message"),
            ConnectorEndpointKind.Source, Id("a"), Anchor("a", "source"), Id("a2"), Anchor("a2", "source")), history: true);
        Assert.Equal(Id("a2"), fixture.Flow.SourceId);
        Assert.Equal(Visual("message"), fixture.MessageRoute.VisualStateId);
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(Id("a"), fixture.Flow.SourceId);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(Id("a2"), fixture.Flow.SourceId);
    }

    [Theory]
    [InlineData("a", "BPMN.ReceiveTask")]
    [InlineData("b", "BPMN.SendTask")]
    [InlineData("catch", "BPMN.TimerCatchEvent")]
    [InlineData("boundary", "BPMN.SignalBoundaryEvent")]
    public async Task A21SwitchCannotInvalidateIncidentMessageFlow(string endpoint, string targetType)
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow("a", endpoint is "a" or "b" ? "b" : endpoint);
        var before = fixture.Snapshot;
        var result = await fixture.History.ExecuteAsync(fixture.Processor,
            new ChangeBpmnElementTypeCommand(before.DocumentId, before.Revision, Id(endpoint), new SemanticTypeId(targetType)));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == BpmnMessageFlowSemantics.TypeChangeInvalid);
        Assert.Same(before, fixture.Snapshot);
    }

    [Fact]
    public async Task ParticipantAssignmentAndDeletionCannotInvalidateExistingFlow()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var before = fixture.Snapshot;
        Assert.False((await fixture.Processor.ExecuteAsync(fixture.Document, new SetBpmnPoolParticipantCommand(before.DocumentId,
            before.Revision, Id("pool-b"), Id("pa")))).IsCommitted);
        Assert.Same(before, fixture.Snapshot);
        Assert.False((await fixture.Processor.ExecuteAsync(fixture.Document, new DeleteBpmnParticipantCommand(before.DocumentId,
            before.Revision, Id("pa")))).IsCommitted);
        Assert.Same(before, fixture.Snapshot);
    }

    [Fact]
    public async Task IncidentNodeDeletionRemovesMessageFlowAndUndoRestoresItsExactIdentity()
    {
        var fixture = await Fixture.CreateAsync();
        var sequenceSource = new ConnectorAnchorId("a:sequence-source");
        await fixture.Commit(new AddConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Visual("a"), sequenceSource, ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0));
        await fixture.Commit(new CreateBpmnSequenceFlowCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Id("sequence"), Visual("sequence"), Id("a"), Id("a2"), sequenceSource, Anchor("a2", "target")));
        var sequenceRecord = fixture.Snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == Visual("sequence"));
        await fixture.CreateFlow();
        var flow = fixture.Flow;
        var messageRecord = fixture.MessageRoute;
        await fixture.Commit(new DeleteBpmnFlowNodeCommand(fixture.Document.DocumentId, fixture.Document.Revision, Id("b"), Visual("b")), history: true);
        Assert.DoesNotContain(fixture.Snapshot.SemanticModel.Relationships, relationship => relationship.Id == flow.Id);
        Assert.Equal(sequenceRecord, fixture.Snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == Visual("sequence")));
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(flow, fixture.Flow);
        Assert.Equal(messageRecord, fixture.MessageRoute);
        Assert.Equal(sequenceRecord, fixture.Snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors)
            .Single(record => record.VisualStateId == Visual("sequence")));
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.DoesNotContain(fixture.Snapshot.SemanticModel.Relationships, relationship => relationship.Id == flow.Id);
    }

    [Fact]
    public async Task MissingExplicitPoolMembershipRejectsInternalEndpoint()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Commit(new MoveVisualStateCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Visual("b"), new PointD(100d, 50d), VisualPlacementMode.Pinned));
        await fixture.Commit(new UnassignOrganizationalElementCommand(fixture.Document.DocumentId, fixture.Document.Revision, Id("b")));
        var before = fixture.Snapshot;
        var missing = await fixture.History.ExecuteAsync(fixture.Processor, fixture.Creation());
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == BpmnEffectiveParticipant.OwnershipMissing);
        Assert.Same(before, fixture.Snapshot);
    }

    [Fact]
    public async Task ParticipantNamesDoNotResolveEndpointOwnership()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Commit(new SetBpmnPoolParticipantCommand(fixture.Document.DocumentId, fixture.Document.Revision, Id("pool-b"), null));
        await fixture.CreateFlow("pa", "b");
        Assert.True(BpmnEffectiveParticipant.TryResolve(fixture.Snapshot, Id("b"), out var owner, out _));
        Assert.Equal(new EffectiveParticipantKey(EffectiveParticipantKind.StandalonePool, Id("pool-b")), owner);
        Assert.Equal(Id("pa"), fixture.Flow.SourceId);
    }

    [Theory]
    [InlineData("pa", "pb", false)]
    [InlineData("a", "b", false)]
    [InlineData("a", "catch", false)]
    [InlineData("a", "boundary", false)]
    [InlineData("a", "b", true)]
    public async Task NativeRoundTripPreservesMessageFlowAndProducesSyntheticMetricsEvidence(string source, string target, bool reconnect)
    {
        var fixture = await Fixture.CreateAsync();
        if (source == "a" && target == "b")
            await fixture.Commit(new ChangeBpmnElementTypeCommand(fixture.Document.DocumentId, fixture.Document.Revision,
                Id("a"), BpmnSemanticTypes.SendTask));
        await fixture.CreateFlow(source, target);
        if (reconnect)
            await fixture.Commit(new ReconnectBpmnMessageFlowEndpointCommand(fixture.Document.DocumentId, fixture.Document.Revision,
                Id("message"), Visual("message"), ConnectorEndpointKind.Source, Id(source), Anchor(source, "source"),
                Id("a2"), Anchor("a2", "source")));
        await fixture.Commit(new UpdateBpmnMessageFlowNameCommand(fixture.Document.DocumentId, fixture.Document.Revision, Id("message"), "Order message"));
        await fixture.Commit(new SetConnectorRoutingTypeCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Visual("message"), ConnectorRoutingType.Manual));
        var route = fixture.MessageRoute.Path;
        await fixture.Commit(new UpdateConnectionRouteCommand(fixture.Document.DocumentId, fixture.Document.Revision,
            Visual("message"), [route[0], new PointD(930d, 580d), route[^1]]));
        await fixture.Commit(new MoveLabelCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual("message"),
            new ConnectorLabelPlacement(0.6d, new VectorD(14d, 22d))));
        var bytes = NativeDocumentSerializer.Export(fixture.Snapshot);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory(), fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, Diagnostics(imported.Diagnostics));
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());
        Assert.Equal(fixture.Flow, imported.Document!.SemanticModel.Relationships.Single(flow => flow.Id == Id("message")));
        Assert.Equal(fixture.MessageRoute, imported.Document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords()
            .Single(record => record.VisualStateId == Visual("message")));
        Assert.Equal(fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")),
            imported.Document.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")));
        Assert.DoesNotContain("subtype", Encoding.UTF8.GetString(bytes.AsSpan()), StringComparison.OrdinalIgnoreCase);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var folder = Path.Combine(root.FullName, "TestResults", "phase-a22");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder,
            $"message-flow-roundtrip-{source}-{target}{(reconnect ? "-reconnected" : string.Empty)}-synthetic-text-metrics.inceptus.json"), bytes.ToArray());
    }

    [Fact]
    public void StructuralValidationReportsCompletelyMissingEndpointsWithFriendlyFlowName()
    {
        var documentId = new DocumentId("orphan-flow");
        var semantic = new SemanticModelSnapshot(documentId, DocumentRevision.Zero, relationships:
            [BpmnSemanticFactory.CreateMessageFlow(Id("broken"), Id("missing-source"), Id("missing-target"), "Orphan order")]);
        var snapshot = new DocumentSnapshot(semantic, new VisualModelSnapshot(documentId, DocumentRevision.Zero), new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero));
        var issue = Assert.Single(new BpmnMessageFlowValidationRule().Validate(new ModelValidationContext(snapshot)));
        Assert.Equal(BpmnMessageFlowSemantics.EndpointMissing, issue.Code);
        Assert.Contains("Orphan order", issue.Message, StringComparison.Ordinal);
    }

    private static SemanticElementId Id(string name) => new(name);
    private static VisualStateId Visual(string name) => new("visual:" + name);
    private static ConnectorAnchorId Anchor(string name, string role) => new(name + ":" + role);
    private static string Diagnostics(IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));

    private sealed record Fixture(Document Document, EditingSessionConfiguration Configuration, CommandProcessor Processor, HistoryManager History)
    {
        internal DocumentSnapshot Snapshot => Document.CaptureSnapshot();
        internal SemanticRelationshipSnapshot Flow => Snapshot.SemanticModel.Relationships.Single(flow => flow.Id == Id("message"));
        internal ConnectorRoutingRecord MessageRoute => Snapshot.VisualModel.EnumerateConnectorRoutingRecords()
            .Single(record => record.VisualStateId == Visual("message"));
        internal CreateBpmnMessageFlowCommand Creation(string source = "a", string target = "b") =>
            new(Document.DocumentId, Document.Revision, Id("message"), Visual("message"), Id(source), Id(target),
                Anchor(source, "source"), Anchor(target, "target"));
        internal Task CreateFlow(string source = "a", string target = "b") => Commit(Creation(source, target), history: true);
        internal DeleteBpmnMessageFlowCommand Delete() => new(Document.DocumentId, Document.Revision, Flow.Id, Visual("message"),
            Flow.SourceId, Flow.TargetId, Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")).SourceAnchorId!,
            Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("message")).TargetAnchorId!);
        internal async Task Commit(ICommand command, bool history = false)
        {
            if (history)
            {
                var result = await History.ExecuteAsync(Processor, command);
                Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            }
            else
            {
                var result = await Processor.ExecuteAsync(Document, command);
                Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            }
        }

        internal static async Task<Fixture> CreateAsync()
        {
            var documentId = new DocumentId("a22:message-flow-acceptance");
            SemanticElementSnapshot Participant(string name) => new(Id(name), BpmnSemanticTypes.Participant,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText(name))], containmentKind: SemanticElementContainmentKind.Document);
            SemanticElementSnapshot Pool(string name, string participant) => new(Id(name), OrganizationalSemanticTypes.Pool,
                [new(OrganizationalSemanticProperties.Name, PropertyValue.FromText(name)),
                    new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participant))]);
            var participantNames = new[] { "pa", "pb" };
            var initial = new DocumentSnapshot(new SemanticModelSnapshot(documentId, DocumentRevision.Zero,
                    [Participant("pa"), Participant("pb"), Pool("pool-a", "pa"), Pool("pool-b", "pb")],
                    modelProfiles: new ModelProfileStateSnapshot([BpmnModelProfiles.OrganizationalId])),
                new VisualModelSnapshot(documentId, DocumentRevision.Zero, participantNames.Select(name =>
                    new VisualStateSnapshot(Visual(name), Id(name), new PointD(0d, 0d), new SizeD(0d, 0d), VisualPlacementMode.Derived,
                        connectorAnchors: [new(Anchor(name, "source"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                            new(Anchor(name, "target"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]))),
                new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero));
            var construction = DocumentFactory.Create(initial, new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
            Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
            var configuration = BpmnModelerComposition.Create(construction.Document!).Configuration;
            var preparer = new ConnectorRoutingStatePreparer(configuration,
                new Canvas2DTextLayoutServiceTests.FixedAdvanceTextMetricsService(), StableRoutingPreparationTests.Fixture.CreateRequest);
            var processor = new CommandProcessor(configuration.CommandHandlers, configuration.CommandValidators,
                configuration.DocumentChangedSubscribers, configuration.HistoryPolicies, configuration.ConnectorAnchorPolicyProvider);
            var fixture = new Fixture(construction.Document!, configuration, processor, new HistoryManager(construction.Document!));
            var peer = fixture.Snapshot.SemanticModel.RootScopeId;
            foreach (var (name, scope, position, type) in new[] {
                         ("a", fixture.Snapshot.SemanticModel.RootScopeId, new PointD(100d, 100d), BpmnSemanticTypes.Task),
                         ("a2", fixture.Snapshot.SemanticModel.RootScopeId, new PointD(350d, 100d), BpmnSemanticTypes.Task),
                         ("b", peer, new PointD(100d, 900d), BpmnSemanticTypes.ReceiveTask) })
                await fixture.Commit(new CreateBpmnTaskCommand(documentId, fixture.Document.Revision, Id(name), Visual(name), position,
                    new SizeD(120d, 80d), name.ToUpperInvariant(), "Task " + name, 1, VisualPlacementMode.Pinned, taskTypeId: type, targetScopeId: scope));
            await fixture.Commit(new CreateBpmnMessageThrowEventCommand(documentId, fixture.Document.Revision, Id("throw"), Visual("throw"),
                new PointD(120d, 310d), new SizeD(40d, 40d), "Send event", VisualPlacementMode.Pinned));
            await fixture.Commit(new CreateBpmnMessageCatchEventCommand(documentId, fixture.Document.Revision, Id("catch"), Visual("catch"),
                new PointD(350d, 1110d), new SizeD(40d, 40d), "Receive event", VisualPlacementMode.Pinned, targetScopeId: peer));
            await fixture.Commit(new CreateBpmnMessageBoundaryEventCommand(documentId, fixture.Document.Revision, Id("boundary"), Visual("boundary"),
                Id("b"), BoundaryAttachmentSide.Bottom, 0.5d, new RectD(100d, 900d, 120d, 80d), "Attached message", targetScopeId: peer));
            await fixture.Commit(new CreateBpmnExclusiveGatewayCommand(documentId, fixture.Document.Revision, Id("gateway"), Visual("gateway"),
                new PointD(450d, 400d), new SizeD(60d, 60d), "GW", "Invalid Message endpoint", VisualPlacementMode.Pinned));
            var nestedScope = new DocumentScopeId("process:nested");
            await fixture.Commit(new CreateBpmnSubProcessCommand(documentId, fixture.Document.Revision, Id("subprocess"), Visual("subprocess"),
                peer, nestedScope, new PointD(400d, 1250d), new SizeD(160d, 100d), "SUB", "Nested process", VisualPlacementMode.Pinned));
            await fixture.Commit(new CreateBpmnTaskCommand(documentId, fixture.Document.Revision, Id("nested"), Visual("nested"),
                new PointD(80d, 80d), new SizeD(120d, 80d), "NEST", "Nested task", 2, VisualPlacementMode.Pinned, targetScopeId: nestedScope));
            foreach (var name in new[] { "a", "a2", "throw", "gateway", "b", "catch", "subprocess" })
                await fixture.Commit(new AssignOrganizationalElementCommand(documentId, fixture.Document.Revision, Id(name),
                    Id(name is "a" or "a2" or "throw" or "gateway" ? "pool-a" : "pool-b")));
            await fixture.Commit(new CreateOrganizationalPoolCommand(documentId, fixture.Document.Revision, Id("nested-pool"),
                nestedScope, OrganizationalPoolCreationMode.AdoptEligibleUnassigned));
            foreach (var name in new[] { "a", "a2", "b", "throw", "catch", "boundary", "gateway", "nested", "subprocess" })
                foreach (var role in new[] { ConnectorAnchorRole.Source, ConnectorAnchorRole.Target })
                    await fixture.Commit(new AddConnectorAnchorCommand(documentId, fixture.Document.Revision, Visual(name),
                        Anchor(name, role == ConnectorAnchorRole.Source ? "source" : "target"),
                        role == ConnectorAnchorRole.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left, role, 0));
            var source = fixture.Snapshot;
            var prepared = await preparer.PrepareAsync(new(source, source, [], [], null, false,
                ConnectorRoutingPreparationPurpose.InitialConstruction), CancellationToken.None);
            Assert.True(prepared.Succeeded, Diagnostics(prepared.Diagnostics));
            var saved = new DocumentSnapshot(source.SemanticModel, new VisualModelSnapshot(documentId, source.Revision,
                source.VisualModel.VisualStates, source.VisualModel.ProfileElementPresentations, prepared.RoutingScopes), source.Metadata);
            var restored = DocumentReconstructor.Reconstruct(saved, configuration.ConnectorAnchorPolicyProvider);
            Assert.True(restored.Succeeded, Diagnostics(restored.Diagnostics));
            return new(restored.Document!, configuration, new CommandProcessor(configuration.CommandHandlers, configuration.CommandValidators,
                configuration.DocumentChangedSubscribers, configuration.HistoryPolicies, configuration.ConnectorAnchorPolicyProvider, preparer),
                new HistoryManager(restored.Document!));
        }
    }
}
