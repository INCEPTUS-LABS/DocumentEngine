using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class CommandExecutionContractTests
{
    private static readonly DocumentId TestDocumentId = new("test:command-execution-contracts");
    private static readonly CommandTypeId TestCommandTypeId = new("test:command/move");

    [Fact]
    public void HandlerSuccessPreservesTheImmutableProposalAndOrdersDiagnostics()
    {
        var snapshot = Snapshot(DocumentRevision.Zero);
        var diagnostics = new List<Diagnostic>
        {
            Diagnostic("Z_TEST", "z"),
            Diagnostic("A_TEST", "a"),
        };

        var result = CommandHandlerResult.Success(snapshot, diagnostics);
        diagnostics.Clear();

        Assert.True(result.Succeeded);
        Assert.Same(snapshot, result.ProposedDocument);
        Assert.Null(result.PipelineInvalidation);
        Assert.Null(result.NodeGeometryImpact);
        Assert.Equal(["A_TEST", "Z_TEST"], result.Diagnostics.Select(item => item.Code));
        Assert.True(((IList<Diagnostic>)result.Diagnostics).IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)result.Diagnostics).Clear());
    }

    [Fact]
    public void HandlerSuccessCanDeclareValidatedPipelineInvalidation()
    {
        var result = CommandHandlerResult.Success(
            Snapshot(DocumentRevision.Zero),
            pipelineInvalidation: CommandPipelineInvalidation.ConnectorOnly);

        Assert.Equal(
            CommandPipelineInvalidation.ConnectorOnly,
            result.PipelineInvalidation);
        Assert.Equal(
            PipelineInvalidation.None,
            CommandHandlerResult.Success(
                Snapshot(DocumentRevision.Zero),
                pipelineInvalidation: PipelineInvalidation.None).PipelineInvalidation);
    }

    [Fact]
    public void HandlerSuccessCanDeclareExplicitNodeGeometryWithoutContradictingLayout()
    {
        var visualStateId = new VisualStateId("test:changed-node");
        var impact = NodeGeometryPipelineImpact.ForChangedVisualStates([visualStateId]);
        var result = CommandHandlerResult.Success(
            Snapshot(DocumentRevision.Zero),
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: impact);

        Assert.Same(impact, result.NodeGeometryImpact);
        Assert.Throws<ArgumentException>(() => CommandHandlerResult.Success(
            Snapshot(DocumentRevision.Zero),
            nodeGeometryImpact: impact));
        Assert.Throws<ArgumentException>(() => CommandHandlerResult.Success(
            Snapshot(DocumentRevision.Zero),
            pipelineInvalidation: CommandPipelineInvalidation.Full,
            nodeGeometryImpact: impact));
    }

    [Fact]
    public void HandlerFailureHasNoProposalAndHasStructuralValueSemantics()
    {
        var first = CommandHandlerResult.Failure([Diagnostic("TEST_FAILURE", "failed")]);
        var second = CommandHandlerResult.Failure([Diagnostic("TEST_FAILURE", "failed")]);

        Assert.False(first.Succeeded);
        Assert.Null(first.ProposedDocument);
        Assert.Null(first.PipelineInvalidation);
        Assert.Null(first.NodeGeometryImpact);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void DocumentChangedEventIdentifiesItsExactCommittedSnapshot()
    {
        var previous = new DocumentRevision(8);
        var committed = previous.Increment();
        var snapshot = Snapshot(committed);

        var changed = new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            snapshot);

        Assert.Equal(TestDocumentId, changed.DocumentId);
        Assert.Equal(previous, changed.PreviousRevision);
        Assert.Equal(committed, changed.CommittedRevision);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, changed.AffectedComponents);
        Assert.Equal(TestCommandTypeId, changed.CommandTypeId);
        Assert.Same(snapshot, changed.CommittedSnapshot);
        Assert.Equal(changed.CommittedRevision, changed.CommittedSnapshot.Revision);
        Assert.Equal(CommandPipelineInvalidation.Full, changed.PipelineInvalidation);
        Assert.Null(changed.NodeGeometryImpact);
    }

    [Fact]
    public void DocumentChangedEventNormalizesPreservedAndExplicitNodeGeometryImpact()
    {
        var previous = new DocumentRevision(8);
        var committed = previous.Increment();
        var snapshot = Snapshot(committed);
        var preserved = new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            snapshot,
            CommandPipelineInvalidation.ConnectorOnly);
        var explicitImpact = NodeGeometryPipelineImpact.ForChangedVisualStates(
            [new VisualStateId("test:changed-node")]);
        var changed = new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            snapshot,
            CommandPipelineInvalidation.WithoutNodeLayout,
            explicitImpact);

        Assert.Same(NodeGeometryPipelineImpact.PreserveAll, preserved.NodeGeometryImpact);
        Assert.Same(explicitImpact, changed.NodeGeometryImpact);
        Assert.Throws<ArgumentException>(() => new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            snapshot,
            CommandPipelineInvalidation.Full,
            explicitImpact));
    }

    [Fact]
    public void DocumentChangedEventRejectsIncoherentCommitIdentityAndRevision()
    {
        var previous = new DocumentRevision(8);
        var committed = previous.Increment();

        Assert.Throws<ArgumentException>(() => new DocumentChangedEvent(
            TestDocumentId,
            previous,
            new DocumentRevision(10),
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            Snapshot(new DocumentRevision(10))));
        Assert.Throws<ArgumentException>(() => new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            Snapshot(committed, new DocumentId("test:other-document"))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.None,
            TestCommandTypeId,
            Snapshot(committed)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            Snapshot(committed),
            (PipelineInvalidation)(1 << 12)));
    }

    [Fact]
    public void CommittedExecutionResultCarriesTheStableEventPayload()
    {
        var previous = new DocumentRevision(12);
        var committed = previous.Increment();
        var changed = new DocumentChangedEvent(
            TestDocumentId,
            previous,
            committed,
            AuthoritativeDocumentComponent.VisualModel,
            TestCommandTypeId,
            Snapshot(committed));

        var result = CommandExecutionResult.CreateCommitted(changed);

        Assert.True(result.IsCommitted);
        Assert.Equal(CommandExecutionStatus.Committed, result.Status);
        Assert.Equal(TestDocumentId, result.DocumentId);
        Assert.Equal(TestCommandTypeId, result.CommandTypeId);
        Assert.Equal(previous, result.PreviousRevision);
        Assert.Equal(committed, result.CommittedRevision);
        Assert.Same(changed, result.CommittedEvent);
        Assert.Same(changed.CommittedSnapshot, result.CommittedSnapshot);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FailedExecutionResultCannotExposeCommittedState()
    {
        var result = CommandExecutionResult.CreateFailure(
            TestDocumentId,
            TestCommandTypeId,
            CommandExecutionStatus.HandlerFailed,
            new DocumentRevision(3),
            AuthoritativeDocumentComponent.VisualModel,
            [Diagnostic("TEST_HANDLER", "failed")]);

        Assert.False(result.IsCommitted);
        Assert.Equal(CommandExecutionStatus.HandlerFailed, result.Status);
        Assert.Null(result.CommittedRevision);
        Assert.Null(result.CommittedEvent);
        Assert.Null(result.CommittedSnapshot);
        Assert.Equal("TEST_HANDLER", Assert.Single(result.Diagnostics).Code);
        Assert.Throws<ArgumentException>(() => CommandExecutionResult.CreateFailure(
            TestDocumentId,
            TestCommandTypeId,
            CommandExecutionStatus.Committed,
            DocumentRevision.Zero,
            AuthoritativeDocumentComponent.VisualModel));
    }

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        DocumentId? documentId = null)
    {
        var id = documentId ?? TestDocumentId;
        return new DocumentSnapshot(
            new SemanticModelSnapshot(id, revision),
            new VisualModelSnapshot(id, revision),
            new DocumentMetadataSnapshot(id, revision));
    }

    private static Diagnostic Diagnostic(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
