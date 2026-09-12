using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseN107PublishedDescriptionTests
{
    [Fact]
    public void MapperCopiesDescriptionForRepresentativeBpmnElementTypes()
    {
        var mapper = new BpmnPublishedNodeDataMapper();
        var types = new[]
        {
            BpmnSemanticTypes.Task,
            BpmnSemanticTypes.UserTask,
            BpmnSemanticTypes.ManualTask,
            BpmnSemanticTypes.ServiceTask,
            BpmnSemanticTypes.SendTask,
            BpmnSemanticTypes.ReceiveTask,
            BpmnSemanticTypes.SubProcess,
            BpmnSemanticTypes.MessageCatchEvent,
            BpmnSemanticTypes.ParallelGateway,
        };

        foreach (var type in types)
        {
            var description = $"Description for {type.Value} — zamówienie";
            var element = Element(
                type.Value,
                type,
                KeyValuePair.Create(
                    BpmnSemanticProperties.Description,
                    PropertyValue.FromText(description)));

            Assert.Equal(description, mapper.Map(element).Description);
        }
    }

    [Fact]
    public void MapperReturnsEmptyForMissingOrNonTextDescriptionWithoutTypeSwitching()
    {
        var mapper = new BpmnPublishedNodeDataMapper();
        var missing = Element("missing", BpmnSemanticTypes.EndEvent);
        var nonText = Element(
            "non-text",
            BpmnSemanticTypes.Task,
            KeyValuePair.Create(
                BpmnSemanticProperties.Description,
                PropertyValue.FromBoolean(true)));

        Assert.Equal(string.Empty, mapper.Map(missing).Description);
        Assert.Equal(string.Empty, mapper.Map(nonText).Description);
    }

    [Fact]
    public void PublishedNodeDescriptionIsAdditiveToExistingPositionalContract()
    {
        var bounds = new PublishedRect(1d, 2d, 3d, 4d);
        var node = new PublishedPresentationNode("node", "rectangle", bounds, "Label");
        var (id, descriptor, deconstructedBounds, label) = node;

        Assert.Equal("node", id);
        Assert.Equal("rectangle", descriptor);
        Assert.Equal(bounds, deconstructedBounds);
        Assert.Equal("Label", label);
        Assert.Equal(string.Empty, node.Description);
        Assert.Equal(
            "Mapped description",
            (node with { Description = "Mapped description" }).Description);
        Assert.Throws<ArgumentNullException>(() => node with { Description = null! });
    }

    [Fact]
    public void PublishedNodeDataRejectsNullDescription()
    {
        Assert.Throws<ArgumentNullException>(() => new PublishedNodeData(null!));
    }

    private static SemanticElementSnapshot Element(
        string id,
        SemanticTypeId type,
        params KeyValuePair<string, PropertyValue>[] properties) =>
        new(new SemanticElementId($"test:n10.7:{id}"), type, properties);
}
