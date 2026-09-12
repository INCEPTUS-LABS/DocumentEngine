using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class ConnectorAnchorPolicyCommandHandlerTests
{
    private static readonly DocumentId DocumentId = new("test:m0:commands");
    private static readonly SemanticElementId ElementId = new("test:m0:element");
    private static readonly SemanticTypeId ElementTypeId = new("test:m0:type");
    private static readonly VisualStateId VisualStateId = new("test:m0:visual");

    [Theory]
    [InlineData(ConnectorAnchorSide.Top)]
    [InlineData(ConnectorAnchorSide.Right)]
    [InlineData(ConnectorAnchorSide.Bottom)]
    [InlineData(ConnectorAnchorSide.Left)]
    public async Task DynamicSingleAcceptsFirstAnchorAndRejectsSecondAcrossRoles(
        ConnectorAnchorSide side)
    {
        var policy = Uniform(EdgeConnectorAnchorPolicy.DynamicSingle());
        var handler = new AddConnectorAnchorCommandHandler(Provider(policy));
        var empty = Snapshot();
        var first = await handler.HandleAsync(
            Add(empty, "first", side, ConnectorAnchorRole.Source, 0),
            empty,
            CancellationToken.None);
        Assert.True(first.Succeeded);
        var occupied = first.ProposedDocument!;

        var second = await handler.HandleAsync(
            Add(occupied, "second", side, ConnectorAnchorRole.Target, 1),
            occupied,
            CancellationToken.None);

        AssertPolicyFailure(second);
        Assert.Single(Visual(occupied).ConnectorAnchors);
    }

    [Theory]
    [InlineData(ConnectorAnchorSide.Top, ConnectorAnchorRole.Source)]
    [InlineData(ConnectorAnchorSide.Top, ConnectorAnchorRole.Target)]
    [InlineData(ConnectorAnchorSide.Right, ConnectorAnchorRole.Source)]
    [InlineData(ConnectorAnchorSide.Right, ConnectorAnchorRole.Target)]
    [InlineData(ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source)]
    [InlineData(ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Target)]
    [InlineData(ConnectorAnchorSide.Left, ConnectorAnchorRole.Source)]
    [InlineData(ConnectorAnchorSide.Left, ConnectorAnchorRole.Target)]
    public async Task DisabledRejectsEveryRoleWithoutAProposal(
        ConnectorAnchorSide side,
        ConnectorAnchorRole role)
    {
        var snapshot = Snapshot();
        var result = await new AddConnectorAnchorCommandHandler(
                Provider(Uniform(EdgeConnectorAnchorPolicy.Disabled)))
            .HandleAsync(
                Add(snapshot, "rejected", side, role, 0),
                snapshot,
                CancellationToken.None);

        AssertPolicyFailure(result);
        Assert.Empty(Visual(snapshot).ConnectorAnchors);
    }

    [Theory]
    [InlineData(
        ConnectorAnchorRoleCapability.Source,
        ConnectorAnchorRole.Source,
        true)]
    [InlineData(
        ConnectorAnchorRoleCapability.Source,
        ConnectorAnchorRole.Target,
        false)]
    [InlineData(
        ConnectorAnchorRoleCapability.Target,
        ConnectorAnchorRole.Source,
        false)]
    [InlineData(
        ConnectorAnchorRoleCapability.Target,
        ConnectorAnchorRole.Target,
        true)]
    [InlineData(
        ConnectorAnchorRoleCapability.SourceOrTarget,
        ConnectorAnchorRole.Source,
        true)]
    [InlineData(
        ConnectorAnchorRoleCapability.SourceOrTarget,
        ConnectorAnchorRole.Target,
        true)]
    public async Task DynamicRoleCapabilitiesAreEnforcedByTheHandler(
        ConnectorAnchorRoleCapability capability,
        ConnectorAnchorRole role,
        bool expectedSuccess)
    {
        var snapshot = Snapshot();
        var policy = Uniform(EdgeConnectorAnchorPolicy.DynamicUnlimited(capability));
        var result = await new AddConnectorAnchorCommandHandler(Provider(policy)).HandleAsync(
            Add(snapshot, "candidate", ConnectorAnchorSide.Right, role, 0),
            snapshot,
            CancellationToken.None);

        Assert.Equal(expectedSuccess, result.Succeeded);
        if (!expectedSuccess)
        {
            AssertPolicyFailure(result);
        }
    }

    [Fact]
    public async Task PredefinedRejectsDynamicAddAndDefinitionRemoval()
    {
        var definition = new PredefinedConnectorAnchorDefinition(
            new PredefinedConnectorAnchorDefinitionId("right-middle"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([definition]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);
        var provider = Provider(policy);
        var snapshot = Snapshot();
        var predefinedId = ConnectorAnchorReferenceIdentity.ForPredefined(
            VisualStateId,
            definition.Id);

        var add = await new AddConnectorAnchorCommandHandler(provider).HandleAsync(
            Add(snapshot, "dynamic", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            snapshot,
            CancellationToken.None);
        var remove = await new RemoveConnectorAnchorCommandHandler(provider).HandleAsync(
            new RemoveConnectorAnchorCommand(
                DocumentId,
                snapshot.Revision,
                VisualStateId,
                predefinedId),
            snapshot,
            CancellationToken.None);

        AssertPolicyFailure(add);
        AssertPolicyFailure(remove);
        Assert.Empty(Visual(snapshot).ConnectorAnchors);
    }

    [Fact]
    public async Task RemoveRequiresCurrentDynamicPolicyAndPreservesUsedProtection()
    {
        var anchor = new ConnectorAnchor(
            new ConnectorAnchorId("existing"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        var snapshot = Snapshot([anchor]);
        var disabled = Uniform(EdgeConnectorAnchorPolicy.Disabled);

        var result = await new RemoveConnectorAnchorCommandHandler(Provider(disabled)).HandleAsync(
            new RemoveConnectorAnchorCommand(
                DocumentId,
                snapshot.Revision,
                VisualStateId,
                anchor.Id),
            snapshot,
            CancellationToken.None);

        AssertPolicyFailure(result);
        Assert.Single(Visual(snapshot).ConnectorAnchors);
    }

    [Fact]
    public async Task DynamicAddRejectsTheReservedPredefinedReferenceNamespace()
    {
        var snapshot = Snapshot();
        var reserved = ConnectorAnchorReferenceIdentity.ForPredefined(
            VisualStateId,
            new PredefinedConnectorAnchorDefinitionId("reserved"));

        var result = await new AddConnectorAnchorCommandHandler().HandleAsync(
            new AddConnectorAnchorCommand(
                DocumentId,
                snapshot.Revision,
                VisualStateId,
                reserved,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                0),
            snapshot,
            CancellationToken.None);

        AssertPolicyFailure(result);
        Assert.Empty(Visual(snapshot).ConnectorAnchors);
    }

    private static AddConnectorAnchorCommand Add(
        DocumentSnapshot snapshot,
        string id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex) =>
        new(
            DocumentId,
            snapshot.Revision,
            VisualStateId,
            new ConnectorAnchorId(id),
            side,
            role,
            insertionIndex);

    private static ElementConnectorAnchorPolicy Uniform(EdgeConnectorAnchorPolicy edge) =>
        new(edge, edge, edge, edge);

    private static ElementConnectorAnchorPolicyRegistry Provider(
        ElementConnectorAnchorPolicy policy) =>
        new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(ElementTypeId, policy),
        ]);

    private static DocumentSnapshot Snapshot(
        IEnumerable<ConnectorAnchor>? anchors = null)
    {
        var revision = DocumentRevision.Zero;
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [new SemanticElementSnapshot(ElementId, ElementTypeId)]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [new VisualStateSnapshot(
                    VisualStateId,
                    ElementId,
                    new PointD(10d, 20d),
                    new SizeD(100d, 50d),
                    VisualPlacementMode.Manual,
                    connectorAnchors: anchors)]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static VisualStateSnapshot Visual(DocumentSnapshot snapshot)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(VisualStateId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static void AssertPolicyFailure(CommandHandlerResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation);
    }
}
