using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN106PublicationIntegrationTests
{
    private const string BootstrapPrefix =
        "globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = ";

    [Fact]
    public async Task ComplexDocumentSavesPublishesUndoesAndNativeRoundTripsPublication()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf",
                        400,
                        TextFontStyle.Normal),
                ],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync(
            "phase-n106",
            new Canvas2DSurfaceSize(1400d, 900d, 1d))).Succeeded);
        var attached = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        Assert.Null(composition.Document.Publication);
        var first = new DocumentPublicationSnapshot(
            "kompletacja-zamowienia",
            "Proces kompletacji zamówienia",
            "Przebieg procesu kompletacji zamówienia.");

        var saved = await session.ExecuteAsync(new UpdateDocumentPublicationCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            first));
        await session.WaitForIdleAsync();

        Assert.True(saved.IsCommitted, Diagnostics(saved.Diagnostics));
        Assert.Equal(first, composition.Document.Publication);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);

        var second = new DocumentPublicationSnapshot(
            first.Code,
            "Proces kompletacji zamówienia — publikacja",
            "</script><script>alert(1)</script> 你好");
        Assert.True((await session.ExecuteAsync(new UpdateDocumentPublicationCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            second))).IsCommitted);
        await session.WaitForIdleAsync();
        var state = session.CaptureState();
        var publishProfiles = state.ModelProfileViewState.WithPreferredVisibility(
            OrganizationalModelProfile.Id,
            isVisible: false);
        var publishElements = new ModelProfileElementViewStateSnapshot(
            state.ModelProfileElementViewState.CollapsedElements.Where(entry =>
                entry.ProfileId != OrganizationalModelProfile.Id));
        var capture = await session.CapturePresentationAsync(
            publishProfiles,
            publishElements);
        var built = new PublishedProcessPackageBuilder(
            new BpmnPublishedTokenRoleClassifier(),
            new BpmnPublishedNodeDataMapper()).Build(capture.Capture!);

        Assert.True(built.Succeeded, Diagnostics(built.Diagnostics));
        var package = Assert.IsType<PublishedProcessPackage>(built.Package);
        Assert.Equal(second.Code, package.Snapshot.Publication.Code);
        Assert.Equal(second.Title, package.Snapshot.Publication.Title);
        Assert.Equal(second.Description, package.Snapshot.Publication.Description);
        using var json = JsonDocument.Parse(package.ProcessJson.AsMemory());
        Assert.False(json.RootElement.TryGetProperty("processId", out _));
        var processJson = Encoding.UTF8.GetString(package.ProcessJson.AsSpan());
        var processData = Encoding.UTF8.GetString(
            ArchiveEntry(package.Archive, "process.data.js"));
        Assert.Equal(processJson, processData[BootstrapPrefix.Length..^2]);
        Assert.DoesNotContain("</script>", processData, StringComparison.OrdinalIgnoreCase);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(first, composition.Document.Publication);
        var native = NativeDocumentSerializer.Export(composition.Document);
        var imported = NativeDocumentSerializer.Import(
            native.AsMemory(),
            composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, Diagnostics(imported.Diagnostics));
        Assert.Equal(first, imported.Document!.Publication);
        Assert.True(native.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(imported.Document).AsSpan()));
    }

    private static byte[] ArchiveEntry(
        System.Collections.Immutable.ImmutableArray<byte> payload,
        string name)
    {
        using var input = new MemoryStream(payload.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry(name)!;
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));
}
