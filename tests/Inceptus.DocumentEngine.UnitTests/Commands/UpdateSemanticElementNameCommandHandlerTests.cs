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

public sealed class UpdateSemanticElementNameCommandHandlerTests
{
    private static readonly DocumentId DocumentId = new("test:name-handler");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly SemanticElementId OtherElementId = new("test:other");
    private const string NameKey = "test:name";

    [Fact]
    public async Task ProposalReplacesOnlyTheExistingTextNameProperty()
    {
        var snapshot = Snapshot();
        var original = Element(snapshot);

        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            Update("Renamed"),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Element(proposed);
        Assert.Equal("Renamed", updated.Properties[NameKey].TextValue);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.TypeId, updated.TypeId);
        Assert.Equal(original.Properties["test:retained"], updated.Properties["test:retained"]);
        Assert.Equal(original.Properties.Count, updated.Properties.Count);
        Assert.Same(
            snapshot.SemanticModel.Elements.Single(element => element.Id == OtherElementId),
            proposed.SemanticModel.Elements.Single(element => element.Id == OtherElementId));
        Assert.Equal(
            snapshot.SemanticModel.Relationships.AsEnumerable(),
            proposed.SemanticModel.Relationships.AsEnumerable());
        Assert.Same(snapshot.VisualModel, proposed.VisualModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
        Assert.Equal(snapshot.Revision, proposed.Revision);
    }

    [Fact]
    public async Task MissingElementFailsWithoutAProposal()
    {
        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            new UpdateSemanticElementNameCommand(
                DocumentId,
                DocumentRevision.Zero,
                new SemanticElementId("test:missing"),
                NameKey,
                "Renamed"),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticElementNotFound,
            Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrNonTextNamePropertyFailsWithoutAProposal(bool useNonText)
    {
        var snapshot = Snapshot(
            useNonText
                ? [new(NameKey, PropertyValue.FromInteger(3))]
                : [new("test:other", PropertyValue.FromText("value"))]);

        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            Update("Renamed"),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticNamePropertyUnsupported,
            Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BlankExistingNameFailsSoEveryAcceptedEditHasAValidHistoryInverse(
        string existingName)
    {
        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            Update("Renamed"),
            Snapshot([new(NameKey, PropertyValue.FromText(existingName))]),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticNamePropertyUnsupported,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task RelationshipIdentityIsNotAcceptedAsAnElementNameTarget()
    {
        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            new UpdateSemanticElementNameCommand(
                DocumentId,
                DocumentRevision.Zero,
                new SemanticElementId("test:relationship"),
                NameKey,
                "Renamed"),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticElementNotFound,
            Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task BlankNameFailsWithoutAProposal(string name)
    {
        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            Update(name),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticNameInvalid,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task ExactOrdinalNoOpFailsWithoutAProposal()
    {
        var result = await new UpdateSemanticElementNameCommandHandler().HandleAsync(
            Update("Original"),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticNameUnchanged,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CancellationBeforeHandlerWorkProducesNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new UpdateSemanticElementNameCommandHandler().HandleAsync(
                Update("Renamed"),
                Snapshot(),
                cancellation.Token));
    }

    private static UpdateSemanticElementNameCommand Update(string name) =>
        new(DocumentId, DocumentRevision.Zero, ElementId, NameKey, name);

    private static SemanticElementSnapshot Element(DocumentSnapshot snapshot) =>
        snapshot.SemanticModel.Elements.Single(element => element.Id == ElementId);

    private static DocumentSnapshot Snapshot(
        IEnumerable<KeyValuePair<string, PropertyValue>>? targetProperties = null)
    {
        var otherId = OtherElementId;
        var relationshipId = new SemanticElementId("test:relationship");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(
                        ElementId,
                        new SemanticTypeId("test:node"),
                        targetProperties ??
                        [
                            new(NameKey, PropertyValue.FromText("Original")),
                            new("test:retained", PropertyValue.FromBoolean(true)),
                        ]),
                    new SemanticElementSnapshot(
                        otherId,
                        new SemanticTypeId("test:node"),
                        [new(NameKey, PropertyValue.FromText("Other"))]),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        relationshipId,
                        new SemanticTypeId("test:connector"),
                        ElementId,
                        otherId,
                        [new(NameKey, PropertyValue.FromText("Relationship"))]),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("test:visual"),
                        ElementId,
                        new PointD(10d, 20d),
                        new SizeD(100d, 50d),
                        VisualPlacementMode.Manual),
                ]),
            new DocumentMetadataSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [new("test:metadata", PropertyValue.FromText("preserved"))]));
    }
}
