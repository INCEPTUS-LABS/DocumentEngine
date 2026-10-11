using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
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
    public void A24Localization(string language)
    {
        using var culture = new ModelerCultureScope(language);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        foreach (var key in new[] { "ElementToolbox_Actions", "ElementToolbox_Create", "ElementToolbox_AutoPoints",
            "ElementToolbox_PropertiesHint", "ElementToolbox_Failed", "Toolbox_Events", "Toolbox_Activities", "Toolbox_Gateways" })
            Assert.False(text[key].ResourceNotFound, language + ": " + key);
        foreach (var candidate in BpmnSmartNextCatalog.GetCandidates(A24Snapshot(), new("source"), A24Snapshot().SemanticModel.RootScopeId))
            Assert.False(text[ModelerLabels.ToolboxKey(candidate.ItemId.Value)!].ResourceNotFound);
    }

    [Theory]
    [InlineData("routing")]
    [InlineData("throw")]
    [InlineData("invariant")]
    public async Task A24PreparationFailuresAreAtomic(string fault)
    {
        var composition = await new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()).CreateAsync();
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-failure", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var original = AttachedDocument(Session(host)).CaptureSnapshot();
        var configuration = composition.Configuration;
        var command = await new BpmnSmartCreatePlanner(configuration.CommandHandlers, configuration.CommandValidators)
            .PlanAsync(original, new("v-source"), new("bpmn:toolbox:task"), GuidDocumentCreationIdentityProvider.Instance);
        var document = DocumentFactory.Create(original, configuration.ConnectorAnchorPolicyProvider).Document!;
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor(configuration.CommandHandlers, configuration.CommandValidators, null,
            configuration.HistoryPolicies, configuration.ConnectorAnchorPolicyProvider, new A23FaultPreparer(fault));
        var result = await history.ExecuteAsync(processor, command);
        Assert.False(result.IsCommitted);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.Equal(new VisualStateId("v-source"), Assert.Single(Session(host).CaptureState().EditorState.Selection));
    }

    [Theory]
    [InlineData("creation")]
    [InlineData("membership")]
    [InlineData("flow")]
    [InlineData("visual")]
    [InlineData("anchor")]
    [InlineData("placement")]
    [InlineData("duplicate-identity")]
    [InlineData("cancelled")]
    public async Task A24PlanningFailureLeavesSourceAndAuthorityUnchanged(string fault)
    {
        var snapshot = fault == "membership" ? A22PoolSnapshot() : A24Snapshot();
        if (fault == "placement")
        {
            var blocker = new Contracts.Semantics.SemanticElementSnapshot(new("blocker"), BpmnSemanticTypes.Task,
                [new(BpmnSemanticProperties.Name, Contracts.Properties.PropertyValue.FromText("Blocker"))]);
            snapshot = new(new Contracts.Semantics.SemanticModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.SemanticModel.Elements.Append(blocker)),
                new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, snapshot.VisualModel.VisualStates.Append(
                    new(new("v-blocker"), blocker.Id, new(320, 0), new(500, 10000), VisualPlacementMode.Pinned))), snapshot.Metadata);
        }
        var composition = await new BpmnModelerCompositionFactory(initialDocument: snapshot).CreateAsync();
        await using var host = A23Host(snapshot);
        await host.InitializeAsync("a24-plan-failure", "container");
        var source = new VisualStateId(fault == "membership" ? "sales-task-visual" : "v-source");
        await A24Select(host, source);
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var failedType = fault == "membership" ? AssignOrganizationalElementCommand.KnownTypeId :
            fault == "flow" ? CreateBpmnElementFlowCommand.KnownTypeId : CreateBpmnTaskCommand.KnownTypeId;
        var handlers = composition.Configuration.CommandHandlers.Select(registration =>
            fault is "creation" or "membership" or "flow" && registration.TypeId == failedType
                ? new CommandHandlerRegistration(registration.TypeId, registration.EnvelopeValidator, new A24FailingHandler()) : registration);
        var planner = new BpmnSmartCreatePlanner(handlers, composition.Configuration.CommandValidators);
        var identity = fault == "duplicate-identity" ? (IDocumentCreationIdentityProvider)new A24DuplicateIdentity() :
            fault is "visual" or "anchor" ? new A24InvalidPartIdentity(fault) : GuidDocumentCreationIdentityProvider.Instance;
        await Assert.ThrowsAnyAsync<Exception>(async () => await planner.PlanAsync(before, source, new("bpmn:toolbox:task"), identity,
            new CancellationToken(fault == "cancelled")));
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(source, Assert.Single(Session(host).CaptureState().EditorState.Selection));
    }

    [Fact]
    public async Task A24FocusedHistoryPreservesLaterUnrelatedPropertyAndRejectsStalePlan()
    {
        var composition = await new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()).CreateAsync();
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-focused-history", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var planner = new BpmnSmartCreatePlanner(composition.Configuration.CommandHandlers, composition.Configuration.CommandValidators);
        var command = await planner.PlanAsync(before, new("v-source"), new("bpmn:toolbox:task"), GuidDocumentCreationIdentityProvider.Instance);
        Assert.True((await session.ExecuteAsync(command)).IsCommitted);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var policy = BpmnPluginRegistration.N100.HistoryPolicies.Single(policy => policy.TypeId == command.TypeId).Policy;
        var history = policy.Prepare(command, before, after);
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new("source"), BpmnSemanticProperties.Name, "Later unrelated name"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.False((await session.ExecuteAsync(command)).IsCommitted);
        var undo = await session.ExecuteAsync(history.UndoFactory!.Create(document.DocumentId, document.Revision));
        Assert.True(undo.IsCommitted, string.Join("; ", undo.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        await session.WaitForIdleAsync();
        Assert.Equal("Later unrelated name", document.SemanticModel.Elements.Single().Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.True((await session.ExecuteAsync(history.RedoFactory!.Create(document.DocumentId, document.Revision))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal("Later unrelated name", document.SemanticModel.Elements.Single(element => element.Id.Value == "source").Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(after.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(after.VisualModel.RoutingScopes!.Value.Single().Geometry.Nodes.AsEnumerable(),
            document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Nodes);
        Assert.Equal(after.VisualModel.EnumerateConnectorRoutingRecords(), document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords());
    }

    [Theory]
    [InlineData("BPMN.TimerBoundaryEvent")]
    [InlineData("BPMN.MessageBoundaryEvent")]
    public async Task A24BoundarySourceRemainsAttached(string type)
    {
        await using var host = A23Host(CreatePropertiesUxSnapshot(type));
        await host.InitializeAsync("a24-boundary", "container");
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        var boundary = before.VisualModel.VisualStates.Single(visual => visual.BoundaryAttachment is not null);
        await A24Select(host, boundary.Id);
        var result = await host.SmartCreateAsync(new("bpmn:toolbox:task"));
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        var after = document.CaptureSnapshot();
        var current = after.VisualModel.VisualStates.Single(visual => visual.Id == boundary.Id);
        Assert.Equal(boundary.Position, current.Position);
        Assert.Equal(boundary.BoundaryAttachment, current.BoundaryAttachment);
    }

    [Fact]
    public async Task A24PropertiesAndDeleteUseExistingAuthorities()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-actions", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var properties = await host.OpenPropertiesAsync(new VisualStateId("v-source"));
        Assert.NotNull(properties);
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
    }

    [Fact]
    public async Task A24DeleteRemovesIncidentFlowAndUndoRestoresIt()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("a24-delete", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))!.IsCommitted);
        var document = AttachedDocument(Session(host));
        var before = document.CaptureSnapshot();
        var result = await host.DeleteToolboxElementAsync();
        Assert.True(result?.IsCommitted, A24Diagnostics(host, result));
        Assert.Single(document.SemanticModel.Elements);
        Assert.Empty(document.SemanticModel.Relationships);
        Assert.Null(host.CaptureState().ElementToolbox);
        await host.UndoAsync();
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.SemanticModel.Relationships);
    }

    private sealed class A24FailingHandler : ICommandHandler
    {
        public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document, CancellationToken cancellationToken) =>
            ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic("test:a24:failure", DiagnosticSeverity.Error, "Injected failure.")]));
    }
    private sealed class A24DuplicateIdentity : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => new(new("source"), new("v-source"));
        public ConnectorAnchorId CreateConnectorAnchorId() => new("duplicate-anchor");
    }
    private sealed class A24InvalidPartIdentity(string fault) : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity()
        {
            var identity = GuidDocumentCreationIdentityProvider.Instance.CreateIdentity();
            return fault == "visual" ? new(identity.SemanticElementId, new("v-source")) : identity;
        }
        public ConnectorAnchorId CreateConnectorAnchorId() => new("same-anchor-for-two-endpoints");
    }
}
