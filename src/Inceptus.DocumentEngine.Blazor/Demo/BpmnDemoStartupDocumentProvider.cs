using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Reconstructs the reference host's representative BPMN sample for one modeler instance.
/// </summary>
internal sealed class BpmnDemoStartupDocumentProvider : IBpmnModelerStartupDocumentProvider
{
    private const string ResourceName =
        "Inceptus.DocumentEngine.Blazor.Demo.bpmn-demo.inceptus.json";

    internal static BpmnDemoStartupDocumentProvider Instance { get; } = new();

    private BpmnDemoStartupDocumentProvider()
    {
    }

    public async ValueTask<Document> GetInitialDocumentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var resource = typeof(BpmnDemoStartupDocumentProvider).Assembly
            .GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException(
                "The embedded BPMN demo Document is unavailable.");
        using var payload = new MemoryStream();
        await resource.CopyToAsync(payload, cancellationToken).ConfigureAwait(false);

        var import = NativeDocumentSerializer.Import(payload.ToArray());
        return import.Document ?? throw new InvalidOperationException(
            "The embedded BPMN demo Document could not be reconstructed.");
    }
}
