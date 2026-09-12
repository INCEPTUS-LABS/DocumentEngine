namespace Inceptus.DocumentEngine.Contracts.Geometry;

public readonly record struct Matrix2D
{
    public const double DefaultInversionTolerance = 1e-12;

    public Matrix2D(
        double m11,
        double m12,
        double m21,
        double m22,
        double offsetX,
        double offsetY)
    {
        M11 = GeometryGuard.RequireFinite(m11, nameof(m11));
        M12 = GeometryGuard.RequireFinite(m12, nameof(m12));
        M21 = GeometryGuard.RequireFinite(m21, nameof(m21));
        M22 = GeometryGuard.RequireFinite(m22, nameof(m22));
        OffsetX = GeometryGuard.RequireFinite(offsetX, nameof(offsetX));
        OffsetY = GeometryGuard.RequireFinite(offsetY, nameof(offsetY));
    }

    public static Matrix2D Identity => new(1d, 0d, 0d, 1d, 0d, 0d);

    public double M11 { get; }

    public double M12 { get; }

    public double M21 { get; }

    public double M22 { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public static Matrix2D CreateTranslation(double x, double y) =>
        new(1d, 0d, 0d, 1d, x, y);

    public static Matrix2D CreateTranslation(VectorD translation) =>
        CreateTranslation(translation.X, translation.Y);

    public static Matrix2D CreateScale(double x, double y) =>
        new(x, 0d, 0d, y, 0d, 0d);

    public static Matrix2D CreateRotation(double radians)
    {
        GeometryGuard.RequireFinite(radians, nameof(radians));
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);

        return new Matrix2D(cosine, sine, -sine, cosine, 0d, 0d);
    }

    public PointD TransformPoint(PointD point) =>
        new(
            (M11 * point.X) + (M21 * point.Y) + OffsetX,
            (M12 * point.X) + (M22 * point.Y) + OffsetY);

    public VectorD TransformVector(VectorD vector) =>
        new(
            (M11 * vector.X) + (M21 * vector.Y),
            (M12 * vector.X) + (M22 * vector.Y));

    public Matrix2D Then(Matrix2D next) => Multiply(next, this);

    public bool TryInvert(out Matrix2D inverse, double relativeTolerance = DefaultInversionTolerance)
    {
        GeometryGuard.RequireNonNegative(relativeTolerance, nameof(relativeTolerance));

        var scale = Math.Max(
            Math.Max(Math.Abs(M11), Math.Abs(M12)),
            Math.Max(Math.Abs(M21), Math.Abs(M22)));

        if (scale == 0d)
        {
            inverse = default;
            return false;
        }

        var a = M11 / scale;
        var b = M12 / scale;
        var c = M21 / scale;
        var d = M22 / scale;
        var normalizedDeterminant = (a * d) - (b * c);

        if (!double.IsFinite(normalizedDeterminant) ||
            Math.Abs(normalizedDeterminant) <= relativeTolerance)
        {
            inverse = default;
            return false;
        }

        var factor = (1d / scale) / normalizedDeterminant;
        var inverseM11 = d * factor;
        var inverseM12 = -b * factor;
        var inverseM21 = -c * factor;
        var inverseM22 = a * factor;
        var inverseOffsetX = -((inverseM11 * OffsetX) + (inverseM21 * OffsetY));
        var inverseOffsetY = -((inverseM12 * OffsetX) + (inverseM22 * OffsetY));

        if (!AllFinite(
                inverseM11,
                inverseM12,
                inverseM21,
                inverseM22,
                inverseOffsetX,
                inverseOffsetY))
        {
            inverse = default;
            return false;
        }

        inverse = new Matrix2D(
            inverseM11,
            inverseM12,
            inverseM21,
            inverseM22,
            inverseOffsetX,
            inverseOffsetY);

        return true;
    }

    public Matrix2D Invert(double relativeTolerance = DefaultInversionTolerance)
    {
        if (!TryInvert(out var inverse, relativeTolerance))
        {
            throw new InvalidOperationException("The matrix is not invertible within the requested tolerance.");
        }

        return inverse;
    }

    private static Matrix2D Multiply(Matrix2D left, Matrix2D right) =>
        new(
            (left.M11 * right.M11) + (left.M21 * right.M12),
            (left.M12 * right.M11) + (left.M22 * right.M12),
            (left.M11 * right.M21) + (left.M21 * right.M22),
            (left.M12 * right.M21) + (left.M22 * right.M22),
            (left.M11 * right.OffsetX) + (left.M21 * right.OffsetY) + left.OffsetX,
            (left.M12 * right.OffsetX) + (left.M22 * right.OffsetY) + left.OffsetY);

    private static bool AllFinite(
        double first,
        double second,
        double third,
        double fourth,
        double fifth,
        double sixth) =>
        double.IsFinite(first) &&
        double.IsFinite(second) &&
        double.IsFinite(third) &&
        double.IsFinite(fourth) &&
        double.IsFinite(fifth) &&
        double.IsFinite(sixth);
}

