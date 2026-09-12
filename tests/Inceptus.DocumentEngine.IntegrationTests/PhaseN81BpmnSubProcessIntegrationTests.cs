using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
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
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN81BpmnSubProcessIntegrationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8.1:integration");
    private static readonly SemanticElementId SubProcessId =
        new("bpmn:n8.1:integration:subprocess");
    private static readonly VisualStateId SubProcessVisualId =
        new("bpmn:n8.1:integration:subprocess:visual");
    private static readonly DocumentScopeId ChildScopeId =
        new("bpmn:n8.1:integration:subprocess:scope");

    [Fact]
    public void FullN81PipelineKeepsChildContentAndGeometryOutOfTheRootScene()
    {
        var emptyChild = RunPipeline(PipelineSnapshot(includeChild: false));
        var populatedChild = RunPipeline(PipelineSnapshot(includeChild: true));
        var childId = new SemanticElementId("bpmn:n8.1:integration:child-task");

        Assert.Equal(emptyChild.Graph, populatedChild.Graph);
        Assert.Equal(emptyChild.Layout, populatedChild.Layout);
        Assert.Equal(emptyChild.Routing, populatedChild.Routing);
        Assert.Equal(emptyChild.Scene, populatedChild.Scene);
        Assert.Contains(populatedChild.Graph.Nodes, node =>
            node.Source.SemanticElementId == SubProcessId);
        Assert.DoesNotContain(populatedChild.Graph.Nodes, node =>
            node.Source.SemanticElementId == childId);
        Assert.DoesNotContain(populatedChild.Scene.Items, item =>
            item.Origin.SemanticElementId == childId);
        var markers = populatedChild.Scene.Items.Where(item =>
            item.Origin.SemanticElementId == SubProcessId &&
            item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension) &&
            item.Layer == Canvas2DSceneLayer.Decoration).ToArray();
        Assert.Equal(2, markers.Length);
        Assert.All(markers, marker =>
            Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode));
    }

    [Fact]
    public async Task N81CreationAndRecursiveDeletionAreExactHistoryUnits()
    {
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: provider).Document);
        var processor = Processor(registration, provider);
        var history = new HistoryManager(document);

        var creation = await history.ExecuteAsync(
            processor,
            new CreateBpmnSubProcessCommand(
                DocumentId,
                document.Revision,
                SubProcessId,
                SubProcessVisualId,
                document.SemanticModel.RootScopeId,
                ChildScopeId,
                new PointD(240d, 160d),
                new SizeD(120d, 80d),
                "PROCESS_ORDER",
                "Process order",
                VisualPlacementMode.Pinned,
                "Processes one order."));

        Assert.True(creation.IsCommitted, Diagnostics(creation.Diagnostics));
        var created = document.CaptureSnapshot();
        var childScope = Assert.Single(created.SemanticModel.NestedScopes);
        Assert.Equal(ChildScopeId, childScope.Id);
        Assert.Equal(created.SemanticModel.RootScopeId, childScope.ParentScopeId);
        Assert.Equal(SubProcessId, childScope.OwnerSemanticElementId);
        Assert.Empty(created.SemanticModel.ScopeMemberships);

        var deletion = await history.ExecuteAsync(
            processor,
            new DeleteBpmnFlowNodeCommand(
                DocumentId,
                document.Revision,
                SubProcessId,
                SubProcessVisualId));

        Assert.True(deletion.IsCommitted, Diagnostics(deletion.Diagnostics));
        var deleted = document.CaptureSnapshot();
        Assert.Empty(deleted.SemanticModel.Elements);
        Assert.Empty(deleted.SemanticModel.NestedScopes);
        Assert.Empty(deleted.VisualModel.VisualStates);
        Assert.Equal(2, history.CaptureStatus().EntryCount);

        var undo = await history.UndoAsync(processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertAuthoritativeContentEqual(created, document.CaptureSnapshot());

        var redo = await history.RedoAsync(processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(deleted, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(false, false, true, null)]
    [InlineData(true, false, false,
        BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid)]
    [InlineData(false, true, false,
        BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope)]
    public async Task N81SequenceFlowRulesCommitOrRejectAtomically(
        bool eventBasedSource,
        bool nestedTarget,
        bool expectedCommit,
        string? expectedDiagnosticCode)
    {
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(
            FlowSnapshot(eventBasedSource, nestedTarget),
            provider);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = Processor(registration, provider);
        var history = new HistoryManager(document);
        var before = document.CaptureSnapshot();
        var flowId = new SemanticElementId("bpmn:n8.1:integration:flow");
        var flowVisualId = new VisualStateId("bpmn:n8.1:integration:flow:visual");

        var result = await history.ExecuteAsync(
            processor,
            new CreateBpmnSequenceFlowCommand(
                DocumentId,
                document.Revision,
                flowId,
                flowVisualId,
                new SemanticElementId("bpmn:n8.1:integration:source"),
                SubProcessId,
                new ConnectorAnchorId("bpmn:n8.1:integration:source-anchor"),
                new ConnectorAnchorId("bpmn:n8.1:integration:target-anchor")));

        Assert.Equal(expectedCommit, result.IsCommitted);
        if (expectedCommit)
        {
            Assert.True(document.SemanticModel.TryGetRelationship(flowId, out _));
            Assert.True(document.VisualModel.TryGetVisualState(flowVisualId, out _));
            Assert.Equal(1, history.CaptureStatus().EntryCount);
        }
        else
        {
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == expectedDiagnosticCode);
            Assert.Same(before, document.CaptureSnapshot());
            Assert.False(document.SemanticModel.TryGetRelationship(flowId, out _));
            Assert.False(document.VisualModel.TryGetVisualState(flowVisualId, out _));
            Assert.Equal(0, history.CaptureStatus().EntryCount);
        }
    }

    private static PipelineArtifacts RunPipeline(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N81;
        var projection = new ProjectionEngine(registration.ProjectionRules).Project(snapshot);
        Assert.True(projection.IsSuccessful, Diagnostics(projection.Diagnostics));
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);
        var layoutExecution = new LayoutEngine(registration.LayoutAlgorithms).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout);
        Assert.True(layoutExecution.IsSuccessful, Diagnostics(layoutExecution.Diagnostics));
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = new RoutingEngine(registration.RoutingAlgorithms).Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting);
        Assert.True(routingExecution.IsSuccessful, Diagnostics(routingExecution.Diagnostics));
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = new Canvas2DSceneBuilder(
            contributors: registration.SceneContributors).Build(
                graph,
                layout,
                routing,
                snapshot.VisualModel,
                EditorStateSnapshot.Empty);
        Assert.True(sceneExecution.Succeeded, Diagnostics(sceneExecution.Diagnostics));
        return new PipelineArtifacts(
            graph,
            layout,
            routing,
            Assert.IsType<Canvas2DScene>(sceneExecution.Scene));
    }

    private static DocumentSnapshot PipelineSnapshot(bool includeChild)
    {
        var beforeId = new SemanticElementId("bpmn:n8.1:integration:before");
        var afterId = new SemanticElementId("bpmn:n8.1:integration:after");
        var childId = new SemanticElementId("bpmn:n8.1:integration:child-task");
        var firstFlowId = new SemanticElementId("bpmn:n8.1:integration:flow-before");
        var secondFlowId = new SemanticElementId("bpmn:n8.1:integration:flow-after");
        var beforeAnchorId = new ConnectorAnchorId("bpmn:n8.1:integration:before-source");
        var subTargetAnchorId = new ConnectorAnchorId("bpmn:n8.1:integration:sub-target");
        var subSourceAnchorId = new ConnectorAnchorId("bpmn:n8.1:integration:sub-source");
        var afterAnchorId = new ConnectorAnchorId("bpmn:n8.1:integration:after-target");
        var elements = new List<SemanticElementSnapshot>
        {
            BpmnSemanticFactory.CreateTask(beforeId, "BEFORE", "Before", 1),
            BpmnSemanticFactory.CreateSubProcess(
                SubProcessId,
                "PROCESS_ORDER",
                "Process order",
                "Processes one order."),
            BpmnSemanticFactory.CreateTask(afterId, "AFTER", "After", 2),
        };
        var visuals = new List<VisualStateSnapshot>
        {
            NodeVisual(
                new VisualStateId("bpmn:n8.1:integration:before:visual"),
                beforeId,
                new PointD(40d, 160d),
                [Anchor(beforeAnchorId, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source)]),
            NodeVisual(
                SubProcessVisualId,
                SubProcessId,
                new PointD(280d, 160d),
                [
                    Anchor(subTargetAnchorId, ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target),
                    Anchor(subSourceAnchorId, ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source),
                ]),
            NodeVisual(
                new VisualStateId("bpmn:n8.1:integration:after:visual"),
                afterId,
                new PointD(560d, 160d),
                [Anchor(afterAnchorId, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target)]),
            ConnectorVisual(
                new VisualStateId("bpmn:n8.1:integration:flow-before:visual"),
                firstFlowId,
                beforeAnchorId,
                subTargetAnchorId),
            ConnectorVisual(
                new VisualStateId("bpmn:n8.1:integration:flow-after:visual"),
                secondFlowId,
                subSourceAnchorId,
                afterAnchorId),
        };
        var memberships = new List<SemanticElementScopeMembershipSnapshot>();
        if (includeChild)
        {
            elements.Add(BpmnSemanticFactory.CreateTask(
                childId,
                "CHILD",
                "Child obstacle",
                3));
            visuals.Add(NodeVisual(
                new VisualStateId("bpmn:n8.1:integration:child:visual"),
                childId,
                new PointD(410d, 150d),
                []));
            memberships.Add(new SemanticElementScopeMembershipSnapshot(
                childId,
                ChildScopeId));
        }

        return Snapshot(
            elements,
            [
                BpmnSemanticFactory.CreateSequenceFlow(
                    firstFlowId,
                    beforeId,
                    SubProcessId),
                BpmnSemanticFactory.CreateSequenceFlow(
                    secondFlowId,
                    SubProcessId,
                    afterId),
            ],
            visuals,
            [new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId(DocumentId.Value),
                SubProcessId)],
            memberships);
    }

    private static DocumentSnapshot FlowSnapshot(
        bool eventBasedSource,
        bool nestedTarget)
    {
        var sourceId = new SemanticElementId("bpmn:n8.1:integration:source");
        var parentId = new SemanticElementId("bpmn:n8.1:integration:parent");
        var source = eventBasedSource
            ? BpmnSemanticFactory.CreateEventBasedGateway(
                sourceId,
                "AWAIT_EVENT",
                "Await event")
            : BpmnSemanticFactory.CreateTask(sourceId, "SOURCE", "Source", 1);
        var elements = new List<SemanticElementSnapshot> { source };
        var scopes = new List<DocumentScopeSnapshot>();
        var memberships = new List<SemanticElementScopeMembershipSnapshot>();
        if (nestedTarget)
        {
            elements.Add(BpmnSemanticFactory.CreateSubProcess(
                parentId,
                "PARENT",
                "Parent"));
            scopes.Add(new DocumentScopeSnapshot(
                new DocumentScopeId("bpmn:n8.1:integration:parent-scope"),
                new DocumentScopeId(DocumentId.Value),
                parentId));
            scopes.Add(new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId("bpmn:n8.1:integration:parent-scope"),
                SubProcessId));
            memberships.Add(new SemanticElementScopeMembershipSnapshot(
                SubProcessId,
                new DocumentScopeId("bpmn:n8.1:integration:parent-scope")));
        }
        else
        {
            scopes.Add(new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId(DocumentId.Value),
                SubProcessId));
        }

        elements.Add(BpmnSemanticFactory.CreateSubProcess(
            SubProcessId,
            "TARGET",
            "Target"));
        var visuals = new List<VisualStateSnapshot>
        {
            NodeVisual(
                new VisualStateId("bpmn:n8.1:integration:source:visual"),
                sourceId,
                new PointD(40d, 80d),
                [Anchor(
                    new ConnectorAnchorId("bpmn:n8.1:integration:source-anchor"),
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source)],
                eventBasedSource ? new SizeD(48d, 48d) : null),
            NodeVisual(
                SubProcessVisualId,
                SubProcessId,
                new PointD(320d, 80d),
                [Anchor(
                    new ConnectorAnchorId("bpmn:n8.1:integration:target-anchor"),
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target)]),
        };
        if (nestedTarget)
        {
            visuals.Add(NodeVisual(
                new VisualStateId("bpmn:n8.1:integration:parent:visual"),
                parentId,
                new PointD(600d, 80d),
                []));
        }

        return Snapshot(elements, [], visuals, scopes, memberships);
    }

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> relationships,
        IEnumerable<VisualStateSnapshot> visuals,
        IEnumerable<DocumentScopeSnapshot> scopes,
        IEnumerable<SemanticElementScopeMembershipSnapshot> memberships) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships,
                scopes,
                memberships),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position,
        IEnumerable<ConnectorAnchor> anchors,
        SizeD? size = null) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            size ?? new SizeD(120d, 80d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors);

    private static VisualStateSnapshot ConnectorVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(0d, 0d),
            new SizeD(0d, 0d),
            VisualPlacementMode.Manual,
            sourceAnchorId: sourceAnchorId,
            targetAnchorId: targetAnchorId);

    private static ConnectorAnchor Anchor(
        ConnectorAnchorId id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role) =>
        new(id, side, role, 0);

    private static CommandProcessor Processor(
        BpmnPluginRegistration registration,
        IElementConnectorAnchorPolicyProvider provider) =>
        new(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);

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

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record PipelineArtifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing,
        Canvas2DScene Scene);
}
