using System.Net;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Properties;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("en", "Edit Colours")]
    [InlineData("pl", "Edytuj kolory")]
    [InlineData("de", "Farben bearbeiten")]
    [InlineData("es", "Editar colores")]
    [InlineData("fr", "Modifier les couleurs")]
    public async Task ColourFormRendersSevenFamiliesLocalisedAndNoFillIsValid(string culture, string title)
    {
        using var scope = new ModelerCultureScope(culture);
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var draft = new DocumentCanvasAppearanceDraft(null);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentColourSchemeForm>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DocumentColourSchemeForm.Scheme)] = draft.Scheme,
                [nameof(DocumentColourSchemeForm.IsValid)] = draft.IsValid,
                [nameof(DocumentColourSchemeForm.ReadColour)] = (Func<string, string, string?>)draft.Get
            })));
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains(title, html, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Count(html, "data-appearance-family="));
        Assert.Equal(21, Regex.Count(html, "type=\"color\""));
        Assert.Single(Regex.Matches(html, "<form\\b"));
        Assert.Contains("role=\"dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Appearance_", html, StringComparison.Ordinal);
        Assert.Contains("fill=\"none\"", html, StringComparison.Ordinal);
        Assert.Contains("M125 12 187 72 125 132 63 72Z", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(html, "<circle\\b"));
        Assert.Contains("stroke-width=\"4\"", html, StringComparison.Ordinal);

        // All editable families belong to the one scrolling body; the title,
        // close action and footer stay outside it and remain keyboard reachable.
        var content = Regex.Match(html, "<div class=\"colour-form-content\"[\\s\\S]*?(?=<footer)");
        Assert.True(content.Success);
        Assert.Equal(7, Regex.Count(content.Value, "data-appearance-family="));
        Assert.Equal(21, Regex.Count(content.Value, "type=\"color\""));
        Assert.DoesNotContain("<header", content.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"submit\"", content.Value, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(html, "class=\"focus-guard\" tabindex=\"0\""));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public async Task ColourFormKeepsActionsOutsideDisabledScrollContent(bool readOnly, bool stale, bool valid)
    {
        using var culture = new ModelerCultureScope("en");
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var draft = new DocumentCanvasAppearanceDraft(null);
        if (!valid) draft.Set("task", "text", "#BAD");
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentColourSchemeForm>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DocumentColourSchemeForm.Scheme)] = draft.Scheme,
                [nameof(DocumentColourSchemeForm.ReadOnly)] = readOnly,
                [nameof(DocumentColourSchemeForm.IsStale)] = stale,
                [nameof(DocumentColourSchemeForm.IsValid)] = draft.IsValid,
                [nameof(DocumentColourSchemeForm.ReadColour)] = (Func<string, string, string?>)draft.Get,
            })));
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        var footer = Regex.Match(html, "<footer[\\s\\S]*?</footer>").Value;
        var cancel = Regex.Match(footer, "<button[^>]*type=\"button\"[^>]*>").Value;
        var accept = Regex.Match(footer, "<button[^>]*type=\"submit\"[^>]*>").Value;
        Assert.NotEmpty(cancel);
        Assert.NotEmpty(accept);
        Assert.DoesNotContain("disabled", cancel, StringComparison.Ordinal);
        Assert.Equal(readOnly || stale || !valid, accept.Contains("disabled", StringComparison.Ordinal));
        Assert.True(html.IndexOf("</fieldset>", StringComparison.Ordinal) < html.IndexOf("<footer", StringComparison.Ordinal));
        if (!valid)
        {
            var invalidInput = Regex.Match(html, "<input[^>]*id=\"document-colours-task-text\"[^>]*>").Value;
            Assert.Contains("aria-invalid=\"true\"", invalidInput, StringComparison.Ordinal);
        }
        if (!valid || stale)
        {
            var content = Regex.Match(html, "<div class=\"colour-form-content\"[\\s\\S]*?(?=<footer)").Value;
            Assert.Contains("role=\"alert\"", content, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColourFormParentOwnsApplyAndCancelWithoutNestedForms(bool apply)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("colour-form-parent", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var activator = new PublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await (Task)typeof(DocumentCanvas).GetMethod("OpenModelViewPropertiesCoreAsync", PropertiesUxFlags)!.Invoke(activator.Component, [true])!;
            await activator.Component.RefreshAsync();
        });
        var html = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("Edit Colours", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-appearance-family=", html, StringComparison.Ordinal);
        var parent = (DocumentCanvasModelViewPropertiesDraft)typeof(DocumentCanvas).GetField("_modelViewPropertiesDraft", PropertiesUxFlags)!.GetValue(activator.Component)!;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePropertiesUxAsync(activator.Component, "OpenColourEditor");
            var child = parent.ColourEditor!;
            ColourComponentEdit(activator.Component, parent, child, editor => editor.Set("lane-header", "text", "#112233"));
            Assert.True(host.CaptureState().ModelViewPropertiesFormDirty);
            ColourComponentClose(activator.Component, parent, child, false);
            Assert.Equal("#0f172a", parent.Appearance.Get("lane-header", "text"));
            await InvokePropertiesUxAsync(activator.Component, "OpenColourEditor");
            ColourComponentEdit(activator.Component, parent, parent.ColourEditor!, editor => editor.Set("lane-header", "text", "#445566"));
            await activator.Component.RefreshAsync();
        });
        html = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Single(Regex.Matches(html, "<form\\b"));
        Assert.Contains("-colour-scheme-form", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"properties-form model-view-properties-form\"", html, StringComparison.Ordinal);
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ColourComponentClose(activator.Component, parent, parent.ColourEditor!, true);
            await activator.Component.RefreshAsync();
            await InvokePropertiesUxAsync(activator.Component, apply ? "ApplyModelViewPropertiesAsync" : "CancelModelViewProperties");
            await activator.Component.RefreshAsync();
        });
        Assert.Equal(apply ? 1 : 0, session.CaptureState().HistoryStatus.EntryCount);
        if (apply)
            Assert.Equal("#445566", AttachedDocument(session).Metadata.ExtensionProperties[BpmnAppearanceSettings.Key("white", "lane-header", "text")].TextValue);
        else Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task ColourFormStaleAndDetachedCallbacksCannotAcceptOrOverwriteDrafts()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("colour-stale", "container");
        var session = Session(host);
        var activator = new PublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await (Task)typeof(DocumentCanvas).GetMethod("OpenModelViewPropertiesCoreAsync", PropertiesUxFlags)!.Invoke(activator.Component, [true])!;
            await InvokePropertiesUxAsync(activator.Component, "OpenColourEditor");
            var field = typeof(DocumentCanvas).GetField("_modelViewPropertiesDraft", PropertiesUxFlags)!;
            var parent = (DocumentCanvasModelViewPropertiesDraft)field.GetValue(activator.Component)!;
            var child = parent.ColourEditor!;
            ColourComponentEdit(activator.Component, parent, child, editor => editor.Set("pool-header", "text", "#112233"));
            var document = AttachedDocument(session);
            await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(document.DocumentId, document.Revision,
                [new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed"))]));
            await session.WaitForIdleAsync();
            typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!.SetValue(activator.Component, host.CaptureState());
            await InvokePropertiesUxAsync(activator.Component, "ReconcileModelViewPropertiesForm");
            Assert.True(parent.IsStale);
            ColourComponentClose(activator.Component, parent, child, true);
            Assert.Same(child, parent.ColourEditor);
            var replacement = new DocumentCanvasModelViewPropertiesDraft(parent.Authoritative);
            field.SetValue(activator.Component, replacement);
            ColourComponentEdit(activator.Component, parent, child, editor => editor.Set("pool-header", "text", "#000000"));
            ColourComponentClose(activator.Component, parent, child, true);
            Assert.Same(replacement, field.GetValue(activator.Component));
            Assert.False(replacement.IsDirty);
            Assert.Equal("#112233", child.Get("pool-header", "text"));
        });
    }

    private static void ColourComponentEdit(DocumentCanvas component, DocumentCanvasModelViewPropertiesDraft parent,
        DocumentCanvasAppearanceDraft child, Action<DocumentCanvasAppearanceDraft> edit) =>
        typeof(DocumentCanvas).GetMethod("EditColours", PropertiesUxFlags)!.Invoke(component, [parent, child, edit]);

    private static void ColourComponentClose(DocumentCanvas component, DocumentCanvasModelViewPropertiesDraft parent,
        DocumentCanvasAppearanceDraft child, bool accept) =>
        typeof(DocumentCanvas).GetMethod("CloseColourEditor", PropertiesUxFlags)!.Invoke(component, [parent, child, accept]);
}
