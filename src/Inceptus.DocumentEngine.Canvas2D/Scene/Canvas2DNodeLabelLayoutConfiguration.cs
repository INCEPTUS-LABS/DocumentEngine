namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Immutable generic layout policy for automatic node-owned Canvas2D labels.
/// Values use logical document-coordinate units.
/// </summary>
internal sealed class Canvas2DNodeLabelLayoutConfiguration :
    IEquatable<Canvas2DNodeLabelLayoutConfiguration>
{
    public Canvas2DNodeLabelLayoutConfiguration(
        double horizontalPadding = 8d,
        double verticalPadding = 8d,
        double lineHeightMultiplier = 1.2d)
    {
        HorizontalPadding = RequireFiniteNonNegative(
            horizontalPadding,
            nameof(horizontalPadding));
        VerticalPadding = RequireFiniteNonNegative(
            verticalPadding,
            nameof(verticalPadding));
        if (!double.IsFinite(lineHeightMultiplier) || lineHeightMultiplier <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineHeightMultiplier),
                lineHeightMultiplier,
                "The line-height multiplier must be finite and greater than zero.");
        }

        LineHeightMultiplier = lineHeightMultiplier;
    }

    public static Canvas2DNodeLabelLayoutConfiguration Default { get; } = new();

    public double HorizontalPadding { get; }

    public double VerticalPadding { get; }

    public double LineHeightMultiplier { get; }

    public bool Equals(Canvas2DNodeLabelLayoutConfiguration? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        HorizontalPadding.Equals(other.HorizontalPadding) &&
        VerticalPadding.Equals(other.VerticalPadding) &&
        LineHeightMultiplier.Equals(other.LineHeightMultiplier);

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DNodeLabelLayoutConfiguration);

    public override int GetHashCode() => HashCode.Combine(
        HorizontalPadding,
        VerticalPadding,
        LineHeightMultiplier);

    private static double RequireFiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be finite and non-negative.");
        }

        return value;
    }
}
