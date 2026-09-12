using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

internal static class Canvas2DRendererDiagnosticCollection
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
            throw new ArgumentException("Diagnostics cannot contain null values.", parameterName);
        }

        Array.Sort(copy, Compare);
        return [.. copy];
    }

    internal static bool SequenceEquals(
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    internal static void AddHashCode(
        ref HashCode hash,
        ImmutableArray<Diagnostic> diagnostics)
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

    private static int Compare(Diagnostic left, Diagnostic right)
    {
        var comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
        comparison = comparison != 0
            ? comparison
            : left.Severity.CompareTo(right.Severity);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.SourceIdentity, right.SourceIdentity);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.Message, right.Message);
        if (comparison != 0)
        {
            return comparison;
        }

        using var leftEnumerator = left.Context.GetEnumerator();
        using var rightEnumerator = right.Context.GetEnumerator();
        while (leftEnumerator.MoveNext())
        {
            if (!rightEnumerator.MoveNext())
            {
                return 1;
            }

            comparison = StringComparer.Ordinal.Compare(
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

    private static bool Equals(Diagnostic left, Diagnostic right) =>
        StringComparer.Ordinal.Equals(left.Code, right.Code) &&
        left.Severity == right.Severity &&
        StringComparer.Ordinal.Equals(left.Message, right.Message) &&
        StringComparer.Ordinal.Equals(left.SourceIdentity, right.SourceIdentity) &&
        left.Context.Count == right.Context.Count &&
        left.Context.SequenceEqual(right.Context);
}
