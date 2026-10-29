using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnHeaderAppearanceTests
{
    [Theory]
    [InlineData("pool-header", "#E9EEF5", "#7B8DA5", "#334155", "#eef2f7", "#64748b", "#0f172a")]
    [InlineData("lane-header", "#E7F0EF", "#76938F", "#294A45", "#eef2f7", "#64748b", "#0f172a")]
    [InlineData("participant-header", "#ECE9F4", "#8B80A3", "#433957", null, "#111827", "#111827")]
    public void HeaderDefaultsPreserveWhiteAndDefineMatteMixed(string family, string background, string border,
        string text, string? whiteBackground, string whiteBorder, string whiteText)
    {
        Assert.Equal(new BpmnElementColours(background, border, text), BpmnAppearanceSettings.Preset("mixed", family));
        Assert.Equal(new BpmnElementColours(whiteBackground, whiteBorder, whiteText), BpmnAppearanceSettings.Resolve(null).Colours[family]);
        foreach (var scheme in BpmnAppearanceSettings.Schemes)
            foreach (var channel in BpmnAppearanceSettings.Channels)
                Assert.True(BpmnAppearanceSettings.IsColourKey(BpmnAppearanceSettings.Key(scheme, family, channel)));
    }

    [Fact]
    public void HeaderFallbackPreservesInvalidAndFutureMetadata()
    {
        var key = BpmnAppearanceSettings.Key("white", "participant-header", "background");
        var metadata = new DocumentMetadataSnapshot(new("headers"), DocumentRevision.Zero,
            extensionProperties: [new(key, PropertyValue.FromText("transparent")), new("bpmn:appearance.future", PropertyValue.FromText("raw"))]);
        var appearance = BpmnAppearanceSettings.Resolve(metadata);
        Assert.Null(appearance.Colours["participant-header"].Background);
        Assert.Contains(key, appearance.InvalidKeys);
        Assert.Equal("transparent", metadata.ExtensionProperties[key].TextValue);
        var draft = new DocumentCanvasAppearanceDraft(metadata);
        draft.RestoreDefaultBackground("participant-header");
        Assert.Contains(draft.CreateChanges(), change => change.Key == key && change.Value is null);
        Assert.DoesNotContain(draft.CreateChanges(), change => change.Key == "bpmn:appearance.future");
    }

    [Fact]
    public void ColourChildCancelAndAcceptPreserveParentAndBothSchemeDrafts()
    {
        var parent = Parent();
        parent.Appearance.Set("task", "text", "#112233");
        parent.BeginColourEditing();
        var child = parent.ColourEditor!;
        child.SelectScheme("mixed");
        child.Set("lane-header", "text", "#445566");
        Assert.Equal("white", parent.Appearance.Scheme);
        Assert.True(parent.IsColourEditorDirty);
        parent.CancelColourEditing();
        Assert.Equal("#112233", parent.Appearance.Get("task", "text"));
        parent.BeginColourEditing();
        child = parent.ColourEditor!;
        child.SelectScheme("mixed");
        Assert.Equal("#294A45", child.Get("lane-header", "text"));
        child.Set("lane-header", "text", "#778899");
        Assert.True(parent.AcceptColourEditing());
        Assert.Null(parent.ColourEditor);
        child.Set("lane-header", "text", "#000000"); // A detached callback cannot mutate the accepted draft.
        Assert.Equal("#778899", parent.Appearance.Get("lane-header", "text"));
        parent.Appearance.SelectScheme("white");
        Assert.Equal("#112233", parent.Appearance.Get("task", "text"));
    }

    [Fact]
    public void ColourChildCopiesInvalidInputAndResetsWithoutWritingUntilApply()
    {
        var parent = Parent();
        parent.Appearance.Set("task", "text", "#BAD");
        parent.BeginColourEditing();
        Assert.Equal("#BAD", parent.ColourEditor!.Get("task", "text"));
        Assert.False(parent.AcceptColourEditing());
        parent.ColourEditor.RestorePreset();
        Assert.True(parent.ColourEditor.IsValid);
        parent.CancelColourEditing();
        Assert.False(parent.Appearance.IsValid);
        parent.BeginColourEditing();
        parent.ColourEditor!.RestorePreset();
        Assert.True(parent.AcceptColourEditing());
        Assert.True(parent.Appearance.IsValid);
        Assert.Empty(parent.Appearance.CreateChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColourChildDefaultBackgroundAndStaleGuard(bool stale)
    {
        var parent = Parent();
        parent.BeginColourEditing();
        Assert.False(parent.IsDirty);
        parent.ColourEditor!.Set("participant-header", "background", "#FFFFFF");
        Assert.True(parent.IsDirty);
        parent.ColourEditor.RestoreDefaultBackground("participant-header");
        Assert.Null(parent.ColourEditor.Get("participant-header", "background"));
        parent.IsStale = stale;
        Assert.Equal(!stale, parent.AcceptColourEditing());
        Assert.Empty(parent.Appearance.CreateChanges());
    }

    [Fact]
    public void UnsupportedVersionRemainsReadOnlyInChild()
    {
        var metadata = new DocumentMetadataSnapshot(new("headers"), DocumentRevision.Zero,
            extensionProperties: [new(BpmnAppearanceSettings.VersionKey, PropertyValue.FromInteger(9))]);
        var parent = new DocumentCanvasModelViewPropertiesDraft(new(metadata.DocumentId, metadata.Revision,
            new DocumentScopeId("headers"), [])
        { AppearanceMetadata = metadata });
        parent.BeginColourEditing();
        Assert.False(parent.ColourEditor!.CanEdit);
        parent.ColourEditor.Set("pool-header", "text", "#123456");
        Assert.False(parent.AcceptColourEditing());
        Assert.False(parent.IsDirty);
    }

    private static DocumentCanvasModelViewPropertiesDraft Parent() => new(new(new("headers"), DocumentRevision.Zero, new DocumentScopeId("headers"), []));
}
