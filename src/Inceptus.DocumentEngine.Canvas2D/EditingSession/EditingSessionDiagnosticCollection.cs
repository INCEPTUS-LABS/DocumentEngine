using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal static class EditingSessionDiagnosticCollection
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

    private static int Compare(Diagnostic left, Diagnostic right)
    {
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
}
