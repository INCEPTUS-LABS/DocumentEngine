using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM31BpmnPropertiesArchitectureTests
{
    [Fact]
    public void BpmnTaskSchemaUsesOnlyImmutableGenericDataContracts()
    {
        var schema = Assert.Single(BpmnPluginRegistration.M31.PropertiesSchemas);

        Assert.IsType<ElementPropertiesSchema>(schema);
        Assert.Same(typeof(ElementPropertiesSchema).Assembly, schema.GetType().Assembly);
        Assert.All(schema.Fields, field =>
        {
            Assert.IsType<ElementPropertyFieldDefinition>(field);
            Assert.All(
                field.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
                static property => Assert.False(
                    typeof(Delegate).IsAssignableFrom(property.PropertyType)));
        });
    }

    [Fact]
    public void M31SchemaDoesNotAddPropertiesToEarlierBpmnCompositions()
    {
        Assert.Empty(BpmnPluginRegistration.M1.PropertiesSchemas);
        Assert.Empty(BpmnPluginRegistration.M2.PropertiesSchemas);
        Assert.Empty(BpmnPluginRegistration.M3.PropertiesSchemas);
        Assert.Single(BpmnPluginRegistration.M31.PropertiesSchemas);
    }

    [Fact]
    public void TaskElementNumberIsTypedSemanticDataAndNotTechnicalIdentity()
    {
        var property = typeof(CreateBpmnTaskCommand).GetProperty(
            nameof(CreateBpmnTaskCommand.ElementNumber));

        Assert.NotNull(property);
        Assert.Equal(typeof(long), property.PropertyType);
        Assert.Equal("BPMN.ElementNumber", BpmnSemanticProperties.ElementNumber);
        Assert.NotEqual(BpmnSemanticProperties.Code, BpmnSemanticProperties.ElementNumber);
        Assert.NotEqual(BpmnSemanticProperties.Name, BpmnSemanticProperties.ElementNumber);
        Assert.NotEqual(BpmnSemanticProperties.Description, BpmnSemanticProperties.ElementNumber);
    }
}
