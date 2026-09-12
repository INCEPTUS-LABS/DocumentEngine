using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed class CommandHandlerRegistration
{
    public CommandHandlerRegistration(
        CommandTypeId typeId,
        ICommandEnvelopeValidator envelopeValidator,
        ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(envelopeValidator);
        ArgumentNullException.ThrowIfNull(handler);

        TypeId = typeId;
        EnvelopeValidator = envelopeValidator;
        Handler = handler;
    }

    public CommandTypeId TypeId { get; }

    public ICommandEnvelopeValidator EnvelopeValidator { get; }

    public ICommandHandler Handler { get; }
}
