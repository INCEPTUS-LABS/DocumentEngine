namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Stable identity of one notation-contributed transient action for a semantic Scene target.
/// </summary>
public sealed record SemanticSceneViewActionId
{
    public SemanticSceneViewActionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
