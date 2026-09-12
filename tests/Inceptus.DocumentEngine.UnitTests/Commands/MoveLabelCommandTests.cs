using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class MoveLabelCommandTests
{
    [Fact]
    public void CommandRetainsItsImmutableVisualDescriptionAndSupportsDefaultRestoration()
    {
        var documentId = new DocumentId("test:move-label");
        var visualId = new VisualStateId("test:connector");
        var placement = new ConnectorLabelPlacement(0.8d, new VectorD(4d, -9d));
        var command = new MoveLabelCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            placement);

        Assert.Equal("inceptus:command/move-label", MoveLabelCommand.KnownTypeId.Value);
        Assert.Equal(MoveLabelCommand.KnownTypeId, command.TypeId);
        Assert.Equal(documentId, command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(3), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(visualId, command.TargetVisualStateId);
        Assert.Equal(placement, command.TargetPlacement);
        Assert.Null(new MoveLabelCommand(
            documentId,
            DocumentRevision.Zero,
            visualId,
            targetPlacement: null).TargetPlacement);
    }

    [Fact]
    public void EqualityIncludesExactPlacementAndRevision()
    {
        var documentId = new DocumentId("test:move-label");
        var visualId = new VisualStateId("test:connector");
        var placement = new ConnectorLabelPlacement(0.8d, new VectorD(4d, -9d));
        var command = new MoveLabelCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            placement);

        Assert.Equal(command, new MoveLabelCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            new ConnectorLabelPlacement(0.8d, new VectorD(4d, -9d))));
        Assert.NotEqual(command, new MoveLabelCommand(
            documentId,
            new DocumentRevision(4),
            visualId,
            placement));
        Assert.NotEqual(command, new MoveLabelCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            targetPlacement: null));
    }
}
