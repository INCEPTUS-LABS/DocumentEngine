using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Declares that a semantic-only Scene item is an intentional transient interaction target.
/// </summary>
public static class Canvas2DSemanticSceneInteractionMetadata
{
    public const string InteractionCapable =
        "Inceptus.DocumentEngine.Canvas2D.SemanticSceneInteraction.InteractionCapable";

    public static KeyValuePair<string, PropertyValue> Enabled { get; } =
        new(InteractionCapable, PropertyValue.FromBoolean(true));

    public const string PlacementBlocked =
        "Inceptus.DocumentEngine.Canvas2D.SceneInteraction.PlacementBlocked";

    public static KeyValuePair<string, PropertyValue> PlacementBlockedEntry { get; } =
        new(PlacementBlocked, PropertyValue.FromBoolean(true));

    public static bool BlocksPlacement(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Metadata.TryGetValue(PlacementBlocked, out var value) &&
            value.Kind == PropertyValueKind.Boolean && value.BooleanValue;
    }

    public static bool IsInteractionCapable(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Origin.SemanticElementId is not null &&
            item.Origin.VisualStateId is null &&
            item.Metadata.TryGetValue(InteractionCapable, out var value) &&
            value.Kind == PropertyValueKind.Boolean &&
            value.BooleanValue;
    }
}
