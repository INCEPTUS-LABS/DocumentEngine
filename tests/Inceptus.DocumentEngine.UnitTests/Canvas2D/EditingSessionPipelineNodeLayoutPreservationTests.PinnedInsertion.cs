using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class EditingSessionPipelineNodeLayoutPreservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclaredPinnedInsertionRetainsEveryOldGeometryAndExactUndoRedo(bool compound)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(configuration.CommandHandlers, configuration.CommandValidators,
            [events], configuration.HistoryPolicies, configuration.ConnectorAnchorPolicyProvider);
        var initial = await pipeline.RunFullAsync(composition.Document.CaptureSnapshot(),
            configuration.InitialEditorState, CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(initial.Artifacts);
        var initialGeometry = GeometryByVisualState(initialArtifacts);
        var moved = await processor.ExecuteAsync(composition.Document, new MoveVisualStateCommand(
            composition.Document.DocumentId, composition.Document.Revision, BpmnDemoPipeline.TaskVisualId,
            initialGeometry[BpmnDemoPipeline.TaskVisualId].Position + new VectorD(40d, 20d),
            VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted);
        var before = await pipeline.RunPreservingNodeLayoutAsync(composition.Document.CaptureSnapshot(),
            initialArtifacts, Assert.IsType<NodeGeometryPipelineImpact>(moved.CommittedEvent?.NodeGeometryImpact),
            configuration.InitialEditorState, CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var beforeGeometry = GeometryByVisualState(beforeArtifacts);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var history = new HistoryManager(composition.Document);
        var first = PinnedInsertionCommand(beforeDocument, "first", VisualPlacementMode.Pinned);
        var second = PinnedInsertionCommand(beforeDocument, "second", VisualPlacementMode.Pinned,
            position: new PointD(1300d, 700d));
        ICommand command = compound
            ? new CompoundDocumentCommand(beforeDocument.DocumentId, beforeDocument.Revision, [first, second])
            : first;

        Assert.True((await history.ExecuteAsync(processor, command)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var change = events.Events.Last();
        Assert.Equal(CommandPipelineInvalidation.WithoutNodeLayout, change.PipelineInvalidation);
        var impact = Assert.IsType<NodeGeometryPipelineImpact>(change.NodeGeometryImpact);
        var addedIds = compound ? new[] { first.VisualStateId, second.VisualStateId } : [first.VisualStateId];
        Assert.Equal(addedIds.OrderBy(id => id.Value), impact.ChangedVisualStateIds.OrderBy(id => id.Value));
        var currentDocument = composition.Document.CaptureSnapshot();
        var current = await pipeline.RunPreservingNodeLayoutAsync(currentDocument, beforeArtifacts, impact,
            configuration.InitialEditorState, CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);
        var currentGeometry = GeometryByVisualState(currentArtifacts);

        Assert.Equal(1, probe.InvocationCount);
        Assert.Equal(beforeGeometry.Count + addedIds.Length, currentGeometry.Count);
        foreach (var (id, geometry) in beforeGeometry)
        {
            Assert.Same(geometry, currentGeometry[id]);
        }

        foreach (var id in addedIds)
        {
            var visual = currentDocument.VisualModel.VisualStates.Single(candidate => candidate.Id == id);
            Assert.Equal(new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height),
                currentGeometry[id].Bounds);
        }

        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.Equal(beforeDocument.Revision.Increment(), currentDocument.Revision);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var undo = await pipeline.RunPreservingNodeLayoutAsync(composition.Document.CaptureSnapshot(),
            currentArtifacts, [beforeArtifacts, currentArtifacts],
            Assert.IsType<NodeGeometryPipelineImpact>(events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState, CancellationToken.None);
        var undoArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(undo.Artifacts);
        AssertAuthoritativeContentEqual(beforeDocument, composition.Document.CaptureSnapshot());
        foreach (var (id, geometry) in beforeGeometry)
        {
            Assert.Same(geometry, GeometryByVisualState(undoArtifacts)[id]);
        }

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var redo = await pipeline.RunPreservingNodeLayoutAsync(composition.Document.CaptureSnapshot(),
            undoArtifacts, [beforeArtifacts, currentArtifacts, undoArtifacts],
            Assert.IsType<NodeGeometryPipelineImpact>(events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState, CancellationToken.None);
        var redoArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(redo.Artifacts);
        AssertAuthoritativeContentEqual(currentDocument, composition.Document.CaptureSnapshot());
        foreach (var (id, geometry) in currentGeometry)
        {
            Assert.Same(geometry, GeometryByVisualState(redoArtifacts)[id]);
        }

        Assert.Equal(1, probe.InvocationCount);
    }

    [Theory]
    [InlineData("unproven")]
    [InlineData("wrong-id")]
    [InlineData("extra-id")]
    [InlineData("manual")]
    [InlineData("automatic")]
    [InlineData("existing-node-changed")]
    [InlineData("edge-changed")]
    [InlineData("same-revision")]
    [InlineData("wrong-scope")]
    public async Task UnprovenOrIncompatibleInsertionUsesSafeFullLayout(string scenario)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var original = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(original, configuration.InitialEditorState, CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var processor = Processor(configuration);
        var placement = scenario == "manual" ? VisualPlacementMode.Manual :
            scenario == "automatic" ? VisualPlacementMode.Automatic : VisualPlacementMode.Pinned;
        var scopeId = scenario == "wrong-scope" ? BpmnDemoPipeline.ProcessOrderScopeId : original.SemanticModel.RootScopeId;
        var command = PinnedInsertionCommand(original, scenario, placement, scopeId);
        var created = await processor.ExecuteAsync(composition.Document, command);
        Assert.True(created.IsCommitted);
        if (placement != VisualPlacementMode.Pinned)
        {
            Assert.Equal(CommandPipelineInvalidation.Full, created.CommittedEvent?.PipelineInvalidation);
        }

        if (scenario == "existing-node-changed")
        {
            Assert.True((await processor.ExecuteAsync(composition.Document, new MoveVisualStateCommand(
                original.DocumentId, composition.Document.Revision, BpmnDemoPipeline.TaskVisualId,
                new PointD(200d, 110d), VisualPlacementMode.Pinned))).IsCommitted);
        }

        if (scenario == "edge-changed")
        {
            var snapshot = composition.Document.CaptureSnapshot();
            var relationship = snapshot.SemanticModel.Relationships.Single(candidate => candidate.Id == BpmnDemoPipeline.ThirdSequenceFlowId);
            var visual = snapshot.VisualModel.VisualStates.Single(candidate => candidate.Id == BpmnDemoPipeline.ThirdSequenceFlowVisualId);
            Assert.True((await processor.ExecuteAsync(composition.Document, new DeleteBpmnSequenceFlowCommand(
                snapshot.DocumentId, snapshot.Revision, relationship.Id, visual.Id,
                relationship.SourceId, relationship.TargetId,
                Assert.IsType<ConnectorAnchorId>(visual.SourceAnchorId),
                Assert.IsType<ConnectorAnchorId>(visual.TargetAnchorId)))).IsCommitted);
        }

        var currentDocument = scenario == "same-revision" ? original : composition.Document.CaptureSnapshot();
        var impact = scenario switch
        {
            "unproven" => NodeGeometryPipelineImpact.PreserveAll,
            "wrong-id" => NodeGeometryPipelineImpact.ForChangedVisualStates([new VisualStateId("missing:insertion")]),
            "extra-id" => NodeGeometryPipelineImpact.ForChangedVisualStates([command.VisualStateId, BpmnDemoPipeline.TaskVisualId]),
            _ => NodeGeometryPipelineImpact.ForChangedVisualStates([command.VisualStateId]),
        };
        var current = await pipeline.RunPreservingNodeLayoutAsync(currentDocument, scopeId, beforeArtifacts,
            impact, configuration.InitialEditorState, CancellationToken.None);

        Assert.Equal(EditingSessionPipelineStatus.Succeeded, current.Status);
        Assert.Equal(2, probe.InvocationCount);
        var artifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);
        Assert.Equal(scopeId, artifacts.ScopeId);
        Assert.Equal(currentDocument.Revision, artifacts.LayoutResult.SourceRevision);
        Assert.Equal(artifacts.ProjectedGraph.NodeCount, artifacts.LayoutResult.NodeCount);
        if (scenario == "wrong-scope")
        {
            Assert.DoesNotContain(artifacts.ProjectedGraph.Nodes, node => node.Source.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        }
    }

    [Fact]
    public async Task AttachedBoundaryInsertionCannotBeCarriedAsAnIndependentPinnedNode()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(beforeDocument, configuration.InitialEditorState, CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var boundaryId = new VisualStateId("test:pinned-insertion:boundary:visual");
        var created = await Processor(configuration).ExecuteAsync(composition.Document,
            new CreateBpmnTimerBoundaryEventCommand(beforeDocument.DocumentId, beforeDocument.Revision,
                new SemanticElementId("test:pinned-insertion:boundary"), boundaryId, BpmnDemoPipeline.TaskId,
                BoundaryAttachmentSide.Bottom, 0.5d, GeometryByVisualState(beforeArtifacts)[BpmnDemoPipeline.TaskVisualId].Bounds,
                "Attached insertion"));
        Assert.True(created.IsCommitted);
        Assert.Equal(CommandPipelineInvalidation.Full, created.CommittedEvent?.PipelineInvalidation);

        var current = await pipeline.RunPreservingNodeLayoutAsync(composition.Document.CaptureSnapshot(), beforeArtifacts,
            NodeGeometryPipelineImpact.ForChangedVisualStates([boundaryId]), configuration.InitialEditorState, CancellationToken.None);

        Assert.Equal(EditingSessionPipelineStatus.Succeeded, current.Status);
        Assert.Equal(2, probe.InvocationCount);
        var geometry = GeometryByVisualState(Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts));
        Assert.Equal(new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d)
            .ResolveBounds(geometry[BpmnDemoPipeline.TaskVisualId].Bounds, new SizeD(36d, 36d)), geometry[boundaryId].Bounds);
    }

    private static CreateBpmnTaskCommand PinnedInsertionCommand(DocumentSnapshot document, string suffix,
        VisualPlacementMode placement, DocumentScopeId? scopeId = null, PointD? position = null) =>
        new(document.DocumentId, document.Revision,
            new SemanticElementId($"test:pinned-insertion:{suffix}"),
            new VisualStateId($"test:pinned-insertion:{suffix}:visual"),
            position ?? new PointD(1100d, 300d), new SizeD(160d, 84d), $"INSERT-{suffix}",
            "Pinned insertion", 2000, placement, targetScopeId: scopeId ?? document.SemanticModel.RootScopeId);
}
