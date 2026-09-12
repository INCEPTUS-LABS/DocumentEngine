using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class UpdateSemanticElementNameCommandTests
{
    private static readonly DocumentId DocumentId = new("test:name-contract");
    private static readonly SemanticElementId ElementId = new("test:element");

    [Fact]
    public void CommandRetainsItsImmutableSemanticDescriptionAndFixedScope()
    {
        var command = new UpdateSemanticElementNameCommand(
            DocumentId,
            new DocumentRevision(4),
            ElementId,
            "test:name",
            "Renamed");

        Assert.Equal(
            "inceptus:command/update-semantic-element-name",
            UpdateSemanticElementNameCommand.KnownTypeId.Value);
        Assert.Equal(UpdateSemanticElementNameCommand.KnownTypeId, command.TypeId);
        Assert.Equal(DocumentId, command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(4), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Semantic, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel,
            command.AffectedComponents);
        Assert.Equal(ElementId, command.TargetSemanticElementId);
        Assert.Equal("test:name", command.NamePropertyKey);
        Assert.Equal("Renamed", command.TargetName);
    }

    [Fact]
    public void EqualityIsDeepOrdinalAndIncludesEveryRequestValue()
    {
        var command = Create("Renamed");
        var equal = Create(new string("Renamed".ToCharArray()));

        Assert.Equal(command, equal);
        Assert.True(command == equal);
        Assert.Equal(command.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(command, Create("renamed"));
        Assert.NotEqual(
            command,
            new UpdateSemanticElementNameCommand(
                DocumentId,
                DocumentRevision.Zero,
                ElementId,
                "test:other-name",
                "Renamed"));
    }

    [Fact]
    public void CommandRequiresIdentitiesKeyAndNonNullExactName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new UpdateSemanticElementNameCommand(
                null!,
                DocumentRevision.Zero,
                ElementId,
                "test:name",
                "Name"));
        Assert.Throws<ArgumentNullException>(() =>
            new UpdateSemanticElementNameCommand(
                DocumentId,
                DocumentRevision.Zero,
                null!,
                "test:name",
                "Name"));
        Assert.Throws<ArgumentException>(() =>
            new UpdateSemanticElementNameCommand(
                DocumentId,
                DocumentRevision.Zero,
                ElementId,
                " ",
                "Name"));
        Assert.Throws<ArgumentNullException>(() =>
            new UpdateSemanticElementNameCommand(
                DocumentId,
                DocumentRevision.Zero,
                ElementId,
                "test:name",
                null!));

        Assert.Equal(string.Empty, Create(string.Empty).TargetName);
        Assert.Equal("  ", Create("  ").TargetName);
    }

    private static UpdateSemanticElementNameCommand Create(string targetName) =>
        new(
            DocumentId,
            DocumentRevision.Zero,
            ElementId,
            "test:name",
            targetName);
}
