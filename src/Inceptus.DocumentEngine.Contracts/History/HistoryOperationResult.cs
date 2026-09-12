using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Immutable result of a History-aware normal, Undo, or Redo operation. It exposes
/// no History entry, cursor, Document snapshot, or mutable Runtime object.
/// </summary>
public sealed class HistoryOperationResult : IEquatable<HistoryOperationResult>
{
    private HistoryOperationResult(
        DocumentId documentId,
        CommandTypeId? commandTypeId,
        HistoryOperationStatus status,
        DocumentRevision previousRevision,
        DocumentRevision? committedRevision,
        HistoryStatus historyStatus,
        IEnumerable<Diagnostic>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(historyStatus);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        if (status == HistoryOperationStatus.Committed)
        {
            if (commandTypeId is null ||
                committedRevision is null ||
                committedRevision.Value != previousRevision.Increment())
            {
                throw new ArgumentException(
                    "A committed History operation requires a Command type and exactly one next revision.",
                    nameof(committedRevision));
            }
        }
        else if (status == HistoryOperationStatus.Applied && commandTypeId is not null)
        {
            throw new ArgumentException(
                "A runtime-applied History operation cannot expose a persistent Command type.",
                nameof(commandTypeId));
        }
        else if (committedRevision is not null)
        {
            throw new ArgumentException(
                "A non-committed History operation cannot expose a committed revision.",
                nameof(committedRevision));
        }

        DocumentId = documentId;
        CommandTypeId = commandTypeId;
        Status = status;
        PreviousRevision = previousRevision;
        CommittedRevision = committedRevision;
        HistoryStatus = historyStatus;
        Diagnostics = DiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
    }

    public DocumentId DocumentId { get; }

    public CommandTypeId? CommandTypeId { get; }

    public HistoryOperationStatus Status { get; }

    public DocumentRevision PreviousRevision { get; }

    public DocumentRevision? CommittedRevision { get; }

    public HistoryStatus HistoryStatus { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool IsCommitted => Status == HistoryOperationStatus.Committed;

    public bool IsApplied => Status == HistoryOperationStatus.Applied;

    public bool Succeeded => IsCommitted || IsApplied;

    public static HistoryOperationResult CreateCommitted(
        DocumentId documentId,
        CommandTypeId commandTypeId,
        DocumentRevision previousRevision,
        DocumentRevision committedRevision,
        HistoryStatus historyStatus,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            documentId,
            commandTypeId,
            HistoryOperationStatus.Committed,
            previousRevision,
            committedRevision,
            historyStatus,
            diagnostics);

    public static HistoryOperationResult CreateNotCommitted(
        DocumentId documentId,
        CommandTypeId? commandTypeId,
        HistoryOperationStatus status,
        DocumentRevision currentRevision,
        HistoryStatus historyStatus,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        if (status is HistoryOperationStatus.Committed or HistoryOperationStatus.Applied)
        {
            throw new ArgumentException(
                "A non-applied result cannot use a successful status.",
                nameof(status));
        }

        return new(
            documentId,
            commandTypeId,
            status,
            currentRevision,
            null,
            historyStatus,
            diagnostics);
    }

    public static HistoryOperationResult CreateApplied(
        DocumentId documentId,
        DocumentRevision currentRevision,
        HistoryStatus historyStatus,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            documentId,
            commandTypeId: null,
            HistoryOperationStatus.Applied,
            currentRevision,
            committedRevision: null,
            historyStatus,
            diagnostics);

    public bool Equals(HistoryOperationResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        CommandTypeId == other.CommandTypeId &&
        Status == other.Status &&
        PreviousRevision == other.PreviousRevision &&
        CommittedRevision == other.CommittedRevision &&
        HistoryStatus.Equals(other.HistoryStatus) &&
        DiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as HistoryOperationResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(CommandTypeId);
        hash.Add(Status);
        hash.Add(PreviousRevision);
        hash.Add(CommittedRevision);
        hash.Add(HistoryStatus);
        DiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
