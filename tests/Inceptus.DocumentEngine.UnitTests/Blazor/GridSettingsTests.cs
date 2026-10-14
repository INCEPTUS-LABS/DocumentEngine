using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class GridSettingsTests
{
    [Fact]
    public async Task OneScopedOwnerPersistsAndRestoresAllPreferences()
    {
        var storage = new GridStorageRuntime();
        await using var provider = new ServiceCollection().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(storage).BuildServiceProvider();
        var owner = provider.GetRequiredService<BpmnEditorSettingsStore>();
        Assert.Same(owner, provider.GetRequiredService<BpmnEditorSettingsStore>());
        var changes = 0;
        owner.Changed += () => { changes++; return Task.CompletedTask; };
        var expected = new GridSettings { ShowGrid = false, SnapToGrid = true, PointDistance = 12.5 };
        Assert.True(await owner.ApplyAsync(expected));
        Assert.Equal(1, changes);
        Assert.Equal(expected, owner.Grid);
        await using var restored = new BpmnEditorSettingsStore(storage);
        await restored.InitializeAsync();
        Assert.Equal(expected, restored.Grid);
        Assert.Equal(expected, JsonSerializer.Deserialize<GridSettings>(storage.Json!));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"PointDistance\":0}")]
    [InlineData("{\"PointDistance\":-10}")]
    [InlineData("null")]
    public async Task InvalidStoredSettingsUseDefaults(string json)
    {
        await using var owner = new BpmnEditorSettingsStore(new GridStorageRuntime { Json = json });
        await owner.InitializeAsync();
        Assert.Equal(new GridSettings(), owner.Grid);
    }

    [Fact]
    public async Task FailedStorageDoesNotPartiallyApply()
    {
        await using var owner = new BpmnEditorSettingsStore(new GridStorageRuntime { CanWrite = false });
        Assert.False(await owner.ApplyAsync(new() { ShowGrid = false }));
        Assert.Equal(new GridSettings(), owner.Grid);
    }
}

internal sealed class GridStorageRuntime : IJSRuntime, IJSObjectReference
{
    internal string? Json { get; set; }
    internal bool CanWrite { get; set; } = true;
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        object? result = identifier switch
        {
            "import" => this,
            "readEditorSettings" => Json,
            "writeEditorSettings" => Write(args),
            _ => throw new InvalidOperationException(identifier)
        };
        return ValueTask.FromResult((TValue)result!);
    }
    private bool Write(object?[]? args)
    {
        Assert.Equal(BpmnEditorSettingsStore.StorageKey, args![0]);
        if (CanWrite) Json = (string?)args[1];
        return CanWrite;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
