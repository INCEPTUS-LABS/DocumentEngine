using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class TextMetricsContractTests
{
    [Fact]
    public void MeasurementRequestIsImmutableStructuralAndPreservesEveryDeterminant()
    {
        var first = CreateRequest();
        var second = CreateRequest();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("Neutral text", first.Text);
        Assert.Equal("Inter", first.FontFamily);
        Assert.Equal("test:font:inter", first.FontIdentity);
        Assert.Equal("4.0", first.FontVersion);
        Assert.Equal(14d, first.FontSize);
        Assert.Equal(18d, first.LineHeight);
        Assert.Equal(600, first.FontWeight);
        Assert.Equal(TextFontStyle.Italic, first.FontStyle);
        Assert.Equal("en-US", first.Locale);
        Assert.Equal(TextDirection.LeftToRight, first.Direction);
        Assert.Equal(TextWritingMode.HorizontalTopToBottom, first.WritingMode);
        Assert.Equal(1.25d, first.Scale);
        Assert.Equal("test:metrics", first.ConfigurationId);
        Assert.Equal("1", first.ConfigurationVersion);
        Assert.All(first.GetType().GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void RequestRejectsInvalidPositiveFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(fontSize: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(lineHeight: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(scale: value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void RequestRejectsInvalidFontWeight(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRequest(fontWeight: value));

    [Fact]
    public void MetricsExposeStableImmutableGeometryAndCanonicalDiagnostics()
    {
        var warnings = new[]
        {
            new Diagnostic("test:z", DiagnosticSeverity.Warning, "z"),
            new Diagnostic("test:a", DiagnosticSeverity.Information, "a"),
        };
        var metrics = new TextMetrics(
            80d,
            10d,
            3d,
            18d,
            new RectD(-1d, -10d, 82d, 14d),
            "Inter/600/italic",
            warnings);
        warnings[0] = new Diagnostic("test:changed", DiagnosticSeverity.Error, "changed");

        Assert.Equal(80d, metrics.Width);
        Assert.Equal(10d, metrics.Ascent);
        Assert.Equal(3d, metrics.Descent);
        Assert.Equal(18d, metrics.LineHeight);
        Assert.Equal(82d, metrics.BoundingWidth);
        Assert.Equal(14d, metrics.BoundingHeight);
        Assert.Equal("Inter/600/italic", metrics.ResolvedFontIdentity);
        Assert.Equal(["test:a", "test:z"], metrics.Diagnostics.Select(static item => item.Code));
        Assert.IsType<ImmutableArray<Diagnostic>>(metrics.Diagnostics);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)metrics.Diagnostics).Add(metrics.Diagnostics[0]));
        Assert.All(metrics.GetType().GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void MetricsRejectInvalidNonNegativeFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMetrics(width: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMetrics(ascent: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMetrics(descent: value));
    }

    [Fact]
    public void ResultNeverLeaksPartialMetricsOnFailureOrCancellation()
    {
        var metrics = CreateMetrics();
        var success = TextMeasurementResult.Success(metrics);
        var failure = TextMeasurementResult.Failure(
        [
            new Diagnostic(
                TextMetricsDiagnosticCodes.MeasurementFailure,
                DiagnosticSeverity.Error,
                "Measurement failed."),
        ]);
        var cancelled = TextMeasurementResult.Cancelled(
        [
            new Diagnostic(
                TextMetricsDiagnosticCodes.Cancelled,
                DiagnosticSeverity.Information,
                "Measurement cancelled."),
        ]);

        Assert.True(success.IsSuccessful);
        Assert.Same(metrics, success.Metrics);
        Assert.Equal(TextMeasurementStatus.Failed, failure.Status);
        Assert.Null(failure.Metrics);
        Assert.Equal(TextMeasurementStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Metrics);
    }

    [Fact]
    public void MetricsRejectErrorDiagnosticsAndFailureRequiresAnError()
    {
        var error = new Diagnostic(
            TextMetricsDiagnosticCodes.MeasurementFailure,
            DiagnosticSeverity.Error,
            "Measurement failed.");

        Assert.Throws<ArgumentException>(() => CreateMetrics([error]));
        Assert.Throws<ArgumentException>(() => TextMeasurementResult.Failure(
        [
            new Diagnostic("test:warning", DiagnosticSeverity.Warning, "Warning."),
        ]));
    }

    [Fact]
    public void ServiceContractContainsNoBrowserNativeMeasurementType()
    {
        var measure = Assert.Single(typeof(ITextMetricsService).GetMethods());
        var parameters = measure.GetParameters();

        Assert.Equal(typeof(ValueTask<TextMeasurementResult>), measure.ReturnType);
        Assert.Equal(typeof(TextMeasurementRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.DoesNotContain(
            typeof(ITextMetricsService).Assembly.GetExportedTypes(),
            type => type.FullName == "System.Drawing.Text.TextRenderingHint" ||
                type.Name == "TextMetrics" && type.Namespace?.Contains("Browser") == true);
    }

    private static TextMeasurementRequest CreateRequest(
        double fontSize = 14d,
        double lineHeight = 18d,
        int fontWeight = 600,
        double scale = 1.25d) =>
        new(
            "Neutral text",
            "Inter",
            "test:font:inter",
            "4.0",
            fontSize,
            lineHeight,
            fontWeight,
            TextFontStyle.Italic,
            "en-US",
            TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom,
            scale,
            "test:metrics",
            "1");

    private static TextMetrics CreateMetrics(
        IEnumerable<Diagnostic>? diagnostics = null,
        double width = 80d,
        double ascent = 10d,
        double descent = 3d) =>
        new(width, ascent, descent, 18d, new RectD(0d, -10d, 80d, 14d), "Inter", diagnostics);
}
