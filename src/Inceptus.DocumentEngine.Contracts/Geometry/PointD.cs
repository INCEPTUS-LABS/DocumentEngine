namespace Inceptus.DocumentEngine.Contracts.Geometry;

public readonly record struct PointD
{
    public PointD(double x, double y)
    {
        X = GeometryGuard.RequireFinite(x, nameof(x));
        Y = GeometryGuard.RequireFinite(y, nameof(y));
    }

    public double X { get; }

    public double Y { get; }

    public PointD Translate(VectorD translation) => this + translation;

    public static PointD operator +(PointD point, VectorD vector) =>
        new(point.X + vector.X, point.Y + vector.Y);

    public static PointD operator -(PointD point, VectorD vector) =>
        new(point.X - vector.X, point.Y - vector.Y);

    public static VectorD operator -(PointD left, PointD right) =>
        new(left.X - right.X, left.Y - right.Y);
}

