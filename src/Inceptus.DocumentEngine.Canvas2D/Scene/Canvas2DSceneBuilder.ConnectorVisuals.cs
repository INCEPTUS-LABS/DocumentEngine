using Inceptus.DocumentEngine.Contracts.Visuals;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private const string ConnectorSourceDecorationKey = "inceptus.canvas2d:connector-source-decoration-item";

    private static ImmutableArray<KeyValuePair<string, PropertyValue>> CreateConnectorVisualMetadata(ConnectorVisualDescriptor descriptor) =>
        descriptor.Equals(ConnectorVisualDescriptor.Default) ? [] : ConnectorVisualMetadata.Encode(descriptor);

    private static Canvas2DSceneGeometry? ConnectorDecorationGeometry(IReadOnlyList<PointD> path,
        ConnectorEndpointDecoration decoration, bool source)
    {
        if (path.Count < 2 || decoration == ConnectorEndpointDecoration.None) return null;
        if (decoration == ConnectorEndpointDecoration.OpenCircle)
        {
            var point = source ? path[0] : path[^1];
            return Canvas2DSceneGeometry.Ellipse(new RectD(point.X - 4d, point.Y - 4d, 8d, 8d));
        }
        var directed = source ? path.Reverse().ToArray() : path;
        return Canvas2DConnectorArrowGeometry.Create(directed);
    }

    private static Canvas2DSceneStyle ConnectorDecorationStyle(ConnectorEndpointDecoration decoration,
        string stroke, double strokeWidth = 1d, double opacity = 1d) => new(
            fill: decoration == ConnectorEndpointDecoration.FilledArrow ? stroke :
                decoration is ConnectorEndpointDecoration.OpenCircle or ConnectorEndpointDecoration.OpenArrow ? "#ffffff" : null,
            stroke: stroke, strokeWidth: strokeWidth, opacity: opacity);

    private static IEnumerable<Canvas2DSceneItem> CreateConnectorDecorations(ProjectedEdge edge,
        Canvas2DSceneOriginTrace origin, IEnumerable<KeyValuePair<string, PropertyValue>> persistentAppearance,
        IReadOnlyList<PointD> path, ConnectorVisualDescriptor descriptor,
        bool isVisible = true, Canvas2DConnectorPresentationMapping? mapping = null)
    {
        foreach (var source in new[] { true, false })
        {
            var decoration = source ? descriptor.SourceDecoration : descriptor.TargetDecoration;
            if (ConnectorDecorationGeometry(path, decoration, source) is not { } geometry) continue;
            yield return new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForProjected(edge.Id,
                source ? "connector-source-decoration" : "connector-target-arrow"), Canvas2DSceneLayer.Connector,
                ConnectorBaseTargetArrowZIndex, geometry, origin, style: ConnectorDecorationStyle(decoration, "#000000"),
                isVisible: isVisible,
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.FillOrStroke, 1d),
                persistentAppearance: persistentAppearance,
                metadata: CreateConnectorVisualMetadata(descriptor).Append(KeyValuePair.Create(
                    source ? ConnectorSourceDecorationKey : Canvas2DConnectorArrowMetadata.TargetArrow,
                    PropertyValue.FromBoolean(true))), connectorPresentationMapping: mapping);
        }
    }

    private static IEnumerable<Canvas2DSceneItem> CreatePreviewConnectorDecorations(string stableKey,
        Canvas2DSceneOriginTrace origin, IReadOnlyList<PointD> path,
        ConnectorVisualDescriptor descriptor, int zIndex, double opacity = 1d,
        bool? sourceOnly = null)
    {
        // Endpoint feedback remains visible above the candidate handle at the same point.
        // The decoration is non-hittable, so the underlying anchor still receives input.
        if (!descriptor.Equals(ConnectorVisualDescriptor.Default))
            zIndex = Math.Max(zIndex, Canvas2DConnectorAnchorMetadata.HandleZIndex + 1);
        foreach (var source in new[] { true, false })
        {
            if (sourceOnly.HasValue && sourceOnly.Value != source) continue;
            var decoration = source ? descriptor.SourceDecoration : descriptor.TargetDecoration;
            if (ConnectorDecorationGeometry(path, decoration, source) is not { } geometry) continue;
            var key = stableKey + (source ? ":source-decoration" : ":target-decoration");
            yield return new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForEditorState(key), Canvas2DSceneLayer.Overlay,
                zIndex, geometry, new Canvas2DSceneOriginTrace(origin.Categories, origin.SemanticElementId,
                    origin.VisualStateId, origin.ProjectedObjectId, key, origin.RelatedProjectedObjectIds, origin.RelatedSceneObjectIds),
                style: ConnectorDecorationStyle(decoration, "#2563eb", opacity: opacity),
                hitTestPolicy: Canvas2DHitTestPolicy.None,
                metadata: CreateConnectorVisualMetadata(descriptor).Append(KeyValuePair.Create(
                    source ? ConnectorSourceDecorationKey : Canvas2DConnectorArrowMetadata.TargetArrow,
                    PropertyValue.FromBoolean(true))));
        }
    }
}
