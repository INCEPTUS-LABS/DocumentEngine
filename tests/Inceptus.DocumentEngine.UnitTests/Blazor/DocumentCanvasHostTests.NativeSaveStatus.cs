using System.Text;
using System.Text.Json.Nodes;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task NativeSaveWaitsForAcceptedPreparationAndExportsItsInstalledRevision()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateNativeTestHost(execution,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("active", "standby", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var task = Assert.Single(before.SemanticModel.Elements);
        execution.BlockMeasureText = true;
        var command = session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId,
            document.Revision, task.Id, BpmnSemanticProperties.Name, "Name awaiting accepted browser text preparation")).AsTask();
        Task<NativeDocumentHostExportResult> saving;
        try
        {
            await execution.TextMeasurementStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(command.IsCompleted);
            Assert.Same(before, document.CaptureSnapshot());
            Assert.False(session.TryCaptureDocumentSnapshot(out _));
            saving = host.ExportNativeDocumentAsync().AsTask();
            Assert.False(saving.IsCompleted);
        }
        finally
        {
            execution.BlockMeasureText = false;
            execution.ReleaseTextMeasurement();
        }

        Assert.True((await command.WaitAsync(TimeSpan.FromSeconds(10))).IsCommitted);
        var saved = await saving.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(saved.Succeeded);
        var installed = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), installed.Revision);
        Assert.NotNull(installed.VisualModel.RoutingScopes);
        Assert.Equal(installed.Revision, saved.SaveCheckpoint!.Revision);
        Assert.Equal(NativeDocumentSerializer.Export(installed).ToArray(), saved.Payload.ToArray());
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        await session.WaitForIdleAsync();
    }

    [Fact]
    public async Task NativeSaveWaitsForTheCommandGateAndCancellationLeavesSavedStateUntouched()
    {
        await using var host = CreateNativeTestHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("active", "standby", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var savedBefore = host.CaptureNativeSaveStatus();
        var gate = Assert.IsType<SemaphoreSlim>(typeof(EditingSession)
            .GetField("_commandGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session));
        Task<NativeDocumentHostExportResult> waiting;
        await gate.WaitAsync();
        try
        {
            Assert.False(session.TryCaptureDocumentSnapshot(out _));
            using var cancellation = new CancellationTokenSource();
            var cancelled = host.ExportNativeDocumentAsync(cancellation.Token).AsTask();
            Assert.False(cancelled.IsCompleted);
            cancellation.Cancel();
            Assert.Equal(NativeDocumentHostOperationStatus.Cancelled,
                (await cancelled.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            waiting = host.ExportNativeDocumentAsync().AsTask();
            Assert.False(waiting.IsCompleted);
        }
        finally
        {
            gate.Release();
        }

        var exported = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(exported.Succeeded);
        Assert.Equal(before.Revision, exported.SaveCheckpoint!.Revision);
        Assert.Equal(NativeDocumentSerializer.Export(before).ToArray(), exported.Payload.ToArray());
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        var savedAfter = Assert.IsType<BpmnModelerNativeSaveStatus>(host.CaptureNativeSaveStatus());
        Assert.Equal(savedBefore!.CurrentCheckpoint, savedAfter.CurrentCheckpoint);
        Assert.Equal(savedBefore.SavedCheckpoint, savedAfter.SavedCheckpoint);
    }

    [Fact]
    public async Task NativeSaveRequiresAnIssuedAcknowledgementAndDoesNotCleanLaterEdits()
    {
        await using var host = CreateNativeTestHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("active", "standby", "container");
        var initial = Assert.IsType<BpmnModelerNativeSaveStatus>(host.CaptureNativeSaveStatus());
        Assert.True(initial.IsDirty);
        Assert.False(host.AcknowledgeNativeSave(initial.CurrentCheckpoint));

        var first = await host.ExportNativeDocumentAsync();
        Assert.True(first.Succeeded);
        Assert.True(host.CaptureNativeSaveStatus()!.IsDirty);
        Assert.True(host.AcknowledgeNativeSave(first.SaveCheckpoint!));
        Assert.False(host.CaptureNativeSaveStatus()!.IsDirty);

        var session = Session(host);
        var state = session.CaptureState();
        using var commandCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.True((await session.ExecuteAsync(new UpdateDocumentPublicationCommand(
            state.DocumentId, state.DocumentRevision, "saved-process", "Changed", null), commandCancellation.Token)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(15))).IsCommitted);
        Assert.True(host.CaptureNativeSaveStatus()!.IsDirty);
        Assert.True(host.AcknowledgeNativeSave(first.SaveCheckpoint!));
        Assert.True(host.CaptureNativeSaveStatus()!.IsDirty);
        var second = await host.ExportNativeDocumentAsync();
        Assert.True(second.Succeeded);
        Assert.True(host.AcknowledgeNativeSave(second.SaveCheckpoint!));
        Assert.False(host.CaptureNativeSaveStatus()!.IsDirty);
        Assert.True(host.AcknowledgeNativeSave(first.SaveCheckpoint!));
        Assert.Equal(second.SaveCheckpoint, host.CaptureNativeSaveStatus()!.SavedCheckpoint);
        Assert.False(host.CaptureNativeSaveStatus()!.IsDirty);
    }

    [Fact]
    public async Task NativeSaveCheckpointCannotAcknowledgeAnotherAttachmentOfTheSameDocument()
    {
        await using var host = CreateNativeTestHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("active", "standby", "container");
        var first = await host.ExportNativeDocumentAsync();
        Assert.True(first.Succeeded);
        var imported = await host.ImportNativeDocumentAsync(first.Payload.AsMemory());
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(static item => item.Message)));
        var current = host.CaptureNativeSaveStatus()!;
        Assert.Equal(first.SaveCheckpoint!.DocumentId, current.CurrentCheckpoint.DocumentId);
        Assert.Equal(first.SaveCheckpoint.Revision, current.CurrentCheckpoint.Revision);
        Assert.NotEqual(first.SaveCheckpoint.AttachmentToken, current.CurrentCheckpoint.AttachmentToken);
        Assert.False(host.AcknowledgeNativeSave(first.SaveCheckpoint));
        Assert.False(current.IsDirty);
    }

    [Fact]
    public async Task IncompatibleV2RequiresExplicitRecoveryAndRecoveryRemainsDirty()
    {
        await using var host = CreateNativeTestHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("active", "standby", "container");
        var exported = await host.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded);
        Assert.True(host.AcknowledgeNativeSave(exported.SaveCheckpoint!));
        var before = Session(host);
        var beforeSnapshot = await before.CaptureDocumentSnapshotAsync();
        var root = JsonNode.Parse(exported.Payload.AsSpan())!;
        root["document"]!["visualModel"]!["routingScopes"]![0]!["geometry"]!["policyVersion"] = "incompatible-version";
        var incompatible = Encoding.UTF8.GetBytes(root.ToJsonString());

        var rejected = await host.ImportNativeDocumentAsync(incompatible);
        Assert.Equal(NativeDocumentHostOperationStatus.Rejected, rejected.Status);
        Assert.Contains(rejected.Diagnostics, static diagnostic =>
            diagnostic.Code == "INCEPTUS.ROUTING.SAVED_STATE.INCOMPATIBLE");
        Assert.Same(before, Session(host));
        Assert.Same(beforeSnapshot, await before.CaptureDocumentSnapshotAsync());
        Assert.False(host.CaptureNativeSaveStatus()!.IsDirty);

        var recovered = await host.ImportNativeDocumentAsync(incompatible,
            new BpmnModelerNativeImportOptions(reprepareIncompatibleV2: true));
        Assert.True(recovered.Succeeded, string.Join("; ", recovered.Diagnostics.Select(static item => item.Message)));
        Assert.NotSame(before, Session(host));
        Assert.True(host.CaptureNativeSaveStatus()!.IsDirty);
        Assert.Null(host.CaptureNativeSaveStatus()!.SavedCheckpoint);
        Assert.Equal(beforeSnapshot!.DocumentId, recovered.Snapshot!.DocumentId);
        Assert.Equal(beforeSnapshot.Revision, recovered.Snapshot.Revision);
        Assert.NotEqual("incompatible-version", recovered.Snapshot.VisualModel.RoutingScopes!.Value[0].Geometry.PolicyVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRecoveryNeverAcceptsV1OrMalformedV2(bool legacy)
    {
        var replacements = 0;
        await using var host = CreateNativeTestHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            () => { replacements++; return CreateRenderer(new RecordingRenderExecution()); });
        await host.InitializeAsync("active", "standby", "container");
        var exported = await host.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded);
        var root = JsonNode.Parse(exported.Payload.AsSpan())!;
        if (legacy) root["formatVersion"] = 1;
        else root["document"]!["visualModel"]!["routingScopes"] = null;

        var rejected = await host.ImportNativeDocumentAsync(Encoding.UTF8.GetBytes(root.ToJsonString()),
            new BpmnModelerNativeImportOptions(reprepareIncompatibleV2: true));

        Assert.Equal(NativeDocumentHostOperationStatus.Rejected, rejected.Status);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code ==
            (legacy ? "NATIVE_DOCUMENT_VERSION_UNSUPPORTED" : "NATIVE_DOCUMENT_STRUCTURE_INVALID"));
        Assert.Equal(0, replacements);
    }
}
