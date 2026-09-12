namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Stable identity of one notation-contributed empty-Canvas model action.
/// </summary>
public sealed record CanvasBackgroundActionId
{
    public CanvasBackgroundActionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
