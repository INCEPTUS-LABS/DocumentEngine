using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Runtime-only metadata that distinguishes derived target-arrow geometry from the editable route.
/// </summary>
internal static class Canvas2DConnectorArrowMetadata
{
    internal const string TargetArrow = "inceptus.canvas2d:connector-target-arrow";

    internal static PropertyValue TargetArrowValue { get; } =
        PropertyValue.FromBoolean(true);
}
