using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable, technology-independent geometry for one Canvas2D scene item.
/// </summary>
public sealed class Canvas2DSceneGeometry : IEquatable<Canvas2DSceneGeometry>
{
    private Canvas2DSceneGeometry(
        Canvas2DSceneGeometryKind kind,
        RectD bounds,
        ImmutableArray<PointD> points,
        string? content,
        bool isClosed,
        PointD textAnchor = default,
        Canvas2DTextAlignment textAlignment = Canvas2DTextAlignment.Start,
        Canvas2DTextBaseline textBaseline = Canvas2DTextBaseline.Top)
    {
        Kind = kind;
        Bounds = bounds;
        Points = points;
        Content = content;
        IsClosed = isClosed;
        TextAnchor = textAnchor;
        TextAlignment = textAlignment;
        TextBaseline = textBaseline;
    }

    public Canvas2DSceneGeometryKind Kind { get; }

    public RectD Bounds { get; }

    public ImmutableArray<PointD> Points { get; }

    public string? Content { get; }

    public bool IsClosed { get; }

    /// <summary>
    /// Gets the local-coordinate anchor used to draw text geometry.
    /// </summary>
    public PointD TextAnchor { get; }

    public Canvas2DTextAlignment TextAlignment { get; }

    public Canvas2DTextBaseline TextBaseline { get; }

    public static Canvas2DSceneGeometry Rectangle(RectD bounds) =>
        new(Canvas2DSceneGeometryKind.Rectangle, bounds, [], null, true);

    public static Canvas2DSceneGeometry Ellipse(RectD bounds) =>
        new(Canvas2DSceneGeometryKind.Ellipse, bounds, [], null, true);

    public static Canvas2DSceneGeometry Path(
        IEnumerable<PointD> points,
        bool isClosed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        var copy = points.ToImmutableArray();
        var minimumCount = isClosed ? 3 : 2;
        if (copy.Length < minimumCount)
        {
            throw new ArgumentException(
                $"A {(isClosed ? "closed" : "open")} path requires at least {minimumCount} points.",
                nameof(points));
        }

        var minimumX = copy[0].X;
        var minimumY = copy[0].Y;
        var maximumX = copy[0].X;
        var maximumY = copy[0].Y;
        foreach (var point in copy.AsSpan()[1..])
        {
            minimumX = Math.Min(minimumX, point.X);
            minimumY = Math.Min(minimumY, point.Y);
            maximumX = Math.Max(maximumX, point.X);
            maximumY = Math.Max(maximumY, point.Y);
        }

        return new Canvas2DSceneGeometry(
            Canvas2DSceneGeometryKind.Path,
            new RectD(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY),
            copy,
            null,
            isClosed);
    }

    public static Canvas2DSceneGeometry Text(RectD bounds, string content) =>
        Text(
            bounds,
            content,
            bounds.TopLeft,
            Canvas2DTextAlignment.Start,
            Canvas2DTextBaseline.Top);

    public static Canvas2DSceneGeometry Text(
        RectD bounds,
        string content,
        PointD textAnchor,
        Canvas2DTextAlignment textAlignment,
        Canvas2DTextBaseline textBaseline)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(textAlignment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(textAlignment), textAlignment, "The text alignment must be defined.");
        }

        if (!Enum.IsDefined(textBaseline))
        {
            throw new ArgumentOutOfRangeException(
                nameof(textBaseline), textBaseline, "The text baseline must be defined.");
        }

        return new(
            Canvas2DSceneGeometryKind.Text,
            bounds,
            [],
            content,
            false,
            textAnchor,
            textAlignment,
            textBaseline);
    }

    public static Canvas2DSceneGeometry Image(RectD bounds, string imageReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReference);
        return new(Canvas2DSceneGeometryKind.Image, bounds, [], imageReference, false);
    }

    public bool Equals(Canvas2DSceneGeometry? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Kind == other.Kind &&
        Bounds == other.Bounds &&
        Points.AsSpan().SequenceEqual(other.Points.AsSpan()) &&
        StringComparer.Ordinal.Equals(Content, other.Content) &&
        IsClosed == other.IsClosed &&
        TextAnchor == other.TextAnchor &&
        TextAlignment == other.TextAlignment &&
        TextBaseline == other.TextBaseline;

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneGeometry);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Bounds);
        foreach (var point in Points)
        {
            hash.Add(point);
        }

        hash.Add(Content, StringComparer.Ordinal);
        hash.Add(IsClosed);
        hash.Add(TextAnchor);
        hash.Add(TextAlignment);
        hash.Add(TextBaseline);
        return hash.ToHashCode();
    }
}
