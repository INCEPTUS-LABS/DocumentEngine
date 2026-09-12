using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
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

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN80ProcessScopeIntegrationTests
{
    private static readonly DocumentId NeutralDocumentId =
        new("test:n8.0:neutral-document");
    private static readonly SemanticElementId RootOwnerId =
        new("test:n8.0:root-owner");
    private static readonly SemanticElementId ScopeAOwnerId =
        new("test:n8.0:scope-a-owner");
    private static readonly SemanticElementId ScopeAElementId =
        new("test:n8.0:scope-a-element");
    private static readonly SemanticElementId ScopeBElementId =
        new("test:n8.0:scope-b-element");
    private static readonly SemanticElementId ScopeBTargetId =
        new("test:n8.0:scope-b-target");
    private static readonly SemanticElementId ScopeBRelationshipId =
        new("test:n8.0:scope-b-relationship");
    private static readonly DocumentScopeId ScopeAId = new("test:n8.0:scope-a");
    private static readonly DocumentScopeId ScopeBId = new("test:n8.0:scope-b");
    private static readonly VisualStateId ScopeBVisualId =
        new("test:n8.0:scope-b-element:visual");

    [Fact]
    public void LegacyRootOnlySnapshotReconstructsWithOneImplicitRootScope()
    {
        var documentId = new DocumentId("test:n8.0:legacy-root");
        var revision = new DocumentRevision(7);
        var sourceId = new SemanticElementId("test:n8.0:legacy-source");
        var targetId = new SemanticElementId("test:n8.0:legacy-target");
        var relationshipId = new SemanticElementId("test:n8.0:legacy-relationship");
        var candidate = Snapshot(
            documentId,
            revision,
            [
                new SemanticElementSnapshot(sourceId, new SemanticTypeId("test:node")),
                new SemanticElementSnapshot(targetId, new SemanticTypeId("test:node")),
            ],
            [
                new SemanticRelationshipSnapshot(
                    relationshipId,
                    new SemanticTypeId("test:edge"),
                    sourceId,
                    targetId),
            ]);

        var reconstruction = DocumentReconstructor.Reconstruct(candidate);

        var document = RequireSuccess(reconstruction);
        var captured = document.CaptureSnapshot();
        var semantic = captured.SemanticModel;
        var expectedRootId = new DocumentScopeId(documentId.Value);
        Assert.Equal(candidate, captured);
        Assert.Equal(expectedRootId, semantic.RootScopeId);
        Assert.Equal(expectedRootId, semantic.GetRootScope().Id);
        Assert.Null(semantic.GetRootScope().ParentScopeId);
        Assert.Null(semantic.GetParentScope(expectedRootId));
        Assert.Empty(semantic.NestedScopes);
        Assert.Empty(semantic.ScopeMemberships);
        Assert.Empty(semantic.GetChildScopes(expectedRootId));
        Assert.Empty(semantic.GetAncestors(expectedRootId));
        Assert.Empty(semantic.GetDescendants(expectedRootId));
        Assert.All(semantic.Elements, element =>
            Assert.Equal(expectedRootId, semantic.GetScope(element.Id).Id));
        Assert.Equal(expectedRootId, semantic.GetScope(relationshipId).Id);
    }

    [Fact]
    public void NestedScopeSnapshotReconstructsAndCapturesExactDeterministicContainment()
    {
        var candidate = CreateNestedNeutralSnapshot(new DocumentRevision(11));

        var firstReconstruction = DocumentReconstructor.Reconstruct(candidate);

        var firstDocument = RequireSuccess(firstReconstruction);
        var first = firstDocument.CaptureSnapshot();
        var semantic = first.SemanticModel;
        Assert.Equal(candidate, first);
        Assert.NotSame(candidate.SemanticModel, semantic);
        Assert.Equal(
            new[] { ScopeAId, ScopeBId },
            semantic.NestedScopes.Select(static scope => scope.Id));
        Assert.Equal(
            new[] { ScopeAId },
            semantic.GetChildScopes(semantic.RootScopeId).Select(static scope => scope.Id));
        Assert.Equal(ScopeAId, semantic.GetParentScope(ScopeBId)!.Id);
        Assert.Equal(
            new[] { ScopeAId, semantic.RootScopeId },
            semantic.GetAncestors(ScopeBId).Select(static scope => scope.Id));
        Assert.Equal(
            new[] { ScopeAId, ScopeBId },
            semantic.GetDescendants(semantic.RootScopeId).Select(static scope => scope.Id));
        Assert.True(semantic.IsAncestorOf(semantic.RootScopeId, ScopeBId));
        Assert.True(semantic.IsAncestorOf(ScopeAId, ScopeBId));
        Assert.False(semantic.IsAncestorOf(ScopeBId, ScopeAId));
        Assert.False(semantic.IsAncestorOf(ScopeAId, ScopeAId));
        Assert.Equal(ScopeBId, semantic.GetScope(ScopeBElementId).Id);
        Assert.Equal(ScopeBId, semantic.GetScope(ScopeBRelationshipId).Id);
        Assert.Equal(
            first.VisualModel.VisualStates.Single(visual =>
                visual.SemanticElementId == RootOwnerId).Position,
            first.VisualModel.VisualStates.Single(visual =>
                visual.SemanticElementId == ScopeBElementId).Position);

        var secondReconstruction = DocumentReconstructor.Reconstruct(first);

        var second = RequireSuccess(secondReconstruction).CaptureSnapshot();
        Assert.Equal(first, second);
        Assert.NotSame(first.SemanticModel.NestedScopes[0],
            second.SemanticModel.NestedScopes[0]);
        Assert.NotSame(first.SemanticModel.ScopeMemberships[0],
            second.SemanticModel.ScopeMemberships[0]);
    }

    [Fact]
    public async Task RootAndNestedDocumentsRunThroughTheUnchangedEditingSessionPipeline()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var configuration = composition.Configuration;
        var rootBefore = composition.Document.CaptureSnapshot();
        var reconstructedRoot = RequireSuccess(DocumentReconstructor.Reconstruct(
            rootBefore,
            configuration.ConnectorAnchorPolicyProvider));

        var originalRootPipeline = await RunEditingSessionPipelineAsync(
            composition.Document,
            configuration,
            "phase-n80-root-original");
        var reconstructedRootPipeline = await RunEditingSessionPipelineAsync(
            reconstructedRoot,
            configuration,
            "phase-n80-root-reconstructed");

        Assert.Equal(rootBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(rootBefore, reconstructedRoot.CaptureSnapshot());
        Assert.Equal(
            BpmnDemoPipeline.ProcessOrderScopeId,
            Assert.Single(rootBefore.SemanticModel.NestedScopes).Id);
        Assert.Equal(5, rootBefore.SemanticModel.ScopeMemberships.Length);
        Assert.Equal(originalRootPipeline.Graph, reconstructedRootPipeline.Graph);
        Assert.Equal(originalRootPipeline.Layout, reconstructedRootPipeline.Layout);
        Assert.Equal(originalRootPipeline.Routing, reconstructedRootPipeline.Routing);
        Assert.Equal(originalRootPipeline.Scene, reconstructedRootPipeline.Scene);

        var nestedDocument = RequireSuccess(DocumentReconstructor.Reconstruct(
            CreateScopedBpmnSnapshot(),
            configuration.ConnectorAnchorPolicyProvider));
        var nestedBefore = nestedDocument.CaptureSnapshot();

        var nestedPipeline = await RunEditingSessionPipelineAsync(
            nestedDocument,
            configuration,
            "phase-n80-nested");

        var nestedAfter = nestedDocument.CaptureSnapshot();
        Assert.Equal(nestedBefore, nestedAfter);
        Assert.Equal(
            nestedBefore.SemanticModel.NestedScopes.AsEnumerable(),
            nestedAfter.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            nestedBefore.SemanticModel.ScopeMemberships.AsEnumerable(),
            nestedAfter.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(BpmnScopeId, nestedAfter.SemanticModel.GetScope(NestedSourceId).Id);
        Assert.Equal(BpmnScopeId, nestedAfter.SemanticModel.GetScope(NestedTargetId).Id);
        Assert.DoesNotContain(nestedPipeline.Graph.Nodes, node =>
            node.Source.SemanticElementId == NestedSourceId ||
            node.Source.SemanticElementId == NestedTargetId);
        Assert.DoesNotContain(nestedPipeline.Graph.Labels, label =>
            label.Source.SemanticElementId == NestedSourceId ||
            label.Source.SemanticElementId == NestedTargetId);
        Assert.DoesNotContain(nestedPipeline.Scene.Items, item =>
            item.Origin.SemanticElementId == NestedSourceId ||
            item.Origin.SemanticElementId == NestedTargetId);
        Assert.Contains(nestedPipeline.Graph.Nodes, node =>
            node.Source.SemanticElementId == BpmnScopeOwnerId);
        Assert.Contains(nestedPipeline.Graph.Nodes, node =>
            node.Source.SemanticElementId == RootSourceId);
    }

    [Fact]
    public async Task VisualCommandCommitPreservesNestedContainmentExactly()
    {
        var document = RequireSuccess(DocumentReconstructor.Reconstruct(
            CreateNestedNeutralSnapshot(DocumentRevision.Zero)));
        var processor = new CommandProcessor();
        var before = document.CaptureSnapshot();

        var result = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                ScopeBVisualId,
                new PointD(640d, 420d),
                VisualPlacementMode.Pinned));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            after.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Relationships.AsEnumerable(),
            after.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.NestedScopes.AsEnumerable(),
            after.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.ScopeMemberships.AsEnumerable(),
            after.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(ScopeBId, after.SemanticModel.GetScope(ScopeBElementId).Id);
        Assert.Equal(ScopeBId, after.SemanticModel.GetScope(ScopeBRelationshipId).Id);
        Assert.Equal(
            new PointD(640d, 420d),
            after.VisualModel.VisualStates.Single(visual =>
                visual.Id == ScopeBVisualId).Position);
    }

    [Fact]
    public async Task SameScopeSequenceFlowCommitsAndCrossScopeAttemptIsAtomic()
    {
        var registration = BpmnPluginRegistration.N7;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = RequireSuccess(DocumentReconstructor.Reconstruct(
            CreateScopedBpmnSnapshot(),
            anchorPolicies));
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: [subscriber],
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var history = new HistoryManager(document);

        var sameScope = await history.ExecuteAsync(
            processor,
            new CreateBpmnSequenceFlowCommand(
                document.DocumentId,
                document.Revision,
                SameScopeFlowId,
                SameScopeFlowVisualId,
                NestedSourceId,
                NestedTargetId,
                NestedSourceAnchorId,
                NestedTargetAnchorId));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(sameScope.IsCommitted, Diagnostics(sameScope.Diagnostics));
        var afterSameScope = document.CaptureSnapshot();
        var historyAfterSameScope = history.CaptureStatus();
        Assert.Equal(1, historyAfterSameScope.EntryCount);
        Assert.Single(subscriber.Events);
        Assert.Equal(BpmnScopeId,
            afterSameScope.SemanticModel.GetScope(SameScopeFlowId).Id);
        Assert.Equal(
            CreateBpmnSequenceFlowCommand.KnownTypeId,
            subscriber.Events.Single().CommandTypeId);

        var crossScope = await history.ExecuteAsync(
            processor,
            new CreateBpmnSequenceFlowCommand(
                document.DocumentId,
                document.Revision,
                CrossScopeFlowId,
                CrossScopeFlowVisualId,
                RootSourceId,
                NestedTargetId,
                RootSourceAnchorId,
                NestedTargetAlternateAnchorId));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(crossScope.IsCommitted);
        Assert.Contains(crossScope.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        Assert.Same(afterSameScope, document.CaptureSnapshot());
        Assert.Equal(historyAfterSameScope, history.CaptureStatus());
        Assert.Single(subscriber.Events);
        Assert.False(afterSameScope.SemanticModel.TryGetRelationship(CrossScopeFlowId, out _));
        Assert.False(afterSameScope.VisualModel.TryGetVisualState(CrossScopeFlowVisualId, out _));
    }

    [Fact]
    public async Task CrossScopeReconnectIsRejectedWithOriginalCommittedStateExact()
    {
        var registration = BpmnPluginRegistration.N7;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = RequireSuccess(DocumentReconstructor.Reconstruct(
            CreateScopedBpmnSnapshot(),
            anchorPolicies));
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: [subscriber],
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var history = new HistoryManager(document);
        var creation = await history.ExecuteAsync(
            processor,
            new CreateBpmnSequenceFlowCommand(
                document.DocumentId,
                document.Revision,
                SameScopeFlowId,
                SameScopeFlowVisualId,
                NestedSourceId,
                NestedTargetId,
                NestedSourceAnchorId,
                NestedTargetAnchorId));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        Assert.True(creation.IsCommitted, Diagnostics(creation.Diagnostics));

        var before = document.CaptureSnapshot();
        var beforeRevision = document.Revision;
        var beforeHistory = history.CaptureStatus();
        var beforeEventCount = subscriber.Events.Count;
        Assert.True(before.SemanticModel.TryGetRelationship(
            SameScopeFlowId,
            out var originalRelationship));
        Assert.True(before.VisualModel.TryGetVisualState(
            SameScopeFlowVisualId,
            out var originalConnector));

        var result = await history.ExecuteAsync(
            processor,
            new ReconnectBpmnSequenceFlowEndpointCommand(
                document.DocumentId,
                document.Revision,
                SameScopeFlowId,
                SameScopeFlowVisualId,
                ConnectorEndpointKind.Source,
                NestedSourceId,
                NestedSourceAnchorId,
                RootSourceId,
                RootSourceAnchorId));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(beforeRevision, document.Revision);
        Assert.Equal(beforeHistory, history.CaptureStatus());
        Assert.Equal(beforeEventCount, subscriber.Events.Count);
        Assert.True(document.SemanticModel.TryGetRelationship(
            SameScopeFlowId,
            out var currentRelationship));
        Assert.True(document.VisualModel.TryGetVisualState(
            SameScopeFlowVisualId,
            out var currentConnector));
        Assert.Same(originalRelationship, currentRelationship);
        Assert.Same(originalConnector, currentConnector);
    }

    private static DocumentSnapshot CreateNestedNeutralSnapshot(DocumentRevision revision)
    {
        var rootScopeId = new DocumentScopeId(NeutralDocumentId.Value);
        var elements = new[]
        {
            new SemanticElementSnapshot(RootOwnerId, new SemanticTypeId("test:node")),
            new SemanticElementSnapshot(ScopeAOwnerId, new SemanticTypeId("test:node")),
            new SemanticElementSnapshot(ScopeAElementId, new SemanticTypeId("test:node")),
            new SemanticElementSnapshot(ScopeBElementId, new SemanticTypeId("test:node")),
            new SemanticElementSnapshot(ScopeBTargetId, new SemanticTypeId("test:node")),
        };
        var scopes = new[]
        {
            new DocumentScopeSnapshot(ScopeBId, ScopeAId, ScopeAOwnerId),
            new DocumentScopeSnapshot(ScopeAId, rootScopeId, RootOwnerId),
        };
        var memberships = new[]
        {
            new SemanticElementScopeMembershipSnapshot(ScopeBTargetId, ScopeBId),
            new SemanticElementScopeMembershipSnapshot(ScopeAOwnerId, ScopeAId),
            new SemanticElementScopeMembershipSnapshot(ScopeBElementId, ScopeBId),
            new SemanticElementScopeMembershipSnapshot(ScopeAElementId, ScopeAId),
        };
        var visuals = new[]
        {
            new VisualStateSnapshot(
                new VisualStateId("test:n8.0:root-owner:visual"),
                RootOwnerId,
                new PointD(120d, 80d),
                new SizeD(120d, 80d),
                VisualPlacementMode.Pinned),
            new VisualStateSnapshot(
                ScopeBVisualId,
                ScopeBElementId,
                new PointD(120d, 80d),
                new SizeD(120d, 80d),
                VisualPlacementMode.Pinned),
        };
        return Snapshot(
            NeutralDocumentId,
            revision,
            elements,
            [
                new SemanticRelationshipSnapshot(
                    ScopeBRelationshipId,
                    new SemanticTypeId("test:edge"),
                    ScopeBElementId,
                    ScopeBTargetId),
            ],
            visuals,
            scopes,
            memberships);
    }

    private static readonly DocumentId BpmnDocumentId =
        new("test:n8.0:bpmn-document");
    private static readonly DocumentScopeId BpmnScopeId =
        new("test:n8.0:bpmn-scope");
    private static readonly SemanticElementId BpmnScopeOwnerId =
        new("test:n8.0:bpmn-scope-owner");
    private static readonly SemanticElementId RootSourceId =
        new("test:n8.0:bpmn-root-source");
    private static readonly SemanticElementId NestedSourceId =
        new("test:n8.0:bpmn-nested-source");
    private static readonly SemanticElementId NestedTargetId =
        new("test:n8.0:bpmn-nested-target");
    private static readonly ConnectorAnchorId RootSourceAnchorId =
        new("test:n8.0:bpmn-root-source-anchor");
    private static readonly ConnectorAnchorId NestedSourceAnchorId =
        new("test:n8.0:bpmn-nested-source-anchor");
    private static readonly ConnectorAnchorId NestedTargetAnchorId =
        new("test:n8.0:bpmn-nested-target-anchor");
    private static readonly ConnectorAnchorId NestedTargetAlternateAnchorId =
        new("test:n8.0:bpmn-nested-target-alternate-anchor");
    private static readonly SemanticElementId SameScopeFlowId =
        new("test:n8.0:bpmn-same-scope-flow");
    private static readonly VisualStateId SameScopeFlowVisualId =
        new("test:n8.0:bpmn-same-scope-flow:visual");
    private static readonly SemanticElementId CrossScopeFlowId =
        new("test:n8.0:bpmn-cross-scope-flow");
    private static readonly VisualStateId CrossScopeFlowVisualId =
        new("test:n8.0:bpmn-cross-scope-flow:visual");

    private static DocumentSnapshot CreateScopedBpmnSnapshot()
    {
        var rootScopeId = new DocumentScopeId(BpmnDocumentId.Value);
        var elements = new[]
        {
            BpmnSemanticFactory.CreateTask(
                BpmnScopeOwnerId,
                "SCOPE_OWNER",
                "Scope owner",
                1),
            BpmnSemanticFactory.CreateTask(
                RootSourceId,
                "ROOT_SOURCE",
                "Root source",
                2),
            BpmnSemanticFactory.CreateTask(
                NestedSourceId,
                "NESTED_SOURCE",
                "Nested source",
                3),
            BpmnSemanticFactory.CreateTask(
                NestedTargetId,
                "NESTED_TARGET",
                "Nested target",
                4),
        };
        var visuals = new[]
        {
            NodeVisual(
                RootSourceId,
                "test:n8.0:bpmn-root-source:visual",
                new PointD(80d, 80d),
                [
                    new ConnectorAnchor(
                        RootSourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                ]),
            NodeVisual(
                NestedSourceId,
                "test:n8.0:bpmn-nested-source:visual",
                new PointD(80d, 80d),
                [
                    new ConnectorAnchor(
                        NestedSourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                ]),
            NodeVisual(
                NestedTargetId,
                "test:n8.0:bpmn-nested-target:visual",
                new PointD(320d, 80d),
                [
                    new ConnectorAnchor(
                        NestedTargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                    new ConnectorAnchor(
                        NestedTargetAlternateAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        1),
                ]),
        };
        return Snapshot(
            BpmnDocumentId,
            DocumentRevision.Zero,
            elements,
            [],
            visuals,
            [new DocumentScopeSnapshot(BpmnScopeId, rootScopeId, BpmnScopeOwnerId)],
            [
                new SemanticElementScopeMembershipSnapshot(NestedSourceId, BpmnScopeId),
                new SemanticElementScopeMembershipSnapshot(NestedTargetId, BpmnScopeId),
            ]);
    }

    private static VisualStateSnapshot NodeVisual(
        SemanticElementId semanticElementId,
        string visualStateId,
        PointD position,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            new VisualStateId(visualStateId),
            semanticElementId,
            position,
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors);

    private static DocumentSnapshot Snapshot(
        DocumentId documentId,
        DocumentRevision revision,
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> relationships,
        IEnumerable<VisualStateSnapshot>? visuals = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                documentId,
                revision,
                elements,
                relationships,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(documentId, revision, visuals),
            new DocumentMetadataSnapshot(documentId, revision));

    private static async Task<PipelineArtifacts> RunEditingSessionPipelineAsync(
        Document document,
        EditingSessionConfiguration configuration,
        string canvasElementId)
    {
        var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var initialization = await renderer.InitializeAsync(
            canvasElementId,
            new Canvas2DSurfaceSize(900d, 600d, 1.25d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));

        var attachment = await EditingSession.AttachAsync(document, renderer, configuration);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var ownedSession = session;
        var state = session.CaptureState();
        var pipelineDiagnostics = Diagnostics(
            attachment.Diagnostics.Concat(state.RuntimeDiagnostics));
        Assert.True(
            attachment.Status == EditingSessionAttachStatus.Ready,
            pipelineDiagnostics);
        Assert.True(state.Status == EditingSessionStatus.Ready, pipelineDiagnostics);
        Assert.Empty(state.RuntimeDiagnostics);
        return new PipelineArtifacts(
            Assert.IsType<ProjectedGraph>(state.ProjectedGraph),
            Assert.IsType<LayoutResult>(state.LayoutResult),
            Assert.IsType<RoutingResult>(state.RoutingResult),
            Assert.IsType<Canvas2DScene>(state.CurrentScene));
    }

    private static Document RequireSuccess(DocumentConstructionResult result)
    {
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<Document>(result.Document);
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record PipelineArtifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing,
        Canvas2DScene Scene);
}
