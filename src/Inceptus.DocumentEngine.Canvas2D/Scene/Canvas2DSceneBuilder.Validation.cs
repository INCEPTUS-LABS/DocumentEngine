using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private const int TransformedBoundsContainmentToleranceUlps = 4;

    private static bool IsValidCanonicalItemVisualOverride(
        Canvas2DCanonicalSceneItemVisualOverride? visualOverride,
        Canvas2DSceneContributorDescriptor descriptor,
        IReadOnlyDictionary<SceneObjectId, Canvas2DSceneItem> canonicalItems,
        List<Diagnostic> diagnostics)
    {
        if (visualOverride is null ||
            visualOverride.TargetSceneObjectId is null ||
            visualOverride.Geometry is null ||
            visualOverride.Style is null)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidCanonicalItemVisualOverride,
                $"Canvas2D scene contributor '{descriptor.ContributorId}' returned a null or " +
                "incomplete canonical Scene-item visual override.",
                descriptor.ContributorId.Value));
            return false;
        }

        if (!canonicalItems.TryGetValue(
                visualOverride.TargetSceneObjectId,
                out var target))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidCanonicalItemVisualOverride,
                $"Canonical Scene-item visual override target " +
                $"'{visualOverride.TargetSceneObjectId}' does not exist.",
                visualOverride.TargetSceneObjectId.Value,
                new KeyValuePair<string, string>(
                    "ContributorId",
                    descriptor.ContributorId.Value)));
            return false;
        }

        const Canvas2DSceneOriginCategory ForbiddenCategories =
            Canvas2DSceneOriginCategory.EditorState |
            Canvas2DSceneOriginCategory.Configuration |
            Canvas2DSceneOriginCategory.RegisteredExtension;
        if ((target.Origin.Categories & ForbiddenCategories) != 0)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidCanonicalItemVisualOverride,
                $"Scene item '{target.Id}' is not an overridable canonical model item.",
                target.Id.Value,
                new KeyValuePair<string, string>(
                    "ContributorId",
                    descriptor.ContributorId.Value)));
            return false;
        }

        if (!AreCompatibleVisualGeometryCategories(
                target.Geometry,
                visualOverride.Geometry))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidCanonicalItemVisualOverride,
                $"Scene item '{target.Id}' cannot replace geometry kind " +
                $"'{target.Geometry.Kind}' with incompatible geometry kind " +
                $"'{visualOverride.Geometry.Kind}'.",
                target.Id.Value,
                new KeyValuePair<string, string>(
                    "ContributorId",
                    descriptor.ContributorId.Value)));
            return false;
        }

        return true;
    }

    private static bool AreCompatibleVisualGeometryCategories(
        Canvas2DSceneGeometry target,
        Canvas2DSceneGeometry replacement)
    {
        var targetCategory = GetVisualGeometryCategory(target);
        return targetCategory != CanonicalVisualGeometryCategory.Invalid &&
            targetCategory == GetVisualGeometryCategory(replacement);
    }

    private static CanonicalVisualGeometryCategory GetVisualGeometryCategory(
        Canvas2DSceneGeometry geometry) =>
        geometry.Kind switch
        {
            Canvas2DSceneGeometryKind.Rectangle or Canvas2DSceneGeometryKind.Ellipse =>
                CanonicalVisualGeometryCategory.Surface,
            Canvas2DSceneGeometryKind.Path when geometry.IsClosed =>
                CanonicalVisualGeometryCategory.Surface,
            Canvas2DSceneGeometryKind.Path => CanonicalVisualGeometryCategory.OpenPath,
            Canvas2DSceneGeometryKind.Text => CanonicalVisualGeometryCategory.Text,
            Canvas2DSceneGeometryKind.Image => CanonicalVisualGeometryCategory.Image,
            _ => CanonicalVisualGeometryCategory.Invalid,
        };

    private enum CanonicalVisualGeometryCategory
    {
        Invalid,
        Surface,
        OpenPath,
        Text,
        Image,
    }

    private static bool IsValidContributorItem(
        Canvas2DSceneItem? item,
        Canvas2DSceneContributorDescriptor descriptor,
        List<Diagnostic> diagnostics)
    {
        if (item is null || item.Id is null || item.Origin is null)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidContribution,
                $"Canvas2D scene contributor '{descriptor.ContributorId}' returned a null or incomplete item.",
                descriptor.ContributorId.Value));
            return false;
        }

        if ((item.Origin.Categories & Canvas2DSceneOriginCategory.RegisteredExtension) == 0 ||
            item.Origin.StableSourceKey is null ||
            item.Id != Canvas2DSceneObjectIdentity.ForExtension(
                descriptor.ContributorId,
                item.Origin.StableSourceKey))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidSourceTrace,
                $"Contributor item '{item.Id}' is not namespaced by contributor '{descriptor.ContributorId}'.",
                item.Id.Value));
            return false;
        }

        return true;
    }

    private static void ValidateAndOrderSceneItems(
        ProjectedGraph graph,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics,
        ScenePresentationInput? presentation,
        Canvas2DSpatialPresentationPlan? spatialPresentationPlan)
    {
        var projectedObjects = new Dictionary<ProjectedObjectId, IProjectedObject>();
        foreach (var projectedObject in EnumerateProjectedObjects(graph))
        {
            if (projectedObject is not null && projectedObject.Id is not null)
            {
                projectedObjects.TryAdd(projectedObject.Id, projectedObject);
            }
        }

        var declaredSpatialRegions = spatialPresentationPlan?.Regions.ToDictionary(
            static region => region.Id) ?? [];

        var visualsById = new Dictionary<VisualStateId, VisualStateSnapshot>();
        foreach (var visual in visualModel.VisualStates)
        {
            if (visual is not null && visual.Id is not null)
            {
                visualsById.TryAdd(visual.Id, visual);
            }
        }

        var itemsById = new Dictionary<SceneObjectId, Canvas2DSceneItem>();
        foreach (var item in items)
        {
            if (item is null || item.Id is null)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidInput,
                    "Canvas2DScene contains a null item or identity.",
                    "Canvas2DSceneItem"));
                continue;
            }

            if (!itemsById.TryAdd(item.Id, item))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.DuplicateSceneObjectId,
                    $"Scene object ID '{item.Id}' occurs more than once.",
                    item.Id.Value));
            }

            ValidateItem(item, diagnostics);
            if (item.SpatialRegion is { } region &&
                (!declaredSpatialRegions.TryGetValue(region.Id, out var declared) ||
                 !declared.Equals(region)))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidInput,
                    $"Scene item '{item.Id}' does not refer to an exact declared spatial region.",
                    item.Id.Value));
            }
        }

        foreach (var item in items)
        {
            if (item is null || item.Id is null || item.Origin is null)
            {
                continue;
            }

            ValidateTrace(
                item,
                projectedObjects,
                visualsById,
                itemsById,
                diagnostics,
                presentation);
        }

        var representedProjectedIds = items
            .Where(static item => item is not null && item.Origin is not null)
            .Select(static item => item.Origin.ProjectedObjectId)
            .Where(static id => id is not null)
            .Cast<ProjectedObjectId>()
            .ToHashSet();
        var intentionallyOmittedEdgeIds = routing.NoRouteEdgeIds.ToHashSet();
        foreach (var projectedObject in graph.Nodes.Cast<IProjectedObject>()
                     .Concat(graph.Edges)
                     .Concat(graph.Groups)
                     .Concat(graph.Labels))
        {
            var isIntentionallyOmitted = projectedObject is ProjectedLabel label &&
                intentionallyOmittedEdgeIds.Contains(label.OwnerId);
            if (isIntentionallyOmitted)
            {
                continue;
            }

            if (!representedProjectedIds.Contains(projectedObject.Id))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.MissingProjectedObject,
                    $"Projected object '{projectedObject.Id}' has no scene representation.",
                    projectedObject.Id.Value));
            }
        }

        items.Sort(CompareItems);
    }

    private static void ValidateItem(
        Canvas2DSceneItem item,
        List<Diagnostic> diagnostics)
    {
        if (!Enum.IsDefined(item.Layer) ||
            item.Geometry is null ||
            item.Style is null ||
            item.HitTestPolicy is null ||
            item.Origin is null ||
            item.PersistentAppearance is null ||
            item.Metadata is null ||
            !IsValid(item.Transform))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidInput,
                $"Scene item '{item.Id}' is not fully initialized.",
                item.Id.Value));
            return;
        }

        ValidateGeometry(item, diagnostics);
        ValidateStyle(item, diagnostics);

        if (!TryCalculateTransformedBounds(
                item.Geometry.Bounds,
                item.Transform,
                out var transformedBounds) ||
            !ContainsWithinFloatingTolerance(item.Bounds, transformedBounds))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Scene item '{item.Id}' has bounds that do not contain its transformed geometry.",
                item.Id.Value));
        }

        if (item.Clip is not null && !IsValid(item.Clip.Value))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidClipping,
                $"Scene item '{item.Id}' has invalid clipping geometry.",
                item.Id.Value));
        }

        if (!Enum.IsDefined(item.HitTestPolicy.Mode) ||
            !double.IsFinite(item.HitTestPolicy.StrokeTolerance) ||
            item.HitTestPolicy.StrokeTolerance < 0d)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Scene item '{item.Id}' has an invalid hit-test policy.",
                item.Id.Value));
        }
    }

    private static void ValidateGeometry(
        Canvas2DSceneItem item,
        List<Diagnostic> diagnostics)
    {
        var geometry = item.Geometry;
        var invalid = !IsValid(item.Bounds) ||
            !Enum.IsDefined(geometry.Kind) ||
            !IsValid(geometry.Bounds) ||
            !IsValid(geometry.TextAnchor) ||
            !Enum.IsDefined(geometry.TextAlignment) ||
            !Enum.IsDefined(geometry.TextBaseline) ||
            geometry.Points.IsDefault ||
            geometry.Points.Any(static point => !IsValid(point));

        invalid |= geometry.Kind switch
        {
            Canvas2DSceneGeometryKind.Rectangle or Canvas2DSceneGeometryKind.Ellipse =>
                !geometry.Points.IsEmpty || geometry.Content is not null || !geometry.IsClosed,
            Canvas2DSceneGeometryKind.Path =>
                geometry.Content is not null ||
                geometry.Points.Length < (geometry.IsClosed ? 3 : 2) ||
                !geometry.Bounds.Equals(CalculateBounds(geometry.Points)),
            Canvas2DSceneGeometryKind.Text =>
                !geometry.Points.IsEmpty || geometry.Content is null || geometry.IsClosed,
            Canvas2DSceneGeometryKind.Image =>
                !geometry.Points.IsEmpty ||
                string.IsNullOrWhiteSpace(geometry.Content) ||
                geometry.IsClosed,
            _ => true,
        };

        if (invalid)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Scene item '{item.Id}' has invalid immutable geometry.",
                item.Id.Value));
        }
    }

    private static void ValidateStyle(
        Canvas2DSceneItem item,
        List<Diagnostic> diagnostics)
    {
        var style = item.Style;
        if ((style.Fill is not null && string.IsNullOrWhiteSpace(style.Fill)) ||
            (style.Stroke is not null && string.IsNullOrWhiteSpace(style.Stroke)) ||
            (style.FontFamily is not null && string.IsNullOrWhiteSpace(style.FontFamily)) ||
            !double.IsFinite(style.StrokeWidth) || style.StrokeWidth < 0d ||
            style.DashPattern.IsDefault ||
            style.DashPattern.Any(static value => !double.IsFinite(value) || value < 0d) ||
            !double.IsFinite(style.Opacity) || style.Opacity < 0d || style.Opacity > 1d ||
            !double.IsFinite(style.FontSize) || style.FontSize <= 0d)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Scene item '{item.Id}' has invalid immutable style data.",
                item.Id.Value));
        }
    }

    private static void ValidateTrace(
        Canvas2DSceneItem item,
        Dictionary<ProjectedObjectId, IProjectedObject> projectedObjects,
        Dictionary<VisualStateId, VisualStateSnapshot> visualsById,
        Dictionary<SceneObjectId, Canvas2DSceneItem> sceneItemsById,
        List<Diagnostic> diagnostics,
        ScenePresentationInput? presentation)
    {
        var trace = item.Origin;
#pragma warning disable CA1031 // Reconstructing the immutable contract is defensive validation.
        try
        {
            _ = new Canvas2DSceneOriginTrace(
                trace.Categories,
                trace.SemanticElementId,
                trace.VisualStateId,
                trace.ProjectedObjectId,
                trace.StableSourceKey,
                trace.RelatedProjectedObjectIds,
                trace.RelatedSceneObjectIds);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidSourceTrace,
                $"Scene item '{item.Id}' has an invalid source trace.",
                item.Id.Value,
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name)));
            return;
        }
#pragma warning restore CA1031

        foreach (var relatedId in trace.RelatedProjectedObjectIds)
        {
            if (!projectedObjects.ContainsKey(relatedId))
            {
                diagnostics.Add(InvalidTrace(item, $"unknown related projected object '{relatedId}'"));
            }
        }

        foreach (var relatedId in trace.RelatedSceneObjectIds)
        {
            if (relatedId == item.Id || !sceneItemsById.ContainsKey(relatedId))
            {
                diagnostics.Add(InvalidTrace(item, $"unknown related scene object '{relatedId}'"));
            }
        }

        VisualStateSnapshot? tracedVisual = null;
        if (trace.VisualStateId is not null &&
            (!visualsById.TryGetValue(trace.VisualStateId, out tracedVisual) ||
             trace.SemanticElementId is null ||
             tracedVisual.SemanticElementId != trace.SemanticElementId))
        {
            diagnostics.Add(InvalidTrace(
                item,
                $"incompatible Visual state '{trace.VisualStateId}'"));
        }

        if (trace.ProjectedObjectId is null)
        {
            if (trace.SemanticElementId is not null &&
                (tracedVisual is null ||
                 tracedVisual.SemanticElementId != trace.SemanticElementId) &&
                !trace.RelatedProjectedObjectIds.Any(relatedId =>
                    projectedObjects.TryGetValue(relatedId, out var relatedObject) &&
                    relatedObject.Source.SemanticElementId == trace.SemanticElementId) &&
                !IsPresentationSemanticSceneSource(
                    trace,
                    presentation,
                    sceneItemsById))
            {
                diagnostics.Add(InvalidTrace(
                    item,
                    $"untraceable semantic source '{trace.SemanticElementId}'"));
            }

            return;
        }

        if (!projectedObjects.TryGetValue(trace.ProjectedObjectId, out var projectedObject) ||
            trace.SemanticElementId != projectedObject.Source.SemanticElementId ||
            trace.VisualStateId != projectedObject.Source.VisualStateId)
        {
            diagnostics.Add(InvalidTrace(
                item,
                $"incompatible projected source '{trace.ProjectedObjectId}'"));
            return;
        }

        var requiredRelatedIds = projectedObject switch
        {
            ProjectedEdge edge => GetEdgeRelationships(edge),
            ProjectedGroup group => group.MemberNodeIds,
            ProjectedPort port => [port.OwnerNodeId],
            ProjectedLabel label => [label.OwnerId],
            _ => [],
        };
        foreach (var requiredId in requiredRelatedIds)
        {
            if (!trace.RelatedProjectedObjectIds.Contains(requiredId))
            {
                diagnostics.Add(InvalidTrace(
                    item,
                    $"incomplete projected relationship trace; missing '{requiredId}'"));
            }
        }
    }

    private static bool IsPresentationSemanticSceneSource(
        Canvas2DSceneOriginTrace trace,
        ScenePresentationInput? presentation,
        Dictionary<SceneObjectId, Canvas2DSceneItem> sceneItemsById)
    {
        if (trace.VisualStateId is not null ||
            trace.SemanticElementId is null ||
            presentation is null ||
            !presentation.Document.SemanticModel.TryGetElement(
                trace.SemanticElementId,
                out var element) ||
            element is null)
        {
            return false;
        }

        var isRegisteredPresentationSource =
            (trace.Categories & Canvas2DSceneOriginCategory.RegisteredExtension) != 0;
        var isDerivedEditorSource =
            (trace.Categories & Canvas2DSceneOriginCategory.EditorState) != 0 &&
            trace.RelatedSceneObjectIds.Length == 1 &&
            sceneItemsById.TryGetValue(trace.RelatedSceneObjectIds[0], out var related) &&
            related.Origin.SemanticElementId == trace.SemanticElementId &&
            related.Origin.VisualStateId is null &&
            related.Origin.ProjectedObjectId is null &&
            (related.Origin.Categories & Canvas2DSceneOriginCategory.RegisteredExtension) != 0 &&
            (related.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            Canvas2DSemanticSceneInteractionMetadata.IsInteractionCapable(related);
        if (!isRegisteredPresentationSource && !isDerivedEditorSource)
        {
            return false;
        }

        if (element.ContainmentKind == SemanticElementContainmentKind.Document)
        {
            return true;
        }

        return element.ContainmentKind == SemanticElementContainmentKind.Scope &&
            presentation.Document.SemanticModel.TryGetScope(element.Id, out var scope) &&
            scope?.Id == presentation.ActiveScopeId;
    }

    private static IEnumerable<ProjectedObjectId> GetEdgeRelationships(ProjectedEdge edge)
    {
        yield return edge.SourceNodeId;
        yield return edge.TargetNodeId;
        if (edge.SourcePortId is not null)
        {
            yield return edge.SourcePortId;
        }

        if (edge.TargetPortId is not null)
        {
            yield return edge.TargetPortId;
        }
    }

    private static Diagnostic InvalidTrace(Canvas2DSceneItem item, string detail) =>
        Error(
            Canvas2DSceneDiagnosticCodes.InvalidSourceTrace,
            $"Scene item '{item.Id}' has an {detail}.",
            item.Id.Value);

    private static RectD CalculateBounds(IEnumerable<PointD> points)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return default;
        }

        var minimumX = enumerator.Current.X;
        var minimumY = enumerator.Current.Y;
        var maximumX = enumerator.Current.X;
        var maximumY = enumerator.Current.Y;
        while (enumerator.MoveNext())
        {
            minimumX = Math.Min(minimumX, enumerator.Current.X);
            minimumY = Math.Min(minimumY, enumerator.Current.Y);
            maximumX = Math.Max(maximumX, enumerator.Current.X);
            maximumY = Math.Max(maximumY, enumerator.Current.Y);
        }

        return new RectD(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }

    private static bool TryCalculateTransformedBounds(
        RectD bounds,
        Matrix2D transform,
        out RectD transformedBounds)
    {
        var corners = new[]
        {
            Transform(bounds.Left, bounds.Top, transform),
            Transform(bounds.Right, bounds.Top, transform),
            Transform(bounds.Left, bounds.Bottom, transform),
            Transform(bounds.Right, bounds.Bottom, transform),
        };
        if (corners.Any(static point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            transformedBounds = default;
            return false;
        }

        var minimumX = corners.Min(static point => point.X);
        var minimumY = corners.Min(static point => point.Y);
        var maximumX = corners.Max(static point => point.X);
        var maximumY = corners.Max(static point => point.Y);
        var width = maximumX - minimumX;
        var height = maximumY - minimumY;
        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            transformedBounds = default;
            return false;
        }

        transformedBounds = new RectD(minimumX, minimumY, width, height);
        return true;
    }

    private static bool ContainsWithinFloatingTolerance(
        RectD container,
        RectD candidate)
    {
        return candidate.Left >= ExpandLowerEdge(container.Left) &&
            candidate.Right <= ExpandUpperEdge(container.Right) &&
            candidate.Top >= ExpandLowerEdge(container.Top) &&
            candidate.Bottom <= ExpandUpperEdge(container.Bottom);
    }

    private static double ExpandLowerEdge(double edge)
    {
        for (var index = 0; index < TransformedBoundsContainmentToleranceUlps; index++)
        {
            edge = Math.BitDecrement(edge);
        }

        return edge;
    }

    private static double ExpandUpperEdge(double edge)
    {
        for (var index = 0; index < TransformedBoundsContainmentToleranceUlps; index++)
        {
            edge = Math.BitIncrement(edge);
        }

        return edge;
    }

    private static (double X, double Y) Transform(
        double x,
        double y,
        Matrix2D transform) =>
        (
            (transform.M11 * x) + (transform.M21 * y) + transform.OffsetX,
            (transform.M12 * x) + (transform.M22 * y) + transform.OffsetY);

    private static int CompareItems(Canvas2DSceneItem left, Canvas2DSceneItem right)
    {
        var comparison = left.Layer.CompareTo(right.Layer);
        comparison = comparison != 0 ? comparison : left.ZIndex.CompareTo(right.ZIndex);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value);
    }
}
