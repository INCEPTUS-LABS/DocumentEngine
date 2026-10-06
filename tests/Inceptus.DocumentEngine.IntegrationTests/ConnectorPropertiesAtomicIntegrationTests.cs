using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Routing;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class ConnectorPropertiesAtomicIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealPropertiesApplyAndHistoryAreAtomicAndNameAddsNoPipelineWork(bool manual)
    {
        var counts = new List<(int Layout, int Routes, int Assessments, int Repairs)>();
        var routeStates = new List<ConnectorRoutingRecord[]>();
        foreach (var rename in new[] { false, true })
        {
            var probe = new Probe();
            await using var test = await CreateAsync(probe);
            var named = test.Composition.Document.CaptureSnapshot();
            var semanticId = named.VisualModel.VisualStates.Single(item => item.Id == BpmnDemoPipeline.SixthSequenceFlowVisualId).SemanticElementId;
            Assert.True((await test.Session.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(named.DocumentId,
                named.Revision, semanticId, "TAK"))).IsCommitted);
            await DrainAsync(test);
            if (manual)
            {
                var initial = test.Composition.Document.CaptureSnapshot();
                Assert.True((await test.Session.ExecuteAsync(new SetConnectorRoutingTypeCommand(initial.DocumentId,
                    initial.Revision, BpmnDemoPipeline.SixthSequenceFlowVisualId, ConnectorRoutingType.Manual))).IsCommitted);
                await DrainAsync(test);
                initial = test.Composition.Document.CaptureSnapshot();
                var path = Route(initial).Path;
                Assert.True((await test.Session.ExecuteAsync(new UpdateConnectionRouteCommand(initial.DocumentId,
                    initial.Revision, BpmnDemoPipeline.SixthSequenceFlowVisualId,
                    [path[0], new PointD(470, 180), new PointD(490, 220), path[^1]]))).IsCommitted);
                await DrainAsync(test);
            }
            var properties = await OpenAsync(test);
            var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = manual ? "automatic" : "manual" };
            if (rename) draft.DataFields.Single(field => field.FieldId.Value == "name").EditorValue = "Approved path";
            test.Host.UpdatePropertiesFormState(true, properties.VisualStateId, draft.IsDirty);
            var before = test.Composition.Document.CaptureSnapshot();
            var history = test.State.HistoryStatus;
            var layout = test.State.LayoutResult!.Computation;
            probe.Reset();

            Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await test.Host.ApplyPropertiesAsync(draft)).Status);
            await DrainAsync(test);
            var committed = test.Composition.Document.CaptureSnapshot();
            Assert.Equal(before.Revision.Increment(), committed.Revision);
            Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
            var change = Assert.Single(probe.Events);
            Assert.Equal(rename ? CompoundDocumentCommand.KnownTypeId : SetConnectorRoutingTypeCommand.KnownTypeId, change.CommandTypeId);
            Assert.Equal(CommandPipelineInvalidation.ConnectorOnly, change.PipelineInvalidation);
            Assert.Equal(0, probe.LayoutCalls);
            Assert.Equal(layout.Nodes.Select(node => (node.ProjectedObjectId, node.Bounds, node.Transform)),
                test.State.LayoutResult.Computation.Nodes.Select(node => (node.ProjectedObjectId, node.Bounds, node.Transform)));
            Assert.Equal(manual ? ConnectorRoutingType.Automatic : ConnectorRoutingType.Manual, Route(committed).RoutingType);
            counts.Add((probe.LayoutCalls, probe.Routes, probe.Assessments, probe.Repairs));
            routeStates.Add(committed.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors).ToArray());
            if (rename)
            {
                Assert.Equal(new[] { UpdateBpmnSequenceFlowNameCommand.KnownTypeId, SetConnectorRoutingTypeCommand.KnownTypeId }, probe.Validated);
                Assert.Equal("Approved path", probe.NameSeenByRoutingValidator);
                Assert.Equal("Approved path", Name(committed, properties.SemanticId));
            }
            test.Host.UpdatePropertiesFormState(false, null, false);
            Assert.True((await test.Session.UndoAsync()).IsCommitted);
            await DrainAsync(test);
            var undone = test.Composition.Document.CaptureSnapshot();
            Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), undone.SemanticModel.Relationships.AsEnumerable());
            Assert.Equal(Route(before).RoutingType, Route(undone).RoutingType);
            Assert.Equal(Route(before).Path.AsEnumerable(), Route(undone).Path.AsEnumerable());
            if (manual) Assert.Equal(Route(before), Route(undone));
            Assert.Equal(2, probe.Events.Count);
            Assert.True((await test.Session.RedoAsync()).IsCommitted);
            await DrainAsync(test);
            var redone = test.Composition.Document.CaptureSnapshot();
            Assert.Equal(committed.SemanticModel.Relationships.AsEnumerable(), redone.SemanticModel.Relationships.AsEnumerable());
            Assert.Equal(Route(committed), Route(redone));
            Assert.Equal(3, probe.Events.Count);
            Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        }
        Assert.Equal(counts[0], counts[1]);
        Assert.Equal(routeStates[0], routeStates[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildValidationFailureRetainsBothValuesFormAndUsableSession(bool rejectName)
    {
        var probe = new Probe();
        await using var test = await CreateAsync(probe);
        var properties = await OpenAsync(test);
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = "manual" };
        draft.DataFields.Single(field => field.FieldId.Value == "name").EditorValue = "Approved path";
        test.Host.UpdatePropertiesFormState(true, properties.VisualStateId, draft.IsDirty);
        var before = test.Composition.Document.CaptureSnapshot();
        var history = test.State.HistoryStatus;
        probe.Reset();
        probe.Reject = rejectName ? UpdateBpmnSequenceFlowNameCommand.KnownTypeId : SetConnectorRoutingTypeCommand.KnownTypeId;

        var rejected = await test.Host.ApplyPropertiesAsync(draft);
        await DrainAsync(test);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Failed, rejected.Status);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "TEST_PROPERTY_REJECTED");
        Assert.Same(before, test.Composition.Document.CaptureSnapshot());
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Empty(probe.Events);
        Assert.Equal((0, 0, 0, 0), (probe.LayoutCalls, probe.Routes, probe.Assessments, probe.Repairs));
        Assert.True(test.Host.CaptureState().PropertiesFormOpen);
        if (!rejectName) Assert.Equal("Approved path", probe.NameSeenByRoutingValidator);

        probe.Reject = null;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await test.Host.ApplyPropertiesAsync(draft)).Status);
        await DrainAsync(test);
        Assert.Single(probe.Events);
        Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
    }

    private static async Task<HostHarness> CreateAsync(Probe probe)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var source = composition.Configuration;
        var configuration = new EditingSessionConfiguration(source.ProjectionEngine,
            new LayoutEngine([new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, probe)]), source.LayoutAlgorithmId,
            new RoutingEngine([new RoutingAlgorithmRegistration(source.RoutingAlgorithmId, probe)]), source.RoutingAlgorithmId,
            source.SceneBuilder, source.ProjectionContext, source.LayoutContext, source.RoutingContext,
            source.InitialEditorState, source.CommandHandlers,
            [.. source.CommandValidators,
                new(UpdateBpmnSequenceFlowNameCommand.KnownTypeId, new CommandValidatorId("test:connector-name"), probe),
                new(SetConnectorRoutingTypeCommand.KnownTypeId, new CommandValidatorId("test:connector-routing"), probe)],
            source.HistoryPolicies, [.. source.DocumentChangedSubscribers, probe], source.ConnectorAnchorPolicyProvider,
            source.ModelProfileCatalog, source.InitialModelProfileViewState, source.RoutingInputPreparer);
        return await HostHarness.CreateAsync(new DocumentCanvasComposition(composition.Document, configuration,
            composition.PropertiesSchemaCatalog, spatialEditPlanners: composition.SpatialEditPlanners));
    }

    private static async Task<DocumentCanvasPropertySnapshot> OpenAsync(HostHarness test)
    {
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [BpmnDemoPipeline.SixthSequenceFlowVisualId]))).Succeeded);
        return Assert.IsType<DocumentCanvasPropertySnapshot>(await test.Host.OpenPropertiesAsync(BpmnDemoPipeline.SixthSequenceFlowVisualId));
    }

    private static async Task DrainAsync(HostHarness test)
    {
        await CommandProcessor.WaitForEventDispatchIdleAsync(test.Composition.Document).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
    }

    private static ConnectorRoutingRecord Route(DocumentSnapshot snapshot) => snapshot.VisualModel.RoutingScopes!.Value
        .SelectMany(scope => scope.Connectors).Single(route => route.VisualStateId == BpmnDemoPipeline.SixthSequenceFlowVisualId);
    private static string? Name(DocumentSnapshot snapshot, SemanticElementId id) => snapshot.SemanticModel.Relationships
        .Single(item => item.Id == id).Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) ? name.TextValue : null;

    private sealed class Probe : ILayoutAlgorithm, IRoutingAlgorithm, IStableConnectorRoutingPolicy, IDocumentChangedSubscriber, ICommandValidator
    {
        private readonly BpmnLayoutAlgorithm _layout = new();
        private readonly BpmnRoutingAlgorithm _routing = new();
        internal int LayoutCalls, Routes, Assessments, Repairs;
        internal readonly List<DocumentChangedEvent> Events = [];
        internal readonly List<CommandTypeId> Validated = [];
        internal CommandTypeId? Reject;
        internal string? NameSeenByRoutingValidator;
        internal void Reset() { LayoutCalls = Routes = Assessments = Repairs = 0; Events.Clear(); Validated.Clear(); }
        public LayoutAlgorithmResult Compute(ProjectedGraph graph, LayoutContext context, CancellationToken cancellationToken)
        { LayoutCalls++; return _layout.Compute(graph, context, cancellationToken); }
        public RoutingAlgorithmResult Route(ProjectedGraph graph, LayoutResult layout, RoutingContext context, CancellationToken cancellationToken)
        { Routes++; return _routing.Route(graph, layout, context, cancellationToken); }
        public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate, CancellationToken cancellationToken = default)
        { Assessments++; return _routing.Assess(graph, logicalLayout, context, edgeId, candidate, cancellationToken); }
        public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ImmutableArray<ProjectedObjectId> orderedEdgeIds, CancellationToken cancellationToken = default)
        { Repairs++; return _routing.RouteAffected(graph, logicalLayout, context, orderedEdgeIds, cancellationToken); }
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change) { Events.Add(change); return ValueTask.CompletedTask; }
        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
        {
            Validated.Add(command.TypeId);
            if (command is SetConnectorRoutingTypeCommand)
            {
                var semanticId = document.VisualModel.VisualStates.Single(item => item.Id == BpmnDemoPipeline.SixthSequenceFlowVisualId).SemanticElementId;
                NameSeenByRoutingValidator = Name(document, semanticId);
            }
            return command.TypeId == Reject ? [new Diagnostic("TEST_PROPERTY_REJECTED", DiagnosticSeverity.Error, "Rejected property edit.")] : [];
        }
    }
}
