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

public sealed class UpdateConnectionRouteCommandHandlerTests
{
    private static readonly DocumentId DocumentId = new("test:route-handler");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId VisualId = new("test:relationship-visual");
    private static readonly PointD[] OriginalRoute =
        [new(10d, 20d), new(30d, 40d), new(80d, 90d)];
    private static readonly PointD[] UpdatedRoute =
        [new(10d, 20d), new(45d, 55d), new(80d, 90d)];

    [Fact]
    public async Task ProposalChangesOnlyRouteAndPreservesAllOtherPersistentVisualData()
    {
        var snapshot = RelationshipSnapshot();
        var original = Assert.Single(snapshot.VisualModel.VisualStates);

        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                VisualId,
                UpdatedRoute),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.Equal(UpdatedRoute, updated.Route.AsEnumerable());
        Assert.Equal(original.SemanticElementId, updated.SemanticElementId);
        Assert.Equal(original.Position, updated.Position);
        Assert.Equal(original.Size, updated.Size);
        Assert.Equal(original.PlacementMode, updated.PlacementMode);
        Assert.Equal(original.Properties, updated.Properties);
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
        Assert.Equal(snapshot.Revision, proposed.Revision);
    }

    [Fact]
    public async Task MissingVisualStateFailsWithoutAProposal()
    {
        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                new VisualStateId("test:missing"),
                UpdatedRoute),
            RelationshipSnapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateNotFound,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task ElementOwnedVisualStateDoesNotSupportPersistentRoutes()
    {
        var elementId = new SemanticElementId("test:element");
        var elementVisualId = new VisualStateId("test:element-visual");
        var visual = new VisualStateSnapshot(
            elementVisualId,
            elementId,
            new PointD(10d, 20d),
            new SizeD(30d, 40d),
            VisualPlacementMode.Manual,
            OriginalRoute,
            [new("test:style", PropertyValue.FromText("preserved"))]);
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [new SemanticElementSnapshot(elementId, new SemanticTypeId("test:type"))]),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, [visual]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                elementVisualId,
                UpdatedRoute),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportRoute,
            Assert.Single(result.Diagnostics).Code);
        Assert.Same(visual, Assert.Single(snapshot.VisualModel.VisualStates));
    }

    [Fact]
    public async Task RelationshipWithoutAnExistingPersistentRouteCanEstablishOneCompleteRoute()
    {
        var snapshot = RelationshipSnapshot(route: []);
        var original = Assert.Single(snapshot.VisualModel.VisualStates);

        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                VisualId,
                UpdatedRoute),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.Equal(UpdatedRoute, updated.Route.AsEnumerable());
        Assert.Equal(original.SemanticElementId, updated.SemanticElementId);
        Assert.Equal(original.Properties, updated.Properties);
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
    }

    [Fact]
    public async Task CompletePersistentRouteCanBeClearedWithoutChangingSemanticOrVisualProperties()
    {
        var snapshot = RelationshipSnapshot();
        var original = Assert.Single(snapshot.VisualModel.VisualStates);

        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                VisualId,
                []),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.Empty(updated.Route);
        Assert.Equal(original.SemanticElementId, updated.SemanticElementId);
        Assert.Equal(original.Position, updated.Position);
        Assert.Equal(original.Size, updated.Size);
        Assert.Equal(original.PlacementMode, updated.PlacementMode);
        Assert.Equal(original.Properties, updated.Properties);
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
    }

    [Theory]
    [InlineData(-1d, 20d)]
    [InlineData(20d, -1d)]
    public async Task NegativeRoutePointIsRejectedAuthoritatively(double x, double y)
    {
        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                VisualId,
                [new PointD(10d, 20d), new PointD(x, y), new PointD(80d, 90d)]),
            RelationshipSnapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
            Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(InternalPointRouteReplacements))]
    public async Task CompleteReplacementPreservesExactInternalPointOrdering(
        PointD[] targetRoute)
    {
        var snapshot = RelationshipSnapshot();

        var result = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                DocumentRevision.Zero,
                VisualId,
                targetRoute),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        Assert.Equal(
            targetRoute,
            Assert.Single(proposed.VisualModel.VisualStates).Route.AsEnumerable());
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
    }

    [Fact]
    public async Task CancellationBeforeHandlerWorkProducesNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new UpdateConnectionRouteCommandHandler().HandleAsync(
                new UpdateConnectionRouteCommand(
                    DocumentId,
                    DocumentRevision.Zero,
                    VisualId,
                    UpdatedRoute),
                RelationshipSnapshot(),
                cancellation.Token));
    }

    private static DocumentSnapshot RelationshipSnapshot(
        IEnumerable<PointD>? route = null)
    {
        var semantic = new SemanticModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [
                new SemanticElementSnapshot(SourceId, new SemanticTypeId("test:type")),
                new SemanticElementSnapshot(TargetId, new SemanticTypeId("test:type")),
            ],
            [
                new SemanticRelationshipSnapshot(
                    RelationshipId,
                    new SemanticTypeId("test:relationship-type"),
                    SourceId,
                    TargetId),
            ]);
        var visual = new VisualModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateSnapshot(
                    VisualId,
                    RelationshipId,
                    new PointD(5d, 6d),
                    new SizeD(7d, 8d),
                    VisualPlacementMode.Pinned,
                    route ?? OriginalRoute,
                    [new("test:style", PropertyValue.FromText("preserved"))]),
            ]);
        var metadata = new DocumentMetadataSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [new("test:schema", PropertyValue.FromInteger(1))]);
        return new DocumentSnapshot(semantic, visual, metadata);
    }

    public static TheoryData<PointD[]> InternalPointRouteReplacements => new()
    {
        new PointD[]
        {
            OriginalRoute[0],
            new(20d, 30d),
            OriginalRoute[1],
            OriginalRoute[^1],
        },
        new PointD[]
        {
            OriginalRoute[0],
            OriginalRoute[1],
            new(55d, 65d),
            OriginalRoute[^1],
        },
        new PointD[]
        {
            OriginalRoute[0],
            OriginalRoute[^1],
        },
    };
}
