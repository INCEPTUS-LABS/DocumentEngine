using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnA21ToolboxTests
{
    [Fact]
    public void CurrentToolboxExposesOnlyFamiliesWhileAllConcreteTypesRemainSupported()
    {
        var registration = BpmnPluginRegistration.N100;
        var catalog = new ToolboxCatalog(registration.ToolboxContributions);
        Assert.Equal(["Events", "Activities", "Gateways", "Flow"],
            catalog.Groups.Select(static group => group.DisplayName));
        Assert.Equal(
            ["Start Event", "Catch Event", "Throw Event", "Boundary Event", "End Event", "Task", "SubProcess", "Gateway"],
            catalog.Groups.SelectMany(group => catalog.GetItems(group.GroupId))
                .Select(static item => item.DisplayName));
        var placement = new ToolboxPlacementCatalog(registration.ToolboxPlacementRegistrations, catalog);
        Assert.Equal(catalog.Items.Select(static item => item.ItemId).OrderBy(static id => id.Value),
            placement.Registrations.Select(static item => item.ToolboxItemId));

        var previous = new ToolboxCatalog(BpmnPluginRegistration.N91.ToolboxContributions);
        var hiddenVariants = previous.Items.Where(item => !catalog.TryGetItem(item.ItemId, out _)).ToArray();
        Assert.NotEmpty(hiddenVariants);
        Assert.All(hiddenVariants, item =>
        {
            Assert.Contains(registration.ProjectionRules, rule => rule.SemanticTypeId == item.ElementTypeId);
            Assert.Contains(registration.PropertiesSchemas, schema => schema.SemanticTypeId == item.ElementTypeId);
        });
    }

    [Theory]
    [InlineData("Start Event", "BPMN.StartEvent")]
    [InlineData("Catch Event", "BPMN.MessageCatchEvent")]
    [InlineData("Throw Event", "BPMN.MessageThrowEvent")]
    [InlineData("Boundary Event", "BPMN.MessageBoundaryEvent")]
    [InlineData("End Event", "BPMN.EndEvent")]
    [InlineData("Task", "BPMN.Task")]
    [InlineData("SubProcess", "BPMN.SubProcess")]
    [InlineData("Gateway", "BPMN.ExclusiveGateway")]
    public async Task FamilyPreviewAndPlacementCreateTheRequiredConcreteDefault(string label, string type)
    {
        var registration = BpmnPluginRegistration.N100;
        var catalog = new ToolboxCatalog(registration.ToolboxContributions);
        var item = Assert.Single(catalog.Items, item => item.DisplayName == label);
        Assert.Equal(type, item.ElementTypeId.Value);
        var placement = Assert.Single(registration.ToolboxPlacementRegistrations,
            entry => entry.ToolboxItemId == item.ItemId);
        var policies = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(new DocumentId("a21:toolbox"),
            connectorAnchorPolicyProvider: policies).Document);
        var processor = new CommandProcessor(registration.CommandHandlers, registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies, connectorAnchorPolicyProvider: policies);
        var hostId = new SemanticElementId("a21:host");
        var hostVisualId = new VisualStateId("a21:host:visual");
        var hostBounds = new RectD(100d, 100d, 120d, 80d);
        var isBoundary = BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(item.ElementTypeId);
        if (isBoundary)
        {
            Assert.True((await processor.ExecuteAsync(document, new CreateBpmnTaskCommand(
                document.DocumentId, document.Revision, hostId, hostVisualId, hostBounds.TopLeft,
                hostBounds.Size, "HOST", "Host", 1, VisualPlacementMode.Pinned))).IsCommitted);
        }

        ToolboxPlacementTarget[] targets = isBoundary
            ? [new(hostId, BpmnSemanticTypes.Task, hostVisualId, new("a21:host:projected"), hostBounds)]
            : [];
        var pointer = isBoundary ? new PointD(160d, 180d) : new PointD(500d, 300d);
        var before = document.CaptureSnapshot();
        var identities = new Identities();
        var preview = placement.PreviewProvider!.Evaluate(new(item.ItemId, before, pointer, visibleTargets: targets));
        Assert.True(preview.IsAllowed);
        Assert.Equal(item.ElementTypeId, preview.SemanticTypeId);
        Assert.Equal(0, identities.Count);
        Assert.Same(before.SemanticModel, document.CaptureSnapshot().SemanticModel);
        var planResult = placement.CommandFactory.CreatePlan(new(item.ItemId, before, before.Revision,
            pointer, identities, visibleTargets: targets, candidate: preview.AttachmentCandidate));
        Assert.True(planResult.Succeeded);
        Assert.Equal(1, identities.Count);
        var plan = Assert.IsType<ToolboxPlacementPlan>(planResult.Plan);
        Assert.True((await processor.ExecuteAsync(document, plan.Command)).IsCommitted);
        var after = document.CaptureSnapshot();
        var created = Assert.Single(after.SemanticModel.Elements, element => element.Id == plan.CreatedSemanticElementId);
        Assert.Equal(item.ElementTypeId, created.TypeId);
        var visual = Assert.Single(after.VisualModel.VisualStates, state => state.Id == plan.CreatedVisualStateId);
        Assert.Equal(created.Id, visual.SemanticElementId);
        Assert.Equal(preview.Bounds.TopLeft, visual.Position);
        Assert.Equal(preview.Bounds.Size, visual.Size);
        Assert.Equal(isBoundary ? VisualPlacementMode.Manual : VisualPlacementMode.Pinned, visual.PlacementMode);
        if (isBoundary)
        {
            Assert.Equal(hostId, Assert.IsType<CreateBpmnMessageBoundaryEventCommand>(plan.Command).AttachedToActivityId);
            Assert.NotNull(visual.BoundaryAttachment);
        }
    }

    private sealed class Identities : IDocumentCreationIdentityProvider
    {
        internal int Count { get; private set; }

        public DocumentCreationIdentity CreateIdentity()
        {
            Count++;
            return new(new("a21:placed"), new("a21:placed:visual"));
        }
    }
}
