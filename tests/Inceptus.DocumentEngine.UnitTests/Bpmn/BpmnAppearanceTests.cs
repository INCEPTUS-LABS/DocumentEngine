using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnAppearanceTests
{
    [Theory]
    [InlineData("start-event", "#E5F1E8", "#60846B", "#223D2B")]
    [InlineData("task", "#E7EFF8", "#5D7FA5", "#263C56")]
    [InlineData("gateway", "#F8F1D9", "#9B8744", "#493E20")]
    [InlineData("end-event", "#F7E5E5", "#A46B6B", "#552D2D")]
    public void AppearancePresetsAndLegacyDefaults(string family, string background, string border, string text)
    {
        Assert.Equal(new BpmnElementColours(background, border, text), BpmnAppearanceSettings.Preset("mixed", family));
        Assert.Equal(new BpmnElementColours("#ffffff", "#000000", "#000000"), BpmnAppearanceSettings.Resolve(null).Colours[family]);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#123")]
    [InlineData("#12345678")]
    [InlineData("url(test)")]
    [InlineData("#12345z")]
    public void AppearanceInvalidPaintFallsBackWithoutChangingRawMetadata(string raw)
    {
        var key = BpmnAppearanceSettings.Key("mixed", "task", "background");
        var metadata = Metadata(new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed")), new(key, PropertyValue.FromText(raw)));
        var resolved = BpmnAppearanceSettings.Resolve(metadata);
        Assert.Equal("#E7EFF8", resolved.Colours["task"].Background);
        Assert.Contains(key, resolved.InvalidKeys);
        Assert.Equal(raw, metadata.ExtensionProperties[key].TextValue);
    }

    [Fact]
    public void AppearanceDraftRetainsBothSchemesAndResetIsExplicitAndLocal()
    {
        var unknown = new KeyValuePair<string, PropertyValue>("bpmn:appearance.mixed.future.fill", PropertyValue.FromText("future"));
        var metadata = Metadata(unknown);
        var draft = new DocumentCanvasAppearanceDraft(metadata);
        draft.Set("task", "background", "#112233");
        draft.SelectScheme("mixed");
        draft.Set("task", "text", "#445566");
        draft.SelectScheme("white");
        Assert.Equal("#112233", draft.Get("task", "background"));
        draft.RestorePreset();
        Assert.Equal("#ffffff", draft.Get("task", "background"));
        draft.SelectScheme("mixed");
        Assert.Equal("#445566", draft.Get("task", "text"));
        Assert.Single(metadata.ExtensionProperties);
        Assert.DoesNotContain(draft.CreateChanges(), entry => entry.Key == unknown.Key);
        Assert.DoesNotContain(draft.CreateChanges(), entry => entry.Value is null);
    }

    [Fact]
    public void AppearanceUnknownVersionIsReadOnlyAndUnknownSchemeRequiresExplicitReplacement()
    {
        var unknown = Metadata(new(BpmnAppearanceSettings.VersionKey, PropertyValue.FromInteger(2)),
            new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed")));
        var draft = new DocumentCanvasAppearanceDraft(unknown);
        draft.SelectScheme("mixed");
        draft.Set("task", "background", "#112233");
        draft.RestorePreset();
        Assert.False(draft.CanEdit);
        Assert.Empty(draft.CreateChanges());
        Assert.Equal("white", draft.Scheme);
        draft = new(Metadata(new KeyValuePair<string, PropertyValue>(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("future"))));
        Assert.True(draft.CanEdit);
        Assert.Empty(draft.CreateChanges());
        draft.Set("task", "border", "#123456");
        Assert.DoesNotContain(draft.CreateChanges(), entry => entry.Key == BpmnAppearanceSettings.SchemeKey);
        draft.SelectScheme("white");
        Assert.Contains(draft.CreateChanges(), entry => entry.Key == BpmnAppearanceSettings.SchemeKey && entry.Value!.TextValue == "white");
    }

    private static DocumentMetadataSnapshot Metadata(params KeyValuePair<string, PropertyValue>[] values) =>
        new(new DocumentId("appearance"), DocumentRevision.Zero, extensionProperties: values);
}
