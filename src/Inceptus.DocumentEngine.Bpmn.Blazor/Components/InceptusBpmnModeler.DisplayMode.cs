using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

public partial class InceptusBpmnModeler
{
    private BpmnModelerDisplayMode _displayMode;
    private BpmnModelerDisplayMode _lastDisplayModeParameter;
    private int _pendingDisplayModeToggles;
    private Task? _displayModeNotifications;

    /// <summary>
    /// Initial layout and subsequent host-driven layout changes. Defaults to Normal.
    /// Use @bind-DisplayMode to coordinate host chrome and container sizing. The component
    /// retains user changes across unrelated renders when this parameter is omitted.
    /// </summary>
    [Parameter]
    public BpmnModelerDisplayMode DisplayMode { get; set; }

    /// <summary>
    /// Reports user layout changes in order. Update host layout before awaiting other work.
    /// Host-driven parameter changes do not echo a notification. No document events are raised.
    /// </summary>
    [Parameter]
    public EventCallback<BpmnModelerDisplayMode> DisplayModeChanged { get; set; }

    private BpmnModelerDisplayPresentation DisplayPresentation => new(
        _displayMode, EventCallback.Factory.Create(this, ToggleDisplayModeAsync));

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!Enum.IsDefined(DisplayMode)) throw new ArgumentOutOfRangeException(nameof(DisplayMode));
        if (_lastDisplayModeParameter != DisplayMode)
        {
            _lastDisplayModeParameter = DisplayMode;
            _displayMode = DisplayMode;
        }
    }

    internal Task ToggleDisplayModeAsync()
    {
        if (_disposed) return Task.CompletedTask;
        _pendingDisplayModeToggles++;
        // Do not allow async host callbacks to complete out of order. Additional clicks
        // retain their intent and run against the mode accepted by the preceding callback.
        return _displayModeNotifications is { IsCompleted: false }
            ? _displayModeNotifications
            : _displayModeNotifications = NotifyDisplayModeChangesAsync();
    }

    private async Task NotifyDisplayModeChangesAsync()
    {
        try
        {
            while (!_disposed && _pendingDisplayModeToggles > 0)
            {
                _pendingDisplayModeToggles--;
                _displayMode = _displayMode == BpmnModelerDisplayMode.Normal
                    ? BpmnModelerDisplayMode.Maximized : BpmnModelerDisplayMode.Normal;
                StateHasChanged();
                await DisplayModeChanged.InvokeAsync(_displayMode);
            }
        }
        finally
        {
            _pendingDisplayModeToggles = 0;
        }
    }
}
