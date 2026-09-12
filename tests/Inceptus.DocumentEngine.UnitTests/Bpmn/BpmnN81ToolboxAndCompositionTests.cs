using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN81ToolboxAndCompositionTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8.1:toolbox-document");

    [Fact]
    public void N81MigratesTasksToActivitiesAndAppendsSubProcessDeterministically()
    {
        var n7 = Assert.Single(BpmnPluginRegistration.N7.ToolboxContributions);
        var n81 = Assert.Single(BpmnPluginRegistration.N81.ToolboxContributions);

        Assert.Equal(
            ["Events", "Activities", "Gateways"],
            n81.Groups.Select(static group => group.DisplayName));
        var activities = Assert.Single(
            n81.Groups,
            static group => group.DisplayName == "Activities");
        Assert.Equal("bpmn:toolbox:activities", activities.GroupId.Value);
        Assert.Equal(
            [
                "Task",
                "User Task",
                "Manual Task",
                "Service Task",
                "Send Task",
                "Receive Task",
                "SubProcess",
            ],
            n81.Items
                .Where(item => item.GroupId == activities.GroupId)
                .Select(static item => item.DisplayName));

        Assert.Equal(
            n7.Items.Select(static item => item.ItemId).OrderBy(static id => id.Value),
            n81.Items
                .Where(static item => item.ElementTypeId != BpmnSemanticTypes.SubProcess)
                .Select(static item => item.ItemId)
                .OrderBy(static id => id.Value));
        var subProcess = Assert.Single(
            n81.Items,
            static item => item.ElementTypeId == BpmnSemanticTypes.SubProcess);
        Assert.Equal("bpmn:toolbox:sub-process", subProcess.ItemId.Value);
        Assert.Equal("bpmn:sub-process", subProcess.Icon.IconKey);
        Assert.NotEqual("▭", subProcess.Icon.FallbackGlyph);
    }

    [Fact]
    public void N81PlacementCreatesAnExplicitRootScopedSubProcessPlan()
    {
        var registration = Assert.Single(
            BpmnPluginRegistration.N81.ToolboxPlacementRegistrations,
            static registration =>
                registration.ToolboxItemId == new ToolboxItemId("bpmn:toolbox:sub-process"));
        var identity = new DocumentCreationIdentity(
            new SemanticElementId("bpmn:n8.1:placed-subprocess"),
            new VisualStateId("bpmn:n8.1:placed-subprocess-visual"));
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var snapshot = document.CaptureSnapshot();

        var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            registration.ToolboxItemId,
            snapshot,
            snapshot.Revision,
            new PointD(300d, 200d),
            new FixedIdentityProvider(identity)));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var plan = Assert.IsType<ToolboxPlacementPlan>(result.Plan);
        var command = Assert.IsType<CreateBpmnSubProcessCommand>(plan.Command);
        Assert.Equal(identity.SemanticElementId, plan.CreatedSemanticElementId);
        Assert.Equal(identity.VisualStateId, plan.CreatedVisualStateId);
        Assert.Equal(snapshot.SemanticModel.RootScopeId, command.ParentScopeId);
        Assert.Equal(
            new DocumentScopeId("bpmn:scope:bpmn:n8.1:placed-subprocess"),
            command.ChildScopeId);
        Assert.Equal(new PointD(240d, 160d), command.Position);
        Assert.Equal(new SizeD(120d, 80d), command.Size);
        Assert.Equal("SUBPROCESS_1", command.Code);
        Assert.Equal("SubProcess 1", command.Name);
        Assert.Equal("SubProcess created from the Toolbox.", command.Description);
        Assert.Equal(VisualPlacementMode.Pinned, command.PlacementMode);
    }

    [Fact]
    public void N81SubProcessUsesDynamicUnlimitedSourceOrTargetAnchorsOnEveryEdge()
    {
        var policy = new ElementConnectorAnchorPolicyRegistry(
            BpmnPluginRegistration.N81.ConnectorAnchorPolicies)
            .Resolve(BpmnSemanticTypes.SubProcess);

        foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
        {
            var edge = policy.ForSide(side);
            Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
            Assert.Equal(ConnectorAnchorRoleCapability.SourceOrTarget, edge.AllowedRoles);
            Assert.Empty(edge.PredefinedAnchors);
        }
    }

    private sealed class FixedIdentityProvider(DocumentCreationIdentity identity) :
        IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => identity;
    }
}
