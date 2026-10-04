using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class PreparedRoutingCommandTests
{
    private static readonly DocumentId Id = new("test:prepared-routing");
    private static readonly VisualStateId SourceVisual = new("test:source-visual");
    private static readonly VisualStateId ConnectorVisual = new("test:connector-visual");
    private static readonly PointD Start = new(40, 20);
    private static readonly PointD End = new(160, 20);

    [Fact]
    public void TypedIntentsAndHandlerResultsCopyTheirInputsAndCompareByValue()
    {
        var points = new List<PointD> { new(80, 70) };
        var intent = ConnectorRoutingIntent.ReplaceManualDefinition(ConnectorVisual, points);
        var intents = new List<ConnectorRoutingIntent> { intent };
        var result = CommandHandlerResult.SuccessWithPreparation(Snapshot(ConnectorRoutingType.Manual), intents, []);
        points.Clear();
        intents.Clear();
        var expected = ConnectorRoutingIntent.ReplaceManualDefinition(ConnectorVisual, [new(80, 70)]);
        Assert.Equal(expected, Assert.Single(result.RoutingIntents));
        Assert.Equal(expected.GetHashCode(), intent.GetHashCode());
        Assert.True(CommandHandlerResult.NoChange().Succeeded);
        Assert.Null(CommandHandlerResult.NoChange().ProposedDocument);
        Assert.NotEqual(CommandHandlerResult.NoChange(), CommandHandlerResult.Failure());
        Assert.Throws<ArgumentException>(() => CommandExecutionResult.CreateFailure(Id,
            SetConnectorRoutingTypeCommand.KnownTypeId, CommandExecutionStatus.NoChange,
            DocumentRevision.Zero, AuthoritativeDocumentComponent.None));
    }

    [Fact]
    public void OriginalSuccessFactoryRetainsPositionalNullArgumentCompatibility()
    {
        var snapshot = Snapshot(ConnectorRoutingType.Manual);
        var result = CommandHandlerResult.Success(snapshot, null, null);
        Assert.Same(snapshot, result.ProposedDocument);
        Assert.Empty(result.RoutingIntents);
        Assert.Empty(result.SpatialHeightIntents);
    }

    [Fact]
    public async Task SameTypeAndIdenticalManualDefinitionAreSuccessfulWithoutPreparationOrHistory()
    {
        var document = CreateDocument(ConnectorRoutingType.Manual);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        var before = document.CaptureSnapshot();

        var sameType = await history.ExecuteAsync(processor, SetType(document, ConnectorRoutingType.Manual));
        var samePoints = await history.ExecuteAsync(processor, Route(document, [new(80, 70)]));

        Assert.Equal(HistoryOperationStatus.NoChange, sameType.Status);
        Assert.True(sameType.Succeeded);
        Assert.Equal(HistoryOperationStatus.NoChange, samePoints.Status);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Empty(preparer.Requests);
        Assert.Equal(0, history.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task PreparedDocumentRejectsMutationWhenNoPreparerWasConfigured()
    {
        var document = CreateDocument(ConnectorRoutingType.Manual);
        var before = document.CaptureSnapshot();
        var result = await new CommandProcessor().ExecuteAsync(document, Move(document, 10));
        Assert.Equal(CommandExecutionStatus.ProposedStateValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "INCEPTUS.ROUTING.PREPARER.MISSING");
        Assert.Same(before, document.CaptureSnapshot());
    }

    [Fact]
    public async Task ManualPointEditPreservesBothHistoryBranchesAndSurvivesOrdinaryReplay()
    {
        var document = CreateDocument(ConnectorRoutingType.Manual);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, Move(document, 10))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Move(document, 20))).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        var branches = history.CaptureStatus();
        Assert.True(branches.CanUndo && branches.CanRedo);
        var newPoint = new PointD(95, 90);

        Assert.True((await history.ExecuteAsync(processor, Route(document, [newPoint]))).IsCommitted);

        Assert.Equal(branches, history.CaptureStatus());
        Assert.Equal(newPoint, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(newPoint, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(newPoint, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
    }

    [Fact]
    public async Task TypeHistoryReplaysOnlyModeAndRetainsLaterManualDefinition()
    {
        var document = CreateDocument(ConnectorRoutingType.Straight);
        var processor = Processor(new RecordingPreparer());
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, SetType(document, ConnectorRoutingType.Manual))).IsCommitted);
        var point = new PointD(88, 66);
        Assert.True((await history.ExecuteAsync(processor, Route(document, [point]))).IsCommitted);
        Assert.Equal(1, history.CaptureStatus().EntryCount);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(ConnectorRoutingType.Straight, CurrentRoute(document).RoutingType);
        Assert.Equal(point, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(ConnectorRoutingType.Manual, CurrentRoute(document).RoutingType);
        Assert.Equal(point, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
    }

    [Fact]
    public async Task AlreadySatisfiedOwnedReplayConsumesOnlyItsPreparedCursor()
    {
        var document = CreateDocument(ConnectorRoutingType.Straight);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, SetType(document, ConnectorRoutingType.Manual))).IsCommitted);
        Assert.True((await processor.ExecuteAsync(document, SetType(document, ConnectorRoutingType.Straight))).IsCommitted);
        var before = document.CaptureSnapshot();
        var calls = preparer.Requests.Count;

        var replay = await history.UndoAsync(processor);

        Assert.Equal(HistoryOperationStatus.NoChange, replay.Status);
        Assert.True(replay.Succeeded);
        Assert.False(replay.HistoryStatus.CanUndo);
        Assert.True(replay.HistoryStatus.CanRedo);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(calls, preparer.Requests.Count);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationFailureOrCancellationLeavesDocumentAndBothBranchesUntouched(bool cancel)
    {
        var document = CreateDocument(ConnectorRoutingType.Manual);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, Move(document, 10))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Move(document, 20))).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        var before = document.CaptureSnapshot();
        var branches = history.CaptureStatus();
        using var cancellation = new CancellationTokenSource();
        preparer.BeforeReturn = () =>
        {
            Assert.Same(before, document.CaptureSnapshot());
            Assert.Equal(branches, history.CaptureStatus());
            if (cancel) cancellation.Cancel();
        };
        preparer.Fail = !cancel;

        var result = await history.ExecuteAsync(processor, Route(document, [new(100, 90)]), cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(branches, history.CaptureStatus());
    }

    [Fact]
    public async Task CancellationBeforeNoChangeReplayCursorInstallLeavesHistoryUntouched()
    {
        var document = CreateDocument(ConnectorRoutingType.Straight);
        var processor = Processor(new RecordingPreparer());
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, SetType(document, ConnectorRoutingType.Manual))).IsCommitted);
        Assert.True((await processor.ExecuteAsync(document, SetType(document, ConnectorRoutingType.Straight))).IsCommitted);
        var before = document.CaptureSnapshot();
        var branches = history.CaptureStatus();
        using var cancellation = new CancellationTokenSource();
        var cancelling = new CommandProcessor(null, null, null, checkpoint =>
        {
            if (checkpoint == CommandExecutionCheckpoint.BeforeFinalCancellationCheck) cancellation.Cancel();
        });

        var result = await history.UndoAsync(cancelling, cancellation.Token);

        Assert.Equal(HistoryOperationStatus.Cancelled, result.Status);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(branches, history.CaptureStatus());
    }

    [Fact]
    public async Task CompoundKeepsOrderedModeIntentsAndPreparesAndInstallsExactlyOnce()
    {
        var document = CreateDocument(ConnectorRoutingType.Straight);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        var command = new CompoundDocumentCommand(Id, document.Revision,
            [SetType(document, ConnectorRoutingType.Manual), SetType(document, ConnectorRoutingType.Straight)]);

        var result = await history.ExecuteAsync(processor, command);

        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message))); // First Manual capture persists even though the final mode is unchanged.
        Assert.Equal(new DocumentRevision(1), document.Revision);
        var request = Assert.Single(preparer.Requests);
        Assert.Equal(new[] { ConnectorRoutingType.Manual, ConnectorRoutingType.Straight },
            request.RoutingIntents.Select(intent => intent.RoutingType!.Value));
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.NotNull(CurrentRoute(document).ManualDefinition);
    }

    [Fact]
    public async Task CompoundReturningToSameCompleteStateIsNoChangeAfterOnePreparation()
    {
        var document = CreateDocument(ConnectorRoutingType.Manual);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, Move(document, 10))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Move(document, 20))).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        var branches = history.CaptureStatus();
        var before = document.CaptureSnapshot();
        var calls = preparer.Requests.Count;
        var compound = new CompoundDocumentCommand(Id, document.Revision,
            [SetType(document, ConnectorRoutingType.Straight), SetType(document, ConnectorRoutingType.Manual)]);

        var result = await history.ExecuteAsync(processor, compound);

        Assert.Equal(HistoryOperationStatus.NoChange, result.Status);
        Assert.Equal(calls + 1, preparer.Requests.Count);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(branches, history.CaptureStatus());
    }

    [Fact]
    public async Task MixedCompoundRestorationKeepsCurrentManualDefinitionAndReplaysItsOwnedMode()
    {
        var document = CreateDocument(ConnectorRoutingType.Straight);
        var preparer = new RecordingPreparer();
        var processor = Processor(preparer);
        var history = new HistoryManager(document);
        var compound = new CompoundDocumentCommand(Id, document.Revision,
            [Move(document, 15), SetType(document, ConnectorRoutingType.Manual)]);
        var compoundResult = await history.ExecuteAsync(processor, compound);
        Assert.True(compoundResult.IsCommitted, string.Join("; ", compoundResult.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        Assert.Single(preparer.Requests);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        var point = new PointD(90, 77);
        Assert.True((await history.ExecuteAsync(processor, Route(document, [point]))).IsCommitted);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(ConnectorRoutingType.Straight, CurrentRoute(document).RoutingType);
        Assert.Equal(point, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
        Assert.Equal(new PointD(0, 0), document.CaptureSnapshot().VisualModel.VisualStates.Single(v => v.Id == SourceVisual).Position);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(ConnectorRoutingType.Manual, CurrentRoute(document).RoutingType);
        Assert.Equal(point, Assert.Single(CurrentRoute(document).ManualDefinition!.Value));
    }

    [Fact]
    public async Task PreparerCannotChangeAuthoredModeWithoutAnIntent()
    {
        var document = CreateDocument(ConnectorRoutingType.Straight);
        var preparer = new RecordingPreparer { ForceManual = true };
        var before = document.CaptureSnapshot();
        var result = await Processor(preparer).ExecuteAsync(document, Move(document, 10));
        Assert.Equal(CommandExecutionStatus.ProposedStateValidationFailed, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "INCEPTUS.ROUTING.TYPE.AUTHORITY");
        Assert.Same(before, document.CaptureSnapshot());
    }

    private static CommandProcessor Processor(RecordingPreparer preparer) => new(null, null, null, null, null, preparer);
    private static SetConnectorRoutingTypeCommand SetType(Document document, ConnectorRoutingType type) =>
        new(Id, document.Revision, ConnectorVisual, type);
    private static MoveVisualStateCommand Move(Document document, double x) =>
        new(Id, document.Revision, SourceVisual, new PointD(x, 0), VisualPlacementMode.Manual);
    private static UpdateConnectionRouteCommand Route(Document document, IEnumerable<PointD> points) =>
        new(Id, document.Revision, ConnectorVisual, new[] { Start }.Concat(points).Append(End));
    private static ConnectorRoutingRecord CurrentRoute(Document document) =>
        Assert.Single(Assert.Single(document.CaptureSnapshot().VisualModel.RoutingScopes!.Value).Connectors);
    private static Document CreateDocument(ConnectorRoutingType type) =>
        Assert.IsType<Document>(DocumentFactory.Create(Snapshot(type)).Document);

    private static DocumentSnapshot Snapshot(ConnectorRoutingType type)
    {
        var source = new SemanticElementId("test:source");
        var target = new SemanticElementId("test:target");
        var connector = new SemanticElementId("test:connector");
        var semantic = new SemanticModelSnapshot(Id, DocumentRevision.Zero,
            [new(source, new SemanticTypeId("test:node")), new(target, new SemanticTypeId("test:node"))],
            [new(connector, new SemanticTypeId("test:connection"), source, target)]);
        VisualStateSnapshot[] visuals = [
            new(SourceVisual, source, new PointD(0, 0), new SizeD(40, 40), VisualPlacementMode.Manual),
            new(new VisualStateId("test:target-visual"), target, new PointD(160, 0), new SizeD(40, 40), VisualPlacementMode.Manual),
            new(ConnectorVisual, connector, new PointD(0, 0), new SizeD(1, 1), VisualPlacementMode.Manual)];
        PointD[]? definition = type == ConnectorRoutingType.Manual ? [new(80, 70)] : null;
        var path = definition is null ? new[] { Start, End } : new[] { Start }.Concat(definition).Append(End);
        var record = new ConnectorRoutingRecord(ConnectorVisual, type, ConnectorRoutingOutcome.Path, path, definition);
        var geometry = Geometry(semantic, visuals);
        return new(semantic, new VisualModelSnapshot(Id, DocumentRevision.Zero, visuals, null,
            [new ScopeRoutingSnapshot(semantic.RootScopeId, geometry, [record])]),
            new DocumentMetadataSnapshot(Id, DocumentRevision.Zero));
    }

    private static ScopeGeometrySnapshot Geometry(SemanticModelSnapshot semantic, IEnumerable<VisualStateSnapshot> visuals) =>
        new("test:routing", "1", new AlgorithmId("test:layout"), Canvas2DSceneConfiguration.Default,
            new TextMeasurementRequest("", "Arial", "test:font", "1", 12, 14, 400, TextFontStyle.Normal,
                "en", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom, 1, "test:text", "1"), [],
            visuals.Where(v => semantic.TryGetElement(v.SemanticElementId, out _)).Select(v =>
                new ScopeNodeGeometrySnapshot(v.Id, new RectD(0, 0, v.Size.Width, v.Size.Height),
                    Matrix2D.CreateTranslation(v.Position.X, v.Position.Y), null)), [], []);

    // This fake isolates the processor transaction and History contract from the production router.
    private sealed class RecordingPreparer : IConnectorRoutingStatePreparer
    {
        internal List<ConnectorRoutingStatePreparationRequest> Requests { get; } = [];
        internal bool Fail { get; set; }
        internal bool ForceManual { get; init; }
        internal Action? BeforeReturn { get; set; }
        public ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(
            ConnectorRoutingStatePreparationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Assert.Equal(request.Before.Revision.Increment(), request.ProposedDocument.Revision);
            var current = request.Before.VisualModel.RoutingScopes!.Value.Single().Connectors.Single();
            var mode = current.RoutingType;
            var definition = current.ManualDefinition;
            foreach (var intent in request.RoutingIntents)
            {
                if (intent.Kind == ConnectorRoutingIntentKind.SetType)
                {
                    mode = intent.RoutingType!.Value;
                    if (mode == ConnectorRoutingType.Manual && definition is null)
                        definition = current.Path.Skip(1).Take(current.Path.Length - 2).ToImmutableArray();
                }
                else if (intent.Kind == ConnectorRoutingIntentKind.ReplaceManualDefinition)
                    definition = intent.ManualDefinition;
                else if (intent.Kind == ConnectorRoutingIntentKind.Recalculate)
                    definition = [];
            }
            if (ForceManual) { mode = ConnectorRoutingType.Manual; definition ??= []; }
            var path = mode == ConnectorRoutingType.Straight ? [Start, End] :
                new[] { Start }.Concat(definition!.Value).Append(End).ToArray();
            var record = new ConnectorRoutingRecord(ConnectorVisual, mode, ConnectorRoutingOutcome.Path, path, definition);
            var geometry = Geometry(request.ProposedDocument.SemanticModel, request.ProposedDocument.VisualModel.VisualStates);
            BeforeReturn?.Invoke();
            return ValueTask.FromResult(Fail
                ? ConnectorRoutingStatePreparationResult.Failure([new Diagnostic("test:preparation-failed", DiagnosticSeverity.Error, "Injected failure.")])
                : ConnectorRoutingStatePreparationResult.Success([
                    new ScopeRoutingSnapshot(request.ProposedDocument.SemanticModel.RootScopeId, geometry, [record])]));
        }
    }
}
