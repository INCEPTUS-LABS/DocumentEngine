using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

internal static class EditingSessionTestHarness
{
    internal static Document CreateDocument(
        Canvas2DSceneTestData inputs,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        var nodes = inputs.Graph.Nodes.OrderBy(static node => node.Id.Value).ToArray();
        var edges = inputs.Graph.Edges.OrderBy(static edge => edge.Id.Value).ToArray();
        var elementType = new SemanticTypeId("test:session:node");
        var relationshipType = new SemanticTypeId("test:session:relationship");
        var elements = nodes.Select(node => new SemanticElementSnapshot(
            node.Source.SemanticElementId,
            elementType));
        var nodeSemanticIds = nodes.ToDictionary(
            static node => node.Id,
            static node => node.Source.SemanticElementId);
        var relationships = edges.Select(edge => new SemanticRelationshipSnapshot(
            edge.Source.SemanticElementId,
            relationshipType,
            nodeSemanticIds[edge.SourceNodeId],
            nodeSemanticIds[edge.TargetNodeId]));
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                inputs.Graph.DocumentId,
                inputs.Graph.SourceRevision,
                elements,
                relationships),
            inputs.VisualModel,
            new DocumentMetadataSnapshot(
                inputs.Graph.DocumentId,
                inputs.Graph.SourceRevision));

        var reconstruction = DocumentReconstructor.Reconstruct(
            snapshot,
            connectorAnchorPolicyProvider);
        return reconstruction.Document ?? throw new InvalidOperationException(
            string.Join(
                Environment.NewLine,
                reconstruction.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    internal static EditingSessionPipelineArtifacts CreateArtifacts(Canvas2DSceneTestData inputs) =>
        new(inputs.Graph, inputs.Layout, inputs.Routing);

    internal static EditingSessionPipelineArtifacts CreateArtifacts(
        Canvas2DSceneTestData inputs,
        DocumentScopeId scopeId) =>
        new(scopeId, inputs.Graph, inputs.Layout, inputs.Routing);

    internal static EditingSessionPipelineArtifacts CreateArtifacts(
        Canvas2DSceneTestData inputs,
        DocumentRevision revision)
    {
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            revision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            inputs.Graph.Labels);
        var layout = new LayoutResult(
            graph.DocumentId,
            revision,
            inputs.Layout.AlgorithmId,
            inputs.Layout.Computation,
            inputs.Layout.Diagnostics);
        var routing = new RoutingResult(
            graph.DocumentId,
            revision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            inputs.Routing.Computation,
            inputs.Routing.Diagnostics);
        return new EditingSessionPipelineArtifacts(graph, layout, routing);
    }

    internal static Canvas2DScene CreateScene(
        Canvas2DSceneTestData inputs,
        EditorStateSnapshot? editorState = null) =>
        Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState ?? inputs.EditorState).Scene);

    internal static Canvas2DScene CreateScene(
        EditingSessionPipelineArtifacts artifacts,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState) =>
        Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            artifacts.ProjectedGraph,
            artifacts.LayoutResult,
            artifacts.RoutingResult,
            visualModel,
            editorState).Scene);

    internal static async ValueTask<(Canvas2DRenderer Renderer, Canvas2DRendererTestExecution Execution)>
        CreateInitializedRendererAsync()
    {
        var execution = new Canvas2DRendererTestExecution();
        execution.MeasurementResult = new Canvas2DTextMeasurementInteropResult
        {
            Succeeded = true,
            Width = 80d,
            Ascent = 10d,
            Descent = 3d,
            LineHeight = 18d,
            BoundingX = -1d,
            BoundingY = -10d,
            BoundingWidth = 82d,
            BoundingHeight = 14d,
            ResolvedFontIdentity = "test:session-font@1",
        };
        var renderer = new Canvas2DRenderer(
            execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "test:session-font",
                        "1",
                        "sans-serif",
                        "/fonts/test-session.woff2",
                        400,
                        TextFontStyle.Normal),
                ],
                defaultFontFamily: "sans-serif"));
        var initialized = await renderer.InitializeAsync(
            "test-session-canvas",
            new Canvas2DSurfaceSize(800d, 600d, 1d));
        Assert.True(initialized.Succeeded);
        return (renderer, execution);
    }

    internal static EditingSessionConfiguration Configuration(
        EditorStateSnapshot? initialEditorState = null,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null) =>
        new(
            new Inceptus.DocumentEngine.Runtime.Projection.ProjectionEngine(),
            new Inceptus.DocumentEngine.Runtime.Layout.LayoutEngine(),
            new AlgorithmId("test:unused-layout"),
            new Inceptus.DocumentEngine.Runtime.Routing.RoutingEngine(),
            new AlgorithmId("test:unused-routing"),
            new Canvas2DSceneBuilder(),
            initialEditorState: initialEditorState,
            connectorAnchorPolicyProvider: connectorAnchorPolicyProvider);
}

internal sealed class ControlledEditingSessionPipeline : ISessionPipelineProcessing
{
    private readonly object _sync = new();
    private readonly Queue<Func<DocumentSnapshot, DocumentScopeId, EditorStateSnapshot,
        CancellationToken,
        ValueTask<EditingSessionPipelineResult>>> _full = [];
    private readonly Queue<Func<DocumentSnapshot, EditingSessionPipelineArtifacts,
        EditorStateSnapshot, CancellationToken, ValueTask<EditingSessionPipelineResult>>>
        _preservingNodeLayout = [];
    private readonly Queue<Func<DocumentSnapshot, EditingSessionPipelineArtifacts,
        EditorStateSnapshot, CancellationToken, ValueTask<EditingSessionPipelineResult>>>
        _preservingScopeLayout = [];
    private readonly Queue<Func<EditingSessionPipelineArtifacts, VisualModelSnapshot,
        EditorStateSnapshot, CancellationToken, ValueTask<EditingSessionPipelineResult>>> _scene = [];

    internal int FullRunCount { get; private set; }

    internal int PreservingNodeLayoutRunCount { get; private set; }

    internal int PreservingScopeLayoutRunCount { get; private set; }

    internal int DocumentRunCount =>
        FullRunCount + PreservingNodeLayoutRunCount + PreservingScopeLayoutRunCount;

    internal int SceneRebuildCount { get; private set; }

    internal List<DocumentSnapshot> FullDocuments { get; } = [];

    internal List<DocumentSnapshot> DocumentRuns { get; } = [];

    internal List<DocumentScopeId> FullRunScopeIds { get; } = [];

    internal List<DocumentScopeId> DocumentRunScopeIds { get; } = [];

    internal List<EditorStateSnapshot> FullEditorStates { get; } = [];

    internal List<EditorStateSnapshot> DocumentEditorStates { get; } = [];

    internal List<NodeGeometryPipelineImpact> NodeGeometryImpacts { get; } = [];

    internal List<EditingSessionPipelineArtifacts> SceneArtifacts { get; } = [];

    internal List<EditorStateSnapshot> SceneEditorStates { get; } = [];

    internal void EnqueueFull(EditingSessionPipelineResult result) =>
        EnqueueFull((_, _, _) => ValueTask.FromResult(result));

    internal void EnqueueFull(
        Func<DocumentSnapshot, EditorStateSnapshot, CancellationToken,
            ValueTask<EditingSessionPipelineResult>> execution)
    {
        lock (_sync)
        {
            _full.Enqueue((document, _, editorState, cancellationToken) =>
                execution(document, editorState, cancellationToken));
        }
    }

    internal void EnqueueScopedFull(
        Func<DocumentSnapshot, DocumentScopeId, EditorStateSnapshot, CancellationToken,
            ValueTask<EditingSessionPipelineResult>> execution)
    {
        lock (_sync)
        {
            _full.Enqueue(execution);
        }
    }

    internal void EnqueueScene(EditingSessionPipelineResult result) =>
        EnqueueScene((_, _, _, _) => ValueTask.FromResult(result));

    internal void EnqueuePreservingNodeLayout(EditingSessionPipelineResult result) =>
        EnqueuePreservingNodeLayout((_, _, _, _) => ValueTask.FromResult(result));

    internal void EnqueuePreservingNodeLayout(
        Func<DocumentSnapshot, EditingSessionPipelineArtifacts, EditorStateSnapshot,
            CancellationToken, ValueTask<EditingSessionPipelineResult>> execution)
    {
        lock (_sync)
        {
            _preservingNodeLayout.Enqueue(execution);
        }
    }

    internal void EnqueuePreservingScopeLayout(
        Func<DocumentSnapshot, EditingSessionPipelineArtifacts, EditorStateSnapshot,
            CancellationToken, ValueTask<EditingSessionPipelineResult>> execution)
    {
        lock (_sync)
        {
            _preservingScopeLayout.Enqueue(execution);
        }
    }

    internal void EnqueueScene(
        Func<EditingSessionPipelineArtifacts, VisualModelSnapshot, EditorStateSnapshot,
            CancellationToken, ValueTask<EditingSessionPipelineResult>> execution)
    {
        lock (_sync)
        {
            _scene.Enqueue(execution);
        }
    }

    public ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken)
    {
        Func<DocumentSnapshot, DocumentScopeId, EditorStateSnapshot, CancellationToken,
            ValueTask<EditingSessionPipelineResult>> execution;
        lock (_sync)
        {
            FullRunCount++;
            FullDocuments.Add(document);
            FullRunScopeIds.Add(activeScopeId);
            FullEditorStates.Add(editorState);
            DocumentRuns.Add(document);
            DocumentRunScopeIds.Add(activeScopeId);
            DocumentEditorStates.Add(editorState);
            execution = _full.Dequeue();
        }

        return execution(document, activeScopeId, editorState, cancellationToken);
    }

    public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken)
    {
        Func<DocumentSnapshot, EditingSessionPipelineArtifacts, EditorStateSnapshot,
            CancellationToken, ValueTask<EditingSessionPipelineResult>>? preservingExecution;
        Func<DocumentSnapshot, EditorStateSnapshot, CancellationToken,
            ValueTask<EditingSessionPipelineResult>>? fullExecution = null;
        lock (_sync)
        {
            PreservingNodeLayoutRunCount++;
            NodeGeometryImpacts.Add(nodeGeometryImpact);
            DocumentRuns.Add(document);
            DocumentRunScopeIds.Add(activeScopeId);
            DocumentEditorStates.Add(editorState);
            if (_preservingNodeLayout.Count != 0)
            {
                preservingExecution = _preservingNodeLayout.Dequeue();
            }
            else
            {
                preservingExecution = null;
                var scopedFullExecution = _full.Dequeue();
                fullExecution = (fullDocument, fullEditorState, fullCancellationToken) =>
                    scopedFullExecution(
                        fullDocument,
                        activeScopeId,
                        fullEditorState,
                        fullCancellationToken);
            }
        }

        return preservingExecution is not null
            ? preservingExecution(document, previousArtifacts, editorState, cancellationToken)
            : fullExecution!(document, editorState, cancellationToken);
    }

    public ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken)
    {
        Func<DocumentSnapshot, EditingSessionPipelineArtifacts, EditorStateSnapshot,
            CancellationToken, ValueTask<EditingSessionPipelineResult>>? preservingExecution;
        Func<DocumentSnapshot, EditorStateSnapshot, CancellationToken,
            ValueTask<EditingSessionPipelineResult>>? fullExecution = null;
        lock (_sync)
        {
            PreservingScopeLayoutRunCount++;
            DocumentRuns.Add(document);
            DocumentRunScopeIds.Add(activeScopeId);
            DocumentEditorStates.Add(editorState);
            if (_preservingScopeLayout.Count != 0)
            {
                preservingExecution = _preservingScopeLayout.Dequeue();
            }
            else
            {
                preservingExecution = null;
                var scopedFullExecution = _full.Dequeue();
                fullExecution = (fullDocument, fullEditorState, fullCancellationToken) =>
                    scopedFullExecution(
                        fullDocument,
                        activeScopeId,
                        fullEditorState,
                        fullCancellationToken);
            }
        }

        return preservingExecution is not null
            ? preservingExecution(document, previousArtifacts, editorState, cancellationToken)
            : fullExecution!(document, editorState, cancellationToken);
    }

    public ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        EditingSessionPipelineArtifacts artifacts,
        Inceptus.DocumentEngine.Contracts.Visuals.VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken)
    {
        Func<EditingSessionPipelineArtifacts, VisualModelSnapshot, EditorStateSnapshot,
            CancellationToken, ValueTask<EditingSessionPipelineResult>> execution;
        lock (_sync)
        {
            SceneRebuildCount++;
            SceneArtifacts.Add(artifacts);
            SceneEditorStates.Add(editorState);
            execution = _scene.Dequeue();
        }

        return execution(artifacts, visualModel, editorState, cancellationToken);
    }

    internal static EditingSessionPipelineResult Success(
        Canvas2DSceneTestData inputs,
        EditorStateSnapshot? editorState = null) =>
        EditingSessionPipelineResult.Success(
            EditingSessionTestHarness.CreateArtifacts(inputs),
            EditingSessionTestHarness.CreateScene(inputs, editorState));

    internal static EditingSessionPipelineResult Success(
        Canvas2DSceneTestData inputs,
        DocumentScopeId scopeId,
        EditorStateSnapshot? editorState = null)
    {
        var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, scopeId);
        return EditingSessionPipelineResult.Success(
            artifacts,
            EditingSessionTestHarness.CreateScene(
                artifacts,
                inputs.VisualModel,
                editorState ?? inputs.EditorState));
    }

    internal static EditingSessionPipelineResult Success(
        EditingSessionPipelineArtifacts artifacts,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState) =>
        EditingSessionPipelineResult.Success(
            artifacts,
            EditingSessionTestHarness.CreateScene(artifacts, visualModel, editorState));

    internal static EditingSessionPipelineResult Failure(string code = "TEST_PIPELINE_FAILURE") =>
        EditingSessionPipelineResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, "Test pipeline failure."),
        ]);
}
