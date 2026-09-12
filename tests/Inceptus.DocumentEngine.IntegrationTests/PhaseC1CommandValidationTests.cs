using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseC1CommandValidationTests
{
    [Fact]
    public void PluginNeutralMoveValidationReadsOneSnapshotWithoutModifyingTheDocument()
    {
        var snapshot = Snapshot();
        var reconstruction = DocumentReconstructor.Reconstruct(snapshot);
        var document = Assert.IsType<Document>(reconstruction.Document);
        var command = new MoveVisualStateCommand(
            snapshot.DocumentId,
            snapshot.Revision,
            new VisualStateId("test:visual"),
            new PointD(50d, 75d),
            VisualPlacementMode.Pinned);
        var validator = new VisualExistsValidator();
        var creation = CommandValidationService.Create(
        [
            new CommandValidatorRegistration(
                MoveVisualStateCommand.KnownTypeId,
                new CommandValidatorId("test:visual-exists-validator"),
                validator),
        ]);
        var service = Assert.IsType<CommandValidationService>(creation.Service);
        var before = document.CaptureSnapshot();

        var valid = service.Validate(command, before);
        var missing = service.Validate(
            new MoveVisualStateCommand(
                snapshot.DocumentId,
                snapshot.Revision,
                new VisualStateId("test:missing-visual"),
                new PointD(50d, 75d)),
            before);
        var stale = service.Validate(
            new MoveVisualStateCommand(
                snapshot.DocumentId,
                snapshot.Revision.Increment(),
                new VisualStateId("test:visual"),
                new PointD(50d, 75d)),
            before);
        var repeatedStale = service.Validate(
            new MoveVisualStateCommand(
                snapshot.DocumentId,
                snapshot.Revision.Increment(),
                new VisualStateId("test:visual"),
                new PointD(50d, 75d)),
            before);

        Assert.True(reconstruction.Succeeded);
        Assert.True(creation.Succeeded);
        Assert.True(valid.IsValid);
        Assert.Empty(valid.Diagnostics);
        Assert.False(missing.IsValid);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "TEST_VISUAL_NOT_FOUND");
        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.ValidatorFailure);
        Assert.False(stale.IsValid);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(stale, repeatedStale);
        Assert.Equal(2, validator.InvocationCount);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(snapshot, document.CaptureSnapshot());
        Assert.Equal(snapshot.Revision, document.Revision);
        Assert.Empty(
            typeof(CommandValidationService).GetEvents(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    private static DocumentSnapshot Snapshot()
    {
        var documentId = new DocumentId("test:document");
        var revision = new DocumentRevision(14);
        var semantic = new SemanticModelSnapshot(
            documentId,
            revision,
            [
                new SemanticElementSnapshot(
                    new SemanticElementId("test:semantic"),
                    new SemanticTypeId("test:node-type")),
            ]);
        var visual = new VisualModelSnapshot(
            documentId,
            revision,
            [
                new VisualStateSnapshot(
                    new VisualStateId("test:visual"),
                    new SemanticElementId("test:semantic"),
                    new PointD(10d, 20d),
                    new SizeD(100d, 60d),
                    VisualPlacementMode.Manual),
            ]);
        var metadata = new DocumentMetadataSnapshot(documentId, revision);
        return new DocumentSnapshot(semantic, visual, metadata);
    }

    private sealed class VisualExistsValidator : ICommandValidator
    {
        public int InvocationCount { get; private set; }

        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
        {
            InvocationCount++;

            if (command is MoveVisualStateCommand move &&
                document.VisualModel.TryGetVisualState(move.TargetVisualStateId, out _))
            {
                return [];
            }

            var sourceIdentity = command is MoveVisualStateCommand requestedMove
                ? requestedMove.TargetVisualStateId.Value
                : command.TypeId.Value;

            return
            [
                new Diagnostic(
                    "TEST_VISUAL_NOT_FOUND",
                    DiagnosticSeverity.Error,
                    "The requested visual state does not exist.",
                    sourceIdentity),
            ];
        }
    }
}
