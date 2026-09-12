using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnM3RegistrationAndToolboxTests
{
    [Fact]
    public void M3ComposesM2WithoutDuplicatingEarlierRegistrations()
    {
        var m2 = BpmnPluginRegistration.M2;
        var m3 = BpmnPluginRegistration.M3;

        Assert.Equal(m2.CommandHandlers.AsEnumerable(), m3.CommandHandlers.AsEnumerable());
        Assert.Equal(m2.CommandValidators.AsEnumerable(), m3.CommandValidators.AsEnumerable());
        Assert.Equal(m2.HistoryPolicies.AsEnumerable(), m3.HistoryPolicies.AsEnumerable());
        Assert.Equal(m2.ProjectionRules.AsEnumerable(), m3.ProjectionRules.AsEnumerable());
        Assert.Equal(m2.LayoutAlgorithms.AsEnumerable(), m3.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m2.RoutingAlgorithms.AsEnumerable(), m3.RoutingAlgorithms.AsEnumerable());
        Assert.Empty(m2.SceneContributors);
        Assert.Empty(m2.ToolboxContributions);
        Assert.Empty(m2.PropertiesSchemas);
        Assert.Single(m3.SceneContributors);
        Assert.Single(m3.ToolboxContributions);
        Assert.Empty(m3.PropertiesSchemas);
    }

    [Fact]
    public void M3SceneContributorIdentityIsStable()
    {
        var registration = Assert.Single(BpmnPluginRegistration.M3.SceneContributors);

        Assert.Equal("bpmn:scene/flow-node-visuals", registration.Descriptor.ContributorId.Value);
        Assert.Equal("1", registration.Descriptor.Version);
    }

    [Fact]
    public void ToolboxContributionIsDataOnlyOrderedAndUsesCanonicalSemanticTypes()
    {
        var contribution = Assert.Single(BpmnPluginRegistration.M3.ToolboxContributions);
        var group = Assert.Single(contribution.Groups);

        Assert.Equal("bpmn:toolbox:flow-elements", group.GroupId.Value);
        Assert.Equal("BPMN", group.DisplayName);
        Assert.Equal(
            ["Start Event", "Task", "End Event"],
            contribution.Items.Select(static item => item.DisplayName).ToArray());
        Assert.Equal(
            ["bpmn:toolbox:start-event", "bpmn:toolbox:task", "bpmn:toolbox:end-event"],
            contribution.Items.Select(static item => item.ItemId.Value).ToArray());
        Assert.Equal(
            [BpmnSemanticTypes.StartEvent, BpmnSemanticTypes.Task, BpmnSemanticTypes.EndEvent],
            contribution.Items.Select(static item => item.ElementTypeId).ToArray());
        Assert.Equal(
            ["bpmn:start-event", "bpmn:task", "bpmn:end-event"],
            contribution.Items.Select(static item => item.Icon.IconKey).ToArray());
        Assert.Equal(
            ["○", "▭", "◎"],
            contribution.Items.Select(static item => item.Icon.FallbackGlyph!).ToArray());
        Assert.DoesNotContain(
            contribution.Items,
            static item => item.ItemId.Value == item.ElementTypeId.Value);
        Assert.DoesNotContain(
            contribution.Items,
            static item => item.ElementTypeId == BpmnSemanticTypes.SequenceFlow);
    }
}
