using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Layout;

internal static class LayoutDiagnosticCollection
{
    internal static ImmutableArray<Diagnostic> CopyAndOrder(
        IEnumerable<Diagnostic>? diagnostics,
        string parameterName)
    {
        if (diagnostics is null)
        {
            return [];
        }

        var copy = diagnostics.ToArray();
        if (Array.Exists(copy, static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Layout diagnostics cannot contain null values.",
                parameterName);
        }

        Array.Sort(copy, Compare);
        return [.. copy];
    }

    internal static bool SequenceEquals(
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right) =>
        left.Length == right.Length && left.AsSpan().SequenceEqual(right.AsSpan(), Comparer);

    internal static void AddHashCode(ref HashCode hash, ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            hash.Add(diagnostic.Code, StringComparer.Ordinal);
            hash.Add(diagnostic.Severity);
            hash.Add(diagnostic.Message, StringComparer.Ordinal);
            hash.Add(diagnostic.SourceIdentity, StringComparer.Ordinal);
            foreach (var entry in diagnostic.Context)
            {
                hash.Add(entry.Key, StringComparer.Ordinal);
                hash.Add(entry.Value, StringComparer.Ordinal);
            }
        }
    }

    private static IEqualityComparer<Diagnostic> Comparer { get; } =
        new DiagnosticValueComparer();

    private static int Compare(Diagnostic? left, Diagnostic? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
        comparison = comparison != 0 ? comparison : left.Severity.CompareTo(right.Severity);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.Message, right.Message);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.SourceIdentity, right.SourceIdentity);
        if (comparison != 0)
        {
            return comparison;
        }

        return CompareContext(left.Context, right.Context);
    }

    private static int CompareContext(
        IEnumerable<KeyValuePair<string, string>> left,
        IEnumerable<KeyValuePair<string, string>> right)
    {
        using var leftEnumerator = left.GetEnumerator();
        using var rightEnumerator = right.GetEnumerator();
        while (leftEnumerator.MoveNext())
        {
            if (!rightEnumerator.MoveNext())
            {
                return 1;
            }

            var comparison = StringComparer.Ordinal.Compare(
                leftEnumerator.Current.Key,
                rightEnumerator.Current.Key);
            comparison = comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(
                    leftEnumerator.Current.Value,
                    rightEnumerator.Current.Value);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return rightEnumerator.MoveNext() ? -1 : 0;
    }

    private sealed class DiagnosticValueComparer : IEqualityComparer<Diagnostic>
    {
        public bool Equals(Diagnostic? x, Diagnostic? y) => Compare(x, y) == 0;

        public int GetHashCode(Diagnostic obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Code, StringComparer.Ordinal);
            hash.Add(obj.Severity);
            hash.Add(obj.Message, StringComparer.Ordinal);
            hash.Add(obj.SourceIdentity, StringComparer.Ordinal);
            foreach (var entry in obj.Context)
            {
                hash.Add(entry.Key, StringComparer.Ordinal);
                hash.Add(entry.Value, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
