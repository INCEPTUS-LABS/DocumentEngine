using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Generic metadata that identifies the canonical interactive body of a projected node.
/// </summary>
internal static class Canvas2DNodeBodyMetadata
{
    internal const string NodeBody = "inceptus.canvas2d:node-body";

    internal static PropertyMap MoveResizeAndNodeBodyCapability { get; } = new(
    [
        new KeyValuePair<string, PropertyValue>(
            Canvas2DMoveGestureMetadata.MoveCapable,
            PropertyValue.FromBoolean(true)),
        new KeyValuePair<string, PropertyValue>(
            Canvas2DResizeGestureMetadata.ResizeCapable,
            PropertyValue.FromBoolean(true)),
        new KeyValuePair<string, PropertyValue>(
            NodeBody,
            PropertyValue.FromBoolean(true)),
    ]);

    internal static PropertyMap AttachedMoveAndNodeBodyCapability { get; } = new(
    [
        new KeyValuePair<string, PropertyValue>(
            Canvas2DMoveGestureMetadata.MoveCapable,
            PropertyValue.FromBoolean(true)),
        new KeyValuePair<string, PropertyValue>(
            NodeBody,
            PropertyValue.FromBoolean(true)),
    ]);

    internal static bool IsNodeBody(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Layer == Canvas2DSceneLayer.Content &&
            item.Metadata.TryGetValue(NodeBody, out var marker) &&
            marker.Kind == PropertyValueKind.Boolean &&
            marker.BooleanValue;
    }
}
