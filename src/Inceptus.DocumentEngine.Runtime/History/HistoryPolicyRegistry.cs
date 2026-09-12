using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class HistoryPolicyRegistry
{
    private readonly ImmutableArray<CommandHistoryPolicyRegistration> _registrations;

    internal HistoryPolicyRegistry(IEnumerable<CommandHistoryPolicyRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var copy = registrations.ToArray();
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "History policy registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.TypeId.Value, right.TypeId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].TypeId == copy[index].TypeId)
            {
                throw new ArgumentException(
                    $"{HistoryDiagnosticCodes.DuplicatePolicyRegistration}: " +
                    $"Command type '{copy[index].TypeId}' has more than one History policy.",
                    nameof(registrations));
            }
        }

        _registrations = [.. copy];
    }

    internal bool TryGet(CommandTypeId typeId, out ICommandHistoryPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        var lower = 0;
        var upper = _registrations.Length - 1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var registration = _registrations[middle];
            var comparison = StringComparer.Ordinal.Compare(
                registration.TypeId.Value,
                typeId.Value);
            if (comparison == 0)
            {
                policy = registration.Policy;
                return true;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        policy = null;
        return false;
    }
}
