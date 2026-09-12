using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Commands;

internal static class DiagnosticCollection
{
    public static ImmutableArray<Diagnostic> CopyAndOrder(
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
            throw new ArgumentException("Diagnostics cannot contain null values.", parameterName);
        }

        Array.Sort(copy, DiagnosticComparer.Instance);
        return [.. copy];
    }

    public static bool SequenceEquals(
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (DiagnosticComparer.Instance.Compare(left[index], right[index]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static void AddHashCode(ref HashCode hash, ImmutableArray<Diagnostic> diagnostics)
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

    private sealed class DiagnosticComparer : IComparer<Diagnostic>
    {
        public static DiagnosticComparer Instance { get; } = new();

        public int Compare(Diagnostic? left, Diagnostic? right)
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
            comparison = comparison != 0
                ? comparison
                : left.Severity.CompareTo(right.Severity);
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

            using var leftContext = left.Context.GetEnumerator();
            using var rightContext = right.Context.GetEnumerator();

            while (leftContext.MoveNext())
            {
                if (!rightContext.MoveNext())
                {
                    return 1;
                }

                comparison = StringComparer.Ordinal.Compare(
                    leftContext.Current.Key,
                    rightContext.Current.Key);
                comparison = comparison != 0
                    ? comparison
                    : StringComparer.Ordinal.Compare(
                        leftContext.Current.Value,
                        rightContext.Current.Value);

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return rightContext.MoveNext() ? -1 : 0;
        }
    }
}
