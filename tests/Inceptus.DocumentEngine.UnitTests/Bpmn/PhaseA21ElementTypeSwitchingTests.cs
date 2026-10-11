using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
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
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseA21ElementTypeSwitchingTests
{
    private static readonly DocumentId DocumentId = new("a21:types");
    private static readonly SemanticElementId ElementId = new("a21:element");
    private static readonly SemanticElementId OwnerId = new("a21:owner");
    private static readonly SemanticElementId TargetId = new("a21:target");
    private static readonly VisualStateId ElementVisualId = new("a21:element:visual");

    public static TheoryData<SemanticTypeId> Families => new()
    {
        BpmnSemanticTypes.Task,
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.ExclusiveGateway,
    };

    [Fact]
    public void GroupsReuseExistingClassifiersAndRejectAllCrossGroupPairs()
    {
        var groups = new[]
        {
            BpmnTaskSemanticTypes.All,
            BpmnIntermediateEventSemanticTypes.Catch,
            BpmnIntermediateEventSemanticTypes.Throw,
            BpmnBoundaryEventSemanticTypes.All,
            BpmnGatewaySemanticTypes.All,
        };
        foreach (var group in groups)
        {
            foreach (var source in group)
            {
                Assert.Equal(group, BpmnElementTypeSwitching.GetSwitchableTypes(source));
                foreach (var other in groups)
                {
                    foreach (var target in other)
                    {
                        Assert.Equal(group == other, BpmnElementTypeSwitching.CanSwitch(source, target));
                    }
                }
            }
        }

        foreach (var standalone in new[]
                 {
                     BpmnSemanticTypes.StartEvent, BpmnSemanticTypes.EndEvent,
                     BpmnSemanticTypes.SubProcess, BpmnSemanticTypes.SequenceFlow,
                     new SemanticTypeId("BPMN.Unknown"),
                 })
        {
            Assert.Empty(BpmnElementTypeSwitching.GetSwitchableTypes(standalone));
            Assert.False(BpmnElementTypeSwitching.CanSwitch(standalone, BpmnSemanticTypes.Task));
            Assert.False(BpmnElementTypeSwitching.CanSwitch(BpmnSemanticTypes.Task, standalone));
        }
    }

    [Theory]
    [MemberData(nameof(Families))]
    public async Task EveryConcreteTypePreservesAllStateThroughCommandsAndHistory(SemanticTypeId initial)
    {
        var harness = Create(initial);
        var before = harness.Document.CaptureSnapshot();
        var original = before.SemanticModel.Elements.Single(item => item.Id == ElementId);
        foreach (var target in BpmnElementTypeSwitching.GetSwitchableTypes(initial)
                     .Where(type => type != initial).Append(initial))
        {
            var previous = CurrentType(harness);
            var result = await harness.History.ExecuteAsync(harness.Processor,
                Switch(harness, target));
            Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            Assert.Equal(target, CurrentType(harness));
            AssertPreserved(before, harness.Document.CaptureSnapshot(), original);
            Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
            Assert.Equal(previous, CurrentType(harness));
            AssertPreserved(before, harness.Document.CaptureSnapshot(), original);
            Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
            Assert.Equal(target, CurrentType(harness));
            AssertPreserved(before, harness.Document.CaptureSnapshot(), original);
        }
    }

    [Fact]
    public async Task FocusedHistoryRestoresOnlyTypeAndKeepsSurvivingPropertiesGeometryAndRoute()
    {
        var harness = Create(BpmnSemanticTypes.Task);
        Assert.True((await harness.History.ExecuteAsync(harness.Processor,
            Switch(harness, BpmnSemanticTypes.UserTask))).IsCommitted);
        Assert.True((await harness.Processor.ExecuteAsync(harness.Document,
            new MoveVisualStateCommand(DocumentId, harness.Document.Revision,
                ElementVisualId, new PointD(200d, 240d), VisualPlacementMode.Pinned))).IsCommitted);
        Assert.True((await harness.Processor.ExecuteAsync(harness.Document,
            new UpdateSemanticElementPropertyCommand(DocumentId, harness.Document.Revision,
                ElementId, BpmnSemanticProperties.Description,
                PropertyValue.FromText("Surviving description")))).IsCommitted);
        var routed = await harness.Processor.ExecuteAsync(harness.Document,
            new UpdateConnectionRouteCommand(DocumentId, harness.Document.Revision,
                new VisualStateId("flow:visual"),
                [new PointD(220d, 140d), new PointD(220d, 280d), new PointD(400d, 280d), new PointD(400d, 140d)]));
        Assert.True(routed.IsCommitted, string.Join("; ", routed.Diagnostics.Select(item => item.Message)));
        var surviving = harness.Document.CaptureSnapshot();
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(BpmnSemanticTypes.Task, CurrentType(harness));
        AssertPreserved(surviving, harness.Document.CaptureSnapshot(),
            surviving.SemanticModel.Elements.Single(element => element.Id == ElementId));
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(BpmnSemanticTypes.UserTask, CurrentType(harness));
        AssertPreserved(surviving, harness.Document.CaptureSnapshot(),
            surviving.SemanticModel.Elements.Single(element => element.Id == ElementId));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("cross-group")]
    [InlineData("subprocess")]
    [InlineData("unchanged")]
    [InlineData("missing")]
    [InlineData("relationship")]
    [InlineData("revision")]
    [InlineData("document")]
    [InlineData("cancelled")]
    public async Task InvalidCommandsFailAtomically(string scenario)
    {
        var harness = Create(BpmnSemanticTypes.Task);
        var before = harness.Document.CaptureSnapshot();
        var target = scenario switch
        {
            "unknown" => new SemanticTypeId("BPMN.Unknown"),
            "cross-group" => BpmnSemanticTypes.ExclusiveGateway,
            "subprocess" => BpmnSemanticTypes.SubProcess,
            "unchanged" => BpmnSemanticTypes.Task,
            _ => BpmnSemanticTypes.UserTask,
        };
        var id = scenario switch
        {
            "missing" => new SemanticElementId("missing"),
            "relationship" => new SemanticElementId("flow"),
            _ => ElementId,
        };
        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancelled") { await cancellation.CancelAsync(); }
        var result = await harness.History.ExecuteAsync(harness.Processor,
            new ChangeBpmnElementTypeCommand(scenario == "document" ? new DocumentId("other") : DocumentId,
                scenario == "revision" ? new DocumentRevision(42) : before.Revision, id, target), cancellation.Token);
        Assert.False(result.IsCommitted);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Theory]
    [InlineData("receive-to-task", BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid)]
    [InlineData("catch-to-message", BpmnCommandDiagnosticCodes.EventBasedGatewayMixedMessageReceptionModes)]
    [InlineData("gateway-to-event-based", BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid)]
    [InlineData("gateway-extra-incoming", BpmnCommandDiagnosticCodes.EventBasedTargetAdditionalIncoming)]
    public async Task SameGroupSwitchesRespectExistingEventBasedGatewayInvariants(string scenario, string code)
    {
        var gatewayId = new SemanticElementId("gateway");
        var receiveId = new SemanticElementId("receive");
        SemanticElementSnapshot[] elements;
        SemanticRelationshipSnapshot[] flows;
        SemanticTypeId target;
        if (scenario.StartsWith("gateway", StringComparison.Ordinal))
        {
            elements = [Element(ElementId, BpmnSemanticTypes.ExclusiveGateway),
                Element(TargetId, scenario == "gateway-extra-incoming" ? BpmnSemanticTypes.TimerCatchEvent : BpmnSemanticTypes.Task),
                Element(OwnerId, BpmnSemanticTypes.Task)];
            flows = scenario == "gateway-extra-incoming"
                ? [Flow("flow", ElementId, TargetId), Flow("other", OwnerId, TargetId)]
                : [Flow("flow", ElementId, TargetId)];
            target = BpmnSemanticTypes.EventBasedGateway;
        }
        else
        {
            elements = [Element(gatewayId, BpmnSemanticTypes.EventBasedGateway),
                Element(ElementId, scenario == "receive-to-task" ? BpmnSemanticTypes.ReceiveTask : BpmnSemanticTypes.TimerCatchEvent),
                Element(receiveId, BpmnSemanticTypes.ReceiveTask)];
            flows = [Flow("flow", gatewayId, ElementId), Flow("other", gatewayId, receiveId)];
            target = scenario == "receive-to-task" ? BpmnSemanticTypes.Task : BpmnSemanticTypes.MessageCatchEvent;
        }

        var harness = Create(elements, flows);
        var before = harness.Document.CaptureSnapshot();
        var result = await harness.History.ExecuteAsync(harness.Processor, Switch(harness, target));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundarySwitchRejectsMissingOrNonActivityAttachment(bool hasOwner)
    {
        var harness = Create(
            [Element(ElementId, BpmnSemanticTypes.MessageBoundaryEvent, hasOwner ? OwnerId : null),
                Element(OwnerId, BpmnSemanticTypes.ExclusiveGateway), Element(TargetId, BpmnSemanticTypes.Task)],
            [Flow("flow", ElementId, TargetId)]);
        var before = harness.Document.CaptureSnapshot();
        var result = await harness.History.ExecuteAsync(harness.Processor,
            Switch(harness, BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public void SwitchInvalidationPreservesLayoutAndRoutingAndUsesFriendlyConcreteNames()
    {
        var command = new ChangeBpmnElementTypeCommand(DocumentId, DocumentRevision.Zero,
            ElementId, BpmnSemanticTypes.UserTask);
        Assert.Equal(PipelineInvalidation.Projection | PipelineInvalidation.Scene,
            CommandPipelineInvalidation.Resolve(command));
        Assert.Equal("User Task", BpmnElementTypeSwitching.GetDisplayName(BpmnSemanticTypes.UserTask));
        Assert.Equal("Timer Catch Event", BpmnElementTypeSwitching.GetDisplayName(BpmnSemanticTypes.TimerCatchEvent));
        Assert.Equal("Event-Based Gateway", BpmnElementTypeSwitching.GetDisplayName(BpmnSemanticTypes.EventBasedGateway));
    }

    private static ChangeBpmnElementTypeCommand Switch(Harness harness, SemanticTypeId target) =>
        new(DocumentId, harness.Document.Revision, ElementId, target);

    private static SemanticTypeId CurrentType(Harness harness) =>
        harness.Document.SemanticModel.Elements.Single(element => element.Id == ElementId).TypeId;

    private static void AssertPreserved(DocumentSnapshot before, DocumentSnapshot after, SemanticElementSnapshot original)
    {
        var element = after.SemanticModel.Elements.Single(item => item.Id == original.Id);
        Assert.Equal(original.Properties, element.Properties);
        Assert.Equal(original.AttachedToElementId, element.AttachedToElementId);
        Assert.Equal(original.ContainmentKind, element.ContainmentKind);
        Assert.Equal(before.SemanticModel.GetScope(original.Id), after.SemanticModel.GetScope(original.Id));
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(before.SemanticModel.ScopeMemberships.AsEnumerable(), after.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.DoesNotContain(element.Properties.Keys, key => key.Contains("subtype", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before.SemanticModel.Elements.Where(item => item.Id != original.Id),
            after.SemanticModel.Elements.Where(item => item.Id != original.Id));
    }

    private static Harness Create(SemanticTypeId type)
    {
        var boundary = BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type);
        return Create([Element(ElementId, type, boundary ? OwnerId : null),
            Element(OwnerId, BpmnSemanticTypes.Task), Element(TargetId, BpmnSemanticTypes.TimerCatchEvent)],
            [Flow("flow", ElementId, TargetId)]);
    }

    private static Harness Create(SemanticElementSnapshot[] elements, SemanticRelationshipSnapshot[] flows)
    {
        var anchors = elements.ToDictionary(element => element.Id, _ => new List<ConnectorAnchor>());
        var visuals = new List<VisualStateSnapshot>();
        foreach (var flow in flows)
        {
            var source = new ConnectorAnchorId($"{flow.Id}:source");
            var target = new ConnectorAnchorId($"{flow.Id}:target");
            anchors[flow.SourceId].Add(new ConnectorAnchor(source, ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source, anchors[flow.SourceId].Count(anchor => anchor.Side == ConnectorAnchorSide.Right)));
            anchors[flow.TargetId].Add(new ConnectorAnchor(target, ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target, anchors[flow.TargetId].Count(anchor => anchor.Side == ConnectorAnchorSide.Left)));
            visuals.Add(new VisualStateSnapshot(new VisualStateId($"{flow.Id}:visual"), flow.Id,
                new PointD(0d, 0d), new SizeD(0d, 0d), VisualPlacementMode.Manual,
                route: [new PointD(220d, 140d), new PointD(400d, 140d)],
                sourceAnchorId: source, targetAnchorId: target));
        }

        var bounds = elements.Select((element, index) =>
        {
            var size = BpmnSemanticTypes.IsEvent(element.TypeId) ? new SizeD(36d, 36d)
                : BpmnGatewaySemanticTypes.IsGateway(element.TypeId) ? new SizeD(48d, 48d)
                : new SizeD(120d, 80d);
            return (element.Id, Bounds: new RectD(100d + index * 160d, 100d, size.Width, size.Height));
        }).ToDictionary(item => item.Id, item => item.Bounds);
        visuals.AddRange(elements.Select(element =>
        {
            var size = bounds[element.Id].Size;
            var attachment = element.AttachedToElementId is not null && BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(element.TypeId)
                ? new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d) : null;
            var position = attachment is null ? bounds[element.Id].TopLeft
                : attachment.ResolveBounds(bounds[element.AttachedToElementId!], size).TopLeft;
            return new VisualStateSnapshot(new VisualStateId($"{element.Id}:visual"), element.Id,
                position, size, VisualPlacementMode.Pinned, connectorAnchors: anchors[element.Id],
                boundaryAttachment: attachment);
        }));
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero, elements, flows),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var registration = BpmnPluginRegistration.N101;
        var policy = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var result = DocumentFactory.Create(snapshot, policy);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        var document = Assert.IsType<Document>(result.Document);
        var processor = new CommandProcessor(registration.CommandHandlers, registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies, connectorAnchorPolicyProvider: policy);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static SemanticElementSnapshot Element(SemanticElementId id, SemanticTypeId type, SemanticElementId? owner = null) =>
        new(id, type,
        [
            new(BpmnSemanticProperties.Code, PropertyValue.FromText("CODE")),
            new(BpmnSemanticProperties.Name, PropertyValue.FromText("Element name")),
            new(BpmnSemanticProperties.ElementNumber, PropertyValue.FromInteger(7)),
            new(BpmnSemanticProperties.Description, PropertyValue.FromText("Original description")),
        ], owner);

    private static SemanticRelationshipSnapshot Flow(string id, SemanticElementId source, SemanticElementId target) =>
        new(new SemanticElementId(id), BpmnSemanticTypes.SequenceFlow, source, target);

    private sealed record Harness(Document Document, CommandProcessor Processor, HistoryManager History);
}
