using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class PublicationNativeDocumentTests
{
    private const string HostileText = "</script><script>alert(1)</script> Zażółć 你好";

    [Fact]
    public async Task PublicationRoundTripsExactlyAndRemainsByteDeterministic()
    {
        var source = CreateDocument("roundtrip");
        await ApplyPublicationAsync(
            source,
            "kompletacja-zamowienia",
            "Proces kompletacji zamówienia",
            HostileText);

        var first = NativeDocumentSerializer.Export(source);
        var second = NativeDocumentSerializer.Export(source);
        var imported = NativeDocumentSerializer.Import(first.AsMemory());
        var reconstructed = Assert.IsType<Document>(imported.Document);
        var reExported = NativeDocumentSerializer.Export(reconstructed);

        Assert.True(imported.Succeeded);
        Assert.Equal(source.CaptureSnapshot(), reconstructed.CaptureSnapshot());
        Assert.Equal(
            new DocumentPublicationSnapshot(
                "kompletacja-zamowienia",
                "Proces kompletacji zamówienia",
                HostileText),
            reconstructed.Publication);
        Assert.True(first.AsSpan().SequenceEqual(second.AsSpan()));
        Assert.True(first.AsSpan().SequenceEqual(reExported.AsSpan()));
        var text = Encoding.UTF8.GetString(first.AsSpan());
        Assert.Contains("\"publication\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", text, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(first.AsMemory());
        var publication = json.RootElement.GetProperty("document").GetProperty("publication");
        Assert.Equal(HostileText, publication.GetProperty("description").GetString());
        Assert.False(json.RootElement.GetProperty("document").TryGetProperty(
            "processId",
            out _));
    }

    [Fact]
    public void LegacyVersionOneWithoutPublicationImportsAsAbsent()
    {
        var source = CreateDocument("legacy");
        var payload = NativeDocumentSerializer.Export(source);
        using var json = JsonDocument.Parse(payload.AsMemory());
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.False(json.RootElement.GetProperty("document").TryGetProperty(
            "publication",
            out _));

        var imported = NativeDocumentSerializer.Import(payload.AsMemory());

        Assert.True(imported.Succeeded);
        Assert.Null(Assert.IsType<Document>(imported.Document).Publication);
    }

    private static async ValueTask ApplyPublicationAsync(
        Document document,
        string code,
        string title,
        string description)
    {
        var result = await new CommandProcessor().ExecuteAsync(
            document,
            new UpdateDocumentPublicationCommand(
                document.DocumentId,
                document.Revision,
                code,
                title,
                description));
        Assert.True(result.IsCommitted);
    }

    private static Document CreateDocument(string suffix) =>
        Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            new DocumentId($"test:n10.6:native:{suffix}")).Document);
}
