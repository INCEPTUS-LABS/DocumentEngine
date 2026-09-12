using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Resolves persistent connector-anchor order into deterministic document-space
/// geometry. Source and Target roles deliberately share the same distribution.
/// </summary>
public static class ConnectorAnchorGeometryResolver
{
    public static ImmutableArray<double> Distribute(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        var denominator = count + 1d;
        var positions = ImmutableArray.CreateBuilder<double>(count);
        for (var index = 0; index < count; index++)
        {
            positions.Add((index + 1d) / denominator);
        }

        return positions.MoveToImmutable();
    }

    public static double ResolveNormalizedPosition(int order, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "The side must contain at least one connector anchor.");
        }

        if (order < 0 || order >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order),
                order,
                "The connector-anchor order must identify an anchor on the side.");
        }

        return (order + 1d) / (count + 1d);
    }

    public static PointD ResolvePoint(
        RectD bounds,
        ConnectorAnchorSide side,
        int order,
        int count)
    {
        ValidateSide(side);
        var position = ResolveNormalizedPosition(order, count);
        return side switch
        {
            ConnectorAnchorSide.Top =>
                new PointD(bounds.Left + (bounds.Width * position), bounds.Top),
            ConnectorAnchorSide.Right =>
                new PointD(bounds.Right, bounds.Top + (bounds.Height * position)),
            ConnectorAnchorSide.Bottom =>
                new PointD(bounds.Left + (bounds.Width * position), bounds.Bottom),
            ConnectorAnchorSide.Left =>
                new PointD(bounds.Left, bounds.Top + (bounds.Height * position)),
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
    }

    public static double ResolveEdgeParameter(
        RectD bounds,
        ConnectorAnchorSide side,
        PointD point)
    {
        ValidateSide(side);
        var offset = side is ConnectorAnchorSide.Top or ConnectorAnchorSide.Bottom
            ? point.X - bounds.Left
            : point.Y - bounds.Top;
        var extent = side is ConnectorAnchorSide.Top or ConnectorAnchorSide.Bottom
            ? bounds.Width
            : bounds.Height;
        return extent == 0d
            ? 0.5d
            : Math.Clamp(offset / extent, 0d, 1d);
    }

    public static int ResolveInsertionIndex(
        IEnumerable<ConnectorAnchor> anchors,
        ConnectorAnchorSide side,
        double edgeParameter)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ValidateSide(side);
        if (!double.IsFinite(edgeParameter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeParameter),
                edgeParameter,
                "The edge parameter must be finite.");
        }

        var ordered = anchors
            .Select(anchor => anchor ?? throw new ArgumentException(
                "Connector anchors cannot contain null values.",
                nameof(anchors)))
            .Where(anchor => anchor.Side == side)
            .OrderBy(static anchor => anchor.Order)
            .ToArray();
        var parameter = Math.Clamp(edgeParameter, 0d, 1d);
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ResolveNormalizedPosition(ordered[index].Order, ordered.Length) > parameter)
            {
                return index;
            }
        }

        return ordered.Length;
    }

    private static void ValidateSide(ConnectorAnchorSide side)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(side),
                side,
                "The connector-anchor side must be defined.");
        }
    }
}
