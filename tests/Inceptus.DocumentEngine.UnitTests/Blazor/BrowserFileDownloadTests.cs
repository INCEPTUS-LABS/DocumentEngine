using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class BrowserFileDownloadTests
{
    [Fact]
    public async Task DownloadPassesExactBrowserDataAndDisposesTheModuleReference()
    {
        var module = new RecordingModule();
        var runtime = new RecordingJsRuntime(module);
        byte[] bytes = [0, 1, 2, 127, 128, 254, 255];

        await BrowserFileDownload.DownloadAsync(
            runtime,
            bytes,
            "application/json",
            "document.inceptus.json");

        var import = Assert.Single(runtime.Invocations);
        Assert.Equal("import", import.Identifier);
        Assert.Equal(
            "./_content/Inceptus.DocumentEngine.Bpmn.Blazor/inceptus.presentation.js",
            Assert.Single(import.Arguments));
        var download = Assert.Single(module.Invocations);
        Assert.Equal("downloadFile", download.Identifier);
        Assert.Equal(bytes, Assert.IsType<byte[]>(download.Arguments[0]));
        Assert.Equal("application/json", download.Arguments[1]);
        Assert.Equal("document.inceptus.json", download.Arguments[2]);
        Assert.Equal(1, module.DisposeCount);
    }

    private sealed class RecordingJsRuntime(IJSObjectReference module) : IJSRuntime
    {
        internal List<Invocation> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(new Invocation(identifier, args ?? []));
            return ValueTask.FromResult((TValue)(object)module);
        }
    }

    private sealed class RecordingModule : IJSObjectReference
    {
        internal List<Invocation> Invocations { get; } = [];

        internal int DisposeCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(new Invocation(identifier, args ?? []));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Invocation(string Identifier, object?[] Arguments);
}
