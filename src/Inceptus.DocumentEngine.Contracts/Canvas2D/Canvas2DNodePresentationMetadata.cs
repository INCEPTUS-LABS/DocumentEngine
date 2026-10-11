using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Derived node appearance hints; these are never persistent semantic state.</summary>
public static class Canvas2DNodePresentationMetadata
{
    public const string BoundaryContainerKey = "Canvas2D.Node.BoundaryContainer";

    public static KeyValuePair<string, PropertyValue> BoundaryContainer { get; } =
        new(BoundaryContainerKey, PropertyValue.FromBoolean(true));

    public static bool IsBoundaryContainer(PropertyMap properties) =>
        properties.TryGetValue(BoundaryContainerKey, out var value) &&
        value.Kind == PropertyValueKind.Boolean && value.BooleanValue;
}
