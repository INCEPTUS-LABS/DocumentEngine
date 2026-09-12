namespace Inceptus.DocumentEngine.Contracts.Text;

/// <summary>
/// Provides technology-independent text measurements in logical document-coordinate units.
/// </summary>
public interface ITextMetricsService
{
    ValueTask<TextMeasurementResult> MeasureAsync(
        TextMeasurementRequest request,
        CancellationToken cancellationToken = default);
}
