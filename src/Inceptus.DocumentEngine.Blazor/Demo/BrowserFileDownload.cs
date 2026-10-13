using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Browser-only boundary for downloading caller-provided bytes.
/// </summary>
internal static class BrowserFileDownload
{
    private const string ModulePath = "./inceptus.documents.js";

    internal static async ValueTask DownloadAsync(
        IJSRuntime jsRuntime,
        ReadOnlyMemory<byte> bytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var module = await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            [ModulePath]);
        try
        {
            await module.InvokeVoidAsync(
                "downloadFile",
                cancellationToken,
                bytes.ToArray(),
                contentType,
                fileName);
        }
        finally
        {
            await module.DisposeAsync();
        }
    }
}

