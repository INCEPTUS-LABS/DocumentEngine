using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// Distinguishes a persistent edit, including Undo/Redo, from explicit document replacement.
/// </summary>
public enum BpmnModelerDocumentChangeKind
{
    PersistentMutation,
    DocumentReplacement,
}

/// <summary>
/// Identifies the high-level operation associated with a bounded failure.
/// </summary>
public enum BpmnModelerOperation
{
    Startup,
    Capture,
    New,
    Load,
    Import,
    Export,
    Publish,
}

/// <summary>
/// Reports the initially attached Document once the modeler's presentation is usable.
/// </summary>
public sealed class BpmnModelerReadyEventArgs : EventArgs
{
    public BpmnModelerReadyEventArgs(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    public DocumentSnapshot Snapshot { get; }
}

/// <summary>
/// Reports one accepted persistent revision or successful document replacement.
/// </summary>
public sealed class BpmnModelerDocumentChangedEventArgs : EventArgs
{
    public BpmnModelerDocumentChangedEventArgs(
        DocumentSnapshot snapshot,
        BpmnModelerDocumentChangeKind kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Snapshot = snapshot;
        Kind = kind;
    }

    public DocumentSnapshot Snapshot { get; }

    public BpmnModelerDocumentChangeKind Kind { get; }
}

/// <summary>
/// Reports an operation failure without exposing a live runtime or exception object.
/// Model validation Issues remain separate from these operation diagnostics.
/// </summary>
public sealed class BpmnModelerOperationFailedEventArgs : EventArgs
{
    public BpmnModelerOperationFailedEventArgs(
        BpmnModelerOperation operation,
        BpmnModelerOperationStatus status,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (!Enum.IsDefined(status) || status == BpmnModelerOperationStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Operation = operation;
        Status = status;
        Diagnostics = BpmnModelerResultDiagnostics.Copy(diagnostics);
    }

    public BpmnModelerOperation Operation { get; }

    public BpmnModelerOperationStatus Status { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
