using System.Text.Json.Nodes;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class StableSpatialRoutingPreparationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpatialEdgeResizeHistoryRestoresOnlyItsDimensionAndKeepsLaterManualPoints(bool width)
    {
        var fixture = await CreateSpatialFixtureAsync();
        var document = fixture.Document;
        var scope = Assert.Single(document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        var connectorId = new VisualStateId("v:flow-a");
        Assert.True((await fixture.Processor.ExecuteAsync(document, new SetConnectorRoutingTypeCommand(
            document.DocumentId, document.Revision, connectorId, ConnectorRoutingType.Manual))).IsCommitted);
        var history = new HistoryManager(document);
        var region = scope.Geometry.Regions.Single(value => value.ContainerSemanticElementId == PoolB);
        var oldWidth = Assert.Single(scope.Geometry.SpatialWidths).OuterWidth;
        ICommand command = width
            ? new SetOrganizationalScopeWidthCommand(document.DocumentId, document.Revision, scope.ScopeId, oldWidth + 90)
            : new SetOrganizationalRegionExpandedHeightCommand(document.DocumentId, document.Revision, scope.ScopeId, region.Id, region.ExpandedHeight + 90);
        Assert.True((await history.ExecuteAsync(fixture.Processor, command)).Succeeded);
        var current = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single();
        var route = current.Connectors.Single(value => value.VisualStateId == connectorId);
        PointD[] path = [route.Path[0], new PointD(1500, 40), new PointD(1500, 1000), route.Path[^1]];
        Assert.True((await history.ExecuteAsync(fixture.Processor, new UpdateConnectionRouteCommand(
            document.DocumentId, document.Revision, connectorId, path))).Succeeded);
        var manual = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single();
        Assert.True((await history.UndoAsync(fixture.Processor)).Succeeded);
        var undone = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single();
        Assert.Equal(oldWidth, Assert.Single(undone.Geometry.SpatialWidths).OuterWidth);
        Assert.Equal(region.ExpandedHeight, undone.Geometry.Regions.Single(value => value.Id == region.Id).ExpandedHeight);
        Assert.Equal(manual.Connectors.Select(value => value.VisualStateId), undone.Connectors.Select(value => value.VisualStateId));
        Assert.Equal(manual.Connectors.Single(value => value.VisualStateId == connectorId).ManualDefinition!.Value.AsEnumerable(),
            undone.Connectors.Single(value => value.VisualStateId == connectorId).ManualDefinition!.Value.AsEnumerable());
        Assert.True((await history.RedoAsync(fixture.Processor)).Succeeded);
        var redone = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single();
        Assert.Equal(path, redone.Connectors.Single(value => value.VisualStateId == connectorId).Path.AsEnumerable());
        Assert.Equal(width ? oldWidth + 90 : oldWidth, Assert.Single(redone.Geometry.SpatialWidths).OuterWidth);
        Assert.Equal(width ? region.ExpandedHeight : region.ExpandedHeight + 90,
            redone.Geometry.Regions.Single(value => value.Id == region.Id).ExpandedHeight);
    }

    [Fact]
    public async Task SpatialEdgeResizeWidthPreservesPathsBodiesAndExactNativeStateAcrossHistory()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var before = fixture.Document.CaptureSnapshot();
        var saved = Assert.Single(before.VisualModel.RoutingScopes!.Value);
        var originalWidth = Assert.Single(saved.Geometry.SpatialWidths).OuterWidth;
        var history = new HistoryManager(fixture.Document);
        fixture.Policy.Searches.Clear();
        var result = await history.ExecuteAsync(fixture.Processor, new SetOrganizationalScopeWidthCommand(
            before.DocumentId, before.Revision, saved.ScopeId, originalWidth + 250.125));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var after = fixture.Document.CaptureSnapshot();
        var current = Assert.Single(after.VisualModel.RoutingScopes!.Value);
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(originalWidth + 250.125, Assert.Single(current.Geometry.SpatialWidths).OuterWidth);
        Assert.Equal(saved.Connectors.AsEnumerable(), current.Connectors.AsEnumerable());
        Assert.Equal(saved.Geometry.Nodes.AsEnumerable(), current.Geometry.Nodes.AsEnumerable());
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.AsEnumerable());
        foreach (var region in current.Geometry.Regions)
        {
            var old = saved.Geometry.Regions.Single(value => value.Id == region.Id);
            Assert.Equal(old.ExpandedHeight, region.ExpandedHeight);
            Assert.Equal(old.LocalToScopeTransform, region.LocalToScopeTransform);
            Assert.Equal(78, region.ContentBounds!.Value.Left);
            Assert.Equal(40 + originalWidth + 250.125, region.ContentBounds.Value.Right);
        }
        Assert.Empty(fixture.Policy.Searches);
        await AssertColdRoundTrip(fixture);
        Assert.True((await history.UndoAsync(fixture.Processor)).Succeeded);
        var undone = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        Assert.Equal(originalWidth, Assert.Single(undone.Geometry.SpatialWidths).OuterWidth);
        Assert.Equal(current.Connectors.AsEnumerable(), undone.Connectors.AsEnumerable());
        Assert.True((await history.RedoAsync(fixture.Processor)).Succeeded);
        var redone = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        Assert.Equal(originalWidth + 250.125, Assert.Single(redone.Geometry.SpatialWidths).OuterWidth);
        Assert.Equal(current.Connectors.AsEnumerable(), redone.Connectors.AsEnumerable());
        Assert.Empty(fixture.Policy.Searches);
    }

    [Fact]
    public async Task SpatialEdgeResizeWidthAllowsExactBodyContactRejectsUnderflowAndDoesNotDemoteOnNoChange()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var before = fixture.Document.CaptureSnapshot();
        var scope = Assert.Single(before.VisualModel.RoutingScopes!.Value);
        // Independent edge arithmetic: child local origin 110, common right 40 + W.
        var minimum = Math.Max(520, 70 + scope.Geometry.Nodes.Max(node => node.LocalBounds.Right));
        fixture.Policy.Searches.Clear();
        var accepted = await fixture.Processor.ExecuteAsync(fixture.Document, new SetOrganizationalScopeWidthCommand(
            before.DocumentId, before.Revision, scope.ScopeId, minimum));
        Assert.True(accepted.IsCommitted, Describe(accepted.Diagnostics));
        var contact = fixture.Document.CaptureSnapshot();
        var result = await fixture.Processor.ExecuteAsync(fixture.Document, new SetOrganizationalScopeWidthCommand(
            contact.DocumentId, contact.Revision, scope.ScopeId, minimum - .01));
        Assert.False(result.IsCommitted);
        Assert.Equal(contact, fixture.Document.CaptureSnapshot());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("complete child bodies", StringComparison.Ordinal));
        var same = await fixture.Processor.ExecuteAsync(fixture.Document, new SetOrganizationalScopeWidthCommand(
            contact.DocumentId, contact.Revision, scope.ScopeId, minimum));
        Assert.Equal(CommandExecutionStatus.NoChange, same.Status);
        Assert.Equal(contact, fixture.Document.CaptureSnapshot());
        Assert.Empty(fixture.Policy.Searches);
    }

    [Fact]
    public async Task SpatialEdgeResizeDormantWidthRetainsItsAuthorityAndRejectsUnsafeActivation()
    {
        var fixture = await CreateSpatialFixtureAsync();
        var scope = Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value);
        var original = Assert.Single(scope.Geometry.SpatialWidths);
        await SetAvailability(fixture, false);
        await MoveNode(fixture, "a-source", new PointD(original.OuterWidth + 200, 20));
        Assert.Equal(original, Assert.Single(fixture.Document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.SpatialWidths));
        fixture = await AssertColdRoundTrip(fixture);
        var before = fixture.Document.CaptureSnapshot();
        var rejected = await fixture.Processor.ExecuteAsync(fixture.Document, new SetModelProfileAvailabilityCommand(
            before.DocumentId, before.Revision, [new(original.ProfileId, true)]));
        Assert.False(rejected.IsCommitted);
        Assert.Equal(before, fixture.Document.CaptureSnapshot());
        var width = await fixture.Processor.ExecuteAsync(fixture.Document, new SetOrganizationalScopeWidthCommand(
            before.DocumentId, before.Revision, scope.ScopeId, original.OuterWidth + 400));
        Assert.True(width.IsCommitted, Describe(width.Diagnostics));
        await SetAvailability(fixture, true);
        await AssertColdRoundTrip(fixture);
    }

    [Theory]
    [InlineData("widths")]
    [InlineData("dependency")]
    public async Task SpatialEdgeResizeNativeV2RequiresNewTypedFields(string field)
    {
        var fixture = await CreateSpatialFixtureAsync();
        var before = fixture.Document.CaptureSnapshot();
        var json = JsonNode.Parse(NativeDocumentSerializer.Export(before).AsSpan())!;
        var geometry = json["document"]!["visualModel"]!["routingScopes"]![0]!["geometry"]!;
        if (field == "widths") geometry.AsObject().Remove("spatialWidths");
        else geometry["contributors"]![0]!.AsObject().Remove("regionResizeDependency");
        var result = NativeDocumentSerializer.Import(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()).AsMemory(),
            fixture.Configuration.ConnectorAnchorPolicyProvider);
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Equal(before, fixture.Document.CaptureSnapshot());
    }
}
