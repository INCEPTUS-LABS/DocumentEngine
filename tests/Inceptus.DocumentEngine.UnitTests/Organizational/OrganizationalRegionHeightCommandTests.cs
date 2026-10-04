using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed class OrganizationalRegionHeightCommandTests
{
    private static readonly DocumentId Id = new("test:region-height");
    private static readonly DocumentScopeId ScopeId = new(Id.Value);
    private static readonly Canvas2DSpatialRegionId RegionId = new("test:unassigned");
    private static readonly OrganizationalPluginRegistration Registration =
        OrganizationalPluginRegistration.Create(new OrganizationalElementEligibilityPolicy(static _ => true));

    [Fact]
    public async Task ExistingDormantRegionProducesOnlyTypedHeightIntent()
    {
        var document = Snapshot(DocumentRevision.Zero, 224);
        var command = new SetOrganizationalRegionExpandedHeightCommand(Id, document.Revision, ScopeId, RegionId, 300);
        var registration = Registration.CommandHandlers.Single(item => item.TypeId == command.TypeId);
        Assert.Empty(registration.EnvelopeValidator.Validate(command));

        var result = await registration.Handler.HandleAsync(command, document, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Same(document, result.ProposedDocument);
        Assert.Empty(result.RoutingIntents);
        Assert.Equal(new SpatialRegionHeightIntent(ScopeId, RegionId, 300), Assert.Single(result.SpatialHeightIntents));
        Assert.Equal(CommandPipelineInvalidation.WithoutNodeLayout, result.PipelineInvalidation);
        Assert.Equal(NodeGeometryPipelineImpact.PreserveAll, result.NodeGeometryImpact);
    }

    [Fact]
    public async Task MissingRegionFailsWithoutProposingAnyMutation()
    {
        var document = Snapshot(DocumentRevision.Zero, 224);
        var command = new SetOrganizationalRegionExpandedHeightCommand(Id, document.Revision, ScopeId,
            new Canvas2DSpatialRegionId("test:missing"), 300);
        var handler = Registration.CommandHandlers.Single(item => item.TypeId == command.TypeId).Handler;
        var result = await handler.HandleAsync(command, document, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Empty(result.SpatialHeightIntents);
    }

    [Fact]
    public void HeightHistoryOwnsOnlyOldAndNewHeightAndReplaysExactIdentity()
    {
        var before = Snapshot(DocumentRevision.Zero, 224);
        var after = Snapshot(new DocumentRevision(1), 300);
        var command = new SetOrganizationalRegionExpandedHeightCommand(Id, before.Revision, ScopeId, RegionId, 300);
        var policy = Registration.HistoryPolicies.Single(item => item.TypeId == command.TypeId).Policy;
        var result = policy.Prepare(command, before, after);
        Assert.Equal(HistoryRecordingBehavior.Undoable, result.Behavior);
        Assert.Empty(result.RoutingTypeDeltas);
        Assert.Equal(new SpatialRegionHeightHistoryDelta(ScopeId, RegionId, 224, 300), Assert.Single(result.SpatialHeightDeltas));
        var undo = Assert.IsType<SetOrganizationalRegionExpandedHeightCommand>(result.UndoFactory!.Create(Id, new DocumentRevision(10)));
        Assert.Equal(224, undo.ExpandedHeight);
        Assert.Equal(ScopeId, undo.ScopeId);
        Assert.Equal(RegionId, undo.RegionId);
        Assert.Equal(new DocumentRevision(10), undo.ExpectedRevision);
        Assert.Equal(300, Assert.IsType<SetOrganizationalRegionExpandedHeightCommand>(
            result.RedoFactory!.Create(Id, new DocumentRevision(11))).ExpandedHeight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void HeightCommandRejectsNonpositiveOrNonfiniteValues(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SetOrganizationalRegionExpandedHeightCommand(
            Id, DocumentRevision.Zero, ScopeId, RegionId, value));

    private static DocumentSnapshot Snapshot(DocumentRevision revision, double height)
    {
        var geometry = new ScopeGeometrySnapshot("test:geometry", "1", new AlgorithmId("test:layout"),
            Canvas2DSceneConfiguration.Default,
            new TextMeasurementRequest("", "Arial", "test:font", "1", 12, 14, 400, TextFontStyle.Normal,
                "en", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom, 1, "test:text", "1"), [], [],
            [new SpatialRegionGeometrySnapshot(RegionId, OrganizationalModelProfile.Id, null, height, null, null)], []);
        return new(new SemanticModelSnapshot(Id, revision),
            new VisualModelSnapshot(Id, revision, [], null, [new ScopeRoutingSnapshot(ScopeId, geometry, [])]),
            new DocumentMetadataSnapshot(Id, revision));
    }
}
