namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Stable identity of one notation-contributed persistent action for a semantic Scene target.
/// </summary>
public sealed record SemanticSceneCommandActionId
{
    public SemanticSceneCommandActionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
