using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

/// <summary>
/// Immutable observation of one active Editing Session. A last-known-good scene is always stale.
/// </summary>
public sealed class EditingSessionState
{
    internal EditingSessionState(
        DocumentId documentId,
        DocumentRevision documentRevision,
        EditingSessionStatus status,
        EditingSessionGeneration generation,
        Canvas2DScene? currentScene,
        Canvas2DScene? lastKnownGoodScene,
        ProjectedGraph? projectedGraph,
        LayoutResult? layoutResult,
        RoutingResult? routingResult,
        EditorStateSnapshot editorState,
        HistoryStatus historyStatus,
        IEnumerable<Diagnostic>? runtimeDiagnostics,
        IEnumerable<Diagnostic>? presentationDiagnostics,
        bool isClosed,
        ModelProfileCatalog? modelProfileCatalog = null,
        ModelProfileStateSnapshot? modelProfileState = null,
        ModelProfileViewStateSnapshot? modelProfileViewState = null,
        ModelProfileElementViewStateSnapshot? modelProfileElementViewState = null)
        : this(
            documentId,
            documentRevision,
            new DocumentScopeId(documentId.Value),
            status,
            generation,
            currentScene,
            lastKnownGoodScene,
            projectedGraph,
            layoutResult,
            routingResult,
            editorState,
            historyStatus,
            runtimeDiagnostics,
            presentationDiagnostics,
            isClosed,
            modelProfileCatalog,
            modelProfileState,
            modelProfileViewState,
            modelProfileElementViewState)
    {
    }

    internal EditingSessionState(
        DocumentId documentId,
        DocumentRevision documentRevision,
        DocumentScopeId activeScopeId,
        EditingSessionStatus status,
        EditingSessionGeneration generation,
        Canvas2DScene? currentScene,
        Canvas2DScene? lastKnownGoodScene,
        ProjectedGraph? projectedGraph,
        LayoutResult? layoutResult,
        RoutingResult? routingResult,
        EditorStateSnapshot editorState,
        HistoryStatus historyStatus,
        IEnumerable<Diagnostic>? runtimeDiagnostics,
        IEnumerable<Diagnostic>? presentationDiagnostics,
        bool isClosed,
        ModelProfileCatalog? modelProfileCatalog = null,
        ModelProfileStateSnapshot? modelProfileState = null,
        ModelProfileViewStateSnapshot? modelProfileViewState = null,
        ModelProfileElementViewStateSnapshot? modelProfileElementViewState = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(editorState);
        ArgumentNullException.ThrowIfNull(historyStatus);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        var hasCompleteRuntimeTuple =
            currentScene is not null &&
            projectedGraph is not null &&
            layoutResult is not null &&
            routingResult is not null;
        if (!isClosed && status == EditingSessionStatus.Ready &&
            (!hasCompleteRuntimeTuple ||
             currentScene!.DocumentId != documentId ||
             currentScene.SourceRevision != documentRevision ||
             projectedGraph!.DocumentId != documentId ||
             projectedGraph.SourceRevision != documentRevision ||
             layoutResult!.DocumentId != documentId ||
             layoutResult.SourceRevision != documentRevision ||
             routingResult!.DocumentId != documentId ||
             routingResult.SourceRevision != documentRevision ||
             lastKnownGoodScene is not null))
        {
            throw new ArgumentException(
                "A Ready session requires one current scene for its current Document revision.",
                nameof(currentScene));
        }

        if (status != EditingSessionStatus.Ready &&
            (currentScene is not null || projectedGraph is not null ||
             layoutResult is not null || routingResult is not null))
        {
            throw new ArgumentException(
                "Only a Ready session may expose a current scene.",
                nameof(currentScene));
        }

        if (isClosed && (currentScene is not null || lastKnownGoodScene is not null ||
            projectedGraph is not null || layoutResult is not null || routingResult is not null))
        {
            throw new ArgumentException("A closed session cannot expose runtime results.", nameof(isClosed));
        }

        DocumentId = documentId;
        DocumentRevision = documentRevision;
        ActiveScopeId = activeScopeId;
        Status = status;
        Generation = generation;
        CurrentScene = currentScene;
        LastKnownGoodScene = lastKnownGoodScene;
        ProjectedGraph = projectedGraph;
        LayoutResult = layoutResult;
        RoutingResult = routingResult;
        EditorState = editorState;
        HistoryStatus = historyStatus;
        ModelProfileCatalog = modelProfileCatalog ??
            Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileCatalog.Empty;
        ModelProfileState = modelProfileState ?? ModelProfileStateSnapshot.Empty;
        ModelProfileViewState = modelProfileViewState ??
            ModelProfileViewStateSnapshot.Empty;
        ModelProfileElementViewState = modelProfileElementViewState ??
            ModelProfileElementViewStateSnapshot.Empty;
        RuntimeDiagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            runtimeDiagnostics,
            nameof(runtimeDiagnostics));
        PresentationDiagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            presentationDiagnostics,
            nameof(presentationDiagnostics));
        IsClosed = isClosed;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision DocumentRevision { get; }

    public DocumentScopeId ActiveScopeId { get; }

    public EditingSessionStatus Status { get; }

    public EditingSessionGeneration Generation { get; }

    public Canvas2DScene? CurrentScene { get; }

    public Canvas2DScene? LastKnownGoodScene { get; }

    public ProjectedGraph? ProjectedGraph { get; }

    public LayoutResult? LayoutResult { get; }

    public RoutingResult? RoutingResult { get; }

    public EditorStateSnapshot EditorState { get; }

    public HistoryStatus HistoryStatus { get; }

    public ModelProfileCatalog ModelProfileCatalog { get; }

    public ModelProfileStateSnapshot ModelProfileState { get; }

    public ModelProfileViewStateSnapshot ModelProfileViewState { get; }

    public ModelProfileElementViewStateSnapshot ModelProfileElementViewState { get; }

    public ImmutableArray<Diagnostic> RuntimeDiagnostics { get; }

    public ImmutableArray<Diagnostic> PresentationDiagnostics { get; }

    public bool IsClosed { get; }

    public bool IsGraphicalInteractionEnabled =>
        !IsClosed && Status == EditingSessionStatus.Ready && CurrentScene is not null;

    public bool IsDisplayingStaleScene =>
        !IsClosed &&
        Status is EditingSessionStatus.Rebuilding or EditingSessionStatus.RuntimeFaulted &&
        LastKnownGoodScene is not null;

    public bool IsModelProfileEffectivelyVisible(ModelProfileId profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ModelProfileViewState.IsEffectivelyVisible(profileId, ModelProfileState);
    }

    /// <summary>Gets the current active Process artifacts for interaction, never another scope.</summary>
    public bool TryGetCurrentProcessInteraction(out EditingSessionState? state)
    {
        state = IsGraphicalInteractionEnabled && ProjectedGraph is not null &&
            LayoutResult is not null && RoutingResult is not null ? this : null;
        return state is not null;
    }
}

public sealed class EditingSessionStateChangedEventArgs : EventArgs
{
    public EditingSessionStateChangedEventArgs(EditingSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    public EditingSessionState State { get; }
}
