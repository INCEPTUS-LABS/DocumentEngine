using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class BpmnModelerDocumentNotificationSourceTests
{
    private static readonly DocumentId TestDocumentId = new("test:modeler-notifications");

    [Fact]
    public async Task InitialSnapshotEstablishesReceiptBaselineWithoutAChangeNotification()
    {
        var initial = CreateSnapshot(6);
        using var source = new BpmnModelerDocumentNotificationSource(initial);

        await source.WaitForReceiptAsync(initial.Revision);

        Assert.Same(initial, source.InitialSnapshot);
        Assert.Equal(initial.Revision, source.LastReceivedRevision);
        Assert.Empty(source.TakePendingThrough(initial.Revision));
    }

    [Fact]
    public async Task PendingSnapshotsRetainExactCommitInstancesInRevisionOrder()
    {
        using var source = new BpmnModelerDocumentNotificationSource(CreateSnapshot(6));
        var first = CreateSnapshot(7);
        var second = CreateSnapshot(8);
        var third = CreateSnapshot(9);
        await source.OnDocumentChangedAsync(CreateChange(first));
        await source.OnDocumentChangedAsync(CreateChange(second));
        await source.OnDocumentChangedAsync(CreateChange(third));

        Assert.Empty(source.TakePendingThrough(new DocumentRevision(6)));
        var accepted = source.TakePendingThrough(new DocumentRevision(8));
        Assert.Collection(accepted,
            snapshot => Assert.Same(first, snapshot),
            snapshot => Assert.Same(second, snapshot));
        Assert.Same(third, Assert.Single(source.TakePendingThrough(new DocumentRevision(9))));
        Assert.Empty(source.TakePendingThrough(new DocumentRevision(9)));
        Assert.Equal(new DocumentRevision(9), source.LastReceivedRevision);
        Assert.Equal(new DocumentRevision(7), accepted[0].Revision);
    }

    [Fact]
    public async Task DuplicateAndStaleReceiptsDoNotReenterThePendingStream()
    {
        var wakeCount = 0;
        using var source = new BpmnModelerDocumentNotificationSource(
            CreateSnapshot(6),
            () => wakeCount++);
        var current = CreateSnapshot(7);

        await source.OnDocumentChangedAsync(CreateChange(CreateSnapshot(6)));
        await source.OnDocumentChangedAsync(CreateChange(current));
        await source.OnDocumentChangedAsync(CreateChange(current));
        Assert.Same(current, Assert.Single(source.TakePendingThrough(current.Revision)));
        await source.OnDocumentChangedAsync(CreateChange(CreateSnapshot(6)));
        await source.OnDocumentChangedAsync(CreateChange(CreateSnapshot(7)));

        Assert.Empty(source.TakePendingThrough(current.Revision));
        Assert.Equal(1, wakeCount);
        Assert.Equal(current.Revision, source.LastReceivedRevision);
    }

    [Fact]
    public async Task ForeignDocumentReceiptCannotAdvanceOrPolluteTheSource()
    {
        using var source = new BpmnModelerDocumentNotificationSource(CreateSnapshot(6));
        var foreign = CreateSnapshot(7, new DocumentId("test:another-modeler"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.OnDocumentChangedAsync(CreateChange(foreign)));

        Assert.Equal("change", exception.ParamName);
        Assert.Equal(new DocumentRevision(6), source.LastReceivedRevision);
        Assert.Empty(source.TakePendingThrough(foreign.Revision));
    }

    [Fact]
    public async Task ReceiptBarrierWaitsForItsRequestedRevisionWithoutDrainingSnapshots()
    {
        using var source = new BpmnModelerDocumentNotificationSource(CreateSnapshot(6));
        var firstReceipt = source.WaitForReceiptAsync(new DocumentRevision(7)).AsTask();
        var secondReceipt = source.WaitForReceiptAsync(new DocumentRevision(8)).AsTask();
        Assert.False(firstReceipt.IsCompleted);
        Assert.False(secondReceipt.IsCompleted);

        var first = CreateSnapshot(7);
        await source.OnDocumentChangedAsync(CreateChange(first));
        await firstReceipt.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(secondReceipt.IsCompleted);
        var second = CreateSnapshot(8);
        await source.OnDocumentChangedAsync(CreateChange(second));
        await secondReceipt.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Collection(source.TakePendingThrough(second.Revision),
            snapshot => Assert.Same(first, snapshot),
            snapshot => Assert.Same(second, snapshot));
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelAnotherReceiptWaiter()
    {
        using var source = new BpmnModelerDocumentNotificationSource(CreateSnapshot(6));
        using var cancellation = new CancellationTokenSource();
        var cancelled = source.WaitForReceiptAsync(new DocumentRevision(7), cancellation.Token)
            .AsTask();
        var surviving = source.WaitForReceiptAsync(new DocumentRevision(7)).AsTask();

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(surviving.IsCompleted);
        await source.OnDocumentChangedAsync(CreateChange(CreateSnapshot(7)));
        await surviving.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(source.TakePendingThrough(new DocumentRevision(7)));
    }

    [Fact]
    public async Task DisposalCancelsReceiptWaitersClearsPendingAndSuppressesLateInput()
    {
        var wakeCount = 0;
        using var source = new BpmnModelerDocumentNotificationSource(
            CreateSnapshot(6),
            () => wakeCount++);
        await source.OnDocumentChangedAsync(CreateChange(CreateSnapshot(7)));
        var pendingReceipt = source.WaitForReceiptAsync(new DocumentRevision(8)).AsTask();

        source.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingReceipt);
        await source.OnDocumentChangedAsync(CreateChange(CreateSnapshot(8)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.WaitForReceiptAsync(new DocumentRevision(7)));
        source.Dispose();

        Assert.Equal(1, wakeCount);
        Assert.Equal(new DocumentRevision(7), source.LastReceivedRevision);
        Assert.Empty(source.TakePendingThrough(new DocumentRevision(8)));
    }

    [Fact]
    public async Task WakeFailureCannotEraseAnAlreadyReceivedCommit()
    {
        using var source = new BpmnModelerDocumentNotificationSource(
            CreateSnapshot(6),
            () => throw new InvalidOperationException("Host wake failed."));
        var snapshot = CreateSnapshot(7);
        var receipt = source.WaitForReceiptAsync(snapshot.Revision).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source.OnDocumentChangedAsync(CreateChange(snapshot)));
        await receipt.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(snapshot, Assert.Single(source.TakePendingThrough(snapshot.Revision)));
        Assert.Equal(snapshot.Revision, source.LastReceivedRevision);
    }

    private static DocumentSnapshot CreateSnapshot(ulong revision, DocumentId? documentId = null)
    {
        documentId ??= TestDocumentId;
        var version = new DocumentRevision(revision);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, version),
            new VisualModelSnapshot(documentId, version),
            new DocumentMetadataSnapshot(documentId, version));
    }

    private static DocumentChangedEvent CreateChange(DocumentSnapshot snapshot) =>
        new(
            snapshot.DocumentId,
            new DocumentRevision(snapshot.Revision.Value - 1),
            snapshot.Revision,
            AuthoritativeDocumentComponent.Metadata,
            new CommandTypeId("test:modeler-notification-change"),
            snapshot,
            PipelineInvalidation.None);
}
