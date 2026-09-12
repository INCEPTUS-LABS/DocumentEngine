using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class UpdateNodeLabelVisualOverrideCommandHandlerTests
{
    internal static readonly DocumentId DocumentId = new("test:node-label-override");
    internal static readonly SemanticElementId ElementId = new("test:node");
    internal static readonly SemanticElementId OtherElementId = new("test:other-node");
    internal static readonly SemanticElementId RelationshipId = new("test:relationship");
    internal static readonly VisualStateId VisualId = new("test:node-visual");

    [Fact]
    public async Task ProposalChangesOnlyOverrideAndPreservesAllOtherDocumentState()
    {
        var initial = new NodeLabelVisualOverride(-10d, 24d, 90d, 28d);
        var expected = new NodeLabelVisualOverride(35d, -18d, 140d, 42d);
        var snapshot = Snapshot(DocumentRevision.Zero, initial);
        var original = Assert.Single(snapshot.VisualModel.VisualStates);

        var result = await new UpdateNodeLabelVisualOverrideCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, expected),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.True(NodeLabelVisualOverride.TryRead(
            updated.Properties,
            out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal("preserved", updated.Properties["test:style"].TextValue);
        Assert.Equal(original.SemanticElementId, updated.SemanticElementId);
        Assert.Equal(original.Position, updated.Position);
        Assert.Equal(original.Size, updated.Size);
        Assert.Equal(original.PlacementMode, updated.PlacementMode);
        Assert.Equal(original.Route.AsEnumerable(), updated.Route.AsEnumerable());
        Assert.Equal(
            original.ConnectorAnchors.AsEnumerable(),
            updated.ConnectorAnchors.AsEnumerable());
        Assert.Equal(original.SourceAnchorId, updated.SourceAnchorId);
        Assert.Equal(original.TargetAnchorId, updated.TargetAnchorId);
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
        Assert.Equal(snapshot.Revision, proposed.Revision);
        Assert.Equal(PipelineInvalidation.Scene, result.PipelineInvalidation);
        Assert.Null(result.NodeGeometryImpact);
    }

    [Fact]
    public async Task NullTargetRemovesOverrideWithoutRemovingUnrelatedProperties()
    {
        var snapshot = Snapshot(
            DocumentRevision.Zero,
            new NodeLabelVisualOverride(12d, 16d, 100d, 30d));

        var result = await new UpdateNodeLabelVisualOverrideCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, targetOverride: null),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var properties = Assert.Single(
            result.ProposedDocument!.VisualModel.VisualStates).Properties;
        Assert.False(NodeLabelVisualOverride.TryRead(properties, out _));
        Assert.Equal("preserved", properties["test:style"].TextValue);
        Assert.Single(properties);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentExplicitUpdateOrAbsentResetIsANoOp(bool isExplicit)
    {
        var existing = isExplicit
            ? new NodeLabelVisualOverride(12d, 16d, 100d, 30d)
            : null;
        var snapshot = Snapshot(DocumentRevision.Zero, existing);

        var result = await new UpdateNodeLabelVisualOverrideCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, existing),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.NodeLabelVisualOverrideUnchanged,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task RelationshipOwnedOrMissingVisualIsRejected()
    {
        var relationshipVisual = new VisualStateSnapshot(
            VisualId,
            RelationshipId,
            new PointD(5d, 6d),
            new SizeD(7d, 8d),
            VisualPlacementMode.Manual);
        var snapshot = Snapshot(
            DocumentRevision.Zero,
            visualStateOverride: relationshipVisual);
        var handler = new UpdateNodeLabelVisualOverrideCommandHandler();
        var target = new NodeLabelVisualOverride(5d, 6d, 80d, 20d);

        var unsupported = await handler.HandleAsync(
            Command(DocumentRevision.Zero, target),
            snapshot,
            CancellationToken.None);
        var missing = await handler.HandleAsync(
            new UpdateNodeLabelVisualOverrideCommand(
                DocumentId,
                DocumentRevision.Zero,
                new VisualStateId("test:missing"),
                target),
            snapshot,
            CancellationToken.None);

        Assert.False(unsupported.Succeeded);
        Assert.Equal(
            CommandExecutionDiagnosticCodes
                .VisualStateDoesNotSupportNodeLabelVisualOverride,
            Assert.Single(unsupported.Diagnostics).Code);
        Assert.False(missing.Succeeded);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateNotFound,
            Assert.Single(missing.Diagnostics).Code);
    }

    [Fact]
    public async Task PinnedLabelBoxOutsideDocumentIsRejectedAuthoritatively()
    {
        var target = new NodeLabelVisualOverride(-70d, -40d, 80d, 20d);

        var result = await new UpdateNodeLabelVisualOverrideCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, target),
            Snapshot(DocumentRevision.Zero),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task BuiltInRegistrationCommitsOneVisualRevisionAndOneEvent()
    {
        var document = Assert.IsType<Document>(DocumentFactory.Create(
            Snapshot(DocumentRevision.Zero)).Document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var expected = new NodeLabelVisualOverride(40d, 34d, 150d, 38d);

        var result = await processor.ExecuteAsync(
            document,
            Command(document.Revision, expected));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            result.AffectedComponents);
        Assert.True(NodeLabelVisualOverride.TryRead(
            Assert.Single(document.VisualModel.VisualStates).Properties,
            out var actual));
        Assert.Equal(expected, actual);
        var change = Assert.Single(subscriber.Events);
        Assert.Equal(
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            change.CommandTypeId);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            change.AffectedComponents);
        Assert.Equal(PipelineInvalidation.Scene, change.PipelineInvalidation);
        Assert.NotNull(change.NodeGeometryImpact);
        Assert.Empty(change.NodeGeometryImpact.ChangedVisualStateIds);
    }

    [Fact]
    public async Task EnvelopeValidatorAcceptsOnlyRegisteredShapeAndCancellationIsAtomic()
    {
        var validator = new UpdateNodeLabelVisualOverrideCommandEnvelopeValidator();
        Assert.Empty(validator.Validate(Command(
            DocumentRevision.Zero,
            new NodeLabelVisualOverride(2d, 3d, 80d, 20d))));
        Assert.Contains(
            validator.Validate(new MalformedUpdateCommand()),
            diagnostic =>
                diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new UpdateNodeLabelVisualOverrideCommandHandler().HandleAsync(
                Command(
                    DocumentRevision.Zero,
                    new NodeLabelVisualOverride(2d, 3d, 80d, 20d)),
                Snapshot(DocumentRevision.Zero),
                cancellation.Token));
    }

    internal static UpdateNodeLabelVisualOverrideCommand Command(
        DocumentRevision revision,
        NodeLabelVisualOverride? targetOverride) =>
        new(DocumentId, revision, VisualId, targetOverride);

    internal static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        NodeLabelVisualOverride? visualOverride = null,
        VisualStateSnapshot? visualStateOverride = null)
    {
        var properties = NodeLabelVisualOverride.UpdateProperties(
            new PropertyMap(
            [
                new("test:style", PropertyValue.FromText("preserved")),
            ]),
            visualOverride);
        var visual = visualStateOverride ?? new VisualStateSnapshot(
            VisualId,
            ElementId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Pinned,
            [new PointD(1d, 2d), new PointD(3d, 4d)],
            properties,
            [
                new ConnectorAnchor(
                    new ConnectorAnchorId("test:anchor"),
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0),
            ]);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    new SemanticElementSnapshot(
                        ElementId,
                        new SemanticTypeId("test:node-type")),
                    new SemanticElementSnapshot(
                        OtherElementId,
                        new SemanticTypeId("test:node-type")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        ElementId,
                        OtherElementId),
                ]),
            new VisualModelSnapshot(DocumentId, revision, [visual]),
            new DocumentMetadataSnapshot(
                DocumentId,
                revision,
                [new("test:schema", PropertyValue.FromInteger(1))]));
    }

    private sealed class MalformedUpdateCommand : ICommand
    {
        public CommandTypeId TypeId => UpdateNodeLabelVisualOverrideCommand.KnownTypeId;

        public DocumentId TargetDocumentId => DocumentId;

        public DocumentRevision ExpectedRevision => DocumentRevision.Zero;

        public CommandCategory Category => CommandCategory.Visual;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.VisualModel;
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
