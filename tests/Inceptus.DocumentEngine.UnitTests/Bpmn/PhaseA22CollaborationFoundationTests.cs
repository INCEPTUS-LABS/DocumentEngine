using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseA22CollaborationFoundationTests
{
    [Fact]
    public void NormalProcessProjectionIncludesOnlyParticipantsReferencedByItsPools()
    {
        var snapshot = PhaseA22PoolParticipantTests.Snapshot();
        var document = DocumentFactory.Create(snapshot).Document!;
        var engine = BpmnModelerComposition.Create(document).Configuration.ProjectionEngine;
        var context = new ProcessScopePresentationContext(snapshot.SemanticModel.RootScopeId);
        var resolution = engine.ResolvePresentation(snapshot, context);
        Assert.True(resolution.Succeeded);
        Assert.Contains(new SemanticElementId("company"), resolution.Plan!.ElementIds);
        var projected = engine.Project(snapshot, context);
        Assert.True(projected.IsSuccessful, string.Join("; ", projected.Diagnostics.Select(item => item.Message)));
        var participant = Assert.Single(projected.Graph!.Nodes, node => node.Source.SemanticTypeId == BpmnSemanticTypes.Participant);
        Assert.NotNull(participant.RegionEnvelope);
        Assert.Null(participant.Source.VisualStateId);
        Assert.Empty(snapshot.VisualModel.VisualStates);
        Assert.Empty(new BpmnCollaborationViewProvider().GetContexts(snapshot));
    }

    [Fact]
    public void SeparateCollaborationViewIsRejectedWithoutSynthesizingParticipantVisuals()
    {
        var snapshot = PhaseA22PoolParticipantTests.Snapshot();
        var result = new BpmnCollaborationViewProvider().Resolve(snapshot, new DocumentPresentationContext(new("company")));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == BpmnCollaborationViewProvider.InvalidView);
        Assert.Empty(snapshot.VisualModel.VisualStates);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("document")]
    [InlineData("missing-pool")]
    [InlineData("task-as-pool")]
    [InlineData("missing-participant")]
    [InlineData("task-as-participant")]
    [InlineData("pool-as-participant")]
    [InlineData("unchanged")]
    public async Task InvalidParticipantAssignmentIsAtomic(string scenario)
    {
        var fixture = Harness();
        var before = fixture.Document.CaptureSnapshot();
        var pool = scenario switch { "missing-pool" => "missing", "task-as-pool" => "sales-task", "unchanged" => "sales", _ => "supplier" };
        var participant = scenario switch { "missing-participant" => "missing", "task-as-participant" => "sales-task", "pool-as-participant" => "warehouse", _ => "company" };
        var command = new SetBpmnPoolParticipantCommand(scenario == "document" ? new("other") : before.DocumentId,
            scenario == "stale" ? before.Revision.Increment() : before.Revision, new(pool), new(participant));
        var result = await fixture.History.ExecuteAsync(fixture.Processor, command);
        Assert.False(result.IsCommitted);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Same(before, fixture.Document.CaptureSnapshot());
        Assert.Equal(0, fixture.History.CaptureStatus().EntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentHistoryOwnsOnlyTheOptionalReference(bool unassign)
    {
        var fixture = Harness();
        var before = fixture.Document.CaptureSnapshot();
        var command = new SetBpmnPoolParticipantCommand(before.DocumentId, before.Revision,
            new(unassign ? "sales" : "supplier"), unassign ? null : new("company"));
        Assert.True((await fixture.History.ExecuteAsync(fixture.Processor, command)).IsCommitted);
        var committed = fixture.Document.CaptureSnapshot();
        var changed = committed.SemanticModel.Elements.Single(element => element.Id == command.PoolId);
        Assert.Equal(command.ParticipantId, OrganizationalSemantics.GetParticipantId(changed));
        Assert.Equal(before.SemanticModel.Elements.Where(element => element.Id != command.PoolId),
            committed.SemanticModel.Elements.Where(element => element.Id != command.PoolId));
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), committed.VisualModel.VisualStates);
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), fixture.Document.SemanticModel.Elements);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal(committed.SemanticModel.Elements.AsEnumerable(), fixture.Document.SemanticModel.Elements);
    }

    [Fact]
    public async Task ReferencedParticipantDeletionIsRejectedWithoutChangingPools()
    {
        var fixture = Harness();
        var before = fixture.Document.CaptureSnapshot();
        var result = await fixture.Processor.ExecuteAsync(fixture.Document,
            new DeleteBpmnParticipantCommand(before.DocumentId, before.Revision, new("company")));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, item => item.Code == "BPMN_PARTICIPANT_REFERENCED_BY_POOL");
        Assert.Same(before, fixture.Document.CaptureSnapshot());
    }

    [Fact]
    public void LegacyParticipantProcessReferencesDoNotSupplyPoolMembership()
    {
        var source = PhaseA22PoolParticipantTests.Snapshot();
        var semantic = source.SemanticModel;
        var collaboration = new SemanticElementId("legacy-collaboration");
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(source.DocumentId, source.Revision,
            semantic.Elements.Append(BpmnSemanticFactory.CreateCollaboration(collaboration))
                .Append(BpmnSemanticFactory.CreateParticipant(new("legacy-participant"), collaboration, semantic.RootScopeId)),
            semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments),
            source.VisualModel, source.Metadata);
        Assert.False(BpmnEffectiveParticipant.TryResolve(snapshot, new("unpooled"), out _, out var diagnostic));
        Assert.Equal(BpmnEffectiveParticipant.OwnershipMissing, diagnostic!.Code);
        Assert.True(BpmnEffectiveParticipant.TryResolve(snapshot, new("sales-task"), out var owner, out _));
        Assert.Equal(new EffectiveParticipantKey(EffectiveParticipantKind.Participant, new("company")), owner);
    }

    private static (Document Document, CommandProcessor Processor, HistoryManager History) Harness()
    {
        var registration = BpmnPluginRegistration.N101;
        var anchors = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var created = DocumentFactory.Create(PhaseA22PoolParticipantTests.Snapshot(), anchors);
        Assert.True(created.Succeeded, string.Join("; ", created.Diagnostics.Select(item => item.Message)));
        var document = created.Document!;
        return (document, new CommandProcessor(registration.CommandHandlers, registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies, connectorAnchorPolicyProvider: anchors), new HistoryManager(document));
    }
}
