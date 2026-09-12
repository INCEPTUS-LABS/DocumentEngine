using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// An ordered, bounded set of ordinary Commands proposed and committed as one Document edit.
/// Children share the outer base revision; none is independently committed or recorded.
/// </summary>
public sealed class CompoundDocumentCommand : ICommand
{
    public static CommandTypeId KnownTypeId { get; } = new("inceptus:command/compound-document-edit");

    public CompoundDocumentCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        IEnumerable<ICommand> commands)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(commands);
        var copy = commands.ToImmutableArray();
        if (copy.Length is < 2 or > 32 || copy.Any(command => command is null ||
                command is CompoundDocumentCommand || command.TargetDocumentId != targetDocumentId ||
                command.ExpectedRevision != expectedRevision))
        {
            throw new ArgumentException(
                "A compound edit requires 2–32 non-nested Commands for the same Document and revision.",
                nameof(commands));
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        Commands = copy;
        AffectedComponents = copy.Aggregate(AuthoritativeDocumentComponent.None,
            static (components, command) => components | command.AffectedComponents);
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public CommandCategory Category => CommandCategory.Compound;
    public AuthoritativeDocumentComponent AffectedComponents { get; }
    public ImmutableArray<ICommand> Commands { get; }
}
