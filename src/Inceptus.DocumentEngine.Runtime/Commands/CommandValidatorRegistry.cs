using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class CommandValidatorRegistry
{
    private readonly ImmutableArray<CommandValidatorRegistration> _registrations;

    internal CommandValidatorRegistry(ImmutableArray<CommandValidatorRegistration> registrations)
    {
        if (registrations.IsDefault)
        {
            throw new ArgumentException(
                "Command validator registrations must be initialized.",
                nameof(registrations));
        }

        _registrations = registrations;
    }

    internal ImmutableArray<CommandValidatorRegistration> Find(CommandTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);

        var builder = ImmutableArray.CreateBuilder<CommandValidatorRegistration>();

        foreach (var registration in _registrations)
        {
            var comparison = StringComparer.Ordinal.Compare(
                registration.TypeId.Value,
                typeId.Value);

            if (comparison < 0)
            {
                continue;
            }

            if (comparison > 0)
            {
                break;
            }

            builder.Add(registration);
        }

        return builder.ToImmutable();
    }

    internal bool Contains(CommandTypeId typeId) => !Find(typeId).IsEmpty;
}
