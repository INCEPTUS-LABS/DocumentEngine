using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class UpdateNodeLabelVisualOverrideCommandTests
{
    [Fact]
    public void CommandRetainsOneAtomicVisualOverrideAndSupportsAutomaticRestoration()
    {
        var documentId = new DocumentId("test:update-node-label");
        var visualId = new VisualStateId("test:node");
        var visualOverride = new NodeLabelVisualOverride(14d, -9d, 120d, 32d);
        var command = new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            visualOverride);

        Assert.Equal(
            "inceptus:command/update-node-label-visual-override",
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId.Value);
        Assert.Equal(UpdateNodeLabelVisualOverrideCommand.KnownTypeId, command.TypeId);
        Assert.Equal(documentId, command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(3), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(visualId, command.TargetVisualStateId);
        Assert.Equal(visualOverride, command.TargetOverride);
        Assert.Null(new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            DocumentRevision.Zero,
            visualId,
            targetOverride: null).TargetOverride);
    }

    [Fact]
    public void EqualityIncludesExactOptionalOverrideAndRevision()
    {
        var documentId = new DocumentId("test:update-node-label");
        var visualId = new VisualStateId("test:node");
        var visualOverride = new NodeLabelVisualOverride(14d, -9d, 120d, 32d);
        var command = new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            visualOverride);

        Assert.Equal(command, new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            new NodeLabelVisualOverride(14d, -9d, 120d, 32d)));
        Assert.NotEqual(command, new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            new DocumentRevision(4),
            visualId,
            visualOverride));
        Assert.NotEqual(command, new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            new DocumentRevision(3),
            visualId,
            targetOverride: null));
    }
}
