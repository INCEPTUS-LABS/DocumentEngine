using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Export;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("StartEvent", "start-event")]
    [InlineData("Task", "task")]
    [InlineData("UserTask", "task")]
    [InlineData("ManualTask", "task")]
    [InlineData("ServiceTask", "task")]
    [InlineData("SendTask", "task")]
    [InlineData("ReceiveTask", "task")]
    [InlineData("ExclusiveGateway", "gateway")]
    [InlineData("ParallelGateway", "gateway")]
    [InlineData("InclusiveGateway", "gateway")]
    [InlineData("EventBasedGateway", "gateway")]
    [InlineData("EndEvent", "end-event")]
    [InlineData("SubProcess", null)]
    [InlineData("MessageCatchEvent", null)]
    [InlineData("TimerCatchEvent", null)]
    public async Task AppearanceFamiliesPreserveGeometryAndInteractionPaint(string type, string? family)
    {
        await using var host = A23Host(A24Snapshot(new SemanticTypeId("BPMN." + type)));
        await host.InitializeAsync("appearance-family", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var originalItems = session.CaptureState().CurrentScene!.Items;
        var edit = new SetBpmnDocumentAppearanceCommand(before.DocumentId, before.Revision,
            [new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed"))]);
        var edited = await session.ExecuteAsync(edit);
        Assert.True(edited.IsCommitted, PublishDiagnostics(edited.Diagnostics));
        await session.WaitForIdleAsync();
        var after = AttachedDocument(session).CaptureSnapshot();
        A23PresentationEqual(before, after);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        var scene = session.CaptureState().CurrentScene!;
        var body = Assert.Single(scene.Items, item => item.Origin.VisualStateId?.Value == "v-source" && item.Layer == Canvas2DSceneLayer.Content);
        var palette = BpmnAppearanceSettings.Preset(family is null ? "white" : "mixed", family ?? "task");
        Assert.Equal(palette.Background, body.Style.Fill);
        Assert.Equal(palette.Border, body.Style.Stroke);
        Assert.All(scene.Items.Where(item => item.Origin.VisualStateId == body.Origin.VisualStateId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text), item => Assert.Equal(palette.Text, item.Style.Fill));
        foreach (var item in originalItems.Where(item => (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0))
        {
            var actual = scene.Items.Single(candidate => candidate.Id == item.Id);
            Assert.Equal(item.Geometry, actual.Geometry);
            Assert.Equal(item.Transform, actual.Transform);
            Assert.Equal(item.HitTestPolicy, actual.HitTestPolicy);
        }
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [body.Origin.VisualStateId!], hoveredObjectId: body.Id));
        var selected = session.CaptureState().CurrentScene!;
        Assert.Equal(palette.Background, selected.Items.Single(item => item.Id == body.Id).Style.Fill);
        Assert.Contains(selected.Items, item => item.Origin.StableSourceKey == "selection:" + body.Id.Value && item.Style.Stroke == "#2563eb");
        Assert.DoesNotContain(selected.Items, item => item.Origin.VisualStateId == body.Origin.VisualStateId && item.Style.Stroke == "#f59e0b");
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(hoveredObjectId: body.Id));
        Assert.Contains(session.CaptureState().CurrentScene!.Items, item => item.Origin.StableSourceKey == "hover:" + body.Id.Value && item.Style.Stroke == "#f59e0b");
    }

    [Fact]
    public async Task AppearanceApplyNativeRoundtripExactUndoRedoAndUnknownMetadata()
    {
        var seed = A24Snapshot();
        var key = BpmnAppearanceSettings.Key("white", "task", "background");
        var metadata = new DocumentMetadataSnapshot(seed.DocumentId, seed.Revision, extensionProperties:
            [new("bpmn:appearance.future", PropertyValue.FromInteger(77)), new(key, PropertyValue.FromText("invalid"))]);
        await using var host = A23Host(new(seed.SemanticModel, seed.VisualModel, metadata));
        await host.InitializeAsync("appearance-history", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var form = Assert.IsType<DocumentCanvasModelViewPropertiesSnapshot>(await host.OpenModelViewPropertiesAsync(true));
        var draft = new DocumentCanvasModelViewPropertiesDraft(form);
        draft.Appearance.Set("task", "background", "#aabbcc");
        draft.Appearance.SelectScheme("mixed");
        draft.Appearance.Set("task", "text", "#112233");
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
        Assert.True((await host.ApplyModelViewPropertiesAsync(draft)).Succeeded);
        var after = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal("#AABBCC", after.Metadata.ExtensionProperties[key].TextValue);
        Assert.Equal(77, after.Metadata.ExtensionProperties["bpmn:appearance.future"].IntegerValue);
        A23PresentationEqual(before, after);
        var imported = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(after).AsMemory());
        Assert.True(imported.Succeeded);
        Assert.Equal(after.Metadata.ExtensionProperties, imported.Document!.CaptureSnapshot().Metadata.ExtensionProperties);
        await host.UndoAsync();
        Assert.Equal(before.Metadata.ExtensionProperties, AttachedDocument(session).Metadata.ExtensionProperties);
        await host.RedoAsync();
        Assert.Equal(after.Metadata.ExtensionProperties, AttachedDocument(session).Metadata.ExtensionProperties);
        var current = AttachedDocument(session).CaptureSnapshot();
        var noChange = await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(current.DocumentId, current.Revision,
            [new(key, PropertyValue.FromText("#AABBCC"))]));
        Assert.True(noChange.Succeeded);
        Assert.Equal(current.Revision, AttachedDocument(session).Revision);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        var invalid = await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(current.DocumentId, current.Revision,
            [new(key, PropertyValue.FromText("red"))]));
        Assert.False(invalid.Succeeded);
        Assert.Equal(current, AttachedDocument(session).CaptureSnapshot());
        var stale = await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(current.DocumentId, before.Revision,
            [new(key, PropertyValue.FromText("#112233"))]));
        Assert.False(stale.Succeeded);
    }

    [Fact]
    public async Task AppearanceExportAndPublishedProcessContainResolvedPaintWithUnchangedExecution()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("appearance-publish", "container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var captureBefore = (await host.CapturePresentationAsync())!.Capture!;
        var builder = new PublishedProcessPackageBuilder(new BpmnPublishedTokenRoleClassifier(), new BpmnPublishedNodeDataMapper());
        var previous = builder.Build(captureBefore);
        Assert.True(previous.Succeeded, PublishDiagnostics(previous.Diagnostics));
        var document = AttachedDocument(session).CaptureSnapshot();
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(document.DocumentId, document.Revision,
            [new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed")),
             new(BpmnAppearanceSettings.Key("mixed", "task", "text"), PropertyValue.FromText("#123456"))]))).IsCommitted);
        await session.WaitForIdleAsync();
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var prepared = GraphicalExportPreparer.Prepare(capture);
        Assert.True(prepared.Succeeded);
        var svg = SvgGraphicalExporter.Export(prepared.Scene!);
        Assert.True(svg.Succeeded);
        var svgText = Encoding.UTF8.GetString(svg.Artifact!.Content.AsSpan());
        Assert.Contains("#123456", svgText, StringComparison.Ordinal);
        Assert.Contains("#E7EFF8", svgText, StringComparison.Ordinal);
        var browser = new AppearanceExportRuntime();
        Assert.True((await new Canvas2DGraphicalExporter(browser).ExportAsync(capture, GraphicalExportFormat.Png)).Succeeded);
        Assert.Contains(browser.Frame!.Items, item => item.Fill == "#123456");
        Assert.Contains(browser.Frame.Items, item => item.Fill == "#E7EFF8" && item.Stroke == "#5D7FA5");
        var published = builder.Build(capture);
        Assert.True(published.Succeeded, PublishDiagnostics(published.Diagnostics));
        using var oldJson = JsonDocument.Parse(previous.Package!.ProcessJson.AsMemory());
        using var json = JsonDocument.Parse(published.Package!.ProcessJson.AsMemory());
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        foreach (var field in new[] { "tokenGraph", "runtime" })
            Assert.Equal(oldJson.RootElement.GetProperty(field).GetRawText(), json.RootElement.GetProperty(field).GetRawText());
        var text = Encoding.UTF8.GetString(published.Package.ProcessJson.AsSpan());
        Assert.Contains("#123456", text, StringComparison.Ordinal);
        Assert.Contains("#E7EFF8", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bpmn:appearance", text, StringComparison.Ordinal);
        var archive = ReadPublishedArchive(published.Package.Archive);
        Assert.Contains("#123456", Encoding.UTF8.GetString(archive["process.data.js"]), StringComparison.Ordinal);
        Assert.All(capture.Scene.Items, item => Assert.False((item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0));
    }

    // Observe the actual PNG browser boundary; encoding is covered by the JavaScript/browser checks.
    private sealed class AppearanceExportRuntime : IJSRuntime, IJSObjectReference
    {
        internal Canvas2DRenderFrame? Frame { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier is "import" or "createGraphicalExport") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "resources") return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>("{}")!);
            if (identifier == "dispose") return ValueTask.FromResult(default(TValue)!);
            Assert.Equal("png", identifier);
            Frame = Assert.IsType<Canvas2DRenderFrame>(args![0]);
            byte[] bytes = new byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
            return ValueTask.FromResult((TValue)(object)bytes);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
