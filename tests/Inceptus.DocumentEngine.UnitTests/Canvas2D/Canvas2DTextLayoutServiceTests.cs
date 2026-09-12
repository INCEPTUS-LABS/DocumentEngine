using System.Text;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DTextLayoutServiceTests
{
    public static IEnumerable<object[]> WrappingCases =>
    [
        ["Alpha", 100d, new[] { "Alpha" }],
        ["Alpha Beta", 50d, new[] { "Alpha Beta" }],
        ["Alpha Beta", 49d, new[] { "Alpha", "Beta" }],
        ["one two three four", 45d, new[] { "one two", "three", "four" }],
        ["Alpha\nBeta", 100d, new[] { "Alpha", "Beta" }],
        ["Alpha\r\nBeta", 100d, new[] { "Alpha", "Beta" }],
        ["ABCDEFGHIJ", 20d, new[] { "ABCD", "EFGH", "IJ" }],
        ["Alpha   Beta", 100d, new[] { "Alpha   Beta" }],
        ["", 100d, new[] { "" }],
        ["   ", 100d, new[] { "   " }],
    ];

    [Theory]
    [MemberData(nameof(WrappingCases))]
    public async Task WrapsDeterministicallyFromApprovedMetrics(
        string text,
        double width,
        string[] expected)
    {
        var metrics = new FixedAdvanceTextMetricsService();
        var service = new Canvas2DTextLayoutService(metrics, CreateRequest);

        var result = await service.LayoutAsync(
            text,
            Canvas2DSceneStyle.Default,
            14.4d,
            width,
            1d,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Lines.Select(static line => line.Text));
        Assert.Empty(service.Diagnostics);
    }

    [Fact]
    public async Task OneLogicalUnitPastExactBoundaryWrapsButExactBoundaryDoesNot()
    {
        var metrics = new FixedAdvanceTextMetricsService();
        var exactService = new Canvas2DTextLayoutService(metrics, CreateRequest);
        var overService = new Canvas2DTextLayoutService(metrics, CreateRequest);

        var exact = await exactService.LayoutAsync(
            "Alpha Beta",
            Canvas2DSceneStyle.Default,
            14.4d,
            50d,
            1d,
            CancellationToken.None);
        var over = await overService.LayoutAsync(
            "Alpha Beta",
            Canvas2DSceneStyle.Default,
            14.4d,
            49d,
            1d,
            CancellationToken.None);

        Assert.Single(exact!.Lines);
        Assert.Equal(2, over!.Lines.Length);
    }

    [Fact]
    public async Task MeasurementRequestsAreCachedByCompleteImmutableRequest()
    {
        var metrics = new FixedAdvanceTextMetricsService();
        var service = new Canvas2DTextLayoutService(metrics, CreateRequest);

        var result = await service.LayoutAsync(
            "same same same",
            Canvas2DSceneStyle.Default,
            14.4d,
            30d,
            1d,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(metrics.Requests.Count, metrics.Requests.Distinct().Count());
    }

    [Fact]
    public async Task MeasurementFailureProducesNoPartialLayout()
    {
        var metrics = new FailingTextMetricsService();
        var service = new Canvas2DTextLayoutService(metrics, CreateRequest);

        var result = await service.LayoutAsync(
            "Alpha Beta",
            Canvas2DSceneStyle.Default,
            14.4d,
            40d,
            1d,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Contains(service.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TextMeasurementRequest CreateRequest(
        string text,
        Canvas2DSceneStyle style,
        double lineHeight) =>
        new(
            text,
            "Test Sans",
            "test:sans",
            "1",
            style.FontSize,
            lineHeight,
            400,
            TextFontStyle.Normal,
            "und",
            TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom,
            1d,
            "test.metrics",
            "1");

    internal sealed class FixedAdvanceTextMetricsService : ITextMetricsService
    {
        internal List<TextMeasurementRequest> Requests { get; } = [];

        public ValueTask<TextMeasurementResult> MeasureAsync(
            TextMeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var width = request.Text.EnumerateRunes().Count() * 5d;
            return ValueTask.FromResult(TextMeasurementResult.Success(new TextMetrics(
                width,
                9d,
                3d,
                request.LineHeight,
                new RectD(0d, -9d, width, 12d),
                $"{request.FontIdentity}@{request.FontVersion}")));
        }
    }

    private sealed class FailingTextMetricsService : ITextMetricsService
    {
        public ValueTask<TextMeasurementResult> MeasureAsync(
            TextMeasurementRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TextMeasurementResult.Failure(
            [
                new Diagnostic(
                    TextMetricsDiagnosticCodes.MeasurementFailure,
                    DiagnosticSeverity.Error,
                    "Test measurement failure.",
                    request.FontIdentity),
            ]));
    }
}
