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

public sealed class UpdateSemanticElementPropertyCommandHandlerTests
{
    private static readonly DocumentId DocumentId = new("test:property-handler");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly SemanticElementId OtherElementId = new("test:other");
    private const string DescriptionKey = "test:description";
    private const string NumberKey = "test:element-number";

    [Fact]
    public async Task ProposalReplacesOnlyTheExistingTypedPropertyAndPreservesReferences()
    {
        var snapshot = Snapshot();
        var original = Element(snapshot);
        const string description = "First line\r\nSecond line\nThird line";

        var result = await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
            Update(DescriptionKey, PropertyValue.FromText(description)),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Element(proposed);
        Assert.Equal(description, updated.Properties[DescriptionKey].TextValue);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.TypeId, updated.TypeId);
        Assert.Equal(original.Properties[NumberKey], updated.Properties[NumberKey]);
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

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public async Task EveryLongIntegerValueIsAccepted(long number)
    {
        var result = await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
            Update(NumberKey, PropertyValue.FromInteger(number)),
            Snapshot(number == 10 ? 11 : 10),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(number, Element(result.ProposedDocument!).Properties[NumberKey].IntegerValue);
    }

    [Fact]
    public async Task EmptyAndWhitespaceTextArePreservedExactly()
    {
        var handler = new UpdateSemanticElementPropertyCommandHandler();

        var empty = await handler.HandleAsync(
            Update(DescriptionKey, PropertyValue.FromText(string.Empty)),
            Snapshot(),
            CancellationToken.None);
        var whitespace = await handler.HandleAsync(
            Update(DescriptionKey, PropertyValue.FromText(" \t\n")),
            Snapshot(),
            CancellationToken.None);

        Assert.Equal(string.Empty, Element(empty.ProposedDocument!).Properties[DescriptionKey].TextValue);
        Assert.Equal(" \t\n", Element(whitespace.ProposedDocument!).Properties[DescriptionKey].TextValue);
    }

    [Fact]
    public async Task MissingElementFailsWithoutAProposal()
    {
        var command = new UpdateSemanticElementPropertyCommand(
            DocumentId,
            DocumentRevision.Zero,
            new SemanticElementId("test:missing"),
            DescriptionKey,
            PropertyValue.FromText("Changed"));

        var result = await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
            command,
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
    public async Task MissingOrDifferentKindPropertyFailsWithoutAProposal(bool useDifferentKind)
    {
        var properties = useDifferentKind
            ? new[] { new KeyValuePair<string, PropertyValue>(
                DescriptionKey,
                PropertyValue.FromInteger(3)) }
            : new[] { new KeyValuePair<string, PropertyValue>(
                "test:other",
                PropertyValue.FromText("value")) };
        var snapshot = Snapshot(targetProperties: properties);

        var result = await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
            Update(DescriptionKey, PropertyValue.FromText("Changed")),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticPropertyUnsupported,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task RelationshipPropertyIsUpdatedWithoutChangingEndpointsOrElements()
    {
        var snapshot = Snapshot();
        var command = new UpdateSemanticElementPropertyCommand(
            DocumentId,
            DocumentRevision.Zero,
            new SemanticElementId("test:relationship"),
            DescriptionKey,
            PropertyValue.FromText("Changed"));

        var result = await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
            command,
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var relationship = Assert.Single(proposed.SemanticModel.Relationships);
        Assert.Equal("Changed", relationship.Properties[DescriptionKey].TextValue);
        Assert.Equal(ElementId, relationship.SourceId);
        Assert.Equal(OtherElementId, relationship.TargetId);
        Assert.Equal(
            snapshot.SemanticModel.Elements.AsEnumerable(),
            proposed.SemanticModel.Elements.AsEnumerable());
        Assert.Same(snapshot.VisualModel, proposed.VisualModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
    }

    [Fact]
    public async Task ExactTypedNoOpFailsWithoutAProposal()
    {
        var result = await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
            Update(DescriptionKey, PropertyValue.FromText("Initial description")),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CancellationBeforeHandlerWorkProducesNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new UpdateSemanticElementPropertyCommandHandler().HandleAsync(
                Update(DescriptionKey, PropertyValue.FromText("Changed")),
                Snapshot(),
                cancellation.Token));
    }

    private static UpdateSemanticElementPropertyCommand Update(
        string key,
        PropertyValue value) =>
        new(DocumentId, DocumentRevision.Zero, ElementId, key, value);

    private static SemanticElementSnapshot Element(DocumentSnapshot snapshot) =>
        snapshot.SemanticModel.Elements.Single(element => element.Id == ElementId);

    private static DocumentSnapshot Snapshot(
        long number = 10,
        IEnumerable<KeyValuePair<string, PropertyValue>>? targetProperties = null)
    {
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
                            new(DescriptionKey, PropertyValue.FromText("Initial description")),
                            new(NumberKey, PropertyValue.FromInteger(number)),
                            new("test:retained", PropertyValue.FromBoolean(true)),
                        ]),
                    new SemanticElementSnapshot(
                        OtherElementId,
                        new SemanticTypeId("test:node"),
                        [new(DescriptionKey, PropertyValue.FromText("Other"))]),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        relationshipId,
                        new SemanticTypeId("test:connector"),
                        ElementId,
                        OtherElementId,
                        [new(DescriptionKey, PropertyValue.FromText("Relationship"))]),
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
