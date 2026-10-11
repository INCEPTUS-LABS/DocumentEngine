using System.Collections.Immutable;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Routing;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseA21ElementTypeSwitchingIntegrationTests
{
    private static readonly DocumentId DocumentId = new("a21:integration");
    private static readonly SemanticElementId ElementId = new("a21:element");
    private static readonly SemanticElementId OwnerId = new("a21:owner");
    private static readonly SemanticElementId TargetId = new("a21:target");
    private static readonly SemanticElementId FlowId = new("a21:outgoing");
    private static readonly VisualStateId ElementVisualId = VisualId(ElementId);
    private static readonly VisualStateId FlowVisualId = VisualId(FlowId);

    public static TheoryData<SemanticTypeId> Families => new()
    {
        BpmnSemanticTypes.Task,
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.ExclusiveGateway,
    };

    [Theory]
    [MemberData(nameof(Families))]
    public async Task EveryConcreteTypeUsesTheSameElementAndPreservesGeometryConnectorsAndHistory(
        SemanticTypeId originalType)
    {
        var probe = new PipelineProbe();
        await using var test = await CreateAsync(originalType, probe);
        var before = test.Composition.Document.CaptureSnapshot();
        var original = Element(before);
        var previousMarkers = Markers(test);
        var options = BpmnElementTypeSwitching.GetSwitchableTypes(originalType);
        var targets = options.Where(type => type != originalType).Append(originalType);

        foreach (var target in targets)
        {
            var sourceType = Element(test.Composition.Document.CaptureSnapshot()).TypeId;
            var historyCount = test.State.HistoryStatus.EntryCount;
            probe.Reset();
            await SwitchAsync(test, target);
            var changed = test.Composition.Document.CaptureSnapshot();
            Assert.Equal(target, Element(changed).TypeId);
            Assert.Equal(original.Id, Element(changed).Id);
            Assert.Equal(original.Properties, Element(changed).Properties);
            Assert.Equal(original.AttachedToElementId, Element(changed).AttachedToElementId);
            Assert.Equal(original.ContainmentKind, Element(changed).ContainmentKind);
            AssertPreserved(before, changed);
            Assert.Equal(historyCount + 1, test.State.HistoryStatus.EntryCount);
            Assert.Equal((0, 0, 0), (probe.LayoutCalls, probe.RoutingCalls, probe.RouteRepairs));
            var markers = Markers(test);
            Assert.False(previousMarkers.SequenceEqual(markers), "Changing the concrete type must change its marker appearance.");
            if (target == BpmnSemanticTypes.Task) Assert.Empty(markers);
            else Assert.NotEmpty(markers);

            Assert.True((await test.Session.UndoAsync()).IsCommitted);
            await DrainAsync(test);
            Assert.Equal(sourceType, Element(test.Composition.Document.CaptureSnapshot()).TypeId);
            AssertPreserved(before, test.Composition.Document.CaptureSnapshot());
            Assert.Equal(previousMarkers, Markers(test));
            Assert.True((await test.Session.RedoAsync()).IsCommitted);
            await DrainAsync(test);
            Assert.Equal(target, Element(test.Composition.Document.CaptureSnapshot()).TypeId);
            AssertPreserved(before, test.Composition.Document.CaptureSnapshot());
            Assert.Equal(markers, Markers(test));
            Assert.Equal((0, 0, 0), (probe.LayoutCalls, probe.RoutingCalls, probe.RouteRepairs));
            previousMarkers = markers;
        }
    }

    [Theory]
    [MemberData(nameof(Families))]
    public async Task PropertiesTypeApplyRefreshesAuthoritativeStateAndNativeOpenPreservesOnlyConcreteType(
        SemanticTypeId originalType)
    {
        await using var test = await CreateAsync(originalType, new PipelineProbe());
        var before = test.Composition.Document.CaptureSnapshot();
        var target = BpmnElementTypeSwitching.GetSwitchableTypes(originalType).First(type => type != originalType);
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [ElementVisualId]))).Succeeded);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await test.Host.OpenPropertiesAsync(ElementVisualId));
        var draft = new DocumentCanvasPropertiesDraft(properties) { TypeIdValue = target.Value };
        test.Host.UpdatePropertiesFormState(true, ElementVisualId, draft.IsDirty);
        var applied = await test.Host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, applied.Status);
        await DrainAsync(test);
        var refreshed = Assert.IsType<DocumentCanvasPropertySnapshot>(applied.Authoritative);
        Assert.Equal(target, refreshed.TypeId);
        Assert.Equal(ElementId, refreshed.SemanticId);
        Assert.Equal(ElementVisualId, refreshed.VisualStateId);
        var switched = test.Composition.Document.CaptureSnapshot();
        AssertPreserved(before, switched);

        var bytes = NativeDocumentSerializer.Export(switched);
        using (var json = JsonDocument.Parse(bytes.AsMemory())) AssertNoGroupState(json.RootElement);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory(), test.Composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(switched, imported.Document!.CaptureSnapshot());
        await using var reopened = await HostHarness.CreateAsync(
            await new BpmnModelerCompositionFactory(initialDocument: imported.Document.CaptureSnapshot()).CreateAsync());
        Assert.Equal(target, Element(reopened.Composition.Document.CaptureSnapshot()).TypeId);
        Assert.Equal(Markers(test), Markers(reopened));
        Assert.Equal(0, reopened.State.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task InactiveNestedBoundarySwitchPreservesItsParentScopeAttachmentAndRoutes()
    {
        await using var test = await HostHarness.CreateAsync();
        var before = test.Composition.Document.CaptureSnapshot();
        var id = BpmnDemoPipeline.ProcessOrderReviewTimeoutEventId;
        var original = before.SemanticModel.Elements.Single(element => element.Id == id);
        var rootScope = test.State.ActiveScopeId;
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, before.SemanticModel.GetScope(id).Id);
        Assert.True((await test.Session.ExecuteAsync(new ChangeBpmnElementTypeCommand(
            before.DocumentId, before.Revision, id, BpmnSemanticTypes.SignalBoundaryEvent))).IsCommitted);
        await DrainAsync(test);
        Assert.Equal(rootScope, test.State.ActiveScopeId);
        var after = test.Composition.Document.CaptureSnapshot();
        var changed = after.SemanticModel.Elements.Single(element => element.Id == id);
        Assert.Equal(original.AttachedToElementId, changed.AttachedToElementId);
        Assert.Equal(original.Properties, changed.Properties);
        AssertPreserved(before, after, id);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await DrainAsync(test);
        Assert.Equal(original, test.Composition.Document.SemanticModel.Elements.Single(element => element.Id == id));
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await DrainAsync(test);
        AssertPreserved(before, test.Composition.Document.CaptureSnapshot(), id);
        Assert.True((await test.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        Assert.Contains(test.State.ProjectedGraph!.Nodes, node => node.Source.SemanticElementId == id);
        Assert.Equal(BpmnSemanticTypes.SignalBoundaryEvent,
            test.Composition.Document.SemanticModel.Elements.Single(element => element.Id == id).TypeId);
    }

    [Fact]
    public async Task ManualPointsAutomaticPathsAnchorBindingsAndConnectorOrderSurviveTypeSwitch()
    {
        var probe = new PipelineProbe();
        await using var test = await CreateAsync(BpmnSemanticTypes.Task, probe);
        var snapshot = test.Composition.Document.CaptureSnapshot();
        Assert.True((await test.Session.ExecuteAsync(new SetConnectorRoutingTypeCommand(
            snapshot.DocumentId, snapshot.Revision, FlowVisualId, ConnectorRoutingType.Manual))).IsCommitted);
        await DrainAsync(test);
        snapshot = test.Composition.Document.CaptureSnapshot();
        var path = Route(snapshot, FlowVisualId).Path;
        Assert.True((await test.Session.ExecuteAsync(new UpdateConnectionRouteCommand(
            snapshot.DocumentId, snapshot.Revision, FlowVisualId,
            [path[0], new PointD(430, 310), new PointD(510, 310), path[^1]]))).IsCommitted);
        await DrainAsync(test);
        var before = test.Composition.Document.CaptureSnapshot();
        probe.Reset();
        await SwitchAsync(test, BpmnSemanticTypes.UserTask);
        AssertPreserved(before, test.Composition.Document.CaptureSnapshot());
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await DrainAsync(test);
        AssertPreserved(before, test.Composition.Document.CaptureSnapshot());
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await DrainAsync(test);
        AssertPreserved(before, test.Composition.Document.CaptureSnapshot());
        Assert.Equal((0, 0, 0), (probe.LayoutCalls, probe.RoutingCalls, probe.RouteRepairs));
    }

    private static async Task SwitchAsync(HostHarness test, SemanticTypeId type)
    {
        var snapshot = test.Composition.Document.CaptureSnapshot();
        var result = await test.Session.ExecuteAsync(new ChangeBpmnElementTypeCommand(
            snapshot.DocumentId, snapshot.Revision, ElementId, type));
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        await DrainAsync(test);
        Assert.Equal(EditingSessionStatus.Ready, test.State.Status);
    }

    private static async Task DrainAsync(HostHarness test)
    {
        await CommandProcessor.WaitForEventDispatchIdleAsync(test.Composition.Document).WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
    }

    private static void AssertPreserved(DocumentSnapshot before, DocumentSnapshot after, SemanticElementId? changedElementId = null)
    {
        changedElementId ??= ElementId;
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(before.SemanticModel.NestedScopes.AsEnumerable(), after.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(before.SemanticModel.ScopeMemberships.AsEnumerable(), after.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(before.SemanticModel.Elements.Where(element => element.Id != changedElementId),
            after.SemanticModel.Elements.Where(element => element.Id != changedElementId));
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.AsEnumerable());
        var oldScopes = before.VisualModel.RoutingScopes!.Value;
        var newScopes = after.VisualModel.RoutingScopes!.Value;
        Assert.Equal(oldScopes.Select(scope => scope.ScopeId), newScopes.Select(scope => scope.ScopeId));
        foreach (var oldScope in oldScopes)
        {
            var newScope = newScopes.Single(scope => scope.ScopeId == oldScope.ScopeId);
            Assert.Equal(oldScope.Connectors.AsEnumerable(), newScope.Connectors.AsEnumerable());
            Assert.Equal(oldScope.Geometry.Nodes.AsEnumerable(), newScope.Geometry.Nodes.AsEnumerable());
            Assert.Equal(oldScope.Geometry.Regions.AsEnumerable(), newScope.Geometry.Regions.AsEnumerable());
            Assert.Equal(oldScope.Geometry.SpatialWidths.AsEnumerable(), newScope.Geometry.SpatialWidths.AsEnumerable());
            // The projection rule and its derived label ID change with concrete type.
            // The same authoritative label placement, text and bounds must survive.
            Assert.Equal(oldScope.Geometry.Captions.Length, newScope.Geometry.Captions.Length);
            foreach (var caption in oldScope.Geometry.Captions)
            {
                var updated = newScope.Geometry.Captions.Single(item => item.OwnerVisualStateId == caption.OwnerVisualStateId);
                Assert.Equal(caption.Placement, updated.Placement);
                Assert.Equal(caption.VisualOverride, updated.VisualOverride);
                Assert.Equal(caption.PlacementBounds, updated.PlacementBounds);
                Assert.Equal(caption.ContentBounds, updated.ContentBounds);
                Assert.Equal(caption.Transform, updated.Transform);
                Assert.Equal(caption.Lines.AsEnumerable(), updated.Lines.AsEnumerable());
            }
        }
        Assert.Equal(before.Publication, after.Publication);
        Assert.Equal(before.Metadata.SystemManagedProperties, after.Metadata.SystemManagedProperties);
        Assert.Equal(before.Metadata.ExtensionProperties, after.Metadata.ExtensionProperties);
    }

    private static (Canvas2DSceneGeometry Geometry, Canvas2DSceneStyle Style)[] Markers(HostHarness test) => test.Scene.Items
        .Where(item => item.Layer == Canvas2DSceneLayer.Decoration && item.Origin.VisualStateId == ElementVisualId &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.RegisteredExtension))
        .Select(item => (item.Geometry, item.Style)).ToArray();

    private static void AssertNoGroupState(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
            foreach (var property in json.EnumerateObject())
            {
                Assert.DoesNotContain("subtype", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("switchgroup", property.Name, StringComparison.OrdinalIgnoreCase);
                AssertNoGroupState(property.Value);
            }
        else if (json.ValueKind == JsonValueKind.Array)
            foreach (var value in json.EnumerateArray()) AssertNoGroupState(value);
    }

    private static SemanticElementSnapshot Element(DocumentSnapshot document) =>
        document.SemanticModel.Elements.Single(element => element.Id == ElementId);
    private static ConnectorRoutingRecord Route(DocumentSnapshot document, VisualStateId visualId) =>
        document.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors).Single(route => route.VisualStateId == visualId);
    private static VisualStateId VisualId(SemanticElementId id) => new(id.Value + ":visual");
    private static ConnectorAnchorId SourceAnchor(SemanticElementId id) => new(id.Value + ":source");
    private static ConnectorAnchorId TargetAnchor(SemanticElementId id) => new(id.Value + ":target");

    private static async Task<HostHarness> CreateAsync(SemanticTypeId type, PipelineProbe probe)
    {
        var boundary = BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type);
        var selected = type == BpmnSemanticTypes.Task
            ? BpmnSemanticFactory.CreateTask(ElementId, "TASK-1", "Preserved name", 7, "Preserved description")
            : type == BpmnSemanticTypes.ExclusiveGateway
                ? BpmnSemanticFactory.CreateExclusiveGateway(ElementId, "GATE-1", "Preserved name", "Preserved description")
                : boundary
                    ? BpmnSemanticFactory.CreateMessageBoundaryEvent(ElementId, OwnerId, "Preserved name", description: "Preserved description")
                    : type == BpmnSemanticTypes.MessageThrowEvent
                        ? BpmnSemanticFactory.CreateMessageThrowEvent(ElementId, "Preserved name", "Preserved description")
                        : BpmnSemanticFactory.CreateMessageCatchEvent(ElementId, "Preserved name", "Preserved description");
        var ownerBounds = new RectD(100, 100, 140, 90);
        var placement = new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5);
        var size = BpmnTaskSemanticTypes.IsTask(type) ? new SizeD(140, 90) :
            type == BpmnSemanticTypes.ExclusiveGateway ? new SizeD(60, 60) : new SizeD(36, 36);
        var position = boundary ? placement.ResolveBounds(ownerBounds, size).TopLeft : new PointD(310, 180);
        var incomingId = new SemanticElementId("a21:incoming");
        var elements = new[] { selected, BpmnSemanticFactory.CreateTask(OwnerId, "OWNER", "Owner", 1),
            BpmnSemanticFactory.CreateMessageCatchEvent(TargetId, "Target") };
        var relationships = new List<SemanticRelationshipSnapshot>
        { new(FlowId, BpmnSemanticTypes.SequenceFlow, ElementId, TargetId) };
        var visuals = new List<VisualStateSnapshot>
        {
            Node(selected.Id, position, size, boundary ? placement : null),
            Node(OwnerId, ownerBounds.TopLeft, ownerBounds.Size),
            Node(TargetId, new PointD(620, 180), new SizeD(36, 36)),
            Flow(FlowId, ElementId, TargetId),
        };
        if (!boundary)
        {
            relationships.Add(new(incomingId, BpmnSemanticTypes.SequenceFlow, OwnerId, ElementId));
            visuals.Add(Flow(incomingId, OwnerId, ElementId));
        }
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero, elements, relationships),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var composition = await new BpmnModelerCompositionFactory(initialDocument: snapshot).CreateAsync();
        var source = composition.Configuration;
        var configuration = new EditingSessionConfiguration(source.ProjectionEngine,
            new LayoutEngine([new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, probe)]), source.LayoutAlgorithmId,
            new RoutingEngine([new RoutingAlgorithmRegistration(source.RoutingAlgorithmId, probe)]), source.RoutingAlgorithmId,
            source.SceneBuilder, source.ProjectionContext, source.LayoutContext, source.RoutingContext,
            source.InitialEditorState, source.CommandHandlers, source.CommandValidators, source.HistoryPolicies,
            source.DocumentChangedSubscribers, source.ConnectorAnchorPolicyProvider, source.ModelProfileCatalog,
            source.InitialModelProfileViewState, source.RoutingInputPreparer);
        return await HostHarness.CreateAsync(new DocumentCanvasComposition(composition.Document, configuration,
            composition.PropertiesSchemaCatalog, spatialEditPlanners: composition.SpatialEditPlanners));
    }

    private static VisualStateSnapshot Node(SemanticElementId id, PointD point, SizeD size,
        BoundaryAttachmentPlacement? attachment = null) => new(VisualId(id), id, point, size, VisualPlacementMode.Pinned,
            connectorAnchors: attachment is null
                ? [new(SourceAnchor(id), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                    new(TargetAnchor(id), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)]
                : [new(SourceAnchor(id), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0)], boundaryAttachment: attachment);

    private static VisualStateSnapshot Flow(SemanticElementId id, SemanticElementId source, SemanticElementId target) =>
        new(VisualId(id), id, default, default, VisualPlacementMode.Automatic,
            sourceAnchorId: SourceAnchor(source), targetAnchorId: TargetAnchor(target));

    private sealed class PipelineProbe : ILayoutAlgorithm, IRoutingAlgorithm, IStableConnectorRoutingPolicy
    {
        private readonly BpmnLayoutAlgorithm _layout = new();
        private readonly BpmnRoutingAlgorithm _routing = new();
        internal int LayoutCalls, RoutingCalls, RouteRepairs;
        internal void Reset() => LayoutCalls = RoutingCalls = RouteRepairs = 0;
        public LayoutAlgorithmResult Compute(ProjectedGraph graph, LayoutContext context, CancellationToken cancellationToken)
        { LayoutCalls++; return _layout.Compute(graph, context, cancellationToken); }
        public RoutingAlgorithmResult Route(ProjectedGraph graph, LayoutResult layout, RoutingContext context, CancellationToken cancellationToken)
        { RoutingCalls++; return _routing.Route(graph, layout, context, cancellationToken); }
        public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate, CancellationToken cancellationToken = default) =>
            _routing.Assess(graph, logicalLayout, context, edgeId, candidate, cancellationToken);
        public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
            ImmutableArray<ProjectedObjectId> orderedEdgeIds, CancellationToken cancellationToken = default)
        { RouteRepairs++; return _routing.RouteAffected(graph, logicalLayout, context, orderedEdgeIds, cancellationToken); }
    }
}
