namespace Inceptus.DocumentEngine.Contracts.Text;

/// <summary>
/// Stable diagnostics reported by the technology-independent text-measurement boundary.
/// </summary>
public static class TextMetricsDiagnosticCodes
{
    public const string InvalidRequest = "TEXT_METRICS_INVALID_REQUEST";
    public const string UnavailableFont = "TEXT_METRICS_UNAVAILABLE_FONT";
    public const string FontSubstituted = "TEXT_METRICS_FONT_SUBSTITUTED";
    public const string UnsupportedConfiguration = "TEXT_METRICS_UNSUPPORTED_CONFIGURATION";
    public const string MeasurementFailure = "TEXT_METRICS_MEASUREMENT_FAILURE";
    public const string Cancelled = "TEXT_METRICS_CANCELLED";
}
