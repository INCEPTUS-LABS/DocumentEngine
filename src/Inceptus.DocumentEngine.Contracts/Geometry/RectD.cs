namespace Inceptus.DocumentEngine.Contracts.Geometry;

public readonly record struct RectD
{
    public RectD(double x, double y, double width, double height)
    {
        X = GeometryGuard.RequireFinite(x, nameof(x));
        Y = GeometryGuard.RequireFinite(y, nameof(y));
        Width = GeometryGuard.RequireNonNegative(width, nameof(width));
        Height = GeometryGuard.RequireNonNegative(height, nameof(height));

        GeometryGuard.RequireFiniteSum(X, Width, nameof(width));
        GeometryGuard.RequireFiniteSum(Y, Height, nameof(height));
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public PointD TopLeft => new(X, Y);

    public SizeD Size => new(Width, Height);

    public bool IsEmpty => Width == 0d || Height == 0d;

    public bool Contains(PointD point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool Contains(RectD rectangle) =>
        rectangle.Left >= Left && rectangle.Right <= Right &&
        rectangle.Top >= Top && rectangle.Bottom <= Bottom;

    public bool Intersects(RectD rectangle) =>
        !IsEmpty && !rectangle.IsEmpty &&
        Left < rectangle.Right && rectangle.Left < Right &&
        Top < rectangle.Bottom && rectangle.Top < Bottom;

    public RectD? Intersection(RectD rectangle)
    {
        if (!Intersects(rectangle))
        {
            return null;
        }

        var left = Math.Max(Left, rectangle.Left);
        var top = Math.Max(Top, rectangle.Top);
        var right = Math.Min(Right, rectangle.Right);
        var bottom = Math.Min(Bottom, rectangle.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    public RectD Translate(VectorD translation) =>
        new(X + translation.X, Y + translation.Y, Width, Height);
}
