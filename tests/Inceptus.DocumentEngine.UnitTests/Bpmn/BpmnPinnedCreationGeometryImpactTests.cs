using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnPinnedCreationGeometryImpactTests
{
    private static readonly DocumentId DocumentId = new("test:pinned-creation-impact");
    private static readonly SemanticElementId ElementId = new("test:pinned-creation-impact:element");
    private static readonly VisualStateId VisualId = new("test:pinned-creation-impact:visual");

    public static TheoryData<string, VisualPlacementMode> CreationModes
    {
        get
        {
            var data = new TheoryData<string, VisualPlacementMode>();
            foreach (var kind in new[] { "task", "gateway", "event", "subprocess" })
            {
                foreach (var mode in Enum.GetValues<VisualPlacementMode>())
                {
                    data.Add(kind, mode);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CreationModes))]
    public async Task PinnedCreationAndReplayDeclareOnlyExactCreatedGeometry(
        string kind,
        VisualPlacementMode mode)
    {
        var registration = BpmnPluginRegistration.N100;
        var provider = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            DocumentId, connectorAnchorPolicyProvider: provider).Document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: [subscriber],
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        var history = new HistoryManager(document);
        var command = Creation(kind, mode);
        var expectedInvalidation = mode == VisualPlacementMode.Pinned
            ? CommandPipelineInvalidation.WithoutNodeLayout
            : CommandPipelineInvalidation.Full;
        Assert.Equal(expectedInvalidation, CommandPipelineInvalidation.Resolve(command));

        Assert.True((await history.ExecuteAsync(processor, command)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        var committed = document.CaptureSnapshot();
        var creationEvent = Assert.Single(subscriber.Events);
        Assert.Equal(expectedInvalidation, creationEvent.PipelineInvalidation);
        Assert.Equal(
            mode == VisualPlacementMode.Pinned
                ? NodeGeometryPipelineImpact.ForChangedVisualStates([VisualId])
                : null,
            creationEvent.NodeGeometryImpact);
        var visual = Assert.Single(committed.VisualModel.VisualStates);
        Assert.Equal(command.Position, visual.Position);
        Assert.Equal(command.Size, visual.Size);
        Assert.Equal(mode, visual.PlacementMode);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Assert.True((await history.UndoAsync(processor)).IsCommitted);
            await CommandProcessor.WaitForEventDispatchIdleAsync(document);
            var undoEvent = subscriber.Events.Last();
            Assert.Equal(expectedInvalidation, undoEvent.PipelineInvalidation);
            Assert.Equal(
                mode == VisualPlacementMode.Pinned
                    ? NodeGeometryPipelineImpact.ForRemovedVisualStates([VisualId])
                    : null,
                undoEvent.NodeGeometryImpact);
            Assert.Empty(document.CaptureSnapshot().VisualModel.VisualStates);

            Assert.True((await history.RedoAsync(processor)).IsCommitted);
            await CommandProcessor.WaitForEventDispatchIdleAsync(document);
            var redoEvent = subscriber.Events.Last();
            Assert.Equal(expectedInvalidation, redoEvent.PipelineInvalidation);
            Assert.Equal(
                mode == VisualPlacementMode.Pinned
                    ? NodeGeometryPipelineImpact.ForHistoricalRestoration(
                        [VisualId], committed.Revision)
                    : null,
                redoEvent.NodeGeometryImpact);
            var redone = document.CaptureSnapshot();
            Assert.Equal(committed.SemanticModel.Elements.AsEnumerable(),
                redone.SemanticModel.Elements.AsEnumerable());
            Assert.Equal(committed.SemanticModel.NestedScopes.AsEnumerable(),
                redone.SemanticModel.NestedScopes.AsEnumerable());
            Assert.Equal(committed.SemanticModel.ScopeMemberships.AsEnumerable(),
                redone.SemanticModel.ScopeMemberships.AsEnumerable());
            Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(),
                redone.VisualModel.VisualStates.AsEnumerable());
        }

        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.Equal(new DocumentRevision(5), document.Revision);
        Assert.Equal(5, subscriber.Events.Count);
    }

    [Fact]
    public void AttachedBoundaryCreationRetainsFullInvalidation()
    {
        var command = new CreateBpmnTimerBoundaryEventCommand(
            DocumentId, DocumentRevision.Zero, ElementId, VisualId,
            new SemanticElementId("test:boundary-owner"),
            BoundaryAttachmentSide.Right, 0.5d, new RectD(80d, 60d, 120d, 80d),
            "Timer");

        Assert.Equal(CommandPipelineInvalidation.Full,
            CommandPipelineInvalidation.Resolve(command));
    }

    private static BpmnElementCreationCommand Creation(string kind, VisualPlacementMode mode)
    {
        var position = new PointD(240d, 180d);
        var size = new SizeD(120d, 80d);
        return kind switch
        {
            "task" => new CreateBpmnTaskCommand(
                DocumentId, DocumentRevision.Zero, ElementId, VisualId,
                position, size, "TASK", "Task", 1, mode),
            "gateway" => new CreateBpmnExclusiveGatewayCommand(
                DocumentId, DocumentRevision.Zero, ElementId, VisualId,
                position, new SizeD(48d, 48d), "GATEWAY", "Gateway", mode),
            "event" => new CreateBpmnTimerCatchEventCommand(
                DocumentId, DocumentRevision.Zero, ElementId, VisualId,
                position, new SizeD(36d, 36d), "Timer", mode),
            "subprocess" => new CreateBpmnSubProcessCommand(
                DocumentId, DocumentRevision.Zero, ElementId, VisualId,
                new DocumentScopeId(DocumentId.Value),
                new DocumentScopeId("test:pinned-creation-impact:child"),
                position, size, "SUBPROCESS", "SubProcess", mode),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
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
