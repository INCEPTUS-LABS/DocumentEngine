using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.Bpmn.Integration;

public enum BpmnPublishingInitialViewMode
{
    FitEntireDiagram,
    CurrentEditorView,
}

/// <summary>A document-coordinate view bound to the exact process revision being published.</summary>
public sealed record BpmnPublishingEditorView(
    DocumentId DocumentId, DocumentRevision Revision, DocumentScopeId ScopeId, RectD VisibleRegion);

/// <summary>Presentation choices for one standalone ZIP; never persisted in the document.</summary>
public sealed record BpmnPublishingOptions
{
    /// <summary>Includes visible Pool/Lane graphics and shared Participant headers from the captured Scene.</summary>
    public bool IncludePools { get; init; }

    /// <summary>Defaults to automatic fitting for compatibility with existing callers.</summary>
    public BpmnPublishingInitialViewMode InitialViewMode { get; init; }

    /// <summary>Required for CurrentEditorView; obtain it from the live editor, not the detached publishing Scene.</summary>
    public BpmnPublishingEditorView? EditorView { get; init; }
}
