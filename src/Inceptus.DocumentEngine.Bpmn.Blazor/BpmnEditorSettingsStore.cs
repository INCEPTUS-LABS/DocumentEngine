using System.Text.Json;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// One editor-wide settings owner per browser application/circuit. Browser storage is
/// independent of native JSON and BPMN documents. All modelers in the scope share this owner.
/// </summary>
public sealed class BpmnEditorSettingsStore(IJSRuntime javaScript) : IGridSettingsSource, IAsyncDisposable
{
    internal const string StorageKey = "inceptus.bpmn.editor.settings.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IJSObjectReference? _module;
    private bool _initialized;
    private GridSettings _grid = new();

    public GridSettings Grid => Volatile.Read(ref _grid);

    public event Func<Task>? Changed;

    public async ValueTask InitializeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            _module ??= await javaScript.InvokeAsync<IJSObjectReference>("import",
                "./_content/Inceptus.DocumentEngine.Bpmn.Blazor/inceptus.presentation.js").ConfigureAwait(false);
            var json = await _module.InvokeAsync<string?>("readEditorSettings", StorageKey).ConfigureAwait(false);
            if (json is not null)
            {
                try
                {
                    var restored = JsonSerializer.Deserialize<GridSettings>(json);
                    if (restored is not null) Volatile.Write(ref _grid, restored);
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    // Malformed or obsolete preferences cannot invalidate a diagram.
                }
            }
            _initialized = true;
        }
        catch (JSException)
        {
            // Defaults remain usable when browser storage is unavailable. Apply reports failure.
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> ApplyAsync(GridSettings grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        await InitializeAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_module is null) return false;
            if (!await _module.InvokeAsync<bool>("writeEditorSettings", StorageKey,
                    JsonSerializer.Serialize(grid)).ConfigureAwait(false)) return false;
            Volatile.Write(ref _grid, grid);
        }
        catch (JSException) { return false; }
        finally { _gate.Release(); }
        if (Changed is { } changed)
            foreach (var subscriber in changed.GetInvocationList().Cast<Func<Task>>())
                await subscriber().ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync().ConfigureAwait(false); }
            catch (JSDisconnectedException) { }
        }
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
