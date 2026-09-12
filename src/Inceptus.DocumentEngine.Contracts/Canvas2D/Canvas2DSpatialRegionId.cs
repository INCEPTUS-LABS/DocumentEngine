namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Stable transient identity for one region in a single-scope spatial Scene presentation.
/// </summary>
public sealed record Canvas2DSpatialRegionId
{
    public Canvas2DSpatialRegionId(string value) =>
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "A non-empty Canvas2D spatial-region identity is required.",
                nameof(value))
            : value;

    public string Value { get; }

    public override string ToString() => Value;
}
