using System.Collections.Immutable;
using System.Text;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Performs deterministic, Scene-construction-owned line breaking from approved text metrics.
/// </summary>
internal sealed class Canvas2DTextLayoutService
{
    private const double WidthTolerance = 1e-9;
    private readonly ITextMetricsService _metricsService;
    private readonly Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest>
        _requestFactory;
    private readonly Dictionary<TextMeasurementRequest, TextMeasurementResult> _measurements = [];
    private readonly List<Diagnostic> _diagnostics = [];

    internal Canvas2DTextLayoutService(
        ITextMetricsService metricsService,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory)
    {
        ArgumentNullException.ThrowIfNull(metricsService);
        ArgumentNullException.ThrowIfNull(requestFactory);
        _metricsService = metricsService;
        _requestFactory = requestFactory;
    }

    internal ImmutableArray<Diagnostic> Diagnostics => [.. _diagnostics];

    internal async ValueTask<Canvas2DTextLayout?> LayoutAsync(
        string text,
        Canvas2DSceneStyle style,
        double requestedLineHeight,
        double availableDocumentWidth,
        double documentHorizontalScale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        if (!double.IsFinite(requestedLineHeight) || requestedLineHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedLineHeight));
        }

        if (!double.IsFinite(availableDocumentWidth) || availableDocumentWidth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(availableDocumentWidth));
        }

        if (!double.IsFinite(documentHorizontalScale) || documentHorizontalScale < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(documentHorizontalScale));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var output = ImmutableArray.CreateBuilder<Canvas2DTextLayoutLine>();
        foreach (var explicitLine in normalized.Split('\n'))
        {
            if (!await WrapExplicitLineAsync(
                    explicitLine,
                    style,
                    requestedLineHeight,
                    availableDocumentWidth,
                    documentHorizontalScale,
                    output,
                    cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return new Canvas2DTextLayout(output.ToImmutable());
    }

    private async ValueTask<bool> WrapExplicitLineAsync(
        string explicitLine,
        Canvas2DSceneStyle style,
        double requestedLineHeight,
        double availableDocumentWidth,
        double documentHorizontalScale,
        ImmutableArray<Canvas2DTextLayoutLine>.Builder output,
        CancellationToken cancellationToken)
    {
        if (explicitLine.Length == 0)
        {
            return await AddMeasuredLineAsync(
                string.Empty,
                style,
                requestedLineHeight,
                output,
                cancellationToken).ConfigureAwait(false);
        }

        var tokens = Tokenize(explicitLine);
        var current = string.Empty;
        var pendingWhitespace = string.Empty;
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.IsWhitespace)
            {
                pendingWhitespace += token.Value;
                continue;
            }

            var candidate = current.Length == 0
                ? token.Value
                : current + pendingWhitespace + token.Value;
            if (await FitsAsync(
                    candidate,
                    style,
                    requestedLineHeight,
                    availableDocumentWidth,
                    documentHorizontalScale,
                    cancellationToken).ConfigureAwait(false) is not { } candidateFits)
            {
                return false;
            }

            if (candidateFits)
            {
                current = candidate;
                pendingWhitespace = string.Empty;
                continue;
            }

            if (current.Length > 0)
            {
                if (!await AddMeasuredLineAsync(
                        current,
                        style,
                        requestedLineHeight,
                        output,
                        cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                current = string.Empty;
                pendingWhitespace = string.Empty;
            }

            var pieces = await BreakLongTokenAsync(
                token.Value,
                style,
                requestedLineHeight,
                availableDocumentWidth,
                documentHorizontalScale,
                cancellationToken).ConfigureAwait(false);
            if (pieces is null)
            {
                return false;
            }

            for (var index = 0; index < pieces.Value.Length - 1; index++)
            {
                if (!await AddMeasuredLineAsync(
                        pieces.Value[index],
                        style,
                        requestedLineHeight,
                        output,
                        cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
            }

            current = pieces.Value[^1];
        }

        if (current.Length == 0 && pendingWhitespace.Length > 0)
        {
            current = pendingWhitespace;
        }
        else if (pendingWhitespace.Length > 0)
        {
            var trailing = current + pendingWhitespace;
            var fits = await FitsAsync(
                trailing,
                style,
                requestedLineHeight,
                availableDocumentWidth,
                documentHorizontalScale,
                cancellationToken).ConfigureAwait(false);
            if (fits is null)
            {
                return false;
            }

            if (fits.Value)
            {
                current = trailing;
            }
        }

        return await AddMeasuredLineAsync(
            current,
            style,
            requestedLineHeight,
            output,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ImmutableArray<string>?> BreakLongTokenAsync(
        string token,
        Canvas2DSceneStyle style,
        double requestedLineHeight,
        double availableDocumentWidth,
        double documentHorizontalScale,
        CancellationToken cancellationToken)
    {
        var wholeFits = await FitsAsync(
            token,
            style,
            requestedLineHeight,
            availableDocumentWidth,
            documentHorizontalScale,
            cancellationToken).ConfigureAwait(false);
        if (wholeFits is null)
        {
            return null;
        }

        if (wholeFits.Value)
        {
            return [token];
        }

        var pieces = ImmutableArray.CreateBuilder<string>();
        var current = new StringBuilder();
        foreach (var rune in token.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runeText = rune.ToString();
            var candidate = current.ToString() + runeText;
            var fits = await FitsAsync(
                candidate,
                style,
                requestedLineHeight,
                availableDocumentWidth,
                documentHorizontalScale,
                cancellationToken).ConfigureAwait(false);
            if (fits is null)
            {
                return null;
            }

            if (fits.Value || current.Length == 0)
            {
                current.Append(runeText);
                continue;
            }

            pieces.Add(current.ToString());
            current.Clear();
            current.Append(runeText);
        }

        pieces.Add(current.ToString());
        return pieces.ToImmutable();
    }

    private async ValueTask<bool?> FitsAsync(
        string text,
        Canvas2DSceneStyle style,
        double requestedLineHeight,
        double availableDocumentWidth,
        double documentHorizontalScale,
        CancellationToken cancellationToken)
    {
        var metrics = await MeasureAsync(
            text,
            style,
            requestedLineHeight,
            cancellationToken).ConfigureAwait(false);
        return metrics is null
            ? null
            : (metrics.Width * documentHorizontalScale) <=
                availableDocumentWidth + WidthTolerance;
    }

    private async ValueTask<bool> AddMeasuredLineAsync(
        string text,
        Canvas2DSceneStyle style,
        double requestedLineHeight,
        ImmutableArray<Canvas2DTextLayoutLine>.Builder output,
        CancellationToken cancellationToken)
    {
        var metrics = await MeasureAsync(
            text,
            style,
            requestedLineHeight,
            cancellationToken).ConfigureAwait(false);
        if (metrics is null)
        {
            return false;
        }

        output.Add(new Canvas2DTextLayoutLine(text, metrics));
        return true;
    }

    private async ValueTask<TextMetrics?> MeasureAsync(
        string text,
        Canvas2DSceneStyle style,
        double requestedLineHeight,
        CancellationToken cancellationToken)
    {
        var request = _requestFactory(text, style, requestedLineHeight);
        if (!_measurements.TryGetValue(request, out var result))
        {
            result = await _metricsService.MeasureAsync(request, cancellationToken)
                .ConfigureAwait(false);
            _measurements.Add(request, result);
            _diagnostics.AddRange(result.Diagnostics);
        }

        if (result.Status == TextMeasurementStatus.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _diagnostics.Add(new Diagnostic(
                Canvas2DSceneDiagnosticCodes.ConstructionFailure,
                DiagnosticSeverity.Error,
                "Canvas2D text measurement was cancelled before layout completed.",
                request.FontIdentity));
            return null;
        }

        return result.IsSuccessful ? result.Metrics : null;
    }

    private static IEnumerable<TextToken> Tokenize(string text)
    {
        var start = 0;
        var whitespace = char.IsWhiteSpace(text[0]);
        for (var index = 1; index < text.Length; index++)
        {
            var nextWhitespace = char.IsWhiteSpace(text[index]);
            if (nextWhitespace == whitespace)
            {
                continue;
            }

            yield return new TextToken(text[start..index], whitespace);
            start = index;
            whitespace = nextWhitespace;
        }

        yield return new TextToken(text[start..], whitespace);
    }

    private readonly record struct TextToken(string Value, bool IsWhitespace);
}

internal sealed class Canvas2DTextLayout
{
    internal Canvas2DTextLayout(ImmutableArray<Canvas2DTextLayoutLine> lines)
    {
        if (lines.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Text layout requires at least one line.", nameof(lines));
        }

        Lines = lines;
    }

    internal ImmutableArray<Canvas2DTextLayoutLine> Lines { get; }
}

internal sealed record Canvas2DTextLayoutLine(string Text, TextMetrics Metrics);
