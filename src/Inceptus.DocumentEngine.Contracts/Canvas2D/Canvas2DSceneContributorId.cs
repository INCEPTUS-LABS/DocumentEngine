namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Identifies a deterministic Canvas2D scene contribution independently of CLR type names.
/// </summary>
public sealed record Canvas2DSceneContributorId
{
    public Canvas2DSceneContributorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
