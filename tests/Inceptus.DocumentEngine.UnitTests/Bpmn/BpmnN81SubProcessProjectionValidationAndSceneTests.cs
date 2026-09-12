using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN81SubProcessProjectionValidationAndSceneTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";
    private static readonly DocumentId DocumentId = new("bpmn:n8.1:projection-document");
    private static readonly DocumentRevision Revision = new(8);
    private static readonly SemanticElementId SubProcessId =
        new("bpmn:n8.1:process-order");
    private static readonly VisualStateId SubProcessVisualId =
        new("bpmn:n8.1:process-order:visual");
    private static readonly DocumentScopeId ChildScopeId =
        new("bpmn:n8.1:process-order:scope");

    [Fact]
    public void PropertiesSchemaExposesOnlyCodeNameAndDescriptionAsEditableData()
    {
        var schema = Assert.Single(
            BpmnPluginRegistration.N81.PropertiesSchemas,
            candidate => candidate.SemanticTypeId == BpmnSemanticTypes.SubProcess);

        Assert.Equal(
            ["Code", "Name", "Description"],
            schema.Fields.Select(static field => field.DisplayName));
        Assert.Equal(
            [
                BpmnSemanticProperties.Code,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.Description,
            ],
            schema.Fields.Select(static field => field.SemanticPropertyKey));
        Assert.Equal(
            [
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.MultilineText,
            ],
            schema.Fields.Select(static field => field.EditorKind));
        Assert.All(schema.Fields, static field => Assert.True(field.IsEditable));
        Assert.DoesNotContain(schema.Fields, field =>
            field.SemanticPropertyKey == BpmnSemanticProperties.ElementNumber ||
            field.SemanticPropertyKey.Contains("Scope", StringComparison.OrdinalIgnoreCase) ||
            field.SemanticPropertyKey.Contains("Expanded", StringComparison.OrdinalIgnoreCase) ||
            field.SemanticPropertyKey.Contains("Runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RootProjectionIncludesTheCompactOwnerAndExcludesAllChildScopeContent()
    {
        var childStart = BpmnSemanticFactory.CreateStartEvent(
            new SemanticElementId("bpmn:n8.1:child:start"),
            "Child Start");
        var childTask = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n8.1:child:user-task"),
            BpmnSemanticTypes.UserTask,
            "CHILD_TASK",
            "Child Task",
            1);
        var childEnd = BpmnSemanticFactory.CreateEndEvent(
            new SemanticElementId("bpmn:n8.1:child:end"),
            "Child End");
        var childFlowA = BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId("bpmn:n8.1:child:flow-a"),
            childStart.Id,
            childTask.Id);
        var childFlowB = BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId("bpmn:n8.1:child:flow-b"),
            childTask.Id,
            childEnd.Id);
        var snapshot = Snapshot(
            [SubProcess(), childStart, childTask, childEnd],
            [childFlowA, childFlowB],
            [
                NodeVisual(SubProcessVisualId, SubProcessId, new PointD(60d, 40d)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:child:start:visual"),
                    childStart.Id,
                    new PointD(2000d, 2000d)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:child:user-task:visual"),
                    childTask.Id,
                    new PointD(2400d, 2200d)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:child:end:visual"),
                    childEnd.Id,
                    new PointD(2800d, 2400d)),
            ],
            [new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId(DocumentId.Value),
                SubProcessId)],
            [
                new SemanticElementScopeMembershipSnapshot(childStart.Id, ChildScopeId),
                new SemanticElementScopeMembershipSnapshot(childTask.Id, ChildScopeId),
                new SemanticElementScopeMembershipSnapshot(childEnd.Id, ChildScopeId),
            ]);

        var result = new ProjectionEngine(BpmnPluginRegistration.N81.ProjectionRules)
            .Project(snapshot);

        Assert.True(result.IsSuccessful, Diagnostics(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        var graph = Assert.IsType<ProjectedGraph>(result.Graph);
        var node = Assert.Single(graph.Nodes);
        Assert.Equal(SubProcessId, node.Source.SemanticElementId);
        Assert.Equal(SubProcessVisualId, node.Source.VisualStateId);
        Assert.Equal(BpmnSemanticTypes.SubProcess, node.Source.SemanticTypeId);
        var label = Assert.Single(graph.Labels);
        Assert.Equal("Process order", label.Text);
        Assert.Null(label.NodePlacement);
        Assert.Equal(NodeLabelInteractionPolicy.Fixed, label.NodeInteractionPolicy);
        Assert.Empty(graph.Edges);
        Assert.DoesNotContain(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == childStart.Id ||
            candidate.Source.SemanticElementId == childTask.Id ||
            candidate.Source.SemanticElementId == childEnd.Id);
        Assert.DoesNotContain(graph.Labels, candidate =>
            candidate.Source.SemanticElementId == childStart.Id ||
            candidate.Source.SemanticElementId == childTask.Id ||
            candidate.Source.SemanticElementId == childEnd.Id);
    }

    [Fact]
    public void N5ReportsMissingChildScopeAndNonSubProcessScopeOwnerDeterministically()
    {
        var missingScopeIssues = Validate(Snapshot(
            [SubProcess()],
            relationships: null,
            visuals: null));
        var missing = Assert.Single(missingScopeIssues, issue =>
            issue.Code == BpmnModelValidationCodes.SubProcessChildScopeMissing);
        Assert.Equal(ModelValidationSeverity.Error, missing.Severity);
        Assert.Equal(SubProcessId, missing.Target.SemanticElementId);
        Assert.Contains(
            "SubProcess \"Process order\" [Code: PROCESS_ORDER, ID: bpmn:n8.1:process-order]",
            missing.Message,
            StringComparison.Ordinal);

        var task = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n8.1:invalid-scope-owner"),
            "TASK_OWNER",
            "Task owner",
            7);
        var invalidOwnerScopeId = new DocumentScopeId("bpmn:n8.1:invalid-owner-scope");
        var invalidOwnerIssues = Validate(Snapshot(
            [task],
            relationships: null,
            visuals: null,
            nestedScopes:
            [
                new DocumentScopeSnapshot(
                    invalidOwnerScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    task.Id),
            ]));
        var invalidOwner = Assert.Single(invalidOwnerIssues, issue =>
            issue.Code == BpmnModelValidationCodes.ProcessScopeOwnerInvalid);
        Assert.Equal(ModelValidationSeverity.Error, invalidOwner.Severity);
        Assert.Equal(task.Id, invalidOwner.Target.SemanticElementId);
        Assert.Equal(invalidOwnerScopeId.Value, invalidOwner.Discriminator);
        Assert.Contains("only SubProcess may own", invalidOwner.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void N5TreatsRootSubProcessAsAnOrdinaryFlowNodeWithoutFlatteningItsChildScope()
    {
        var start = BpmnSemanticFactory.CreateStartEvent(
            new SemanticElementId("bpmn:n8.1:root:start"),
            "Start");
        var subProcess = SubProcess();
        var end = BpmnSemanticFactory.CreateEndEvent(
            new SemanticElementId("bpmn:n8.1:root:end"),
            "End");
        var childTask = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n8.1:child:isolated"),
            "CHILD_ISOLATED",
            "Child Isolated",
            1);
        var childEventBasedGateway = BpmnSemanticFactory.CreateEventBasedGateway(
            new SemanticElementId("bpmn:n8.1:child:event-based-gateway"),
            "CHILD_EVENT_BASED",
            "Child Event-Based Gateway");
        var scopes = new[]
        {
            new DocumentScopeSnapshot(
                ChildScopeId,
                new DocumentScopeId(DocumentId.Value),
                SubProcessId),
        };
        var rootFlows = new[]
        {
            BpmnSemanticFactory.CreateSequenceFlow(
                new SemanticElementId("bpmn:n8.1:root:start-subprocess"),
                start.Id,
                subProcess.Id),
            BpmnSemanticFactory.CreateSequenceFlow(
                new SemanticElementId("bpmn:n8.1:root:subprocess-end"),
                subProcess.Id,
                end.Id),
        };

        var emptyChildIssues = Validate(Snapshot(
            [start, subProcess, end],
            rootFlows,
            visuals: null,
            nestedScopes: scopes));
        Assert.Empty(emptyChildIssues);

        var populatedChildIssues = Validate(Snapshot(
            [start, subProcess, end, childEventBasedGateway, childTask],
            [
                .. rootFlows,
                BpmnSemanticFactory.CreateSequenceFlow(
                    new SemanticElementId("bpmn:n8.1:child:invalid-event-target"),
                    childEventBasedGateway.Id,
                    childTask.Id),
            ],
            visuals: null,
            nestedScopes: scopes,
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(
                    childEventBasedGateway.Id,
                    ChildScopeId),
                new SemanticElementScopeMembershipSnapshot(childTask.Id, ChildScopeId),
            ]));
        Assert.Empty(populatedChildIssues);
    }

    [Fact]
    public void N51FormatsTheFullSubProcessIdentityInFlowNodeIssues()
    {
        var start = BpmnSemanticFactory.CreateStartEvent(
            new SemanticElementId("bpmn:n8.1:formatter:start"),
            "Start");
        var end = BpmnSemanticFactory.CreateEndEvent(
            new SemanticElementId("bpmn:n8.1:formatter:end"),
            "End");
        var direct = BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId("bpmn:n8.1:formatter:direct"),
            start.Id,
            end.Id);
        var issues = Validate(Snapshot(
            [start, SubProcess(), end],
            [direct],
            visuals: null,
            nestedScopes:
            [
                new DocumentScopeSnapshot(
                    ChildScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    SubProcessId),
            ]));

        var isolated = Assert.Single(issues, issue =>
            issue.Code == BpmnModelValidationCodes.FlowNodeIsolated &&
            issue.Target.SemanticElementId == SubProcessId);
        Assert.Equal(
            "SubProcess \"Process order\" [Code: PROCESS_ORDER, ID: bpmn:n8.1:process-order] is isolated from every Sequence Flow.",
            isolated.Message);
    }

    [Fact]
    public void CompactSceneUsesTheRoundedActivityBodyAndTwoDerivedNonHittableMarkers()
    {
        var node = ProjectedSubProcess();
        var bounds = new RectD(40d, 30d, 120d, 80d);
        var graph = new ProjectedGraph(DocumentId, Revision, nodes: [node]);
        var layout = new LayoutResult(
            DocumentId,
            Revision,
            BpmnAlgorithmIds.DefaultLayout,
            new LayoutComputation(
            [
                new LayoutNodeGeometry(
                    node.Id,
                    bounds,
                    Matrix2D.CreateTranslation(bounds.X, bounds.Y)),
            ]));
        var routing = new RoutingResult(
            DocumentId,
            Revision,
            BpmnAlgorithmIds.DefaultLayout,
            BpmnAlgorithmIds.DefaultRouting,
            RoutingComputation.Empty);
        var registration = Assert.Single(
            BpmnPluginRegistration.N81.SceneContributors);
        var result = registration.Contributor.Contribute(
            new Canvas2DSceneContributionContext(
                graph,
                layout,
                routing,
                new VisualModelSnapshot(
                    DocumentId,
                    Revision,
                    [NodeVisual(SubProcessVisualId, SubProcessId, bounds.TopLeft)]),
                EditorStateSnapshot.Empty,
                Canvas2DSceneConfiguration.Default,
                registration.Descriptor));

        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        var contribution = Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
        var body = Assert.Single(contribution.CanonicalItemVisualOverrides);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.True(body.Geometry.IsClosed);
        Assert.True(body.Geometry.Points.Length > 4);
        Assert.Equal(new RectD(0d, 0d, 120d, 80d), body.Geometry.Bounds);
        Assert.Equal("#ffffff", body.Style.Fill);
        Assert.Equal("#000000", body.Style.Stroke);

        Assert.Equal(2, contribution.Items.Length);
        Assert.All(contribution.Items, marker =>
        {
            Assert.Equal(Canvas2DSceneLayer.Decoration, marker.Layer);
            Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
            Assert.True(marker.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));
            Assert.Equal(SubProcessId, marker.Origin.SemanticElementId);
            Assert.Equal(node.Id, marker.Origin.ProjectedObjectId);
            Assert.Empty(marker.PersistentAppearance);
            Assert.Empty(marker.Metadata);
            Assert.Equal(Canvas2DSceneGeometryKind.Path, marker.Geometry.Kind);
            Assert.True(marker.Geometry.IsClosed);
            Assert.True(IsFinite(marker.Geometry.Bounds));
            Assert.True(marker.Geometry.Bounds.Left >= 0d);
            Assert.True(marker.Geometry.Bounds.Top >= 0d);
            Assert.True(marker.Geometry.Bounds.Right <= 120d);
            Assert.True(marker.Geometry.Bounds.Bottom <= 80d);
        });
        var markerBox = Assert.Single(contribution.Items, marker =>
            marker.Style.Fill == "#ffffff" && marker.Style.Stroke == "#000000");
        var markerPlus = Assert.Single(contribution.Items, marker =>
            marker.Style.Fill == "#000000" && marker.Style.Stroke is null);
        Assert.True(markerPlus.Geometry.Bounds.Left > markerBox.Geometry.Bounds.Left);
        Assert.True(markerPlus.Geometry.Bounds.Top > markerBox.Geometry.Bounds.Top);
        Assert.True(markerPlus.Geometry.Bounds.Right < markerBox.Geometry.Bounds.Right);
        Assert.True(markerPlus.Geometry.Bounds.Bottom < markerBox.Geometry.Bounds.Bottom);
    }

    private static SemanticElementSnapshot SubProcess() =>
        BpmnSemanticFactory.CreateSubProcess(
            SubProcessId,
            "PROCESS_ORDER",
            "Process order",
            "Processes one order.");

    private static ProjectedNode ProjectedSubProcess()
    {
        var trace = new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId("bpmn:n8.1:test:subprocess"),
            ProjectionSourceKind.SemanticElement,
            SubProcessId,
            BpmnSemanticTypes.SubProcess,
            "node",
            SubProcessVisualId);
        return new ProjectedNode(
            trace,
            projectedProperties:
            [
                new(
                    ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(BpmnSemanticTypes.SubProcess.Value)),
            ]);
    }

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue> Validate(
        DocumentSnapshot snapshot) =>
        new ModelValidationEngine(new ModelValidationCatalog(
            BpmnPluginRegistration.N81.ModelValidationRules))
            .Validate(new ModelValidationContext(snapshot))
            .Issues;

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? relationships,
        IEnumerable<VisualStateSnapshot>? visuals,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                elements,
                relationships,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(DocumentId, Revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned);

    private static bool IsFinite(RectD bounds) =>
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height);

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));
}
