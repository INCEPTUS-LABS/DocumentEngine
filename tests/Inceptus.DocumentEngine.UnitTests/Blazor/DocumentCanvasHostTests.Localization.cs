using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("en-GB", "Data", "Name", "Undo")]
    [InlineData("pl-PL", "Dane", "Nazwa", "Cofnij")]
    [InlineData("fr-FR", "Données", "Nom", "Annuler")]
    [InlineData("de-DE", "Daten", "Name", "Rückgängig")]
    [InlineData("es-ES", "Datos", "Nombre", "Deshacer")]
    public async Task LocalizationHostRerenderPreservesSessionDocumentHistoryAndOpenProperties(
        string culture, string dataLabel, string nameLabel, string undo)
    {
        using var cultureScope = new ModelerCultureScope("en-GB");
        var events = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(events);
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("localization-canvas", "localization-container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [BpmnDemoPipeline.TaskVisualId],
            viewport: new ViewportSnapshot(1.37d, new VectorD(41d, -23d))))).Succeeded);
        await session.WaitForIdleAsync();
        await host.ValidateAsync();
        var properties = await host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId);
        Assert.NotNull(properties);
        var draft = new DocumentCanvasPropertiesDraft(properties) { Width = "invalid", Feedback = "validation" };
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<LocalizationTestHost>());
        var english = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Matches("<legend[^>]*>Data</legend>", english);
        var document = AttachedDocument(session);
        var snapshot = document.CaptureSnapshot();
        var before = host.CaptureState();
        var native = NativeDocumentSerializer.Export(snapshot);
        await notifications.Pending;
        var eventCount = events.Count;
        var component = activator.Canvas.Component;
        var root = activator.Modeler;

        await renderer.Dispatcher.InvokeAsync(() => activator.Host.SwitchAsync(culture));
        var localized = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Matches($"<legend[^>]*>{dataLabel}</legend>", localized);
        Assert.Contains(nameLabel, localized, StringComparison.Ordinal);
        Assert.Contains($">{undo}</button>", localized, StringComparison.Ordinal);
        Assert.Contains("data-property-field-id=\"name\"", localized, StringComparison.Ordinal);
        Assert.Contains("data-property-field-id=\"code\"", localized, StringComparison.Ordinal);
        Assert.Contains("readonly", localized, StringComparison.Ordinal);
        Assert.Contains("value=\"invalid\"", localized, StringComparison.Ordinal);
        using (new ModelerCultureScope(culture))
        {
            var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
            Assert.Contains(text["Validation_Finite", text["Properties_Width"].Value].Value,
                localized, StringComparison.Ordinal);
            Assert.Contains(text["Element_StartEvent"].Value, localized, StringComparison.Ordinal);
        }

        ModelerLocalizationTests.AssertNoResourceKeys(localized);
        Assert.Same(root, activator.Modeler);
        Assert.Same(component, activator.Canvas.Component);
        Assert.Same(draft, typeof(DocumentCanvas).GetField("_propertiesDraft",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component));
        Assert.Same(session, Session(host));
        Assert.Same(document, AttachedDocument(session));
        Assert.Same(snapshot, document.CaptureSnapshot());
        var after = host.CaptureState();
        Assert.Equal(before.DocumentSessionVersion, after.DocumentSessionVersion);
        Assert.Equal(before.Session!.DocumentId, after.Session!.DocumentId);
        Assert.Equal(before.Session.DocumentRevision, after.Session.DocumentRevision);
        Assert.Equal(before.Session.HistoryStatus, after.Session.HistoryStatus);
        Assert.Same(before.Session.EditorState, after.Session.EditorState);
        Assert.Same(before.Session.CurrentScene, after.Session.CurrentScene);
        Assert.Same(before.ValidationSnapshot, after.ValidationSnapshot);
        Assert.Equal(before.SuccessfulRenderCount, after.SuccessfulRenderCount);
        Assert.Equal(native.ToArray(), NativeDocumentSerializer.Export(snapshot).ToArray());
        await notifications.Pending;
        Assert.Equal(eventCount, events.Count);

        await renderer.Dispatcher.InvokeAsync(() => activator.Host.SwitchAsync("it-IT"));
        var fallback = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Equal(english, fallback);
    }

    [Theory]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public async Task LocalizationNativeAndPublishedFormatsRemainByteIdentical(string culture)
    {
        using var scope = new ModelerCultureScope("en-GB");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("format-canvas", "format-container");
        await SaveDefaultPublicationAsync(Session(host));
        var snapshot = AttachedDocument(Session(host)).CaptureSnapshot();
        var nativeEnglish = await host.ExportNativeDocumentAsync();
        var publishedEnglish = await host.PublishProcessAsync();
        Assert.True(nativeEnglish.Succeeded);
        Assert.True(publishedEnglish.Succeeded, PublishDiagnostics(publishedEnglish.Diagnostics));
        using var changed = new ModelerCultureScope(culture);
        var nativeLocalized = await host.ExportNativeDocumentAsync();
        var publishedLocalized = await host.PublishProcessAsync();
        Assert.True(nativeLocalized.Succeeded);
        Assert.True(publishedLocalized.Succeeded, PublishDiagnostics(publishedLocalized.Diagnostics));
        Assert.Equal(nativeEnglish.Status, nativeLocalized.Status);
        Assert.Equal(publishedEnglish.Status, publishedLocalized.Status);
        Assert.Equal(nativeEnglish.Payload.ToArray(), nativeLocalized.Payload.ToArray());
        Assert.Equal(publishedEnglish.Payload.ToArray(), publishedLocalized.Payload.ToArray());
        Assert.Same(snapshot, AttachedDocument(Session(host)).CaptureSnapshot());
        var imported = NativeDocumentSerializer.Import(nativeEnglish.Payload.AsMemory(),
            new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
        Assert.True(imported.Succeeded);
        Assert.Equal(nativeEnglish.Payload.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());
        using var nativeJson = JsonDocument.Parse(nativeLocalized.Payload.AsMemory());
        Assert.Equal("Inceptus.Document", nativeJson.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, nativeJson.RootElement.GetProperty("formatVersion").GetInt32());
        using var publishedJson = JsonDocument.Parse(ReadPublishedArchive(publishedLocalized.Payload)["process.json"]);
        Assert.Equal("Inceptus.PublishedProcess", publishedJson.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, publishedJson.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.True(publishedJson.RootElement.TryGetProperty("runtime", out _));
        var badImport = NativeDocumentSerializer.Import(Encoding.UTF8.GetBytes("{}"));
        using var englishAgain = new ModelerCultureScope("en-GB");
        var badEnglish = NativeDocumentSerializer.Import(Encoding.UTF8.GetBytes("{}"));
        Assert.Equal(badEnglish.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Message)),
            badImport.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Message)));
    }

    private sealed class LocalizationComponentActivator(
        DocumentCanvasHost host, DocumentCanvasPropertiesDraft draft) : IComponentActivator
    {
        internal PublishComponentActivator Canvas { get; } = new(host);

        internal LocalizationTestHost Host { get; private set; } = null!;

        internal InceptusBpmnModeler Modeler { get; private set; } = null!;

        public IComponent CreateInstance(Type componentType)
        {
            if (componentType == typeof(LocalizationTestHost))
            {
                return Host = new LocalizationTestHost();
            }

            var component = Canvas.CreateInstance(componentType);
            if (component is InceptusBpmnModeler modeler)
            {
                Modeler = modeler;
            }

            if (componentType == typeof(DocumentCanvas))
            {
                typeof(DocumentCanvas).GetField("_propertiesDraft", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(component, draft);
            }

            return component;
        }
    }
}
