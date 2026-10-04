using Inceptus.DocumentEngine.Bpmn.Projection;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Bpmn.Scene;

/// <summary>
/// Maps the supported BPMN flow-node slice to notation-specific Canvas2D Scene geometry.
/// It consumes immutable pipeline results and replaces only canonical node appearance.
/// </summary>
public sealed class BpmnCanvas2DSceneContributor : ICanvas2DSceneContributor, ICanvas2DScopeGeometryContributor
{
    private const double StartEventStrokeWidth = 1.5d;
    private const double TaskStrokeWidth = 1.5d;
    private const double GatewayStrokeWidth = 1.5d;
    private const double EndEventStrokeWidth = 3.5d;
    private const double MaximumTaskCornerRadius = 12d;
    private const int RoundedCornerIntermediatePointCount = 3;
    private const int GatewayMarkerZIndex = 100;
    private const int TaskMarkerZIndex = 90;
    private const int SubProcessMarkerZIndex = 90;
    private const int BoundaryEventBodyAppearanceZIndex = GatewayMarkerZIndex - 1;
    private const double ExclusiveGatewayMarkerHalfExtentRatio = 0.21d;
    private const double ExclusiveGatewayMarkerThicknessRatio = 0.06d;
    private const double ParallelGatewayMarkerHalfExtentRatio = 0.23d;
    private const double ParallelGatewayMarkerThicknessRatio = 0.075d;
    private const double InclusiveGatewayMarkerRadiusRatio = 0.18d;
    private const double InclusiveGatewayMarkerStrokeWidthRatio = 0.055d;
    private const double IntermediateEventInnerRingInsetRatio = 0.105d;
    private const double EventMarkerStrokeWidthRatio = 0.04d;
    private const double EventBasedGatewayOuterRingRadiusRatio = 0.24d;
    private const double EventBasedGatewayInnerRingRadiusRatio = 0.18d;

    private static readonly Canvas2DSceneContributorDescriptor Descriptor = new(
        new Canvas2DSceneContributorId("bpmn:scene/flow-node-visuals"),
        "1",
        Canvas2DScenePanDependency.Invariant,
        Canvas2DSceneMoveGestureDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant);

    private static readonly Canvas2DSceneContributorDescriptor NodesDescriptor = new(
        Descriptor.ContributorId, Descriptor.Version, Canvas2DScenePanDependency.Invariant,
        Canvas2DSceneMoveGestureDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant, Canvas2DScenePlacementDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant);

    private static readonly Canvas2DSceneContributorDescriptor PlacementDescriptor = new(
        new Canvas2DSceneContributorId("bpmn:scene/placement-feedback"), "1",
        Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
        Canvas2DScenePlacementDependency.BoundedFeedbackOnly,
        Canvas2DSceneTransientDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant,
        Canvas2DSceneTransientDependency.Invariant);

    internal static Canvas2DSceneContributorRegistration PlacementRegistration { get; } =
        new(PlacementDescriptor, new PlacementContributor());

    internal static Canvas2DSceneContributorRegistration Registration { get; } =
        new(Descriptor, new BpmnCanvas2DSceneContributor());

    internal static Canvas2DSceneContributorRegistration NodesRegistration { get; } =
        new(NodesDescriptor, new NodesContributor());

    public Canvas2DSceneContributionResult Contribute(
        Canvas2DSceneContributionContext context) => ContributeNodes(context, includeLegacyFeedback: true);

    private static Canvas2DSceneContributionResult ContributeNodes(
        Canvas2DSceneContributionContext context, bool includeLegacyFeedback)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ContributeNodes(context.ProjectedGraph, context.LayoutResult, context.EditorState, includeLegacyFeedback);
    }

    private static Canvas2DSceneContributionResult ContributeNodes(
        ProjectedGraph graph, LayoutResult localLayout, EditorStateSnapshot editorState, bool includeLegacyFeedback)
    {

        var layouts = localLayout.Nodes.ToDictionary(
            static geometry => geometry.ProjectedObjectId);
        var items = new List<Canvas2DSceneItem>();
        var visualOverrides = new List<Canvas2DCanonicalSceneItemVisualOverride>();
        foreach (var node in graph.Nodes)
        {
            if (!BpmnSemanticTypes.IsFlowNode(node.Source.SemanticTypeId))
            {
                continue;
            }

            if (!HasCanonicalProjectedType(node))
            {
                return Failure(
                    BpmnSceneDiagnosticCodes.InvalidProjectedNode,
                    $"BPMN node '{node.Id}' has inconsistent projected semantic type metadata.",
                    node.Id.Value);
            }

            if (!layouts.TryGetValue(node.Id, out var layout))
            {
                return Failure(
                    BpmnSceneDiagnosticCodes.MissingLayoutGeometry,
                    $"BPMN node '{node.Id}' has no layout geometry.",
                    node.Id.Value);
            }

            var appearance = CreateAppearance(node, layout);
            var localBounds = new RectD(0d, 0d, layout.Bounds.Width, layout.Bounds.Height);
            visualOverrides.Add(new Canvas2DCanonicalSceneItemVisualOverride(
                Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"),
                Geometry(node.Source.SemanticTypeId, localBounds),
                Style(appearance)));
            if (BpmnSemanticTypes.IsGateway(node.Source.SemanticTypeId))
            {
                items.AddRange(CreateGatewayMarkers(appearance, localBounds));
            }
            else if (BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(
                    node.Source.SemanticTypeId) ||
                BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(
                    node.Source.SemanticTypeId))
            {
                items.AddRange(CreateIntermediateEventMarkers(appearance, localBounds));
            }
            else if (BpmnTaskSemanticTypes.IsTask(node.Source.SemanticTypeId) &&
                node.Source.SemanticTypeId != BpmnSemanticTypes.Task)
            {
                items.AddRange(CreateTaskMarkers(appearance, localBounds));
            }
            else if (node.Source.SemanticTypeId == BpmnSemanticTypes.SubProcess)
            {
                items.AddRange(CreateSubProcessMarkers(appearance, localBounds));
            }
        }

        if (includeLegacyFeedback)
        {
            AddBoundaryEventCandidateFeedback(editorState, items, Descriptor.ContributorId);
            AddPlacementFeedback(editorState, items, Descriptor.ContributorId);
        }

        return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            items: items,
            canonicalItemVisualOverrides: visualOverrides));
    }

    public Canvas2DScopeGeometryBaseResult PrepareBase(Canvas2DScopeGeometryBaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = ContributeNodes(context.ProjectedGraph, context.LocalLayout,
            EditorStateSnapshot.Empty, includeLegacyFeedback: false);
        return result.Succeeded
            ? Canvas2DScopeGeometryBaseResult.Success(result.Contribution!, diagnostics: result.Diagnostics)
            : Canvas2DScopeGeometryBaseResult.Failure(result.Diagnostics);
    }

    public Canvas2DScopeGeometryPresentationResult PreparePresentation(Canvas2DScopeGeometryPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Canvas2DScopeGeometryPresentationResult.Success([]);
    }

    private static bool HasCanonicalProjectedType(ProjectedNode node) =>
        node.ProjectedProperties.TryGetValue(
            BpmnProjectionIdentities.ProjectedSemanticTypeProperty,
            out var projectedType) &&
        projectedType.Kind == PropertyValueKind.Text &&
        string.Equals(
            projectedType.TextValue,
            node.Source.SemanticTypeId.Value,
            StringComparison.Ordinal);

    private static Canvas2DSceneGeometry Geometry(
        SemanticTypeId semanticTypeId,
        RectD localBounds)
    {
        if (semanticTypeId == BpmnSemanticTypes.StartEvent ||
            semanticTypeId == BpmnSemanticTypes.EndEvent ||
            BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(semanticTypeId) ||
            BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            return Canvas2DSceneGeometry.Ellipse(localBounds);
        }

        if (BpmnSemanticTypes.IsGateway(semanticTypeId))
        {
            return Canvas2DSceneGeometry.Path(
            [
                new PointD(localBounds.Width / 2d, 0d),
                new PointD(localBounds.Width, localBounds.Height / 2d),
                new PointD(localBounds.Width / 2d, localBounds.Height),
                new PointD(0d, localBounds.Height / 2d),
            ],
            isClosed: true);
        }

        return Canvas2DSceneGeometry.Path(RoundedRectanglePoints(localBounds), isClosed: true);
    }

    private static Canvas2DSceneStyle Style(NodeAppearanceContext node)
    {
        var semanticTypeId = node.SemanticTypeId;
        var dashPattern = node.Interrupting ? null : new[] { 3d, 2d };
        return new Canvas2DSceneStyle(
            fill: "#ffffff",
            stroke: "#000000",
            strokeWidth: semanticTypeId == BpmnSemanticTypes.EndEvent
                ? EndEventStrokeWidth
                : semanticTypeId == BpmnSemanticTypes.StartEvent ||
                    BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(semanticTypeId) ||
                    BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId)
                    ? StartEventStrokeWidth
                    : BpmnSemanticTypes.IsGateway(semanticTypeId)
                        ? GatewayStrokeWidth
                        : TaskStrokeWidth,
            dashPattern: dashPattern);
    }

    private static IEnumerable<Canvas2DSceneItem> CreateGatewayMarkers(
        NodeAppearanceContext node,
        RectD localBounds)
    {
        var semanticTypeId = node.SemanticTypeId;
        var isExclusive = semanticTypeId == BpmnSemanticTypes.ExclusiveGateway;
        var isParallel = semanticTypeId == BpmnSemanticTypes.ParallelGateway;
        var isInclusive = semanticTypeId == BpmnSemanticTypes.InclusiveGateway;
        if (semanticTypeId == BpmnSemanticTypes.EventBasedGateway)
        {
            return CreateEventBasedGatewayMarkers(node, localBounds);
        }

        var stableSourceKey = isExclusive
            ? $"exclusive-gateway-x:{node.StableKey}"
            : isParallel
                ? $"parallel-gateway-plus:{node.StableKey}"
                : isInclusive
                    ? $"inclusive-gateway-o:{node.StableKey}"
                    : throw new InvalidOperationException(
                        "A BPMN Gateway marker requires a supported Gateway type.");
        return
        [
            CreateMarker(
                node,
                    stableSourceKey,
                GatewayMarkerZIndex,
            isInclusive
                ? Canvas2DSceneGeometry.Ellipse(
                    InclusiveGatewayMarkerBounds(localBounds))
                : Canvas2DSceneGeometry.Path(
                    isExclusive
                        ? ExclusiveGatewayMarkerPoints(localBounds)
                        : ParallelGatewayMarkerPoints(localBounds),
                    isClosed: true),
                isInclusive
                ? new Canvas2DSceneStyle(
                    stroke: "#000000",
                    strokeWidth: Math.Min(localBounds.Width, localBounds.Height) *
                        InclusiveGatewayMarkerStrokeWidthRatio)
                : new Canvas2DSceneStyle(fill: "#000000")),
        ];
    }

    private static List<Canvas2DSceneItem> CreateIntermediateEventMarkers(
        NodeAppearanceContext node,
        RectD bounds)
    {
        var semanticTypeId = node.SemanticTypeId;
        var prefix = IntermediateEventMarkerPrefix(semanticTypeId);
        var minimumDimension = Math.Min(bounds.Width, bounds.Height);
        var strokeWidth = minimumDimension * EventMarkerStrokeWidthRatio;
        var inset = minimumDimension * IntermediateEventInnerRingInsetRatio;
        var markerBounds = BpmnIntermediateEventMarkerPlacementPolicy.ResolveMarkerBounds(
            bounds);
        var dashPattern = node.Interrupting ? null : new[] { 3d, 2d };
        var items = new List<Canvas2DSceneItem>();
        if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            items.Add(CreateMarker(
                node,
                    $"{prefix}:body-appearance:{node.StableKey}",
                BoundaryEventBodyAppearanceZIndex,
                Canvas2DSceneGeometry.Ellipse(bounds),
                Style(node)));
        }

        items.Add(CreateMarker(
                node,
                    $"{prefix}:inner-ring:{node.StableKey}",
                GatewayMarkerZIndex,
                Canvas2DSceneGeometry.Ellipse(Inset(bounds, inset)),
                new Canvas2DSceneStyle(
                    stroke: "#000000",
                    strokeWidth: strokeWidth,
                    dashPattern: dashPattern)));

        if (semanticTypeId == BpmnSemanticTypes.MessageCatchEvent ||
            semanticTypeId == BpmnSemanticTypes.MessageThrowEvent ||
            semanticTypeId == BpmnSemanticTypes.MessageBoundaryEvent)
        {
            var isThrow = BpmnIntermediateEventSemanticTypes.IsThrowEvent(semanticTypeId);
            var envelope =
                BpmnIntermediateEventMarkerPlacementPolicy.ResolveEnvelopeBounds(
                    markerBounds);
            items.Add(CreateMarker(
                node,
                    $"{prefix}:envelope:{node.StableKey}",
                GatewayMarkerZIndex + 1,
                Canvas2DSceneGeometry.Path(
                [
                    envelope.TopLeft,
                    new PointD(envelope.Right, envelope.Top),
                    new PointD(envelope.Right, envelope.Bottom),
                    new PointD(envelope.Left, envelope.Bottom),
                ],
                isClosed: true),
                new Canvas2DSceneStyle(
                    fill: isThrow ? "#000000" : "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth)));
            items.Add(CreateMarker(
                node,
                    $"{prefix}:envelope-flap:{node.StableKey}",
                GatewayMarkerZIndex + 2,
                Canvas2DSceneGeometry.Path(
                [
                    envelope.TopLeft,
                    new PointD(
                        envelope.Left + (envelope.Width / 2d),
                        envelope.Top + (envelope.Height / 2d)),
                    new PointD(envelope.Right, envelope.Top),
                ]),
                new Canvas2DSceneStyle(
                    stroke: isThrow ? "#ffffff" : "#000000",
                    strokeWidth: strokeWidth)));
        }
        else if (semanticTypeId == BpmnSemanticTypes.TimerCatchEvent ||
            semanticTypeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            var clockBounds =
                BpmnIntermediateEventMarkerPlacementPolicy.ResolveClockBounds(
                    markerBounds);
            var center = Center(clockBounds);
            var clockRadius = Math.Min(clockBounds.Width, clockBounds.Height) / 2d;
            items.Add(CreateMarker(
                node,
                    $"{prefix}:clock:{node.StableKey}",
                GatewayMarkerZIndex + 1,
                Canvas2DSceneGeometry.Ellipse(clockBounds),
                new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth)));
            items.Add(CreateMarker(
                node,
                    $"{prefix}:clock-hands:{node.StableKey}",
                GatewayMarkerZIndex + 2,
                Canvas2DSceneGeometry.Path(
                [
                    new PointD(center.X, center.Y - (clockRadius * 0.62d)),
                    center,
                    new PointD(center.X + (clockRadius * 0.52d), center.Y + (clockRadius * 0.28d)),
                ]),
                new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth)));
        }
        else
        {
            var isThrow = BpmnIntermediateEventSemanticTypes.IsThrowEvent(semanticTypeId);
            items.Add(CreateMarker(
                node,
                    $"{prefix}:signal:{node.StableKey}",
                GatewayMarkerZIndex + 1,
                Canvas2DSceneGeometry.Path(
                    BpmnIntermediateEventMarkerPlacementPolicy.ResolveSignalTriangle(
                        markerBounds),
                    isClosed: true),
                new Canvas2DSceneStyle(
                    fill: isThrow ? "#000000" : "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth)));
        }

        return items;
    }

    private static string IntermediateEventMarkerPrefix(SemanticTypeId semanticTypeId)
    {
        if (semanticTypeId == BpmnSemanticTypes.MessageCatchEvent)
        {
            return "message-catch-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.MessageThrowEvent)
        {
            return "message-throw-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.MessageBoundaryEvent)
        {
            return "message-boundary-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.TimerCatchEvent)
        {
            return "timer-catch-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            return "timer-boundary-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.SignalCatchEvent)
        {
            return "signal-catch-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.SignalThrowEvent)
        {
            return "signal-throw-event";
        }

        if (semanticTypeId == BpmnSemanticTypes.SignalBoundaryEvent)
        {
            return "signal-boundary-event";
        }

        throw new ArgumentException(
            $"Semantic type '{semanticTypeId}' does not have a supported BPMN event marker.",
            nameof(semanticTypeId));
    }

    private static bool IsInterrupting(ProjectedNode node) =>
        !BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(node.Source.SemanticTypeId) ||
        !node.SemanticProperties.TryGetValue(
            BpmnSemanticProperties.CancelActivity,
            out var cancelActivity) ||
        cancelActivity.Kind != PropertyValueKind.Boolean ||
        cancelActivity.BooleanValue;

    private static void AddBoundaryEventCandidateFeedback(
        EditorStateSnapshot editorState,
        List<Canvas2DSceneItem> items,
        Canvas2DSceneContributorId contributorId)
    {
        foreach (var feedback in editorState.TemporaryFeedback)
        {
            if (feedback.Bounds is null ||
                !TryResolveBoundaryEventFeedbackType(feedback.Kind, out var semanticTypeId))
            {
                continue;
            }

            var bounds = feedback.Bounds!.Value;
            var minimumDimension = Math.Min(bounds.Width, bounds.Height);
            var strokeWidth = minimumDimension * EventMarkerStrokeWidthRatio;
            var inset = minimumDimension * IntermediateEventInnerRingInsetRatio;
            var markerBounds =
                BpmnIntermediateEventMarkerPlacementPolicy.ResolveMarkerBounds(bounds);
            var interrupting = !feedback.Properties.TryGetValue(
                    BpmnSemanticProperties.CancelActivity,
                    out var cancelActivity) ||
                cancelActivity.Kind != PropertyValueKind.Boolean ||
                cancelActivity.BooleanValue;
            var dashPattern = interrupting ? null : new[] { 3d, 2d };

            items.Add(CreateFeedbackItem(
                contributorId,
                feedback,
                "body",
                5000,
                Canvas2DSceneGeometry.Ellipse(bounds),
                new Canvas2DSceneStyle(
                    fill: "#ffffff",
                    stroke: "#000000",
                    strokeWidth: StartEventStrokeWidth,
                    dashPattern: dashPattern),
                bounds));
            items.Add(CreateFeedbackItem(
                contributorId,
                feedback,
                "inner-ring",
                5001,
                Canvas2DSceneGeometry.Ellipse(Inset(bounds, inset)),
                new Canvas2DSceneStyle(
                    stroke: "#000000",
                    strokeWidth: strokeWidth,
                    dashPattern: dashPattern),
                bounds));

            AddBoundaryEventCandidateMarker(
                contributorId,
                feedback,
                semanticTypeId!,
                markerBounds,
                strokeWidth,
                bounds,
                items);
        }
    }

    private static void AddBoundaryEventCandidateMarker(
        Canvas2DSceneContributorId contributorId,
        EditorFeedbackSnapshot feedback,
        SemanticTypeId semanticTypeId,
        RectD markerBounds,
        double strokeWidth,
        RectD bounds,
        List<Canvas2DSceneItem> items)
    {
        if (semanticTypeId == BpmnSemanticTypes.MessageBoundaryEvent)
        {
            var envelope =
                BpmnIntermediateEventMarkerPlacementPolicy.ResolveEnvelopeBounds(
                    markerBounds);
            items.Add(CreateFeedbackItem(
                contributorId,
                feedback,
                "envelope",
                5002,
                Canvas2DSceneGeometry.Path(
                [
                    envelope.TopLeft,
                    new PointD(envelope.Right, envelope.Top),
                    new PointD(envelope.Right, envelope.Bottom),
                    new PointD(envelope.Left, envelope.Bottom),
                ],
                isClosed: true),
                new Canvas2DSceneStyle(
                    fill: "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth),
                bounds));
            items.Add(CreateFeedbackItem(
                contributorId,
                feedback,
                "envelope-flap",
                5003,
                Canvas2DSceneGeometry.Path(
                [
                    envelope.TopLeft,
                    new PointD(
                        envelope.Left + (envelope.Width / 2d),
                        envelope.Top + (envelope.Height / 2d)),
                    new PointD(envelope.Right, envelope.Top),
                ]),
                new Canvas2DSceneStyle(
                    stroke: "#000000",
                    strokeWidth: strokeWidth),
                bounds));
            return;
        }

        if (semanticTypeId == BpmnSemanticTypes.SignalBoundaryEvent)
        {
            items.Add(CreateFeedbackItem(
                contributorId,
                feedback,
                "signal",
                5002,
                Canvas2DSceneGeometry.Path(
                    BpmnIntermediateEventMarkerPlacementPolicy.ResolveSignalTriangle(
                        markerBounds),
                    isClosed: true),
                new Canvas2DSceneStyle(
                    fill: "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth),
                bounds));
            return;
        }

        var clockBounds =
            BpmnIntermediateEventMarkerPlacementPolicy.ResolveClockBounds(markerBounds);
        var center = Center(clockBounds);
        var clockRadius = Math.Min(clockBounds.Width, clockBounds.Height) / 2d;
        items.Add(CreateFeedbackItem(
            contributorId,
            feedback,
            "clock",
            5002,
            Canvas2DSceneGeometry.Ellipse(clockBounds),
            new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth),
            bounds));
        items.Add(CreateFeedbackItem(
            contributorId,
            feedback,
            "clock-hands",
            5003,
            Canvas2DSceneGeometry.Path(
            [
                new PointD(center.X, center.Y - (clockRadius * 0.62d)),
                center,
                new PointD(
                    center.X + (clockRadius * 0.52d),
                    center.Y + (clockRadius * 0.28d)),
            ]),
            new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth),
            bounds));
    }

    private static bool TryResolveBoundaryEventFeedbackType(
        string feedbackKind,
        out SemanticTypeId? semanticTypeId)
    {
        if (string.Equals(
                feedbackKind,
                BpmnMessageBoundaryEventSceneFeedback.AttachmentCandidateKind,
                StringComparison.Ordinal))
        {
            semanticTypeId = BpmnSemanticTypes.MessageBoundaryEvent;
            return true;
        }

        if (string.Equals(
                feedbackKind,
                BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
                StringComparison.Ordinal))
        {
            semanticTypeId = BpmnSemanticTypes.TimerBoundaryEvent;
            return true;
        }

        if (string.Equals(
                feedbackKind,
                BpmnSignalBoundaryEventSceneFeedback.AttachmentCandidateKind,
                StringComparison.Ordinal))
        {
            semanticTypeId = BpmnSemanticTypes.SignalBoundaryEvent;
            return true;
        }

        semanticTypeId = null;
        return false;
    }

    private static Canvas2DSceneItem CreateFeedbackItem(
        Canvas2DSceneContributorId contributorId,
        EditorFeedbackSnapshot feedback,
        string localKey,
        int zIndex,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneStyle style,
        RectD bounds) =>
        new(
            Canvas2DSceneObjectIdentity.ForExtension(
                contributorId,
                $"feedback:{feedback.Id}:{localKey}"),
            Canvas2DSceneLayer.Overlay,
            zIndex,
            geometry,
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.EditorState |
                Canvas2DSceneOriginCategory.RegisteredExtension,
                stableSourceKey: $"feedback:{feedback.Id}:{localKey}"),
            style: style,
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            bounds: bounds);

    private static List<Canvas2DSceneItem> CreateTaskMarkers(
        NodeAppearanceContext node,
        RectD taskBounds)
    {
        var markerBounds = BpmnTaskMarkerPlacementPolicy.ResolveBounds(taskBounds);
        var strokeWidth = BpmnTaskMarkerPlacementPolicy.ResolveStrokeWidth(markerBounds);
        var prefix = BpmnTaskSemanticTypes.DisplayName(node.SemanticTypeId)
            .Replace(' ', '-')
            .ToLowerInvariant();
        var items = new List<Canvas2DSceneItem>();

        if (node.SemanticTypeId == BpmnSemanticTypes.UserTask)
        {
            var headRadius = markerBounds.Width * 0.15d;
            var headCenter = new PointD(
                markerBounds.Left + (markerBounds.Width / 2d),
                markerBounds.Top + (markerBounds.Height * 0.27d));
            items.Add(CreateMarker(
                node,
                    $"{prefix}:head:{node.StableKey}",
                TaskMarkerZIndex,
                Canvas2DSceneGeometry.Ellipse(CenteredSquare(headCenter, headRadius)),
                new Canvas2DSceneStyle(fill: "#000000")));
            items.Add(CreateMarker(
                node,
                    $"{prefix}:body:{node.StableKey}",
                TaskMarkerZIndex + 1,
                Canvas2DSceneGeometry.Path(
                [
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.18d),
                        markerBounds.Bottom - (markerBounds.Height * 0.12d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.26d),
                        markerBounds.Top + (markerBounds.Height * 0.55d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.42d),
                        markerBounds.Top + (markerBounds.Height * 0.46d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.58d),
                        markerBounds.Top + (markerBounds.Height * 0.46d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.74d),
                        markerBounds.Top + (markerBounds.Height * 0.55d)),
                    new PointD(markerBounds.Right - (markerBounds.Width * 0.18d),
                        markerBounds.Bottom - (markerBounds.Height * 0.12d)),
                ],
                isClosed: true),
                new Canvas2DSceneStyle(fill: "#000000")));
            return items;
        }

        if (node.SemanticTypeId == BpmnSemanticTypes.ManualTask)
        {
            items.Add(CreateMarker(
                node,
                    $"{prefix}:hand:{node.StableKey}",
                TaskMarkerZIndex,
                Canvas2DSceneGeometry.Path(
                [
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.18d),
                        markerBounds.Top + (markerBounds.Height * 0.48d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.18d),
                        markerBounds.Top + (markerBounds.Height * 0.27d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.28d),
                        markerBounds.Top + (markerBounds.Height * 0.27d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.31d),
                        markerBounds.Top + (markerBounds.Height * 0.12d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.41d),
                        markerBounds.Top + (markerBounds.Height * 0.12d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.45d),
                        markerBounds.Top + (markerBounds.Height * 0.08d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.55d),
                        markerBounds.Top + (markerBounds.Height * 0.1d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.59d),
                        markerBounds.Top + (markerBounds.Height * 0.14d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.69d),
                        markerBounds.Top + (markerBounds.Height * 0.18d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.72d),
                        markerBounds.Top + (markerBounds.Height * 0.38d)),
                    new PointD(markerBounds.Right - (markerBounds.Width * 0.1d),
                        markerBounds.Top + (markerBounds.Height * 0.48d)),
                    new PointD(markerBounds.Right - (markerBounds.Width * 0.14d),
                        markerBounds.Bottom - (markerBounds.Height * 0.18d)),
                    new PointD(markerBounds.Left + (markerBounds.Width * 0.47d),
                        markerBounds.Bottom - (markerBounds.Height * 0.05d)),
                ],
                isClosed: true),
                new Canvas2DSceneStyle(
                    fill: "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth)));
            return items;
        }

        if (node.SemanticTypeId == BpmnSemanticTypes.ServiceTask)
        {
            var center = Center(markerBounds);
            var outerRadius = markerBounds.Width * 0.43d;
            items.Add(CreateMarker(
                node,
                    $"{prefix}:gear:{node.StableKey}",
                TaskMarkerZIndex,
                Canvas2DSceneGeometry.Path(
                    GearPoints(center, outerRadius, outerRadius * 0.72d, 8),
                    isClosed: true),
                new Canvas2DSceneStyle(fill: "#000000")));
            items.Add(CreateMarker(
                node,
                    $"{prefix}:gear-hole:{node.StableKey}",
                TaskMarkerZIndex + 1,
                Canvas2DSceneGeometry.Ellipse(
                    CenteredSquare(center, markerBounds.Width * 0.16d)),
                new Canvas2DSceneStyle(fill: "#ffffff")));
            return items;
        }

        if (node.SemanticTypeId == BpmnSemanticTypes.SendTask ||
            node.SemanticTypeId == BpmnSemanticTypes.ReceiveTask)
        {
            var envelope = new RectD(
                markerBounds.Left + (markerBounds.Width * 0.08d),
                markerBounds.Top + (markerBounds.Height * 0.2d),
                markerBounds.Width * 0.84d,
                markerBounds.Height * 0.6d);
            var isSend = node.SemanticTypeId == BpmnSemanticTypes.SendTask;
            items.Add(CreateMarker(
                node,
                    $"{prefix}:envelope:{node.StableKey}",
                TaskMarkerZIndex,
                Canvas2DSceneGeometry.Path(
                [
                    envelope.TopLeft,
                    new PointD(envelope.Right, envelope.Top),
                    new PointD(envelope.Right, envelope.Bottom),
                    new PointD(envelope.Left, envelope.Bottom),
                ],
                isClosed: true),
                new Canvas2DSceneStyle(
                    fill: isSend ? "#000000" : "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth)));
            items.Add(CreateMarker(
                node,
                    $"{prefix}:envelope-flap:{node.StableKey}",
                TaskMarkerZIndex + 1,
                Canvas2DSceneGeometry.Path(
                [
                    envelope.TopLeft,
                    new PointD(envelope.Left + (envelope.Width / 2d),
                        envelope.Top + (envelope.Height * 0.55d)),
                    new PointD(envelope.Right, envelope.Top),
                ]),
                new Canvas2DSceneStyle(
                    stroke: isSend ? "#ffffff" : "#000000",
                    strokeWidth: strokeWidth)));
            return items;
        }

        throw new InvalidOperationException(
            $"BPMN Task type '{node.SemanticTypeId}' has no specialized marker.");
    }

    private static IEnumerable<Canvas2DSceneItem> CreateSubProcessMarkers(
        NodeAppearanceContext node,
        RectD activityBounds)
    {
        var markerBounds = BpmnSubProcessMarkerPlacementPolicy.ResolveBounds(activityBounds);
        var strokeWidth =
            BpmnSubProcessMarkerPlacementPolicy.ResolveStrokeWidth(markerBounds);
        return
        [
            CreateMarker(
                node,
                    $"subprocess:marker-box:{node.StableKey}",
                SubProcessMarkerZIndex,
                Canvas2DSceneGeometry.Path(
                [
                    markerBounds.TopLeft,
                    new PointD(markerBounds.Right, markerBounds.Top),
                    new PointD(markerBounds.Right, markerBounds.Bottom),
                    new PointD(markerBounds.Left, markerBounds.Bottom),
                ],
                isClosed: true),
                new Canvas2DSceneStyle(
                    fill: "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth)),
            CreateMarker(
                node,
                    $"subprocess:marker-plus:{node.StableKey}",
                SubProcessMarkerZIndex + 1,
                Canvas2DSceneGeometry.Path(
                    BpmnSubProcessMarkerPlacementPolicy.ResolvePlusPoints(markerBounds),
                    isClosed: true),
                new Canvas2DSceneStyle(fill: "#000000")),
        ];
    }

    private static IEnumerable<Canvas2DSceneItem> CreateEventBasedGatewayMarkers(
        NodeAppearanceContext node,
        RectD bounds)
    {
        var minimumDimension = Math.Min(bounds.Width, bounds.Height);
        var center = Center(bounds);
        var strokeWidth = minimumDimension * EventMarkerStrokeWidthRatio;
        var outerRadius = minimumDimension * EventBasedGatewayOuterRingRadiusRatio;
        var innerRadius = minimumDimension * EventBasedGatewayInnerRingRadiusRatio;
        var pentagonRadius = minimumDimension * 0.115d;
        return
        [
            CreateMarker(
                node,
                    $"event-based-gateway:outer-ring:{node.StableKey}",
                GatewayMarkerZIndex,
                CenteredSquare(center, outerRadius),
                strokeWidth),
            CreateMarker(
                node,
                    $"event-based-gateway:inner-ring:{node.StableKey}",
                GatewayMarkerZIndex + 1,
                CenteredSquare(center, innerRadius),
                strokeWidth),
            CreateMarker(
                node,
                    $"event-based-gateway:pentagon:{node.StableKey}",
                GatewayMarkerZIndex + 2,
                Canvas2DSceneGeometry.Path(
                    RegularPolygon(center, pentagonRadius, 5, -Math.PI / 2d),
                    isClosed: true),
                new Canvas2DSceneStyle(
                    fill: "#ffffff",
                    stroke: "#000000",
                    strokeWidth: strokeWidth)),
        ];
    }

    private static Canvas2DSceneItem CreateMarker(
        NodeAppearanceContext node,
        string stableSourceKey,
        int zIndex,
        RectD ellipseBounds,
        double strokeWidth) =>
        CreateMarker(
            node,
            stableSourceKey,
            zIndex,
            Canvas2DSceneGeometry.Ellipse(ellipseBounds),
            new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth));

    private static Canvas2DSceneItem CreateMarker(
        NodeAppearanceContext node,
        string stableSourceKey,
        int zIndex,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneStyle style) => node.CreateItem(stableSourceKey, zIndex, geometry, style);

    private static Canvas2DSceneItem CreateNormalMarker(
        ProjectedNode node,
        LayoutNodeGeometry layout,
        string stableSourceKey,
        int zIndex,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneStyle style)
    {
        var categories = Canvas2DSceneOriginCategory.SemanticElement |
            Canvas2DSceneOriginCategory.ProjectedRuntimeObject |
            Canvas2DSceneOriginCategory.RegisteredExtension;
        if (node.Source.VisualStateId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.VisualState;
        }

        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForExtension(
                Descriptor.ContributorId,
                stableSourceKey),
            Canvas2DSceneLayer.Decoration,
            zIndex,
            geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                node.Source.SemanticElementId,
                node.Source.VisualStateId,
                node.Id,
                stableSourceKey),
            transform: layout.Transform,
            style: style,
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            bounds: layout.Bounds);
    }


    private sealed record NodeAppearanceContext(
        SemanticTypeId SemanticTypeId,
        string StableKey,
        bool Interrupting,
        Func<string, int, Canvas2DSceneGeometry, Canvas2DSceneStyle, Canvas2DSceneItem> CreateItem);

    private static NodeAppearanceContext CreateAppearance(ProjectedNode node, LayoutNodeGeometry layout) =>
        new(node.Source.SemanticTypeId, node.Id.Value, IsInterrupting(node),
            (key, z, geometry, style) => CreateNormalMarker(node, layout, key, z, geometry, style));

    private sealed class NodesContributor : ICanvas2DSceneContributor, ICanvas2DScopeGeometryContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            ContributeNodes(context, includeLegacyFeedback: false);

        public Canvas2DScopeGeometryBaseResult PrepareBase(Canvas2DScopeGeometryBaseContext context) =>
            new BpmnCanvas2DSceneContributor().PrepareBase(context);

        public Canvas2DScopeGeometryPresentationResult PreparePresentation(Canvas2DScopeGeometryPresentationContext context) =>
            new BpmnCanvas2DSceneContributor().PreparePresentation(context);
    }

    private sealed class PlacementContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var items = new List<Canvas2DSceneItem>();
            AddPlacementFeedback(context.EditorState, items, PlacementDescriptor.ContributorId);
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(items: items));
        }
    }

    private static void AddPlacementFeedback(EditorStateSnapshot editorState,
        List<Canvas2DSceneItem> items, Canvas2DSceneContributorId contributorId)
    {
        foreach (var feedback in editorState.TemporaryFeedback)
        {
            if (feedback.PlacementPreview is not { } preview || feedback.Bounds is not { } bounds ||
                !BpmnSemanticTypes.IsFlowNode(preview.SemanticTypeId))
            {
                continue;
            }

            var local = new RectD(0d, 0d, bounds.Width, bounds.Height);
            var tint = preview.IsAllowed ? "#15803d" : "#b91c1c";
            var appearance = new NodeAppearanceContext(preview.SemanticTypeId, feedback.Id, true,
                (key, z, geometry, style) => PreviewItem(feedback, contributorId, key, z + 5100, geometry,
                    style, bounds, tint));
            items.Add(PreviewItem(feedback, contributorId, "body", 5000, Geometry(preview.SemanticTypeId, local),
                Style(appearance), bounds, tint));
            if (BpmnSemanticTypes.IsGateway(preview.SemanticTypeId))
            {
                items.AddRange(CreateGatewayMarkers(appearance, local));
            }
            else if (BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(preview.SemanticTypeId) ||
                BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(preview.SemanticTypeId))
            {
                items.AddRange(CreateIntermediateEventMarkers(appearance, local));
            }
            else if (BpmnTaskSemanticTypes.IsTask(preview.SemanticTypeId) &&
                preview.SemanticTypeId != BpmnSemanticTypes.Task)
            {
                items.AddRange(CreateTaskMarkers(appearance, local));
            }
            else if (preview.SemanticTypeId == BpmnSemanticTypes.SubProcess)
            {
                items.AddRange(CreateSubProcessMarkers(appearance, local));
            }
        }
    }

    private static Canvas2DSceneItem PreviewItem(EditorFeedbackSnapshot feedback,
        Canvas2DSceneContributorId contributorId, string localKey,
        int zIndex, Canvas2DSceneGeometry geometry, Canvas2DSceneStyle style, RectD bounds, string tint)
    {
        var key = $"feedback:{feedback.Id}:{localKey}";
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForExtension(contributorId, key),
            Canvas2DSceneLayer.Overlay, zIndex, geometry,
            new Canvas2DSceneOriginTrace(Canvas2DSceneOriginCategory.EditorState |
                Canvas2DSceneOriginCategory.RegisteredExtension, stableSourceKey: key),
            transform: Matrix2D.CreateTranslation(bounds.X, bounds.Y),
            style: new Canvas2DSceneStyle(
                fill: style.Fill == "#000000" ? tint : style.Fill,
                stroke: style.Stroke == "#000000" ? tint : style.Stroke,
                strokeWidth: style.StrokeWidth, dashPattern: style.DashPattern, opacity: 0.9d),
            hitTestPolicy: Canvas2DHitTestPolicy.None, bounds: bounds);
    }

    private static RectD Inset(RectD bounds, double inset) =>
        new(
            bounds.X + inset,
            bounds.Y + inset,
            bounds.Width - (2d * inset),
            bounds.Height - (2d * inset));

    private static RectD CenteredSquare(PointD center, double radius) =>
        new(center.X - radius, center.Y - radius, radius * 2d, radius * 2d);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static PointD[] RegularPolygon(
        PointD center,
        double radius,
        int sideCount,
        double startAngle) =>
        Enumerable.Range(0, sideCount)
            .Select(index =>
            {
                var angle = startAngle + (index * 2d * Math.PI / sideCount);
                return new PointD(
                    center.X + (radius * Math.Cos(angle)),
                    center.Y + (radius * Math.Sin(angle)));
            })
            .ToArray();

    private static PointD[] GearPoints(
        PointD center,
        double outerRadius,
        double innerRadius,
        int toothCount) =>
        Enumerable.Range(0, toothCount * 2)
            .Select(index =>
            {
                var angle = (-Math.PI / 2d) + (index * Math.PI / toothCount);
                var radius = index % 2 == 0 ? outerRadius : innerRadius;
                return new PointD(
                    center.X + (radius * Math.Cos(angle)),
                    center.Y + (radius * Math.Sin(angle)));
            })
            .ToArray();

    private static RectD InclusiveGatewayMarkerBounds(RectD bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) *
            InclusiveGatewayMarkerRadiusRatio;
        var centerX = bounds.X + (bounds.Width / 2d);
        var centerY = bounds.Y + (bounds.Height / 2d);
        return new RectD(
            centerX - radius,
            centerY - radius,
            radius * 2d,
            radius * 2d);
    }

    private static PointD[] ExclusiveGatewayMarkerPoints(RectD bounds)
    {
        var minimumDimension = Math.Min(bounds.Width, bounds.Height);
        var halfExtent = minimumDimension * ExclusiveGatewayMarkerHalfExtentRatio;
        var thickness = minimumDimension * ExclusiveGatewayMarkerThicknessRatio;
        var centerX = bounds.Width / 2d;
        var centerY = bounds.Height / 2d;
        var left = centerX - halfExtent;
        var right = centerX + halfExtent;
        var top = centerY - halfExtent;
        var bottom = centerY + halfExtent;
        var halfThickness = thickness / 2d;

        return
        [
            new PointD(left, top),
            new PointD(left + thickness, top),
            new PointD(centerX, centerY - halfThickness),
            new PointD(right - thickness, top),
            new PointD(right, top),
            new PointD(centerX + halfThickness, centerY),
            new PointD(right, bottom),
            new PointD(right - thickness, bottom),
            new PointD(centerX, centerY + halfThickness),
            new PointD(left + thickness, bottom),
            new PointD(left, bottom),
            new PointD(centerX - halfThickness, centerY),
        ];
    }

    private static PointD[] ParallelGatewayMarkerPoints(RectD bounds)
    {
        var minimumDimension = Math.Min(bounds.Width, bounds.Height);
        var halfExtent = minimumDimension * ParallelGatewayMarkerHalfExtentRatio;
        var halfThickness =
            minimumDimension * ParallelGatewayMarkerThicknessRatio / 2d;
        var centerX = bounds.Width / 2d;
        var centerY = bounds.Height / 2d;
        var left = centerX - halfExtent;
        var right = centerX + halfExtent;
        var top = centerY - halfExtent;
        var bottom = centerY + halfExtent;

        return
        [
            new PointD(centerX - halfThickness, top),
            new PointD(centerX + halfThickness, top),
            new PointD(centerX + halfThickness, centerY - halfThickness),
            new PointD(right, centerY - halfThickness),
            new PointD(right, centerY + halfThickness),
            new PointD(centerX + halfThickness, centerY + halfThickness),
            new PointD(centerX + halfThickness, bottom),
            new PointD(centerX - halfThickness, bottom),
            new PointD(centerX - halfThickness, centerY + halfThickness),
            new PointD(left, centerY + halfThickness),
            new PointD(left, centerY - halfThickness),
            new PointD(centerX - halfThickness, centerY - halfThickness),
        ];
    }

    private static List<PointD> RoundedRectanglePoints(RectD bounds)
    {
        var radius = Math.Min(
            MaximumTaskCornerRadius,
            Math.Min(bounds.Width / 2d, bounds.Height / 2d));
        var points = new List<PointD>(4 * (RoundedCornerIntermediatePointCount + 2))
        {
            new(radius, 0d),
            new(bounds.Width - radius, 0d),
        };

        AddIntermediateArc(
            points,
            new PointD(bounds.Width - radius, radius),
            radius,
            -Math.PI / 2d,
            0d);
        points.Add(new PointD(bounds.Width, radius));
        points.Add(new PointD(bounds.Width, bounds.Height - radius));
        AddIntermediateArc(
            points,
            new PointD(bounds.Width - radius, bounds.Height - radius),
            radius,
            0d,
            Math.PI / 2d);
        points.Add(new PointD(bounds.Width - radius, bounds.Height));
        points.Add(new PointD(radius, bounds.Height));
        AddIntermediateArc(
            points,
            new PointD(radius, bounds.Height - radius),
            radius,
            Math.PI / 2d,
            Math.PI);
        points.Add(new PointD(0d, bounds.Height - radius));
        points.Add(new PointD(0d, radius));
        AddIntermediateArc(
            points,
            new PointD(radius, radius),
            radius,
            Math.PI,
            3d * Math.PI / 2d);

        return points;
    }

    private static void AddIntermediateArc(
        List<PointD> points,
        PointD center,
        double radius,
        double startAngle,
        double endAngle)
    {
        var step = (endAngle - startAngle) / (RoundedCornerIntermediatePointCount + 1d);
        for (var index = 1; index <= RoundedCornerIntermediatePointCount; index++)
        {
            var angle = startAngle + (index * step);
            points.Add(new PointD(
                center.X + (radius * Math.Cos(angle)),
                center.Y + (radius * Math.Sin(angle))));
        }
    }

    private static Canvas2DSceneContributionResult Failure(
        string code,
        string message,
        string sourceIdentity) =>
        Canvas2DSceneContributionResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, sourceIdentity),
        ]);
}

public static class BpmnSceneDiagnosticCodes
{
    public const string InvalidProjectedNode = "BPMN_SCENE_INVALID_PROJECTED_NODE";

    public const string MissingLayoutGeometry = "BPMN_SCENE_MISSING_LAYOUT_GEOMETRY";
}

/// <summary>
/// Stable BPMN-local Editor feedback identity used by attachment placement previews.
/// Generic Toolbox and Canvas layers remain notation-neutral.
/// </summary>
public static class BpmnTimerBoundaryEventSceneFeedback
{
    public const string AttachmentCandidateKind =
        "bpmn:timer-boundary-event/attachment-candidate";
}

/// <summary>
/// Stable BPMN-local Editor feedback identity for Message Boundary Event previews.
/// </summary>
public static class BpmnMessageBoundaryEventSceneFeedback
{
    public const string AttachmentCandidateKind =
        "bpmn:message-boundary-event/attachment-candidate";
}

/// <summary>
/// Stable BPMN-local Editor feedback identity for Signal Boundary Event previews.
/// </summary>
public static class BpmnSignalBoundaryEventSceneFeedback
{
    public const string AttachmentCandidateKind =
        "bpmn:signal-boundary-event/attachment-candidate";
}
