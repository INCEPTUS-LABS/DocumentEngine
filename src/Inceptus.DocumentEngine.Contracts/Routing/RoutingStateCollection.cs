using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Routing;

internal static class RoutingStateCollection
{
    internal static ImmutableArray<T> Copy<T>(IEnumerable<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToImmutableArray();
        if (copy.Any(static value => value is null))
            throw new ArgumentException("Saved state cannot contain null entries.", parameterName);
        return copy;
    }

    internal static ImmutableArray<T> Unique<T>(
        IEnumerable<T> values, Func<T, string> key, string parameterName, bool order = true)
        where T : class
    {
        var copy = Copy(values, parameterName);
        if (copy.Select(key).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Saved state identities must be unique.", parameterName);
        return order ? [.. copy.OrderBy(key, StringComparer.Ordinal)] : copy;
    }
}
