using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Application-only neutral composition for the complete processing pipeline owned by EditingSession.
/// It is not framework domain behavior and is never used by upstream engine projects.
/// </summary>
internal static class NeutralDemoPipeline
{
    internal const string LabelPropertyKey = "demo:label";
    internal const string ElementNumberPropertyKey = "demo:element-number";
    internal const string DescriptionPropertyKey = "demo:description";
    private const string FillKey = "demo:fill";

    private static readonly DocumentId DemoDocumentId = new("demo:neutral-document");
    private static readonly SemanticTypeId NodeTypeId = new("demo:neutral-node");
    private static readonly SemanticTypeId MixedPolicyNodeTypeId =
        new("demo:neutral-mixed-anchor-node");
    private static readonly SemanticTypeId EdgeTypeId = new("demo:neutral-edge");
    private static readonly ProjectionRuleId NodeRuleId = new("demo:projection:node");
    private static readonly ProjectionRuleId MixedPolicyNodeRuleId =
        new("demo:projection:mixed-anchor-node");
    private static readonly ProjectionRuleId EdgeRuleId = new("demo:projection:edge");
    private static readonly AlgorithmId LayoutAlgorithmId = new("demo:layout:placement-hints");
    private static readonly AlgorithmId RoutingAlgorithmId = new("demo:routing:direct");
    private static readonly Canvas2DSceneContributorId ContributorId = new("demo:scene:appearance");
    private static readonly ElementConnectorAnchorPolicyRegistry DemoPolicyProvider = new(
    [
        new ElementConnectorAnchorPolicyRegistration(
            MixedPolicyNodeTypeId,
            CreateMixedPolicy()),
    ]);

    internal static SemanticTypeId NeutralNodeTypeId => NodeTypeId;

    internal static SemanticTypeId NeutralMixedPolicyNodeTypeId => MixedPolicyNodeTypeId;

    internal static SemanticTypeId NeutralEdgeTypeId => EdgeTypeId;

    internal static ElementConnectorAnchorPolicyRegistry ConnectorAnchorPolicyProvider =>
        DemoPolicyProvider;

    internal static NeutralDemoComposition CreateComposition(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        connectorAnchorPolicyProvider ??= DemoPolicyProvider;
        var alphaId = new SemanticElementId("demo:alpha");
        var betaId = new SemanticElementId("demo:beta");
        var gammaId = new SemanticElementId("demo:gamma");
        var alphaBetaId = new SemanticElementId("demo:alpha-beta");
        var betaGammaId = new SemanticElementId("demo:beta-gamma");

        var elements = new[]
        {
            Element(alphaId, "Alpha", 10L, "Initial Alpha description"),
            Element(betaId, "Beta", 20L, "Initial Beta description"),
            Element(
                gammaId,
                "Gamma",
                30L,
                "Initial Gamma description",
                MixedPolicyNodeTypeId),
        };
        var relationships = new[]
        {
            Relationship(alphaBetaId, alphaId, betaId, "Initial Alpha to Beta description"),
            Relationship(betaGammaId, betaId, gammaId, "Initial Beta to Gamma description"),
        };
        var visuals = new[]
        {
            Visual("demo:visual:alpha", alphaId, 60d, 70d, 150d, 70d, "#dbeafe"),
            Visual("demo:visual:beta", betaId, 300d, 190d, 160d, 80d, "#dcfce7"),
            Visual("demo:visual:gamma", gammaId, 560d, 90d, 150d, 70d, "#fef3c7"),
            new VisualStateSnapshot(
                new VisualStateId("demo:visual:alpha-beta"),
                alphaBetaId,
                default,
                default,
                VisualPlacementMode.Automatic),
            new VisualStateSnapshot(
                new VisualStateId("demo:visual:beta-gamma"),
                betaGammaId,
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Manual,
                route:
                [
                    new PointD(460d, 230d),
                    new PointD(510d, 160d),
                    new PointD(560d, 125d),
                ]),
        };
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(DemoDocumentId, DocumentRevision.Zero, elements, relationships),
            new VisualModelSnapshot(DemoDocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DemoDocumentId, DocumentRevision.Zero));
        var creation = DocumentFactory.Create(
            snapshot,
            connectorAnchorPolicyProvider);
        var document = creation.Document ?? throw new InvalidOperationException(
            $"Neutral demo Document creation failed: {FirstCode(creation.Diagnostics)}");
        var counters = new NeutralDemoPipelineCounters();
        var projectionEngine = new ProjectionEngine(
        [
            new ProjectionRuleRegistration(
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                new NeutralNodeProjectionRule(
                    counters,
                    connectorAnchorPolicyProvider,
                    NodeRuleId)),
            new ProjectionRuleRegistration(
                MixedPolicyNodeRuleId,
                ProjectionSourceKind.SemanticElement,
                MixedPolicyNodeTypeId,
                new NeutralNodeProjectionRule(
                    counters,
                    connectorAnchorPolicyProvider,
                    MixedPolicyNodeRuleId)),
            new ProjectionRuleRegistration(
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                EdgeTypeId,
                new NeutralEdgeProjectionRule(counters)),
        ]);
        var layoutEngine = new LayoutEngine(
        [
            new LayoutAlgorithmRegistration(
                LayoutAlgorithmId,
                new PlacementHintLayoutAlgorithm(counters)),
        ]);
        var routingEngine = new RoutingEngine(
        [
            new RoutingAlgorithmRegistration(
                RoutingAlgorithmId,
                new DirectRoutingAlgorithm(counters)),
        ]);

        var editorState = new EditorStateSnapshot(
            selection: [new VisualStateId("demo:visual:alpha")],
            viewport: new ViewportSnapshot(1.08d, new VectorD(30d, 18d)));
        var sceneBuilder = new Canvas2DSceneBuilder(
            contributors:
            [
                new Canvas2DSceneContributorRegistration(
                    new Canvas2DSceneContributorDescriptor(ContributorId, "1"),
                    new PersistentAppearanceContributor(counters)),
            ]);
        var configuration = new EditingSessionConfiguration(
            projectionEngine,
            layoutEngine,
            LayoutAlgorithmId,
            routingEngine,
            RoutingAlgorithmId,
            sceneBuilder,
            initialEditorState: editorState,
            connectorAnchorPolicyProvider: connectorAnchorPolicyProvider);

        return new NeutralDemoComposition(
            document,
            configuration,
            counters);
    }

    private static SemanticElementSnapshot Element(
        SemanticElementId id,
        string label,
        long elementNumber,
        string description,
        SemanticTypeId? typeId = null) =>
        new(
            id,
            typeId ?? NodeTypeId,
            [
                new(LabelPropertyKey, PropertyValue.FromText(label)),
                new(ElementNumberPropertyKey, PropertyValue.FromInteger(elementNumber)),
                new(DescriptionPropertyKey, PropertyValue.FromText(description)),
            ]);

    private static ElementConnectorAnchorPolicy CreateMixedPolicy() =>
        new(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined(
            [
                new PredefinedConnectorAnchorDefinition(
                    new PredefinedConnectorAnchorDefinitionId(
                        "demo:mixed-anchor:right:source"),
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Source,
                    0),
                new PredefinedConnectorAnchorDefinition(
                    new PredefinedConnectorAnchorDefinitionId(
                        "demo:mixed-anchor:right:target"),
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Target,
                    1),
            ]),
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.DynamicUnlimited());

    private static VisualStateSnapshot Visual(
        string visualId,
        SemanticElementId semanticElementId,
        double x,
        double y,
        double width,
        double height,
        string fill) =>
        new(
            new VisualStateId(visualId),
            semanticElementId,
            new PointD(x, y),
            new SizeD(width, height),
            VisualPlacementMode.Manual,
            properties: [new(FillKey, PropertyValue.FromText(fill))]);

    private static SemanticRelationshipSnapshot Relationship(
        SemanticElementId id,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        string description) =>
        new(
            id,
            EdgeTypeId,
            sourceId,
            targetId,
            [
                new(LabelPropertyKey, PropertyValue.FromText(string.Empty)),
                new(DescriptionPropertyKey, PropertyValue.FromText(description)),
            ]);

    private static string FirstCode(IEnumerable<Diagnostic> diagnostics)
    {
        var diagnostic = diagnostics.FirstOrDefault();
        return diagnostic is null
            ? "unknown failure"
            : $"{diagnostic.Code}: {diagnostic.Message}";
    }

    private static ProjectionSourceTrace Trace(
        ProjectionRuleInput input,
        ProjectionRuleId ruleId,
        string localKey,
        VisualStateId? visualStateId = null) =>
        new(
            input.DocumentId,
            ruleId,
            input.SourceKind,
            input.SemanticId,
            input.SemanticTypeId,
            localKey,
            visualStateId);

    private sealed class NeutralNodeProjectionRule : IProjectionRule
    {
        private readonly NeutralDemoPipelineCounters _counters;
        private readonly IElementConnectorAnchorPolicyProvider _connectorAnchorPolicyProvider;
        private readonly ProjectionRuleId _ruleId;

        internal NeutralNodeProjectionRule(
            NeutralDemoPipelineCounters counters,
            IElementConnectorAnchorPolicyProvider connectorAnchorPolicyProvider,
            ProjectionRuleId ruleId)
        {
            _counters = counters;
            _connectorAnchorPolicyProvider = connectorAnchorPolicyProvider;
            _ruleId = ruleId;
        }

        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _counters.RecordProjectionRuleInvocation();
            var element = (ElementProjectionRuleInput)input;
            var visual = element.VisualStates.SingleOrDefault();
            var node = new ProjectedNode(
                Trace(input, _ruleId, "node", visual?.Id),
                visual is null
                    ? null
                    : new ProjectedPlacementHint(
                        visual.Position,
                        visual.Size,
                        visual.PlacementMode),
                semanticProperties: element.Element.Properties);
            var anchors = visual is null
                ? []
                : ElementConnectorAnchorResolver.Resolve(
                    visual,
                    element.Element.TypeId,
                    _connectorAnchorPolicyProvider);
            var anchorCounts = anchors
                .GroupBy(static anchor => anchor.Side)
                .ToDictionary(static group => group.Key, static group => group.Count());
            var ports = anchors.Select(anchor => new ProjectedPort(
                Trace(input, _ruleId, AnchorLocalKey(anchor.Id), visual!.Id),
                node.Id,
                routingHints: ProjectedConnectorAnchorMetadata.Encode(
                    ProjectedConnectorAnchor.FromResolved(
                        anchor,
                        anchorCounts[anchor.Side]))));
            var label = element.Element.Properties.TryGetValue(
                LabelPropertyKey,
                out var labelValue)
                ? labelValue.TextValue
                : element.Element.Id.Value;
            return ProjectionRuleResult.Success(new ProjectionRuleContribution(
                nodes: [node],
                ports: ports,
                labels:
                [
                    new ProjectedLabel(
                        Trace(input, _ruleId, "label", visual?.Id),
                        node.Id,
                        label),
                ]));
        }
    }

    private sealed class NeutralEdgeProjectionRule : IProjectionRule
    {
        private readonly NeutralDemoPipelineCounters _counters;

        internal NeutralEdgeProjectionRule(NeutralDemoPipelineCounters counters) =>
            _counters = counters;

        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _counters.RecordProjectionRuleInvocation();
            var relationship = (RelationshipProjectionRuleInput)input;
            var visual = relationship.VisualStates.SingleOrDefault();
            var edge = new ProjectedEdge(
                Trace(input, EdgeRuleId, "edge", visual?.Id),
                NodeId(relationship.SourceElement),
                NodeId(relationship.TargetElement),
                sourcePortId: visual?.SourceAnchorId is { } sourceAnchorId
                    ? AnchorPortId(relationship.SourceElement, sourceAnchorId)
                    : null,
                targetPortId: visual?.TargetAnchorId is { } targetAnchorId
                    ? AnchorPortId(relationship.TargetElement, targetAnchorId)
                    : null,
                persistentRoute: visual?.Route,
                semanticProperties: relationship.Relationship.Properties);
            ProjectedLabel[] labels = relationship.Relationship.Properties.TryGetValue(
                    LabelPropertyKey,
                    out var name) &&
                name.Kind == PropertyValueKind.Text &&
                !string.IsNullOrWhiteSpace(name.TextValue)
                    ? new[]
                    {
                        new ProjectedLabel(
                            Trace(input, EdgeRuleId, "label", visual?.Id),
                            edge.Id,
                            name.TextValue,
                            semanticProperties: relationship.Relationship.Properties),
                    }
                    : [];
            return ProjectionRuleResult.Success(new ProjectionRuleContribution(
                edges: [edge],
                labels: labels));
        }

        private static ProjectedObjectId NodeId(SemanticElementSnapshot element) =>
            ProjectedObjectIdentity.Create(
                DemoDocumentId,
                NodeProjectionRuleId(element.TypeId),
                ProjectionSourceKind.SemanticElement,
                element.Id,
                ProjectedObjectKind.Node,
                "node");

        private static ProjectedObjectId AnchorPortId(
            SemanticElementSnapshot element,
            ConnectorAnchorId anchorId) =>
            ProjectedObjectIdentity.Create(
                DemoDocumentId,
                NodeProjectionRuleId(element.TypeId),
                ProjectionSourceKind.SemanticElement,
                element.Id,
                ProjectedObjectKind.Port,
                AnchorLocalKey(anchorId));

        private static ProjectionRuleId NodeProjectionRuleId(SemanticTypeId typeId) =>
            typeId == MixedPolicyNodeTypeId
                ? MixedPolicyNodeRuleId
                : NodeRuleId;
    }

    private sealed class PlacementHintLayoutAlgorithm : ILayoutAlgorithm
    {
        private readonly NeutralDemoPipelineCounters _counters;

        internal PlacementHintLayoutAlgorithm(NeutralDemoPipelineCounters counters) =>
            _counters = counters;

        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken)
        {
            _counters.RecordLayoutInvocation();
            var geometries = graph.Nodes.Select(node =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hint = node.PlacementHint ?? throw new InvalidOperationException(
                    $"Neutral node '{node.Id}' has no placement hint.");
                var bounds = new RectD(
                    hint.Position.X,
                    hint.Position.Y,
                    hint.Size.Width,
                    hint.Size.Height);
                return new LayoutNodeGeometry(
                    node.Id,
                    bounds,
                    Matrix2D.CreateTranslation(bounds.X, bounds.Y));
            });
            return LayoutAlgorithmResult.Success(new LayoutComputation(geometries));
        }
    }

    private sealed class DirectRoutingAlgorithm : IRoutingAlgorithm
    {
        private readonly NeutralDemoPipelineCounters _counters;

        internal DirectRoutingAlgorithm(NeutralDemoPipelineCounters counters) =>
            _counters = counters;

        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken)
        {
            _counters.RecordRoutingInvocation();
            var geometries = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
            var ports = graph.Ports.ToDictionary(static port => port.Id);
            var routes = graph.Edges.Select(edge =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = geometries[edge.SourceNodeId].Bounds;
                var target = geometries[edge.TargetNodeId].Bounds;
                var sourceAnchor = edge.SourcePortId is { } sourcePortId
                    ? ResolveAnchorPoint(
                        ports[sourcePortId],
                        source,
                        ConnectorAnchorRole.Source)
                    : new PointD(source.Right, source.Y + (source.Height / 2d));
                var destinationAnchor = edge.TargetPortId is { } targetPortId
                    ? ResolveAnchorPoint(
                        ports[targetPortId],
                        target,
                        ConnectorAnchorRole.Target)
                    : new PointD(target.Left, target.Y + (target.Height / 2d));
                if (edge.PersistentRoute.Length >= 2)
                {
                    return new RoutedConnectorGeometry(
                        edge.Id,
                        sourceAnchor,
                        destinationAnchor,
                        edge.PersistentRoute.Skip(1).SkipLast(1),
                        edge.SourcePortId,
                        edge.TargetPortId);
                }

                return new RoutedConnectorGeometry(
                    edge.Id,
                    sourceAnchor,
                    destinationAnchor,
                    sourcePortId: edge.SourcePortId,
                    targetPortId: edge.TargetPortId);
            });
            return RoutingAlgorithmResult.Success(new RoutingComputation(routes));
        }

        private static PointD ResolveAnchorPoint(
            ProjectedPort port,
            RectD bounds,
            ConnectorAnchorRole expectedRole)
        {
            if (!ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) ||
                anchor is null ||
                !anchor.Allows(expectedRole))
            {
                throw new InvalidOperationException(
                    $"Projected connector anchor '{port.Id}' has invalid routing metadata.");
            }

            return ConnectorAnchorGeometryResolver.ResolvePoint(
                bounds,
                anchor.Side,
                anchor.Order,
                anchor.SideCount);
        }
    }

    private static string AnchorLocalKey(ConnectorAnchorId anchorId) =>
        $"connector-anchor:{anchorId.Value}";

    private sealed class PersistentAppearanceContributor : ICanvas2DSceneContributor
    {
        private readonly NeutralDemoPipelineCounters _counters;

        internal PersistentAppearanceContributor(NeutralDemoPipelineCounters counters) =>
            _counters = counters;

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            _counters.RecordSceneContributionInvocation();
            var visuals = context.VisualModel.VisualStates.ToDictionary(static visual => visual.Id);
            var geometries = context.LayoutResult.Nodes.ToDictionary(static node => node.ProjectedObjectId);
            var items = new List<Canvas2DSceneItem>();
            foreach (var node in context.ProjectedGraph.Nodes)
            {
                if (node.Source.VisualStateId is null ||
                    !visuals.TryGetValue(node.Source.VisualStateId, out var visual) ||
                    !visual.Properties.TryGetValue(FillKey, out var fill))
                {
                    continue;
                }

                var geometry = geometries[node.Id];
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(
                        context.Contributor.ContributorId,
                        $"fill:{node.Id.Value}"),
                    Canvas2DSceneLayer.Content,
                    10,
                    Canvas2DSceneGeometry.Rectangle(new RectD(
                        0d,
                        0d,
                        geometry.Bounds.Width,
                        geometry.Bounds.Height)),
                    new Canvas2DSceneOriginTrace(
                        Canvas2DSceneOriginCategory.RegisteredExtension |
                        Canvas2DSceneOriginCategory.SemanticElement |
                        Canvas2DSceneOriginCategory.VisualState |
                        Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
                        node.Source.SemanticElementId,
                        visual.Id,
                        node.Id,
                        $"fill:{node.Id.Value}"),
                    transform: geometry.Transform,
                    style: new Canvas2DSceneStyle(fill.TextValue, "#334155", 2d),
                    hitTestPolicy: Canvas2DHitTestPolicy.None,
                    persistentAppearance: visual.Properties,
                    bounds: geometry.Bounds));
            }

            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(items));
        }
    }
}

internal sealed record NeutralDemoComposition(
    Document Document,
    EditingSessionConfiguration Configuration,
    NeutralDemoPipelineCounters Counters);

internal sealed class NeutralDemoPipelineCounters
{
    private int _projectionRuleInvocationCount;
    private int _layoutInvocationCount;
    private int _routingInvocationCount;
    private int _sceneContributionInvocationCount;

    internal int ProjectionRuleInvocationCount => Volatile.Read(ref _projectionRuleInvocationCount);

    internal int LayoutInvocationCount => Volatile.Read(ref _layoutInvocationCount);

    internal int RoutingInvocationCount => Volatile.Read(ref _routingInvocationCount);

    internal int SceneContributionInvocationCount => Volatile.Read(ref _sceneContributionInvocationCount);

    internal void RecordProjectionRuleInvocation() =>
        Interlocked.Increment(ref _projectionRuleInvocationCount);

    internal void RecordLayoutInvocation() => Interlocked.Increment(ref _layoutInvocationCount);

    internal void RecordRoutingInvocation() => Interlocked.Increment(ref _routingInvocationCount);

    internal void RecordSceneContributionInvocation() =>
        Interlocked.Increment(ref _sceneContributionInvocationCount);

}
