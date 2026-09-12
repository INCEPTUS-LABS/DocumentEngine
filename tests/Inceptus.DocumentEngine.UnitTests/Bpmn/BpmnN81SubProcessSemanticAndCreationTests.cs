using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN81SubProcessSemanticAndCreationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8.1:creation-document");
    private static readonly SemanticElementId SubProcessId =
        new("bpmn:n8.1:subprocess");
    private static readonly VisualStateId SubProcessVisualId =
        new("bpmn:n8.1:subprocess:visual");
    private static readonly DocumentScopeId ChildScopeId =
        new("bpmn:n8.1:subprocess:scope");

    [Fact]
    public void SubProcessHasOneCanonicalActivityClassificationAndIsNotATaskEventOrGateway()
    {
        Assert.Equal("BPMN.SubProcess", BpmnSemanticTypes.SubProcess.Value);
        Assert.Equal(
            [.. BpmnTaskSemanticTypes.All, BpmnSemanticTypes.SubProcess],
            BpmnActivitySemanticTypes.All.AsEnumerable());
        Assert.True(BpmnActivitySemanticTypes.IsActivity(BpmnSemanticTypes.SubProcess));
        Assert.False(BpmnTaskSemanticTypes.IsTask(BpmnSemanticTypes.SubProcess));
        Assert.False(BpmnSemanticTypes.IsEvent(BpmnSemanticTypes.SubProcess));
        Assert.DoesNotContain(
            BpmnSemanticTypes.SubProcess,
            new[]
            {
                BpmnSemanticTypes.ExclusiveGateway,
                BpmnSemanticTypes.ParallelGateway,
                BpmnSemanticTypes.InclusiveGateway,
                BpmnSemanticTypes.EventBasedGateway,
            });
    }

    [Fact]
    public void FactoryCreatesExactlyTheThreeSubProcessDataProperties()
    {
        var element = BpmnSemanticFactory.CreateSubProcess(
            SubProcessId,
            "PROCESS_ORDER",
            "Process order",
            "Processes one order.");

        Assert.Equal(BpmnSemanticTypes.SubProcess, element.TypeId);
        Assert.Equal(
            [
                BpmnSemanticProperties.Code,
                BpmnSemanticProperties.Description,
                BpmnSemanticProperties.Name,
            ],
            element.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("PROCESS_ORDER",
            element.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Process order",
            element.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal("Processes one order.",
            element.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.DoesNotContain(BpmnSemanticProperties.ElementNumber, element.Properties.Keys);
        Assert.DoesNotContain(element.Properties.Keys, key =>
            key.Contains("Scope", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Expanded", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RootCreationIsOneAtomicHistoryUnitWithOneExactOwnedChildScope()
    {
        var harness = CreateEmptyHarness();
        var before = harness.Document.CaptureSnapshot();
        var command = Command(
            before.SemanticModel.RootScopeId,
            ChildScopeId,
            before.Revision);

        var result = await harness.History.ExecuteAsync(harness.Processor, command);
        await CommandProcessor.WaitForEventDispatchIdleAsync(harness.Document);

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), committed.Revision);
        Assert.Equal(1, committed.SemanticModel.ElementCount);
        Assert.Equal(0, committed.SemanticModel.RelationshipCount);
        Assert.Equal(1, committed.VisualModel.Count);
        Assert.Single(committed.SemanticModel.NestedScopes);
        Assert.Empty(committed.SemanticModel.ScopeMemberships);
        Assert.Equal(before.SemanticModel.RootScopeId,
            committed.SemanticModel.GetScope(SubProcessId).Id);

        var child = Assert.Single(committed.SemanticModel.NestedScopes);
        Assert.Equal(ChildScopeId, child.Id);
        Assert.Equal(before.SemanticModel.RootScopeId, child.ParentScopeId);
        Assert.Equal(SubProcessId, child.OwnerSemanticElementId);
        var visual = Assert.Single(committed.VisualModel.VisualStates);
        Assert.Equal(SubProcessVisualId, visual.Id);
        Assert.Equal(SubProcessId, visual.SemanticElementId);
        Assert.Equal(new PointD(80d, 60d), visual.Position);
        Assert.Equal(new SizeD(120d, 80d), visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Empty(visual.ConnectorAnchors);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        Assert.Single(harness.Subscriber.Events);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        var undone = harness.Document.CaptureSnapshot();
        Assert.Empty(undone.SemanticModel.Elements);
        Assert.Empty(undone.SemanticModel.NestedScopes);
        Assert.Empty(undone.SemanticModel.ScopeMemberships);
        Assert.Empty(undone.VisualModel.VisualStates);

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        var redone = harness.Document.CaptureSnapshot();
        Assert.Equal(committed.SemanticModel.Elements.AsEnumerable(),
            redone.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(committed.SemanticModel.NestedScopes.AsEnumerable(),
            redone.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(committed.SemanticModel.ScopeMemberships.AsEnumerable(),
            redone.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(),
            redone.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task NestedCreationUsesTheExplicitParentWithoutTransformingGeometry()
    {
        var parentId = new SemanticElementId("bpmn:n8.1:parent-subprocess");
        var parentScopeId = new DocumentScopeId("bpmn:n8.1:parent-scope");
        var rootScopeId = new DocumentScopeId(DocumentId.Value);
        var snapshot = Snapshot(
            elements:
            [
                BpmnSemanticFactory.CreateSubProcess(
                    parentId,
                    "PARENT",
                    "Parent"),
            ],
            visuals:
            [
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:parent-subprocess:visual"),
                    parentId,
                    new PointD(500d, 400d)),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(parentScopeId, rootScopeId, parentId),
            ]);
        var harness = CreateHarness(snapshot);
        var command = Command(parentScopeId, ChildScopeId, harness.Document.Revision);

        var result = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(parentScopeId, committed.SemanticModel.GetScope(SubProcessId).Id);
        Assert.Contains(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == SubProcessId &&
            membership.ScopeId == parentScopeId);
        var child = Assert.Single(committed.SemanticModel.NestedScopes, scope =>
            scope.Id == ChildScopeId);
        Assert.Equal(parentScopeId, child.ParentScopeId);
        Assert.Equal(SubProcessId, child.OwnerSemanticElementId);
        var visual = Assert.Single(committed.VisualModel.VisualStates, candidate =>
            candidate.Id == SubProcessVisualId);
        Assert.Equal(new PointD(80d, 60d), visual.Position);
        Assert.Equal(new SizeD(120d, 80d), visual.Size);
    }

    [Fact]
    public async Task AllThreeDataPropertiesApplyAndUndoRedoWithoutChangingContainment()
    {
        var rootScopeId = new DocumentScopeId(DocumentId.Value);
        var snapshot = Snapshot(
            elements:
            [
                BpmnSemanticFactory.CreateSubProcess(
                    SubProcessId,
                    "PROCESS_ORDER",
                    "Process order",
                    "Original description."),
            ],
            visuals:
            [
                NodeVisual(SubProcessVisualId, SubProcessId, new PointD(80d, 60d)),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(ChildScopeId, rootScopeId, SubProcessId),
            ]);
        var harness = CreateHarness(snapshot);

        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                SubProcessId,
                BpmnSemanticProperties.Code,
                PropertyValue.FromText("FULFILL_ORDER")))).IsCommitted);
        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementNameCommand(
                DocumentId,
                harness.Document.Revision,
                SubProcessId,
                BpmnSemanticProperties.Name,
                "Fulfill order"))).IsCommitted);
        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                SubProcessId,
                BpmnSemanticProperties.Description,
                PropertyValue.FromText("Updated description.")))).IsCommitted);
        var updated = harness.Document.CaptureSnapshot();
        var updatedElement = Assert.Single(updated.SemanticModel.Elements);
        Assert.Equal("FULFILL_ORDER",
            updatedElement.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Fulfill order",
            updatedElement.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal("Updated description.",
            updatedElement.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.Equal(snapshot.SemanticModel.NestedScopes.AsEnumerable(),
            updated.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(snapshot.SemanticModel.ScopeMemberships.AsEnumerable(),
            updated.SemanticModel.ScopeMemberships.AsEnumerable());

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        var original = Assert.Single(harness.Document.SemanticModel.Elements);
        Assert.Equal(snapshot.SemanticModel.Elements.Single(), original);
        Assert.Equal(snapshot.SemanticModel.NestedScopes.AsEnumerable(),
            harness.Document.SemanticModel.NestedScopes.AsEnumerable());

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        var redone = harness.Document.CaptureSnapshot();
        Assert.Equal(updated.SemanticModel.Elements.AsEnumerable(),
            redone.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(updated.SemanticModel.NestedScopes.AsEnumerable(),
            redone.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(updated.VisualModel.VisualStates.AsEnumerable(),
            redone.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task InvalidScopeIdentityOwnershipGeometryAndSemanticInputsAreAtomic()
    {
        var existingId = new SemanticElementId("bpmn:n8.1:existing-task");
        var existingVisualId = new VisualStateId("bpmn:n8.1:existing-task:visual");
        var existingScopeId = new DocumentScopeId("bpmn:n8.1:existing-scope");
        var rootScopeId = new DocumentScopeId(DocumentId.Value);
        var snapshot = Snapshot(
            elements:
            [
                BpmnSemanticFactory.CreateTask(existingId, "EXISTING", "Existing", 1),
            ],
            visuals:
            [
                NodeVisual(existingVisualId, existingId, new PointD(20d, 20d)),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(existingScopeId, rootScopeId, existingId),
            ]);

        var invalidCommands = new (CreateBpmnSubProcessCommand Command, string Code)[]
        {
            (Command(
                new DocumentScopeId("bpmn:n8.1:missing-parent"),
                ChildScopeId,
                snapshot.Revision),
                BpmnCommandDiagnosticCodes.SubProcessParentScopeInvalid),
            (Command(rootScopeId, existingScopeId, snapshot.Revision),
                BpmnCommandDiagnosticCodes.SubProcessChildScopeInvalid),
            (Command(rootScopeId, rootScopeId, snapshot.Revision),
                BpmnCommandDiagnosticCodes.SubProcessChildScopeInvalid),
            (Command(
                rootScopeId,
                ChildScopeId,
                snapshot.Revision,
                elementId: existingId),
                BpmnCommandDiagnosticCodes.DuplicateSemanticId),
            (Command(
                rootScopeId,
                ChildScopeId,
                snapshot.Revision,
                visualStateId: existingVisualId),
                BpmnCommandDiagnosticCodes.DuplicateVisualId),
            (Command(
                rootScopeId,
                ChildScopeId,
                snapshot.Revision,
                size: new SizeD(0d, 80d)),
                CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid),
            (Command(
                rootScopeId,
                ChildScopeId,
                snapshot.Revision,
                code: ""),
                BpmnCommandDiagnosticCodes.InvalidSubProcess),
        };

        foreach (var (command, diagnosticCode) in invalidCommands)
        {
            var harness = CreateHarness(snapshot);
            var before = harness.Document.CaptureSnapshot();
            var historyBefore = harness.History.CaptureStatus();

            var result = await harness.History.ExecuteAsync(harness.Processor, command);

            Assert.False(result.IsCommitted);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == diagnosticCode);
            Assert.Same(before, harness.Document.CaptureSnapshot());
            Assert.Equal(snapshot.Revision, harness.Document.Revision);
            Assert.Equal(historyBefore, harness.History.CaptureStatus());
            Assert.Empty(harness.Subscriber.Events);
            Assert.False(harness.Document.SemanticModel.TryGetElement(SubProcessId, out _));
            Assert.DoesNotContain(harness.Document.SemanticModel.NestedScopes, scope =>
                scope.Id == ChildScopeId);
        }
    }

    [Fact]
    public async Task ExistingOwnerCannotAcquireASecondChildScopeThroughCreation()
    {
        var rootScopeId = new DocumentScopeId(DocumentId.Value);
        var firstChildScopeId = new DocumentScopeId("bpmn:n8.1:first-child");
        var snapshot = Snapshot(
            elements:
            [
                BpmnSemanticFactory.CreateSubProcess(
                    SubProcessId,
                    "EXISTING_SUBPROCESS",
                    "Existing SubProcess"),
            ],
            visuals:
            [
                NodeVisual(SubProcessVisualId, SubProcessId, new PointD(20d, 20d)),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(firstChildScopeId, rootScopeId, SubProcessId),
            ]);
        var harness = CreateHarness(snapshot);
        var before = harness.Document.CaptureSnapshot();
        var command = Command(
            rootScopeId,
            new DocumentScopeId("bpmn:n8.1:second-child"),
            snapshot.Revision,
            visualStateId: new VisualStateId("bpmn:n8.1:second:visual"));

        var result = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SubProcessChildScopeInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Single(harness.Document.SemanticModel.NestedScopes);
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    private static CreateBpmnSubProcessCommand Command(
        DocumentScopeId parentScopeId,
        DocumentScopeId childScopeId,
        DocumentRevision revision,
        SemanticElementId? elementId = null,
        VisualStateId? visualStateId = null,
        SizeD? size = null,
        string code = "PROCESS_ORDER") =>
        new(
            DocumentId,
            revision,
            elementId ?? SubProcessId,
            visualStateId ?? SubProcessVisualId,
            parentScopeId,
            childScopeId,
            new PointD(80d, 60d),
            size ?? new SizeD(120d, 80d),
            code,
            "Process order",
            VisualPlacementMode.Pinned,
            "Processes one order.");

    private static Harness CreateEmptyHarness()
    {
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: provider);
        var document = Assert.IsType<Document>(construction.Document);
        return CreateHarness(document, registration, provider);
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(snapshot, provider);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        return CreateHarness(
            Assert.IsType<Document>(construction.Document),
            registration,
            provider);
    }

    private static Harness CreateHarness(
        Document document,
        BpmnPluginRegistration registration,
        IElementConnectorAnchorPolicyProvider provider)
    {
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: [subscriber],
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        return new Harness(
            document,
            processor,
            new HistoryManager(document),
            subscriber);
    }

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<VisualStateSnapshot> visuals,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships: null,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned);

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History,
        RecordingSubscriber Subscriber);
}
