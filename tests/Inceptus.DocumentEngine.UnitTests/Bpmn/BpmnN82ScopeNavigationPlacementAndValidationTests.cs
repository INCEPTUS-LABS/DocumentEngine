using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN82ScopeNavigationPlacementAndValidationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8.2:document");
    private static readonly SemanticElementId OwnerId = new("bpmn:n8.2:owner");
    private static readonly VisualStateId OwnerVisualId = new("bpmn:n8.2:owner:visual");
    private static readonly DocumentScopeId ChildScopeId = new("bpmn:n8.2:child-scope");

    [Fact]
    public void N82RegistersSubProcessNavigationWithCurrentOwnedScopeAndReadableLabel()
    {
        var snapshot = ParentWithEmptyChildScope();
        var registration = Assert.Single(
            BpmnPluginRegistration.N82.ScopeNavigationRegistrations);
        var owner = Assert.Single(snapshot.SemanticModel.Elements);

        Assert.Equal(BpmnSemanticTypes.SubProcess, registration.SemanticTypeId);
        Assert.Equal("Open SubProcess", registration.OpenActionLabel);
        Assert.True(registration.Contribution.TryResolveTargetScope(
            snapshot,
            owner,
            out var targetScopeId));
        Assert.Equal(ChildScopeId, targetScopeId);
        Assert.Equal(
            "Process order",
            registration.Contribution.ResolveBreadcrumbLabel(snapshot, owner));
        var codeOnlyOwner = new SemanticElementSnapshot(
            OwnerId,
            BpmnSemanticTypes.SubProcess,
            [
                new(BpmnSemanticProperties.Code, PropertyValue.FromText("PROCESS_ORDER")),
            ]);
        var unnamedOwner = new SemanticElementSnapshot(
            OwnerId,
            BpmnSemanticTypes.SubProcess);
        Assert.Equal(
            "PROCESS_ORDER",
            registration.Contribution.ResolveBreadcrumbLabel(snapshot, codeOnlyOwner));
        Assert.Equal(
            "SubProcess",
            registration.Contribution.ResolveBreadcrumbLabel(snapshot, unnamedOwner));

        var malformed = Snapshot(
            [owner],
            [NodeVisual(OwnerVisualId, OwnerId)],
            nestedScopes: null,
            memberships: null);
        Assert.False(registration.Contribution.TryResolveTargetScope(
            malformed,
            owner,
            out targetScopeId));
        Assert.Null(targetScopeId);
    }

    [Fact]
    public async Task EveryToolboxNodeFamilyTargetsTheRequestedNestedScope()
    {
        var registration = BpmnPluginRegistration.N82;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(
            ParentWithEmptyChildScope(),
            anchorPolicies);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var history = new HistoryManager(document);
        var ordinal = 0;

        foreach (var placement in registration.ToolboxPlacementRegistrations)
        {
            ordinal++;
            var semanticId = new SemanticElementId($"bpmn:n8.2:placed:{ordinal}");
            var visualId = new VisualStateId($"bpmn:n8.2:placed:{ordinal}:visual");
            var snapshot = document.CaptureSnapshot();
            var planResult = placement.CommandFactory.CreatePlan(
                new ToolboxPlacementRequest(
                    placement.ToolboxItemId,
                    snapshot,
                    snapshot.Revision,
                    new PointD(300d + ordinal, 240d + ordinal),
                    new FixedIdentityProvider(new DocumentCreationIdentity(
                        semanticId,
                        visualId)),
                    ChildScopeId));

            Assert.True(planResult.Succeeded, Diagnostics(planResult.Diagnostics));
            var plan = Assert.IsType<ToolboxPlacementPlan>(planResult.Plan);
            var creation = Assert.IsAssignableFrom<BpmnElementCreationCommand>(plan.Command);
            Assert.Equal(ChildScopeId, creation.TargetScopeId);
            if (creation is CreateBpmnSubProcessCommand subProcess)
            {
                Assert.Equal(ChildScopeId, subProcess.ParentScopeId);
            }

            var result = await history.ExecuteAsync(processor, plan.Command);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            var committed = document.CaptureSnapshot();
            Assert.Equal(ChildScopeId, committed.SemanticModel.GetScope(semanticId).Id);
            Assert.Contains(committed.SemanticModel.ScopeMemberships, membership =>
                membership == new SemanticElementScopeMembershipSnapshot(
                    semanticId,
                    ChildScopeId));
        }

        Assert.Equal(
            registration.ToolboxPlacementRegistrations.Length,
            document.SemanticModel.ScopeMemberships.Count(membership =>
                membership.ScopeId == ChildScopeId));
        Assert.Equal(
            registration.ToolboxPlacementRegistrations.Length,
            history.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task NestedOrdinaryCreationUndoRedoRestoresExactSparseMembership()
    {
        var registration = BpmnPluginRegistration.N82;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(
            ParentWithEmptyChildScope(),
            anchorPolicies);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var history = new HistoryManager(document);
        var before = document.CaptureSnapshot();
        var taskId = new SemanticElementId("bpmn:n8.2:nested-task");
        var taskVisualId = new VisualStateId("bpmn:n8.2:nested-task:visual");
        var command = new CreateBpmnTaskCommand(
            DocumentId,
            before.Revision,
            taskId,
            taskVisualId,
            new PointD(120d, 100d),
            new SizeD(120d, 80d),
            "NESTED_TASK",
            "Nested task",
            1,
            targetScopeId: ChildScopeId);

        var result = await history.ExecuteAsync(processor, command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = document.CaptureSnapshot();
        Assert.Equal(
            [new SemanticElementScopeMembershipSnapshot(taskId, ChildScopeId)],
            committed.SemanticModel.ScopeMemberships
                .Where(membership => membership.SemanticElementId == taskId));

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        var undone = document.CaptureSnapshot();
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            undone.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Relationships.AsEnumerable(),
            undone.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.NestedScopes.AsEnumerable(),
            undone.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.ScopeMemberships.AsEnumerable(),
            undone.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(
            before.VisualModel.VisualStates.AsEnumerable(),
            undone.VisualModel.VisualStates.AsEnumerable());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        var redone = document.CaptureSnapshot();
        Assert.Equal(
            committed.SemanticModel.ScopeMemberships.AsEnumerable(),
            redone.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(
            committed.SemanticModel.Elements.AsEnumerable(),
            redone.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            committed.VisualModel.VisualStates.AsEnumerable(),
            redone.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task MissingOrdinaryCreationTargetScopeIsRejectedAtomically()
    {
        var registration = BpmnPluginRegistration.N82;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(
            ParentWithEmptyChildScope(),
            anchorPolicies);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var before = document.CaptureSnapshot();
        var result = await processor.ExecuteAsync(
            document,
            new CreateBpmnTaskCommand(
                DocumentId,
                before.Revision,
                new SemanticElementId("bpmn:n8.2:invalid-scope-task"),
                new VisualStateId("bpmn:n8.2:invalid-scope-task:visual"),
                new PointD(120d, 100d),
                new SizeD(120d, 80d),
                "INVALID_SCOPE_TASK",
                "Invalid scope task",
                1,
                targetScopeId: new DocumentScopeId("bpmn:n8.2:missing-scope")));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.ElementTargetScopeInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
    }

    [Fact]
    public void ChildValidationUsesOnlyTheActiveScopeAndSuppressesRootCompleteness()
    {
        var rootTaskId = new SemanticElementId("bpmn:n8.2:root-task");
        var childTaskId = new SemanticElementId("bpmn:n8.2:child-task");
        var snapshot = Snapshot(
            [
                BpmnSemanticFactory.CreateSubProcess(
                    OwnerId,
                    "PROCESS_ORDER",
                    "Process order"),
                BpmnSemanticFactory.CreateTask(rootTaskId, "ROOT", "Root task", 1),
                BpmnSemanticFactory.CreateTask(childTaskId, "CHILD", "Child task", 2),
            ],
            [
                NodeVisual(OwnerVisualId, OwnerId),
                NodeVisual(new VisualStateId("bpmn:n8.2:root-task:visual"), rootTaskId),
                NodeVisual(new VisualStateId("bpmn:n8.2:child-task:visual"), childTaskId),
            ],
            [new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId(DocumentId.Value),
                OwnerId)],
            [new SemanticElementScopeMembershipSnapshot(childTaskId, ChildScopeId)]);

        var issues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(snapshot, ChildScopeId));

        Assert.DoesNotContain(issues, issue =>
            issue.Code == BpmnModelValidationCodes.NoStartEvent ||
            issue.Code == BpmnModelValidationCodes.NoEndEvent);
        var isolated = Assert.Single(issues, issue =>
            issue.Code == BpmnModelValidationCodes.FlowNodeIsolated);
        Assert.Equal(childTaskId, isolated.Target.SemanticElementId);
        Assert.DoesNotContain(issues, issue =>
            issue.Target.SemanticElementId == rootTaskId);
    }

    [Fact]
    public void ChildValidationPreservesEventBasedGatewayConfigurationRules()
    {
        var owner = BpmnSemanticFactory.CreateSubProcess(
            OwnerId,
            "PROCESS_ORDER",
            "Process order");
        var gateway = new SemanticElementSnapshot(
            new SemanticElementId("bpmn:n8.2:child-event-gateway"),
            BpmnSemanticTypes.EventBasedGateway);
        var signal = new SemanticElementSnapshot(
            new SemanticElementId("bpmn:n8.2:child-signal-catch"),
            BpmnSemanticTypes.SignalCatchEvent);
        var message = new SemanticElementSnapshot(
            new SemanticElementId("bpmn:n8.2:child-message-catch"),
            BpmnSemanticTypes.MessageCatchEvent);
        var receive = new SemanticElementSnapshot(
            new SemanticElementId("bpmn:n8.2:child-receive"),
            BpmnSemanticTypes.ReceiveTask);
        var signalThrow = new SemanticElementSnapshot(
            new SemanticElementId("bpmn:n8.2:child-signal-throw"),
            BpmnSemanticTypes.SignalThrowEvent);
        var task = new SemanticElementSnapshot(
            new SemanticElementId("bpmn:n8.2:child-ordinary-task"),
            BpmnSemanticTypes.Task);
        var elements = new[] { owner, gateway, signal, message, receive, signalThrow, task };
        var memberships = elements
            .Where(element => element.Id != OwnerId)
            .Select(element => new SemanticElementScopeMembershipSnapshot(
                element.Id,
                ChildScopeId))
            .ToArray();
        var nestedScopes = new[]
        {
            new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId(DocumentId.Value),
                OwnerId),
        };

        DocumentSnapshot ChildSnapshot(params SemanticRelationshipSnapshot[] flows) =>
            Snapshot(
                elements,
                [NodeVisual(OwnerVisualId, OwnerId)],
                nestedScopes,
                memberships,
                flows);
        SemanticRelationshipSnapshot Flow(
            string id,
            SemanticElementSnapshot source,
            SemanticElementSnapshot target) =>
            new(new SemanticElementId(id), BpmnSemanticTypes.SequenceFlow, source.Id, target.Id);
        IReadOnlyList<ModelValidationIssue> Validate(params SemanticRelationshipSnapshot[] flows) =>
            new BpmnStructuralValidationRule().Validate(
                new ModelValidationContext(ChildSnapshot(flows), ChildScopeId));

        var validSignal = Validate(Flow("child-gateway-signal", gateway, signal));
        Assert.DoesNotContain(validSignal, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget ||
            issue.Code == BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming ||
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayMixedMessageReceptionModes);

        Assert.Single(
            Validate(Flow("child-gateway-throw", gateway, signalThrow)),
            issue => issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget);
        Assert.Single(
            Validate(
                Flow("child-gateway-message", gateway, message),
                Flow("child-gateway-receive", gateway, receive)),
            issue => issue.Code == BpmnModelValidationCodes
                .EventBasedGatewayMixedMessageReceptionModes);
        Assert.Single(
            Validate(
                Flow("child-gateway-signal", gateway, signal),
                Flow("child-task-signal", task, signal)),
            issue => issue.Code == BpmnModelValidationCodes
                .EventBasedTargetAdditionalIncoming);
    }

    private static DocumentSnapshot ParentWithEmptyChildScope() => Snapshot(
        [BpmnSemanticFactory.CreateSubProcess(
            OwnerId,
            "PROCESS_ORDER",
            "Process order")],
        [NodeVisual(OwnerVisualId, OwnerId)],
        [new DocumentScopeSnapshot(
            ChildScopeId,
            new DocumentScopeId(DocumentId.Value),
            OwnerId)],
        memberships: null);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<VisualStateSnapshot> visuals,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships,
        IEnumerable<SemanticRelationshipSnapshot>? relationships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(40d, 40d),
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned);

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class FixedIdentityProvider(DocumentCreationIdentity identity) :
        IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => identity;
    }
}
