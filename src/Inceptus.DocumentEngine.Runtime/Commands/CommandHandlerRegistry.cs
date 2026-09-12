using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class CommandHandlerRegistry
{
    private readonly ImmutableArray<CommandHandlerRegistration> _registrations;

    internal CommandHandlerRegistry(IEnumerable<CommandHandlerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var copy = registrations.ToArray();
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Command handler registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.TypeId.Value, right.TypeId.Value));

        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].TypeId == copy[index].TypeId)
            {
                throw new ArgumentException(
                    $"{CommandExecutionDiagnosticCodes.DuplicateHandlerRegistration}: " +
                    $"Command type '{copy[index].TypeId}' has more than one execution handler.",
                    nameof(registrations));
            }
        }

        _registrations = [.. copy];
    }

    internal bool TryGet(
        CommandTypeId typeId,
        out CommandHandlerRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(typeId);

        var lower = 0;
        var upper = _registrations.Length - 1;

        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var candidate = _registrations[middle];
            var comparison = StringComparer.Ordinal.Compare(
                candidate.TypeId.Value,
                typeId.Value);

            if (comparison == 0)
            {
                registration = candidate;
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

        registration = null;
        return false;
    }
}
