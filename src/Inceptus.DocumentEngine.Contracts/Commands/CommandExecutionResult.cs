using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed class CommandExecutionResult : IEquatable<CommandExecutionResult>
{
    private CommandExecutionResult(
        DocumentId documentId,
        CommandTypeId commandTypeId,
        CommandExecutionStatus status,
        DocumentRevision previousRevision,
        AuthoritativeDocumentComponent affectedComponents,
        DocumentChangedEvent? committedEvent,
        IEnumerable<Diagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(commandTypeId);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        DocumentId = documentId;
        CommandTypeId = commandTypeId;
        Status = status;
        PreviousRevision = previousRevision;
        AffectedComponents = affectedComponents;
        CommittedEvent = committedEvent;
        Diagnostics = DiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
    }

    public DocumentId DocumentId { get; }

    public CommandTypeId CommandTypeId { get; }

    public CommandExecutionStatus Status { get; }

    public DocumentRevision PreviousRevision { get; }

    public DocumentRevision? CommittedRevision => CommittedEvent?.CommittedRevision;

    public AuthoritativeDocumentComponent AffectedComponents { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public DocumentChangedEvent? CommittedEvent { get; }

    public DocumentSnapshot? CommittedSnapshot => CommittedEvent?.CommittedSnapshot;

    public bool IsCommitted => Status == CommandExecutionStatus.Committed;

    public static CommandExecutionResult CreateCommitted(
        DocumentChangedEvent committedEvent,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(committedEvent);

        return new CommandExecutionResult(
            committedEvent.DocumentId,
            committedEvent.CommandTypeId,
            CommandExecutionStatus.Committed,
            committedEvent.PreviousRevision,
            committedEvent.AffectedComponents,
            committedEvent,
            diagnostics);
    }

    public static CommandExecutionResult CreateFailure(
        DocumentId documentId,
        CommandTypeId commandTypeId,
        CommandExecutionStatus status,
        DocumentRevision previousRevision,
        AuthoritativeDocumentComponent affectedComponents,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        if (status == CommandExecutionStatus.Committed)
        {
            throw new ArgumentException(
                "A failure result cannot use the committed status.",
                nameof(status));
        }

        return new CommandExecutionResult(
            documentId,
            commandTypeId,
            status,
            previousRevision,
            affectedComponents,
            null,
            diagnostics);
    }

    public bool Equals(CommandExecutionResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        CommandTypeId == other.CommandTypeId &&
        Status == other.Status &&
        PreviousRevision == other.PreviousRevision &&
        AffectedComponents == other.AffectedComponents &&
        Equals(CommittedEvent, other.CommittedEvent) &&
        DiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as CommandExecutionResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(CommandTypeId);
        hash.Add(Status);
        hash.Add(PreviousRevision);
        hash.Add(AffectedComponents);
        hash.Add(CommittedEvent);
        DiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }

    public static bool operator ==(CommandExecutionResult? left, CommandExecutionResult? right) =>
        EqualityComparer<CommandExecutionResult>.Default.Equals(left, right);

    public static bool operator !=(CommandExecutionResult? left, CommandExecutionResult? right) =>
        !(left == right);
}
