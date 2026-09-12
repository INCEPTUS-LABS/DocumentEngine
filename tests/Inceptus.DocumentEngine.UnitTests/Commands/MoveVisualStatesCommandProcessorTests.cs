using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class MoveVisualStatesCommandProcessorTests
{
    private static readonly DocumentId DocumentId = new("test:atomic-move-processor");
    private static readonly VisualStateId AlphaVisualId = new("test:visual:alpha");
    private static readonly VisualStateId BetaVisualId = new("test:visual:beta");
    private static readonly VisualStateId GammaVisualId = new("test:visual:gamma");

    [Fact]
    public async Task BuiltInAtomicMoveCommitsOneRevisionHistoryEntryAndEvent()
    {
        var document = CreateDocument();
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var validator = new CountingValidator();
        var processor = new CommandProcessor(
            validators:
            [
                new CommandValidatorRegistration(
                    MoveVisualStatesCommand.KnownTypeId,
                    new CommandValidatorId("test:atomic-move-validator"),
                    validator),
            ],
            subscribers: [subscriber]);
        var command = Command(document.Revision);

        var result = await history.ExecuteAsync(processor, command);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, result.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), result.CommittedRevision);
        Assert.Equal(MoveVisualStatesCommand.KnownTypeId, result.CommandTypeId);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), history.CaptureStatus());
        Assert.Equal(1, validator.InvocationCount);
        var change = Assert.Single(subscriber.Events);
        Assert.Equal(MoveVisualStatesCommand.KnownTypeId, change.CommandTypeId);
        Assert.Equal(DocumentRevision.Zero, change.PreviousRevision);
        Assert.Equal(new DocumentRevision(1), change.CommittedRevision);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, change.AffectedComponents);
        Assert.Equal(
            CommandPipelineInvalidation.WithoutNodeLayout,
            change.PipelineInvalidation);
        Assert.Equal(
            [AlphaVisualId, BetaVisualId, GammaVisualId],
            Assert.IsType<NodeGeometryPipelineImpact>(change.NodeGeometryImpact)
                .ChangedVisualStateIds
                .ToArray());
        Assert.Equal(new PointD(70d, 80d), Position(document, AlphaVisualId));
        Assert.Equal(new PointD(180d, 100d), Position(document, BetaVisualId));
        Assert.Equal(new PointD(290d, 130d), Position(document, GammaVisualId));
    }

    [Fact]
    public async Task OneInvalidTargetInstallsNoStateHistoryOrEvent()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateMove(AlphaVisualId, new PointD(70d, 80d)),
                new VisualStateMove(BetaVisualId, new PointD(double.MaxValue, 100d)),
                new VisualStateMove(GammaVisualId, new PointD(290d, 130d)),
            ]);

        var result = await history.ExecuteAsync(processor, command);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), history.CaptureStatus());
        Assert.Empty(subscriber.Events);
    }

    [Fact]
    public async Task StaleAtomicMoveLeavesTheCommittedGroupAndHistoryUnchanged()
    {
        var document = CreateDocument();
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        Assert.True((await history.ExecuteAsync(processor, Command(document.Revision))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        var committed = document.CaptureSnapshot();
        var historyStatus = history.CaptureStatus();

        var stale = await history.ExecuteAsync(processor, Command(DocumentRevision.Zero));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(stale.IsCommitted);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(committed, document.CaptureSnapshot());
        Assert.Equal(historyStatus, history.CaptureStatus());
        Assert.Single(subscriber.Events);
    }

    private static MoveVisualStatesCommand Command(DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            [
                new VisualStateMove(
                    AlphaVisualId,
                    new PointD(70d, 80d),
                    VisualPlacementMode.Pinned),
                new VisualStateMove(
                    BetaVisualId,
                    new PointD(180d, 100d),
                    VisualPlacementMode.Pinned),
                new VisualStateMove(
                    GammaVisualId,
                    new PointD(290d, 130d),
                    VisualPlacementMode.Pinned),
            ]);

    private static Document CreateDocument()
    {
        var elementIds = new[]
        {
            new SemanticElementId("test:element:alpha"),
            new SemanticElementId("test:element:beta"),
            new SemanticElementId("test:element:gamma"),
        };
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elementIds.Select(id => new SemanticElementSnapshot(
                    id,
                    new SemanticTypeId("test:type")))),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        AlphaVisualId,
                        elementIds[0],
                        new PointD(10d, 20d),
                        new SizeD(50d, 30d),
                        VisualPlacementMode.Automatic),
                    new VisualStateSnapshot(
                        BetaVisualId,
                        elementIds[1],
                        new PointD(120d, 40d),
                        new SizeD(60d, 35d),
                        VisualPlacementMode.Manual),
                    new VisualStateSnapshot(
                        GammaVisualId,
                        elementIds[2],
                        new PointD(230d, 70d),
                        new SizeD(70d, 40d),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private static PointD Position(Document document, VisualStateId visualStateId) =>
        document.VisualModel.VisualStates.Single(state => state.Id == visualStateId).Position;

    private sealed class CountingValidator : ICommandValidator
    {
        public int InvocationCount { get; private set; }

        public ImmutableArray<Diagnostic> Validate(
            ICommand command,
            DocumentSnapshot document)
        {
            InvocationCount++;
            Assert.IsType<MoveVisualStatesCommand>(command);
            return [];
        }
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal List<DocumentChangedEvent> Events { get; } = [];

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Add(change);
            return ValueTask.CompletedTask;
        }
    }
}
