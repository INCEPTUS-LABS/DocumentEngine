using System.Text;
using System.Text.Json.Nodes;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components.Forms;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task NativeExportReturnsExactSerializerBytesWithoutChangingSessionState()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var selectedVisualId = session.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        var transient = new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "test:n10.3:tool",
            viewport: new ViewportSnapshot(1.4d, new VectorD(37d, -19d)));
        Assert.True((await session.UpdateEditorStateAsync(transient)).Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));

        var export = await host.ExportNativeDocumentAsync();

        Assert.True(export.Succeeded);
        Assert.Equal(NativeDocumentHostOperationStatus.Succeeded, export.Status);
        Assert.Empty(export.Diagnostics);
        Assert.True(NativeDocumentSerializer.Export(document).AsSpan().SequenceEqual(
            export.Payload.AsSpan()));
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.True(session.TryCaptureDocumentSnapshot(out var unchanged));
        Assert.Same(document, unchanged);
    }

    [Theory]
    [InlineData("malformed", "NATIVE_DOCUMENT_JSON_MALFORMED")]
    [InlineData("format", "NATIVE_DOCUMENT_FORMAT_INVALID")]
    [InlineData("version", "NATIVE_DOCUMENT_VERSION_UNSUPPORTED")]
    [InlineData("structure", "NATIVE_DOCUMENT_STRUCTURE_INVALID")]
    public async Task RejectedNativeImportLeavesTheExactActiveSessionUntouched(
        string corruption,
        string expectedCode)
    {
        var execution = new RecordingRenderExecution();
        var replacementCreationCount = 0;
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            replacementRendererFactory: () =>
            {
                replacementCreationCount++;
                return CreateRenderer(new RecordingRenderExecution());
            });
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var beforeState = session.CaptureState();
        Assert.True(session.TryCaptureDocumentSnapshot(out var beforeDocument));
        var payload = CorruptNativePayload(
            NativeDocumentSerializer.Export(CreateImportedDocument(
                "test:n10.3:rejected",
                31)),
            corruption);

        var result = await host.ImportNativeDocumentAsync(payload);

        Assert.False(result.Succeeded);
        Assert.Equal(NativeDocumentHostOperationStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.Same(session, Session(host));
        var afterState = session.CaptureState();
        Assert.Equal(beforeState.DocumentRevision, afterState.DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, afterState.HistoryStatus);
        Assert.Same(beforeState.EditorState, afterState.EditorState);
        Assert.Same(beforeState.CurrentScene, afterState.CurrentScene);
        Assert.Equal(beforeState.ActiveScopeId, afterState.ActiveScopeId);
        Assert.True(session.TryCaptureDocumentSnapshot(out var afterDocument));
        Assert.Same(beforeDocument, afterDocument);
        Assert.Equal(0, replacementCreationCount);
        Assert.Equal(0, execution.DisposeCount);
    }

    [Fact]
    public async Task CancelledNativeImportIsAnExactNoOp()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await host.ImportNativeDocumentAsync(
            NativeDocumentSerializer.Export(CreateImportedDocument(
                "test:n10.3:cancelled",
                39)).AsMemory(),
            cancellation.Token);

        Assert.Equal(NativeDocumentHostOperationStatus.Cancelled, result.Status);
        Assert.Same(session, Session(host));
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.True(session.TryCaptureDocumentSnapshot(out var unchanged));
        Assert.Same(document, unchanged);
        Assert.Equal(0, execution.DisposeCount);
    }

    [Fact]
    public async Task FileWithinExplicitBrowserLimitIsReadWithThatLimitAndImported()
    {
        var initialExecution = new RecordingRenderExecution();
        var replacementExecution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            initialExecution,
            observer,
            replacementRendererFactory: () => CreateRenderer(replacementExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var importedDocument = CreateImportedDocument("test:n10.3:bounded-file", 40);
        var payload = NativeDocumentSerializer.Export(importedDocument).ToArray();
        var file = new RecordingBrowserFile(payload, payload.LongLength);

        var result = await DocumentCanvas.ReadAndImportNativeDocumentFileAsync(
            file,
            host.ImportNativeDocumentAsync);

        Assert.False(result.FileTooLarge);
        Assert.True(result.ImportResult?.Succeeded == true);
        Assert.Equal(1, file.OpenReadCount);
        Assert.Equal(DocumentCanvas.MaximumNativeDocumentFileSize, file.MaximumAllowedSize);
        Assert.Equal("standby-canvas", host.CaptureState().ActiveCanvasElementId);
        var session = Session(host);
        Assert.Equal(importedDocument.DocumentId, session.CaptureState().DocumentId);
        Assert.Equal(importedDocument.Revision, session.CaptureState().DocumentRevision);
    }

    [Fact]
    public async Task OverLimitBrowserFileRejectsBeforeReadAndLeavesActiveSessionUsable()
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            observer,
            replacementRendererFactory: () =>
                throw new InvalidOperationException("The importer must not be called."));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var selectedVisualId = session.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        var transient = new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "test:n10.3:file-limit",
            viewport: new ViewportSnapshot(1.25d, new VectorD(23d, -17d)));
        Assert.True((await session.UpdateEditorStateAsync(transient)).Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));
        var importCount = 0;
        var file = new RecordingBrowserFile(
            [],
            DocumentCanvas.MaximumNativeDocumentFileSize + 1L);

        var result = await DocumentCanvas.ReadAndImportNativeDocumentFileAsync(
            file,
            (payload, cancellationToken) =>
            {
                importCount++;
                return host.ImportNativeDocumentAsync(payload, cancellationToken);
            });

        Assert.True(result.FileTooLarge);
        Assert.Null(result.ImportResult);
        Assert.Equal(0, file.OpenReadCount);
        Assert.Equal(0, importCount);
        Assert.Equal(
            "CANVAS_NATIVE_DOCUMENT_FILE_TOO_LARGE",
            DocumentCanvas.NativeDocumentFileTooLargeCode);
        Assert.Equal(
            "The selected native Document exceeds the 16 MiB browser import limit.",
            DocumentCanvas.NativeDocumentFileTooLargeMessage);
        Assert.Same(session, Session(host));
        var after = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.True(session.TryCaptureDocumentSnapshot(out var unchanged));
        Assert.Same(document, unchanged);
        Assert.Equal(0, execution.DisposeCount);

        await observer.RaiseAsync(new Canvas2DSurfaceSize(960d, 640d, 1d));

        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
        Assert.Contains("resize", execution.Calls);
        Assert.Equal(0, execution.DisposeCount);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("initialization")]
    [InlineData("attachment")]
    public async Task SessionOpenFailurePreservesExactActiveSessionAndRenderer(
        string failureStage)
    {
        var initialExecution = new RecordingRenderExecution();
        var replacementExecution = new RecordingRenderExecution();
        if (failureStage == "initialization")
        {
            replacementExecution.InitializeResult = new Canvas2DInteropOperationResult
            {
                Succeeded = false,
                Code = "TEST_REPLACEMENT_INITIALIZATION_FAILED",
            };
        }
        else if (failureStage == "attachment")
        {
            replacementExecution.MeasureTextException =
                new InvalidOperationException("Replacement attachment failed.");
        }

        Func<Canvas2DRenderer> replacementFactory = failureStage == "factory"
            ? () => throw new InvalidOperationException("Replacement creation failed.")
            : () => CreateRenderer(replacementExecution);
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            initialExecution,
            observer,
            replacementRendererFactory: replacementFactory);
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var selectedVisualId = session.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        var transient = new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "test:n10.3:preserved-tool",
            viewport: new ViewportSnapshot(1.4d, new VectorD(37d, -19d)));
        Assert.True((await session.UpdateEditorStateAsync(transient)).Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));
        var payload = NativeDocumentSerializer.Export(document);

        var result = await host.ImportNativeDocumentAsync(payload.AsMemory());

        Assert.False(result.Succeeded);
        Assert.Equal(NativeDocumentHostOperationStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "CANVAS_NATIVE_DOCUMENT_REPLACEMENT_FAILED");
        Assert.Equal(
            ("CANVAS_NATIVE_DOCUMENT_REPLACEMENT_FAILED",
                "The native Document was valid, but its editor session could not be opened."),
            DocumentCanvas.NativeDocumentImportMessage(result));
        Assert.Same(session, Session(host));
        var after = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.False(after.IsClosed);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.True(session.TryCaptureDocumentSnapshot(out var unchanged));
        Assert.Same(document, unchanged);
        var hostState = host.CaptureState();
        Assert.True(hostState.IsInitialized);
        Assert.True(hostState.LatestPresentationSucceeded);
        Assert.Equal(after.DocumentId, hostState.Session?.DocumentId);
        Assert.Equal(after.DocumentRevision, hostState.Session?.DocumentRevision);
        Assert.Equal(after.HistoryStatus, hostState.Session?.HistoryStatus);
        Assert.Same(after.EditorState, hostState.Session?.EditorState);
        Assert.Equal("active-canvas", hostState.ActiveCanvasElementId);
        Assert.Equal(0, initialExecution.DisposeCount);
        Assert.Equal(0, PointerObserver(host).DisposeCount);
        if (failureStage != "factory")
        {
            Assert.Contains("initialize:standby-canvas", replacementExecution.Calls);
            Assert.Equal(1, replacementExecution.DisposeCount);
        }

        await observer.RaiseAsync(new Canvas2DSurfaceSize(960d, 640d, 1d));

        Assert.Same(session, Session(host));
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
        Assert.Contains("resize", initialExecution.Calls);
        Assert.Equal(0, initialExecution.DisposeCount);
        var export = await host.ExportNativeDocumentAsync();
        Assert.True(export.Succeeded);
        Assert.True(NativeDocumentSerializer.Export(document).AsSpan().SequenceEqual(
            export.Payload.AsSpan()));
    }

    [Fact]
    public async Task ValidImportsReplaceAndDisposeSessionsWithFreshRuntimeStateAndRepeatCleanly()
    {
        var initialExecution = new RecordingRenderExecution();
        var firstImportExecution = new RecordingRenderExecution();
        var secondImportExecution = new RecordingRenderExecution();
        var replacements = new Queue<RecordingRenderExecution>(
            [firstImportExecution, secondImportExecution]);
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            initialExecution,
            observer,
            replacementRendererFactory: () => CreateRenderer(replacements.Dequeue()));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var initialSession = Session(host);
        var initialState = initialSession.CaptureState();
        var oldTransient = new EditorStateSnapshot(
            selection: [initialState.CurrentScene!.Items
                .Select(static item => item.Origin.VisualStateId)
                .First(static id => id is not null)!],
            activeToolId: "test:n10.3:old-tool",
            viewport: new ViewportSnapshot(1.75d, new VectorD(73d, -41d)));
        Assert.True((await initialSession.UpdateEditorStateAsync(oldTransient)).Succeeded);
        await initialSession.WaitForIdleAsync();
        Assert.NotNull(await host.ValidateAsync());
        Assert.NotNull(host.CaptureState().ValidationSnapshot);
        var documentA = CreateImportedDocument("test:n10.3:document-a", 41);
        var bytesA = NativeDocumentSerializer.Export(documentA);

        var importA = await host.ImportNativeDocumentAsync(bytesA.AsMemory());

        Assert.True(importA.Succeeded);
        var sessionA = Session(host);
        var stateA = sessionA.CaptureState();
        Assert.NotSame(initialSession, sessionA);
        Assert.True(initialSession.CaptureState().IsClosed);
        Assert.Equal(documentA.DocumentId, stateA.DocumentId);
        Assert.Equal(documentA.Revision, stateA.DocumentRevision);
        Assert.Equal(0, stateA.HistoryStatus.EntryCount);
        Assert.False(stateA.HistoryStatus.CanUndo);
        Assert.False(stateA.HistoryStatus.CanRedo);
        Assert.Equal(documentA.SemanticModel.RootScopeId, stateA.ActiveScopeId);
        Assert.Empty(stateA.EditorState.Selection);
        Assert.Null(stateA.EditorState.SemanticSceneSelection);
        Assert.Null(stateA.EditorState.ActiveGesture);
        Assert.NotEqual(oldTransient.Viewport, stateA.EditorState.Viewport);
        Assert.Equal(
            ModelProfileElementViewStateSnapshot.Empty,
            stateA.ModelProfileElementViewState);
        Assert.Equal(EditingSessionStatus.Ready, stateA.Status);
        Assert.NotNull(stateA.CurrentScene);
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Equal(1, initialExecution.DisposeCount);
        Assert.Equal("standby-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Contains("initialize:standby-canvas", firstImportExecution.Calls);
        var exportA = await host.ExportNativeDocumentAsync();
        Assert.True(exportA.Succeeded);
        Assert.True(bytesA.AsSpan().SequenceEqual(exportA.Payload.AsSpan()));

        var documentB = CreateImportedDocument("test:n10.3:document-b", 77);
        var bytesB = NativeDocumentSerializer.Export(documentB);
        var importB = await host.ImportNativeDocumentAsync(bytesB.AsMemory());

        Assert.True(importB.Succeeded);
        var sessionB = Session(host);
        var stateB = sessionB.CaptureState();
        Assert.NotSame(sessionA, sessionB);
        Assert.True(sessionA.CaptureState().IsClosed);
        Assert.Equal(documentB.DocumentId, stateB.DocumentId);
        Assert.Equal(new DocumentRevision(77), stateB.DocumentRevision);
        Assert.Equal(0, stateB.HistoryStatus.EntryCount);
        Assert.Equal(EditingSessionStatus.Ready, stateB.Status);
        Assert.Equal(1, firstImportExecution.DisposeCount);
        Assert.Equal(0, secondImportExecution.DisposeCount);
        Assert.Equal("active-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Contains("initialize:active-canvas", secondImportExecution.Calls);
        var exportB = await host.ExportNativeDocumentAsync();
        Assert.True(exportB.Succeeded);
        Assert.True(bytesB.AsSpan().SequenceEqual(exportB.Payload.AsSpan()));
        Assert.Empty(replacements);
    }

    private static Document CreateImportedDocument(string id, ulong revision)
    {
        var documentId = new DocumentId(id);
        var documentRevision = new DocumentRevision(revision);
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, documentRevision),
            new VisualModelSnapshot(documentId, documentRevision),
            new DocumentMetadataSnapshot(documentId, documentRevision));
        var result = DocumentReconstructor.Reconstruct(snapshot);
        Assert.True(result.Succeeded);
        return Assert.IsType<Document>(result.Document);
    }

    private static byte[] CorruptNativePayload(
        System.Collections.Immutable.ImmutableArray<byte> payload,
        string corruption)
    {
        if (corruption == "malformed")
        {
            return Encoding.UTF8.GetBytes("{\"format\":\"Inceptus.Document\"");
        }

        var root = Assert.IsType<JsonObject>(JsonNode.Parse(
            Encoding.UTF8.GetString(payload.AsSpan())));
        switch (corruption)
        {
            case "format":
                root["format"] = "Other.Document";
                break;
            case "version":
                root["formatVersion"] = NativeDocumentSerializer.FormatVersion + 1;
                break;
            case "structure":
                Assert.True(root.Remove("document"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private sealed class RecordingBrowserFile(byte[] contents, long reportedSize) : IBrowserFile
    {
        internal int OpenReadCount { get; private set; }

        internal long? MaximumAllowedSize { get; private set; }

        public string Name => "test.inceptus.json";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => reportedSize;

        public string ContentType => "application/json";

        public Stream OpenReadStream(
            long maxAllowedSize = 512000,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenReadCount++;
            MaximumAllowedSize = maxAllowedSize;
            if (reportedSize > maxAllowedSize)
            {
                throw new IOException("The reported browser file size exceeds the read limit.");
            }

            return new MemoryStream(contents, writable: false);
        }
    }
}
