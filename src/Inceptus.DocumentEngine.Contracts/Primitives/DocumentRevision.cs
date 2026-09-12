using System.Globalization;

namespace Inceptus.DocumentEngine.Contracts.Primitives;

public readonly record struct DocumentRevision(ulong Value) : IComparable<DocumentRevision>
{
    public static DocumentRevision Zero => default;

    public DocumentRevision Increment() => new(checked(Value + 1UL));

    public int CompareTo(DocumentRevision other) => Value.CompareTo(other.Value);

    public static bool operator <(DocumentRevision left, DocumentRevision right) =>
        left.Value < right.Value;

    public static bool operator <=(DocumentRevision left, DocumentRevision right) =>
        left.Value <= right.Value;

    public static bool operator >(DocumentRevision left, DocumentRevision right) =>
        left.Value > right.Value;

    public static bool operator >=(DocumentRevision left, DocumentRevision right) =>
        left.Value >= right.Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
