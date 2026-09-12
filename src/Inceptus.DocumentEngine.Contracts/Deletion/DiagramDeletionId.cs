using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Identifies one notation-owned diagram deletion capability.
/// </summary>
public sealed record DiagramDeletionId
{
    public DiagramDeletionId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}
