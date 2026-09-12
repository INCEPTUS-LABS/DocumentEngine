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

public sealed class MoveLabelCommandHandlerTests
{
    internal static readonly DocumentId DocumentId = new("test:move-label-handler");
    internal static readonly SemanticElementId SourceId = new("test:source");
    internal static readonly SemanticElementId TargetId = new("test:target");
    internal static readonly SemanticElementId RelationshipId = new("test:relationship");
    internal static readonly VisualStateId VisualId = new("test:relationship-visual");
    private static readonly PointD[] Route =
        [new(10d, 20d), new(30d, 40d), new(80d, 90d)];

    [Fact]
    public async Task ProposalChangesOnlyLabelPlacementAndPreservesAllOtherState()
    {
        var snapshot = Snapshot(DocumentRevision.Zero);
        var originalVisual = Assert.Single(snapshot.VisualModel.VisualStates);
        var originalRelationship = Assert.Single(snapshot.SemanticModel.Relationships);
        var placement = new ConnectorLabelPlacement(0.8d, new VectorD(6d, -4d));

        var result = await new MoveLabelCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, placement),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var updated = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.Equal(placement, ConnectorLabelPlacement.Resolve(updated.Properties));
        Assert.Equal("preserved", updated.Properties["test:style"].TextValue);
        Assert.Equal(originalVisual.SemanticElementId, updated.SemanticElementId);
        Assert.Equal(originalVisual.Position, updated.Position);
        Assert.Equal(originalVisual.Size, updated.Size);
        Assert.Equal(originalVisual.PlacementMode, updated.PlacementMode);
        Assert.Equal(originalVisual.Route.AsEnumerable(), updated.Route.AsEnumerable());
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(originalRelationship, Assert.Single(proposed.SemanticModel.Relationships));
        Assert.Same(snapshot.Metadata, proposed.Metadata);
        Assert.Equal(snapshot.Revision, proposed.Revision);
    }

    [Fact]
    public async Task NullTargetRemovesExplicitPlacementWithoutRemovingOtherVisualProperties()
    {
        var explicitPlacement = new ConnectorLabelPlacement(0.2d, new VectorD(-3d, 5d));
        var snapshot = Snapshot(DocumentRevision.Zero, explicitPlacement);

        var result = await new MoveLabelCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, targetPlacement: null),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var properties = Assert.Single(result.ProposedDocument!.VisualModel.VisualStates).Properties;
        Assert.False(ConnectorLabelPlacement.TryRead(properties, out _));
        Assert.Equal(ConnectorLabelPlacement.Default, ConnectorLabelPlacement.Resolve(properties));
        Assert.Equal("preserved", properties["test:style"].TextValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentDefaultOrExplicitPlacementIsANoOp(bool useExplicitDefault)
    {
        var snapshot = Snapshot(
            DocumentRevision.Zero,
            useExplicitDefault ? ConnectorLabelPlacement.Default : null);

        var result = await new MoveLabelCommandHandler().HandleAsync(
            Command(DocumentRevision.Zero, ConnectorLabelPlacement.Default),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.LabelPlacementUnchanged,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task ElementOwnedOrMissingVisualIsRejected()
    {
        var elementVisual = new VisualStateSnapshot(
            VisualId,
            SourceId,
            new PointD(5d, 6d),
            new SizeD(7d, 8d),
            VisualPlacementMode.Manual);
        var elementSnapshot = Snapshot(
            DocumentRevision.Zero,
            visualOverride: elementVisual);
        var handler = new MoveLabelCommandHandler();

        var unsupported = await handler.HandleAsync(
            Command(
                DocumentRevision.Zero,
                new ConnectorLabelPlacement(0.75d, default)),
            elementSnapshot,
            CancellationToken.None);
        var missing = await handler.HandleAsync(
            new MoveLabelCommand(
                DocumentId,
                DocumentRevision.Zero,
                new VisualStateId("test:missing"),
                new ConnectorLabelPlacement(0.75d, default)),
            elementSnapshot,
            CancellationToken.None);

        Assert.False(unsupported.Succeeded);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportLabelPlacement,
            Assert.Single(unsupported.Diagnostics).Code);
        Assert.False(missing.Succeeded);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateNotFound,
            Assert.Single(missing.Diagnostics).Code);
    }

    [Fact]
    public async Task BuiltInRegistrationCommitsOneVisualRevisionAndOneEvent()
    {
        var document = Assert.IsType<Document>(DocumentFactory.Create(
            Snapshot(DocumentRevision.Zero)).Document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var placement = new ConnectorLabelPlacement(0.9d, new VectorD(2d, -7d));

        var result = await processor.ExecuteAsync(
            document,
            Command(document.Revision, placement));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, result.AffectedComponents);
        Assert.Equal(
            placement,
            ConnectorLabelPlacement.Resolve(
                Assert.Single(document.VisualModel.VisualStates).Properties));
        var change = Assert.Single(subscriber.Events);
        Assert.Equal(MoveLabelCommand.KnownTypeId, change.CommandTypeId);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, change.AffectedComponents);
    }

    [Fact]
    public async Task EnvelopeValidatorAcceptsOnlyTheRegisteredShapeAndCancellationIsAtomic()
    {
        var validator = new MoveLabelCommandEnvelopeValidator();
        Assert.Empty(validator.Validate(Command(
            DocumentRevision.Zero,
            new ConnectorLabelPlacement(0.25d, default))));
        Assert.Contains(validator.Validate(new MalformedMoveLabelCommand()), diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.InvalidCommandStructure);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new MoveLabelCommandHandler().HandleAsync(
                Command(
                    DocumentRevision.Zero,
                    new ConnectorLabelPlacement(0.25d, default)),
                Snapshot(DocumentRevision.Zero),
                cancellation.Token));
    }

    internal static MoveLabelCommand Command(
        DocumentRevision revision,
        ConnectorLabelPlacement? targetPlacement) =>
        new(DocumentId, revision, VisualId, targetPlacement);

    internal static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        ConnectorLabelPlacement? placement = null,
        VisualStateSnapshot? visualOverride = null)
    {
        var properties = ConnectorLabelPlacement.UpdateProperties(
            new PropertyMap(
            [
                new("test:style", PropertyValue.FromText("preserved")),
            ]),
            placement);
        var visual = visualOverride ?? new VisualStateSnapshot(
            VisualId,
            RelationshipId,
            new PointD(5d, 6d),
            new SizeD(7d, 8d),
            VisualPlacementMode.Manual,
            Route,
            properties);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    new SemanticElementSnapshot(SourceId, new SemanticTypeId("test:node")),
                    new SemanticElementSnapshot(TargetId, new SemanticTypeId("test:node")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship"),
                        SourceId,
                        TargetId,
                        [new("test:name", PropertyValue.FromText("Approved"))]),
                ]),
            new VisualModelSnapshot(DocumentId, revision, [visual]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private sealed class MalformedMoveLabelCommand : ICommand
    {
        public CommandTypeId TypeId => MoveLabelCommand.KnownTypeId;

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
