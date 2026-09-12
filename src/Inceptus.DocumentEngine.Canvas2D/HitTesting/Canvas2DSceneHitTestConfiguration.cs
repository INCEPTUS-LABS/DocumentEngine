namespace Inceptus.DocumentEngine.Canvas2D.HitTesting;

/// <summary>
/// Immutable document-space configuration for geometric Canvas2D scene hit testing.
/// </summary>
public sealed class Canvas2DSceneHitTestConfiguration :
    IEquatable<Canvas2DSceneHitTestConfiguration>
{
    public Canvas2DSceneHitTestConfiguration(double minimumStrokeTolerance = 0d)
    {
        if (!double.IsFinite(minimumStrokeTolerance) || minimumStrokeTolerance < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumStrokeTolerance),
                minimumStrokeTolerance,
                "The minimum stroke tolerance must be finite and non-negative.");
        }

        MinimumStrokeTolerance = minimumStrokeTolerance;
    }

    public static Canvas2DSceneHitTestConfiguration Default { get; } = new();

    public double MinimumStrokeTolerance { get; }

    public bool Equals(Canvas2DSceneHitTestConfiguration? other) =>
        ReferenceEquals(this, other) ||
        other is not null && MinimumStrokeTolerance.Equals(other.MinimumStrokeTolerance);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneHitTestConfiguration);

    public override int GetHashCode() => MinimumStrokeTolerance.GetHashCode();
}
