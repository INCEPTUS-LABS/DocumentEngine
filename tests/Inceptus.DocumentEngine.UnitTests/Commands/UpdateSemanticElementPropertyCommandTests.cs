using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class UpdateSemanticElementPropertyCommandTests
{
    private static readonly DocumentId DocumentId = new("test:property-contract");
    private static readonly SemanticElementId ElementId = new("test:element");

    [Fact]
    public void CommandRetainsItsImmutableTypedSemanticDescriptionAndFixedScope()
    {
        var value = PropertyValue.FromInteger(25);
        var command = new UpdateSemanticElementPropertyCommand(
            DocumentId,
            new DocumentRevision(4),
            ElementId,
            "test:element-number",
            value);

        Assert.Equal(
            "inceptus:command/update-semantic-element-property",
            UpdateSemanticElementPropertyCommand.KnownTypeId.Value);
        Assert.Equal(UpdateSemanticElementPropertyCommand.KnownTypeId, command.TypeId);
        Assert.Equal(DocumentId, command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(4), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Semantic, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel,
            command.AffectedComponents);
        Assert.Equal(ElementId, command.TargetSemanticElementId);
        Assert.Equal("test:element-number", command.PropertyKey);
        Assert.Same(value, command.TargetValue);
        Assert.Equal(PropertyValueKind.Integer, command.TargetValue.Kind);
    }

    [Fact]
    public void EqualityIsDeepOrdinalTypedAndIncludesEveryRequestValue()
    {
        var command = Create("test:description", PropertyValue.FromText("First\nSecond"));
        var equal = Create(
            new string("test:description".ToCharArray()),
            PropertyValue.FromText(new string("First\nSecond".ToCharArray())));

        Assert.Equal(command, equal);
        Assert.True(command == equal);
        Assert.Equal(command.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(command, Create("test:Description", command.TargetValue));
        Assert.NotEqual(command, Create("test:description", PropertyValue.FromText("First")));
        Assert.NotEqual(command, Create("test:description", PropertyValue.FromInteger(1)));
    }

    [Fact]
    public void CommandRequiresIdentitiesKeyAndTypedValue()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new UpdateSemanticElementPropertyCommand(
                null!,
                DocumentRevision.Zero,
                ElementId,
                "test:key",
                PropertyValue.FromText(string.Empty)));
        Assert.Throws<ArgumentNullException>(() =>
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                DocumentRevision.Zero,
                null!,
                "test:key",
                PropertyValue.FromText(string.Empty)));
        Assert.Throws<ArgumentException>(() =>
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                DocumentRevision.Zero,
                ElementId,
                " ",
                PropertyValue.FromText(string.Empty)));
        Assert.Throws<ArgumentNullException>(() =>
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                DocumentRevision.Zero,
                ElementId,
                "test:key",
                null!));

        Assert.Equal(
            string.Empty,
            Create("test:description", PropertyValue.FromText(string.Empty))
                .TargetValue.TextValue);
    }

    private static UpdateSemanticElementPropertyCommand Create(
        string propertyKey,
        PropertyValue targetValue) =>
        new(DocumentId, DocumentRevision.Zero, ElementId, propertyKey, targetValue);
}
