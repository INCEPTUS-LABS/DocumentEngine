using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Notation-owned transient interaction capabilities carried from projection into Scene items.</summary>
public static class Canvas2DTransientInteractionMetadata
{
    /// <summary>Excludes an item only from ordinary pointer-move hover acquisition.</summary>
    public const string ExcludeFromHover = "inceptus.canvas2d:exclude-from-hover";

    /// <summary>Opts a node body into certified bounded ordinary hover and single visual selection.</summary>
    public const string BoundedSelectionEligible = "inceptus.canvas2d:bounded-selection-eligible";

    public static KeyValuePair<string, PropertyValue> HoverExcluded { get; } =
        new(ExcludeFromHover, PropertyValue.FromBoolean(true));

    public static KeyValuePair<string, PropertyValue> BoundedSelectionEnabled { get; } =
        new(BoundedSelectionEligible, PropertyValue.FromBoolean(true));
}
