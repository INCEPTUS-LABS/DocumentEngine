using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PublishedMessageExecutionPolicyTests
{
    [Theory]
    [InlineData("BPMN.MessageThrowEvent", "BPMN.MessageCatchEvent", true)]
    [InlineData("BPMN.MessageThrowEvent", "BPMN.ReceiveTask", true)]
    [InlineData("BPMN.MessageThrowEvent", "BPMN.Task", true)]
    [InlineData("BPMN.Task", "BPMN.Task", true)]
    [InlineData("BPMN.SendTask", "BPMN.MessageCatchEvent", true)]
    [InlineData("BPMN.Task", "BPMN.ReceiveTask", true)]
    public void MessageExecutionUsesCanonicalEndpointTypesAndWarnsForVisualOnlyCases(string source, string target, bool executable)
    {
        var document = Snapshot(new(source), new(target));
        var result = Describe(document);
        Assert.Equal(executable, Assert.Single(result.Metadata!.CommunicationRoutes).CanDeliver);
        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Equal(executable ? 0 : 1, result.Diagnostics.Length);
        if (!executable)
        {
            Assert.Equal("PUBLISH_MESSAGE_ENDPOINT_UNSUPPORTED", result.Diagnostics[0].Code);
            Assert.Equal("message", result.Diagnostics[0].SourceIdentity);
            Assert.Single(result.Metadata.Warnings);
        }
        Assert.Equal("source", Assert.Single(result.Metadata.SenderIds));
        if (target == "BPMN.MessageCatchEvent") Assert.Empty(result.Metadata.ReceiverIds);
        else Assert.Equal("target", Assert.Single(result.Metadata.ReceiverIds));
        Assert.Equal(DocumentRevision.Zero, document.Revision);
    }

    [Theory]
    [InlineData("pool-a", "target")]
    [InlineData("source", "pool-b")]
    [InlineData("source", "boundary")]
    public void ContainerEndpointsAndIncomingBoundaryMessageRemainVisualOnly(string source, string target)
    {
        var result = Describe(Snapshot(BpmnSemanticTypes.Task, BpmnSemanticTypes.ReceiveTask,
            BpmnSemanticTypes.MessageBoundaryEvent, messageSource: source, messageTarget: target));
        Assert.False(Assert.Single(result.Metadata!.CommunicationRoutes).CanDeliver);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PUBLISH_MESSAGE_ENDPOINT_UNSUPPORTED", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("message", diagnostic.SourceIdentity);
        Assert.Single(result.Metadata.Warnings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PT0S")]
    [InlineData("-PT1S")]
    [InlineData("P1M")]
    [InlineData("P1Y")]
    [InlineData("R3/PT5S")]
    [InlineData("2026-10-16T00:00:00Z")]
    [InlineData("five seconds")]
    [InlineData("PT999999999999999999999S")]
    public void MissingOrUnsupportedBoundaryTimerUsesThreeSecondPresentationDelayWithoutChangingModel(string? definition)
    {
        var document = Snapshot(BpmnSemanticTypes.MessageThrowEvent, BpmnSemanticTypes.Task,
            BpmnSemanticTypes.TimerBoundaryEvent, definition);
        var result = Describe(document);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3000, Assert.Single(result.Metadata!.AttachedTriggers).DelayMs);
        var properties = document.SemanticModel.Elements.Single(element => element.Id.Value == "boundary").Properties;
        Assert.Equal(definition is not null, properties.TryGetValue(BpmnSemanticProperties.TimerDefinition, out var savedTimer));
        if (definition is not null) Assert.Equal(definition, savedTimer?.TextValue);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
    }

    [Theory]
    [InlineData("PT0.125S", 1000)]
    [InlineData("PT1S", 1000)]
    [InlineData("PT2.5S", 2500)]
    [InlineData("PT5S", 5000)]
    [InlineData("PT5M", 5000)]
    [InlineData("P1DT2H", 5000)]
    public void BoundaryPresentationDurationIsClampedAndPreservesModelAttachmentAndInterruption(string definition, double milliseconds)
    {
        foreach (var interrupting in new[] { false, true })
        {
            var document = Snapshot(BpmnSemanticTypes.MessageThrowEvent, BpmnSemanticTypes.Task,
                BpmnSemanticTypes.TimerBoundaryEvent, definition, interrupting);
            var result = Describe(document);
            Assert.Empty(result.Diagnostics);
            var trigger = Assert.Single(result.Metadata!.AttachedTriggers);
            Assert.Equal(new PublishedAttachedTrigger("boundary", "target", PublishedTriggerKind.IdleTimer, interrupting, milliseconds), trigger);
            Assert.Equal(definition, document.SemanticModel.Elements.Single(element => element.Id.Value == "boundary")
                .Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
        }
    }

    [Theory]
    [InlineData("BPMN.SignalBoundaryEvent", PublishedTriggerKind.IdleSignal)]
    [InlineData("BPMN.MessageBoundaryEvent", PublishedTriggerKind.Completion)]
    public void BoundaryRulesUseCanonicalAttachmentWithoutChangingMessageEndpointDirection(string type, PublishedTriggerKind kind)
    {
        var result = Describe(Snapshot(BpmnSemanticTypes.MessageThrowEvent, BpmnSemanticTypes.Task, new(type)));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(kind, Assert.Single(result.Metadata!.AttachedTriggers).Kind);
        Assert.False(BpmnMessageFlowSemantics.CanBeSource(new(type)));
    }

    [Fact]
    public void CanonicalMessageEndpointFailuresAreNotWeakenedByPublishingSupport()
    {
        var result = Describe(Snapshot(BpmnSemanticTypes.MessageThrowEvent, BpmnSemanticTypes.ExclusiveGateway));
        Assert.Contains(result.Diagnostics, item => item.Code == BpmnMessageFlowSemantics.TargetInvalid && item.Severity == DiagnosticSeverity.Error);
        var invalidSender = Describe(Snapshot(BpmnSemanticTypes.ReceiveTask, BpmnSemanticTypes.Task));
        Assert.Contains(invalidSender.Diagnostics, item => item.Code == BpmnMessageFlowSemantics.SourceInvalid && item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void LegacyPureSequenceDocumentHasNoNewExecutionMetadata()
    {
        var document = Snapshot(BpmnSemanticTypes.Task, BpmnSemanticTypes.Task);
        var result = new BpmnPublishedTokenRoleClassifier().DescribeExecution(document,
            document.SemanticModel.Elements.Select(element => element.Id).ToImmutableHashSet(), []);
        Assert.Null(result.Metadata);
        Assert.Empty(result.Diagnostics);
    }

    private static PublishedExecutionDescription Describe(DocumentSnapshot document) =>
        new BpmnPublishedTokenRoleClassifier().DescribeExecution(document,
            document.SemanticModel.Elements.Select(element => element.Id).ToImmutableHashSet(),
            document.SemanticModel.Relationships.Select(flow => flow.Id).ToImmutableHashSet());

    private static DocumentSnapshot Snapshot(SemanticTypeId source, SemanticTypeId target,
        SemanticTypeId? boundary = null, string? timer = null, bool interrupting = true,
        string messageSource = "source", string messageTarget = "target")
    {
        var id = new DocumentId("publish:messages");
        var elements = new List<SemanticElementSnapshot>
        {
            new(new("pool-a"), OrganizationalSemanticTypes.Pool), new(new("pool-b"), OrganizationalSemanticTypes.Pool),
            new(new("source"), source), new(new("target"), target),
        };
        if (boundary is not null)
        {
            var properties = new List<KeyValuePair<string, PropertyValue>>
            { new(BpmnSemanticProperties.CancelActivity, PropertyValue.FromBoolean(interrupting)) };
            if (timer is not null) properties.Add(new(BpmnSemanticProperties.TimerDefinition, PropertyValue.FromText(timer)));
            elements.Add(new(new("boundary"), boundary, properties, new("target")));
        }
        return new(new SemanticModelSnapshot(id, DocumentRevision.Zero, elements,
            [new(new("message"), BpmnSemanticTypes.MessageFlow, new(messageSource), new(messageTarget))],
            modelProfiles: new([OrganizationalModelProfile.Id]), profileAssignments:
            [new(OrganizationalModelProfile.Id, new("source"), new("pool-a")), new(OrganizationalModelProfile.Id, new("target"), new("pool-b"))]),
            new VisualModelSnapshot(id, DocumentRevision.Zero, elements.Where(element => element.Id.Value is "source" or "target" or "boundary")
                .Select(element => new VisualStateSnapshot(new(element.Id.Value + ":visual"), element.Id, new(0, 0), new(40, 40),
                    VisualPlacementMode.Manual, boundaryAttachment: element.AttachedToElementId is null ? null : new(BoundaryAttachmentSide.Bottom, 0.5)))),
            new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
    }
}
