using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.UnitTests.Commands;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class UpdateSemanticElementPropertyHistoryTests
{
    [Fact]
    public void PolicyBuildsExactTypedInverseAndRedoValues()
    {
        var before = UpdateSemanticElementPropertyCommandProcessorTests.Snapshot(
            DocumentRevision.Zero,
            "Initial description",
            10);
        var committed = UpdateSemanticElementPropertyCommandProcessorTests.Snapshot(
            new DocumentRevision(1),
            "First line\nSecond line",
            10);
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator(
            [UpdateSemanticElementPropertyHistoryPolicy.Registration]);
        var command = UpdateSemanticElementPropertyCommandProcessorTests.Update(
            DocumentRevision.Zero,
            UpdateSemanticElementPropertyCommandProcessorTests.DescriptionKey,
            PropertyValue.FromText("First line\nSecond line"));

        Install(store, coordinator.PrepareRecord(store, command, before, committed));

        var undo = store.PrepareUndo(before.DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<UpdateSemanticElementPropertyCommand>(
            undo.Mutation!.RestorationCommand);
        Assert.Equal("Initial description", inverse.TargetValue.TextValue);
        Assert.Equal(
            UpdateSemanticElementPropertyCommandProcessorTests.DescriptionKey,
            inverse.PropertyKey);
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(before.DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<UpdateSemanticElementPropertyCommand>(
            redo.Mutation!.RestorationCommand);
        Assert.Equal("First line\nSecond line", replay.TargetValue.TextValue);
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public async Task BuiltInHistoryUndoesMixedTypedEditsInReverseAndRedoesInForwardOrder()
    {
        var document = UpdateSemanticElementPropertyCommandProcessorTests.CreateDocument();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        const string changedDescription = "First line\r\nSecond line\nThird line";

        var descriptionUpdate = await history.ExecuteAsync(
            processor,
            UpdateSemanticElementPropertyCommandProcessorTests.Update(
                document.Revision,
                UpdateSemanticElementPropertyCommandProcessorTests.DescriptionKey,
                PropertyValue.FromText(changedDescription)));
        var numberUpdate = await history.ExecuteAsync(
            processor,
            UpdateSemanticElementPropertyCommandProcessorTests.Update(
                document.Revision,
                UpdateSemanticElementPropertyCommandProcessorTests.NumberKey,
                PropertyValue.FromInteger(25)));

        Assert.True(descriptionUpdate.IsCommitted);
        Assert.True(numberUpdate.IsCommitted);
        Assert.Equal(new HistoryStatus(2, true, false), history.CaptureStatus());
        AssertState(changedDescription, 25);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        AssertState(changedDescription, 10);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        AssertState("Initial description", 10);
        Assert.Equal(new HistoryStatus(2, false, true), history.CaptureStatus());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        AssertState(changedDescription, 10);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        AssertState(changedDescription, 25);
        Assert.Equal(new HistoryStatus(2, true, false), history.CaptureStatus());
        Assert.Equal(new DocumentRevision(6), document.Revision);

        void AssertState(string description, long number)
        {
            var snapshot = document.CaptureSnapshot();
            Assert.Equal(
                description,
                UpdateSemanticElementPropertyCommandProcessorTests.Value(
                    snapshot,
                    UpdateSemanticElementPropertyCommandProcessorTests.DescriptionKey)
                    .TextValue);
            var numberValue = UpdateSemanticElementPropertyCommandProcessorTests.Value(
                snapshot,
                UpdateSemanticElementPropertyCommandProcessorTests.NumberKey);
            Assert.Equal(PropertyValueKind.Integer, numberValue.Kind);
            Assert.Equal(number, numberValue.IntegerValue);
        }
    }

    [Fact]
    public async Task RelationshipNameAndDescriptionRoundTripWithoutChangingEndpointsOrVisuals()
    {
        var documentId = new DocumentId("test:relationship-property-history");
        var sourceId = new SemanticElementId("test:source");
        var targetId = new SemanticElementId("test:target");
        var relationshipId = new SemanticElementId("test:relationship");
        var visualId = new VisualStateId("test:relationship-visual");
        const string nameKey = "test:name";
        const string descriptionKey = "test:description";
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(sourceId, new SemanticTypeId("test:node")),
                    new SemanticElementSnapshot(targetId, new SemanticTypeId("test:node")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        relationshipId,
                        new SemanticTypeId("test:connector"),
                        sourceId,
                        targetId,
                        [
                            new(nameKey, PropertyValue.FromText(string.Empty)),
                            new(descriptionKey, PropertyValue.FromText("Initial")),
                        ]),
                ]),
            new VisualModelSnapshot(
                documentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        visualId,
                        relationshipId,
                        default,
                        default,
                        VisualPlacementMode.Manual,
                        [new PointD(0d, 0d), new PointD(10d, 10d)]),
                ]),
            new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero));
        var document = Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        Assert.True((await Execute(nameKey, "Approved")).IsCommitted);
        Assert.True((await Execute(descriptionKey, "First line\nSecond line")).IsCommitted);
        AssertState("Approved", "First line\nSecond line");

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        AssertState("Approved", "Initial");
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        AssertState(string.Empty, "Initial");
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        AssertState("Approved", "Initial");
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        AssertState("Approved", "First line\nSecond line");

        ValueTask<HistoryOperationResult> Execute(string key, string value) =>
            history.ExecuteAsync(
                processor,
                new UpdateSemanticElementPropertyCommand(
                    documentId,
                    document.Revision,
                    relationshipId,
                    key,
                    PropertyValue.FromText(value)));

        void AssertState(string name, string description)
        {
            var current = document.CaptureSnapshot();
            Assert.True(current.SemanticModel.TryGetRelationship(
                relationshipId,
                out var relationship));
            Assert.Equal(name, relationship!.Properties[nameKey].TextValue);
            Assert.Equal(description, relationship.Properties[descriptionKey].TextValue);
            Assert.Equal(sourceId, relationship.SourceId);
            Assert.Equal(targetId, relationship.TargetId);
            Assert.Equal(
                snapshot.VisualModel.VisualStates[0].Route.AsEnumerable(),
                current.VisualModel.VisualStates[0].Route.AsEnumerable());
        }
    }

    [Fact]
    public void PolicyRejectsMissingOrMismatchedCommittedState()
    {
        var before = UpdateSemanticElementPropertyCommandProcessorTests.Snapshot(
            DocumentRevision.Zero,
            "Initial description",
            10);
        var committed = UpdateSemanticElementPropertyCommandProcessorTests.Snapshot(
            new DocumentRevision(1),
            "Unexpected value",
            10);
        var command = UpdateSemanticElementPropertyCommandProcessorTests.Update(
            DocumentRevision.Zero,
            UpdateSemanticElementPropertyCommandProcessorTests.DescriptionKey,
            PropertyValue.FromText("Requested value"));

        var mismatched = new UpdateSemanticElementPropertyHistoryPolicy().Prepare(
            command,
            before,
            committed);
        var missing = new UpdateSemanticElementPropertyHistoryPolicy().Prepare(
            new UpdateSemanticElementPropertyCommand(
                before.DocumentId,
                DocumentRevision.Zero,
                new SemanticElementId("test:missing"),
                UpdateSemanticElementPropertyCommandProcessorTests.DescriptionKey,
                PropertyValue.FromText("Requested value")),
            before,
            committed);

        Assert.False(mismatched.Succeeded);
        Assert.Contains(mismatched.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }
}
