using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Browser-only boundary for downloading caller-provided bytes.
/// </summary>
internal static class BrowserFileDownload
{
    private const string ModulePath = "./_content/Inceptus.DocumentEngine.Bpmn.Blazor/inceptus.presentation.js";

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
