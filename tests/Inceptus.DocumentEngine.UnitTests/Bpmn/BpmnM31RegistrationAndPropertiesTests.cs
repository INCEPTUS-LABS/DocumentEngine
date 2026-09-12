using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnM31RegistrationAndPropertiesTests
{
    [Fact]
    public void M31ComposesM3AndAddsOnlyTheTaskPropertiesSchema()
    {
        var m3 = BpmnPluginRegistration.M3;
        var m31 = BpmnPluginRegistration.M31;

        Assert.Equal(m3.CommandHandlers.AsEnumerable(), m31.CommandHandlers.AsEnumerable());
        Assert.Equal(m3.CommandValidators.AsEnumerable(), m31.CommandValidators.AsEnumerable());
        Assert.Equal(m3.HistoryPolicies.AsEnumerable(), m31.HistoryPolicies.AsEnumerable());
        Assert.Equal(m3.ProjectionRules.AsEnumerable(), m31.ProjectionRules.AsEnumerable());
        Assert.Equal(m3.LayoutAlgorithms.AsEnumerable(), m31.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m3.RoutingAlgorithms.AsEnumerable(), m31.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(m3.SceneContributors.AsEnumerable(), m31.SceneContributors.AsEnumerable());
        Assert.Equal(
            m3.ToolboxContributions.AsEnumerable(),
            m31.ToolboxContributions.AsEnumerable());
        Assert.Empty(m3.PropertiesSchemas);
        Assert.Equal(BpmnSemanticTypes.Task, Assert.Single(m31.PropertiesSchemas).SemanticTypeId);
    }

    [Fact]
    public void TaskSchemaIsOrderedDataOnlyAndUsesDomainOwnedPropertyKeys()
    {
        var schema = Assert.Single(BpmnPluginRegistration.M31.PropertiesSchemas);

        Assert.Equal(BpmnSemanticTypes.Task, schema.SemanticTypeId);
        Assert.Equal(
            ["code", "name", "element-number", "description"],
            schema.Fields.Select(static field => field.FieldId.Value).ToArray());
        Assert.Equal(
            ["Code", "Name", "Element number", "Description"],
            schema.Fields.Select(static field => field.DisplayName).ToArray());
        Assert.Equal(
            [
                BpmnSemanticProperties.Code,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.ElementNumber,
                BpmnSemanticProperties.Description,
            ],
            schema.Fields.Select(static field => field.SemanticPropertyKey).ToArray());
        Assert.Equal(
            [
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.Integer,
                ElementPropertyEditorKind.MultilineText,
            ],
            schema.Fields.Select(static field => field.EditorKind).ToArray());
        Assert.Equal(
            [
                SemanticPropertyMutationKind.Property,
                SemanticPropertyMutationKind.Name,
                SemanticPropertyMutationKind.Property,
                SemanticPropertyMutationKind.Property,
            ],
            schema.Fields.Select(static field => field.MutationKind).ToArray());
        Assert.Equal([0, 1, 2, 3], schema.Fields.Select(static field => field.Order).ToArray());
        Assert.All(schema.Fields, static field => Assert.True(field.IsEditable));

        var catalog = new ElementPropertiesSchemaCatalog(BpmnPluginRegistration.M31.PropertiesSchemas);
        Assert.True(catalog.TryGetSchema(BpmnSemanticTypes.Task, out var resolved));
        Assert.Same(schema, resolved);
        Assert.False(catalog.TryGetSchema(BpmnSemanticTypes.StartEvent, out _));
        Assert.False(catalog.TryGetSchema(BpmnSemanticTypes.EndEvent, out _));
    }
}
