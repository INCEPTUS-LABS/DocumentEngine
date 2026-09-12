using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// Describes the bounded outcome of a public modeler operation.
/// </summary>
public enum BpmnModelerOperationStatus
{
    Succeeded,
    Rejected,
    Unavailable,
    Failed,
    Cancelled,
}

/// <summary>
/// Returns an immutable snapshot after capture or a completed document replacement.
/// </summary>
public sealed class BpmnModelerDocumentResult
{
    public BpmnModelerDocumentResult(
        BpmnModelerOperationStatus status,
        DocumentSnapshot? snapshot,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if ((status == BpmnModelerOperationStatus.Succeeded) != (snapshot is not null))
        {
            throw new ArgumentException(
                "Only a successful modeler operation can return a Document snapshot.",
                nameof(snapshot));
        }

        Status = status;
        Snapshot = snapshot;
        Diagnostics = BpmnModelerResultDiagnostics.Copy(diagnostics);
    }

    public BpmnModelerOperationStatus Status { get; }

    public bool Succeeded => Status == BpmnModelerOperationStatus.Succeeded;

    public DocumentSnapshot? Snapshot { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

/// <summary>
/// Contains generated file bytes without requiring a browser download or host storage.
/// </summary>
public sealed class BpmnModelerFileArtifact
{
    public BpmnModelerFileArtifact(
        string fileName,
        string contentType,
        IEnumerable<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);

        FileName = fileName;
        ContentType = contentType;
        Content = content.ToImmutableArray();
    }

    public string FileName { get; }

    public string ContentType { get; }

    public ImmutableArray<byte> Content { get; }
}

/// <summary>
/// Returns generated native Document or Publish content, or bounded failure diagnostics.
/// </summary>
public sealed class BpmnModelerFileResult
{
    public BpmnModelerFileResult(
        BpmnModelerOperationStatus status,
        BpmnModelerFileArtifact? artifact,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if ((status == BpmnModelerOperationStatus.Succeeded) != (artifact is not null))
        {
            throw new ArgumentException(
                "Only a successful modeler operation can return a file artifact.",
                nameof(artifact));
        }

        Status = status;
        Artifact = artifact;
        Diagnostics = BpmnModelerResultDiagnostics.Copy(diagnostics);
    }

    public BpmnModelerOperationStatus Status { get; }

    public bool Succeeded => Status == BpmnModelerOperationStatus.Succeeded;

    public BpmnModelerFileArtifact? Artifact { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

internal static class BpmnModelerResultDiagnostics
{
    internal static ImmutableArray<Diagnostic> Copy(ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return [];
        }

        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Modeler diagnostics cannot contain null entries.",
                nameof(diagnostics));
        }

        return diagnostics;
    }
}
