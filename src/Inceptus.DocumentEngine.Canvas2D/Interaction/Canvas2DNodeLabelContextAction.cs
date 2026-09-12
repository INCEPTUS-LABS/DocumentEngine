using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Describes one transient, revision-bound reset action for a manual node-label override.
/// </summary>
public sealed class Canvas2DNodeLabelContextAction
{
    internal Canvas2DNodeLabelContextAction(
        VisualStateId targetVisualStateId,
        ProjectedObjectId targetLabelProjectedObjectId,
        SceneObjectId sourceSceneObjectId,
        SceneObjectId targetNodeSceneObjectId)
    {
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetLabelProjectedObjectId);
        ArgumentNullException.ThrowIfNull(sourceSceneObjectId);
        ArgumentNullException.ThrowIfNull(targetNodeSceneObjectId);

        TargetVisualStateId = targetVisualStateId;
        TargetLabelProjectedObjectId = targetLabelProjectedObjectId;
        SourceSceneObjectId = sourceSceneObjectId;
        TargetNodeSceneObjectId = targetNodeSceneObjectId;
    }

    public VisualStateId TargetVisualStateId { get; }

    public ProjectedObjectId TargetLabelProjectedObjectId { get; }

    /// <summary>
    /// Gets the transient label-box Scene identity that supplied this exact context.
    /// </summary>
    public SceneObjectId SourceSceneObjectId { get; }

    /// <summary>
    /// Gets the owning node's transient canonical Scene identity for revalidation.
    /// </summary>
    public SceneObjectId TargetNodeSceneObjectId { get; }

    public bool IsCurrent(Canvas2DScene scene, VisualStateSnapshot visualState)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(visualState);
        if (visualState.Id != TargetVisualStateId ||
            !NodeLabelVisualOverride.TryRead(visualState.Properties, out _))
        {
            return false;
        }

        var target = scene.Items.SingleOrDefault(item =>
            item.Id == TargetNodeSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == TargetVisualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        var source = scene.Items.SingleOrDefault(item =>
            item.Id == SourceSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == TargetVisualStateId &&
            item.Origin.ProjectedObjectId == TargetLabelProjectedObjectId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0);
        return target is not null &&
            source is not null &&
            source.Origin.RelatedSceneObjectIds.Contains(TargetNodeSceneObjectId) &&
            HasTrue(source, Canvas2DNodeLabelGestureMetadata.InteractionCapable) &&
            HasText(source, Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                TargetNodeSceneObjectId.Value) &&
            HasText(source, Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                SourceSceneObjectId.Value) &&
            HasText(source, Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                TargetVisualStateId.Value) &&
            HasText(source, Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                TargetLabelProjectedObjectId.Value);
    }

    private static bool HasText(Canvas2DSceneItem item, string key, string expected) =>
        item.Metadata.TryGetValue(key, out var value) &&
        value.Kind == PropertyValueKind.Text &&
        StringComparer.Ordinal.Equals(value.TextValue, expected);

    private static bool HasTrue(Canvas2DSceneItem item, string key) =>
        item.Metadata.TryGetValue(key, out var value) &&
        value.Kind == PropertyValueKind.Boolean &&
        value.BooleanValue;
}
