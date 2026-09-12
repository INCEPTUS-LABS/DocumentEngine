using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class ProjectedConnectorAnchorTests
{
    [Theory]
    [InlineData(ResolvedConnectorAnchorKind.Dynamic, true, false)]
    [InlineData(ResolvedConnectorAnchorKind.Dynamic, false, true)]
    [InlineData(ResolvedConnectorAnchorKind.Predefined, true, true)]
    public void CanonicalMetadataRoundTripsTypedAnchorData(
        ResolvedConnectorAnchorKind kind,
        bool allowsSource,
        bool allowsTarget)
    {
        var id = kind == ResolvedConnectorAnchorKind.Predefined
            ? PredefinedId("round-trip")
            : new ConnectorAnchorId("test:anchor");
        var expected = new ProjectedConnectorAnchor(
            id,
            ConnectorAnchorSide.Left,
            (allowsSource ? ConnectorAnchorRoleCapability.Source : 0) |
                (allowsTarget ? ConnectorAnchorRoleCapability.Target : 0),
            1,
            3,
            kind);

        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(
            ProjectedConnectorAnchorMetadata.Encode(expected),
            out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(allowsSource, actual!.Allows(ConnectorAnchorRole.Source));
        Assert.Equal(allowsTarget, actual.Allows(ConnectorAnchorRole.Target));
    }

    [Fact]
    public void MalformedMetadataDoesNotProduceCorruptResolvedGeometry()
    {
        var valid = ProjectedConnectorAnchorMetadata.Encode(new ProjectedConnectorAnchor(
            new ConnectorAnchorId("test:anchor"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Dynamic));
        var malformed = new PropertyMap(valid.Select(entry =>
            StringComparer.Ordinal.Equals(
                entry.Key,
                ProjectedConnectorAnchorMetadata.SideCount)
                    ? new KeyValuePair<string, PropertyValue>(
                        entry.Key,
                        PropertyValue.FromInteger(0))
                    : entry));

        Assert.False(ProjectedConnectorAnchorMetadata.TryDecode(malformed, out var anchor));
        Assert.Null(anchor);
    }

    [Fact]
    public void ConstructorRejectsAnchorWithoutAnyEndpointCapability()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectedConnectorAnchor(
            PredefinedId("none"),
            ConnectorAnchorSide.Top,
            ConnectorAnchorRoleCapability.None,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Predefined));
    }

    [Fact]
    public void ConstructorRejectsIncoherentKindRoleAndIdentityCombinations()
    {
        var predefined = PredefinedId("canonical");

        Assert.Throws<ArgumentException>(() => new ProjectedConnectorAnchor(
            new ConnectorAnchorId("dynamic:both"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Dynamic));
        Assert.Throws<ArgumentException>(() => new ProjectedConnectorAnchor(
            predefined,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Dynamic));
        Assert.Throws<ArgumentException>(() => new ProjectedConnectorAnchor(
            new ConnectorAnchorId("predefined:not-canonical"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Predefined));
        Assert.Throws<ArgumentException>(() => new ProjectedConnectorAnchor(
            new ConnectorAnchorId("inceptus:predefined-connector-anchor:malformed"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Predefined));

        var valid = new ProjectedConnectorAnchor(
            predefined,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Predefined);
        Assert.Equal(predefined, valid.Id);
    }

    private static ConnectorAnchorId PredefinedId(string definition) =>
        ConnectorAnchorReferenceIdentity.ForPredefined(
            new VisualStateId("test:projected-owner"),
            new PredefinedConnectorAnchorDefinitionId(definition));
}
