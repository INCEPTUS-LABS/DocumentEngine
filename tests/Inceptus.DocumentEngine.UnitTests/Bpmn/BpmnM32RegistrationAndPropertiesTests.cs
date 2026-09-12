using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnM32RegistrationAndPropertiesTests
{
    [Fact]
    public void ExclusiveGatewaySemanticTypeAndFactoryContractAreStableAndIndependent()
    {
        var id = new SemanticElementId("bpmn:gateway:technical-id");
        var gateway = BpmnSemanticFactory.CreateExclusiveGateway(
            id,
            "CHECK_STOCK",
            "Stock available?",
            "Route according to stock availability.");

        Assert.Equal("BPMN.ExclusiveGateway", BpmnSemanticTypes.ExclusiveGateway.Value);
        Assert.Equal(id, gateway.Id);
        Assert.Equal(BpmnSemanticTypes.ExclusiveGateway, gateway.TypeId);
        Assert.Equal("CHECK_STOCK", gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Stock available?", gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "Route according to stock availability.",
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.NotEqual(id.Value, gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        Assert.Equal(3, gateway.Properties.Count);

        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateExclusiveGateway(id, " ", "Gateway"));
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateExclusiveGateway(id, "CHECK", " "));
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateExclusiveGateway(id, "CHECK", "Gateway", " "));
    }

    [Fact]
    public void M32AppendsGatewayRegistrationsWhileEarlierSurfacesRemainUnchanged()
    {
        var m1 = BpmnPluginRegistration.M1;
        var m2 = BpmnPluginRegistration.M2;
        var m3 = BpmnPluginRegistration.M3;
        var m31 = BpmnPluginRegistration.M31;
        var m32 = BpmnPluginRegistration.M32;

        Assert.Equal(5, m1.CommandHandlers.Length);
        Assert.Equal(6, m1.CommandValidators.Length);
        Assert.Equal(4, m1.HistoryPolicies.Length);
        Assert.Equal(4, m1.ProjectionRules.Length);
        Assert.Equal(m1.CommandHandlers.AsEnumerable(), m2.CommandHandlers.AsEnumerable());
        Assert.Equal(m2.CommandHandlers.AsEnumerable(), m3.CommandHandlers.AsEnumerable());
        Assert.Equal(m3.CommandHandlers.AsEnumerable(), m31.CommandHandlers.AsEnumerable());

        Assert.Equal(
            m31.CommandHandlers.AsEnumerable(),
            m32.CommandHandlers.Take(m31.CommandHandlers.Length));
        Assert.Equal(
            m31.CommandValidators.AsEnumerable(),
            m32.CommandValidators.Take(m31.CommandValidators.Length));
        Assert.Equal(
            m31.HistoryPolicies.AsEnumerable(),
            m32.HistoryPolicies.Take(m31.HistoryPolicies.Length));
        Assert.Equal(
            m31.ProjectionRules.AsEnumerable(),
            m32.ProjectionRules.Take(m31.ProjectionRules.Length));
        Assert.Equal(m31.LayoutAlgorithms.AsEnumerable(), m32.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m31.RoutingAlgorithms.AsEnumerable(), m32.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(m31.SceneContributors.AsEnumerable(), m32.SceneContributors.AsEnumerable());

        Assert.Contains(m32.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnExclusiveGatewayCommand.KnownTypeId);
        Assert.Contains(m32.CommandValidators, registration =>
            registration.TypeId == CreateBpmnExclusiveGatewayCommand.KnownTypeId &&
            registration.ValidatorId.Value == "bpmn:validator/create-exclusive-gateway");
        Assert.Contains(m32.CommandValidators, registration =>
            registration.TypeId == UpdateSemanticElementPropertyCommand.KnownTypeId &&
            registration.ValidatorId.Value ==
                "bpmn:validator/exclusive-gateway-code-update");
        Assert.Contains(m32.HistoryPolicies, registration =>
            registration.TypeId == CreateBpmnExclusiveGatewayCommand.KnownTypeId);
        Assert.Contains(m32.ProjectionRules, registration =>
            registration.SourceKind == ProjectionSourceKind.SemanticElement &&
            registration.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);

        Assert.DoesNotContain(m31.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnExclusiveGatewayCommand.KnownTypeId);
        Assert.DoesNotContain(m31.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.Single(m31.PropertiesSchemas);
    }

    [Fact]
    public void GatewaySchemaUsesTheExactGenericDataFieldsWithoutElementNumber()
    {
        var schema = Assert.Single(
            BpmnPluginRegistration.M32.PropertiesSchemas,
            candidate => candidate.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);

        Assert.Equal(
            ["code", "name", "description"],
            schema.Fields.Select(static field => field.FieldId.Value).ToArray());
        Assert.Equal(
            ["Code", "Name", "Description"],
            schema.Fields.Select(static field => field.DisplayName).ToArray());
        Assert.Equal(
            [
                BpmnSemanticProperties.Code,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.Description,
            ],
            schema.Fields.Select(static field => field.SemanticPropertyKey).ToArray());
        Assert.Equal(
            [
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.MultilineText,
            ],
            schema.Fields.Select(static field => field.EditorKind).ToArray());
        Assert.Equal(
            [
                SemanticPropertyMutationKind.Property,
                SemanticPropertyMutationKind.Name,
                SemanticPropertyMutationKind.Property,
            ],
            schema.Fields.Select(static field => field.MutationKind).ToArray());
        Assert.Equal([0, 1, 2], schema.Fields.Select(static field => field.Order).ToArray());
        Assert.All(schema.Fields, static field => Assert.True(field.IsEditable));
        Assert.DoesNotContain(schema.Fields, field =>
            StringComparer.Ordinal.Equals(
                field.SemanticPropertyKey,
                BpmnSemanticProperties.ElementNumber));

        var catalog = new ElementPropertiesSchemaCatalog(
            BpmnPluginRegistration.M32.PropertiesSchemas);
        Assert.True(catalog.TryGetSchema(BpmnSemanticTypes.ExclusiveGateway, out var resolved));
        Assert.Same(schema, resolved);
        Assert.True(catalog.TryGetSchema(BpmnSemanticTypes.Task, out _));
    }

    [Fact]
    public void M32ToolboxIsVersionedWithoutChangingTheM3Contribution()
    {
        var m3 = Assert.Single(BpmnPluginRegistration.M3.ToolboxContributions);
        var m32 = Assert.Single(BpmnPluginRegistration.M32.ToolboxContributions);

        Assert.Equal(
            ["Start Event", "Task", "End Event"],
            m3.Items.Select(static item => item.DisplayName).ToArray());
        Assert.Equal(
            ["Start Event", "Task", "Exclusive Gateway", "End Event"],
            m32.Items.Select(static item => item.DisplayName).ToArray());
        Assert.Equal(
            [
                "bpmn:toolbox:start-event",
                "bpmn:toolbox:task",
                "bpmn:toolbox:exclusive-gateway",
                "bpmn:toolbox:end-event",
            ],
            m32.Items.Select(static item => item.ItemId.Value).ToArray());
        Assert.Equal(
            [
                BpmnSemanticTypes.StartEvent,
                BpmnSemanticTypes.Task,
                BpmnSemanticTypes.ExclusiveGateway,
                BpmnSemanticTypes.EndEvent,
            ],
            m32.Items.Select(static item => item.ElementTypeId).ToArray());
        Assert.Equal(
            ["bpmn:start-event", "bpmn:task", "bpmn:exclusive-gateway", "bpmn:end-event"],
            m32.Items.Select(static item => item.Icon.IconKey).ToArray());
        Assert.Equal("◇", m32.Items[2].Icon.FallbackGlyph);
        Assert.DoesNotContain(m32.Items, static item =>
            item.ElementTypeId == BpmnSemanticTypes.SequenceFlow);
    }
}
