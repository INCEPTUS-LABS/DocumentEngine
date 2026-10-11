using System.Net;
using System.Globalization;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class SmartEditToggleTests
{
    [Fact]
    public async Task A24SmartEditCultureChangesRefreshLabelWithoutChangingToggleState()
    {
        using var culture = new ModelerCultureScope("en");
        var activator = new SmartCultureActivator();
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<SmartCultureHost>());
        foreach (var language in new[] { "pl", "de", "fr", "es", "en" })
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                using var nextCulture = new ModelerCultureScope(language);
                activator.Host.Culture = CultureInfo.GetCultureInfo(language);
                await activator.Host.RefreshAsync();
                var label = services.GetRequiredService<IStringLocalizer<ModelerStrings>>()["SmartEdit_Label"];
                var markup = WebUtility.HtmlDecode(rendered.ToHtmlString());
                Assert.Contains($"aria-label=\"{label}\"", markup);
                Assert.Contains("aria-checked=\"false\"", markup);
            });
        }
    }

    [Theory]
    [InlineData("en", "Smart Edit", "ON", "OFF")]
    [InlineData("pl", "Inteligentna edycja", "WŁ.", "WYŁ.")]
    [InlineData("de", "Intelligente Bearbeitung", "EIN", "AUS")]
    [InlineData("fr", "Édition intelligente", "OUI", "NON")]
    [InlineData("es", "Edición inteligente", "SÍ", "NO")]
    public async Task A24SmartEditOptionFourHasLocalizedNativeKeyboardSemanticsAndVisibleState(string language, string label, string on, string off)
    {
        using var culture = new ModelerCultureScope(language);
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<ModelerStrings>>();
        foreach (var key in new[] { "SmartEdit_Label", "SmartEdit_On", "SmartEdit_Off" })
            Assert.False(text[key].ResourceNotFound);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        foreach (var enabled in new[] { true, false })
        {
            var component = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<SmartEditToggle>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SmartEditToggle.Enabled)] = enabled })));
            var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(component.ToHtmlString));
            Assert.Contains("type=\"button\"", markup);
            Assert.Contains("role=\"switch\"", markup);
            Assert.Contains($"aria-label=\"{label}\"", markup);
            Assert.Contains($"aria-checked=\"{(enabled ? "true" : "false")}\"", markup);
            Assert.Contains($">{(enabled ? on : off)}</span>", markup);
            Assert.Contains("smart-edit-icon", markup);
            Assert.Contains("smart-edit-thumb", markup);
            Assert.DoesNotContain("tabindex=\"-1\"", markup);
            Assert.DoesNotContain("@onclick", markup);
            Assert.DoesNotContain("@onpointer", markup);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A24SmartEditActivationPublishesNewStateOnlyWhenEnabled(bool disabled)
    {
        var component = new SmartEditToggle();
        bool? published = null;
        typeof(SmartEditToggle).GetProperty(nameof(SmartEditToggle.Disabled))!.SetValue(component, disabled);
        typeof(SmartEditToggle).GetProperty(nameof(SmartEditToggle.EnabledChanged))!.SetValue(component,
            EventCallback.Factory.Create<bool>(new object(), value => published = value));
        await component.ToggleAsync();
        Assert.Equal(disabled ? null : false, published);
        typeof(SmartEditToggle).GetProperty(nameof(SmartEditToggle.Enabled))!.SetValue(component, false);
        await component.ToggleAsync();
        Assert.Equal(disabled ? null : true, published);
    }

    private sealed class SmartCultureActivator : IComponentActivator
    {
        internal SmartCultureHost Host { get; private set; } = null!;
        public IComponent CreateInstance(Type componentType) => componentType == typeof(SmartCultureHost)
            ? Host = new SmartCultureHost() : (IComponent)Activator.CreateInstance(componentType)!;
    }

    private sealed class SmartCultureHost : ComponentBase
    {
        internal CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("en");
        internal Task RefreshAsync() => InvokeAsync(StateHasChanged);
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<CultureInfo>>(0);
            builder.AddAttribute(1, "Name", "ModelerUICulture");
            builder.AddAttribute(2, "Value", Culture);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(content =>
            {
                content.OpenComponent<SmartEditToggle>(0);
                content.AddAttribute(1, nameof(SmartEditToggle.Enabled), false);
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
