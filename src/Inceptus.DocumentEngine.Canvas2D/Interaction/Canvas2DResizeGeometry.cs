using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DResizeGeometry
{
    internal static ImmutableArray<Canvas2DResizeDirection> Directions { get; } =
    [
        Canvas2DResizeDirection.NorthWest,
        Canvas2DResizeDirection.North,
        Canvas2DResizeDirection.NorthEast,
        Canvas2DResizeDirection.East,
        Canvas2DResizeDirection.SouthEast,
        Canvas2DResizeDirection.South,
        Canvas2DResizeDirection.SouthWest,
        Canvas2DResizeDirection.West,
    ];

    internal static string Role(Canvas2DResizeDirection direction) => direction switch
    {
        Canvas2DResizeDirection.NorthWest => Canvas2DResizeGestureMetadata.NorthWestRole,
        Canvas2DResizeDirection.North => Canvas2DResizeGestureMetadata.NorthRole,
        Canvas2DResizeDirection.NorthEast => Canvas2DResizeGestureMetadata.NorthEastRole,
        Canvas2DResizeDirection.East => Canvas2DResizeGestureMetadata.EastRole,
        Canvas2DResizeDirection.SouthEast => Canvas2DResizeGestureMetadata.SouthEastRole,
        Canvas2DResizeDirection.South => Canvas2DResizeGestureMetadata.SouthRole,
        Canvas2DResizeDirection.SouthWest => Canvas2DResizeGestureMetadata.SouthWestRole,
        Canvas2DResizeDirection.West => Canvas2DResizeGestureMetadata.WestRole,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The resize direction must be defined."),
    };

    internal static bool TryParseRole(string role, out Canvas2DResizeDirection direction)
    {
        direction = role switch
        {
            Canvas2DResizeGestureMetadata.NorthWestRole => Canvas2DResizeDirection.NorthWest,
            Canvas2DResizeGestureMetadata.NorthRole => Canvas2DResizeDirection.North,
            Canvas2DResizeGestureMetadata.NorthEastRole => Canvas2DResizeDirection.NorthEast,
            Canvas2DResizeGestureMetadata.EastRole => Canvas2DResizeDirection.East,
            Canvas2DResizeGestureMetadata.SouthEastRole => Canvas2DResizeDirection.SouthEast,
            Canvas2DResizeGestureMetadata.SouthRole => Canvas2DResizeDirection.South,
            Canvas2DResizeGestureMetadata.SouthWestRole => Canvas2DResizeDirection.SouthWest,
            Canvas2DResizeGestureMetadata.WestRole => Canvas2DResizeDirection.West,
            _ => default,
        };
        return role is
            Canvas2DResizeGestureMetadata.NorthWestRole or
            Canvas2DResizeGestureMetadata.NorthRole or
            Canvas2DResizeGestureMetadata.NorthEastRole or
            Canvas2DResizeGestureMetadata.EastRole or
            Canvas2DResizeGestureMetadata.SouthEastRole or
            Canvas2DResizeGestureMetadata.SouthRole or
            Canvas2DResizeGestureMetadata.SouthWestRole or
            Canvas2DResizeGestureMetadata.WestRole;
    }

    internal static string CssCursor(Canvas2DResizeDirection direction) => direction switch
    {
        Canvas2DResizeDirection.North or Canvas2DResizeDirection.South => "ns-resize",
        Canvas2DResizeDirection.East or Canvas2DResizeDirection.West => "ew-resize",
        Canvas2DResizeDirection.NorthEast or Canvas2DResizeDirection.SouthWest => "nesw-resize",
        Canvas2DResizeDirection.NorthWest or Canvas2DResizeDirection.SouthEast => "nwse-resize",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The resize direction must be defined."),
    };

    internal static int ZIndex(Canvas2DResizeDirection direction) => direction switch
    {
        Canvas2DResizeDirection.North => 5000,
        Canvas2DResizeDirection.East => 5001,
        Canvas2DResizeDirection.South => 5002,
        Canvas2DResizeDirection.West => 5003,
        Canvas2DResizeDirection.NorthWest => 5010,
        Canvas2DResizeDirection.NorthEast => 5011,
        Canvas2DResizeDirection.SouthWest => 5012,
        Canvas2DResizeDirection.SouthEast => 5013,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The resize direction must be defined."),
    };

    internal static RectD CalculateBounds(
        RectD original,
        VectorD delta,
        Canvas2DResizeDirection direction) =>
        CalculateBounds(
            original,
            delta,
            direction,
            Canvas2DResizeGestureMetadata.MinimumExtent,
            Canvas2DResizeGestureMetadata.MinimumExtent);

    internal static RectD CalculateBounds(
        RectD original,
        VectorD delta,
        Canvas2DResizeDirection direction,
        double minimumWidth,
        double minimumHeight)
    {
        if (!double.IsFinite(minimumWidth) || minimumWidth <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumWidth),
                minimumWidth,
                "The minimum width must be finite and positive.");
        }

        if (!double.IsFinite(minimumHeight) || minimumHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumHeight),
                minimumHeight,
                "The minimum height must be finite and positive.");
        }

        var left = original.Left;
        var top = original.Top;
        var right = original.Right;
        var bottom = original.Bottom;
        if (direction is Canvas2DResizeDirection.NorthWest or
            Canvas2DResizeDirection.SouthWest or
            Canvas2DResizeDirection.West)
        {
            left = Math.Min(left + delta.X, right - minimumWidth);
        }
        else if (direction is Canvas2DResizeDirection.NorthEast or
                 Canvas2DResizeDirection.East or
                 Canvas2DResizeDirection.SouthEast)
        {
            right = Math.Max(right + delta.X, left + minimumWidth);
        }

        if (direction is Canvas2DResizeDirection.NorthWest or
            Canvas2DResizeDirection.North or
            Canvas2DResizeDirection.NorthEast)
        {
            top = Math.Min(top + delta.Y, bottom - minimumHeight);
        }
        else if (direction is Canvas2DResizeDirection.SouthEast or
                 Canvas2DResizeDirection.South or
                 Canvas2DResizeDirection.SouthWest)
        {
            bottom = Math.Max(bottom + delta.Y, top + minimumHeight);
        }

        return new RectD(left, top, right - left, bottom - top);
    }

    internal static bool IsCorner(Canvas2DResizeDirection direction) => direction is
        Canvas2DResizeDirection.NorthWest or
        Canvas2DResizeDirection.NorthEast or
        Canvas2DResizeDirection.SouthEast or
        Canvas2DResizeDirection.SouthWest;

    internal static RectD InteractionBounds(RectD bounds, Canvas2DResizeDirection direction)
    {
        var availableExtent = Math.Min(bounds.Width, bounds.Height) / 2d;
        var edgeExtent = Math.Min(
            Canvas2DResizeGestureMetadata.EdgeHitExtent,
            availableExtent);
        var halfEdgeExtent = edgeExtent / 2d;
        if (!IsCorner(direction))
        {
            return direction switch
            {
                Canvas2DResizeDirection.North => new RectD(
                    bounds.Left,
                    bounds.Top - halfEdgeExtent,
                    bounds.Width,
                    edgeExtent),
                Canvas2DResizeDirection.East => new RectD(
                    bounds.Right - halfEdgeExtent,
                    bounds.Top,
                    edgeExtent,
                    bounds.Height),
                Canvas2DResizeDirection.South => new RectD(
                    bounds.Left,
                    bounds.Bottom - halfEdgeExtent,
                    bounds.Width,
                    edgeExtent),
                Canvas2DResizeDirection.West => new RectD(
                    bounds.Left - halfEdgeExtent,
                    bounds.Top,
                    edgeExtent,
                    bounds.Height),
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The resize direction must be defined."),
            };
        }

        var center = direction switch
        {
            Canvas2DResizeDirection.NorthWest => new PointD(bounds.Left, bounds.Top),
            Canvas2DResizeDirection.NorthEast => new PointD(bounds.Right, bounds.Top),
            Canvas2DResizeDirection.SouthEast => new PointD(bounds.Right, bounds.Bottom),
            Canvas2DResizeDirection.SouthWest => new PointD(bounds.Left, bounds.Bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The resize direction must be defined."),
        };
        var handleExtent = Math.Min(
            Canvas2DResizeGestureMetadata.HandleExtent,
            availableExtent);
        var halfExtent = handleExtent / 2d;
        return new RectD(
            center.X - halfExtent,
            center.Y - halfExtent,
            handleExtent,
            handleExtent);
    }

    internal static Matrix2D CreateAdjustment(RectD original, RectD resized) =>
        Matrix2D.CreateTranslation(-original.X, -original.Y)
            .Then(Matrix2D.CreateScale(
                resized.Width / original.Width,
                resized.Height / original.Height))
            .Then(Matrix2D.CreateTranslation(resized.X, resized.Y));

    internal static RectD ScaleBounds(RectD bounds, RectD original, RectD resized)
    {
        var scaleX = resized.Width / original.Width;
        var scaleY = resized.Height / original.Height;
        return new RectD(
            resized.X + ((bounds.X - original.X) * scaleX),
            resized.Y + ((bounds.Y - original.Y) * scaleY),
            bounds.Width * scaleX,
            bounds.Height * scaleY);
    }
}
