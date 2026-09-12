using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Publishing;

namespace Inceptus.DocumentEngine.Canvas2D.Publishing;

public sealed record PublishedPoint(double X, double Y);

public sealed record PublishedRect(double X, double Y, double Width, double Height);

public sealed record PublishedMatrix(
    double M11,
    double M12,
    double M21,
    double M22,
    double OffsetX,
    double OffsetY);

public sealed record PublishedSource(string DocumentId, ulong Revision, string ScopeId);

public sealed record PublishedPublication(
    string Code,
    string Title,
    string Description);

public sealed record PublishedRuntimeConfiguration(
    int ActivityDelayMs,
    double TokenSpeedPxPerSecond,
    int MinimumConnectorDurationMs);

public sealed record PublishedPresentationNode(
    string Id,
    string Descriptor,
    PublishedRect Bounds,
    string? Label)
{
    private string _description = string.Empty;

    public string Description
    {
        get => _description;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _description = value;
        }
    }
}

public sealed record PublishedPresentationConnector(
    string Id,
    string SourceElementId,
    string TargetElementId,
    ImmutableArray<PublishedPoint> Points);

public sealed record PublishedPresentationItem(
    string Id,
    int Layer,
    int ZIndex,
    int GeometryKind,
    PublishedRect GeometryBounds,
    ImmutableArray<PublishedPoint> Points,
    string? Content,
    bool IsClosed,
    PublishedPoint TextAnchor,
    int TextAlignment,
    int TextBaseline,
    PublishedMatrix Transform,
    PublishedRect? Clip,
    string? Fill,
    string? Stroke,
    double StrokeWidth,
    ImmutableArray<double> DashPattern,
    double Opacity,
    string? FontFamily,
    double FontSize);

public sealed record PublishedPresentation(
    ImmutableArray<PublishedPresentationNode> Nodes,
    ImmutableArray<PublishedPresentationConnector> Connectors,
    ImmutableArray<PublishedPresentationItem> Items,
    PublishedRect ContentBounds);

public sealed record PublishedTokenNode(
    string Id,
    PublishedTokenRole Role,
    ImmutableArray<string> IncomingConnectorIds,
    ImmutableArray<string> OutgoingConnectorIds);

public sealed record PublishedTokenGraph(ImmutableArray<PublishedTokenNode> Nodes);

/// <summary>
/// Explicitly versioned, derived data consumed by the standalone static viewer.
/// </summary>
public sealed record PublishedProcessSnapshot(
    string Format,
    int FormatVersion,
    PublishedPublication Publication,
    PublishedSource Source,
    PublishedPresentation Presentation,
    PublishedTokenGraph TokenGraph,
    PublishedRuntimeConfiguration Runtime);
