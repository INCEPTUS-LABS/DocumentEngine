using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class ConnectorAnchorOccupancyTests
{
    [Fact]
    public void CountsPersistentEndpointReferencesRatherThanConnectorVisuals()
    {
        var shared = new ConnectorAnchorId("test:shared-anchor");
        var other = new ConnectorAnchorId("test:other-anchor");
        var visualModel = VisualModel(
            ConnectorVisual("test:connector:a", shared, shared),
            ConnectorVisual("test:connector:b", shared, other));

        Assert.Equal(3, ConnectorAnchorOccupancy.CountEndpointReferences(
            visualModel,
            shared));
        Assert.Equal(1, ConnectorAnchorOccupancy.CountEndpointReferences(
            visualModel,
            other));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(visualModel, shared));
        Assert.Equal(
            [shared, shared, shared, other],
            ConnectorAnchorOccupancy.EnumerateEndpointReferences(visualModel));
    }

    [Fact]
    public void ReportsUnreferencedAnchorAsFree()
    {
        var visualModel = VisualModel(
            ConnectorVisual(
                "test:connector",
                new ConnectorAnchorId("test:source"),
                new ConnectorAnchorId("test:target")));
        var free = new ConnectorAnchorId("test:free");

        Assert.Equal(0, ConnectorAnchorOccupancy.CountEndpointReferences(
            visualModel,
            free));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(visualModel, free));
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    public void ExcludesOnlyTheExactEndpointBeingReconnected(
        ConnectorEndpointKind excludedEndpointKind)
    {
        var currentAnchor = new ConnectorAnchorId("test:current-anchor");
        var otherAnchor = new ConnectorAnchorId("test:other-anchor");
        var connectorId = new VisualStateId("test:connector:current");
        var visualModel = VisualModel(new VisualStateSnapshot(
            connectorId,
            new SemanticElementId("test:connector:current:semantic"),
            default,
            default,
            VisualPlacementMode.Automatic,
            sourceAnchorId: excludedEndpointKind == ConnectorEndpointKind.Source
                ? currentAnchor
                : otherAnchor,
            targetAnchorId: excludedEndpointKind == ConnectorEndpointKind.Target
                ? currentAnchor
                : otherAnchor));

        Assert.False(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            visualModel,
            currentAnchor,
            connectorId,
            excludedEndpointKind));
        Assert.True(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            visualModel,
            otherAnchor,
            connectorId,
            excludedEndpointKind));
    }

    [Fact]
    public void OtherConnectorEndpointStillOccupiesTheExcludedEndpointsAnchor()
    {
        var shared = new ConnectorAnchorId("test:shared-anchor");
        var currentConnectorId = new VisualStateId("test:connector:current");
        var visualModel = VisualModel(
            new VisualStateSnapshot(
                currentConnectorId,
                new SemanticElementId("test:connector:current:semantic"),
                default,
                default,
                VisualPlacementMode.Automatic,
                sourceAnchorId: shared),
            ConnectorVisual("test:connector:other", shared, shared));

        Assert.True(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            visualModel,
            shared,
            currentConnectorId,
            ConnectorEndpointKind.Source));
    }

    [Fact]
    public void OppositeEndpointOnTheSameConnectorIsNeverExcluded()
    {
        var shared = new ConnectorAnchorId("test:shared-anchor");
        var connectorId = new VisualStateId("test:connector:current");
        var visualModel = VisualModel(new VisualStateSnapshot(
            connectorId,
            new SemanticElementId("test:connector:current:semantic"),
            default,
            default,
            VisualPlacementMode.Automatic,
            sourceAnchorId: shared,
            targetAnchorId: shared));

        Assert.True(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            visualModel,
            shared,
            connectorId,
            ConnectorEndpointKind.Source));
        Assert.True(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            visualModel,
            shared,
            connectorId,
            ConnectorEndpointKind.Target));
    }

    [Fact]
    public void OtherEndpointQueryRejectsInvalidInputs()
    {
        var visualModel = VisualModel();
        var anchorId = new ConnectorAnchorId("test:anchor");
        var visualStateId = new VisualStateId("test:connector");

        Assert.Throws<ArgumentNullException>(() =>
            ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                null!,
                anchorId,
                visualStateId,
                ConnectorEndpointKind.Source));
        Assert.Throws<ArgumentNullException>(() =>
            ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                visualModel,
                null!,
                visualStateId,
                ConnectorEndpointKind.Source));
        Assert.Throws<ArgumentNullException>(() =>
            ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                visualModel,
                anchorId,
                null!,
                ConnectorEndpointKind.Source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                visualModel,
                anchorId,
                visualStateId,
                (ConnectorEndpointKind)99));
    }

    private static VisualModelSnapshot VisualModel(
        params VisualStateSnapshot[] visualStates) =>
        new(
            new DocumentId("test:connector-anchor-occupancy"),
            DocumentRevision.Zero,
            visualStates);

    private static VisualStateSnapshot ConnectorVisual(
        string id,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            new VisualStateId(id),
            new SemanticElementId($"{id}:semantic"),
            default,
            default,
            VisualPlacementMode.Automatic,
            sourceAnchorId: sourceAnchorId,
            targetAnchorId: targetAnchorId);
}
