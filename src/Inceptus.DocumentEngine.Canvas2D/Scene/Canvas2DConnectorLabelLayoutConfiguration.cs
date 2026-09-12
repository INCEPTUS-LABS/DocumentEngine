namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Immutable generic layout policy for connector-owned Canvas2D labels.
/// Values use logical document-coordinate units.
/// </summary>
internal sealed class Canvas2DConnectorLabelLayoutConfiguration
{
    internal Canvas2DConnectorLabelLayoutConfiguration(
        double maximumWidth = 160d,
        double lineHeightMultiplier = 1.2d)
    {
        if (!double.IsFinite(maximumWidth) || maximumWidth <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        if (!double.IsFinite(lineHeightMultiplier) || lineHeightMultiplier <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(lineHeightMultiplier));
        }

        MaximumWidth = maximumWidth;
        LineHeightMultiplier = lineHeightMultiplier;
    }

    internal static Canvas2DConnectorLabelLayoutConfiguration Default { get; } = new();

    internal double MaximumWidth { get; }

    internal double LineHeightMultiplier { get; }
}
