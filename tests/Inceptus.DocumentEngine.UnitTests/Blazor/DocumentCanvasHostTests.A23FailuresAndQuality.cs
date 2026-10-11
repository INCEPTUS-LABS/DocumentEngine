using System.Diagnostics;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void A23AutoArrangeLocalization(string language)
    {
        using var culture = new ModelerCultureScope(language);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        Assert.False(text["Toolbar_AutoArrange"].ResourceNotFound);
        Assert.False(string.IsNullOrWhiteSpace(text["Toolbar_AutoArrange"]));
        Assert.False(text["Diagnostic_AutoArrangeFailed"].ResourceNotFound);
        Assert.False(text["Diagnostic_AutoArrangeStale"].ResourceNotFound);
    }

    [Theory]
    [InlineData("routing")]
    [InlineData("throw")]
    [InlineData("invariant")]
    public async Task A23AutoArrangePreparationFailuresAreAtomic(string fault)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("branches")));
        await host.InitializeAsync("arrange-failure", "container");
        var original = AttachedDocument(Session(host)).CaptureSnapshot();
        var registration = BpmnPluginRegistration.N100;
        var policy = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var document = DocumentFactory.Create(original, policy).Document!;
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var preparer = new A23FaultPreparer(fault);
        var processor = new CommandProcessor(registration.CommandHandlers, registration.CommandValidators, null,
            registration.HistoryPolicies, policy, preparer);
        var result = await history.ExecuteAsync(processor, new BpmnAutoArrangeCommand(BpmnAutoArrangePlanner.Plan(original, [original.SemanticModel.RootScopeId])));
        Assert.False(result.IsCommitted);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.Equal(1, preparer.Count);
    }

    [Fact]
    public async Task A23AutoArrangePlanningCancellationAndFocusedReplayPreserveLaterProperties()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("branches")));
        await host.InitializeAsync("arrange-focused-history", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        Assert.Throws<OperationCanceledException>(() => BpmnAutoArrangePlanner.Plan(before, [before.SemanticModel.RootScopeId], new CancellationToken(true)));
        Assert.Same(before, document.CaptureSnapshot());
        var command = new BpmnAutoArrangeCommand(BpmnAutoArrangePlanner.Plan(before, [before.SemanticModel.RootScopeId]));
        Assert.True((await session.ExecuteAsync(command)).IsCommitted);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var policy = BpmnPluginRegistration.N100.HistoryPolicies.Single(policy => policy.TypeId == command.TypeId).Policy;
        var history = policy.Prepare(command, before, after);
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new("a"), BpmnSemanticProperties.Name, "Later unrelated name"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.True((await session.ExecuteAsync(history.UndoFactory!.Create(document.DocumentId, document.Revision))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal("Later unrelated name", document.SemanticModel.Elements.Single(element => element.Id.Value == "a").Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(before.VisualModel.VisualStates.Select(state => state.Position), document.VisualModel.VisualStates.Select(state => state.Position));
        Assert.True((await session.ExecuteAsync(history.RedoFactory!.Create(document.DocumentId, document.Revision))).IsCommitted);
        Assert.Equal("Later unrelated name", document.SemanticModel.Elements.Single(element => element.Id.Value == "a").Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(after.VisualModel.VisualStates.Select(state => state.Position), document.VisualModel.VisualStates.Select(state => state.Position));
    }

    [Fact]
    public async Task A23AutoArrangeCrossingSweepRemovesCanonicalCrossingAndIsPermutationStable()
    {
        var original = A23Snapshot([("start", "a"), ("start", "b"), ("a", "d"), ("b", "c"), ("c", "end"), ("d", "end")]);
        var positions = new Dictionary<string, PointD>
        {
            ["start"] = new(40, 130),
            ["a"] = new(200, 40),
            ["b"] = new(200, 220),
            ["c"] = new(400, 40),
            ["d"] = new(400, 220),
            ["end"] = new(600, 130)
        };
        var visual = new VisualModelSnapshot(original.DocumentId, original.Revision, original.VisualModel.VisualStates.Select(state =>
            positions.TryGetValue(state.SemanticElementId.Value, out var point) ? new VisualStateSnapshot(state.Id, state.SemanticElementId, point,
                state.Size, state.PlacementMode, state.Route, state.Properties, state.ConnectorAnchors, state.SourceAnchorId, state.TargetAnchorId) : state));
        await using var host = A23Host(new(original.SemanticModel, visual, original.Metadata));
        await host.InitializeAsync("arrange-crossings", "container");
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        Assert.True(Crossed(before));
        Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
        var after = document.CaptureSnapshot();
        Assert.False(Crossed(after));
        Assert.True(BpmnAutoArrangePlanner.Plan(after, [after.SemanticModel.RootScopeId]).IsNoChange);
        var reversed = new DocumentSnapshot(after.SemanticModel, new VisualModelSnapshot(after.DocumentId, after.Revision,
            after.VisualModel.VisualStates.Reverse(), after.VisualModel.ProfileElementPresentations, after.VisualModel.RoutingScopes,
            after.VisualModel.DocumentRoutingStates), after.Metadata);
        Assert.True(BpmnAutoArrangePlanner.Plan(reversed, [after.SemanticModel.RootScopeId]).IsNoChange);
        static bool Crossed(DocumentSnapshot snapshot) => (A23Bounds(snapshot, "a").Y - A23Bounds(snapshot, "b").Y) *
            (A23Bounds(snapshot, "d").Y - A23Bounds(snapshot, "c").Y) < 0;
    }

    [Fact]
    public async Task A23AutoArrangeLargerDiagramCommitsOnceWithoutPerNodePreparation()
    {
        var edges = Enumerable.Range(0, 59).Select(index => ($"node-{index:D3}", $"node-{index + 1:D3}")).ToArray();
        var composition = await new BpmnModelerCompositionFactory(initialDocument: A23Snapshot(edges)).CreateAsync();
        await using var host = A23Host(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("arrange-large", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var processor = (CommandProcessor)typeof(EditingSession).GetField("_commandProcessor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        var inner = (IConnectorRoutingStatePreparer)typeof(CommandProcessor).GetField("_routingStatePreparer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(processor)!;
        var counter = new A23CountingPreparer(inner);
        var configuration = composition.Configuration;
        var measured = new CommandProcessor(configuration.CommandHandlers, configuration.CommandValidators, null,
            configuration.HistoryPolicies, configuration.ConnectorAnchorPolicyProvider, counter);
        var before = document.CaptureSnapshot();
        var detached = DocumentFactory.Create(before, configuration.ConnectorAnchorPolicyProvider).Document!;
        var history = new HistoryManager(detached);
        var timer = Stopwatch.StartNew();
        var plan = BpmnAutoArrangePlanner.Plan(before, [before.SemanticModel.RootScopeId]);
        var result = await history.ExecuteAsync(measured, new BpmnAutoArrangeCommand(plan));
        timer.Stop();
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(1, counter.Count);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.Equal(before.Revision.Increment(), detached.Revision);
        Assert.True(BpmnAutoArrangePlanner.Plan(detached.CaptureSnapshot(), [before.SemanticModel.RootScopeId]).IsNoChange);
        Console.WriteLine($"A23 larger diagnostic: 60 nodes, 59 connectors, {timer.Elapsed.TotalMilliseconds:F1} ms, one routing preparation, one commit.");
    }

    private sealed class A23CountingPreparer(IConnectorRoutingStatePreparer inner) : IConnectorRoutingStatePreparer
    {
        internal int Count { get; private set; }
        public ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken)
        { Count++; return inner.PrepareAsync(request, cancellationToken); }
    }

    private sealed class A23FaultPreparer(string fault) : IConnectorRoutingStatePreparer
    {
        internal int Count { get; private set; }
        public ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken)
        {
            Count++;
            if (fault == "throw") throw new InvalidOperationException("Injected routing preparation exception.");
            return ValueTask.FromResult(fault == "routing" ? ConnectorRoutingStatePreparationResult.Failure(
                [new Diagnostic("test:a23:route-failure", DiagnosticSeverity.Error, "Injected routing preparation failure.")]) :
                ConnectorRoutingStatePreparationResult.Success([]));
        }
    }
}
