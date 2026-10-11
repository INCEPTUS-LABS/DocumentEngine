using Inceptus.DocumentEngine.Bpmn.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class PhaseA22LocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void PresentationAndMessageFlowDiagnosticsUseLocalizedFeedback(string culture)
    {
        using var scope = new ModelerCultureScope(culture);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        Assert.False(text["Flow_SelectSource", "Sequence Flow"].ResourceNotFound);
        Assert.False(text["Flow_SelectTarget", "Message Flow"].ResourceNotFound);
        Assert.Contains("Sequence Flow", text["Flow_SelectSource", "Sequence Flow"].Value, StringComparison.Ordinal);
        Assert.Contains("Message Flow", text["Flow_SelectTarget", "Message Flow"].Value, StringComparison.Ordinal);
        string[] codes =
        [
            "BPMN_ELEMENT_FLOW_SOURCE_INVALID",
            "BPMN_ELEMENT_FLOW_TARGET_INVALID",
            "BPMN_ELEMENT_FLOW_POINT_PLAN_INVALID",
            "CANVAS2D_ELEMENT_CONNECTION_NO_ROUTE",
            "BPMN_COLLABORATION_VIEW_INVALID",
            "BPMN_PARTICIPANT_VISUAL_MISSING",
            "BPMN_PARTICIPANT_OWNERSHIP_INVALID",
            "BPMN_PARTICIPANT_OWNERSHIP_MISSING",
            "BPMN_PARTICIPANT_OWNERSHIP_AMBIGUOUS",
            "PUBLISH_DOCUMENT_PRESENTATION_UNSUPPORTED",
            "PRESENTATION_CONTEXT_INVALID",
            "BPMN_MESSAGE_FLOW_ENDPOINT_MISSING",
            "BPMN_MESSAGE_FLOW_SOURCE_INVALID",
            "BPMN_MESSAGE_FLOW_TARGET_INVALID",
            "BPMN_MESSAGE_FLOW_SAME_PARTICIPANT",
            "BPMN_MESSAGE_FLOW_DIFFERENT_COLLABORATIONS",
            "BPMN_MESSAGE_FLOW_BOUNDARY_ATTACHMENT_INVALID",
            "BPMN_MESSAGE_FLOW_PRESENTATION_UNAVAILABLE",
            "BPMN_MESSAGE_FLOW_ANCHOR_INVALID",
            "BPMN_MESSAGE_FLOW_STATE_MISMATCH",
            "BPMN_MESSAGE_FLOW_COMMAND_INVALID",
            "BPMN_MESSAGE_FLOW_TYPE_CHANGE_INVALID",
        ];
        foreach (var code in codes)
        {
            var key = ModelerDiagnosticLabels.ResourceKey(code);
            Assert.NotNull(key);
            Assert.False(text[key].ResourceNotFound);
            Assert.Equal(text[key].Value, ModelerDiagnosticLabels.Message(text, code, "canonical detail"));
            Assert.NotEqual("canonical detail", ModelerDiagnosticLabels.Message(text, code, "canonical detail"));
        }

        Assert.Equal("canonical detail", ModelerDiagnosticLabels.Message(text, "OTHER_DIAGNOSTIC", "canonical detail"));
    }
}
