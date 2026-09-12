using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class EditingSessionPipelineNodeLayoutPreservationTests
{
    [Fact]
    public async Task ConnectorOnlyRunCarriesLayoutAndRebuildsCurrentProjectionRoutingAndScene()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var pipeline = new EditingSessionPipeline(composition.Configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            composition.Configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var processor = Processor(composition.Configuration);

        var committed = await processor.ExecuteAsync(
            composition.Document,
            new UpdateConnectionRouteCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                BpmnDemoPipeline.ThirdSequenceFlowVisualId,
                [new PointD(410d, 220d), new PointD(470d, 195d), new PointD(520d, 170d)]));
        Assert.True(committed.IsCommitted);
        var currentDocument = composition.Document.CaptureSnapshot();

        var current = await pipeline.RunPreservingNodeLayoutAsync(
            currentDocument,
            beforeArtifacts,
            NodeGeometryPipelineImpact.PreserveAll,
            composition.Configuration.InitialEditorState,
            CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);

        Assert.Same(
            beforeArtifacts.LayoutResult.Computation,
            currentArtifacts.LayoutResult.Computation);
        Assert.NotSame(beforeArtifacts.ProjectedGraph, currentArtifacts.ProjectedGraph);
        Assert.NotSame(
            beforeArtifacts.RoutingResult.Computation,
            currentArtifacts.RoutingResult.Computation);
        Assert.Equal(currentDocument.Revision, currentArtifacts.ProjectedGraph.SourceRevision);
        Assert.Equal(currentDocument.Revision, currentArtifacts.LayoutResult.SourceRevision);
        Assert.Equal(currentDocument.Revision, currentArtifacts.RoutingResult.SourceRevision);
        Assert.Equal(currentDocument.Revision, current.Scene?.SourceRevision);
    }

    [Fact]
    public async Task CompatibleScopeRunCarriesLayoutAcrossInactiveScopeFullInvalidation()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var rootScopeId = beforeDocument.SemanticModel.RootScopeId;
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            rootScopeId,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);

        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new CreateBpmnTaskCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                new SemanticElementId("test:n82:inactive-scope:task"),
                new VisualStateId("test:n82:inactive-scope:task:visual"),
                new PointD(420d, 260d),
                new SizeD(140d, 82d),
                "N82-INACTIVE",
                "Inactive-scope task",
                995,
                VisualPlacementMode.Manual,
                targetScopeId: BpmnDemoPipeline.ProcessOrderScopeId));
        Assert.True(committed.IsCommitted);

        var current = await pipeline.RunPreservingScopeLayoutIfCompatibleAsync(
            composition.Document.CaptureSnapshot(),
            rootScopeId,
            beforeArtifacts,
            configuration.InitialEditorState,
            CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);

        Assert.Equal(1, probe.InvocationCount);
        Assert.Same(
            beforeArtifacts.LayoutResult.Computation,
            currentArtifacts.LayoutResult.Computation);
        Assert.True(beforeArtifacts.ProjectedGraph.Nodes.AsSpan().SequenceEqual(
            currentArtifacts.ProjectedGraph.Nodes.AsSpan()));
        Assert.True(beforeArtifacts.ProjectedGraph.Edges.AsSpan().SequenceEqual(
            currentArtifacts.ProjectedGraph.Edges.AsSpan()));
    }

    [Fact]
    public async Task CompatibleScopeRunRelayoutsWhenActiveScopeGraphChanges()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var rootScopeId = beforeDocument.SemanticModel.RootScopeId;
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            rootScopeId,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);

        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new CreateBpmnTaskCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                new SemanticElementId("test:n82:active-scope:task"),
                new VisualStateId("test:n82:active-scope:task:visual"),
                new PointD(1720d, 860d),
                new SizeD(140d, 82d),
                "N82-ACTIVE",
                "Active-scope task",
                996,
                VisualPlacementMode.Manual));
        Assert.True(committed.IsCommitted);

        var current = await pipeline.RunPreservingScopeLayoutIfCompatibleAsync(
            composition.Document.CaptureSnapshot(),
            rootScopeId,
            beforeArtifacts,
            configuration.InitialEditorState,
            CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);

        Assert.Equal(2, probe.InvocationCount);
        Assert.NotSame(
            beforeArtifacts.LayoutResult.Computation,
            currentArtifacts.LayoutResult.Computation);
        Assert.Equal(
            beforeArtifacts.ProjectedGraph.NodeCount + 1,
            currentArtifacts.ProjectedGraph.NodeCount);
    }

    [Fact]
    public async Task PreservingRunFallsBackToLayoutWhenProjectedNodeSetChanges()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var pipeline = new EditingSessionPipeline(composition.Configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            composition.Configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);

        var committed = await Processor(composition.Configuration).ExecuteAsync(
            composition.Document,
            new CreateBpmnTaskCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                new SemanticElementId("test:n311:fallback:task"),
                new VisualStateId("test:n311:fallback:task:visual"),
                new PointD(1800d, 900d),
                new SizeD(160d, 84d),
                "N311-FALLBACK",
                "Fallback",
                999));
        Assert.True(committed.IsCommitted);
        var currentDocument = composition.Document.CaptureSnapshot();

        var current = await pipeline.RunPreservingNodeLayoutAsync(
            currentDocument,
            beforeArtifacts,
            NodeGeometryPipelineImpact.PreserveAll,
            composition.Configuration.InitialEditorState,
            CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);

        Assert.NotSame(
            beforeArtifacts.LayoutResult.Computation,
            currentArtifacts.LayoutResult.Computation);
        Assert.Equal(
            beforeArtifacts.LayoutResult.NodeCount + 1,
            currentArtifacts.LayoutResult.NodeCount);
        Assert.Equal(
            currentArtifacts.ProjectedGraph.NodeCount,
            currentArtifacts.LayoutResult.NodeCount);
        Assert.Equal(currentDocument.Revision, currentArtifacts.LayoutResult.SourceRevision);
    }

    [Fact]
    public async Task ExplicitMovePatchesOnlyDeclaredNodeAndSkipsLayoutAlgorithm()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var beforeGeometry = GeometryByVisualState(beforeArtifacts);
        var targetPosition = beforeGeometry[BpmnDemoPipeline.TaskVisualId].Position +
            new VectorD(55d, -35d);

        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new MoveVisualStateCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                BpmnDemoPipeline.TaskVisualId,
                targetPosition,
                VisualPlacementMode.Pinned));
        var impact = Assert.IsType<NodeGeometryPipelineImpact>(
            committed.CommittedEvent?.NodeGeometryImpact);
        var current = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            beforeArtifacts,
            impact,
            configuration.InitialEditorState,
            CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts);
        var currentGeometry = GeometryByVisualState(currentArtifacts);

        Assert.Equal(1, probe.InvocationCount);
        Assert.Equal(targetPosition, currentGeometry[BpmnDemoPipeline.TaskVisualId].Position);
        AssertGeometryUnchangedExcept(
            beforeGeometry,
            currentGeometry,
            BpmnDemoPipeline.TaskVisualId);
        Assert.NotSame(
            beforeArtifacts.RoutingResult.Computation,
            currentArtifacts.RoutingResult.Computation);
    }

    [Fact]
    public async Task ExplicitMultiMovePatchesEveryDeclaredNodeAndNoOthers()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var beforeGeometry = GeometryByVisualState(beforeArtifacts);
        var approvedPosition = beforeGeometry[BpmnDemoPipeline.ApprovedTaskVisualId].Position +
            new VectorD(35d, 25d);
        var rejectedPosition = beforeGeometry[BpmnDemoPipeline.RejectedTaskVisualId].Position +
            new VectorD(35d, 25d);

        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new MoveVisualStatesCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                [
                    new VisualStateMove(
                        BpmnDemoPipeline.ApprovedTaskVisualId,
                        approvedPosition,
                        VisualPlacementMode.Pinned),
                    new VisualStateMove(
                        BpmnDemoPipeline.RejectedTaskVisualId,
                        rejectedPosition,
                        VisualPlacementMode.Pinned),
                ]));
        var current = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            beforeArtifacts,
            Assert.IsType<NodeGeometryPipelineImpact>(
                committed.CommittedEvent?.NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);
        var currentGeometry = GeometryByVisualState(
            Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts));

        Assert.Equal(1, probe.InvocationCount);
        Assert.Equal(
            approvedPosition,
            currentGeometry[BpmnDemoPipeline.ApprovedTaskVisualId].Position);
        Assert.Equal(
            rejectedPosition,
            currentGeometry[BpmnDemoPipeline.RejectedTaskVisualId].Position);
        AssertGeometryUnchangedExcept(
            beforeGeometry,
            currentGeometry,
            BpmnDemoPipeline.ApprovedTaskVisualId,
            BpmnDemoPipeline.RejectedTaskVisualId);
    }

    [Fact]
    public async Task ExplicitResizePatchesAuthoritativeBoundsAndPreservesOtherNodes()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var beforeGeometry = GeometryByVisualState(beforeArtifacts);
        var original = beforeGeometry[BpmnDemoPipeline.TaskVisualId].Bounds;
        var targetBounds = new RectD(
            original.X - 10d,
            original.Y - 5d,
            original.Width + 30d,
            original.Height + 20d);

        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new ResizeVisualStateCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                BpmnDemoPipeline.TaskVisualId,
                targetBounds,
                VisualPlacementMode.Pinned));
        var current = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            beforeArtifacts,
            Assert.IsType<NodeGeometryPipelineImpact>(
                committed.CommittedEvent?.NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);
        var currentGeometry = GeometryByVisualState(
            Assert.IsType<EditingSessionPipelineArtifacts>(current.Artifacts));

        Assert.Equal(1, probe.InvocationCount);
        Assert.Equal(targetBounds, currentGeometry[BpmnDemoPipeline.TaskVisualId].Bounds);
        AssertGeometryUnchangedExcept(
            beforeGeometry,
            currentGeometry,
            BpmnDemoPipeline.TaskVisualId);
    }

    [Fact]
    public async Task MissingDeclaredVisualStateFallsBackToSafeFullLayout()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new UpdateConnectionRouteCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                BpmnDemoPipeline.ThirdSequenceFlowVisualId,
                [new PointD(410d, 220d), new PointD(470d, 195d)]));
        Assert.True(committed.IsCommitted);

        var current = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            beforeArtifacts,
            NodeGeometryPipelineImpact.ForChangedVisualStates(
                [new VisualStateId("test:missing-visual-state")]),
            configuration.InitialEditorState,
            CancellationToken.None);

        Assert.Equal(2, probe.InvocationCount);
        Assert.True(
            current.Status == EditingSessionPipelineStatus.Succeeded,
            string.Join(Environment.NewLine, current.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    [Fact]
    public async Task BoundaryAttachmentEditUsesRetainedAutomaticOwnerGeometryWithoutRelayout()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var processor = Processor(configuration);
        var ownerBeforeCreation = composition.Document.CaptureSnapshot()
            .VisualModel.VisualStates.Single(visual =>
                visual.Id == BpmnDemoPipeline.TaskVisualId);
        var automaticOwner = await processor.ExecuteAsync(
            composition.Document,
            new MoveVisualStateCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                ownerBeforeCreation.Id,
                ownerBeforeCreation.Position,
                VisualPlacementMode.Automatic));
        Assert.True(automaticOwner.IsCommitted);
        ownerBeforeCreation = composition.Document.CaptureSnapshot()
            .VisualModel.VisualStates.Single(visual =>
                visual.Id == BpmnDemoPipeline.TaskVisualId);
        Assert.Equal(VisualPlacementMode.Automatic, ownerBeforeCreation.PlacementMode);
        var persistentOwnerBounds = new RectD(
            ownerBeforeCreation.Position.X,
            ownerBeforeCreation.Position.Y,
            ownerBeforeCreation.Size.Width,
            ownerBeforeCreation.Size.Height);
        var boundaryId = new SemanticElementId(
            "test:n90:automatic-owner:boundary");
        var boundaryVisualId = new VisualStateId(
            "test:n90:automatic-owner:boundary:visual");
        var created = await processor.ExecuteAsync(
            composition.Document,
            new CreateBpmnTimerBoundaryEventCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                boundaryId,
                boundaryVisualId,
                BpmnDemoPipeline.TaskId,
                BoundaryAttachmentSide.Bottom,
                0.5d,
                persistentOwnerBounds,
                "Automatic owner timeout"));
        Assert.True(created.IsCommitted);

        var initialDocument = composition.Document.CaptureSnapshot();
        var initial = await pipeline.RunFullAsync(
            initialDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            initial.Artifacts);
        var initialGeometry = GeometryByVisualState(initialArtifacts);
        var effectiveOwnerBounds = initialGeometry[BpmnDemoPipeline.TaskVisualId].Bounds;
        Assert.NotEqual(persistentOwnerBounds, effectiveOwnerBounds);
        Assert.Equal(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d)
                .ResolveBounds(effectiveOwnerBounds, new SizeD(36d, 36d)),
            initialGeometry[boundaryVisualId].Bounds);

        var targetAttachment = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.25d);
        var updated = await processor.ExecuteAsync(
            composition.Document,
            new UpdateBoundaryAttachmentCommand(
                initialDocument.DocumentId,
                initialDocument.Revision,
                boundaryVisualId,
                targetAttachment,
                effectiveOwnerBounds));
        Assert.True(updated.IsCommitted);
        var impact = Assert.IsType<NodeGeometryPipelineImpact>(
            updated.CommittedEvent?.NodeGeometryImpact);

        var current = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            initialArtifacts,
            impact,
            configuration.InitialEditorState,
            CancellationToken.None);
        Assert.True(
            current.Status == EditingSessionPipelineStatus.Succeeded,
            string.Join(Environment.NewLine, current.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            current.Artifacts);
        var currentGeometry = GeometryByVisualState(currentArtifacts);
        var expectedBoundaryBounds = targetAttachment.ResolveBounds(
            effectiveOwnerBounds,
            new SizeD(36d, 36d));

        Assert.Equal(1, probe.InvocationCount);
        Assert.Same(
            initialGeometry[BpmnDemoPipeline.TaskVisualId],
            currentGeometry[BpmnDemoPipeline.TaskVisualId]);
        Assert.Equal(effectiveOwnerBounds,
            currentGeometry[BpmnDemoPipeline.TaskVisualId].Bounds);
        Assert.Equal(expectedBoundaryBounds, currentGeometry[boundaryVisualId].Bounds);
        var persistentOwner = composition.Document.CaptureSnapshot()
            .VisualModel.VisualStates.Single(visual =>
                visual.Id == BpmnDemoPipeline.TaskVisualId);
        Assert.Equal(persistentOwnerBounds.TopLeft, persistentOwner.Position);
        Assert.Equal(persistentOwnerBounds.Size, persistentOwner.Size);
        Assert.Equal(VisualPlacementMode.Automatic, persistentOwner.PlacementMode);
    }

    [Fact]
    public async Task BoundaryCreationOnEffectiveAutomaticOwnerBuildsAValidFullScene()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var pipeline = new EditingSessionPipeline(composition.Configuration);
        var processor = Processor(composition.Configuration);
        var originalOwner = composition.Document.CaptureSnapshot()
            .VisualModel.VisualStates.Single(visual =>
                visual.Id == BpmnDemoPipeline.ApprovedTaskVisualId);
        var automaticOwner = await processor.ExecuteAsync(
            composition.Document,
            new MoveVisualStateCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                originalOwner.Id,
                originalOwner.Position,
                VisualPlacementMode.Automatic));
        Assert.True(automaticOwner.IsCommitted);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            composition.Configuration.InitialEditorState,
            CancellationToken.None);
        Assert.Equal(EditingSessionPipelineStatus.Succeeded, before.Status);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            before.Artifacts);
        var beforeGeometry = GeometryByVisualState(beforeArtifacts);
        var ownerVisual = beforeDocument.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ApprovedTaskVisualId);
        Assert.Equal(VisualPlacementMode.Automatic, ownerVisual.PlacementMode);
        var persistentOwnerBounds = new RectD(
            ownerVisual.Position.X,
            ownerVisual.Position.Y,
            ownerVisual.Size.Width,
            ownerVisual.Size.Height);
        var effectiveOwnerBounds = beforeGeometry[
            BpmnDemoPipeline.ApprovedTaskVisualId].Bounds;
        Assert.NotEqual(persistentOwnerBounds, effectiveOwnerBounds);
        var boundaryId = new SemanticElementId(
            "test:n90:automatic-owner:create-boundary");
        var boundaryVisualId = new VisualStateId(
            "test:n90:automatic-owner:create-boundary:visual");
        var attachment = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.5d);
        var expectedBounds = attachment.ResolveBounds(
            effectiveOwnerBounds,
            new SizeD(36d, 36d));
        var beforeCreation = composition.Document.CaptureSnapshot();
        var history = new HistoryManager(composition.Document);

        var created = await history.ExecuteAsync(
            processor,
            new CreateBpmnTimerBoundaryEventCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                boundaryId,
                boundaryVisualId,
                BpmnDemoPipeline.ApprovedTaskId,
                attachment.Side,
                attachment.PositionOnSide,
                effectiveOwnerBounds,
                "Approved task timeout"));
        Assert.True(created.IsCommitted);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        var committedCreation = composition.Document.CaptureSnapshot();
        var persistedBoundary = committedCreation
            .VisualModel.VisualStates.Single(visual => visual.Id == boundaryVisualId);
        Assert.Equal(expectedBounds.TopLeft, persistedBoundary.Position);

        var rebuilt = await pipeline.RunFullAsync(
            composition.Document.CaptureSnapshot(),
            composition.Configuration.InitialEditorState,
            CancellationToken.None);
        Assert.Equal(EditingSessionPipelineStatus.Succeeded, rebuilt.Status);
        Assert.NotNull(rebuilt.Scene);
        Assert.Empty(rebuilt.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));
        var rebuiltArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            rebuilt.Artifacts);
        var rebuiltGeometry = GeometryByVisualState(rebuiltArtifacts);
        Assert.Equal(
            attachment.ResolveBounds(
                rebuiltGeometry[BpmnDemoPipeline.ApprovedTaskVisualId].Bounds,
                new SizeD(36d, 36d)),
            rebuiltGeometry[boundaryVisualId].Bounds);

        var undo = await history.UndoAsync(processor);
        Assert.True(undo.IsCommitted);
        AssertAuthoritativeContentEqual(
            beforeCreation,
            composition.Document.CaptureSnapshot());
        var redo = await history.RedoAsync(processor);
        Assert.True(redo.IsCommitted);
        AssertAuthoritativeContentEqual(
            committedCreation,
            composition.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task UndoRestoresHistoricalAutomaticGeometryWithoutInvokingLayout()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var initialDocument = composition.Document.CaptureSnapshot();
        var initial = await pipeline.RunFullAsync(
            initialDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            initial.Artifacts);
        var initialGeometry = GeometryByVisualState(initialArtifacts);
        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            [events],
            configuration.HistoryPolicies,
            configuration.ConnectorAnchorPolicyProvider);
        var history = new HistoryManager(composition.Document);
        var movedPosition = initialGeometry[BpmnDemoPipeline.TaskVisualId].Position +
            new VectorD(20d, 45d);

        var moved = await history.ExecuteAsync(
            processor,
            new MoveVisualStateCommand(
                initialDocument.DocumentId,
                initialDocument.Revision,
                BpmnDemoPipeline.TaskVisualId,
                movedPosition,
                VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var moveImpact = Assert.IsType<NodeGeometryPipelineImpact>(
            events.Events.Last().NodeGeometryImpact);
        var movedPipeline = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            initialArtifacts,
            moveImpact,
            configuration.InitialEditorState,
            CancellationToken.None);
        Assert.True(
            movedPipeline.Status == EditingSessionPipelineStatus.Succeeded,
            string.Join(Environment.NewLine, movedPipeline.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var movedArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            movedPipeline.Artifacts);

        var undone = await history.UndoAsync(processor);
        Assert.True(undone.IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var undoImpact = Assert.IsType<NodeGeometryPipelineImpact>(
            events.Events.Last().NodeGeometryImpact);
        var undonePipeline = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            movedArtifacts,
            [initialArtifacts, movedArtifacts],
            undoImpact,
            configuration.InitialEditorState,
            CancellationToken.None);
        Assert.True(
            undonePipeline.Status == EditingSessionPipelineStatus.Succeeded,
            string.Join(Environment.NewLine, undonePipeline.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var undoneArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            undonePipeline.Artifacts);

        Assert.Equal(1, probe.InvocationCount);
        Assert.Same(
            initialArtifacts.LayoutResult.Computation,
            undoneArtifacts.LayoutResult.Computation);
        Assert.Equal(
            initialGeometry[BpmnDemoPipeline.TaskVisualId].Bounds,
            GeometryByVisualState(undoneArtifacts)[BpmnDemoPipeline.TaskVisualId].Bounds);
    }

    [Fact]
    public async Task RemovedNodeImpactShrinksLayoutAndPreservesEverySurvivorWithoutLayout()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var beforeDocument = composition.Document.CaptureSnapshot();
        var before = await pipeline.RunFullAsync(
            beforeDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var beforeArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(before.Artifacts);
        var beforeGeometry = GeometryByVisualState(beforeArtifacts);

        var committed = await Processor(configuration).ExecuteAsync(
            composition.Document,
            new DeleteBpmnFlowNodeCommand(
                beforeDocument.DocumentId,
                beforeDocument.Revision,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.TaskVisualId));
        Assert.True(committed.IsCommitted);
        var impact = Assert.IsType<NodeGeometryPipelineImpact>(
            committed.CommittedEvent?.NodeGeometryImpact);
        var current = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            beforeArtifacts,
            impact,
            configuration.InitialEditorState,
            CancellationToken.None);
        var currentArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            current.Artifacts);
        var currentGeometry = GeometryByVisualState(currentArtifacts);

        Assert.Equal(1, probe.InvocationCount);
        Assert.DoesNotContain(BpmnDemoPipeline.TaskVisualId, currentGeometry.Keys);
        Assert.Equal(beforeGeometry.Count - 1, currentGeometry.Count);
        foreach (var (visualStateId, geometry) in currentGeometry)
        {
            Assert.Same(beforeGeometry[visualStateId], geometry);
        }
    }

    [Fact]
    public async Task RecursiveDeletionLocalizesDocumentWideGeometryImpactToActiveScope()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var initialDocument = composition.Document.CaptureSnapshot();
        var initial = await pipeline.RunFullAsync(
            initialDocument,
            initialDocument.SemanticModel.RootScopeId,
            configuration.InitialEditorState,
            CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            initial.Artifacts);
        var initialGeometry = GeometryByVisualState(initialArtifacts);
        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            [events],
            configuration.HistoryPolicies,
            configuration.ConnectorAnchorPolicyProvider);
        var history = new HistoryManager(composition.Document);

        Assert.True((await history.ExecuteAsync(
            processor,
            new DeleteBpmnFlowNodeCommand(
                initialDocument.DocumentId,
                initialDocument.Revision,
                BpmnDemoPipeline.ProcessOrderSubProcessId,
                BpmnDemoPipeline.ProcessOrderSubProcessVisualId))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var deleteImpact = Assert.IsType<NodeGeometryPipelineImpact>(
            events.Events.Last().NodeGeometryImpact);
        Assert.Equal(6, deleteImpact.RemovedVisualStateIds.Length);
        Assert.Contains(
            BpmnDemoPipeline.ProcessOrderTaskVisualId,
            deleteImpact.RemovedVisualStateIds);
        Assert.Contains(
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId,
            deleteImpact.RemovedVisualStateIds);
        Assert.Contains(
            BpmnDemoPipeline.ProcessOrderHandleTimeoutTaskVisualId,
            deleteImpact.RemovedVisualStateIds);
        var deleted = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            initialDocument.SemanticModel.RootScopeId,
            initialArtifacts,
            deleteImpact,
            configuration.InitialEditorState,
            CancellationToken.None);
        var deletedArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            deleted.Artifacts);
        var deletedGeometry = GeometryByVisualState(deletedArtifacts);
        Assert.DoesNotContain(
            BpmnDemoPipeline.ProcessOrderSubProcessVisualId,
            deletedGeometry.Keys);
        foreach (var (visualStateId, geometry) in deletedGeometry)
        {
            Assert.Same(initialGeometry[visualStateId], geometry);
        }

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var undoImpact = Assert.IsType<NodeGeometryPipelineImpact>(
            events.Events.Last().NodeGeometryImpact);
        Assert.Equal(6, undoImpact.ChangedVisualStateIds.Length);
        Assert.Contains(
            BpmnDemoPipeline.ProcessOrderReviewTimeoutEventVisualId,
            undoImpact.ChangedVisualStateIds);
        var restored = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            initialDocument.SemanticModel.RootScopeId,
            deletedArtifacts,
            [initialArtifacts, deletedArtifacts],
            undoImpact,
            configuration.InitialEditorState,
            CancellationToken.None);
        var restoredArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            restored.Artifacts);
        Assert.Same(
            initialArtifacts.LayoutResult.Computation,
            restoredArtifacts.LayoutResult.Computation);

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var redone = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            initialDocument.SemanticModel.RootScopeId,
            restoredArtifacts,
            [initialArtifacts, deletedArtifacts, restoredArtifacts],
            Assert.IsType<NodeGeometryPipelineImpact>(
                events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);

        Assert.Equal(EditingSessionPipelineStatus.Succeeded, redone.Status);
        Assert.Equal(1, probe.InvocationCount);
    }

    [Fact]
    public async Task OutOfScopeHistoryImpactPreservesActiveScopeGeometryWithoutLayout()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var initialDocument = composition.Document.CaptureSnapshot();
        var initial = await pipeline.RunFullAsync(
            initialDocument,
            BpmnDemoPipeline.ProcessOrderScopeId,
            configuration.InitialEditorState,
            CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            initial.Artifacts);
        var initialGeometry = GeometryByVisualState(initialArtifacts);
        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            [events],
            configuration.HistoryPolicies,
            configuration.ConnectorAnchorPolicyProvider);
        var history = new HistoryManager(composition.Document);
        var rootVisual = initialDocument.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);

        Assert.True((await history.ExecuteAsync(
            processor,
            new MoveVisualStateCommand(
                initialDocument.DocumentId,
                initialDocument.Revision,
                rootVisual.Id,
                rootVisual.Position + new VectorD(25d, 15d),
                VisualPlacementMode.Pinned))).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var moved = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            BpmnDemoPipeline.ProcessOrderScopeId,
            initialArtifacts,
            Assert.IsType<NodeGeometryPipelineImpact>(
                events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);
        var movedArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(moved.Artifacts);
        Assert.Same(
            initialArtifacts.LayoutResult.Computation,
            movedArtifacts.LayoutResult.Computation);
        Assert.Equal(initialGeometry, GeometryByVisualState(movedArtifacts));

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var undone = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            BpmnDemoPipeline.ProcessOrderScopeId,
            movedArtifacts,
            [initialArtifacts, movedArtifacts],
            Assert.IsType<NodeGeometryPipelineImpact>(
                events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);
        var undoneArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(undone.Artifacts);
        Assert.Same(
            initialArtifacts.LayoutResult.Computation,
            undoneArtifacts.LayoutResult.Computation);
        Assert.Equal(initialGeometry, GeometryByVisualState(undoneArtifacts));

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var redone = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            BpmnDemoPipeline.ProcessOrderScopeId,
            undoneArtifacts,
            [initialArtifacts, movedArtifacts, undoneArtifacts],
            Assert.IsType<NodeGeometryPipelineImpact>(
                events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);

        Assert.Equal(EditingSessionPipelineStatus.Succeeded, redone.Status);
        Assert.Same(
            initialArtifacts.LayoutResult.Computation,
            redone.Artifacts?.LayoutResult.Computation);
        Assert.Equal(1, probe.InvocationCount);
    }

    [Fact]
    public async Task UndoDeletedUnpinnedNodeRestoresExactHistoricalEffectiveGeometry()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var initialDocument = composition.Document.CaptureSnapshot();
        var initial = await pipeline.RunFullAsync(
            initialDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            initial.Artifacts);
        var initialGeometry = GeometryByVisualState(initialArtifacts);
        var persistent = initialDocument.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);
        Assert.NotEqual(
            new RectD(
                persistent.Position.X,
                persistent.Position.Y,
                persistent.Size.Width,
                persistent.Size.Height),
            initialGeometry[BpmnDemoPipeline.TaskVisualId].Bounds);
        Assert.NotEqual(VisualPlacementMode.Pinned, persistent.PlacementMode);

        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            [events],
            configuration.HistoryPolicies,
            configuration.ConnectorAnchorPolicyProvider);
        var history = new HistoryManager(composition.Document);
        var deleted = await history.ExecuteAsync(
            processor,
            new DeleteBpmnFlowNodeCommand(
                initialDocument.DocumentId,
                initialDocument.Revision,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.TaskVisualId));
        Assert.True(deleted.IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var deletedPipeline = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            initialArtifacts,
            Assert.IsType<NodeGeometryPipelineImpact>(
                events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);
        var deletedArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            deletedPipeline.Artifacts);

        var undone = await history.UndoAsync(processor);
        Assert.True(undone.IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var undoImpact = Assert.IsType<NodeGeometryPipelineImpact>(
            events.Events.Last().NodeGeometryImpact);
        Assert.Equal(initialDocument.Revision, undoImpact.HistoricalSourceRevision);
        var restored = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            deletedArtifacts,
            [initialArtifacts, deletedArtifacts],
            undoImpact,
            configuration.InitialEditorState,
            CancellationToken.None);
        var restoredArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            restored.Artifacts);

        Assert.Equal(1, probe.InvocationCount);
        Assert.Same(initialArtifacts.LayoutResult.Computation,
            restoredArtifacts.LayoutResult.Computation);
        var restoredGeometry = GeometryByVisualState(restoredArtifacts);
        Assert.Equal(initialGeometry.Keys, restoredGeometry.Keys);
        foreach (var (visualStateId, geometry) in initialGeometry)
        {
            Assert.Same(geometry, restoredGeometry[visualStateId]);
        }

        var redone = await history.RedoAsync(processor);
        Assert.True(redone.IsCommitted);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);
        var redonePipeline = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            restoredArtifacts,
            [initialArtifacts, deletedArtifacts, restoredArtifacts],
            Assert.IsType<NodeGeometryPipelineImpact>(
                events.Events.Last().NodeGeometryImpact),
            configuration.InitialEditorState,
            CancellationToken.None);
        Assert.Equal(EditingSessionPipelineStatus.Succeeded, redonePipeline.Status);
        Assert.Equal(1, probe.InvocationCount);
    }

    [Fact]
    public async Task MissingHistoricalDeletionGeometryFaultsWithoutAutomaticLayoutFallback()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var initialDocument = composition.Document.CaptureSnapshot();
        var initial = await pipeline.RunFullAsync(
            initialDocument,
            configuration.InitialEditorState,
            CancellationToken.None);
        var initialArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            initial.Artifacts);
        var processor = new CommandProcessor(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            historyPolicies: configuration.HistoryPolicies,
            connectorAnchorPolicyProvider: configuration.ConnectorAnchorPolicyProvider);
        var history = new HistoryManager(composition.Document);
        Assert.True((await history.ExecuteAsync(
            processor,
            new DeleteBpmnFlowNodeCommand(
                initialDocument.DocumentId,
                initialDocument.Revision,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.TaskVisualId))).IsCommitted);
        var deletedDocument = composition.Document.CaptureSnapshot();
        var deleted = await pipeline.RunPreservingNodeLayoutAsync(
            deletedDocument,
            initialArtifacts,
            NodeGeometryPipelineImpact.ForRemovedVisualStates(
                [BpmnDemoPipeline.TaskVisualId]),
            configuration.InitialEditorState,
            CancellationToken.None);
        var deletedArtifacts = Assert.IsType<EditingSessionPipelineArtifacts>(
            deleted.Artifacts);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);

        var rejected = await pipeline.RunPreservingNodeLayoutAsync(
            composition.Document.CaptureSnapshot(),
            deletedArtifacts,
            [],
            NodeGeometryPipelineImpact.ForHistoricalRestoration(
                [BpmnDemoPipeline.TaskVisualId],
                initialDocument.Revision),
            configuration.InitialEditorState,
            CancellationToken.None);

        Assert.Equal(EditingSessionPipelineStatus.Failed, rejected.Status);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code ==
                EditingSessionDiagnosticCodes.NodeGeometryPreservationUnavailable);
        Assert.Equal(1, probe.InvocationCount);
    }

    private static EditingSessionConfiguration WithLayoutProbe(
        EditingSessionConfiguration source,
        ILayoutAlgorithm probe) =>
        new(
            source.ProjectionEngine,
            new LayoutEngine(
                [new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, probe)]),
            source.LayoutAlgorithmId,
            source.RoutingEngine,
            source.RoutingAlgorithmId,
            source.SceneBuilder,
            source.ProjectionContext,
            source.LayoutContext,
            source.RoutingContext,
            source.InitialEditorState,
            source.CommandHandlers,
            source.CommandValidators,
            source.HistoryPolicies,
            source.DocumentChangedSubscribers,
            source.ConnectorAnchorPolicyProvider);

    private static Dictionary<VisualStateId, LayoutNodeGeometry> GeometryByVisualState(
        EditingSessionPipelineArtifacts artifacts)
    {
        var geometryById = artifacts.LayoutResult.Nodes.ToDictionary(
            static geometry => geometry.ProjectedObjectId);
        return artifacts.ProjectedGraph.Nodes.ToDictionary(
            static node => node.Source.VisualStateId!,
            node => geometryById[node.Id]);
    }

    private static void AssertGeometryUnchangedExcept(
        IReadOnlyDictionary<VisualStateId, LayoutNodeGeometry> before,
        IReadOnlyDictionary<VisualStateId, LayoutNodeGeometry> current,
        params VisualStateId[] changedIds)
    {
        var changed = changedIds.ToHashSet();
        Assert.Equal(before.Keys.OrderBy(static id => id.Value), current.Keys.OrderBy(static id => id.Value));
        foreach (var (id, geometry) in before)
        {
            if (!changed.Contains(id))
            {
                Assert.Same(geometry, current[id]);
            }
        }
    }

    private static void AssertAuthoritativeContentEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
    }

    private sealed class CountingLayoutAlgorithm(ILayoutAlgorithm inner) : ILayoutAlgorithm
    {
        internal int InvocationCount { get; private set; }

        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return inner.Compute(graph, context, cancellationToken);
        }
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = [];

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private static CommandProcessor Processor(EditingSessionConfiguration configuration) =>
        new(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            historyPolicies: configuration.HistoryPolicies,
            connectorAnchorPolicyProvider: configuration.ConnectorAnchorPolicyProvider);
}
