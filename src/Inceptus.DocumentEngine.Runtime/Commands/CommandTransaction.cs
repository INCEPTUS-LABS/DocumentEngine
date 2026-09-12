using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Holds one isolated command attempt. Its proposed state is never observable through
/// the runtime Document until the processor performs the single compare-exchange commit.
/// </summary>
internal sealed class CommandTransaction
{
    internal CommandTransaction(
        DocumentState baseState,
        AuthoritativeDocumentComponent declaredAffectedComponents)
    {
        ArgumentNullException.ThrowIfNull(baseState);

        BaseState = baseState;
        BaseSnapshot = baseState.Snapshot;
        DocumentId = BaseSnapshot.DocumentId;
        BaseRevision = BaseSnapshot.Revision;
        DeclaredAffectedComponents = declaredAffectedComponents;
        Status = CommandTransactionStatus.Created;
    }

    internal DocumentId DocumentId { get; }

    internal DocumentRevision BaseRevision { get; }

    internal DocumentState BaseState { get; }

    internal DocumentSnapshot BaseSnapshot { get; }

    internal DocumentSnapshot? ProposedSnapshot { get; private set; }

    internal AuthoritativeDocumentComponent DeclaredAffectedComponents { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; private set; } = [];

    internal CommandTransactionStatus Status { get; private set; }

    internal void AddDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var copy = diagnostics.ToImmutableArray();
        if (copy.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Transaction diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        Diagnostics = Diagnostics.AddRange(copy);
    }

    internal void MarkDelegatedValidationComplete() =>
        Status = CommandTransactionStatus.Validated;

    internal void SetProposedSnapshot(DocumentSnapshot proposedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(proposedSnapshot);

        if (Status != CommandTransactionStatus.Validated)
        {
            throw new InvalidOperationException(
                "A proposed state can be supplied only after delegated validation succeeds.");
        }

        ProposedSnapshot = proposedSnapshot;
        Status = CommandTransactionStatus.Executed;
    }

    internal void MarkPrepared() => Status = CommandTransactionStatus.Prepared;

    internal void MarkCommitted() => Status = CommandTransactionStatus.Committed;

    internal void MarkAborted() => Status = CommandTransactionStatus.Aborted;
}

internal enum CommandTransactionStatus
{
    Created,
    Validated,
    Executed,
    Prepared,
    Committed,
    Aborted,
}
