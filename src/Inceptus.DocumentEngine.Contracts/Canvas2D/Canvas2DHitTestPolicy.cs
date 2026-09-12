namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public sealed class Canvas2DHitTestPolicy : IEquatable<Canvas2DHitTestPolicy>
{
    public Canvas2DHitTestPolicy(Canvas2DHitTestMode mode, double strokeTolerance = 0d)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The hit-test mode must be defined.");
        }

        if (!double.IsFinite(strokeTolerance) || strokeTolerance < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strokeTolerance), strokeTolerance, "Stroke tolerance must be finite and non-negative.");
        }

        Mode = mode;
        StrokeTolerance = strokeTolerance;
    }

    public static Canvas2DHitTestPolicy None { get; } = new(Canvas2DHitTestMode.None);

    public Canvas2DHitTestMode Mode { get; }

    public double StrokeTolerance { get; }

    public bool Equals(Canvas2DHitTestPolicy? other) =>
        ReferenceEquals(this, other) ||
        other is not null && Mode == other.Mode && StrokeTolerance.Equals(other.StrokeTolerance);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DHitTestPolicy);

    public override int GetHashCode() => HashCode.Combine(Mode, StrokeTolerance);
}
