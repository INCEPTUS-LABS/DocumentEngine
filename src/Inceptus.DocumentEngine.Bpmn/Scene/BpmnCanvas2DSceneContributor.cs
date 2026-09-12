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
public sealed class BpmnCanvas2DSceneContributor : ICanvas2DSceneContributor
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
        "1");

    internal static Canvas2DSceneContributorRegistration Registration { get; } =
        new(Descriptor, new BpmnCanvas2DSceneContributor());

    public Canvas2DSceneContributionResult Contribute(
        Canvas2DSceneContributionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var layouts = context.LayoutResult.Nodes.ToDictionary(
            static geometry => geometry.ProjectedObjectId);
        var items = new List<Canvas2DSceneItem>();
        var visualOverrides = new List<Canvas2DCanonicalSceneItemVisualOverride>();
        foreach (var node in context.ProjectedGraph.Nodes)
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

            var localBounds = new RectD(0d, 0d, layout.Bounds.Width, layout.Bounds.Height);
            visualOverrides.Add(new Canvas2DCanonicalSceneItemVisualOverride(
                Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"),
                Geometry(node.Source.SemanticTypeId, localBounds),
                Style(node)));
            if (BpmnSemanticTypes.IsGateway(node.Source.SemanticTypeId))
            {
                items.AddRange(CreateGatewayMarkers(node, layout, localBounds));
            }
            else if (BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(
                    node.Source.SemanticTypeId) ||
                BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(
                    node.Source.SemanticTypeId))
            {
                items.AddRange(CreateIntermediateEventMarkers(node, layout, localBounds));
            }
            else if (BpmnTaskSemanticTypes.IsTask(node.Source.SemanticTypeId) &&
                node.Source.SemanticTypeId != BpmnSemanticTypes.Task)
            {
                items.AddRange(CreateTaskMarkers(node, layout, localBounds));
            }
            else if (node.Source.SemanticTypeId == BpmnSemanticTypes.SubProcess)
            {
                items.AddRange(CreateSubProcessMarkers(node, layout, localBounds));
            }
        }

        AddBoundaryEventCandidateFeedback(context.EditorState, items);

        return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            items: items,
            canonicalItemVisualOverrides: visualOverrides));
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

    private static Canvas2DSceneStyle Style(ProjectedNode node)
    {
        var semanticTypeId = node.Source.SemanticTypeId;
        var dashPattern = IsInterrupting(node) ? null : new[] { 3d, 2d };
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
        ProjectedNode node,
        LayoutNodeGeometry layout,
        RectD localBounds)
    {
        var semanticTypeId = node.Source.SemanticTypeId;
        var isExclusive = semanticTypeId == BpmnSemanticTypes.ExclusiveGateway;
        var isParallel = semanticTypeId == BpmnSemanticTypes.ParallelGateway;
        var isInclusive = semanticTypeId == BpmnSemanticTypes.InclusiveGateway;
        if (semanticTypeId == BpmnSemanticTypes.EventBasedGateway)
        {
            return CreateEventBasedGatewayMarkers(node, layout, localBounds);
        }

        var stableSourceKey = isExclusive
            ? $"exclusive-gateway-x:{node.Id.Value}"
            : isParallel
                ? $"parallel-gateway-plus:{node.Id.Value}"
                : isInclusive
                    ? $"inclusive-gateway-o:{node.Id.Value}"
                    : throw new InvalidOperationException(
                        "A BPMN Gateway marker requires a supported Gateway type.");
        return
        [
            CreateMarker(
                node,
                layout,
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
        ProjectedNode node,
        LayoutNodeGeometry layout,
        RectD bounds)
    {
        var semanticTypeId = node.Source.SemanticTypeId;
        var prefix = IntermediateEventMarkerPrefix(semanticTypeId);
        var minimumDimension = Math.Min(bounds.Width, bounds.Height);
        var strokeWidth = minimumDimension * EventMarkerStrokeWidthRatio;
        var inset = minimumDimension * IntermediateEventInnerRingInsetRatio;
        var markerBounds = BpmnIntermediateEventMarkerPlacementPolicy.ResolveMarkerBounds(
            bounds);
        var dashPattern = IsInterrupting(node) ? null : new[] { 3d, 2d };
        var items = new List<Canvas2DSceneItem>();
        if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:body-appearance:{node.Id.Value}",
                BoundaryEventBodyAppearanceZIndex,
                Canvas2DSceneGeometry.Ellipse(bounds),
                Style(node)));
        }

        items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:inner-ring:{node.Id.Value}",
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
                layout,
                $"{prefix}:envelope:{node.Id.Value}",
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
                layout,
                $"{prefix}:envelope-flap:{node.Id.Value}",
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
                layout,
                $"{prefix}:clock:{node.Id.Value}",
                GatewayMarkerZIndex + 1,
                Canvas2DSceneGeometry.Ellipse(clockBounds),
                new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth)));
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:clock-hands:{node.Id.Value}",
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
                layout,
                $"{prefix}:signal:{node.Id.Value}",
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
        List<Canvas2DSceneItem> items)
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
                feedback,
                semanticTypeId!,
                markerBounds,
                strokeWidth,
                bounds,
                items);
        }
    }

    private static void AddBoundaryEventCandidateMarker(
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
            feedback,
            "clock",
            5002,
            Canvas2DSceneGeometry.Ellipse(clockBounds),
            new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth),
            bounds));
        items.Add(CreateFeedbackItem(
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
        EditorFeedbackSnapshot feedback,
        string localKey,
        int zIndex,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneStyle style,
        RectD bounds) =>
        new(
            Canvas2DSceneObjectIdentity.ForExtension(
                Descriptor.ContributorId,
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
        ProjectedNode node,
        LayoutNodeGeometry layout,
        RectD taskBounds)
    {
        var markerBounds = BpmnTaskMarkerPlacementPolicy.ResolveBounds(taskBounds);
        var strokeWidth = BpmnTaskMarkerPlacementPolicy.ResolveStrokeWidth(markerBounds);
        var prefix = BpmnTaskSemanticTypes.DisplayName(node.Source.SemanticTypeId)
            .Replace(' ', '-')
            .ToLowerInvariant();
        var items = new List<Canvas2DSceneItem>();

        if (node.Source.SemanticTypeId == BpmnSemanticTypes.UserTask)
        {
            var headRadius = markerBounds.Width * 0.15d;
            var headCenter = new PointD(
                markerBounds.Left + (markerBounds.Width / 2d),
                markerBounds.Top + (markerBounds.Height * 0.27d));
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:head:{node.Id.Value}",
                TaskMarkerZIndex,
                Canvas2DSceneGeometry.Ellipse(CenteredSquare(headCenter, headRadius)),
                new Canvas2DSceneStyle(fill: "#000000")));
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:body:{node.Id.Value}",
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

        if (node.Source.SemanticTypeId == BpmnSemanticTypes.ManualTask)
        {
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:hand:{node.Id.Value}",
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

        if (node.Source.SemanticTypeId == BpmnSemanticTypes.ServiceTask)
        {
            var center = Center(markerBounds);
            var outerRadius = markerBounds.Width * 0.43d;
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:gear:{node.Id.Value}",
                TaskMarkerZIndex,
                Canvas2DSceneGeometry.Path(
                    GearPoints(center, outerRadius, outerRadius * 0.72d, 8),
                    isClosed: true),
                new Canvas2DSceneStyle(fill: "#000000")));
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:gear-hole:{node.Id.Value}",
                TaskMarkerZIndex + 1,
                Canvas2DSceneGeometry.Ellipse(
                    CenteredSquare(center, markerBounds.Width * 0.16d)),
                new Canvas2DSceneStyle(fill: "#ffffff")));
            return items;
        }

        if (node.Source.SemanticTypeId == BpmnSemanticTypes.SendTask ||
            node.Source.SemanticTypeId == BpmnSemanticTypes.ReceiveTask)
        {
            var envelope = new RectD(
                markerBounds.Left + (markerBounds.Width * 0.08d),
                markerBounds.Top + (markerBounds.Height * 0.2d),
                markerBounds.Width * 0.84d,
                markerBounds.Height * 0.6d);
            var isSend = node.Source.SemanticTypeId == BpmnSemanticTypes.SendTask;
            items.Add(CreateMarker(
                node,
                layout,
                $"{prefix}:envelope:{node.Id.Value}",
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
                layout,
                $"{prefix}:envelope-flap:{node.Id.Value}",
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
            $"BPMN Task type '{node.Source.SemanticTypeId}' has no specialized marker.");
    }

    private static IEnumerable<Canvas2DSceneItem> CreateSubProcessMarkers(
        ProjectedNode node,
        LayoutNodeGeometry layout,
        RectD activityBounds)
    {
        var markerBounds = BpmnSubProcessMarkerPlacementPolicy.ResolveBounds(activityBounds);
        var strokeWidth =
            BpmnSubProcessMarkerPlacementPolicy.ResolveStrokeWidth(markerBounds);
        return
        [
            CreateMarker(
                node,
                layout,
                $"subprocess:marker-box:{node.Id.Value}",
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
                layout,
                $"subprocess:marker-plus:{node.Id.Value}",
                SubProcessMarkerZIndex + 1,
                Canvas2DSceneGeometry.Path(
                    BpmnSubProcessMarkerPlacementPolicy.ResolvePlusPoints(markerBounds),
                    isClosed: true),
                new Canvas2DSceneStyle(fill: "#000000")),
        ];
    }

    private static IEnumerable<Canvas2DSceneItem> CreateEventBasedGatewayMarkers(
        ProjectedNode node,
        LayoutNodeGeometry layout,
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
                layout,
                $"event-based-gateway:outer-ring:{node.Id.Value}",
                GatewayMarkerZIndex,
                CenteredSquare(center, outerRadius),
                strokeWidth),
            CreateMarker(
                node,
                layout,
                $"event-based-gateway:inner-ring:{node.Id.Value}",
                GatewayMarkerZIndex + 1,
                CenteredSquare(center, innerRadius),
                strokeWidth),
            CreateMarker(
                node,
                layout,
                $"event-based-gateway:pentagon:{node.Id.Value}",
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
        ProjectedNode node,
        LayoutNodeGeometry layout,
        string stableSourceKey,
        int zIndex,
        RectD ellipseBounds,
        double strokeWidth) =>
        CreateMarker(
            node,
            layout,
            stableSourceKey,
            zIndex,
            Canvas2DSceneGeometry.Ellipse(ellipseBounds),
            new Canvas2DSceneStyle(stroke: "#000000", strokeWidth: strokeWidth));

    private static Canvas2DSceneItem CreateMarker(
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
