using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal interface ISessionPipelineProcessing
{
    ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunFullAsync(document, activeScopeId, editorState, cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunFullAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken) =>
        RunFullAsync(
            document,
            activeScopeId,
            editorState,
            modelProfileViewState,
            cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeLayoutHistory,
            nodeGeometryImpact,
            editorState,
            cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
        NodeGeometryPipelineImpact nodeGeometryImpact,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken) =>
        RunPreservingNodeLayoutAsync(
            document,
            activeScopeId,
            previousArtifacts,
            nodeLayoutHistory,
            nodeGeometryImpact,
            editorState,
            modelProfileViewState,
            cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken) =>
        RunFullAsync(document, activeScopeId, editorState, cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RunPreservingScopeLayoutIfCompatibleAsync(
            document,
            activeScopeId,
            previousArtifacts,
            editorState,
            cancellationToken);

    ValueTask<EditingSessionPipelineResult> RunPreservingScopeLayoutIfCompatibleAsync(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        EditingSessionPipelineArtifacts previousArtifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken) =>
        RunPreservingScopeLayoutIfCompatibleAsync(
            document,
            activeScopeId,
            previousArtifacts,
            editorState,
            modelProfileViewState,
            cancellationToken);

    ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        EditingSessionPipelineArtifacts artifacts,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken);

    ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        DocumentSnapshot document,
        EditingSessionPipelineArtifacts artifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken) =>
        RebuildSceneAsync(
            artifacts,
            document.VisualModel,
            editorState,
            cancellationToken);

    ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
        DocumentSnapshot document,
        EditingSessionPipelineArtifacts artifacts,
        EditorStateSnapshot editorState,
        ModelProfileViewStateSnapshot modelProfileViewState,
        ModelProfileElementViewStateSnapshot modelProfileElementViewState,
        CancellationToken cancellationToken) =>
        RebuildSceneAsync(
            document,
            artifacts,
            editorState,
            modelProfileViewState,
            cancellationToken);
}
