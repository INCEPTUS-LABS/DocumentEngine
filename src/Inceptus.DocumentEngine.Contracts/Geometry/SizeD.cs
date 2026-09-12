namespace Inceptus.DocumentEngine.Contracts.Geometry;

public readonly record struct SizeD
{
    public SizeD(double width, double height)
    {
        Width = GeometryGuard.RequireNonNegative(width, nameof(width));
        Height = GeometryGuard.RequireNonNegative(height, nameof(height));
    }

    public double Width { get; }

    public double Height { get; }

    public bool IsEmpty => Width == 0d || Height == 0d;
}

