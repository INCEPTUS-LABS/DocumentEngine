using Microsoft.Extensions.DependencyInjection;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

public partial class DocumentCanvas
{
    private BpmnEditorSettingsStore? _settings;
    private bool _editorParametersOpen;
    private Func<Task>? _settingsClosed;

    internal Task OpenSettingsAsync(Func<Task>? closed) => InvokeAsync(async () =>
    {
        if (_settings is null || _disposed || _host is null) return;
        await _host.CancelActiveInteractionAsync(_componentLifetime.Token);
        _settingsClosed = closed;
        _editorParametersOpen = true;
        StateHasChanged();
    });

    private async Task CloseEditorParametersAsync()
    {
        _editorParametersOpen = false;
        var closed = _settingsClosed;
        _settingsClosed = null;
        await InvokeAsync(StateHasChanged);
        if (closed is not null) await closed();
    }

    private Task HandleGridChangedAsync() => InvokeAsync(async () =>
    {
        if (!_disposed && _host is { } host) await host.RefreshGridAsync();
    });

    private async Task InitializeGridAsync()
    {
        _settings = Services.GetService<BpmnEditorSettingsStore>();
        if (_settings is null) return;
        await _settings.InitializeAsync();
        _settings.Changed += HandleGridChangedAsync;
    }
}
