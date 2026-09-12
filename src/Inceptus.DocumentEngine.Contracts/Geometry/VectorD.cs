namespace Inceptus.DocumentEngine.Contracts.Geometry;

public readonly record struct VectorD
{
    public VectorD(double x, double y)
    {
        X = GeometryGuard.RequireFinite(x, nameof(x));
        Y = GeometryGuard.RequireFinite(y, nameof(y));
    }

    public double X { get; }

    public double Y { get; }

    public static VectorD operator +(VectorD left, VectorD right) =>
        new(left.X + right.X, left.Y + right.Y);

    public static VectorD operator -(VectorD left, VectorD right) =>
        new(left.X - right.X, left.Y - right.Y);

    public static VectorD operator -(VectorD vector) => new(-vector.X, -vector.Y);

    public static VectorD operator *(VectorD vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar);

    public static VectorD operator *(double scalar, VectorD vector) => vector * scalar;
}

