using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class MoveVisualStatesCommandHandlerTests
{
    private static readonly DocumentId DocumentId = new("test:atomic-move-handler");
    private static readonly VisualStateId AlphaVisualId = new("test:visual:alpha");
    private static readonly VisualStateId BetaVisualId = new("test:visual:beta");
    private static readonly VisualStateId GammaVisualId = new("test:visual:gamma");

    [Fact]
    public async Task ProposalMovesEveryTargetAndPreservesUnrelatedPersistentData()
    {
        var snapshot = Snapshot();
        var alphaBefore = Visual(snapshot, AlphaVisualId);
        var betaBefore = Visual(snapshot, BetaVisualId);
        var gammaBefore = Visual(snapshot, GammaVisualId);
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateMove(
                    BetaVisualId,
                    new PointD(240d, 150d)),
                new VisualStateMove(
                    AlphaVisualId,
                    new PointD(80d, 90d),
                    VisualPlacementMode.Pinned),
            ]);

        var result = await new MoveVisualStatesCommandHandler().HandleAsync(
            command,
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var alphaAfter = Visual(proposed, AlphaVisualId);
        var betaAfter = Visual(proposed, BetaVisualId);
        var gammaAfter = Visual(proposed, GammaVisualId);
        Assert.Equal(new PointD(80d, 90d), alphaAfter.Position);
        Assert.Equal(VisualPlacementMode.Pinned, alphaAfter.PlacementMode);
        Assert.Equal(new PointD(240d, 150d), betaAfter.Position);
        Assert.Equal(betaBefore.PlacementMode, betaAfter.PlacementMode);
        Assert.Equal(alphaBefore.Size, alphaAfter.Size);
        Assert.Equal(alphaBefore.Route, alphaAfter.Route);
        Assert.Equal(alphaBefore.Properties, alphaAfter.Properties);
        Assert.Equal(betaBefore.Size, betaAfter.Size);
        Assert.Equal(betaBefore.Route, betaAfter.Route);
        Assert.Equal(betaBefore.Properties, betaAfter.Properties);
        Assert.Same(gammaBefore, gammaAfter);
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
        Assert.Equal(snapshot.Revision, proposed.Revision);
        Assert.Equal(
            CommandPipelineInvalidation.WithoutNodeLayout,
            result.PipelineInvalidation);
        Assert.Equal(
            [AlphaVisualId, BetaVisualId],
            Assert.IsType<NodeGeometryPipelineImpact>(result.NodeGeometryImpact)
                .ChangedVisualStateIds
                .ToArray());
        Assert.Equal(new PointD(10d, 20d), alphaBefore.Position);
        Assert.Equal(new PointD(120d, 40d), betaBefore.Position);
    }

    [Fact]
    public async Task OneMissingTargetRejectsTheCompleteProposal()
    {
        var snapshot = Snapshot();
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateMove(AlphaVisualId, new PointD(80d, 90d)),
                new VisualStateMove(
                    new VisualStateId("test:visual:missing"),
                    new PointD(240d, 150d)),
            ]);

        var result = await new MoveVisualStatesCommandHandler().HandleAsync(
            command,
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateNotFound);
        Assert.Equal(new PointD(10d, 20d), Visual(snapshot, AlphaVisualId).Position);
    }

    [Fact]
    public async Task OneNonRenderableTargetRejectsTheCompleteProposal()
    {
        var snapshot = Snapshot();
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateMove(AlphaVisualId, new PointD(80d, 90d)),
                new VisualStateMove(BetaVisualId, new PointD(double.MaxValue, 150d)),
            ]);

        var result = await new MoveVisualStatesCommandHandler().HandleAsync(
            command,
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(new PointD(10d, 20d), Visual(snapshot, AlphaVisualId).Position);
        Assert.Equal(new PointD(120d, 40d), Visual(snapshot, BetaVisualId).Position);
    }

    [Fact]
    public async Task OneNegativeTargetRejectsTheCompleteAtomicProposal()
    {
        var snapshot = Snapshot();
        var result = await new MoveVisualStatesCommandHandler().HandleAsync(
            new MoveVisualStatesCommand(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateMove(AlphaVisualId, new PointD(80d, 90d)),
                    new VisualStateMove(BetaVisualId, new PointD(-1d, 150d)),
                ]),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(new PointD(10d, 20d), Visual(snapshot, AlphaVisualId).Position);
        Assert.Equal(new PointD(120d, 40d), Visual(snapshot, BetaVisualId).Position);
    }

    [Fact]
    public async Task RelationshipTargetRejectsTheCompleteProposal()
    {
        var relationshipVisualId = new VisualStateId("test:visual:relationship");
        var snapshot = Snapshot(includeRelationship: true);
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateMove(AlphaVisualId, new PointD(80d, 90d)),
                new VisualStateMove(relationshipVisualId, new PointD(240d, 150d)),
            ]);

        var result = await new MoveVisualStatesCommandHandler().HandleAsync(
            command,
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition);
        Assert.Equal(new PointD(10d, 20d), Visual(snapshot, AlphaVisualId).Position);
    }

    [Fact]
    public async Task CancellationBeforeWorkProducesNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [new VisualStateMove(AlphaVisualId, new PointD(80d, 90d))]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new MoveVisualStatesCommandHandler().HandleAsync(
                command,
                Snapshot(),
                cancellation.Token));
    }

    private static DocumentSnapshot Snapshot(bool includeRelationship = false)
    {
        var alphaElementId = new SemanticElementId("test:element:alpha");
        var betaElementId = new SemanticElementId("test:element:beta");
        var gammaElementId = new SemanticElementId("test:element:gamma");
        var relationshipId = new SemanticElementId("test:relationship");
        var semantic = new SemanticModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [
                new SemanticElementSnapshot(alphaElementId, new SemanticTypeId("test:type")),
                new SemanticElementSnapshot(betaElementId, new SemanticTypeId("test:type")),
                new SemanticElementSnapshot(gammaElementId, new SemanticTypeId("test:type")),
            ],
            includeRelationship
                ?
                [
                    new SemanticRelationshipSnapshot(
                        relationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        alphaElementId,
                        betaElementId),
                ]
                : null);
        var visualStates = new List<VisualStateSnapshot>
        {
            new(
                AlphaVisualId,
                alphaElementId,
                new PointD(10d, 20d),
                new SizeD(50d, 30d),
                VisualPlacementMode.Automatic,
                properties:
                [
                    new KeyValuePair<string, PropertyValue>(
                        "test:property",
                        PropertyValue.FromText("alpha")),
                ]),
            new(
                BetaVisualId,
                betaElementId,
                new PointD(120d, 40d),
                new SizeD(60d, 35d),
                VisualPlacementMode.Manual),
            new(
                GammaVisualId,
                gammaElementId,
                new PointD(230d, 70d),
                new SizeD(70d, 40d),
                VisualPlacementMode.Pinned),
        };
        if (includeRelationship)
        {
            visualStates.Add(new VisualStateSnapshot(
                new VisualStateId("test:visual:relationship"),
                relationshipId,
                new PointD(35d, 35d),
                new SizeD(110d, 20d),
                VisualPlacementMode.Manual,
                [new PointD(35d, 35d), new PointD(145d, 55d)]));
        }

        return new DocumentSnapshot(
            semantic,
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visualStates),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }

    private static VisualStateSnapshot Visual(
        DocumentSnapshot snapshot,
        VisualStateId visualStateId)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(visualStateId, out var visualState));
        return Assert.IsType<VisualStateSnapshot>(visualState);
    }
}
