using System.Collections;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Resources;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class ModelerLocalizationTests
{
    [Theory]
    [InlineData("", "Undo")]
    [InlineData("en", "Undo")]
    [InlineData("en-GB", "Undo")]
    [InlineData("en-US", "Undo")]
    [InlineData("pl", "Cofnij")]
    [InlineData("pl-PL", "Cofnij")]
    [InlineData("fr", "Annuler")]
    [InlineData("fr-FR", "Annuler")]
    [InlineData("fr-CA", "Annuler")]
    [InlineData("de", "Rückgängig")]
    [InlineData("de-DE", "Rückgängig")]
    [InlineData("de-AT", "Rückgängig")]
    [InlineData("es", "Deshacer")]
    [InlineData("es-ES", "Deshacer")]
    [InlineData("es-MX", "Deshacer")]
    [InlineData("it-IT", "Undo")]
    public void LocalizationUsesHostUICultureAndStandardFallback(string culture, string undo)
    {
        using var scope = new ModelerCultureScope(culture);
        // CurrentCulture is deliberately different: it must not select UI resources.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        Assert.Equal(undo, text["Toolbar_Undo"].Value);
        foreach (var key in ResourceKeys(string.Empty))
        {
            var value = text[key];
            Assert.False(value.ResourceNotFound, $"{culture}: {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.Value));
            Assert.NotEqual(key, value.Value);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("pl")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    public void LocalizationResourcesHaveIdenticalKeysAndCompiledSatellites(string culture)
    {
        var expected = ResourceKeys(string.Empty);
        var actual = ResourceKeys(culture);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        var manager = new ResourceManager(
            "Inceptus.DocumentEngine.Bpmn.Blazor.Resources.ModelerStrings", typeof(ModelerStrings).Assembly);
        var set = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
        Assert.NotNull(set);
        Assert.Equal(expected, set.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal));
        var neutral = ResourceValues(string.Empty);
        foreach (var (key, value) in ResourceValues(culture))
        {
            Assert.Equal(System.Text.CompositeFormat.Parse(neutral[key]).MinimumArgumentCount,
                System.Text.CompositeFormat.Parse(value).MinimumArgumentCount);
        }

        if (culture.Length > 0)
        {
            var satellite = typeof(ModelerStrings).Assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo(culture));
            Assert.Equal(culture, satellite.GetName().CultureName);
            Assert.Single(satellite.GetManifestResourceNames());
        }
    }

    [Theory]
    [InlineData("en-GB", "Undo", "Validate", "Issues", "Start Event")]
    [InlineData("pl-PL", "Cofnij", "Waliduj", "Problemy", "Zdarzenie początkowe")]
    [InlineData("fr-FR", "Annuler", "Valider", "Problèmes", "Événement de début")]
    [InlineData("de-DE", "Rückgängig", "Validieren", "Probleme", "Startereignis")]
    [InlineData("es-ES", "Deshacer", "Validar", "Problemas", "Evento de inicio")]
    public async Task LocalizationRendersToolboxToolbarAndStatusInEveryLanguage(
        string culture, string undo, string validate, string issues, string start)
    {
        using var scope = new ModelerCultureScope(culture);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new NoBrowserRuntime()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<InceptusBpmnModeler>());
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains($">{undo}</button>", markup, StringComparison.Ordinal);
        Assert.Contains($">{validate}</button>", markup, StringComparison.Ordinal);
        Assert.Contains($">{issues}</span>", markup, StringComparison.Ordinal);
        Assert.Contains($"title=\"{start}\"", markup, StringComparison.Ordinal);
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        foreach (var group in BpmnModelerCompositionFactory.ToolboxCatalog.Groups)
        {
            Assert.NotNull(ModelerLabels.ToolboxKey(group.GroupId.Value));
            Assert.Contains(ModelerLabels.ToolboxLabel(text, group), markup, StringComparison.Ordinal);
            foreach (var item in BpmnModelerCompositionFactory.ToolboxCatalog.GetItems(group.GroupId))
            {
                Assert.NotNull(ModelerLabels.ToolboxKey(item.ItemId.Value));
                Assert.Contains(ModelerLabels.ToolboxLabel(text, item), markup, StringComparison.Ordinal);
                Assert.Contains(item.ItemId.Value, markup, StringComparison.Ordinal);
            }
        }

        AssertNoResourceKeys(markup);
        Assert.Contains("data-validation-state=\"not-validated\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizationRegistrationIsIdempotentAndRespectsHostServicesAndResourcePath()
    {
        var services = new ServiceCollection();
        services.AddLocalization(options => options.ResourcesPath = "ConsumerResources");
        services.AddLogging().AddInceptusBpmnModeler();
        var first = services.ToArray();
        services.AddLogging().AddInceptusBpmnModeler();
        Assert.Equal(first, services.ToArray());
        Assert.Single(services, item => item.ServiceType == typeof(IStringLocalizerFactory));
        Assert.Single(services, item => item.ServiceType == typeof(IStringLocalizer<>));
        using var provider = services.BuildServiceProvider();
        using var scope = new ModelerCultureScope("pl-PL");
        Assert.Equal("Cofnij", provider.GetRequiredService<IStringLocalizer<ModelerStrings>>()["Toolbar_Undo"].Value);
        Assert.DoesNotContain(typeof(InceptusBpmnModeler).GetProperties(), property =>
            property.Name is "Language" or "Culture" or "CurrentCulture" or "CurrentUICulture");
        Assert.False(typeof(ModelerStrings).IsPublic);
    }

    internal static void AssertNoResourceKeys(string markup)
    {
        foreach (var key in ResourceKeys(string.Empty))
        {
            Assert.DoesNotContain(key, markup, StringComparison.Ordinal);
        }
    }

    private static string[] ResourceKeys(string culture) =>
        ResourceValues(culture).Keys.Order(StringComparer.Ordinal).ToArray();

    private static Dictionary<string, string> ResourceValues(string culture)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Inceptus.DocumentEngine.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var suffix = culture.Length == 0 ? string.Empty : "." + culture;
        var path = Path.Combine(root.FullName, "src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Resources",
            $"ModelerStrings{suffix}.resx");
        var entries = XDocument.Load(path).Root!.Elements("data").ToArray();
        var keys = entries.Select(entry => (string)entry.Attribute("name")!).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Element("value")?.Value)));
        return entries.ToDictionary(entry => (string)entry.Attribute("name")!, entry => entry.Element("value")!.Value,
            StringComparer.Ordinal);
    }

    private sealed class NoBrowserRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static UI rendering must not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}

internal sealed class ModelerCultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    internal ModelerCultureScope(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}

// Uses ordinary host culture + cascading re-rendering around the real reusable modeler.
internal sealed class LocalizationTestHost : ComponentBase
{
    internal Task SwitchAsync(string culture) => InvokeAsync(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        StateHasChanged();
    });

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<CultureInfo>>(0);
        builder.AddAttribute(1, "Value", CultureInfo.CurrentUICulture);
        builder.AddAttribute(2, "ChildContent", (RenderFragment)(content =>
        {
            content.OpenComponent<InceptusBpmnModeler>(0);
            content.CloseComponent();
        }));
        builder.CloseComponent();
    }
}
