using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class PipelineInvalidationTests
{
    [Fact]
    public void CommandWithoutDeclarationConservativelyInvalidatesFullPipeline()
    {
        var invalidation = CommandPipelineInvalidation.Resolve(new UndeclaredCommand());

        Assert.Equal(CommandPipelineInvalidation.Full, invalidation);
        Assert.True(invalidation.HasFlag(PipelineInvalidation.NodeLayout));
    }

    [Fact]
    public void DeclaredConnectorImpactExcludesOnlyNodeLayout()
    {
        var invalidation = CommandPipelineInvalidation.Resolve(
            new DeclaredCommand(CommandPipelineInvalidation.ConnectorOnly));

        Assert.Equal(
            PipelineInvalidation.Projection |
            PipelineInvalidation.Routing |
            PipelineInvalidation.Scene,
            invalidation);
        Assert.False(invalidation.HasFlag(PipelineInvalidation.NodeLayout));
    }

    [Fact]
    public void EmptyDeclarationRepresentsNoDerivedProcessingInvalidation()
    {
        var invalidation = CommandPipelineInvalidation.Resolve(
            new DeclaredCommand(PipelineInvalidation.None));

        Assert.Equal(PipelineInvalidation.None, invalidation);
        Assert.Same(
            NodeGeometryPipelineImpact.PreserveAll,
            CommandPipelineInvalidation.ResolveNodeGeometryImpact(invalidation));
    }

    [Fact]
    public void UnknownDeclarationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommandPipelineInvalidation.Resolve(
                new DeclaredCommand((PipelineInvalidation)(1 << 12))));
    }

    [Fact]
    public void NodeGeometryImpactIsCanonicalImmutableAndRejectsInvalidShapes()
    {
        var alpha = new VisualStateId("test:visual:alpha");
        var beta = new VisualStateId("test:visual:beta");
        var first = NodeGeometryPipelineImpact.ForChangedVisualStates([beta, alpha]);
        var second = NodeGeometryPipelineImpact.ForChangedVisualStates([alpha, beta]);

        Assert.Equal([alpha, beta], first.ChangedVisualStateIds.ToArray());
        Assert.Equal(first, second);
        Assert.True(first.HasExplicitChanges);
        Assert.False(NodeGeometryPipelineImpact.PreserveAll.HasExplicitChanges);
        Assert.Throws<ArgumentException>(() =>
            NodeGeometryPipelineImpact.ForChangedVisualStates([]));
        Assert.Throws<ArgumentException>(() =>
            NodeGeometryPipelineImpact.ForChangedVisualStates([alpha, alpha]));
    }

    [Fact]
    public void RemovedAndHistoricalGeometryImpactsAreExactCanonicalAndDisjoint()
    {
        var alpha = new VisualStateId("test:visual:alpha");
        var beta = new VisualStateId("test:visual:beta");
        var revision = new DocumentRevision(17);
        var removed = NodeGeometryPipelineImpact.ForRemovedVisualStates([beta, alpha]);
        var historical = NodeGeometryPipelineImpact.ForHistoricalRestoration(
            [beta, alpha],
            revision);

        Assert.Equal([alpha, beta], removed.RemovedVisualStateIds.ToArray());
        Assert.True(removed.HasRemovedVisualStates);
        Assert.True(removed.RequiresExactCarryForward);
        Assert.Empty(removed.ChangedVisualStateIds);
        Assert.Null(removed.HistoricalSourceRevision);
        Assert.Equal([alpha, beta], historical.ChangedVisualStateIds.ToArray());
        Assert.Empty(historical.RemovedVisualStateIds);
        Assert.Equal(revision, historical.HistoricalSourceRevision);
        Assert.True(historical.RequiresExactCarryForward);
        Assert.Equal(
            historical,
            NodeGeometryPipelineImpact.ForHistoricalRestoration(
                [alpha, beta],
                revision));
        Assert.Throws<ArgumentException>(() =>
            NodeGeometryPipelineImpact.ForRemovedVisualStates([]));
        Assert.Throws<ArgumentException>(() =>
            NodeGeometryPipelineImpact.ForRemovedVisualStates([alpha, alpha]));
        Assert.Throws<ArgumentException>(() =>
            NodeGeometryPipelineImpact.ForHistoricalRestoration([], revision));
    }

    [Fact]
    public void FullLayoutAndSelectiveNodeGeometryAreMutuallyExclusive()
    {
        var impact = NodeGeometryPipelineImpact.ForChangedVisualStates(
            [new VisualStateId("test:visual:alpha")]);

        Assert.Null(CommandPipelineInvalidation.ResolveNodeGeometryImpact(
            CommandPipelineInvalidation.Full));
        Assert.Same(
            NodeGeometryPipelineImpact.PreserveAll,
            CommandPipelineInvalidation.ResolveNodeGeometryImpact(
                CommandPipelineInvalidation.ConnectorOnly));
        Assert.Same(
            impact,
            CommandPipelineInvalidation.ResolveNodeGeometryImpact(
                CommandPipelineInvalidation.WithoutNodeLayout,
                impact));
        Assert.Throws<ArgumentException>(() =>
            CommandPipelineInvalidation.ResolveNodeGeometryImpact(
                CommandPipelineInvalidation.Full,
                impact));
    }

    private class UndeclaredCommand : ICommand
    {
        public CommandTypeId TypeId { get; } = new("test:command/pipeline-invalidation");

        public DocumentId TargetDocumentId { get; } = new("test:document");

        public DocumentRevision ExpectedRevision => DocumentRevision.Zero;

        public CommandCategory Category => CommandCategory.Document;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.VisualModel;
    }

    private sealed class DeclaredCommand(PipelineInvalidation pipelineInvalidation) :
        UndeclaredCommand,
        ICommandPipelineInvalidation
    {
        public PipelineInvalidation PipelineInvalidation { get; } = pipelineInvalidation;
    }
}
