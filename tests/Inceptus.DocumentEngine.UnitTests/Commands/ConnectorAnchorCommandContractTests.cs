using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class ConnectorAnchorCommandContractTests
{
    [Fact]
    public void AddCommandRetainsTypedImmutableVisualIntent()
    {
        var command = Add();

        Assert.Equal("inceptus:command/add-connector-anchor",
            AddConnectorAnchorCommand.KnownTypeId.Value);
        Assert.Equal(AddConnectorAnchorCommand.KnownTypeId, command.TypeId);
        Assert.Equal(new DocumentId("test:document"), command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(8), command.ExpectedRevision);
        Assert.Equal(new VisualStateId("test:visual"), command.TargetVisualStateId);
        Assert.Equal(new ConnectorAnchorId("test:anchor"), command.AnchorId);
        Assert.Equal(ConnectorAnchorSide.Bottom, command.Side);
        Assert.Equal(ConnectorAnchorRole.Target, command.Role);
        Assert.Equal(2, command.InsertionIndex);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, command.AffectedComponents);
        Assert.Equal(command, Add());
        Assert.Equal(command.GetHashCode(), Add().GetHashCode());
    }

    [Fact]
    public void RemoveCommandRetainsTypedImmutableVisualIntent()
    {
        var command = new RemoveConnectorAnchorCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new ConnectorAnchorId("test:anchor"));

        Assert.Equal("inceptus:command/remove-connector-anchor",
            RemoveConnectorAnchorCommand.KnownTypeId.Value);
        Assert.Equal(RemoveConnectorAnchorCommand.KnownTypeId, command.TypeId);
        Assert.Equal(new ConnectorAnchorId("test:anchor"), command.AnchorId);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, command.AffectedComponents);
        Assert.Equal(command, new RemoveConnectorAnchorCommand(
            command.TargetDocumentId,
            command.ExpectedRevision,
            command.TargetVisualStateId,
            command.AnchorId));
    }

    [Fact]
    public void CommandsRejectInvalidIdentityEnumAndOrderInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new AddConnectorAnchorCommand(
            null!, DocumentRevision.Zero, new VisualStateId("visual"),
            new ConnectorAnchorId("anchor"), ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Source, 0));
        Assert.Throws<ArgumentNullException>(() => new AddConnectorAnchorCommand(
            new DocumentId("document"), DocumentRevision.Zero, null!,
            new ConnectorAnchorId("anchor"), ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Source, 0));
        Assert.Throws<ArgumentNullException>(() => new AddConnectorAnchorCommand(
            new DocumentId("document"), DocumentRevision.Zero,
            new VisualStateId("visual"), null!, ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Source, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddConnectorAnchorCommand(
            new DocumentId("document"), DocumentRevision.Zero,
            new VisualStateId("visual"), new ConnectorAnchorId("anchor"),
            (ConnectorAnchorSide)99, ConnectorAnchorRole.Source, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddConnectorAnchorCommand(
            new DocumentId("document"), DocumentRevision.Zero,
            new VisualStateId("visual"), new ConnectorAnchorId("anchor"),
            ConnectorAnchorSide.Top, (ConnectorAnchorRole)99, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddConnectorAnchorCommand(
            new DocumentId("document"), DocumentRevision.Zero,
            new VisualStateId("visual"), new ConnectorAnchorId("anchor"),
            ConnectorAnchorSide.Top, ConnectorAnchorRole.Source, -1));
        Assert.Throws<ArgumentNullException>(() => new RemoveConnectorAnchorCommand(
            new DocumentId("document"), DocumentRevision.Zero,
            new VisualStateId("visual"), null!));
    }

    private static AddConnectorAnchorCommand Add() =>
        new(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new ConnectorAnchorId("test:anchor"),
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Target,
            2);
}
