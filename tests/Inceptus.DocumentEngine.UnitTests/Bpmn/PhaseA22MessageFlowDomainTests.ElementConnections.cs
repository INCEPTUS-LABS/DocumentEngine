using System.Text;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class PhaseA22MessageFlowDomainTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ElementFlowJointPlansReuseBeforeCreationAndOwnFocusedHistoryAndNativeState(bool message, bool addSource, bool addTarget)
    {
        var fixture = await Fixture.CreateAsync();
        var target = message ? "b" : "a2";
        if (addSource) await fixture.Commit(new RemoveConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual("a"), Anchor("a", "source")));
        if (addTarget) await fixture.Commit(new RemoveConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual(target), Anchor(target, "target")));
        var before = fixture.Snapshot;
        var factory = ElementFactory(message);
        var request = ElementRequest(fixture, "a", target, message);
        Assert.Empty(factory.ValidateEndpoints(request));
        var candidates = await factory.CreateCandidatesAsync(request, CancellationToken.None);
        Assert.NotEmpty(candidates);
        Assert.Same(before, fixture.Snapshot);
        foreach (var candidate in candidates)
        {
            var flow = Assert.IsType<CreateBpmnElementFlowCommand>(candidate.Plan.Command);
            Assert.Equal(addSource, flow.NewSourceAnchor is not null);
            Assert.Equal(addTarget, flow.NewTargetAnchor is not null);
            if (!addSource) Assert.Equal(Anchor("a", "source"), flow.SourceAnchorId);
            if (!addTarget) Assert.Equal(Anchor(target, "target"), flow.TargetAnchorId);
        }
        var chosen = Assert.IsType<CreateBpmnElementFlowCommand>(candidates[0].Plan.Command);
        await fixture.Commit(chosen, history: true);
        var created = fixture.Snapshot;
        Assert.Equal(1, fixture.History.CaptureStatus().EntryCount);
        var saved = NativeDocumentSerializer.Export(created);
        var opened = NativeDocumentSerializer.Import(saved.AsMemory(), fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(opened.Succeeded, Diagnostics(opened.Diagnostics));
        Assert.Equal(created, opened.Document!.CaptureSnapshot());
        var json = Encoding.UTF8.GetString(saved.AsSpan());
        Assert.DoesNotContain("element-connection", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentPoint", json, StringComparison.Ordinal);
        if (addSource || addTarget) Assert.Contains("normalizedPosition", json, StringComparison.Ordinal);
        await fixture.Commit(new MoveVisualStateCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual("gateway"),
            new PointD(500d, 400d), VisualPlacementMode.Pinned));
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.DoesNotContain(fixture.Snapshot.SemanticModel.Relationships, relationship => relationship.Id == chosen.RelationshipId);
        Assert.Equal(new PointD(500d, 400d), fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("gateway")).Position);
        Assert.Equal<ConnectorAnchor>(before.VisualModel.VisualStates.Single(visual => visual.Id == Visual("a")).ConnectorAnchors,
            fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("a")).ConnectorAnchors);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal<SemanticRelationshipSnapshot>(created.SemanticModel.Relationships, fixture.Snapshot.SemanticModel.Relationships);
        Assert.Equal(created.VisualModel.EnumerateConnectorRoutingRecords(), fixture.Snapshot.VisualModel.EnumerateConnectorRoutingRecords());
    }

    [Theory]
    [InlineData("pa", "pb")]
    [InlineData("a", "b")]
    [InlineData("a", "catch")]
    [InlineData("throw", "b")]
    [InlineData("a", "boundary")]
    [InlineData("pa", "b")]
    public async Task ElementMessageFlowUsesTheApprovedEndpointAuthority(string source, string target)
    {
        var fixture = await Fixture.CreateAsync();
        var candidates = await ElementFactory(true).CreateCandidatesAsync(ElementRequest(fixture, source, target, true), CancellationToken.None);
        await fixture.Commit(Assert.Single(candidates).Plan.Command, history: true);
        Assert.Equal(BpmnSemanticTypes.MessageFlow, Assert.Single(fixture.Snapshot.SemanticModel.Relationships).TypeId);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("document")]
    [InlineData("target-owner")]
    [InlineData("target-role")]
    [InlineData("duplicate-anchor")]
    [InlineData("reuse-first")]
    [InlineData("same-participant")]
    [InlineData("cross-process-sequence")]
    public async Task ElementFlowFailuresLeaveNoSourcePointRelationshipRouteOrHistory(string failure)
    {
        var fixture = await Fixture.CreateAsync();
        if (failure != "reuse-first") await fixture.Commit(new RemoveConnectorAnchorCommand(fixture.Document.DocumentId,
            fixture.Document.Revision, Visual("a"), Anchor("a", "source")));
        var before = fixture.Snapshot;
        var source = new ConnectorAnchor(new("new-source"), ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0, 0.5d);
        var command = new CreateBpmnElementFlowCommand(failure == "document" ? new("other") : before.DocumentId,
            failure == "stale" ? DocumentRevision.Zero : before.Revision,
            failure == "cross-process-sequence" ? BpmnSemanticTypes.SequenceFlow : BpmnSemanticTypes.MessageFlow,
            Id("created"), Visual("created"), Id("a"), failure == "same-participant" ? Id("a2") : Id("b"), Visual("a"),
            failure == "target-owner" ? Visual("a2") : Visual("b"), source.Id,
            failure == "duplicate-anchor" ? source.Id : Anchor("b", failure == "target-role" ? "source" : "target"), source);
        var result = await fixture.History.ExecuteAsync(fixture.Processor, command);
        Assert.False(result.IsCommitted);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Same(before, fixture.Snapshot);
        Assert.Equal(0, fixture.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task ElementFlowPreservesOccupiedPointsAndRoutesWhenAddingOnTheSameEdge()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var before = fixture.Snapshot;
        var previous = fixture.MessageRoute;
        var request = ElementRequest(fixture, "a", "catch", true);
        var candidates = await ElementFactory(true).CreateCandidatesAsync(request, CancellationToken.None);
        var selected = candidates.First(candidate => ((CreateBpmnElementFlowCommand)candidate.Plan.Command).NewSourceAnchor?.Side == ConnectorAnchorSide.Right);
        var command = Assert.IsType<CreateBpmnElementFlowCommand>(selected.Plan.Command);
        Assert.NotEqual(Anchor("a", "source"), command.SourceAnchorId);
        await fixture.Commit(command, history: true);
        Assert.Equal<PointD>(previous.Path, fixture.MessageRoute.Path);
        Assert.Equal(previous.AutomaticProof!.Source.Point, fixture.MessageRoute.AutomaticProof!.Source.Point);
        var occupied = fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual("a")).ConnectorAnchors.Single(anchor => anchor.Id == Anchor("a", "source"));
        Assert.Equal(0.5d, occupied.NormalizedPosition);
        Assert.Equal(2, fixture.Snapshot.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == fixture.Snapshot.SemanticModel.RootScopeId).Connectors.Length);
        Assert.Equal(previous.VisualStateId, fixture.Snapshot.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == fixture.Snapshot.SemanticModel.RootScopeId).Connectors[0].VisualStateId);
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal<SemanticRelationshipSnapshot>(before.SemanticModel.Relationships, fixture.Snapshot.SemanticModel.Relationships);
        Assert.Equal<VisualStateSnapshot>(before.VisualModel.VisualStates, fixture.Snapshot.VisualModel.VisualStates);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal<PointD>(previous.Path, fixture.MessageRoute.Path);
    }

    [Fact]
    public async Task ElementFlowCandidatesAreJointDeterministicAndCannotReplaceAFreePointWithANewPoint()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Commit(new AddConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual("a"),
            new("a:extra-source"), ConnectorAnchorSide.Top, ConnectorAnchorRole.Source, 0));
        await fixture.Commit(new AddConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual("b"),
            new("b:extra-target"), ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Target, 0));
        var factory = ElementFactory(true);
        var first = await factory.CreateCandidatesAsync(ElementRequest(fixture, "a", "b", true), CancellationToken.None);
        var second = await factory.CreateCandidatesAsync(ElementRequest(fixture, "a", "b", true), CancellationToken.None);
        Assert.Equal(4, first.Length);
        static (ConnectorAnchorId, ConnectorAnchorId) Pair(ElementConnectionCandidate candidate)
        {
            var flow = Assert.IsType<CreateBpmnElementFlowCommand>(candidate.Plan.Command);
            Assert.Null(flow.NewSourceAnchor);
            Assert.Null(flow.NewTargetAnchor);
            return (flow.SourceAnchorId, flow.TargetAnchorId);
        }
        Assert.Equal(first.Select(Pair), second.Select(Pair));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ElementFlowRoutingFailureOrCancellationIsAtomicEvenWithTwoProposedPoints(bool cancel)
    {
        var fixture = await Fixture.CreateAsync();
        foreach (var (name, role) in new[] { ("a", "source"), ("b", "target") })
            await fixture.Commit(new RemoveConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual(name), Anchor(name, role)));
        var before = fixture.Snapshot;
        var candidates = await ElementFactory(true).CreateCandidatesAsync(ElementRequest(fixture, "a", "b", true), CancellationToken.None);
        var processor = new CommandProcessor(fixture.Configuration.CommandHandlers, fixture.Configuration.CommandValidators,
            fixture.Configuration.DocumentChangedSubscribers, fixture.Configuration.HistoryPolicies, fixture.Configuration.ConnectorAnchorPolicyProvider,
            new RejectElementRouting());
        var result = await fixture.History.ExecuteAsync(processor, candidates[0].Plan.Command, new CancellationToken(cancel));
        Assert.False(result.IsCommitted);
        Assert.Same(before, fixture.Snapshot);
        Assert.Equal(0, fixture.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task ElementSequenceSelfFlowRestoresBothOwnedPointsOnTheSameVisual()
    {
        var fixture = await Fixture.CreateAsync();
        foreach (var role in new[] { "source", "target" })
            await fixture.Commit(new RemoveConnectorAnchorCommand(fixture.Document.DocumentId, fixture.Document.Revision, Visual("a"), Anchor("a", role)));
        var before = fixture.Snapshot;
        var candidates = await ElementFactory(false).CreateCandidatesAsync(ElementRequest(fixture, "a", "a", false), CancellationToken.None);
        Assert.NotEmpty(candidates);
        await fixture.Commit(candidates[0].Plan.Command, history: true);
        var created = fixture.Snapshot;
        Assert.True((await fixture.History.UndoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal<VisualStateSnapshot>(before.VisualModel.VisualStates, fixture.Snapshot.VisualModel.VisualStates);
        Assert.True((await fixture.History.RedoAsync(fixture.Processor)).IsCommitted);
        Assert.Equal<VisualStateSnapshot>(created.VisualModel.VisualStates, fixture.Snapshot.VisualModel.VisualStates);
    }

    private sealed class RejectElementRouting : IConnectorRoutingStatePreparer
    {
        public ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ConnectorRoutingStatePreparationResult.Failure([new Diagnostic("TEST_ROUTING_FAILURE", DiagnosticSeverity.Error, "Injected routing preparation failure.")]));
    }

    private static IElementConnectionCreationCommandFactory ElementFactory(bool message) =>
        Assert.IsAssignableFrom<IElementConnectionCreationCommandFactory>(BpmnPluginRegistration.N100.AnchorConnectionCreationRegistrations
            .Single(registration => registration.CreationId.Value.EndsWith(message ? "/message-flow" : "/sequence-flow", StringComparison.Ordinal)).CommandFactory);

    private static ElementConnectionCreationRequest ElementRequest(Fixture fixture, string source, string target, bool message)
    {
        PresentationViewContext context = new ProcessScopePresentationContext(fixture.Snapshot.SemanticModel.RootScopeId);
        var plan = fixture.Configuration.ProjectionEngine.ResolvePresentation(fixture.Snapshot, context).Plan;
        ElementConnectionEndpoint Endpoint(string name)
        {
            var visual = fixture.Snapshot.VisualModel.VisualStates.Single(visual => visual.Id == Visual(name));
            fixture.Snapshot.SemanticModel.TryGetScope(Id(name), out var scope);
            return new(Id(name), visual.Id, new("projected:" + name), new("scene:" + name), scope?.Id, new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height));
        }
        return new(fixture.Snapshot, context, plan, Endpoint(source), Endpoint(target), new ElementIdentityProvider());
    }

    private sealed class ElementIdentityProvider : IDocumentCreationIdentityProvider
    {
        private int _next;
        public DocumentCreationIdentity CreateIdentity() { var value = ++_next; return new(new("element-flow:" + value), new("element-flow:visual:" + value)); }
    }
}
