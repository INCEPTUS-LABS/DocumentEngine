using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class BpmnA21ToolboxLocalizationTests
{
    [Theory]
    [InlineData("en", "Catch Event", "Throw Event", "Boundary Event", "Gateway")]
    [InlineData("pl", "Zdarzenie przechwytujące", "Zdarzenie generujące", "Zdarzenie brzegowe", "Bramka")]
    [InlineData("de", "Empfangendes Ereignis", "Sendendes Ereignis", "Angeheftetes Ereignis", "Gateway")]
    [InlineData("fr", "Événement de réception", "Événement d’émission", "Événement de bordure", "Passerelle")]
    [InlineData("es", "Evento de captura", "Evento de lanzamiento", "Evento de borde", "Compuerta")]
    public void FamilyToolboxLabelsRemainDistinctFromConcreteTypeLabels(
        string culture, string catchEvent, string throwEvent, string boundaryEvent, string gateway)
    {
        using var scope = new ModelerCultureScope(culture);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        var current = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions);
        var previous = new ToolboxCatalog(BpmnPluginRegistration.N91.ToolboxContributions);
        foreach (var (family, expected) in new[]
        {
            ("Catch Event", catchEvent), ("Throw Event", throwEvent),
            ("Boundary Event", boundaryEvent), ("Gateway", gateway),
        })
        {
            var item = Assert.Single(current.Items, item => item.DisplayName == family);
            Assert.Equal(expected, ModelerLabels.ToolboxLabel(text, item));
            var concrete = Assert.Single(previous.Items, candidate => candidate.ItemId == item.ItemId);
            Assert.Equal(ModelerLabels.ToolboxLabel(text, concrete),
                ModelerLabels.SemanticTypeLabel(text, item.ElementTypeId));
            Assert.NotEqual(expected, ModelerLabels.SemanticTypeLabel(text, item.ElementTypeId));
        }

        foreach (var concrete in previous.Items)
        {
            Assert.Equal(ModelerLabels.ToolboxLabel(text, concrete),
                ModelerLabels.SemanticTypeLabel(text, concrete.ElementTypeId));
        }

        Assert.Equal(text["Element_EventBasedGateway"].Value,
            ModelerLabels.SemanticTypeLabel(text, BpmnSemanticTypes.EventBasedGateway));
    }
}
