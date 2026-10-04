using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class DocumentCanvasRoutingPropertiesTests
{
    [Theory]
    [InlineData(ConnectorRoutingType.Automatic, "automatic")]
    [InlineData(ConnectorRoutingType.Straight, "straight")]
    [InlineData(ConnectorRoutingType.Manual, "manual")]
    public void EachTypedModeHasAStableFormValue(ConnectorRoutingType mode, string value)
    {
        var draft = new DocumentCanvasPropertiesDraft(Snapshot(mode));
        Assert.True(draft.Authoritative.CanEditRoutingType);
        Assert.Equal(value, draft.RoutingTypeValue);
        Assert.True(draft.TryParseRoutingType(out var parsed));
        Assert.Equal(mode, parsed);
        Assert.False(draft.IsDirty);
        Assert.True(draft.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Automatic")]
    [InlineData("3")]
    [InlineData("other")]
    public void InvalidFormModeCannotBeApplied(string value)
    {
        var draft = new DocumentCanvasPropertiesDraft(Snapshot(ConnectorRoutingType.Automatic))
        {
            RoutingTypeValue = value,
        };
        Assert.True(draft.IsDirty);
        Assert.False(draft.TryParseRoutingType(out _));
        Assert.False(draft.IsValid);
    }

    [Fact]
    public void ModeAndNameUseSeparateApplyTransactions()
    {
        var draft = new DocumentCanvasPropertiesDraft(Snapshot(ConnectorRoutingType.Automatic))
        {
            RoutingTypeValue = "manual",
        };
        Assert.True(draft.IsRoutingTypeDirty);
        Assert.True(draft.IsValid);
        Assert.False(draft.HasConflictingChanges);
        draft.DataFields[0].EditorValue = "Changed name";
        Assert.True(draft.HasConflictingChanges);
        Assert.False(draft.IsValid);
        draft.RoutingTypeValue = "automatic";
        Assert.True(draft.IsValid);
        Assert.True(draft.IsSemanticDirty);
        Assert.False(draft.IsRoutingTypeDirty);
    }

    [Fact]
    public void UnpreparedAndNonConnectorSnapshotsDoNotOfferModeEditing()
    {
        Assert.False(Snapshot(null).CanEditRoutingType);
        Assert.False((Snapshot(ConnectorRoutingType.Automatic) with { IsConnector = false }).CanEditRoutingType);
        var draft = new DocumentCanvasPropertiesDraft(Snapshot(null)) { RoutingTypeValue = "manual" };
        Assert.False(draft.IsDirty);
    }

    private static DocumentCanvasPropertySnapshot Snapshot(ConnectorRoutingType? routingType) =>
        new(new DocumentId("test:routing-properties"), new DocumentRevision(4), new VisualStateId("flow:visual"),
            new SemanticElementId("flow"), new SemanticTypeId("test:flow"),
            [new DocumentCanvasDataPropertySnapshot(
                new ElementPropertyFieldDefinition(new ElementPropertyFieldId("name"), "Name", "name",
                    ElementPropertyEditorKind.SingleLineText, SemanticPropertyMutationKind.Name, true, 0),
                PropertyValue.FromText("Flow"), "Flow", true)],
            VisualPlacementMode.Automatic, new RectD(0, 0, 0, 0), true,
            new SemanticElementId("source"), new SemanticElementId("target"), ConnectorLabelPlacement.Default,
            RoutingType: routingType);
}
