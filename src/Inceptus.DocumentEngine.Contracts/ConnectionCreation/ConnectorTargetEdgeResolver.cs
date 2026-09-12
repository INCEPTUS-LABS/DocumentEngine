using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Resolves exactly one connector-anchor edge from a document-space point and node bounds.
/// Ties follow the canonical connector-anchor side order: Top, Right, Bottom, Left.
/// </summary>
public static class ConnectorTargetEdgeResolver
{
    public static ConnectorAnchorSide Resolve(RectD bounds, PointD documentPoint)
    {
        Validate(bounds, documentPoint);
        var distances = new[]
        {
            Math.Abs(documentPoint.Y - bounds.Top),
            Math.Abs(documentPoint.X - bounds.Right),
            Math.Abs(documentPoint.Y - bounds.Bottom),
            Math.Abs(documentPoint.X - bounds.Left),
        };
        var selected = 0;
        for (var index = 1; index < distances.Length; index++)
        {
            if (distances[index] < distances[selected])
            {
                selected = index;
            }
        }

        return (ConnectorAnchorSide)selected;
    }

    private static void Validate(RectD bounds, PointD point)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width < 0d || bounds.Height < 0d ||
            !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
    }
}
